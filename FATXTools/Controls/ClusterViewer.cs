// Переписано
using FATX.Analyzers;
using FATX.FileSystem;
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

using ClusterColorMap = System.Collections.Generic.Dictionary<uint, System.Drawing.Color>;

namespace FATXTools.Controls
{
    public partial class ClusterViewer : UserControl
    {
        private DataMap dataMap;
        private Volume volume;
        private IntegrityAnalyzer integrityAnalyzer;

        private ClusterColorMap clusterColorMap;

        private Color emptyColor = Color.White;
        private Color activeColor = Color.Green;
        private Color recoveredColor = Color.Yellow;
        private Color collisionColor = Color.Red;
        private Color rootColor = Color.Purple;

        private int previousSelectedIndex;
        private int currentSelectedIndex;

        private int currentClusterChainIndex;

        public ClusterViewer(Volume volume, IntegrityAnalyzer integrityAnalyzer)
        {
            try
            {
                InitializeComponent();

                this.volume = volume;
                this.integrityAnalyzer = integrityAnalyzer;

                if (volume != null)
                {
                    dataMap = new DataMap((int)volume.MaxClusters);
                    dataMap.Location = new Point(0, 0);
                    dataMap.Dock = DockStyle.Fill;
                    dataMap.CellSelected += DataMap_CellSelected;
                    dataMap.CellHovered += DataMap_CellHovered;
                    dataMap.Increment = (int)volume.BytesPerCluster;

                    clusterColorMap = new ClusterColorMap();

                    this.Controls.Add(dataMap);
                    InitializeActiveFileSystem();
                    UpdateDataMap();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClusterViewer] Ошибка инициализации: {ex.Message}");
            }
        }

        public void UpdateClusters()
        {
            if (volume == null || integrityAnalyzer == null) return;

            try
            {
                // Правило 1: Защита цикла обновления
                for (uint i = 1; i < volume.MaxClusters; i++)
                {
                    try
                    {
                        var occupants = integrityAnalyzer.GetClusterOccupants(i);

                        // Сброс цвета на белый по умолчанию
                        if (!clusterColorMap.ContainsKey(i))
                        {
                            clusterColorMap[i] = emptyColor;
                        }
                        else
                        {
                            clusterColorMap[i] = emptyColor;
                        }

                        if (occupants == null || occupants.Count == 0)
                        {
                            // No occupants
                            continue;
                        }

                        if (occupants.Count > 1)
                        {
                            if (occupants.Any(file => !file.IsDeleted))
                            {
                                clusterColorMap[i] = activeColor;
                            }
                            else
                            {
                                clusterColorMap[i] = collisionColor;
                            }
                        }
                        else
                        {
                            // Проверка Count > 0 для безопасности
                            if (occupants.Count > 0)
                            {
                                var occupant = occupants[0];
                                if (!occupant.IsDeleted)
                                {
                                    // Sole occupant
                                    clusterColorMap[i] = activeColor;
                                }
                                else
                                {
                                    // Only recovered occupant
                                    clusterColorMap[i] = recoveredColor;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Правило 1: Продолжаем отрисовку, если один кластер вызывает ошибку
                        Trace.WriteLine($"[ClusterViewer] Ошибка обновления кластера {i}: {ex.Message}");
                    }
                }

                // Правило 1: Защита от ошибки доступа к ключу словаря для рутового кластера
                if (volume.RootDirFirstCluster > 0 && volume.RootDirFirstCluster < volume.MaxClusters)
                {
                    clusterColorMap[volume.RootDirFirstCluster] = rootColor;
                }

                UpdateDataMap();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClusterViewer] Критическая ошибка при обновлении карты: {ex.Message}");
            }
        }

        private void InitializeActiveFileSystem()
        {
            try
            {
                // TODO: See if we can merge this with UpdateClusters
                if (volume != null && volume.RootDirFirstCluster > 0)
                {
                    clusterColorMap[volume.RootDirFirstCluster] = rootColor;
                }
                UpdateClusters();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClusterViewer] Ошибка InitActiveFileSystem: {ex.Message}");
            }
        }

        private int ClusterToCellIndex(uint clusterIndex)
        {
            return (int)clusterIndex - 1;
        }

        private uint CellToClusterIndex(int cellIndex)
        {
            return (uint)cellIndex + 1;
        }

        private void SetCellColor(int cellIndex, Color color)
        {
            try
            {
                // Правило 1: Проверка границ
                if (cellIndex >= 0 && cellIndex < dataMap.CellCount)
                {
                    // Проверка на null и.ContainsKey
                    if (dataMap.Cells != null && dataMap.Cells.ContainsKey(cellIndex))
                    {
                        dataMap.Cells[cellIndex].Color = color;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClusterViewer] Ошибка SetCellColor: {ex.Message}");
            }
        }

        private void UpdateDataMap()
        {
            try
            {
                foreach (var pair in clusterColorMap)
                {
                    SetCellColor(ClusterToCellIndex(pair.Key), pair.Value);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClusterViewer] Ошибка UpdateDataMap: {ex.Message}");
            }
        }

        private string BuildToolTipMessage(int index, DirectoryEntry dirent, bool deleted)
        {
            try
            {
                // What kind of data is stored in this cluster?
                string dataType;
                if (dirent.IsDirectory())
                {
                    dataType = "Dirent Stream";
                }
                else
                {
                    dataType = "File Data";
                }

                // Правило 1: Безопасное получение даты (AsDateTime может падать)
                string creationDate = "Unknown";
                string writeDate = "Unknown";
                string accessDate = "Unknown";

                try
                {
                    creationDate = dirent.CreationTime.AsDateTime().ToString();
                }
                catch { }

                try
                {
                    writeDate = dirent.LastWriteTime.AsDateTime().ToString();
                }
                catch { }

                try
                {
                    accessDate = dirent.LastAccessTime.AsDateTime().ToString();
                }
                catch { }

                string message = Environment.NewLine +
                    index.ToString() + "." +
                    " Type: " + dataType + Environment.NewLine +
                    " Occupant: " + dirent.FileName + Environment.NewLine +
                    " File Size: " + dirent.FileSize.ToString("X8") + Environment.NewLine +
                    " Date Created: " + creationDate + Environment.NewLine +
                    " Date Written: " + writeDate + Environment.NewLine +
                    " Date Accessed: " + accessDate + Environment.NewLine +
                    " Deleted: " + (deleted).ToString() + Environment.NewLine;

                return message;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClusterViewer] Ошибка BuildToolTip для {dirent.FileName}: {ex.Message}");
                return "Error reading metadata.";
            }
        }

        private void DataMap_CellHovered(object sender, EventArgs e)
        {
            try
            {
                var cellHoveredEventArgs = e as CellHoveredEventArgs;

                if (cellHoveredEventArgs != null)
                {
                    var clusterIndex = CellToClusterIndex(cellHoveredEventArgs.Index);

                    // Правило 2: Trace.WriteLine вместо Debug.WriteLine
                    // Trace.WriteLine($"Cluster Index: {clusterIndex}"); // Логирование ховера отключено для чистоты, можно включить для дебага

                    var occupants = integrityAnalyzer?.GetClusterOccupants(clusterIndex);

                    string toolTipMessage = "Cluster Index: " + clusterIndex.ToString() + Environment.NewLine;
                    toolTipMessage += "Cluster Address: 0x" + volume.ClusterToPhysicalOffset(clusterIndex).ToString("X") + Environment.NewLine;

                    if (clusterIndex == volume.RootDirFirstCluster)
                    {
                        toolTipMessage += Environment.NewLine + Environment.NewLine;
                        toolTipMessage += "Type: Root Directory";
                    }
                    else if (occupants == null)
                    {
                        // TODO: something is off
                        Trace.WriteLine("[ClusterViewer] Occupants is null for cluster " + clusterIndex);
                    }
                    else if (occupants.Count > 0)
                    {
                        toolTipMessage += Environment.NewLine;

                        int index = 1;
                        foreach (var occupant in occupants)
                        {
                            try
                            {
                                var dirent = occupant.GetDirent();
                                if (dirent != null)
                                {
                                    toolTipMessage += BuildToolTipMessage(index, dirent, occupant.IsDeleted);
                                    index++;
                                }
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"[ClusterViewer] Ошибка в цикле occupants (hover): {ex.Message}");
                            }
                        }
                    }

                    toolTip1.SetToolTip(this.dataMap, toolTipMessage);
                }
                else
                {
                    toolTip1.SetToolTip(this.dataMap, "");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClusterViewer] Ошибка CellHovered: {ex.Message}");
            }
        }

