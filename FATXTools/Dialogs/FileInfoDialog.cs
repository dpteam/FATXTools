// Переписано
using FATX.FileSystem;
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.Windows.Forms;

namespace FATXTools.Dialogs
{
    public partial class FileInfoDialog : Form
    {
        public FileInfoDialog(Volume volume, DirectoryEntry dirent)
        {
            InitializeComponent();

            try
            {
                // Правило 1: Проверка входных параметров
                if (volume == null || dirent == null)
                {
                    Trace.WriteLine("[FileInfoDialog] Попытка открытия диалога с null объектом (Volume или DirectoryEntry).");
                    throw new ArgumentNullException("Volume или DirectoryEntry");
                }

                listView1.Items.Add("Name").SubItems.Add(dirent.FileName ?? "Unknown");
                listView1.Items.Add("Size in bytes").SubItems.Add(dirent.FileSize.ToString());
                listView1.Items.Add("First Cluster").SubItems.Add(dirent.FirstCluster.ToString());

                // Правило 1: Защита при расчете смещения (может вызвать ошибку при некорректном кластере)
                try
                {
                    var offset = volume.ClusterToPhysicalOffset(dirent.FirstCluster);
                    listView1.Items.Add("First Cluster Offset").SubItems.Add("0x" + offset.ToString("x"));
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[FileInfoDialog] Ошибка расчета смещения для кластера {dirent.FirstCluster}: {ex.Message}");
                    listView1.Items.Add("First Cluster Offset").SubItems.Add("Error (Invalid Cluster)");
                }

                listView1.Items.Add("Attributes").SubItems.Add(FormatAttributes(dirent.FileAttributes));

                // Правило 1: Безопасное получение даты
                // Используем метод AsDateTime() (который мы исправили ранее) вместо ручной сборки,
                // так как он безопаснее обрабатывает некорректные значения
                listView1.Items.Add("Creation Time").SubItems.Add(GetSafeDateTime(dirent.CreationTime).ToString());
                listView1.Items.Add("Last Write Time").SubItems.Add(GetSafeDateTime(dirent.LastWriteTime).ToString());
                listView1.Items.Add("Last Access Time").SubItems.Add(GetSafeDateTime(dirent.LastAccessTime).ToString());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileInfoDialog] Критическая ошибка при инициализации диалога: {ex.Message}");
                MessageBox.Show($"Не удалось отобразить информацию о файле.\nОшибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private DateTime GetSafeDateTime(TimeStamp timeStamp)
        {
            try
            {
                return timeStamp.AsDateTime();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileInfoDialog] Ошибка конвертации времени: {ex.Message}");
                return DateTime.MinValue; // Возвращаем минимальную дату при ошибке
            }
        }

        private string FormatAttributes(FileAttribute attributes)
        {
            string attrStr = "";

            // ИСПРАВЛЕНИЕ: Убран else if.
            // В оригинале, если файл был Hidden, он не мог быть Archive.
            // Теперь отображаем все актуальные флаги.
            if (attributes.HasFlag(FileAttribute.Archive))
            {
                attrStr += "A";
            }
            if (attributes.HasFlag(FileAttribute.Directory))
            {
                attrStr += "D";
            }
            if (attributes.HasFlag(FileAttribute.Hidden))
            {
                attrStr += "H";
            }
            if (attributes.HasFlag(FileAttribute.ReadOnly))
            {
                attrStr += "R";
            }
            if (attributes.HasFlag(FileAttribute.System))
            {
                attrStr += "S";
            }

            return string.IsNullOrEmpty(attrStr) ? "-" : attrStr;
        }
    }
}