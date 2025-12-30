// Переписано
using FATX.FileSystem;
using FATXTools.Database;
using System;
using System.Collections.Generic;
using System.Diagnostics; // 1. Подключаем Trace

namespace FATX.Analyzers
{
    public class IntegrityAnalyzer
    {
        /// <summary>
        /// Active volume to analyze against.
        /// </summary>
        private Volume volume;

        private FileDatabase database;

        /// <summary>
        /// Mapping of cluster indexes to a list of entities that occupy it.
        /// </summary>
        private Dictionary<uint, List<DatabaseFile>> clusterMap;

        public IntegrityAnalyzer(Volume volume, FileDatabase database)
        {
            this.volume = volume;
            this.database = database;

            // Now that we have registered them, let's update the cluster map
            UpdateClusterMap();
        }

        private void UpdateClusters(DatabaseFile databaseFile)
        {
            foreach (var cluster in databaseFile.ClusterChain)
            {
                // Правило 1: Проверка границ перед доступом к словарю, чтобы избежать Exception
                if (cluster < volume.MaxClusters)
                {
                    if (clusterMap.ContainsKey(cluster))
                    {
                        var occupants = clusterMap[cluster];
                        if (!occupants.Contains(databaseFile))
                            occupants.Add(databaseFile);
                    }
                }
                else
                {
                    // Правило 3: Улучшенное логирование (некорректный кластер)
                    Trace.WriteLine($"[IntegrityAnalyzer] Файл {databaseFile.FileName} ссылается на кластер {cluster}, который вне границ тома (MaxClusters: {volume.MaxClusters}).");
                }
            }
        }

