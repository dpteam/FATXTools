// Переписано
using FATX.Analyzers;
using FATX.FileSystem;
using System;
using System.Collections.Generic;
using System.Diagnostics; // 1. Подключаем Trace
using System.IO;
using System.Text.Json;

namespace FATXTools.Database
{
    public class PartitionDatabase
    {
        // We just want this for volume info (offset, length, name)
        Volume volume;

        FileCarver fileCarver;  // TODO: Get rid of this. We should be able to get this information from the FileDatabase.

        /// <summary>
        /// Whether or not MetadataAnalyzer's results are in.
        /// </summary>
        bool metadataAnalyzer;

        /// <summary>
        /// Database that stores analysis results.
        /// </summary>
        FileDatabase fileDatabase;

        /// <summary>
        /// The associated view for this PartitionDatabase
        /// </summary>
        PartitionView view; // TODO: Use events instead.

        public event EventHandler OnLoadRecoveryFromDatabase;

        public PartitionDatabase(Volume volume)
        {
            this.volume = volume;
            this.metadataAnalyzer = false;
            this.fileCarver = null;
            this.view = null;

            // FileDatabase может выбросить исключение, если Volume поврежден
            try
            {
                this.fileDatabase = new FileDatabase(volume);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PartitionDatabase] Ошибка при инициализации FileDatabase: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get the partition name associated with this database.
        /// </summary>
        public string PartitionName => volume?.Name ?? "Unknown";

        public Volume Volume => volume;

        /// <summary>
        /// Set associated view for this database.
        /// </summary>
        public void SetPartitionView(PartitionView view)
        {
            this.view = view;
        }

        /// <summary>
        /// Set whether or not analysis was performed.
        /// </summary>
        public void SetMetadataAnalyzer(bool metadataAnalyzer)
        {
            this.metadataAnalyzer = metadataAnalyzer;
        }

        public void SetFileCarver(FileCarver fileCarver)
        {
            this.fileCarver = fileCarver;
        }

        /// <summary>
        /// Get FileDatabase for this partition.
        /// </summary>
        public FileDatabase GetFileDatabase()
        {
            return this.fileDatabase;
        }

        public void Save(Dictionary<string, object> partitionObject)
        {
            try
            {
                partitionObject["Name"] = this.volume?.Name;
                partitionObject["Offset"] = this.volume?.Offset;
                partitionObject["Length"] = this.volume?.Length;

                partitionObject["Analysis"] = new Dictionary<string, object>();
                var analysisObject = partitionObject["Analysis"] as Dictionary<string, object>;

                analysisObject["MetadataAnalyzer"] = new List<Dictionary<string, object>>();
                if (metadataAnalyzer)
                {
                    var metadataAnalysisList = analysisObject["MetadataAnalyzer"] as List<Dictionary<string, object>>;
                    SaveMetadataAnalysis(metadataAnalysisList);
                }

                analysisObject["FileCarver"] = new List<Dictionary<string, object>>();
                if (fileCarver != null)
                {
                    var fileCarverObject = analysisObject["FileCarver"] as List<Dictionary<string, object>>;
                    SaveFileCarver(fileCarverObject);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PartitionDatabase] Ошибка сохранения данных раздела: {ex.Message}");
            }
        }

        private void SaveMetadataAnalysis(List<Dictionary<string, object>> metadataAnalysisList)
        {
            if (metadataAnalysisList == null) return;

            foreach (var databaseFile in fileDatabase.GetRootFiles())
            {
                try
                {
                    var directoryEntryObject = new Dictionary<string, object>();
                    metadataAnalysisList.Add(directoryEntryObject);
                    SaveDirectoryEntry(directoryEntryObject, databaseFile);
                }
                catch (Exception ex)
                {
                    // Правило 1: Продолжаем сохранение других файлов, если один вызывает ошибку
                    Trace.WriteLine($"[PartitionDatabase] Ошибка сохранения файла {databaseFile.FileName}: {ex.Message}");
                }
            }
        }

        private void SaveFileCarver(List<Dictionary<string, object>> fileCarverList)
        {
            // Правило 1: Проверка на null
            if (fileCarver == null) return;

            foreach (var file in fileCarver.GetCarvedFiles())
            {
                try
                {
                    var fileCarverObject = new Dictionary<string, object>();

                    fileCarverObject["Offset"] = file.Offset;
                    fileCarverObject["Name"] = file.FileName;
                    fileCarverObject["Size"] = file.FileSize;

                    fileCarverList.Add(fileCarverObject);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[PartitionDatabase] Ошибка сохранения вырезанного файла {file.FileName}: {ex.Message}");
                }
            }
        }

        private void SaveDirectoryEntry(Dictionary<string, object> directoryEntryObject, DatabaseFile directoryEntry)
        {
            try
            {
                directoryEntryObject["Cluster"] = directoryEntry.Cluster;
                directoryEntryObject["Offset"] = directoryEntry.Offset;

                /*
                 * At this moment, I believe this will only be used for debugging.
                 */
                directoryEntryObject["FileNameLength"] = directoryEntry.FileNameLength;
                directoryEntryObject["FileAttributes"] = (byte)directoryEntry.FileAttributes;
                directoryEntryObject["FileName"] = directoryEntry.FileName;
                directoryEntryObject["FileNameBytes"] = directoryEntry.FileNameBytes;
                directoryEntryObject["FirstCluster"] = directoryEntry.FirstCluster;
                directoryEntryObject["FileSize"] = directoryEntry.FileSize;

                // AsInteger может выбросить исключение если данные повреждены, но мы защитили TimeStamp класс
                directoryEntryObject["CreationTime"] = directoryEntry.CreationTime.AsInteger();
                directoryEntryObject["LastWriteTime"] = directoryEntry.LastWriteTime.AsInteger();
                directoryEntryObject["LastAccessTime"] = directoryEntry.LastAccessTime.AsInteger();

                if (directoryEntry.IsDirectory())
                {
                    directoryEntryObject["Children"] = new List<Dictionary<string, object>>();
                    var childrenList = directoryEntryObject["Children"] as List<Dictionary<string, object>>;

                    if (directoryEntry.Children != null)
                    {
                        foreach (var child in directoryEntry.Children)
                        {
                            try
                            {
                                var childObject = new Dictionary<string, object>();
                                childrenList.Add(childObject);
                                SaveDirectoryEntry(childObject, child);
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"[PartitionDatabase] Ошибка сохранения потомка {child.FileName}: {ex.Message}");
                            }
                        }
                    }
                }

                directoryEntryObject["Clusters"] = directoryEntry.ClusterChain;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PartitionDatabase] Критическая ошибка сохранения структуры директории {directoryEntry.FileName}: {ex.Message}");
            }
        }

