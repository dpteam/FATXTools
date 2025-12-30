// Переписано
using FATX.FileSystem;
using System;
using System.Collections.Generic;
using System.Diagnostics; // 1. Подключаем Trace

namespace FATXTools.Database
{
    public class DatabaseFile
    {
        /// <summary>
        /// Whether or not this file has been deleted.
        /// </summary>
        private bool deleted;

        /// <summary>
        /// The status ranking given to this file.
        /// </summary>
        private int ranking;

        /// <summary>
        /// The DirectoryEntry this DatabaseFile represents.
        /// </summary>
        private DirectoryEntry dirent;

        /// <summary>
        /// List of files that this file collides with.
        /// </summary>
        private List<uint> collisions;

        /// <summary>
        /// This file's cluster chain.
        /// </summary>
        private List<uint> clusterChain;

        /// <summary>
        /// This file's parent.
        /// </summary>
        private DatabaseFile parent;

        public DatabaseFile(DirectoryEntry dirent, bool deleted)
        {
            this.deleted = deleted;
            this.dirent = dirent;
            this.clusterChain = null;
            this.parent = null;
            this.Children = new List<DatabaseFile>();

            // Правило 3: Улучшенное логирование
            if (this.dirent == null)
            {
                Trace.WriteLine("[DatabaseFile] ВНИМАНИЕ: Создан DatabaseFile с null DirectoryEntry!");
            }
        }

        /// <summary>
        /// Counts and returns the number of files contained within this file.
        /// </summary>
        /// <returns>Number of files in this file</returns>
        public long CountFiles()
        {
            try
            {
                // Правило 1: Проверка на null и удаленный статус
                if (this.dirent == null || this.dirent.IsDeleted())
                {
                    return 0;
                }

                if (IsDirectory())
                {
                    long numFiles = 1;

                    foreach (var child in Children)
                    {
                        try
                        {
                            numFiles += child.CountFiles();
                        }
                        catch (Exception ex)
                        {
                            // Правило 1: Продолжаем подсчет даже если одна ветка битая
                            Trace.WriteLine($"[DatabaseFile] Ошибка подсчета файлов в директории '{this.FileName}': {ex.Message}");
                        }
                    }

                    return numFiles;
                }
                else
                {
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DatabaseFile] Критическая ошибка при подсчете файлов: {ex.Message}");
                return 0;
            }
        }

        public int GetRanking()
        {
            return ranking;
        }

        public void SetRanking(int value)
        {
            this.ranking = value;
        }

        public List<uint> GetCollisions()
        {
            return collisions;
        }

        public void SetCollisions(List<uint> value)
        {
            collisions = value;
        }

        public DirectoryEntry GetDirent()
        {
            return dirent;
        }

        public void SetParent(DatabaseFile parent)
        {
            this.parent = parent;
        }

        public DatabaseFile GetParent()
        {
            return this.parent;
        }

        public bool HasParent()
        {
            return this.parent != null;
        }

        public bool IsDirectory()
        {
            // Правило 1: Защита от NullReference
            if (dirent == null) return false;
            return dirent.IsDirectory();
        }

        // TODO: Rename to IsRecovered? This conflicts with DirectoryEntry::IsDeleted()
        public bool IsDeleted => deleted;

        public List<DatabaseFile> Children
        {
            get;
            set;
        }

        public List<uint> ClusterChain
        {
            get => clusterChain;
            set => clusterChain = value;
        }

        // Правило 1: Защита всех свойств, обращающихся к dirent
        // Используем null-условный оператор (?.) чтобы избежать исключений

        public uint Cluster => dirent?.Cluster ?? 0;

        public long Offset => dirent?.Offset ?? 0;

        public uint FileNameLength => dirent?.FileNameLength ?? 0;

        public FileAttribute FileAttributes => dirent != null ? dirent.FileAttributes : 0;

        public string FileName => dirent?.FileName ?? "(Error: Null)";

        public byte[] FileNameBytes => dirent != null ? dirent.FileNameBytes : new byte[0];

        public uint FirstCluster => dirent?.FirstCluster ?? 0;

        public uint FileSize => dirent?.FileSize ?? 0;

        public TimeStamp CreationTime => dirent?.CreationTime;

        public TimeStamp LastWriteTime => dirent?.LastWriteTime;

        public TimeStamp LastAccessTime => dirent?.LastAccessTime;
    }
}