        private void DataMap_CellSelected(object sender, EventArgs e)
        {
            try
            {
                currentSelectedIndex = dataMap.SelectedIndex;

                var clusterIndex = CellToClusterIndex(currentSelectedIndex);

                // Правило 2: Trace.WriteLine вместо Debug.WriteLine
                Trace.WriteLine($"[ClusterViewer] Selected Cluster Index: {clusterIndex}");

                var occupants = integrityAnalyzer?.GetClusterOccupants(clusterIndex);

                if (occupants == null)
                {
                    // Something is wrong
                    Trace.WriteLine("[ClusterViewer] Occupants is null on selection.");
                    return;
                }

                if (occupants.Count > 0)
                {
                    if (currentSelectedIndex != previousSelectedIndex)
                    {
                        previousSelectedIndex = currentSelectedIndex;
                        currentClusterChainIndex = 0;
                    }

                    // Правило 1: Защита от выхода за границы массива
                    if (currentClusterChainIndex >= occupants.Count)
                    {
                        currentClusterChainIndex = 0;
                    }

                    var databaseFile = occupants[currentClusterChainIndex];
                    if (databaseFile != null)
                    {
                        var clusterChain = databaseFile.ClusterChain;
                        if (clusterChain != null)
                        {
                            // TODO: Change highlight color for colliding clusters
                            foreach (var cluster in clusterChain)
                            {
                                try
                                {
                                    int cellIdx = ClusterToCellIndex(cluster);
                                    if (dataMap.Cells.ContainsKey(cellIdx))
                                    {
                                        dataMap.Cells[cellIdx].Selected = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Trace.WriteLine($"[ClusterViewer] Ошибка выделения кластера {cluster}: {ex.Message}");
                                }
                            }
                        }
                    }

                    // Toggle between each occupant after each click
                    currentClusterChainIndex++;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClusterViewer] Ошибка CellSelected: {ex.Message}");
            }
        }
    }
}