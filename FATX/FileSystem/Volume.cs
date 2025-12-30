// Переписано
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics; // 1. Подключаем Trace

namespace FATX.FileSystem
{
    public class Volume
    {
        private readonly DriveReader _reader;
        private readonly string _partitionName;
        private readonly long _partitionOffset;
        private readonly long _partitionLength;

        public const uint VolumeSignature = 0x58544146;

        private uint _signature;
        private uint _serialNumber;
        private uint _sectorsPerCluster;
        private uint _rootDirFirstCluster;

        private uint _bytesPerCluster;
        private uint _maxClusters;
        private uint _bytesPerFat;
        private bool _isFat16;
        private uint _fatByteOffset;
        private uint _fileAreaByteOffset;

        private bool _usesLegacyFormat;
        private uint _jump;
        private ushort _bytesPerSector;
        private ushort _reservedSectors;
        private ushort _sectorsPerTrack;
        private ushort _heads;
        private uint _hiddenSectors;
        private uint _largeSectors;
        private uint _largeSectorsPerFat;

        private List<DirectoryEntry> _root = new List<DirectoryEntry>();
        private uint[] _fileAllocationTable;
        private long _fileAreaLength;
        private Platform _platform;

        public Volume(DriveReader reader, string name, long offset, long length, bool legacy = false)
        {
            this._reader = reader;
            this._partitionName = name;
            this._partitionLength = length;
            this._partitionOffset = offset;
            this._usesLegacyFormat = legacy;

            this._platform = (reader.ByteOrder == ByteOrder.Big) ?
                Platform.X360 : Platform.Xbox;

            Mounted = false;
        }

        public string Name
        {
            get { return _partitionName; }
        }

        public uint RootDirFirstCluster
        {
            get { return _rootDirFirstCluster; }
        }

        public long Length
        {
            get { return _partitionLength; }
        }

        public long FileAreaLength
        {
            get { return _fileAreaLength; }
        }

        public uint MaxClusters
        {
            get { return _maxClusters; }
        }

        public uint BytesPerCluster
        {
            get { return _bytesPerCluster; }
        }

        public DriveReader GetReader()
        { return _reader; }

        public uint[] FileAllocationTable
        {
            get { return _fileAllocationTable; }
        }

        public long FileAreaByteOffset
        {
            get { return _fileAreaByteOffset; }
        }

        public long Offset
        {
            get { return _partitionOffset; }
        }

        public List<DirectoryEntry> GetRoot()
        {
            return _root;
        }

        public Platform Platform
        {
            get { return _platform; }
        }

        public bool Mounted { get; private set; }

        public void Mount()
        {
            try
            {
                Trace.WriteLine($"[Volume] Попытка монтирования тома '{_partitionName}'...");

                Mounted = false;

                // Read and verify volume metadata.
                ReadBootSector();
                CalculateOffsets();
                ReadFileAllocationTable();

                _root = ReadDirectoryStream(_rootDirFirstCluster);
                PopulateDirentStream(_root, _rootDirFirstCluster);

                Mounted = true;
                Trace.WriteLine($"[Volume] Том '{_partitionName}' успешно смонтирован.");
            }
            catch (Exception ex)
            {
                // Правило 1: Если монтаж не удался, не падаем, а логируем
                Trace.WriteLine($"[Volume] Ошибка монтирования тома '{_partitionName}': {ex.Message}");
                Mounted = false;
            }
        }

        private void ReadBootSector()
        {
            try
            {
                _reader.Seek(_partitionOffset);
                // Правило 2 и 3: Trace с контекстом
                Trace.WriteLine($"[Volume] Чтение загрузочного сектора по смещению 0x{_partitionOffset:X16}");

                if (!_usesLegacyFormat)
                {
                    ReadVolumeMetadata();
                }
                else
                {
                    ReadLegacyVolumeMetadata();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Volume] Критическая ошибка при чтении загрузочного сектора: {ex.Message}");
                throw; // Пробрасываем, чтобы Mount установил Mounted = false
            }
        }

        private void ReadVolumeMetadata()
        {
            _signature = _reader.ReadUInt32();
            _serialNumber = _reader.ReadUInt32();
            _sectorsPerCluster = _reader.ReadUInt32();
            _rootDirFirstCluster = _reader.ReadUInt32();

            if (_signature != VolumeSignature)
            {
                string msg = $"Неверная сигнатура FATX для тома {_partitionName}: {_signature:X8}. Ожидается 0x58544146.";
                Trace.WriteLine($"[Volume] {msg}");
                throw new FormatException(msg);
            }
        }

