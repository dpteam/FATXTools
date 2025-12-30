// Переписано
using FATX.FileSystem;
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.IO;
using System.Text;

namespace FATX.Analyzers.Signatures
{
    class XEXSignature : FileSignature
    {
        private const string XEX1Signature = "XEX1";
        private const string XEX2Signature = "XEX2";

        public XEXSignature(Volume volume, long offset)
            : base(volume, offset)
        {
        }

        public override bool Test()
        {
            try
            {
                byte[] magic = ReadBytes(4);

                // Правило 1: Проверка на null и длину перед сравнением
                if (magic != null && Encoding.ASCII.GetString(magic) == XEX2Signature)
                {
                    // Правило 3: Улучшенное логирование (успех)
                    Trace.WriteLine($"[XEXSignature] Найдена сигнатура XEX2 на смещении {Offset}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                // Правило 1 и 3: Логирование ошибок проверки
                Trace.WriteLine($"[XEXSignature] Ошибка при проверке сигнатуры (Offset: {Offset}): {ex.Message}");
            }

            return false;
        }

        public override void Parse()
        {
            try
            {
                // XEX файлы Xbox 360 обычно имеют Little Endian порядок байт
                SetByteOrder(ByteOrder.Little);

                // Правило 3: Улучшенное логирование
                Trace.WriteLine($"[XEXSignature] Начало парсинга XEX-файла {FileName}...");

                Seek(0x10);
                var securityOffset = ReadUInt32();
                var headerCount = ReadUInt32();

                uint fileNameOffset = 0;

                // Правило 1: Защита от DoS (бесконечного цикла) при поврежденном заголовке
                // Ограничиваем количество заголовков разумным числом
                const uint MaxHeaders = 10000;
                if (headerCount > MaxHeaders)
                {
                    Trace.WriteLine($"[XEXSignature] Внимание: HeaderCount ({headerCount}) слишком велик. Ограничиваем {MaxHeaders}.");
                    headerCount = MaxHeaders;
                }

                for (int i = 0; i < headerCount; i++)
                {
                    var xid = ReadUInt32();
                    if (xid == 0x000183ff)
                    {
                        fileNameOffset = ReadUInt32();
                    }
                    else
                    {
                        ReadUInt32();
                    }
                }

                // Правило 1: Проверка валидности смещения перед Seek
                if (securityOffset == 0 || securityOffset > uint.MaxValue)
                {
                    Trace.WriteLine($"[XEXSignature] Некорректное смещение секьюрити (0x{securityOffset:X}). Чтение размера пропущено.");
                }
                else
                {
                    Seek(securityOffset + 4);
                    this.FileSize = ReadUInt32();
                    Trace.WriteLine($"[XEXSignature] Размер файла определен: {this.FileSize} байт");
                }

                if (fileNameOffset != 0)
                {
                    // Правило 1: Проверка валидности смещения имени
                    // Простая эвристика: имя обычно лежит в пределах первых 16 МБ заголовка (запас для больших файлов)
                    if (fileNameOffset < 0x1000000)
                    {
                        Seek(fileNameOffset + 4);
                        var name = ReadCString();

                        if (!string.IsNullOrEmpty(name))
                        {
                            this.FileName = Path.ChangeExtension(name, ".xex");
                            Trace.WriteLine($"[XEXSignature] Извлечено имя файла: {this.FileName}");
                        }
                    }
                    else
                    {
                        Trace.WriteLine($"[XEXSignature] Смещение имени файла (0x{fileNameOffset:X}) похоже на ошибку или выходит за разумные пределы.");
                    }
                }
            }
            catch (Exception ex)
            {
                // Правило 1: Отказоустойчивость при парсинге
                Trace.WriteLine($"[XEXSignature] Критическая ошибка при парсинге XEX-файла {FileName}: {ex.Message}");
            }
        }
    }
}