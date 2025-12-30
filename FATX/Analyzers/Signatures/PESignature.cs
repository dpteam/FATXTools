// Переписано
using FATX.FileSystem;
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.Linq;

namespace FATX.Analyzers.Signatures
{
    class PESignature : FileSignature
    {
        // Лучше сделать static readonly
        private static readonly byte[] PEMagic = new byte[] { 0x4D, 0x5A, 0x90, 0x00 };

        public PESignature(Volume volume, long offset)
            : base(volume, offset)
        {
        }

        public override bool Test()
        {
            try
            {
                byte[] magic = ReadBytes(4);

                // Правило 1: Проверка на null и длину перед сравнением
                if (magic != null && magic.SequenceEqual(PEMagic))
                {
                    // Правило 3: Улучшенное логирование
                    Trace.WriteLine($"[PESignature] Найден MZ-заголовок на смещении {Offset}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                // Правило 1 и 3: Логирование ошибки
                Trace.WriteLine($"[PESignature] Ошибка при проверке сигнатуры (Offset: {Offset}): {ex.Message}");
            }

            return false;
        }

        public override void Parse()
        {
            try
            {
                Trace.WriteLine($"[PESignature] Начало парсинга PE-файла {FileName}...");

                SetByteOrder(ByteOrder.Little);

                Seek(0x3C);
                var lfanew = ReadUInt32();
                Trace.WriteLine($"[PESignature] Смещение PE-заголовка (e_lfanew): 0x{lfanew:X8}");

                Seek(lfanew);
                var sign = ReadUInt32();

                // Проверка валидности PE-сигнатуры
                if (sign != 0x00004550)
                {
                    Trace.WriteLine($"[PESignature] Неверная PE-сигнатура (0x{sign:X8}) для файла {FileName}. Парсинг остановлен.");
                    return;
                }

                Seek(lfanew + 0x6);
                var nsec = ReadUInt16();
                Trace.WriteLine($"[PESignature] Количество секций: {nsec}");

                // Защита от некорректного количества секций (nsec = 0 или переполнение)
                if (nsec > 0)
                {
                    // Используем long для вычислений, чтобы избежать переполнения при больших значениях nsec
                    long lastSecOff = (long)(lfanew + 0xF8) + ((long)(nsec - 1) * 0x28);

                    Seek(lastSecOff + 0x10);
                    var secLen = ReadUInt32();

                    Seek(lastSecOff + 0x14);
                    var secOff = ReadUInt32();

                    this.FileSize = secOff + secLen;

                    Trace.WriteLine($"[PESignature] Размер файла {FileName} вычислен: {this.FileSize} байт (LastSection Offset: 0x{secOff:X8}, Size: 0x{secLen:X8})");
                }
                else
                {
                    Trace.WriteLine($"[PESignature] Внимание: Файл {FileName} не содержит секций (nsec = 0). Размер не может быть вычислен стандартным методом.");
                }
            }
            catch (Exception ex)
            {
                // Правило 1: Отказоустойчивость при парсинге
                Trace.WriteLine($"[PESignature] Критическая ошибка при парсинге PE-файла {FileName}: {ex.Message}");
            }
        }
    }
}