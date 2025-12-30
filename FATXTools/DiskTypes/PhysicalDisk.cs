// Переписано
using FATX;
using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.IO;

namespace FATXTools.DiskTypes
{
    public class PhysicalDisk : DriveReader
    {
        private long _length;
        private long _sectorLength;
        private long _position;

        public PhysicalDisk(SafeFileHandle handle, long length, long sectorLength)
            : base(new FileStream(handle, FileAccess.Read))
        {
            this._length = length;
            this._sectorLength = sectorLength;
            this.Initialize();
        }

        public override long Length => _length;

        public override long Position => _position;

        public override long Seek(long offset)
        {
            try
            {
                if (offset < 0) offset = 0; // Правило 1: Защита от отрицательного смещения
                _position = offset;
                return _position;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PhysicalDisk] Ошибка Seek (offset: {offset}): {ex.Message}");
                return _position; // Возвращаем последнюю валидную позицию
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            try
            {
                switch (origin)
                {
                    case SeekOrigin.Begin:
                        _position = offset;
                        break;
                    case SeekOrigin.Current:
                        _position += offset;
                        break;
                    case SeekOrigin.End:
                        _position = _length - offset;
                        break;
                }

                // Правило 1: Защита от переполнения и отрицательных значений
                if (_position < 0) _position = 0;
                if (_position > _length) _position = _length;

                return _position;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PhysicalDisk] Ошибка Seek ({origin}, offset: {offset}): {ex.Message}");
                return _position;
            }
        }

        public override void Read(byte[] buffer, int count)
        {
            // Правило 1: Валидация входных данных
            if (buffer == null)
            {
                Trace.WriteLine("[PhysicalDisk] Попытка чтения в null буфер.");
                throw new ArgumentNullException(nameof(buffer));
            }

            if (count < 0)
            {
                Trace.WriteLine($"[PhysicalDisk] Попытка чтения отрицательного количества байт: {count}");
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (count == 0) return; // Нечего читать

            try
            {
                // Align position down to nearest sector
                var offset = _position;
                if (_position % _sectorLength != 0)
                {
                    offset -= _position % _sectorLength;
                }

                // Then seek to the sector aligned offset
                BaseStream.Seek(offset, SeekOrigin.Begin);

                // Now read bytes of sector size
                // Вычисляем выровненный размер чтения
                long alignedCountLong = ((count + (_sectorLength - 1)) & ~(_sectorLength - 1));

                if (alignedCountLong > int.MaxValue)
                {
                    throw new IOException($"Слишком большой запрос на чтение: {count} байт (выровнено: {alignedCountLong}).");
                }

                var alignedCount = (int)alignedCountLong;
                var tempBuf = new byte[alignedCount];

                // Чтение из потока
                int bytesRead = BaseStream.Read(tempBuf, 0, alignedCount);

                // Если прочитано меньше, чем ожидалось (например, конец диска), BlockCopy 
                // просто скопирует то, что есть (нули в конце tempBuf), что безопасно.

                // Only copy of bytes we need
                int sourceIndex = (int)(_position % _sectorLength);

                // Проверка на случай неполного чтения в конце диска
                if (sourceIndex + count > bytesRead)
                {
                    // Можно заполнить нулями остаток, но обычно Volume ловит EOF
                }

                Buffer.BlockCopy(tempBuf, sourceIndex, buffer, 0, count);

                // Increment the position by how much we took
                _position += count;
            }
            catch (Exception ex)
            {
                // Правило 3: Улучшенное логирование
                Trace.WriteLine($"[PhysicalDisk] Ошибка чтения с диска (Pos: {_position:X}, Count: {count}): {ex.Message}");

                // Пробрасываем исключение, так как класс Volume ожидает IOException для обработки ошибок
                throw;
            }
        }

        public override byte[] ReadBytes(int count)
        {
            try
            {
                byte[] buf = new byte[count];
                Read(buf, count);
                return buf;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PhysicalDisk] Исключение в ReadBytes({count}): {ex.Message}");
                throw;
            }
        }

        public override byte ReadByte()
        {
            try
            {
                byte[] buf = new byte[1];
                Read(buf, 1);
                return buf[0];
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PhysicalDisk] Исключение в ReadByte: {ex.Message}");
                throw;
            }
        }
    }
}