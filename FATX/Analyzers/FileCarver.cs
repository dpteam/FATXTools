// Переписано
using FATX.Analyzers.Signatures;
using FATX.Analyzers.Signatures.Blank;
using FATX.FileSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Diagnostics; // 1. Подключаем Trace

namespace FATX.Analyzers
{
    public enum FileCarverInterval
    {
        Byte = 0x1,
        Align = 0x10,
        Sector = 0x200,
        Page = 0x1000,
        Cluster = 0x4000,
    }

    public class FileCarver
    {
        private readonly Volume _volume;
        private readonly FileCarverInterval _interval;
        private readonly long _length;
        private List<FileSignature> _carvedFiles;

        public FileCarver(Volume volume)
        {
            _volume = volume;
            _interval = FileCarverInterval.Cluster;
            _length = volume.Length;
        }

        public FileCarver(Volume volume, FileCarverInterval interval, long length)
        {
            if (length == 0 || length > volume.FileAreaLength)
            {
                length = volume.Length;
            }

            _volume = volume;
            _interval = interval;
            _length = length;
        }

        public void LoadFromDatabase(JsonElement fileCarverList)
        {
            _carvedFiles = new List<FileSignature>();

            foreach (var file in fileCarverList.EnumerateArray())
            {
                try
                {
                    JsonElement offsetElement;
                    if (!file.TryGetProperty("Offset", out offsetElement))
                    {
                        // Правило 2 и 3: Trace.WriteLine с детальным описанием
                        Trace.WriteLine("[FileCarver] Ошибка загрузки сигнатуры из БД: отсутствует поле 'Offset'");
                        continue;
                    }

                    var fileSignature = new BlankSignature(_volume, offsetElement.GetInt64());

                    if (file.TryGetProperty("Name", out var nameElement))
                    {
                        fileSignature.FileName = nameElement.GetString();
                    }

                    if (file.TryGetProperty("Size", out var sizeElement))
                    {
                        fileSignature.FileSize = sizeElement.GetInt64();
                    }

                    _carvedFiles.Add(fileSignature);
                }
                catch (Exception ex)
                {
                    // Правило 1: Отказоустойчивость при чтении одной записи (не ломаем весь цикл)
                    Trace.WriteLine($"[FileCarver] Исключение при обработке записи из базы данных: {ex.Message}");
                }
            }
        }

        public List<FileSignature> GetCarvedFiles()
        {
            return _carvedFiles;
        }

        public Volume GetVolume()
        {
            return _volume;
        }

        public List<FileSignature> Analyze(CancellationToken cancellationToken, IProgress<int> progress)
        {
            try
            {
                var allSignatures = from assembly in AppDomain.CurrentDomain.GetAssemblies()
                                    from type in assembly.GetTypes()
                                    where type.Namespace == "FATX.Analyzers.Signatures"
                                    where type.IsSubclassOf(typeof(FileSignature))
                                    select type;

                _carvedFiles = new List<FileSignature>();
                var interval = (long)_interval;

                var types = allSignatures.ToList();

                // Правило 3: Логируем количество найденных сигнатур
                Trace.WriteLine($"[FileCarver] Загружено {types.Count} типов сигнатур для анализа.");

                // Сохраняем оригинальный порядок байт, чтобы восстанавливать его для каждой новой сигнатуры
                var origByteOrder = _volume.GetReader().ByteOrder;

                long progressValue = 0;
                long progressUpdate = interval * 0x200;

                for (long offset = 0; offset < _length; offset += interval)
                {
                    foreach (Type type in types)
                    {
                        try
                        {
                            // Создаем экземпляр сигнатуры
                            FileSignature signature = (FileSignature)Activator.CreateInstance(type, _volume, offset);

                            // Восстанавливаем порядок байт тома перед тестом
                            _volume.GetReader().ByteOrder = origByteOrder;

                            _volume.SeekFileArea(offset);
                            bool test = signature.Test();

                            if (test)
                            {
                                try
                                {
                                    // Убеждаемся, что файл записан первым
                                    _carvedFiles.Add(signature);

                                    // Пытаемся распарсить файл
                                    _volume.SeekFileArea(offset);
                                    signature.Parse();

                                    // Правило 2: Trace.WriteLine вместо Console
                                    // Правило 3: Добавлено имя файла в лог
                                    Trace.WriteLine($"[FileCarver] Найден {signature.GetType().Name} по адресу 0x{offset:X}. Имя: {signature.FileName}");
                                }
                                catch (Exception e)
                                {
                                    // Правило 1 и 3: Логируем ошибки парсинга, но продолжаем сканирование
                                    Trace.WriteLine($"[FileCarver] Ошибка парсинга {signature.GetType().Name} по адресу 0x{offset:X}: {e.Message}");
                                    // Trace.WriteLine(e.StackTrace); // Можно раскомментировать для детального дебага
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Правило 1: Важно! Ловим ошибки при создании инстанса (Activator) или в методе Test(),
                            // чтобы одна "сломанная" сигнатура не останавливала весь цикл.
                            Trace.WriteLine($"[FileCarver] Критическая ошибка при обработке типа {type.Name} по адресу 0x{offset:X}: {ex.Message}");
                        }
                    }

                    progressValue += interval;

                    if (progressValue % progressUpdate == 0)
                        progress?.Report((int)(progressValue / interval));

                    if (cancellationToken.IsCancellationRequested)
                    {
                        Trace.WriteLine("[FileCarver] Анализ отменен пользователем.");
                        return _carvedFiles;
                    }
                }

                // Заполняем прогресс-бар до конца
                progress?.Report((int)(_length / interval));

                // Правило 2: Финальное сообщение через Trace
                Trace.WriteLine("[FileCarver] Анализ тома завершен!");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileCarver] Глобальная ошибка в методе Analyze: {ex.Message}");
            }

            return _carvedFiles;
        }
    }
}