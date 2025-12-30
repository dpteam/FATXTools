// Переписано
using FATX.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics; // 1. Подключаем Trace

namespace FATX
{
    public class DriveReader : EndianReader
    {
        private List<Volume> _partitions = new List<Volume>();

        public DriveReader(Stream stream)
            : base(stream)
        {
        }

        public void Initialize()
        {
            try
            {
                Seek(0);

                // Проверка на Xbox 360 Memory Unit
                try
                {
                    if (ReadUInt64() == 0x534F44534D9058EB)
                    {
                        Trace.WriteLine("[DriveReader] Обнаружен образ Xbox 360 Memory Unit.");

                        ByteOrder = ByteOrder.Big;
                        AddPartition("Storage", 0x20E2A000, 0xCE1D0000);
                        AddPartition("SystemExtPartition", 0x13FFA000, 0xCE30000);
                        AddPartition("SystemURLCachePartition", 0xDFFA000, 0x6000000);
                        AddPartition("TitleURLCachePartition", 0xBFFA000, 0x2000000);
                        AddPartition("StorageSystem", 0x7FFA000, 0x4000000);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[DriveReader] Не удалось проверить сигнатуру Xbox 360 MU: {ex.Message}");
                }

                // Проверка на Original XBOX partition.
                try
                {
                    Seek(0xABE80000);
                    if (ReadUInt32() == 0x58544146)
                    {
                        Trace.WriteLine("[DriveReader] Обнаружен HDD Original XBOX.");

                        AddPartition("Partition1", 0xABE80000, 0x1312D6000);    // DATA
                        AddPartition("Partition2", 0x8CA80000, 0x1f400000);     // SHELL
                        AddPartition("Partition3", 0x5DC80000, 0x2ee00000);     // CACHE
                        AddPartition("Partition4", 0x2EE80000, 0x2ee00000);     // CACHE
                        AddPartition("Partition5", 0x80000, 0x2ee00000);        // CACHE
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[DriveReader] Не удалось проверить сигнатуру Original XBOX (возможно, поток слишком мал): {ex.Message}");
                }

                // Проверка на Original XBOX DVT3 (Prototype Development Kit).
                try
                {
                    Seek(0x80000);
                    if (ReadUInt32() == 0x58544146)
                    {
                        Trace.WriteLine("[DriveReader] Обнаружен HDD Xbox DVT3 (v2)..");

                        AddPartition("Partition1", 0x80000, 0x1312D6000);        // DATA
                        AddPartition("Partition2", 0x131356000, 0x1f400000);     // SHELL
                        AddPartition("Partition3", 0x150756000, 0x2ee00000);     // CACHE
                        AddPartition("Partition4", 0x17F556000, 0x2ee00000);     // CACHE
                        AddPartition("Partition5", 0x1AE356000, 0x2ee00000);     // CACHE
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[DriveReader] Не удалось проверить сигнатуру DVT3 (v2): {ex.Message}");
                }

                try
                {
                    Seek(0x80004);
                    if (ReadUInt32() == 0x58544146)
                    {
                        Trace.WriteLine("[DriveReader] Обнаружен HDD Xbox DVT3 (v1)..");

                        AddPartition("Partition1", 0x80000, 0x1312D6000, true);        // DATA
                        AddPartition("Partition2", 0x131356000, 0x1f400000, true);     // SHELL
                        AddPartition("Partition3", 0x150756000, 0x2ee00000, true);     // CACHE
                        AddPartition("Partition4", 0x17F556000, 0x2ee00000, true);     // CACHE
                        AddPartition("Partition5", 0x1AE356000, 0x2ee00000, true);     // CACHE
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[DriveReader] Не удалось проверить сигнатуру DVT3 (v1): {ex.Message}");
                }

                // Проверка на XBOX 360 partitions.
                try
                {
                    Seek(0);
                    ByteOrder = ByteOrder.Big;
                    if (ReadUInt32() == 0x20000)
                    {
                        Trace.WriteLine("[DriveReader] Обнаружен HDD Xbox 360 Dev.");

                        // Это Dev HDD.
                        ReadUInt16();  // Kernel version
                        ReadUInt16();

                        // TODO: чтение из raw устройств требует выровненных операций чтения.
                        Seek(8);
                        // Partition1
                        long dataOffset = (long)ReadUInt32() * Constants.SectorSize;
                        long dataLength = (long)ReadUInt32() * Constants.SectorSize;
                        // SystemPartition
                        long shellOffset = (long)ReadUInt32() * Constants.SectorSize;
                        long shellLength = (long)ReadUInt32() * Constants.SectorSize;
                        // Unused?
                        ReadUInt32();
                        ReadUInt32();
                        // DumpPartition
                        ReadUInt32();
                        ReadUInt32();
                        // PixDump
                        ReadUInt32();
                        ReadUInt32();
                        // Unused?
                        ReadUInt32();
                        ReadUInt32();
                        // Unused?
                        ReadUInt32();
                        ReadUInt32();
                        // AltFlash
                        ReadUInt32();
                        ReadUInt32();
                        // Cache0
                        long cache0Offset = (long)ReadUInt32() * Constants.SectorSize;
                        long cache0Length = (long)ReadUInt32() * Constants.SectorSize;
                        // Cache1
                        long cache1Offset = (long)ReadUInt32() * Constants.SectorSize;
                        long cache1Length = (long)ReadUInt32() * Constants.SectorSize;

                        AddPartition("Partition1", dataOffset, dataLength);
                        AddPartition("SystemPartition", shellOffset, shellLength);
                        // TODO: добавить поддержку этих разделов
                        //AddPartition("Cache0", cache0Offset, cache0Length);
                        //AddPartition("Cache1", cache1Offset, cache1Length);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[DriveReader] Ошибка проверки сигнатуры Xbox 360 Dev: {ex.Message}");
                }

                // Если ничего не найдено, предполагаем Retail (или логируем, что сигнатура не совпала)
                try
                {
                    Trace.WriteLine("[DriveReader] Сигнатура Dev не найдена. Попытка монтирования как Xbox 360 Retail HDD...");

                    //Seek(8);
                    //var test = ReadUInt32();

                    // Это Retail HDD.
                    // Стандартная структура для Retail

                    AddPartition("Partition1", 0x130eb0000, this.Length - 0x130eb0000);
                    AddPartition("SystemPartition", 0x120eb0000, 0x10000000);

                    const long dumpPartitionOffset = 0x100080000;
                    // TODO: добавить поддержку этих разделов
                    //AddPartition("DumpPartition", 0x100080000, 0x20E30000);
                    //AddPartition("SystemURLCachePartition", dumpPartitionOffset + 0, 0x6000000);
                    //AddPartition("TitleURLCachePartition", dumpPartitionOffset + 0x6000000, 0x2000000);
                    //AddPartition("SystemExtPartition", dumpPartitionOffset + 0x0C000000, 0xCE30000);
                    AddPartition("SystemAuxPartition", dumpPartitionOffset + 0x18e30000, 0x8000000);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[DriveReader] Ошибка при инициализации Xbox 360 Retail структуры: {ex.Message}");
                }

                Trace.WriteLine($"[DriveReader] Инициализация завершена. Всего найдено разделов: {_partitions.Count}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveReader] Критическая ошибка при инициализации диска: {ex.Message}");
            }
        }

        public void AddPartition(string name, long offset, long length, bool legacy = false)
        {
            try
            {
                Volume partition = new Volume(this, name, offset, length, legacy);
                _partitions.Add(partition);

                // Правило 3: Улучшенное логирование
                Trace.WriteLine($"[DriveReader] Добавлен раздел: {name}, Offset: 0x{offset:X}, Length: 0x{length:X}");
            }
            catch (Exception ex)
            {
                // Правило 1: Не прерываем процесс при ошибке добавления одного раздела
                Trace.WriteLine($"[DriveReader] Ошибка при добавлении раздела {name}: {ex.Message}");
            }
        }

        public Volume GetPartition(int index)
        {
            if (index >= 0 && index < _partitions.Count)
                return _partitions[index];

            Trace.WriteLine($"[DriveReader] Попытка получения раздела с несуществующим индексом: {index}");
            return null;
        }

        public List<Volume> Partitions => _partitions;
    }
}