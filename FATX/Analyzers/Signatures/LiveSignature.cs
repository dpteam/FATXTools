// Переписано
using FATX.FileSystem;
using System;
using System.Diagnostics; // 1. Добавлено для Trace
using System.Text;

namespace FATX.Analyzers.Signatures
{
    class LiveSignature : FileSignature
    {
        // Лучше сделать readonly, так как константа не меняется
        private readonly string LiveMagic = "LIVE";

        public LiveSignature(Volume volume, long offset)
            : base(volume, offset)
        {
        }

        public override bool Test()
        {
            try
            {
                byte[] magic = ReadBytes(4);

                // Правило 1: Проверка на null/длину (на случай ошибки чтения в базовом классе)
                if (magic == null || magic.Length < 4)
                {
                    return false;
                }

                string magicString = Encoding.ASCII.GetString(magic);

                if (magicString == LiveMagic)
                {
                    // Правило 3: Улучшенное логирование (фиксируем успешный поиск)
                    Trace.WriteLine($"[LiveSignature] Найдена сигнатура 'LIVE' на смещении {Offset}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                // Правило 1 и 3: Отказоустойчивость и детальный лог проблемы
                Trace.WriteLine($"[LiveSignature] Ошибка при проверке сигнатуры (Offset: {Offset}): {ex.Message}");
            }

            return false;
        }

        public override void Parse()
        {
            try
            {
                // Правило 3: Улучшенное логирование начала парсинга
                Trace.WriteLine($"[LiveSignature] Начало парсинга файла {FileName} (Offset: {Offset})");

                // Логика парсинга...
                // does nothing for now
            }
            catch (Exception ex)
            {
                // Правило 1: Отказоустойчивость - не даем программе упасть
                Trace.WriteLine($"[LiveSignature] Критическая ошибка при парсинге файла {FileName} (Offset: {Offset}): {ex.Message}");
            }
        }
    }
}