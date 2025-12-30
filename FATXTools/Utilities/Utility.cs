// Переписано
using System;
using System.IO;
using System.Diagnostics; // 1. Подключаем Trace

namespace FATXTools.Utilities
{
    public static class Utility
    {
        public static string FormatBytes(long bytes)
        {
            // Правило 1: Проверка на некорректные значения (например, -1 при ошибке чтения)
            if (bytes < 0)
            {
                return "Unknown Size";
            }

            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }

            return String.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
        }

        public static string UniqueFileName(string path, int maxAttempts = 256)
        {
            // Правило 1: Проверка входных данных
            if (string.IsNullOrEmpty(path))
            {
                Trace.WriteLine("[Utility] Метод UniqueFileName получил пустой путь.");
                return null;
            }

            try
            {
                if (!File.Exists(path))
                {
                    return path;
                }

                var fileDirectory = Path.GetDirectoryName(path);
                var fileName = Path.GetFileName(path);
                var fileBaseName = Path.GetFileNameWithoutExtension(fileName);
                var fileExt = Path.GetExtension(fileName);

                for (var i = 1; i <= maxAttempts; i++)
                {
                    // Правило 1: Используем Path.Combine для надежности
                    var testPath = Path.Combine(fileDirectory, $"{fileBaseName} ({i}){fileExt}");

                    if (!File.Exists(testPath))
                    {
                        return testPath;
                    }
                }

                // Правило 3: Улучшенное логирование (если лимит попыток превышен)
                Trace.WriteLine($"[Utility] Не удалось найти уникальное имя для файла {path} за {maxAttempts} попыток.");
                return null;
            }
            catch (Exception ex)
            {
                // Правило 1: Отказоустойчивость (ловим ошибки доступа, недопустимые символы и т.д.)
                Trace.WriteLine($"[Utility] Ошибка при генерации уникального имени для пути '{path}': {ex.Message}");
                return null;
            }
        }
    }
}