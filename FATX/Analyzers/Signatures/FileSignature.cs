// Переписано
using FATX.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics; // 1. Подключаем Trace

namespace FATX.Analyzers.Signatures
{
    public abstract class FileSignature
    {
        private Volume _volume;
        private DriveReader _reader;
        private long _offset;
        private long _fileSize;
        private string _fileName;

        // Добавляем объект блокировки для потокобезопасности (правило 1)
        private static readonly object _lockObj = new object();
        private static Dictionary<string, int> _counters = new Dictionary<string, int>();

        public FileSignature(Volume volume, long offset)
        {
            try
            {
                this._fileName = null;
                this._fileSize = 0;
                this._offset = offset;
                this._volume = volume ?? throw new ArgumentNullException(nameof(volume));

                // Инициализация ридера с защитой
                _reader = volume.GetReader();
                if (_reader == null)
                {
                    Trace.WriteLine($"[FileSignature] Внимание: GetReader() вернул null для тома на смещении {offset}.");
                }
            }
            catch (Exception ex)
            {
                // Логируем критические ошибки инициализации, но не даем упасть приложению
                Trace.WriteLine($"[FileSignature] Ошибка при инициализации FileSignature (Offset: {offset}): {ex.Message}");
            }
        }

        public abstract bool Test();

        public abstract void Parse();

        public string FileName
        {
            get
            {
                if (_fileName == null)
                {
                    // Защита доступа к общему ресурсу
                    lock (_lockObj)
                    {
                        // Double-check locking
                        if (_fileName == null)
                        {
                            try
                            {
                                string typeName = this.GetType().Name;
                                if (!_counters.ContainsKey(typeName))
                                {
                                    _counters[typeName] = 1;
                                }
                                _fileName = typeName + (_counters[typeName]++).ToString();

                                // Правило 3: Улучшенное логирование (фиксируем генерацию имени)
                                Trace.WriteLine($"[FileSignature] Сгенерировано имя файла: {_fileName} для типа {typeName}");
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"[FileSignature] Ошибка при генерации имени файла: {ex.Message}");
                                // Fallback значение, чтобы не ломать дальнейшую логику null-ссылками
                                _fileName = "Unknown_" + Guid.NewGuid().ToString();
                            }
                        }
                    }
                }

                return _fileName;
            }
            set => _fileName = value;
        }

        public long FileSize
        {
            get => _fileSize;
            set => _fileSize = value;
        }

        public long Offset
        {
            get => _offset;
            set => _offset = value;
        }

        protected void Seek(long offset, SeekOrigin origin = SeekOrigin.Begin)
        {
            try
            {
                offset += this._offset;
                _volume.SeekFileArea(offset, origin);
            }
            catch (Exception ex)
            {
                // Правило 1 и 3: Пропуск ошибки, логирование с контекстом
                Trace.WriteLine($"[FileSignature] Ошибка Seek (целевое смещение: {this._offset + offset}): {ex.Message}");
            }
        }

        protected void SetByteOrder(ByteOrder byteOrder)
        {
            try
            {
                if (_reader != null)
                    _reader.ByteOrder = byteOrder;
                else
                    Trace.WriteLine("[FileSignature] Попытка установить ByteOrder при null _reader.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileSignature] Ошибка установки ByteOrder: {ex.Message}");
            }
        }

        protected byte[] ReadBytes(int count)
        {
            try
            {
                if (_reader == null) return new byte[0];
                return _reader.ReadBytes(count);
            }
            catch (Exception ex)
            {
                // Правило 1 и 3: Логируем ошибку чтения, возвращаем пустой массив
                Trace.WriteLine($"[FileSignature] Ошибка чтения {count} байт (позиция: {_reader?.Position ?? -1}): {ex.Message}");
                return new byte[0];
            }
        }

        protected byte ReadByte()
        {
            try
            {
                if (_reader == null) return 0;
                return _reader.ReadByte();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileSignature] Ошибка чтения байта (позиция: {_reader?.Position ?? -1}): {ex.Message}");
                return 0;
            }
        }

        protected ushort ReadUInt16()
        {
            try
            {
                if (_reader == null) return 0;
                return _reader.ReadUInt16();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileSignature] Ошибка чтения UInt16 (позиция: {_reader?.Position ?? -1}): {ex.Message}");
                return 0;
            }
        }

        protected uint ReadUInt32()
        {
            try
            {
                if (_reader == null) return 0;
                return _reader.ReadUInt32();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileSignature] Ошибка чтения UInt32 (позиция: {_reader?.Position ?? -1}): {ex.Message}");
                return 0;
            }
        }

        protected String ReadCString(int terminant = 0)
        {
            String tempString = String.Empty;
            try
            {
                if (_reader == null) return String.Empty;

                // Улучшенная логика чтения с защитой от выхода за границы
                while (_reader.Position < _reader.Length)
                {
                    int tempChar = ReadByte(); // Используем защищенный метод
                    if (tempChar == terminant)
                        break;

                    tempString += Convert.ToChar(tempChar);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileSignature] Ошибка чтения C-строки (текущая длина: {tempString.Length}): {ex.Message}");
            }
            return tempString;
        }
    }
}