        public DatabaseFile LoadDirectoryEntryFromDatabase(JsonElement directoryEntryObject)
        {
            try
            {
                // Make sure that offset property was stored for this file
                JsonElement offsetElement;
                if (!directoryEntryObject.TryGetProperty("Offset", out offsetElement))
                {
                    // Правило 2 и 3: Trace + контекст
                    Trace.WriteLine("[PartitionDatabase] Ошибка загрузки объекта из БД: отсутствует поле Offset");
                    return null;
                }

                // Make sure that cluster property was stored for this file
                JsonElement clusterElement;
                if (!directoryEntryObject.TryGetProperty("Cluster", out clusterElement))
                {
                    Trace.WriteLine("[PartitionDatabase] Ошибка загрузки объекта из БД: отсутствует поле Cluster");
                    return null;
                }

                long offset = offsetElement.GetInt64();
                uint cluster = clusterElement.GetUInt32();

                // Ensure that offset is within bounds of partition.
                if (volume == null || offset < volume.Offset || offset > volume.Offset + volume.Length)
                {
                    Trace.WriteLine($"[PartitionDatabase] Ошибка загрузки объекта: некорректный Offset {offset} (Partition: 0x{volume.Offset:X} - 0x{volume.Offset + volume.Length:X})");
                    return null;
                }

                // Ensure that cluster index is valid
                if (cluster < 0 || cluster > volume.MaxClusters)
                {
                    Trace.WriteLine($"[PartitionDatabase] Ошибка загрузки объекта: некорректный Cluster {cluster} (MaxClusters: {volume.MaxClusters})");
                    return null;
                }

                // Read DirectoryEntry data
                byte[] data = new byte[0x40];

                // Правило 1: Seek и Read защищены в PhysicalDisk/Volume, но на всякий случай
                volume.GetReader().Seek(offset);
                volume.GetReader().Read(data, 0x40);

                // Create a DirectoryEntry
                var directoryEntry = new DirectoryEntry(volume.Platform, data, 0);
                directoryEntry.Cluster = cluster;
                directoryEntry.Offset = offset;

                // Add this file to the FileDatabase
                var databaseFile = fileDatabase.AddFile(directoryEntry, true);

                /*
                 * Here we begin assigning our user-configurable modifications
                 */

                if (directoryEntryObject.TryGetProperty("Children", out var childrenElement))
                {
                    foreach (var childElement in childrenElement.EnumerateArray())
                    {
                        try
                        {
                            // Рекурсивный вызов
                            LoadDirectoryEntryFromDatabase(childElement);
                        }
                        catch (Exception ex)
                        {
                            // Правило 1: Не даем одному плохому потомку сломать загрузку всего дерева
                            Trace.WriteLine($"[PartitionDatabase] Ошибка при загрузке потомка (JSON): {ex.Message}");
                        }
                    }
                }

                if (directoryEntryObject.TryGetProperty("Clusters", out var clustersElement))
                {
                    List<uint> clusterChain = new List<uint>();

                    //if (databaseFile.FileName == "ears_godfather")
                    //    System.Diagnostics.Debugger.Break();

                    foreach (var clusterIndex in clustersElement.EnumerateArray())
                    {
                        clusterChain.Add(clusterIndex.GetUInt32());
                    }

                    databaseFile.ClusterChain = clusterChain;
                }

                return databaseFile;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PartitionDatabase] Исключение при загрузке DirectoryEntry из базы данных: {ex.Message}");
                return null;
            }
        }

        private bool LoadFromDatabase(JsonElement metadataAnalysisObject)
        {
            if (metadataAnalysisObject.GetArrayLength() == 0)
                return false;

            // Load each root file and its children from json database
            foreach (var directoryEntryObject in metadataAnalysisObject.EnumerateArray())
            {
                try
                {
                    LoadDirectoryEntryFromDatabase(directoryEntryObject);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[PartitionDatabase] Пропуск поврежденной записи при загрузке корня: {ex.Message}");
                }
            }

            return true;
        }

        public void LoadFromJson(JsonElement partitionElement)
        {
            try
            {
                // We are loading a new database so clear previous results
                fileDatabase.Reset();

                // Find Analysis element, which contains analysis results
                JsonElement analysisElement;
                if (!partitionElement.TryGetProperty("Analysis", out analysisElement))
                {
                    var name = partitionElement.GetProperty("Name").GetString();
                    throw new FileLoadException($"Database: Partition {name} is missing Analysis object!");
                }

                if (analysisElement.TryGetProperty("MetadataAnalyzer", out var metadataAnalysisList))
                {
                    // Loads files from json into FileDatabase
                    // Only post results if there actually was any
                    if (LoadFromDatabase(metadataAnalysisList))
                    {
                        OnLoadRecoveryFromDatabase?.Invoke(null, null);

                        // Mark that analysis was done
                        this.metadataAnalyzer = true;
                    }
                    else
                    {
                        // Element was there but no analysis results were loaded
                        this.metadataAnalyzer = false;
                    }
                }

                if (analysisElement.TryGetProperty("FileCarver", out var fileCarverList))
                {
                    try
                    {
                        // TODO: We will begin replacing this when we start work on customizable "CarvedFiles"
                        var analyzer = new FileCarver(this.volume, FileCarverInterval.Cluster, this.volume.Length);

                        analyzer.LoadFromDatabase(fileCarverList);

                        if (analyzer.GetCarvedFiles().Count > 0)
                        {
                            // Правило 1: Проверка на null перед вызовом View
                            if (view != null)
                            {
                                view.CreateCarverView(analyzer);
                            }
                            else
                            {
                                Trace.WriteLine("[PartitionDatabase] Попытка создания CarverView, но View равен null.");
                            }

                            this.fileCarver = analyzer;
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"[PartitionDatabase] Ошибка при загрузке FileCarver: {ex.Message}");
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                Trace.WriteLine($"[PartitionDatabase] Ошибка формата JSON: {jsonEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PartitionDatabase] Критическая ошибка LoadFromJson: {ex.Message}");
            }
        }
    }
}