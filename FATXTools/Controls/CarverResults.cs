// Переписано
using FATX.Analyzers;
using FATX.Analyzers.Signatures;
using FATX.FileSystem;
using FATXTools.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics; // 1. Подключаем Trace
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace FATXTools
{
    public partial class CarverResults : UserControl
    {
        private FileCarver _analyzer;
        private Volume _volume;
        private TaskRunner taskRunner;

        public CarverResults(FileCarver analyzer, TaskRunner taskRunner)
        {
            InitializeComponent();

            try
            {
                this._analyzer = analyzer;
                this._volume = analyzer.GetVolume();
                this.taskRunner = taskRunner;

                // Правило 1: Защита при инициализации списка
                PopulateResultsList(analyzer.GetCarvedFiles());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[CarverResults] Ошибка инициализации результатов: {ex.Message}");
            }
        }

        public void PopulateResultsList(List<FileSignature> results)
        {
            if (results == null) return;

            var i = 1;
            listView1.Items.Clear(); // Очистка перед заполнением

            try
            {
                long baseOffset = (_volume?.Offset ?? 0) + _volume?.FileAreaByteOffset ?? 0;

                foreach (var result in results)
                {
                    try
                    {
                        var item = listView1.Items.Add(i.ToString());
                        item.SubItems.Add(result.FileName);
                        item.SubItems.Add(String.Format("0x{0:X}", result.Offset + baseOffset));
                        item.SubItems.Add(String.Format("0x{0:X}", result.FileSize));
                        item.Tag = result;
                        i++;
                    }
                    catch (Exception ex)
                    {
                        // Правило 1: Одна битая запись не должна ломать отрисовку списка
                        Trace.WriteLine($"[CarverResults] Ошибка отображения записи {result?.FileName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[CarverResults] Критическая ошибка наполнения списка: {ex.Message}");
            }
        }

        private void SaveFile(FileSignature signature, string path)
        {
            try
            {
                // Правило 1: Проверка null
                if (signature == null || _volume == null) return;

                const int bufsize = 0x100000;
                var remains = signature.FileSize;

                _volume.SeekFileArea(signature.Offset);

                // Правило 1: Безопасная склейка путей
                string fileName = Path.GetFileName(signature.FileName); // Безопасность от "../../"
                string uniquePath = Utility.UniqueFileName(Path.Combine(path, fileName));

                // Если UniqueFileName вернул null (например, превысил лимит попыток), используем оригинальный
                if (uniquePath == null) uniquePath = Path.Combine(path, fileName);

                using (FileStream file = new FileStream(uniquePath, FileMode.Create))
                {
                    while (remains > 0)
                    {
                        var read = Math.Min(remains, bufsize);
                        remains -= read;

                        byte[] buf = new byte[read];
                        _volume.GetReader().Read(buf, (int)read);

                        file.Write(buf, 0, (int)read);
                    }
                }

                // Правило 3: Логируем успех
                // Trace.WriteLine($"[CarverResults] Файл сохранен: {signature.FileName}");
            }
            catch (IOException ioEx)
            {
                // Правило 1 и 3: Конкретная обработка IO ошибок
                Trace.WriteLine($"[CarverResults] Ошибка записи файла {signature.FileName}: {ioEx.Message}");
                throw; // Пробрасываем, чтобы UI обработал
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[CarverResults] Неизвестная ошибка при сохранении {signature.FileName}: {ex.Message}");
                throw; // Пробрасываем
            }
        }

        private void recoverFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 0) return;

            using (var fbd = new FolderBrowserDialog())
            {
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    int successCount = 0;

                    foreach (ListViewItem item in listView1.SelectedItems)
                    {
                        try
                        {
                            SaveFile((FileSignature)item.Tag, fbd.SelectedPath);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            // Правило 1: Продолжаем восстановление других файлов, если один не сохранился
                            Trace.WriteLine($"[CarverResults] Ошибка восстановления файла {item?.SubItems[1]?.Text}: {ex.Message}");
                            MessageBox.Show($"Не удалось сохранить файл {item?.SubItems[1]?.Text}.\nОшибка: {ex.Message}",
                                            "Ошибка сохранения", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }

                    if (successCount > 0)
                    {
                        Trace.WriteLine($"[CarverResults] Сохранено {successCount} файлов вручную.");
                    }
                }
            }
        }

        private async void recoverAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                using (var fbd = new FolderBrowserDialog())
                {
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        var numFiles = listView1.Items.Count;
                        string currentFile = string.Empty;

                        this.taskRunner.Maximum = listView1.Items.Count;
                        this.taskRunner.Interval = 1;

                        List<FileSignature> signatures = new List<FileSignature>();
                        foreach (ListViewItem item in listView1.Items)
                        {
                            if (item.Tag is FileSignature sig)
                            {
                                signatures.Add(sig);
                            }
                        }

                        Trace.WriteLine($"[CarverResults] Запуск сохранения {numFiles} файлов...");

                        await taskRunner.RunTaskAsync("Save File",
                            (CancellationToken cancellationToken, IProgress<int> progress) =>
                            {
                                int p = 1;
                                foreach (var signature in signatures)
                                {
                                    if (cancellationToken.IsCancellationRequested) break;

                                    try
                                    {
                                        currentFile = signature.FileName;

                                        // Вызываем SaveFile, который может бросить исключение
                                        SaveFile(signature, fbd.SelectedPath);

                                        progress.Report(p++);
                                    }
                                    catch (Exception ex)
                                    {
                                        // Правило 1: Если один файл вызвал ошибку, не останавливаем весь процесс
                                        Trace.WriteLine($"[CarverResults] [Batch] Ошибка сохранения {signature.FileName}: {ex.Message}");

                                        // Увеличиваем счетчик прогресса, чтобы бар не завис
                                        progress.Report(p++);
                                    }
                                }
                            },
                            (int progressVal) =>
                            {
                                taskRunner.UpdateLabel($"{progressVal}/{numFiles}: {currentFile}");
                                taskRunner.UpdateProgress(progressVal);
                            },
                            () =>
                            {
                                // Правило 2: Trace.WriteLine вместо Console
                                Trace.WriteLine("[CarverResults] Сохранение всех файлов завершено.");
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[CarverResults] Критическая ошибка в recoverAll: {ex.Message}");
                MessageBox.Show($"Произошла ошибка при массовом сохранении: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}