// Переписано
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.IO;
using System.Windows.Forms;
using FATXTools.Forms;

namespace FATXTools
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // --- Инициализация Trace ---
            try
            {
                // Создаем файл лога в папке с программой
                string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fatx_log.txt");

                // Добавляем слушателя для записи в файл
                Trace.Listeners.Add(new TextWriterTraceListener(logFilePath));

                // Добавляем вывод в консоль/Debug (удобно при разработке)
                Trace.Listeners.Add(new ConsoleTraceListener());

                // Включаем автоматический сброс буфера после каждой записи (важно для сохранения логов при падении)
                Trace.AutoFlush = true;

                Trace.WriteLine("========================================");
                Trace.WriteLine($"[Program] Приложение запущено. Дата: {DateTime.Now}");
                Trace.WriteLine("========================================");
            }
            catch (Exception initEx)
            {
                // Если не удалось создать файл лога, сообщаем пользователю, но не падаем
                MessageBox.Show($"Не удалось инициализировать систему логирования: {initEx.Message}");
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Запуск главной формы
                Application.Run(new MainWindow());
            }
            catch (Exception ex)
            {
                // Правило 1: Глобальный перехват критических ошибок UI потока
                Trace.WriteLine("========================================");
                Trace.WriteLine("[Program] КРИТИЧЕСКАЯ ОШИБКА В ГЛАВНОМ ПОТОКЕ");
                Trace.WriteLine($"Сообщение: {ex.Message}");
                Trace.WriteLine($"Стек вызовов: {ex.StackTrace}");
                Trace.WriteLine("========================================");

                MessageBox.Show($"Произошла критическая ошибка и приложение будет закрыто.\n\nОшибка: {ex.Message}\nПодробности см. в файле лога.", "Критическая ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // Корректное завершение работы логера
                Trace.WriteLine("[Program] Приложение завершает работу.");
                Trace.Close();
            }
        }
    }
}