        private void ReadLegacyVolumeMetadata()
        {
            // Read _BOOT_SECTOR
            _jump = _reader.ReadUInt32();                   // EB FE
            _signature = _reader.ReadUInt32();              // FATX
            _serialNumber = _reader.ReadUInt32();           // 05C29C00

            if (_signature != VolumeSignature)
            {
                string msg = $"Неверная сигнатура Legacy FATX для тома {_partitionName}: {_signature:X8}.";
                Trace.WriteLine($"[Volume] {msg}");
                throw new FormatException(msg);
            }

            // Read BIOS_PARAMETER_BLOCK
            _bytesPerSector = _reader.ReadUInt16();         // 0200
            _sectorsPerCluster = _reader.ReadByte();        // 20
            _reservedSectors = _reader.ReadUInt16();        // 08
            _sectorsPerTrack = _reader.ReadUInt16();        // 3F00
            _heads = _reader.ReadUInt16();                  // FF00
            _hiddenSectors = _reader.ReadUInt32();          // 00000400
            _largeSectors = _reader.ReadUInt32();           // 009896B0
            _largeSectorsPerFat = _reader.ReadUInt32();     // 00000990
            _rootDirFirstCluster = _reader.ReadUInt32();    // 00000001
        }

        private void CalculateOffsets()
        {
            _bytesPerCluster = _sectorsPerCluster * Constants.SectorSize;

            // Защита от деления на ноль или переполнения, если данные повреждены
            if (_bytesPerCluster == 0)
            {
                throw new InvalidOperationException("BytesPerCluster равно 0, невозможно рассчитать смещения.");
            }

            _maxClusters = (uint)(_partitionLength / (long)_bytesPerCluster) + Constants.ReservedClusters;

            uint bytesPerFat;
            if (_maxClusters < 0xfff0)
            {
                bytesPerFat = _maxClusters * 2;
                _isFat16 = true;
            }
            else
            {
                bytesPerFat = _maxClusters * 4;
                _isFat16 = false;
            }

            _bytesPerFat = (bytesPerFat + (Constants.PageSize - 1)) & ~(Constants.PageSize - 1);

            this._fatByteOffset = Constants.ReservedBytes;
            this._fileAreaByteOffset = this._fatByteOffset + this._bytesPerFat;
            this._fileAreaLength = this.Length - this.FileAreaByteOffset;

            Trace.WriteLine($"[Volume] Параметры тома: Clusters: {_maxClusters}, FatOffset: 0x{_fatByteOffset:X}");
        }

        private void ReadFileAllocationTable()
        {
            try
            {
                // Правило 1: Защита от OutOfMemory при поврежденном MaxClusters
                if (_maxClusters > int.MaxValue / 4) // Примерная проверка на разумность
                {
                    throw new OutOfMemoryException($"Слишком большое количество кластеров ({_maxClusters}), возможно повреждение метаданных.");
                }

                _fileAllocationTable = new uint[_maxClusters];

                var fatOffset = ByteOffsetToPhysicalOffset(this._fatByteOffset);
                _reader.Seek(fatOffset);

                if (this._isFat16)
                {
                    byte[] _tempFat = new byte[_maxClusters * 2];
                    _reader.Read(_tempFat, (int)(_maxClusters * 2));

                    if (_reader.ByteOrder == ByteOrder.Big)
                    {
                        for (int i = 0; i < _maxClusters; i++)
                        {
                            Array.Reverse(_tempFat, i * 2, 2);
                        }
                    }

                    for (int i = 0; i < _maxClusters; i++)
                    {
                        _fileAllocationTable[i] = BitConverter.ToUInt16(_tempFat, i * 2);
                    }
                }
                else
                {
                    byte[] _tempFat = new byte[_maxClusters * 4];
                    _reader.Read(_tempFat, (int)(_maxClusters * 4));

                    if (_reader.ByteOrder == ByteOrder.Big)
                    {
                        for (int i = 0; i < _maxClusters; i++)
                        {
                            Array.Reverse(_tempFat, i * 4, 4);
                        }
                    }

                    for (int i = 0; i < _maxClusters; i++)
                    {
                        _fileAllocationTable[i] = BitConverter.ToUInt32(_tempFat, i * 4);
                    }
                }
            }
            catch (OutOfMemoryException ex)
            {
                Trace.WriteLine($"[Volume] Критическая ошибка: {ex.Message}. FAT не загружена.");
                throw;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Volume] Ошибка чтения FAT таблицы: {ex.Message}");
                throw;
            }
        }

        private List<DirectoryEntry> ReadDirectoryStream(uint cluster)
        {
            List<DirectoryEntry> stream = new List<DirectoryEntry>();

            byte[] data;

            try
            {
                data = ReadCluster(cluster);
            }
            catch (IOException exception)
            {
                // Правило 2 и 3: Trace вместо Console
                Trace.WriteLine($"[Volume] Ошибка чтения: {exception.Message}. Возвращается пустой список для кластера {cluster}.");
                return stream;
            }

            long clusterOffset = ClusterToPhysicalOffset(cluster);

            for (int i = 0; i < 256; i++)
            {
                DirectoryEntry dirent = new DirectoryEntry(this.Platform, data, (i * 0x40));

                if (dirent.FileNameLength == Constants.DirentNeverUsed ||
                    dirent.FileNameLength == Constants.DirentNeverUsed2)
                {
                    break;
                }

                dirent.Offset = clusterOffset + (i * 0x40);

                stream.Add(dirent);
            }

            return stream;
        }

