// Переписано
using System;
using System.IO;
using System.Diagnostics; // 1. Подключаем Trace

namespace FATX
{
    public enum ByteOrder
    {
        Big,
        Little
    }

    public class EndianReader : BinaryReader
    {
        private ByteOrder byteOrder;

        public EndianReader(Stream stream, ByteOrder byteOrder)
            : base(stream)
        {
            this.byteOrder = byteOrder;
        }

        public EndianReader(Stream stream)
            : base(stream)
        {
            this.byteOrder = ByteOrder.Little;
        }

        public ByteOrder ByteOrder
        {
            get { return this.byteOrder; }
            set { this.byteOrder = value; }
        }

        public virtual long Length
        {
            get
            {
                try
                {
                    if (BaseStream != null)
                        return BaseStream.Length;
                }
                catch (Exception ex)
                {
                    // Правило 2 и 3: Логируем ошибку, возвращаем -1
                    Trace.WriteLine($"[EndianReader] Ошибка получения длины потока: {ex.Message}");
                }
                return -1;
            }
        }

        public virtual long Position
        {
            get
            {
                try
                {
                    if (BaseStream != null)
                        return BaseStream.Position;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[EndianReader] Ошибка получения позиции потока: {ex.Message}");
                }
                return -1;
            }
        }

        public virtual long Seek(long offset)
        {
            try
            {
                if (BaseStream != null)
                {
                    BaseStream.Position = offset;
                    return BaseStream.Position;
                }
            }
            catch (Exception ex)
            {
                // Правило 2 и 3: Логируем ошибку
                Trace.WriteLine($"[EndianReader] Ошибка смещения потока (Offset: {offset}): {ex.Message}");
                throw; // Пробрасываем, чтобы вызывающий код знал, что операция не удалась
            }
            return -1;
        }

        public virtual long Seek(long offset, SeekOrigin origin)
        {
            try
            {
                if (BaseStream != null)
                    return BaseStream.Seek(offset, origin);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EndianReader] Ошибка смещения потока (Offset: {offset}, Origin: {origin}): {ex.Message}");
                throw;
            }
            return -1;
        }

        public virtual void Read(byte[] buffer, int count)
        {
            try
            {
                if (BaseStream == null) throw new ObjectDisposedException("BaseStream");

                int read = BaseStream.Read(buffer, 0, count);
                // В оригинале возвращаемое значение игнорировалось.
                // Если прочитано меньше, чем запрошено (EOF), это нормальная ситуация для FileStream,
                // но если возникло исключение, мы его ловим.
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EndianReader] Ошибка чтения {count} байт: {ex.Message}");
                throw; // Пробрасываем, чтобы методы ReadInt32 знали, что данных нет
            }
        }

        public override short ReadInt16()
        {
            try
            {
                var temp = new byte[2];
                Read(temp, 2);
                if (byteOrder == ByteOrder.Big)
                {
                    Array.Reverse(temp);
                }
                return BitConverter.ToInt16(temp, 0);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EndianReader] Ошибка чтения Int16: {ex.Message}");
                throw;
            }
        }

        public override ushort ReadUInt16()
        {
            try
            {
                var temp = new byte[2];
                Read(temp, 2);
                if (byteOrder == ByteOrder.Big)
                {
                    Array.Reverse(temp);
                }
                return BitConverter.ToUInt16(temp, 0);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EndianReader] Ошибка чтения UInt16: {ex.Message}");
                throw;
            }
        }

        public override int ReadInt32()
        {
            try
            {
                var temp = new byte[4];
                Read(temp, 4);
                if (byteOrder == ByteOrder.Big)
                {
                    Array.Reverse(temp);
                }
                return BitConverter.ToInt32(temp, 0);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EndianReader] Ошибка чтения Int32: {ex.Message}");
                throw;
            }
        }

        public override uint ReadUInt32()
        {
            try
            {
                var temp = new byte[4];
                Read(temp, 4);
                if (byteOrder == ByteOrder.Big)
                {
                    Array.Reverse(temp);
                }
                return BitConverter.ToUInt32(temp, 0);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EndianReader] Ошибка чтения UInt32: {ex.Message}");
                throw;
            }
        }
    }
}