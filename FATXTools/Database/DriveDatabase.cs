// Переписано
using System;
using System.Collections.Generic;
using System.Diagnostics; // 1. Подключаем Trace
using System.IO;
using System.Text.Json;
using FATX;
using FATX.FileSystem;

namespace FATXTools.Database
{
    public class DriveDatabase
    {
        string driveName;
        DriveReader drive;
        List<PartitionDatabase> partitionDatabases;

        public DriveDatabase(string driveName, DriveReader drive)
        {
            this.driveName = driveName;
            this.drive = drive;

            partitionDatabases = new List<PartitionDatabase>();
        }

        public event EventHandler<AddPartitionEventArgs> OnPartitionAdded;
        public event EventHandler<RemovePartitionEventArgs> OnPartitionRemoved;

        public PartitionDatabase AddPartition(Volume volume)
        {
            try
            {
                var partitionDatabase = new PartitionDatabase(volume);
                partitionDatabases.Add(partitionDatabase);
                return partitionDatabase;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveDatabase] Ошибка добавления раздела в базу: {ex.Message}");
                return null;
            }
        }

        public void RemovePartition(int index)
        {
            try
            {
                if (index >= 0 && index < partitionDatabases.Count)
                {
                    partitionDatabases.RemoveAt(index);
                    OnPartitionRemoved?.Invoke(this, new RemovePartitionEventArgs(index));
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveDatabase] Ошибка удаления раздела по индексу {index}: {ex.Message}");
            }
        }

        public void Save(string path)
        {
            try
            {
                Trace.WriteLine($"[DriveDatabase] Сохранение базы данных в файл: {path}");

                Dictionary<string, object> databaseObject = new Dictionary<string, object>();

                databaseObject["Version"] = 1;
                databaseObject["Drive"] = new Dictionary<string, object>();

                var driveObject = databaseObject["Drive"] as Dictionary<string, object>;
                driveObject["FileName"] = driveName;

                driveObject["Partitions"] = new List<Dictionary<string, object>>();
                var partitionList = driveObject["Partitions"] as List<Dictionary<string, object>>;

                foreach (var partitionDatabase in partitionDatabases)
                {
                    var partitionObject = new Dictionary<string, object>();
                    partitionList.Add(partitionObject);
                    partitionDatabase.Save(partitionObject);
                }

                JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions()
                {
                    WriteIndented = true,
                };

                string json = JsonSerializer.Serialize(databaseObject, jsonSerializerOptions);

                File.WriteAllText(path, json);

                Trace.WriteLine("[DriveDatabase] База данных успешно сохранена.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveDatabase] Критическая ошибка при сохранении базы данных: {ex.Message}");
                throw; // Пробрасываем, так как пользователь должен знать, что сохранение не удалось
            }
        }

        private bool LoadIfNotExists(JsonElement partitionElement)
        {
            try
            {
                foreach (var partitionDatabase in partitionDatabases)
                {
                    // Правило 1: Защита доступа к Volume
                    if (partitionDatabase?.Volume == null) continue;

                    // Используем безопасное чтение JSON свойства
                    if (partitionElement.TryGetProperty("Offset", out JsonElement offsetElement))
                    {
                        if (partitionDatabase.Volume.Offset == offsetElement.GetInt64())
                        {
                            partitionDatabase.LoadFromJson(partitionElement);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveDatabase] Ошибка проверки существования раздела: {ex.Message}");
            }
            return false;
        }

        public void LoadFromJson(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Trace.WriteLine($"[DriveDatabase] Файл базы данных не найден: {path}");
                    return;
                }

                string json = File.ReadAllText(path);
                var databaseObject = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                if (databaseObject == null)
                {
                    Trace.WriteLine("[DriveDatabase] Не удалось десериализовать базу данных (null результат).");
                    return;
                }

                if (databaseObject.ContainsKey("Drive"))
                {
                    JsonElement driveJsonElement = (JsonElement)databaseObject["Drive"];

                    if (driveJsonElement.TryGetProperty("Partitions", out var partitionsElement))
                    {
                        Trace.WriteLine("[DriveDatabase] Загрузка разделов из базы данных...");

                        foreach (var partitionElement in partitionsElement.EnumerateArray())
                        {
                            // Проверяем, существует ли раздел, и пробуем загрузить
                            if (!LoadIfNotExists(partitionElement))
                            {
                                // Раздел не найден, создаем новый
                                // Правило 3 и 1: Безопасное чтение свойств JSON
                                if (!partitionElement.TryGetProperty("Offset", out JsonElement offsetElement))
                                {
                                    Trace.WriteLine("[DriveDatabase] Пропуск раздела: отсутствует поле Offset.");
                                    continue;
                                }

                                var offset = offsetElement.GetInt64();

                                if (!partitionElement.TryGetProperty("Length", out JsonElement lengthElement))
                                {
                                    Trace.WriteLine("[DriveDatabase] Пропуск раздела: отсутствует поле Length.");
                                    continue;
                                }
                                var length = lengthElement.GetInt64();

                                if (!partitionElement.TryGetProperty("Name", out JsonElement nameElement))
                                {
                                    Trace.WriteLine("[DriveDatabase] Пропуск раздела: отсутствует поле Name.");
                                    continue;
                                }
                                var name = nameElement.GetString();

                                try
                                {
                                    Trace.WriteLine($"[DriveDatabase] Создание нового раздела из базы: {name}, Offset: 0x{offset:X}");

                                    Volume newVolume = new Volume(this.drive, name, offset, length);

                                    OnPartitionAdded?.Invoke(this, new AddPartitionEventArgs(newVolume));

                                    // ВНИМАНИЕ: Оригинальный код полагается на то, что обработчик события 
                                    // добавил PartitionDatabase в partitionDatabases. Это опасно, если обработчик не сработает.
                                    if (partitionDatabases.Count > 0)
                                    {
                                        partitionDatabases[partitionDatabases.Count - 1].LoadFromJson(partitionElement);
                                    }
                                    else
                                    {
                                        Trace.WriteLine("[DriveDatabase] Ошибка: список разделов пуст после события добавления. Данные не загружены.");
                                    }
                                }
                                catch (Exception volEx)
                                {
                                    Trace.WriteLine($"[DriveDatabase] Ошибка создания Volume из JSON ({name}): {volEx.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        Trace.WriteLine("[DriveDatabase] В JSON отсутствует список Partitions.");
                    }
                }
                else
                {
                    Trace.WriteLine("[DriveDatabase] В JSON отсутствует объект Drive.");
                }
            }
            catch (JsonException jsonEx)
            {
                Trace.WriteLine($"[DriveDatabase] Ошибка формата JSON файла: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DriveDatabase] Критическая ошибка при загрузке базы данных: {ex.Message}");
            }
        }
    }
}