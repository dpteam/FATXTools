// Переписано
using FATX.FileSystem;
using FATXTools.Controls;
using FATXTools.Dialogs;
using FATXTools.DiskTypes;
using FATXTools.Utilities;
using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace FATXTools.Forms
{
    public partial class MainWindow : Form
    {
        private DriveView driveView;

        private const string ApplicationTitle = "FATX-Recover";

        public MainWindow()
        {
            InitializeComponent();

            this.Text = ApplicationTitle;

            DisableDatabaseOptions();

            // Перенаправляем вывод в наш TextBox
            // Обратите внимание: Trace также будет писать в этот TextBox через переопределение в LogWriter
            Console.SetOut(new LogWriter(this.textBox1));

            Trace.WriteLine("--------------------------------");
            Trace.WriteLine("FATX-Tools v0.3");
            Trace.WriteLine("--------------------------------");
        }

        public class LogWriter : TextWriter
        {
            private TextBox textBox;
            private delegate void SafeCallDelegate(string text);

            public LogWriter(TextBox textBox)
            {
                this.textBox = textBox;
            }

            public override void Write(char value)
            {
                try
                {
                    if (textBox.IsDisposed) return;
                    textBox.AppendText(value.ToString());
                }
                catch { /* Игнорируем ошибки UI при закрытии */ }
            }

            public override void Write(string value)
            {
                try
                {
                    if (textBox.IsDisposed) return;
                    textBox.AppendText(value);
                }
                catch { }
            }

            public override void WriteLine()
            {
                try
                {
                    if (textBox.IsDisposed) return;
                    textBox.AppendText(NewLine);
                }
                catch { }
            }

            public override void WriteLine(string value)
            {
                // 1. Запись в Trace (файловый лог)
                Trace.WriteLine(value);

                // 2. Вывод в UI (текстбокс)
                if (textBox.IsDisposed) return;

                if (textBox.InvokeRequired)
                {
                    var d = new SafeCallDelegate(WriteLine);
                    try
                    {
                        textBox.BeginInvoke(d, new object[] { value });
                    }
                    catch { }
                }
                else
                {
                    textBox.AppendText(value + NewLine);
                }
            }

            public override Encoding Encoding
            {
                get { return Encoding.ASCII; }
            }
        }

        private void CreateNewDriveView(string path)
        {
            this.Text = $"{ApplicationTitle} - {Path.GetFileName(path)}";

            // Destroy without exception
            if (driveView != null)
            {
                splitContainer1.Panel1.Controls.Remove(driveView);
                driveView.Dispose();
            }

            // Create a new view for this drive
            try
            {
                driveView = new DriveView();
                driveView.Dock = DockStyle.Fill;
                driveView.TabSelectionChanged += DriveView_TabSelectionChanged;
                driveView.TaskStarted += DriveView_TaskStarted;
                driveView.TaskCompleted += DriveView_TaskCompleted;

                // Add the view to the panel
                splitContainer1.Panel1.Controls.Add(driveView);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MainWindow] Ошибка при создании интерфейса диска: {ex.Message}");
            }
        }

        private void DriveView_TaskCompleted(object sender, EventArgs e)
        {
            EnableOpenOptions();
            EnableDatabaseOptions();
        }

        private void DriveView_TaskStarted(object sender, EventArgs e)
        {
            DisableOpenOptions();
            DisableDatabaseOptions();
        }

        private void DriveView_TabSelectionChanged(object sender, PartitionSelectedEventArgs e)
        {
            try
            {
                statusStrip1.Items.Clear();

                if (e == null || e.volume == null)
                {
                    return;
                }

                var volume = e.volume;

                if (volume.Mounted)
                {
                    var usedSpace = volume.GetUsedSpace();
                    var freeSpace = volume.GetFreeSpace();
                    var totalSpace = volume.GetTotalSpace();

                    statusStrip1.Items.Add($"Volume Offset: 0x{volume.Offset:X}");
                    statusStrip1.Items.Add($"Volume Length: 0x{volume.Length:X}");
                    statusStrip1.Items.Add($"Used Space: {Utility.FormatBytes(usedSpace)}");
                    statusStrip1.Items.Add($"Free Space: {Utility.FormatBytes(freeSpace)}");
                    statusStrip1.Items.Add($"Total Space: {Utility.FormatBytes(totalSpace)}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MainWindow] Ошибка обновления статуса раздела: {ex.Message}");
            }
        }

        private void EnableDatabaseOptions()
        {
            loadToolStripMenuItem.Enabled = true;
            saveToolStripMenuItem.Enabled = true;
            addPartitionToolStripMenuItem.Enabled = true;
        }

        private void DisableDatabaseOptions()
        {
            loadToolStripMenuItem.Enabled = false;
            saveToolStripMenuItem.Enabled = false;
            addPartitionToolStripMenuItem.Enabled = false;
        }

        private void EnableOpenOptions()
        {
            openImageToolStripMenuItem.Enabled = true;
            openDeviceToolStripMenuItem.Enabled = true;
        }

        private void DisableOpenOptions()
        {
            openImageToolStripMenuItem.Enabled = false;
            openDeviceToolStripMenuItem.Enabled = false;
        }

        private void OpenDiskImage(string path)
        {
            try
            {
                CreateNewDriveView(path);

                string fileName = Path.GetFileName(path);
                Trace.WriteLine($"[MainWindow] Попытка открытия образа: {fileName}");

                RawImage rawImage = new RawImage(path);
                driveView.AddDrive(fileName, rawImage);

                Trace.WriteLine($"[MainWindow] Образ '{fileName}' успешно открыт.");
                EnableDatabaseOptions();
            }
            catch (IOException ioEx)
            {
                Trace.WriteLine($"[MainWindow] Ошибка ввода-вывода при открытии образа: {ioEx.Message}");
                MessageBox.Show($"Не удалось открыть образ файла: {ioEx.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MainWindow] Критическая ошибка при открытии образа: {ex.Message}");
                MessageBox.Show($"Не удалось открыть образ: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenDisk(string device)
        {
            try
            {
                CreateNewDriveView(device);

                Trace.WriteLine($"[MainWindow] Попытка открытия физического устройства: {device}");

                SafeFileHandle handle = WinApi.CreateFile(device,
                           FileAccess.Read,
                           FileShare.ReadWrite, // Разрешаем чтение/запись для других процессов (Shared)
                           IntPtr.Zero,
                           FileMode.Open,
                           0,
                           IntPtr.Zero);

                if (handle.IsInvalid)
                {
                    int errorCode = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    string msg = $"Не удалось получить дескриптор устройства {device}. Код ошибки Windows: {errorCode}";
                    Trace.WriteLine($"[MainWindow] {msg}");
                    MessageBox.Show(msg, "Ошибка доступа", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                long length = WinApi.GetDiskCapactity(handle);
                if (length == -1)
                {
                    Trace.WriteLine($"[MainWindow] Не удалось получить размер диска {device}.");
                    MessageBox.Show("Не удалось получить информацию о размере диска.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    // Продолжаем, может быть размер указан вручную или не критичен для чтения секторов
                }

                long sectorLength = WinApi.GetSectorSize(handle);
                if (sectorLength == -1)
                {
                    Trace.WriteLine($"[MainWindow] Не удалось получить размер сектора для {device}. Используем значение по умолчанию (0x200).");
                    sectorLength = 0x200;
                }

                PhysicalDisk drive = new PhysicalDisk(handle, length, sectorLength);
                driveView.AddDrive(device, drive);

                Trace.WriteLine($"[MainWindow] Устройство {device} успешно открыто.");
                EnableDatabaseOptions();
            }
            catch (UnauthorizedAccessException)
            {
                Trace.WriteLine($"[MainWindow] Доступ запрещен к устройству {device}. Требуются права администратора.");
                MessageBox.Show("Не хватает прав администратора для чтения устройства.", "Ошибка прав", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MainWindow] Критическая ошибка при открытии устройства: {ex.Message}");
                MessageBox.Show($"Произошла ошибка при работе с диском: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void openImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                OpenFileDialog ofd = new OpenFileDialog();
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    OpenDiskImage(ofd.FileName);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MainWindow] Ошибка диалога выбора файла: {ex.Message}");
            }
        }

        private void openDeviceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                var isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                    .IsInRole(WindowsBuiltInRole.Administrator);

                if (!isAdmin)
                {
                    Trace.WriteLine("[MainWindow] Попытка открыть устройство без прав администратора.");
                    MessageBox.Show("Для чтения с физических дисков необходимо запустить программу от имени администратора.",
                                    "Недостаточно прав", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                DeviceSelectionDialog ds = new DeviceSelectionDialog();
                if (ds.ShowDialog() == DialogResult.OK)
                {
                    OpenDisk(ds.SelectedDevice);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MainWindow] Ошибка выбора устройства: {ex.Message}");
            }
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                "Developed by aerosoul94\n" +
                "Source code: https://github.com/aerosoul94/FATXTools\n" +
                "Please report any bugs\n",
                "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void MainWindow_DragEnter(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    e.Effect = (files.Length > 1) ? DragDropEffects.None : DragDropEffects.Link;
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                }
            }
            catch { e.Effect = DragDropEffects.None; }
        }

        private void MainWindow_DragDrop(object sender, DragEventArgs e)
        {
            try
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 1)
                {
                    MessageBox.Show("Можно перетащить только один файл!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else if (files.Length == 1)
                {
                    OpenDiskImage(files[0]);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MainWindow] Ошибка Drag & Drop: {ex.Message}");
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void managePartitionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Unused in current version, kept for structure
        }

        private void addPartitionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (driveView == null) return;

                NewPartitionDialog partitionDialog = new NewPartitionDialog();
                var dialogResult = partitionDialog.ShowDialog();
                if (dialogResult == DialogResult.OK)
                {
                    driveView.AddPartition(new Volume(driveView.GetDrive(),
                        partitionDialog.PartitionName,
                        partitionDialog.PartitionOffset,
                        partitionDialog.PartitionLength));

                    Trace.WriteLine($"[MainWindow] Добавлен новый раздел: {partitionDialog.PartitionName}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MainWindow] Ошибка добавления раздела: {ex.Message}");
                MessageBox.Show($"Не удалось добавить раздел: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void settingsToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            try
            {
                SettingsDialog settings = new SettingsDialog();
                if (settings.ShowDialog() == DialogResult.OK)
                {
                    Properties.Settings.Default.FileCarverInterval = settings.FileCarverInterval;
                    Properties.Settings.Default.LogFile = settings.LogFile;
                    Properties.Settings.Default.Save();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MainWindow] Ошибка сохранения настроек: {ex.Message}");
            }
        }

        private void saveToJSONToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog()
                {
                    Filter = "JSON File (*.json)|*.json"
                };

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    driveView.Save(saveFileDialog.FileName);
                    Trace.WriteLine($"[MainWindow] База данных сохранена: {saveFileDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MainWindow] Ошибка сохранения JSON: {ex.Message}");
                MessageBox.Show($"Не удалось сохранить файл: {ex.Message}", "Ошибка сохранения", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void loadFromJSONToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog()
                {
                    Filter = "JSON File (*.json)|*.json"
                };

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    var dialogResult = MessageBox.Show($"Загрузка базы данных перезапишет текущий прогресс анализа.\n"
                        + $"Вы уверены, что хотите загрузить \'{Path.GetFileName(openFileDialog.FileName)}\'?",
                        "Загрузка файла", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (dialogResult == DialogResult.Yes)
                    {
                        driveView.LoadFromJson(openFileDialog.FileName);
                        Trace.WriteLine($"[MainWindow] База данных загружена: {openFileDialog.FileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MainWindow] Ошибка загрузки JSON: {ex.Message}");
                MessageBox.Show($"Не удалось загрузить файл: {ex.Message}", "Ошибка загрузки", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (driveView != null)
                {
                    var dialogResult = MessageBox.Show("Сохранить прогресс перед закрытием?", "Сохранение", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

                    if (dialogResult == DialogResult.Yes)
                    {
                        SaveFileDialog saveFileDialog = new SaveFileDialog()
                        {
                            Filter = "JSON File (*.json)|*.json"
                        };

                        if (saveFileDialog.ShowDialog() == DialogResult.OK)
                        {
                            driveView.Save(saveFileDialog.FileName);
                            Trace.WriteLine($"[MainWindow] Прогресс сохранен перед закрытием: {saveFileDialog.FileName}");
                        }
                        else
                        {
                            // Пользователь отменил диалог сохранения, но нажал Yes в главном вопросе?
                            // Обычно это значит "Сохранить", если отменен конкретный файл - игнорируем сохранение
                        }
                    }
                    else if (dialogResult == DialogResult.Cancel)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MainWindow] Ошибка при закрытии формы: {ex.Message}");
                // Не блокируем закрытие при ошибке сохранения
            }
        }
    }
}