// Переписано
using FATX.Analyzers;
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.Windows.Forms;

namespace FATXTools.Dialogs
{
    public partial class SettingsDialog : Form
    {
        public FileCarverInterval FileCarverInterval
        {
            get;
            set;
        }

        public string LogFile
        {
            get;
            set;
        }

        public SettingsDialog()
        {
            try
            {
                InitializeComponent();

                // Загрузка настроек
                this.FileCarverInterval = Properties.Settings.Default.FileCarverInterval;
                this.LogFile = Properties.Settings.Default.LogFile;

                if (this.LogFile == null)
                {
                    this.LogFile = "";
                }

                // Выбор значения в выпадающем списке
                // Предполагаем, что порядок элементов в ComboBox совпадает с Enum
                switch (this.FileCarverInterval)
                {
                    case FileCarverInterval.Byte:
                        comboBox1.SelectedIndex = 0;
                        break;
                    case FileCarverInterval.Align:
                        comboBox1.SelectedIndex = 1;
                        break;
                    case FileCarverInterval.Sector:
                        comboBox1.SelectedIndex = 2;
                        break;
                    case FileCarverInterval.Page:
                        comboBox1.SelectedIndex = 3;
                        break;
                    case FileCarverInterval.Cluster:
                        comboBox1.SelectedIndex = 4;
                        break;
                    default:
                        // Правило 1 и 3: Обработка некорректного значения в настройках
                        Trace.WriteLine($"[SettingsDialog] Некорректное значение интервала в настройках: {this.FileCarverInterval}. Установлено по умолчанию: Cluster.");
                        comboBox1.SelectedIndex = 4;
                        this.FileCarverInterval = FileCarverInterval.Cluster;
                        break;
                }

                textBox1.Text = this.LogFile;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[SettingsDialog] Ошибка при инициализации настроек: {ex.Message}");

                // Установка значений по умолчанию при ошибке
                if (comboBox1.Items.Count > 4)
                    comboBox1.SelectedIndex = 4;
            }
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                switch (comboBox1.SelectedIndex)
                {
                    case 0: FileCarverInterval = FileCarverInterval.Byte; break;
                    case 1: FileCarverInterval = FileCarverInterval.Align; break;
                    case 2: FileCarverInterval = FileCarverInterval.Sector; break;
                    case 3: FileCarverInterval = FileCarverInterval.Page; break;
                    case 4: FileCarverInterval = FileCarverInterval.Cluster; break;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[SettingsDialog] Ошибка выбора интервала: {ex.Message}");
            }
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            try
            {
                // Правило 1: Проверка на null перед доступом к TextBox
                if (textBox1 != null)
                {
                    LogFile = textBox1.Text;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[SettingsDialog] Ошибка изменения пути лога: {ex.Message}");
            }
        }
    }
}