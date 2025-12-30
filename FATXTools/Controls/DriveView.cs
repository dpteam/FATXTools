// Переписано
using FATX;
using FATX.FileSystem;
using FATXTools.Controls;
using FATXTools.Database;
using FATXTools.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics; // 1. Подключаем Trace
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FATXTools
{
    public partial class DriveView : UserControl
    {
        /// <summary>
        /// List of loaded drives.
        /// </summary>
        //private List<DriveReader> driveList = new List<DriveReader>();

        /// <summary>
        /// Currently loaded drive.
        /// </summary>
        private DriveReader drive;

        private string driveName;

        /// <summary>
        /// List of partitions in this drive.
        /// </summary>
        private List<PartitionView> partitionViews = new List<PartitionView>();

        private TaskRunner taskRunner;

        public event EventHandler TaskStarted;

        public event EventHandler TaskCompleted;

        public event EventHandler<PartitionSelectedEventArgs> TabSelectionChanged;

        private DriveDatabase driveDatabase;

        public DriveView()
        {
            InitializeComponent();
        }

        public void AddDrive(string name, DriveReader drive)
        {
            try
            {
                this.driveName = name;
                this.drive = drive;

                // Инициализация базы может упасть (например, если JSON поврежден)
                this.driveDatabase = new DriveDatabase(name, drive);
                this.driveDatabase.OnPartitionAdded += DriveDatabase_OnPartitionAdded;
                this.driveDatabase.OnPartitionRemoved += DriveDatabase_OnPartitionRemoved;

                // Single task runner for this drive
                // Currently only one task will be allowed to operate on a drive to avoid race conditions.

                // Правило 1: Защита, если ParentForm равен null
                Form parentForm = this.ParentForm;
                if (parentForm == null && this.FindForm() is Form mainForm)
                {
                    parentForm = mainForm;
                }

                this.taskRunner = new TaskRunner(parentForm);
                this.taskRunner.TaskStarted += TaskRunner_TaskStarted;
                this.taskRunner.TaskCompleted += TaskRunner_TaskCompleted;

                this.partitionTabControl.MouseClick += PartitionTabControl_MouseClick;

                foreach (var volume in drive.Partitions)
                {
                    try
                    {
                        AddPartition(volume);
                    }
                    catch (Exception ex)
                    {
                        // Правило 1: Если один раздел не загружается, пробуем остальные
                        Trace.WriteLine($"[DriveView] Ошибка при автоматическом добавлении раздела {volume.Name}: {ex.Message}");
                    }
                }

                // Fire SelectedIndexChanged event.
                SelectedIndexChanged();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveView] Критическая ошибка при инициализации диска {name}: {ex.Message}");
            }
        }

        private void DriveDatabase_OnPartitionRemoved(object sender, RemovePartitionEventArgs e)
        {
            try
            {
                var index = e.Index;

                // Правило 1: Проверка границ перед удалением
                if (index >= 0 && index < partitionTabControl.TabPages.Count && index < partitionViews.Count)
                {
                    partitionTabControl.TabPages.RemoveAt(index);
                    partitionViews.RemoveAt(index);
                }
                else
                {
                    Trace.WriteLine($"[DriveView] Попытка удаления раздела с невалидным индексом: {index}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveView] Ошибка удаления раздела (индекс {e.Index}): {ex.Message}");
            }
        }

        private void PartitionTabControl_MouseClick(object sender, MouseEventArgs e)
        {
            try
            {
                if (e.Button == MouseButtons.Right)
                {
                    for (var i = 0; i < partitionTabControl.TabCount; i++)
                    {
                        Rectangle r = partitionTabControl.GetTabRect(i);
                        if (r.Contains(e.Location))
                        {
                            partitionTabControl.SelectedIndex = i;
                            // Правило 1: Защита от ошибок при показе меню
                            try
                            {
                                this.contextMenuStrip.Show(this.partitionTabControl, e.Location);
                            }
                            catch (Exception menuEx)
                            {
                                Trace.WriteLine($"[DriveView] Ошибка отображения контекстного меню: {menuEx.Message}");
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveView] Ошибка клика мышью на вкладках: {ex.Message}");
            }
        }

        private void DriveDatabase_OnPartitionAdded(object sender, AddPartitionEventArgs e)
        {
            try
            {
                AddPartition(e.Volume);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveView] Ошибка обработки события добавления раздела: {ex.Message}");
            }
        }

        private void TaskRunner_TaskCompleted(object sender, EventArgs e)
        {
            TaskCompleted?.Invoke(sender, e);
        }

        private void TaskRunner_TaskStarted(object sender, EventArgs e)
        {
            TaskStarted?.Invoke(sender, e);
        }

        public void AddPartition(Volume volume)
        {
            try
            {
                // Правило 2 и 3: Trace и улучшенное логирование
                Trace.WriteLine($"[DriveView] Попытка монтирования раздела: {volume.Name}");
                volume.Mount();
                Trace.WriteLine($"[DriveView] Успешно смонтировано: {volume.Name}");
            }
            catch (Exception e)
            {
                // Правило 2 и 3: Trace вместо Console
                Trace.WriteLine($"[DriveView] Не удалось смонтировать {volume.Name}: {e.Message}");
            }

            try
            {
                var page = new TabPage(volume.Name);
                var partitionDatabase = driveDatabase.AddPartition(volume);
                var partitionView = new PartitionView(taskRunner, volume, partitionDatabase);
                partitionView.Dock = DockStyle.Fill;
                page.Controls.Add(partitionView);
                partitionTabControl.TabPages.Add(page);
                partitionViews.Add(partitionView);

                // Обновляем выбор вкладки, чтобы UI обновился
                SelectedIndexChanged();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveView] Критическая ошибка при создании вкладки раздела {volume.Name}: {ex.Message}");
            }
        }

        public DriveReader GetDrive()
        {
            return drive;
        }

        public List<Volume> GetVolumes()
        {
            try
            {
                return partitionViews.Select(partitionView => partitionView.Volume).ToList();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveView] Ошибка получения списка разделов: {ex.Message}");
                return new List<Volume>();
            }
        }

        public void Save(string path)
        {
            try
            {
                driveDatabase.Save(path);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveView] Ошибка сохранения базы данных: {ex.Message}");
                throw;
            }
        }

        public void LoadFromJson(string path)
        {
            try
            {
                driveDatabase.LoadFromJson(path);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveView] Ошибка загрузки базы данных: {ex.Message}");
                throw;
            }
        }

        private void SelectedIndexChanged()
        {
            try
            {
                // Правило 1: Защита от отсутствия вкладок
                if (partitionTabControl.TabCount > 0 && partitionTabControl.SelectedIndex >= 0 &&
                    partitionTabControl.SelectedIndex < partitionViews.Count)
                {
                    TabSelectionChanged?.Invoke(this, new PartitionSelectedEventArgs()
                    {
                        volume = partitionViews[partitionTabControl.SelectedIndex].Volume
                    });
                }
                else
                {
                    TabSelectionChanged?.Invoke(this, null);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveView] Ошибка обновления выбора вкладки: {ex.Message}");
            }
        }

        private void partitionTabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            SelectedIndexChanged();
        }

        private void toolStripMenuItem1_Click(object sender, System.EventArgs e)
        {
            try
            {
                // Правило 1: Проверка выбранной вкладки перед удалением
                if (partitionTabControl.SelectedIndex < 0 || partitionTabControl.SelectedIndex >= partitionViews.Count)
                {
                    Trace.WriteLine("[DriveView] Попытка удалить раздел, но ничего не выбрано.");
                    return;
                }

                var dialogResult = MessageBox.Show("Are you sure you want to remove this partition?", "Are you sure?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dialogResult == DialogResult.Yes)
                {
                    try
                    {
                        driveDatabase.RemovePartition(partitionTabControl.SelectedIndex);
                    }
                    catch (Exception dbEx)
                    {
                        Trace.WriteLine($"[DriveView] Ошибка удаления из базы данных: {dbEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveView] Критическая ошибка в обработчике удаления: {ex.Message}");
            }
        }
    }
}