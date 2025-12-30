// Переписано
using FATX.FileSystem;
using System;
using System.Collections.Generic;
using System.Diagnostics; // 1. Подключаем Trace
using System.Linq;

namespace FATXTools.Database
{
    public class FileDatabase
    {
        /// <summary>
        /// Map of files according to its offset
        /// </summary>
        Dictionary<long, DatabaseFile> files;

        /// <summary>
        /// List of files at the root of file system.
        /// </summary>
        List<DatabaseFile> root;

        /// <summary>
        /// Volume associated with this database.
        /// </summary>
        Volume volume;

        public FileDatabase(Volume volume)
        {
            this.files = new Dictionary<long, DatabaseFile>();
            this.root = new List<DatabaseFile>();
            this.volume = volume;

            try
            {
                MergeActiveFileSystem(volume);
                Trace.WriteLine($"[FileDatabase] База данных инициализирована. Загружено записей: {files.Count}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileDatabase] Критическая ошибка при инициализации базы данных: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the number of files in this database.
        /// </summary>
        public int Count()
        {
            return files.Count;
        }

        /// <summary>
        /// Update the file system in this database. This should be called after 
        /// any modifications are made to the database.
        /// </summary>
        public void Update()
        {
            // TODO: Only update affected files

            try
            {
                // Construct a new file system.
                root = new List<DatabaseFile>();

                // Link file system together.
                LinkFileSystem();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileDatabase] Ошибка обновления структуры файловой системы: {ex.Message}");
            }
        }

        public void Reset()
        {
            try
            {
                this.files = new Dictionary<long, DatabaseFile>();
                MergeActiveFileSystem(this.volume);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileDatabase] Ошибка сброса (Reset) базы данных: {ex.Message}");
            }
        }

        private void FindChildren(DatabaseFile parent)
        {
            try
            {
                if (parent.ClusterChain == null) return;

                var chainMap = parent.ClusterChain;

                // Используем ToList() для копирования, если нужно безопасное удаление, но здесь только чтение
                foreach (var child in files.Values)
                {
                    try
                    {
                        // Правило 1: Проверка на null перед доступом к Cluster
                        if (child.Cluster == 0 || !chainMap.Contains(child.Cluster))
                        {
                            continue;
                        }

                        // Add file as a child of the parent.
                        if (!parent.Children.Contains(child))
                            parent.Children.Add(child);

                        // Assigns the parent file for this file.
                        // Если у файла уже есть родитель, мы меняем его (логика FATX может быть сложной)
                        if (child.GetParent() != parent)
                            child.SetParent(parent);
                    }
                    catch (Exception ex)
                    {
                        // Правило 1: Не даем одному "битому" ребенку сломать все родительские связи
                        Trace.WriteLine($"[FileDatabase] Ошибка связывания ребенка с родителем {parent.FileName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileDatabase] Ошибка поиска детей для файла {parent.FileName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Build the file system.
        /// </summary>
        private void LinkFileSystem()
        {
            // Clear all previous links
            foreach (var file in files.Values)
            {
                try
                {
                    file.Children = new List<DatabaseFile>();
                    file.SetParent(null);
                }
                catch { /* Игнорируем ошибки сброса */ }
            }

            // Link all of the files together
            foreach (var file in files.Values)
            {
                try
                {
                    if (file.IsDirectory())
                    {
                        FindChildren(file);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[FileDatabase] Ошибка линковки директории {file.FileName}: {ex.Message}");
                }
            }

            // Gather files at the root
            foreach (var file in files.Values)
            {
                try
                {
                    if (!file.HasParent())
                    {
                        root.Add(file);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[FileDatabase] Ошибка проверки родителя для {file.FileName}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Merge the active file system into this database.
        /// </summary>
        /// <param name="volume">The active file system.</param>
        private void MergeActiveFileSystem(Volume volume)
        {
            try
            {
                var rootEntries = volume.GetRoot();
                if (rootEntries != null)
                {
                    RegisterDirectoryEntries(rootEntries);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileDatabase] Критическая ошибка при чтении активной файловой системы: {ex.Message}");
            }
        }

        /// <summary>
        /// Register directory entries in bulk.
        /// </summary>
        /// <param name="dirents"></param>
        private void RegisterDirectoryEntries(List<DirectoryEntry> dirents)
        {
            if (dirents == null) return;

            foreach (var dirent in dirents)
            {
                try
                {
                    if (dirent.IsDeleted())
                    {
                        AddFile(dirent, true);
                    }
                    else
                    {
                        AddFile(dirent, false);

                        if (dirent.IsDirectory())
                        {
                            // Рекурсивный вызов. Если внутри произойдет ошибка, она будет поймана внутри следующего вызова RegisterDirectoryEntries
                            // или при доступе к Children.
                            var children = dirent.Children;
                            if (children != null)
                            {
                                RegisterDirectoryEntries(children);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Правило 1: Если одна запись битая, пропускаем её, но не прерываем сканирование директории
                    Trace.WriteLine($"[FileDatabase] Ошибка регистрации записи (dirent) по адресу 0x{dirent.Offset:X}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Generates a new cluster chain for a deleted file.
        /// </summary>
        /// <param name="dirent">The deleted file.</param>
        /// <returns></returns>
        private List<uint> GenerateArtificialClusterChain(DirectoryEntry dirent)
        {
            try
            {
                // Правило 1: Защита от деления на ноль
                if (this.volume.BytesPerCluster == 0)
                {
                    Trace.WriteLine("[FileDatabase] BytesPerCluster равно 0. Невозможно сгенерировать цепочку.");
                    return new List<uint>();
                }

                // NOTE: Directories with more than one 256 files would have multiple clusters
                if (dirent.IsDirectory())
                {
                    return new List<uint>() { dirent.FirstCluster };
                }
                else
                {
                    // Правило 1: Защита от огромных размеров, которые вызовут Overflow в Enumerable.Range
                    // Используем long для промежуточных вычислений
                    long clusterCountLong = (((long)dirent.FileSize + (this.volume.BytesPerCluster - 1)) &
                                     ~(this.volume.BytesPerCluster - 1)) / this.volume.BytesPerCluster;

                    if (clusterCountLong > int.MaxValue)
                    {
                        Trace.WriteLine($"[FileDatabase] Размер файла {dirent.FileName} слишком велик для искусственной цепочки.");
                        clusterCountLong = int.MaxValue;
                    }

                    int clusterCount = (int)clusterCountLong;

                    if (clusterCount <= 0) return new List<uint>();

                    // Защита от генерации огромного списка (например, если FirstCluster = 0xFFFFFFFF)
                    if (dirent.FirstCluster > uint.MaxValue - (uint)clusterCount)
                    {
                        Trace.WriteLine($"[FileDatabase] Arithmetic overflow for {dirent.FileName} cluster chain generation.");
                        return new List<uint> { dirent.FirstCluster };
                    }

                    return Enumerable.Range((int)dirent.FirstCluster, clusterCount).Select(i => (uint)i).ToList();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileDatabase] Ошибка генерации искусственной цепочки для {dirent.FileName}: {ex.Message}");
                return new List<uint>();
            }
        }

        /// <summary>
        /// Get a file by offset into the file system.
        /// </summary>
        /// <param name="offset">File area offset</param>
        /// <returns>DatabaseFile from this database</returns>
        public DatabaseFile GetFile(long offset)
        {
            if (files.ContainsKey(offset))
            {
                return files[offset];
            }

            return null;
        }

        /// <summary>
        /// Get a file by an instance of a DirectoryEntry.
        /// </summary>
        /// <param name="dirent">DirectoryEntry instance</param>
        /// <returns>DatabaseFile from this database</returns>
        public DatabaseFile GetFile(DirectoryEntry dirent)
        {
            try
            {
                if (dirent == null) return null;
                if (files.ContainsKey(dirent.Offset))
                {
                    return files[dirent.Offset];
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileDatabase] Ошибка получения файла по DirectoryEntry: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Create and initialize a new DatabaseFile for this DirectoryEntry.
        /// </summary>
        private DatabaseFile CreateDatabaseFile(DirectoryEntry dirent, bool deleted)
        {
            try
            {
                // Сначала создаем запись
                files[dirent.Offset] = new DatabaseFile(dirent, deleted);

                // Затем пытаемся вычислить цепочку
                if (deleted)
                {
                    files[dirent.Offset].ClusterChain = GenerateArtificialClusterChain(dirent);
                }
                else
                {
                    // Правило 1: Защита от падения при ошибке FAT
                    files[dirent.Offset].ClusterChain = this.volume.GetClusterChain(dirent);
                }

                return files[dirent.Offset];
            }
            catch (Exception ex)
            {
                // Правило 1: Если GetClusterChain упала, мы все равно хотим сохранить файл, но с пустой цепочкой
                Trace.WriteLine($"[FileDatabase] Ошибка инициализации цепочки для {dirent.FileName} (используем пустую цепочку): {ex.Message}");

                files[dirent.Offset].ClusterChain = new List<uint>();
                return files[dirent.Offset];
            }
        }

        /// <summary>
        /// Add and initialize a new DatabaseFile.
        /// </summary>
        /// <param name="dirent">The associated DirectoryEntry</param>
        /// <param name="deleted">Whether or not this file was deleted</param>
        public DatabaseFile AddFile(DirectoryEntry dirent, bool deleted)
        {
            try
            {
                // Create the file if it was not already added
                if (!files.ContainsKey(dirent.Offset))
                {
                    return CreateDatabaseFile(dirent, deleted);
                }

                return files[dirent.Offset];
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileDatabase] Ошибка добавления файла в базу: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get all files from this database.
        /// </summary>
        public Dictionary<long, DatabaseFile> GetFiles()
        {
            return files;
        }

        /// <summary>
        /// Get root files from this database's file system.
        /// </summary>
        public List<DatabaseFile> GetRootFiles()
        {
            return root;
        }

        /// <summary>
        /// Get volume associated with this database.
        /// </summary>
        public Volume GetVolume()
        {
            return volume;
        }
    }
}