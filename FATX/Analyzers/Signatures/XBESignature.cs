// Переписано
using FATX.FileSystem;
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.IO;
using System.Text;

namespace FATX.Analyzers.Signatures
{
    class XBESignature : FileSignature
    {
        private const string XBEMagic = "XBEH";

        public XBESignature(Volume volume, long offset)
            : base(volume, offset)
        {
        }

        public override bool Test()
        {
            try
            {
                byte[] magic = this.ReadBytes(4);

                // Правило 1: Проверка на null перед сравнением
                if (magic != null && Encoding.ASCII.GetString(magic) == XBEMagic)
                {
                    // Правило 3: Улучшенное логирование
                    Trace.WriteLine($"[XBESignature] Найдена сигнатура 'XBEH' на смещении {Offset}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                // Правило 1 и 3: Логирование ошибок
                Trace.WriteLine($"[XBESignature] Ошибка при проверке сигнатуры (Offset: {Offset}): {ex.Message}");
            }

            return false;
        }

        public override void Parse()
        {
            try
            {
                // Правило 3: Улучшенное логирование
                Trace.WriteLine($"[XBESignature] Начало парсинга XBE-файла {FileName}...");

                // XBE использует little-endian
                SetByteOrder(ByteOrder.Little);

                Seek(0x104);
                var baseAddress = ReadUInt32();

                Seek(0x10C);
                this.FileSize = ReadUInt32();

                Trace.WriteLine($"[XBESignature] Базовый адрес: 0x{baseAddress:X8}, Размер файла: {this.FileSize}");

                Seek(0x150);
                var debugFileNameOffset = ReadUInt32();

                // Правило 1: Проверка валидности смещений перед вычислением
                if (debugFileNameOffset != 0 && debugFileNameOffset >= baseAddress)
                {
                    // Вычисляем RVA (Relative Virtual Address) для строки отладки
                    long stringOffset = debugFileNameOffset - baseAddress;

                    Seek(stringOffset);
                    var debugFileName = ReadCString();

                    if (!string.IsNullOrEmpty(debugFileName))
                    {
                        // Извлекаем только имя файла без пути, чтобы избежать проблем с путями
                        string safeName = Path.GetFileName(debugFileName);
                        this.FileName = Path.ChangeExtension(safeName, ".xbe");

                        Trace.WriteLine($"[XBESignature] Имя файла обновлено на: {this.FileName} (исходная строка отладки: {debugFileName})");
                    }
                    else
                    {
                        Trace.WriteLine($"[XBESignature] Строка отладки пуста или не может быть прочитана. Используется стандартное имя: {this.FileName}");
                    }
                }
                else
                {
                    Trace.WriteLine($"[XBESignature] Некорректные смещения для имени файла (DebugOffset: 0x{debugFileNameOffset:X}, Base: 0x{baseAddress:X}). Имя не изменено.");
                }
            }
            catch (Exception ex)
            {
                // Правило 1: Отказоустойчивость при парсинге
                Trace.WriteLine($"[XBESignature] Критическая ошибка при парсинге XBE-файла {FileName}: {ex.Message}");
            }
        }
    }
}