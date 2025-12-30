// Переписано
using FATX;
using FATX.FileSystem;
using System;
using System.Collections.Generic;
using System.Diagnostics; // 1. Подключаем Trace
using System.Windows.Forms;

namespace FATXTools.Dialogs
{
    public partial class PartitionManagerDialog : Form
    {
        private List<Volume> volumes;
        private DriveReader reader;

        public PartitionManagerDialog()
        {
            InitializeComponent();
        }

        public PartitionManagerDialog(DriveReader reader, List<Volume> volumes)
        {
            InitializeComponent();

            this.reader = reader;
            this.volumes = volumes;

            PopulateList(volumes);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                NewPartitionDialog dialog = new NewPartitionDialog();
                var dialogResult = dialog.ShowDialog();

                if (dialogResult == DialogResult.OK)
                {
                    // Правило 3: Улучшенное логирование
                    Trace.WriteLine($"[PartitionManagerDialog] Попытка добавить раздел: {dialog.PartitionName}");

                    // Создаем объект Volume. Конструктор обычно не вызывает Mount, 
                    // но свойства dialog могут выбросить исключение, если ввод был некорректным
                    var newVolume = new Volume(this.reader, dialog.PartitionName, dialog.PartitionOffset, dialog.PartitionLength);

                    volumes.Add(newVolume);

                    // Обновляем UI
                    PopulateList(volumes);

                    Trace.WriteLine("[PartitionManagerDialog] Раздел успешно добавлен в список.");
                }
            }
            catch (Exception ex)
            {
                // Правило 1 и 3: Логируем ошибку и показываем пользователю
                Trace.WriteLine($"[PartitionManagerDialog] Ошибка при добавлении раздела: {ex.Message}");
                MessageBox.Show($"Не удалось добавить раздел.\n\nОшибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PopulateList(List<Volume> volumes)
        {
            try
            {
                listView1.Items.Clear();

                // Правило 1: Проверка на null списка
                if (volumes == null) return;

                foreach (var volume in volumes)
                {
                    try
                    {
                        // Правило 1: Проверка элемента списка
                        if (volume == null) continue;

                        // Свойства Name или Offset могут теоретически вызывать ошибки в редких случаях
                        ListViewItem item = new ListViewItem(volume.Name);
                        item.SubItems.Add("0x" + volume.Offset.ToString("X"));
                        item.SubItems.Add("0x" + volume.Length.ToString("X"));

                        listView1.Items.Add(item);
                    }
                    catch (Exception ex)
                    {
                        // Правило 1: Если один раздел вызывает ошибку при отрисовке, пропускаем его
                        Trace.WriteLine($"[PartitionManagerDialog] Ошибка при отрисовке элемента списка (раздел не отображен): {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PartitionManagerDialog] Критическая ошибка при обновлении списка разделов: {ex.Message}");
            }
        }
    }
}