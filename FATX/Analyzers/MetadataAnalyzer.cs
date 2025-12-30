// Переписано
using FATX.FileSystem;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace FATX.Analyzers
{
    public class MetadataAnalyzer
    {
        private Volume _volume;
        private long _interval;
        private long _length;

        private int _currentYear;

        private List<DirectoryEntry> _dirents = new List<DirectoryEntry>();

        private const string VALID_CHARS = "abcdefghijklmnopqrstuvwxyz" +
                                           "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
                                           "0123456789" +
                                           "!#$%&\'()-.@[]^_`{}~ " +
                                           "\xff";

        public MetadataAnalyzer(Volume volume, long interval, long length)
        {
            if (length == 0 || length > volume.FileAreaLength)
            {
                length = volume.FileAreaLength;
            }

            _volume = volume;
            _interval = interval;
            _length = length;

            _currentYear = DateTime.Now.Year;
        }

        public List<DirectoryEntry> Analyze(CancellationToken cancellationToken, IProgress<int> progress)
        {
            var sw = new Stopwatch();
            sw.Start();

            try
            {
                RecoverMetadata(cancellationToken, progress);
            }
            catch (Exception ex)
            {
                // Правило 1: Ловим глобальные ошибки, чтобы анализ не упал молча
                Trace.WriteLine($"[MetadataAnalyzer] Критическая ошибка в процессе анализа: {ex.Message}");
            }

            sw.Stop();

            // Правило 2 и 3: Trace и улучшенное логирование
            Trace.WriteLine($"[MetadataAnalyzer] Анализ завершен. Время выполнения: {sw.ElapsedMilliseconds} мс");
            Trace.WriteLine($"[MetadataAnalyzer] Найдено записей (dirents): {_dirents.Count}");

            return _dirents;
        }

        /// <summary>
        /// Searches for dirent's.
        /// </summary>
        private void RecoverMetadata(CancellationToken cancellationToken, IProgress<int> progress)
        {
            var maxClusters = _length / _interval;

            // Правило 3: Логируем начало сканирования
            Trace.WriteLine($"[MetadataAnalyzer] Начало сканирования кластеров (всего: {maxClusters})...");

            for (uint cluster = 1; cluster < maxClusters; cluster++)
            {
                byte[] data = null;
                try
                {
                    data = _volume.ReadCluster(cluster);
                }
                catch (Exception exception) // Правило 1: Ловим любые ошибки чтения, не только IOException
                {
                    // Правило 2 и 3: Детальный лог ошибки
                    Trace.WriteLine($"[MetadataAnalyzer] Ошибка чтения кластера {cluster}: {exception.Message}. Пропуск...");
                    continue;
                }

                var clusterOffset = (cluster - 1) * _interval;

                for (int i = 0; i < 256; i++)
                {
                    var direntOffset = i * 0x40;
                    try
                    {
                        DirectoryEntry dirent = new DirectoryEntry(_volume.Platform, data, direntOffset);

                        if (IsValidDirent(dirent))
                        {
                            // Правило 2 и 3: Логируем найденный валидный элемент
                            Trace.WriteLine($"[MetadataAnalyzer] 0x{clusterOffset + direntOffset:X8}: {dirent.FileName}");
                            dirent.Cluster = cluster;
                            dirent.Offset = _volume.ClusterToPhysicalOffset(cluster) + direntOffset;
                            _dirents.Add(dirent);
                        }
                    }
                    catch (Exception e)
                    {
                        // Правило 1 и 3: Логируем ошибку парсинга конкретной записи, но продолжаем цикл
                        Trace.WriteLine($"[MetadataAnalyzer] Ошибка парсинга dirent в кластере {cluster}, смещение 0x{direntOffset:X}: {e.Message}");
                        // StackTrace можно раскомментировать для глубокой отладки, если нужно
                        // Trace.WriteLine(e.StackTrace);
                    }
                }

                if (cluster % 0x100 == 0)
                    progress?.Report((int)cluster);

                if (cancellationToken.IsCancellationRequested)
                {
                    Trace.WriteLine("[MetadataAnalyzer] Сканирование отменено пользователем.");
                    break;
                }
            }

            progress?.Report((int)maxClusters);
        }

        /// <summary>
        /// Dump a directory to path.
        /// </summary>
        private void DumpDirectory(DirectoryEntry dirent, string path)
        {
            try
            {
                path = Path.Combine(path, dirent.FileName);

                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                foreach (DirectoryEntry child in dirent.Children)
                {
                    Dump(child, path);
                }

                // Правило 1: Безопасная установка атрибутов времени
                try
                {
                    Directory.SetCreationTime(path, dirent.CreationTime.AsDateTime());
                    Directory.SetLastWriteTime(path, dirent.LastWriteTime.AsDateTime());
                    Directory.SetLastAccessTime(path, dirent.LastAccessTime.AsDateTime());
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[MetadataAnalyzer] Не удалось установить метаданные времени для папки {path}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MetadataAnalyzer] Критическая ошибка при создании папки {dirent.FileName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Dump a file to path.
        /// </summary>
        private void DumpFile(DirectoryEntry dirent, string path)
        {
            try
            {
                path = Path.Combine(path, dirent.FileName);
                const int bufsize = 0x100000;
                var remains = dirent.FileSize;

                _volume.SeekToCluster(dirent.FirstCluster);

                using (FileStream file = new FileStream(path, FileMode.Create))
                {
                    while (remains > 0)
                    {
                        var read = Math.Min(remains, bufsize);
                        remains -= read;
                        byte[] buf = new byte[read];

                        // Метод Read возвращает void, просто вызываем его.
                        // Если возникнет ошибка чтения, она будет перехвачена внешним try-catch.
                        _volume.GetReader().Read(buf, (int)read);

                        file.Write(buf, 0, (int)read);
                    }
                }

                // Правило 1: Безопасная установка атрибутов времени
                try
                {
                    File.SetCreationTime(path, dirent.CreationTime.AsDateTime());
                    File.SetLastWriteTime(path, dirent.LastWriteTime.AsDateTime());
                    File.SetLastAccessTime(path, dirent.LastAccessTime.AsDateTime());
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[MetadataAnalyzer] Не удалось установить метаданные времени для файла {path}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                // Правило 1: Если файл не сбросился на диск, логируем и идем дальше
                Trace.WriteLine($"[MetadataAnalyzer] Ошибка при сбросе файла {dirent.FileName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Dumps a DirectoryEntry to path.
        /// </summary>
        public void Dump(DirectoryEntry dirent, string path)
        {
            if (dirent.IsDirectory())
            {
                DumpDirectory(dirent, path);
            }
            else
            {
                DumpFile(dirent, path);
            }
        }

        public List<DirectoryEntry> GetDirents()
        {
            return _dirents;
        }

        public Volume GetVolume()
        {
            return _volume;
        }

        /// <summary>
        /// Validate FileNameBytes.
        /// </summary>
        private bool IsValidFileNameBytes(byte[] bytes)
        {
            foreach (byte b in bytes)
            {
                if (VALID_CHARS.IndexOf((char)b) == -1)
                {
                    return false;
                }
            }

            return true;
        }

        private const int ValidAttributes = 55;

        /// <summary>
        /// Validate FileAttributes.
        /// </summary>
        private bool IsValidAttributes(FileAttribute attributes)
        {
            if (attributes == 0)
            {
                return true;
            }

            if (!Enum.IsDefined(typeof(FileAttribute), attributes))
            {
                return false;
            }

            return true;
        }

        private int[] MaxDays =
        {
              31, // Jan
              29, // Feb
              31, // Mar
              30, // Apr
              31, // May
              30, // Jun
              31, // Jul
              31, // Aug
              30, // Sep
              31, // Oct
              30, // Nov
              31  // Dec
        };

        /// <summary>
        /// Validate a TimeStamp.
        /// </summary>
        private bool IsValidDateTime(TimeStamp dateTime)
        {
            if (dateTime == null)
            {
                return false;
            }

            // TODO: create settings to customize these specifics
            if (dateTime.Year > _currentYear)
            {
                return false;
            }

            if (dateTime.Month > 12 || dateTime.Month < 1)
            {
                return false;
            }

            if (dateTime.Day > MaxDays[dateTime.Month - 1] || dateTime.Day < 1)
            {
                return false;
            }

            if (dateTime.Hour > 23 || dateTime.Hour < 0)
            {
                return false;
            }

            if (dateTime.Minute > 59 || dateTime.Minute < 0)
            {
                return false;
            }

            if (dateTime.Second > 59 || dateTime.Second < 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validate FirstCluster.
        /// </summary>
        private bool IsValidFirstCluster(uint firstCluster)
        {
            if (firstCluster > _volume.MaxClusters)
            {
                return false;
            }

            // NOTE: deleted files have been found with firstCluster set to 0
            //  To be as thorough as we can, let's include those.
            //if (firstCluster == 0)
            //{
            //    return false;
            //}

            return true;
        }

        /// <summary>
        /// Validate FileNameLength.
        /// </summary>
        private bool IsValidFileNameLength(uint fileNameLength)
        {
            if (fileNameLength == 0x00 || fileNameLength == 0x01 || fileNameLength == 0xff)
            {
                return false;
            }

            if (fileNameLength > 0x2a && fileNameLength != 0xe5)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Check if dirent is actually a dirent.
        /// </summary>
        private bool IsValidDirent(DirectoryEntry dirent)
        {
            if (!IsValidFileNameLength(dirent.FileNameLength))
            {
                return false;
            }

            if (!IsValidFirstCluster(dirent.FirstCluster))
            {
                return false;
            }

            if (!IsValidFileNameBytes(dirent.FileNameBytes))
            {
                return false;
            }

            if (!IsValidAttributes(dirent.FileAttributes))
            {
                return false;
            }

            if (!IsValidDateTime(dirent.CreationTime) ||
                !IsValidDateTime(dirent.LastAccessTime) ||
                !IsValidDateTime(dirent.LastWriteTime))
            {
                return false;
            }

            return true;
        }
    }
}