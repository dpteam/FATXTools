// Переписано
using FATX.FileSystem;
using FATXTools.Database;
using System;
using System.Collections.Generic;
using System.Diagnostics; // 1. Подключаем Trace
using System.Windows.Forms;

namespace FATXTools.Dialogs
{
    public partial class ClusterChainDialog : Form
    {
        Volume volume;
        DatabaseFile file;

        public List<uint> NewClusterChain { get; set; }

        public ClusterChainDialog(Volume volume, DatabaseFile file)
        {
            try
            {
                InitializeComponent();
                this.volume = volume;
                this.file = file;

                numericUpDown1.Minimum = 1;

                // Правило 1: Проверка валидности данных перед использованием
                if (volume != null && volume.MaxClusters > 0)
                {
                    numericUpDown1.Maximum = volume.MaxClusters;
                }
                else
                {
                    Trace.WriteLine("[ClusterChainDialog] Внимание: Том не инициализирован или MaxClusters = 0.");
                    numericUpDown1.Maximum = int.MaxValue; // Значение по умолчанию
                }

                InitializeClusterList(file?.ClusterChain);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClusterChainDialog] Ошибка инициализации диалога: {ex.Message}");
            }
        }

        private void InitializeClusterList(List<uint> clusterChain)
        {
            listView1.BeginUpdate();

            // Правило 1: Проверка на null перед перебором
            if (clusterChain != null)
            {
                foreach (var cluster in clusterChain)
                {
                    try
                    {
                        AddCluster(cluster);
                    }
                    catch (Exception ex)
                    {
                        // Правило 3: Логируем ошибку добавления конкретного элемента, но не прерываем загрузку всего списка
                        Trace.WriteLine($"[ClusterChainDialog] Не удалось добавить кластер {cluster} в список: {ex.Message}");
                    }
                }
            }

            listView1.EndUpdate();
        }

        private void AddCluster(uint cluster)
        {
            try
            {
                // Правило 1: Проверка на null тома
                if (volume == null) return;

                // Правило 1: Проверка на выход за границы тома
                if (cluster > volume.MaxClusters)
                {
                    Trace.WriteLine($"[ClusterChainDialog] Некорректный кластер: {cluster}. MaxClusters: {volume.MaxClusters}. Смещение может быть неверным.");
                }

                // Метод ClusterToPhysicalOffset может выбросить исключение при арифметическом переполнении, ловим его здесь или выше
                var address = volume.ClusterToPhysicalOffset(cluster);

                ListViewItem item = new ListViewItem(new string[] { $"0x{address.ToString("X")}", cluster.ToString() });
                item.Tag = cluster;
                listView1.Items.Add(item);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClusterChainDialog] Ошибка вычисления физического адреса для кластера {cluster}: {ex.Message}");
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            try
            {
                var cluster = (uint)numericUpDown1.Value;
                AddCluster(cluster);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClusterChainDialog] Ошибка при ручном добавлении кластера: {ex.Message}");
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            try
            {
                listView1.BeginUpdate();

                foreach (ListViewItem item in listView1.SelectedItems)
                {
                    listView1.Items.Remove(item);
                }

                listView1.EndUpdate();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClusterChainDialog] Ошибка при удалении выбранных элементов: {ex.Message}");
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                NewClusterChain = new List<uint>();

                foreach (ListViewItem item in listView1.Items)
                {
                    // Правило 1: Проверка на null перед кастом
                    if (item.Tag != null)
                    {
                        NewClusterChain.Add((uint)item.Tag);
                    }
                    else
                    {
                        Trace.WriteLine("[ClusterChainDialog] Обнаружен ListViewItem без Tag (данные могут быть потеряны).");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClusterChainDialog] Критическая ошибка при формировании списка кластеров: {ex.Message}");
                MessageBox.Show("Не удалось сформировать список кластеров. Подробности в логе.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}