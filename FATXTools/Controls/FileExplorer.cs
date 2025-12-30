// Переписано
using FATX.Analyzers;
using FATX.FileSystem;
using FATXTools.Dialogs;
using FATXTools.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics; // 1. Подключаем Trace
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace FATXTools.Controls
{
    public partial class FileExplorer : UserControl
    {
        private Color deletedColor = Color.FromArgb(255, 200, 200);

        private PartitionView parent;
        private Volume volume;

        public event EventHandler OnMetadataAnalyzerCompleted;
        public event EventHandler OnFileCarverCompleted;

        private ListViewItemComparer listViewItemComparer;

        private TaskRunner taskRunner;

        private enum NodeType
        {
            Root,
            Dirent
        }

        private struct NodeTag
        {
            public object Tag;
            public NodeType Type;

            public NodeTag(object tag, NodeType type)
            {
                this.Tag = tag;
                this.Type = type;
            }
        }

        public FileExplorer(PartitionView parent, TaskRunner taskRunner, Volume volume)
        {
            try
            {
                InitializeComponent();

                this.parent = parent;
                this.taskRunner = taskRunner;
                this.volume = volume;

                this.listViewItemComparer = new ListViewItemComparer();
                this.listView1.ListViewItemSorter = this.listViewItemComparer;

                var rootNode = treeView1.Nodes.Add("Root");
                rootNode.Tag = new NodeTag(null, NodeType.Root);

                PopulateTreeNodeDirectory(rootNode, volume.GetRoot());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileExplorer] Критическая ошибка инициализации FileExplorer: {ex.Message}");
            }
        }

        private void PopulateTreeNodeDirectory(TreeNode parentNode, List<DirectoryEntry> dirents)
        {
            if (dirents == null) return;

            foreach (var dirent in dirents)
            {
                try
                {
                    if (dirent.IsDirectory())
                    {
                        TreeNode node = parentNode.Nodes.Add(dirent.FileName);

                        node.Tag = new NodeTag(dirent, NodeType.Dirent);

                        if (dirent.IsDeleted())
                        {
                            node.ForeColor = Color.FromArgb(100, 100, 100);
                        }

                        PopulateTreeNodeDirectory(node, dirent.Children);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[FileExplorer] Ошибка добавления узла дерева {dirent.FileName}: {ex.Message}");
                }
            }
        }

        private void PopulateListView(List<DirectoryEntry> dirents, DirectoryEntry parent)
        {
            listView1.BeginUpdate();
            listView1.Items.Clear();

            // Add "up" item
            var upDir = listView1.Items.Add("");
            upDir.SubItems.Add("...");
            if (parent != null)
            {
                if (parent.GetParent() != null)
                {
                    upDir.Tag = new NodeTag(parent.GetParent(), NodeType.Dirent);
                }
                else
                {
                    upDir.Tag = new NodeTag(null, NodeType.Root);
                }
            }
            else
            {
                upDir.Tag = new NodeTag(null, NodeType.Root);
            }

            List<ListViewItem> items = new List<ListViewItem>();
            int index = 1;

            foreach (DirectoryEntry dirent in dirents)
            {
                try
                {
                    ListViewItem item = new ListViewItem(index.ToString());
                    item.Tag = new NodeTag(dirent, NodeType.Dirent);

                    item.SubItems.Add(dirent.FileName);

                    // Правило 1: Защита при получении даты
                    DateTime creationTime = new DateTime();
                    DateTime lastWriteTime = new DateTime();
                    DateTime lastAccessTime = new DateTime();

                    try
                    {
                        creationTime = dirent.CreationTime.AsDateTime();
                        lastWriteTime = dirent.LastWriteTime.AsDateTime();
                        lastAccessTime = dirent.LastAccessTime.AsDateTime();
                    }
                    catch { /* Используем MinValue если парсинг упал */ }

                    string sizeStr = "";
                    if (!dirent.IsDirectory())
                    {
                        sizeStr = Utility.FormatBytes(dirent.FileSize);
                    }

                    item.SubItems.Add(sizeStr);
                    item.SubItems.Add(creationTime.ToString());
                    item.SubItems.Add(lastWriteTime.ToString());
                    item.SubItems.Add(lastAccessTime.ToString());
                    item.SubItems.Add("0x" + dirent.Offset.ToString("x"));
                    item.SubItems.Add(dirent.Cluster.ToString());

                    if (dirent.IsDeleted())
                    {
                        item.BackColor = deletedColor;
                    }

                    index++;
                    items.Add(item);
                }
                catch (Exception ex)
                {
                    // Правило 1: Одна запись не должна ломать отрисовку списка
                    Trace.WriteLine($"[FileExplorer] Ошибка добавления файла в список {dirent.FileName}: {ex.Message}");
                }
            }

            listView1.Items.AddRange(items.ToArray());
            listView1.EndUpdate();
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            try
            {
                NodeTag nodeTag = (NodeTag)e.Node.Tag;

                switch (nodeTag.Type)
                {
                    case NodeType.Dirent:
                        DirectoryEntry dirent = nodeTag.Tag as DirectoryEntry;

                        if (dirent.IsDeleted())
                        {
                            // Правило 2: Trace.WriteLine вместо Console
                            Trace.WriteLine($"[FileExplorer] Попытка загрузки содержимого удаленной директории: {dirent.FileName}");
                        }
                        else
                        {
                            PopulateListView(dirent.Children, dirent);
                        }

                        break;
                    case NodeType.Root:

                        PopulateListView(this.volume.GetRoot(), null);

                        break;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileExplorer] Ошибка выбора директории: {ex.Message}");
            }
        }

        private void listView1_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                // Правило 1: Проверка выбранности элементов
                if (listView1.SelectedItems.Count == 0)
                {
                    return;
                }

                ListViewItem item = listView1.SelectedItems[0];
                NodeTag nodeTag = (NodeTag)item.Tag;

                switch (nodeTag.Type)
                {
                    case NodeType.Dirent:
                        DirectoryEntry dirent = (DirectoryEntry)nodeTag.Tag;

                        if (!dirent.IsDirectory())
                        {
                            return;
                        }

                        if (dirent.IsDeleted())
                        {
                            // Правило 2: Trace.WriteLine вместо Console
                            Trace.WriteLine($"[FileExplorer] Попытка отображения содержимого удаленной директории: {dirent.FileName}");
                        }
                        else
                        {
                            PopulateListView(dirent.Children, dirent);
                        }

                        break;

                    case NodeType.Root:
                        PopulateListView(this.volume.GetRoot(), null);
                        break;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileExplorer] Ошибка двойного клика: {ex.Message}");
            }
        }

        private async void runMetadataAnalyzerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                // TODO: Make into a user controlled setting
                var searchLength = this.volume.FileAreaLength;
                var searchInterval = this.volume.BytesPerCluster;

                taskRunner.Maximum = searchLength;
                taskRunner.Interval = searchInterval;

                MetadataAnalyzer analyzer = new MetadataAnalyzer(this.volume, searchInterval, searchLength);
                var numBlocks = searchLength / searchInterval;

                Trace.WriteLine($"[FileExplorer] Запуск анализатора метаданных...");

                await taskRunner.RunTaskAsync("Metadata Analyzer",
                    // Task
                    (CancellationToken cancellationToken, IProgress<int> progress) =>
                    {
                        analyzer.Analyze(cancellationToken, progress);
                    },
                    // Progress Update
                    (int progress) =>
                    {
                        taskRunner.UpdateLabel($"Processing cluster {progress}/{numBlocks}");
                        taskRunner.UpdateProgress(progress);
                    },
                    // On Task Completion
                    () =>
                    {
                        OnMetadataAnalyzerCompleted?.Invoke(this, new MetadataAnalyzerResults()
                        {
                            analyzer = analyzer
                        });
                    });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileExplorer] Ошибка запуска анализатора метаданных: {ex.Message}");
            }
        }

        private async void runFileCarverToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                // TODO: Make into a user controlled setting
                var searchLength = this.volume.FileAreaLength;
                var searchInterval = Properties.Settings.Default.FileCarverInterval;

                taskRunner.Maximum = searchLength;
                taskRunner.Interval = (long)searchInterval;

                FileCarver carver = new FileCarver(this.volume, searchInterval, searchLength);
                var numBlocks = searchLength / (long)searchInterval;

                Trace.WriteLine($"[FileExplorer] Запуск FileCarver...");

                await taskRunner.RunTaskAsync("File Carver",
                    // Task
                    (CancellationToken cancellationToken, IProgress<int> progress) =>
                    {
                        carver.Analyze(cancellationToken, progress);
                    },
                    // Progress Update
                    (int progress) =>
                    {
                        taskRunner.UpdateLabel($"Processing block {progress}/{numBlocks}");
                        taskRunner.UpdateProgress(progress);
                    },
                    // On Task Completion
                    () =>
                    {
                        OnFileCarverCompleted?.Invoke(this, new FileCarverResults()
                        {
                            carver = carver
                        });
                    });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileExplorer] Ошибка запуска FileCarver: {ex.Message}");
            }
        }

        private void SaveNodeTag(string path, NodeTag nodeTag)
        {
            try
            {
                switch (nodeTag.Type)
                {
                    case NodeType.Dirent:
                        DirectoryEntry dirent = nodeTag.Tag as DirectoryEntry;

                        RunSaveDirectoryEntryTaskAsync(path, dirent);

                        break;
                    case NodeType.Root:
                        RunSaveAllTaskAsync(path, volume.GetRoot());
                        break;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileExplorer] Ошибка SaveNodeTag: {ex.Message}");
            }
        }

        private void treeSaveSelectedToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                SaveNodeTag(dialog.SelectedPath, (NodeTag)treeView1.SelectedNode.Tag);
            }
        }

        private void listSaveSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (listView1.SelectedItems.Count == 0)
                    return;

                FolderBrowserDialog dialog = new FolderBrowserDialog();
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    List<DirectoryEntry> selectedFiles = new List<DirectoryEntry>();

                    foreach (ListViewItem selected in listView1.SelectedItems)
                    {
                        NodeTag nodeTag = (NodeTag)selected.Tag;
                        switch (nodeTag.Type)
                        {
                            case NodeType.Dirent:
                                DirectoryEntry dirent = nodeTag.Tag as DirectoryEntry;

                                selectedFiles.Add(dirent);

                                break;
                        }
                    }

                    RunSaveAllTaskAsync(dialog.SelectedPath, selectedFiles);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileExplorer] Ошибка сохранения выделенного: {ex.Message}");
            }
        }

        private async void RunSaveDirectoryEntryTaskAsync(string path, DirectoryEntry dirent)
        {
            SaveContentTask saveContentTask = null;

            var numFiles = dirent.CountFiles();
            taskRunner.Maximum = numFiles;
            taskRunner.Interval = 1;

            await taskRunner.RunTaskAsync("Save File",
                (CancellationToken cancellationToken, IProgress<int> progress) =>
                {
                    saveContentTask = new SaveContentTask(this.volume, cancellationToken, progress);
                    saveContentTask.Save(path, dirent);
                },
                (int progress) =>
                {
                    string currentFile = saveContentTask.GetCurrentFile();
                    taskRunner.UpdateLabel($"{progress}/{numFiles}: {currentFile}");
                    taskRunner.UpdateProgress(progress);
                },
                () =>
                {
                    Trace.WriteLine("[FileExplorer] Сохранение директории завершено.");
                });
        }

        private async void RunSaveAllTaskAsync(string path, List<DirectoryEntry> dirents)
        {
            SaveContentTask saveContentTask = null;
            var numFiles = volume.CountFiles();
            taskRunner.Maximum = numFiles;
            taskRunner.Interval = 1;

            await taskRunner.RunTaskAsync("Save All",
                (CancellationToken cancellationToken, IProgress<int> progress) =>
                {
                    saveContentTask = new SaveContentTask(this.volume, cancellationToken, progress);
                    saveContentTask.SaveAll(path, dirents);
                },
                (int progress) =>
                {
                    string currentFile = saveContentTask.GetCurrentFile();
                    taskRunner.UpdateLabel($"{progress}/{numFiles}: {currentFile}");
                    taskRunner.UpdateProgress(progress);
                },
                () =>
                {
                    Trace.WriteLine("[FileExplorer] Сохранение всех файлов завершено.");
                });
        }

        private void saveAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                RunSaveAllTaskAsync(dialog.SelectedPath, volume.GetRoot());
            }
        }

        private void saveAllToolStripMenuItem2_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                RunSaveAllTaskAsync(dialog.SelectedPath, volume.GetRoot());
            }
        }

        private void viewInformationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (listView1.SelectedItems.Count == 0)
                    return;

                NodeTag nodeTag = (NodeTag)listView1.SelectedItems[0].Tag;

                switch (nodeTag.Type)
                {
                    case NodeType.Dirent:
                        DirectoryEntry dirent = (DirectoryEntry)nodeTag.Tag;

                        FileInfoDialog dialog = new FileInfoDialog(this.volume, dirent);
                        dialog.ShowDialog();

                        break;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileExplorer] Ошибка просмотра информации: {ex.Message}");
            }
        }

        public class SaveContentTask
        {
            private CancellationToken cancellationToken;

            private IProgress<int> progress;

            private Volume volume;

            private string currentFile;

            private int numSaved;

            public SaveContentTask(Volume volume, CancellationToken cancellationToken, IProgress<int> progress)
            {
                currentFile = String.Empty;

                this.cancellationToken = cancellationToken;
                this.progress = progress;
                this.volume = volume;

                this.numSaved = 0;
            }

            public string GetCurrentFile()
            {
                return currentFile;
            }

            private DialogResult ShowIOErrorDialog(Exception e)
            {
                Trace.WriteLine($"[SaveContentTask] Ошибка IO: {e.Message}");
                return MessageBox.Show($"Не удалось записать файл: {e.Message}\n\nПовторить?", "Ошибка записи", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
            }

            private void WriteFile(string path, DirectoryEntry dirent, List<uint> chainMap)
            {
                using (FileStream outFile = File.OpenWrite(path))
                {
                    uint bytesLeft = dirent.FileSize;

                    foreach (uint cluster in chainMap)
                    {
                        byte[] clusterData = null;

                        try
                        {
                            clusterData = this.volume.ReadCluster(cluster);
                        }
                        catch (IOException exception)
                        {
                            // Failed to read cluster, write null bytes instead.
                            var position = outFile.Position;
                            // Правило 2: Trace.WriteLine вместо Console
                            Trace.WriteLine($"[SaveContentTask] {exception.Message}");
                            Trace.WriteLine($"[SaveContentTask] Из-за исключения, записан пустой кластер в файл: {path} (Offset: {position})");
                            clusterData = new byte[volume.BytesPerCluster];
                        }

                        var writeSize = Math.Min(bytesLeft, this.volume.BytesPerCluster);

                        if (clusterData != null)
                        {
                            outFile.Write(clusterData, 0, (int)writeSize);
                        }

                        bytesLeft -= writeSize;
                    }
                }
            }

            private void FileSetTimeStamps(string path, DirectoryEntry dirent)
            {
                try
                {
                    File.SetCreationTime(path, dirent.CreationTime.AsDateTime());
                    File.SetLastWriteTime(path, dirent.LastWriteTime.AsDateTime());
                    File.SetLastAccessTime(path, dirent.LastAccessTime.AsDateTime());
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[SaveContentTask] Ошибка установки временных меток для {path}: {ex.Message}");
                }
            }

            private void DirectorySetTimestamps(string path, DirectoryEntry dirent)
            {
                try
                {
                    Directory.SetCreationTime(path, dirent.CreationTime.AsDateTime());
                    Directory.SetLastWriteTime(path, dirent.LastWriteTime.AsDateTime());
                    Directory.SetLastAccessTime(path, dirent.LastAccessTime.AsDateTime());
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[SaveContentTask] Ошибка установки меток директории для {path}: {ex.Message}");
                }
            }

            private void TryIOOperation(Action action)
            {
                int retries = 0;
                while (true)
                {
                    var dialogResult = DialogResult.Retry;
                    try
                    {
                        action();
                        return; // Success
                    }
                    catch (IOException e)
                    {
                        dialogResult = ShowIOErrorDialog(e);
                    }
                    catch (Exception ex)
                    {
                        // Другие исключения не ретраим
                        Trace.WriteLine($"[SaveContentTask] Критическая ошибка (не IO): {ex.Message}");
                        throw;
                    }

                    if (dialogResult == DialogResult.Retry)
                    {
                        retries++;
                        if (retries > 3)
                        {
                            Trace.WriteLine("[SaveContentTask] Превышен лимит попыток записи.");
                            throw new IOException("Превышен лимит попыток записи.");
                        }
                    }
                    else if (dialogResult == DialogResult.Cancel)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new OperationCanceledException("Пользователь отменил сохранение.");
                    }
                }
            }

            private void SaveFile(string path, DirectoryEntry dirent)
            {
                // Правило 1: Безопасная склейка путей
                string safePath = Path.Combine(path, dirent.FileName);

                Console.WriteLine(safePath); // Оригинал
                Trace.WriteLine($"[SaveContentTask] Сохранение файла: {safePath}");

                // Report where we are at
                currentFile = dirent.FileName;
                progress.Report(numSaved++);

                List<uint> chainMap = this.volume.GetClusterChain(dirent);

                TryIOOperation(() =>
                {
                    WriteFile(safePath, dirent, chainMap);
                    FileSetTimeStamps(safePath, dirent);
                });

                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            private void SaveDirectory(string path, DirectoryEntry dirent)
            {
                string safePath = Path.Combine(path, dirent.FileName);

                Console.WriteLine(safePath);
                Trace.WriteLine($"[SaveContentTask] Сохранение директории: {safePath}");

                // Report where we are at
                currentFile = dirent.FileName;
                progress.Report(numSaved++);

                Directory.CreateDirectory(path); // Создаем родительскую папку

                string childPath = safePath; // Передаем путь текущей папки как родитель

                foreach (DirectoryEntry child in dirent.Children)
                {
                    SaveDirectoryEntry(childPath, child);
                }

                TryIOOperation(() =>
                {
                    DirectorySetTimestamps(safePath, dirent);
                });

                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            private void SaveDeleted(string path, DirectoryEntry dirent)
            {
                string safePath = Path.Combine(path, dirent.FileName);

                currentFile = dirent.GetFullPath();

                Trace.WriteLine($"[SaveContentTask] {safePath}: Не удалось сохранить удаленные файлы.");
            }

            private void SaveDirectoryEntry(string path, DirectoryEntry dirent)
            {
                try
                {
                    if (dirent.IsDeleted())
                    {
                        SaveDeleted(path, dirent);
                        return;
                    }

                    if (dirent.IsDirectory())
                    {
                        SaveDirectory(path, dirent);
                    }
                    else
                    {
                        SaveFile(path, dirent);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[SaveContentTask] Ошибка в SaveDirectoryEntry ({dirent.FileName}): {ex.Message}");
                }
            }

            public void Save(string path, DirectoryEntry dirent)
            {
                SaveDirectoryEntry(path, dirent);
            }

            public void SaveAll(string path, List<DirectoryEntry> dirents)
            {
                foreach (var dirent in dirents)
                {
                    SaveDirectoryEntry(path, dirent);
                }
            }
        }

        private void listView1_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            try
            {
                listViewItemComparer.Column = (ColumnIndex)e.Column;

                if (listViewItemComparer.Order == SortOrder.Ascending)
                {
                    listViewItemComparer.Order = SortOrder.Descending;
                }
                else
                {
                    listViewItemComparer.Order = SortOrder.Ascending;
                }

                listView1.Sort();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FileExplorer] Ошибка сортировки колонки: {ex.Message}");
            }
        }

        public enum ColumnIndex
        {
            Index,
            Name,
            Size,
            Created,
            Modified,
            Accessed,
            Offset,
            Cluster
        }

        class ListViewItemComparer : IComparer
        {
            private ColumnIndex column;
            private SortOrder order;

            public ColumnIndex Column
            {
                get => column;
                set => column = value;
            }

            public SortOrder Order
            {
                get => order;
                set => order = value;
            }

            public ListViewItemComparer()
            {
                this.order = SortOrder.Ascending;
                this.column = 0;
            }

            public ListViewItemComparer(ColumnIndex column)
            {
                this.column = column;
            }

            public int Compare(object x, object y)
            {
                // Default, don't swap order.
                int result = 0;

                ListViewItem itemX = (ListViewItem)x;
                ListViewItem itemY = (ListViewItem)y;

                // Правило 1: Проверка на null тегов
                if (itemX?.Tag == null || itemY?.Tag == null)
                {
                    return result;
                }

                // Skip "up" item (Index == 0 is handled by string parsing logic below implicitly if needed, 
                // but checking Tag type is safer)
                NodeTag tagX = (NodeTag)itemX.Tag;
                NodeTag tagY = (NodeTag)itemY.Tag;

                if (tagX.Type != NodeType.Dirent || tagY.Type != NodeType.Dirent)
                {
                    return result;
                }

                DirectoryEntry direntX = (DirectoryEntry)tagX.Tag;
                DirectoryEntry direntY = (DirectoryEntry)tagY.Tag;

                if (direntX == null || direntY == null) return result;

                switch (column)
                {
                    case ColumnIndex.Index:
                        // Безопасный парсинг
                        uint valX = 0, valY = 0;
                        if (uint.TryParse(itemX.Text, out valX) && uint.TryParse(itemY.Text, out valY))
                            result = valX.CompareTo(valY);
                        break;
                    case ColumnIndex.Name:
                        result = string.Compare(direntX.FileName, direntY.FileName);
                        break;
                    case ColumnIndex.Size:
                        result = direntX.FileSize.CompareTo(direntY.FileSize);
                        break;
                    case ColumnIndex.Created:
                        // Правило 1: Защита при сортировке по дате
                        try
                        {
                            result = direntX.CreationTime.AsDateTime().CompareTo(direntY.CreationTime.AsDateTime());
                        }
                        catch (Exception)
                        {
                            result = 0;
                        }
                        break;
                    case ColumnIndex.Modified:
                        try
                        {
                            result = direntX.LastWriteTime.AsDateTime().CompareTo(direntY.LastWriteTime.AsDateTime());
                        }
                        catch (Exception)
                        {
                            result = 0;
                        }
                        break;
                    case ColumnIndex.Accessed:
                        try
                        {
                            result = direntX.LastAccessTime.AsDateTime().CompareTo(direntY.LastAccessTime.AsDateTime());
                        }
                        catch (Exception)
                        {
                            result = 0;
                        }
                        break;
                    case ColumnIndex.Offset:
                        result = direntX.Offset.CompareTo(direntY.Offset);
                        break;
                    case ColumnIndex.Cluster:
                        result = direntX.Cluster.CompareTo(direntY.Cluster);
                        break;
                }

                if (order == SortOrder.Ascending)
                {
                    return result;
                }
                else
                {
                    return -result;
                }
            }
        }
    }
}