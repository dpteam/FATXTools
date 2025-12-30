// Переписано
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics; // 1. Подключаем Trace

namespace FATXTools.Utilities
{
    public static class WinApi
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            string FileName,
            FileAccess DesiredAccess,
            FileShare ShareMode,
            IntPtr SecurityAttributes,
            FileMode CreationDisposition,
            int FlagsAndAttributes,
            IntPtr Template);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            [MarshalAs(UnmanagedType.AsAny)]
            [Out] object lpInBuffer,
            int nInBufferSize,
            [MarshalAs(UnmanagedType.AsAny)]
            [Out] object lpOutBuffer,
            int nOutBufferSize,
            ref int pBytesReturned,
            IntPtr lpOverlapped
            );

        public static long GetDiskCapactity(SafeFileHandle diskHandle)
        {
            byte[] sizeBytes = new byte[8];
            int bytesRet = sizeBytes.Length;

            try
            {
                if (!DeviceIoControl(diskHandle, 0x00000007405C, null, 0, sizeBytes, bytesRet, ref bytesRet, IntPtr.Zero))
                {
                    // Правило 3: Улучшенное логирование (получаем код ошибки Windows)
                    int errorCode = Marshal.GetLastWin32Error();
                    Trace.WriteLine($"[WinApi] Не удалось получить размер диска (IOCTL 0x00000007405C). Код ошибки: {errorCode}");

                    // Правило 1: Не выбрасываем исключение, возвращаем -1 (ошибка)
                    return -1;
                }
                return BitConverter.ToInt64(sizeBytes, 0);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[WinApi] Исключение при вызове GetDiskCapactity: {ex.Message}");
                return -1;
            }
        }

        public static long GetSectorSize(SafeFileHandle diskHandle)
        {
            byte[] buf = new byte[0x18];
            int bytesRet = buf.Length;

            try
            {
                if (!DeviceIoControl(diskHandle, 0x000000070000, null, 0, buf, bytesRet, ref bytesRet, IntPtr.Zero))
                {
                    // Правило 3: Улучшенное логирование
                    int errorCode = Marshal.GetLastWin32Error();
                    Trace.WriteLine($"[WinApi] Не удалось получить геометрию диска (IOCTL 0x000000070000). Код ошибки: {errorCode}");

                    // Правило 1: Возвращаем -1 вместо исключения
                    return -1;
                }
                return BitConverter.ToInt32(buf, 0x14);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[WinApi] Исключение при вызове GetSectorSize: {ex.Message}");
                return -1;
            }
        }
    }
}