        private void PopulateDirentStream(List<DirectoryEntry> stream, uint clusterIndex)
        {
            foreach (DirectoryEntry dirent in stream)
            {
                try
                {
                    dirent.Cluster = clusterIndex;

                    if (dirent.IsDirectory() && !dirent.IsDeleted())
                    {
                        List<uint> chainMap = GetClusterChain(dirent);

                        foreach (uint cluster in chainMap)
                        {
                            List<DirectoryEntry> direntStream = ReadDirectoryStream(cluster);
                            dirent.AddChildren(direntStream);
                            PopulateDirentStream(direntStream, cluster);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Правило 1: Продолжаем обход дерева даже если одна директория битая
                    Trace.WriteLine($"[Volume] Ошибка при обработке содержимого директории '{dirent.FileName}': {ex.Message}");
                }
            }
        }

        public void SeekFileArea(long offset, SeekOrigin origin = SeekOrigin.Begin)
        {
            try
            {
                // TODO: Проверка на недопустимое смещение (реализована частично try-catch ниже)
                offset += FileAreaByteOffset + _partitionOffset;
                _reader.Seek(offset, origin);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Volume] Ошибка смещения (Seek) в области файлов (Offset: {offset}): {ex.Message}");
                throw;
            }
        }

        public void SeekToCluster(uint cluster)
        {
            try
            {
                var offset = ClusterToPhysicalOffset(cluster);
                _reader.Seek(offset);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Volume] Ошибка смещения к кластеру {cluster}: {ex.Message}");
                throw;
            }
        }

        public byte[] ReadCluster(uint cluster)
        {
            try
            {
                var clusterOffset = ClusterToPhysicalOffset(cluster);
                _reader.Seek(clusterOffset);
                byte[] clusterData = new byte[_bytesPerCluster];
                _reader.Read(clusterData, (int)_bytesPerCluster);
                return clusterData;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Volume] Ошибка чтения кластера {cluster}: {ex.Message}");
                throw;
            }
        }

        public List<uint> GetClusterChain(DirectoryEntry dirent)
        {
            var firstCluster = dirent.FirstCluster;
            List<uint> clusterChain = new List<uint>();

            if (firstCluster == 0 || firstCluster > this.MaxClusters)
            {
                // Правило 2 и 3: Trace вместо Console, контекст
                Trace.WriteLine($"[Volume] {dirent.GetFullPath()}: Неверный первый кластер (FirstCluster={firstCluster}, MaxClusters={this.MaxClusters}). Цепочка пуста.");
                return clusterChain;
            }

            clusterChain.Add(firstCluster);

            if (dirent.IsDeleted())
            {
                return clusterChain;
            }

            uint fatEntry = firstCluster;
            uint reservedIndexes = (_isFat16) ? Constants.Cluster16Reserved : Constants.ClusterReserved;

            try
            {
                while (true)
                {
                    // Защита от выхода за границы массива FAT
                    if (fatEntry >= _fileAllocationTable.Length)
                    {
                        Trace.WriteLine($"[Volume] {dirent.FileName}: FAT цепочка ссылается на индекс {fatEntry} за пределами таблицы (Length: {_fileAllocationTable.Length}). Обрыв цепочки.");
                        break;
                    }

                    fatEntry = _fileAllocationTable[fatEntry];
                    if (fatEntry >= reservedIndexes)
                    {
                        break;
                    }

                    if (fatEntry == 0 || fatEntry > _fileAllocationTable.Length)
                    {
                        Trace.WriteLine($"[Volume] Файл {dirent.FileName} имеет поврежденную цепочку кластеров (значение FAT: {fatEntry}).");
                        clusterChain = new List<uint>(1);
                        clusterChain.Add(firstCluster);
                        return clusterChain;
                    }

                    clusterChain.Add(fatEntry);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Volume] Ошибка при обходе цепочки кластеров для {dirent.FileName}: {ex.Message}");
            }

            return clusterChain;
        }

        private long ByteOffsetToPhysicalOffset(long offset)
        {
            return this._partitionOffset + offset;
        }

        public long ClusterToPhysicalOffset(uint cluster)
        {
            var physicalOffset = ByteOffsetToPhysicalOffset(FileAreaByteOffset);
            long clusterOffset = (long)_bytesPerCluster * (long)(cluster - 1);
            return (physicalOffset + clusterOffset);
        }

        private long CountFiles(List<DirectoryEntry> dirents)
        {
            long numFiles = 0;

            foreach (var dirent in dirents)
            {
                numFiles += dirent.CountFiles();
            }

            return numFiles;
        }

        public long CountFiles()
        {
            return CountFiles(_root);
        }

        public long GetTotalSpace()
        {
            return _fileAreaLength;
        }

        public long GetFreeSpace()
        {
            return (_fileAreaLength) - GetUsedSpace();
        }

        public long GetUsedSpace()
        {
            long clustersUsed = 0;

            if (_fileAllocationTable == null) return 0;

            foreach (var cluster in FileAllocationTable)
            {
                if (cluster != Constants.ClusterAvailable)
                {
                    clustersUsed++;
                }
            }

            return (clustersUsed * BytesPerCluster);
        }
    }
}