// Переписано
using FATX.FileSystem;
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.Text;

namespace FATX.Analyzers.Signatures
{
    class PDBSignature : FileSignature
    {
        private const string PDBMagic = "Microsoft C/C++ MSF 7.00\r\n\x1A\x44\x53\0\0\0";

        public PDBSignature(Volume volume, long offset)
            : base(volume, offset)
        {
        }

        public override bool Test()
        {
            try
            {
                byte[] magic = ReadBytes(0x20);

                // Правило 1: Проверка данных перед использованием (защита от ошибок чтения в базовом классе)
                if (magic == null || magic.Length < PDBMagic.Length)
                {
                    return false;
                }

                string magicString = Encoding.ASCII.GetString(magic);

                if (magicString == PDBMagic)
                {
                    // Правило 3: Улучшенное логирование (успех)
                    Trace.WriteLine($"[PDBSignature] Найдена сигнатура PDB (Microsoft C/C++ MSF 7.00) на смещении {Offset}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                // Правило 1 и 3: Логирование ошибок проверки
                Trace.WriteLine($"[PDBSignature] Ошибка при проверке сигнатуры (Offset: {Offset}): {ex.Message}");
            }

            return false;
        }

        public override void Parse()
        {
            try
            {
                // Правило 3: Улучшенное логирование (начало парсинга)
                Trace.WriteLine($"[PDBSignature] Начало парсинга файла {FileName}...");

                SetByteOrder(ByteOrder.Little);

                Seek(0x20);
                var blockSize = ReadUInt32();

                Seek(0x28);
                var numBlocks = ReadUInt32();

                // Вычисляем размер файла
                this.FileSize = blockSize * numBlocks;

                // Правило 3: Логирование полученных метаданных
                Trace.WriteLine($"[PDBSignature] Размер файла {FileName} вычислен: {this.FileSize} байт (Block: {blockSize}, Count: {numBlocks})");
            }
            catch (Exception ex)
            {
                // Правило 1: Отказоустойчивость при парсинге
                Trace.WriteLine($"[PDBSignature] Критическая ошибка при парсинге файла {FileName}: {ex.Message}");
            }
        }
    }
}