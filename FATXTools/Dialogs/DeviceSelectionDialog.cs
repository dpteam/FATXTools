// Переписано
using FATXTools.Utilities;
using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.IO;
using System.Windows.Forms;

namespace FATXTools.Dialogs
{
    public partial class DeviceSelectionDialog : Form
    {
        private string selectedDevice;

        public DeviceSelectionDialog()
        {
            InitializeComponent();

            Trace.WriteLine("[DeviceSelectionDialog] Сканирование физических дисков...");

            for (int i = 0; i < 24; i++)
            {
                try
                {
                    string deviceName = String.Format(@"\\.\PhysicalDrive{0}", i);

                    // Пытаемся открыть устройство
                    SafeFileHandle handle = WinApi.CreateFile(
                        deviceName,
                        FileAccess.Read,
                        FileShare.None, // Note: Если диск занят, IsInvalid будет true
                        IntPtr.Zero,
                        FileMode.Open,
                        0,
                        IntPtr.Zero
                        );

                    if (handle.IsInvalid)
                    {
                        // Диск не существует или недоступен (например, занят системой)
                        continue;
                    }

                    // Получаем размер
                    long capacity = WinApi.GetDiskCapactity(handle);

                    var deviceItem = listView1.Items.Add(deviceName);

                    // Правило 1: Проверка валидности размера (-1 = ошибка из обновленного WinApi)
                    if (capacity == -1)
                    {
                        deviceItem.SubItems.Add("Unknown Size");
                    }
                    else
                    {
                        deviceItem.SubItems.Add(FormatSize(capacity));
                    }

                    deviceItem.ImageIndex = 0;
                    deviceItem.StateImageIndex = 0;

                    handle.Close();
                }
                catch (Exception ex)
                {
                    // Правило 1 и 3: Логируем ошибку доступа к конкретному диску, но продолжаем сканирование
                    Trace.WriteLine($"[DeviceSelectionDialog] Ошибка при проверке диска {i}: {ex.Message}");
                }
            }

            Trace.WriteLine("[DeviceSelectionDialog] Сканирование завершено.");
        }

        static readonly string[] suffixes = { "Bytes", "KB", "MB", "GB", "TB", "PB" };
        public static string FormatSize(Int64 bytes)
        {
            // Правило 1: Обработка некорректных значений (например, -1)
            if (bytes < 0)
            {
                return "Unknown";
            }

            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1}{1}", number, suffixes[counter]);
        }

        public string SelectedDevice
        {
            get { return selectedDevice; }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            // Правило 1: Проверка на наличие выбора перед обращением к индексу
            if (listView1.SelectedItems.Count > 0)
            {
                this.selectedDevice = listView1.SelectedItems[0].Text;
                this.DialogResult = DialogResult.OK;
                Trace.WriteLine($"[DeviceSelectionDialog] Выбрано устройство: {this.selectedDevice}");
            }
            else
            {
                MessageBox.Show("Пожалуйста, выберите устройство из списка.", "Устройство не выбрано", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
        }
    }
}