        private void UpdateClusterMap()
        {
            try
            {
                clusterMap = new Dictionary<uint, List<DatabaseFile>>((int)volume.MaxClusters);

                for (uint i = 0; i < volume.MaxClusters; i++)
                {
                    clusterMap[i] = new List<DatabaseFile>();
                }

                foreach (var pair in database.GetFiles())
                {
                    try
                    {
                        var databaseFile = pair.Value;

                        // We handle active cluster chains conventionally
                        if (!databaseFile.IsDeleted)
                        {
                            UpdateClusters(databaseFile);
                        }
                        // Otherwise, we generate an artificial cluster chain
                        else
                        {
                            // TODO: Add a blocklist setting
                            if (databaseFile.FileName.StartsWith("xdk_data") ||
                                databaseFile.FileName.StartsWith("xdk_file") ||
                                databaseFile.FileName.StartsWith("tempcda"))
                            {
                                // These are usually always large and/or corrupted
                                // TODO: still don't really know what these files are
                                Trace.WriteLine($"[IntegrityAnalyzer] Пропуск системного файла: {databaseFile.FileName}");
                                continue;
                            }

                            UpdateClusters(databaseFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Правило 1: Если один файл вызвал ошибку, пропускаем его, но продолжаем строить карту
                        Trace.WriteLine($"[IntegrityAnalyzer] Ошибка при обновлении карты кластеров для файла {pair.Key}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[IntegrityAnalyzer] Критическая ошибка при инициализации карты кластеров: {ex.Message}");
            }
        }

        public void Update()
        {
            UpdateClusterMap(); // Update clusterMap
            UpdateCollisions(); // Update collisions (Do the collision check)
            PerformRanking();   // Rank all clusters
        }

        private void UpdateCollisions()
        {
            foreach (var databaseFile in database.GetFiles().Values)
            {
                try
                {
                    databaseFile.SetCollisions(FindCollidingClusters(databaseFile));
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[IntegrityAnalyzer] Ошибка при поиске коллизий для {databaseFile.FileName}: {ex.Message}");
                }
            }
        }

        private List<uint> FindCollidingClusters(DatabaseFile databaseFile)
        {
            // Get a list of cluster who are possibly corrupted
            List<uint> collidingClusters = new List<uint>();

            try
            {
                // for each cluster used by this dirent, check if other dirents are
                // also claiming it.
                foreach (var cluster in databaseFile.ClusterChain)
                {
                    if (cluster < volume.MaxClusters && clusterMap.ContainsKey(cluster))
                    {
                        if (clusterMap[cluster].Count > 1)
                        {
                            collidingClusters.Add((uint)cluster);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[IntegrityAnalyzer] Ошибка в процессе проверки коллизий: {ex.Message}");
            }

            return collidingClusters;
        }

        private bool WasModifiedLast(DatabaseFile databaseFile, List<uint> collisions)
        {
            var dirent = databaseFile.GetDirent();

            try
            {
                foreach (var cluster in collisions)
                {
                    if (clusterMap.ContainsKey(cluster))
                    {
                        var clusterEnts = clusterMap[cluster];
                        foreach (var ent in clusterEnts)
                        {
                            var entDirent = ent.GetDirent();

                            // Skip when we encounter the same dirent
                            if (dirent.Offset == entDirent.Offset)
                            {
                                continue;
                            }

                            // Правило 1: Защита при сравнении дат (AsDateTime может падать)
                            try
                            {
                                if (dirent.LastAccessTime.AsDateTime() < entDirent.LastAccessTime.AsDateTime())
                                {
                                    return false;
                                }
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"[IntegrityAnalyzer] Не удалось сравнить время доступа для {dirent.FileName}: {ex.Message}. Предполагаем, что файл старый.");
                                // Если не можем сравнить, безопасно предположить false (не последний)
                                return false;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[IntegrityAnalyzer] Ошибка при проверке времени изменения {databaseFile.FileName}: {ex.Message}");
                return false;
            }

            return true;
        }

        private void DoRanking(DatabaseFile databaseFile)
        {
            try
            {
                var dirent = databaseFile.GetDirent();

                // Rank 1 (0 в коде) - Part of active file system / Not deleted
                // Rank 2 (1 в коде) - Recovered / No conflicting clusters
                // Rank 3 (2 в коде) - Recovered / Conflicting / Most recent
                // Rank 4 (3 в коде) - Recovered / Conflicting / Not most recent
                // Rank 5 (4 в коде) - Recovered / All overwritten

                if (!databaseFile.IsDeleted)
                {
                    if (!dirent.IsDeleted())
                    {
                        databaseFile.SetRanking((0));
                    }
                }
                else
                {
                    // File was deleted
                    var collisions = databaseFile.GetCollisions();
                    if (collisions.Count == 0)
                    {
                        databaseFile.SetRanking((1));
                    }
                    else
                    {
                        // File has colliding clusters
                        if (WasModifiedLast(databaseFile, collisions))
                        {
                            // This file appears to have been written most recently.
                            databaseFile.SetRanking((2));
                        }
                        else
                        {
                            // File was predicted to be overwritten
                            var numClusters = (int)(((dirent.FileSize + (this.volume.BytesPerCluster - 1)) &
                                ~(this.volume.BytesPerCluster - 1)) / this.volume.BytesPerCluster);

                            if (collisions.Count != numClusters)
                            {
                                // Not every cluster was overwritten
                                databaseFile.SetRanking((3));
                            }
                            else
                            {
                                // Every cluster appears to have been overwritten
                                databaseFile.SetRanking((4));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Правило 1: Ошибка ранжирования не должна ломать цикл
                Trace.WriteLine($"[IntegrityAnalyzer] Ошибка при присвоении ранга файлу {databaseFile.FileName}: {ex.Message}");
                // Можно присвоить самый низкий ранг по умолчанию, если нужно
                databaseFile.SetRanking(4);
            }
        }

        private void PerformRanking()
        {
            foreach (var pair in database.GetFiles())
            {
                DoRanking(pair.Value);
            }
        }

        public List<DatabaseFile> GetClusterOccupants(uint cluster)
        {
            List<DatabaseFile> occupants;

            if (clusterMap.ContainsKey(cluster))
            {
                occupants = clusterMap[cluster];
            }
            else
            {
                occupants = null;
                Trace.WriteLine($"[IntegrityAnalyzer] Попытка получить жильцов кластера {cluster}, который отсутствует в карте.");
            }

            return occupants;
        }
    }
}