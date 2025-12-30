// Переписано
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.Globalization; // Для NumberStyles.HexNumber
using System.Windows.Forms;

namespace FATXTools.Dialogs
{
    public partial class NewPartitionDialog : Form
    {
        public NewPartitionDialog()
        {
            InitializeComponent();
        }

        public string PartitionName
        {
            get => textBox1.Text;
        }

        public long PartitionOffset
        {
            get
            {
                try
                {
                    string text = textBox2.Text;

                    // Правило 1: Защита от пустых строк
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        Trace.WriteLine("[NewPartitionDialog] Ввод: поле Offset пустое.");
                        throw new ArgumentException("Смещение (Offset) не может быть пустым.");
                    }

                    return long.Parse(text, NumberStyles.HexNumber);
                }
                catch (Exception ex)
                {
                    // Правило 3: Улучшенное логирование (записываем что ввел пользователь)
                    Trace.WriteLine($"[NewPartitionDialog] Ошибка конвертации Offset (введено: '{textBox2.Text}'): {ex.Message}");

                    // Пробрасываем исключение, чтобы вызывающий код (в MainWindow) мог показать ошибку пользователю,
                    // но при этом она уже записана в лог
                    throw new FormatException($"Неверный формат смещения: '{textBox2.Text}'. Ожидается шестнадцатеричное число.", ex);
                }
            }
        }

        public long PartitionLength
        {
            get
            {
                try
                {
                    string text = textBox3.Text;

                    // Правило 1: Защита от пустых строк
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        Trace.WriteLine("[NewPartitionDialog] Ввод: поле Length пустое.");
                        throw new ArgumentException("Длина (Length) не может быть пустой.");
                    }

                    return long.Parse(text, NumberStyles.HexNumber);
                }
                catch (Exception ex)
                {
                    // Правило 3: Улучшенное логирование
                    Trace.WriteLine($"[NewPartitionDialog] Ошибка конвертации Length (введено: '{textBox3.Text}'): {ex.Message}");
                    throw new FormatException($"Неверный формат длины: '{textBox3.Text}'. Ожидается шестнадцатеричное число.", ex);
                }
            }
        }
    }
}