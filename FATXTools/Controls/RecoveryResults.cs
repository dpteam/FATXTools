// Переписано
using FATX.Analyzers;
using FATX.FileSystem;
using FATXTools.Database;
using FATXTools.Dialogs;
using FATXTools.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace FATXTools
{
    public partial class RecoveryResults : UserControl
    {
        private Volume _volume;
        private TaskRunner _taskRunner;

        /// <summary>
        /// Сопоставление индекса кластера с его записями каталогов.
        /// </summary>
        private Dictionary<uint, List<DatabaseFile>> clusterNodes =
            new Dictionary<uint, List<DatabaseFile>>();

        /// <summary>
        /// Ссылка на узел текущего кластера.
        /// </summary>
        private TreeNode currentClusterNode;

        private ListViewItemComparer listViewItemComparer;

        private IntegrityAnalyzer _integrityAnalyzer;

        private FileDatabase _fileDatabase;

        private Color[] statusColor = new Color[]
        {
            Color.FromArgb(150, 250, 150), // Green
            Color.FromArgb(200, 250, 150), // Yellow-Green
            Color.FromArgb(250, 250, 150),
            Color.FromArgb(250, 200, 150),
            Color.FromArgb(250, 150, 150),
        };

        public event EventHandler NotifyDatabaseChanged;

        public RecoveryResults(FileDatabase database, IntegrityAnalyzer integrityAnalyzer, TaskRunner taskRunner)
        {
            InitializeComponent();

            this._fileDatabase = database;
            this._integrityAnalyzer = integrityAnalyzer;
            this._taskRunner = taskRunner;
            this._volume = database.GetVolume();

            listViewItemComparer = new ListViewItemComparer();
            listView1.ListViewItemSorter = listViewItemComparer;

            try
            {
                PopulateTreeView(database.GetRootFiles());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Критическая ошибка при инициализации дерева: {ex.Message}");
            }
        }

        private enum NodeType
        {
            Cluster,
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

        private void PopulateFolder(List<DatabaseFile> children, TreeNode parent)
        {
            if (children == null) return;

            foreach (var child in children)
            {
                try
                {
                    if (child.IsDirectory())
                    {
                        var childNode = parent.Nodes.Add(child.FileName);
                        childNode.Tag = new NodeTag(child, NodeType.Dirent);
                        PopulateFolder(child.Children, childNode);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Ошибка при добавлении папки '{child?.FileName}': {ex.Message}");
                }
            }
        }

        private void RefreshTreeView()
        {
            try
            {
                PopulateTreeView(_fileDatabase.GetRootFiles());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Ошибка при обновлении дерева: {ex.Message}");
            }
        }

        public void PopulateTreeView(List<DatabaseFile> results)
        {
            // Удаление всех узлов
            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();
            clusterNodes.Clear();

            if (results == null)
            {
                treeView1.EndUpdate();
                return;
            }

            Trace.WriteLine($"Начало заполнения дерева, найдено записей: {results.Count}");

            foreach (var result in results)
            {
                try
                {
                    var cluster = result.Cluster;
                    if (!clusterNodes.ContainsKey(cluster))
                    {
                        List<DatabaseFile> list = new List<DatabaseFile>()
                        {
                            result
                        };

                        clusterNodes.Add(cluster, list);
                    }
                    else
                    {
                        var list = clusterNodes[cluster];
                        list.Add(result);
                    }

                    var clusterNodeText = "Cluster " + result.Cluster;
                    TreeNode clusterNode;
                    if (!treeView1.Nodes.ContainsKey(clusterNodeText))
                    {
                        clusterNode = treeView1.Nodes.Add(clusterNodeText, clusterNodeText);
                        clusterNode.Tag = new NodeTag(clusterNodes[cluster], NodeType.Cluster);
                    }
                    else
                    {
                        clusterNode = treeView1.Nodes[clusterNodeText];
                    }

                    if (result.IsDirectory())
                    {
                        var rootNode = clusterNode.Nodes.Add(result.FileName);
                        rootNode.Tag = new NodeTag(result, NodeType.Dirent);
                        PopulateFolder(result.Children, rootNode);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Ошибка при обработке записи '{result.FileName}': {ex.Message}");
                }
            }

            treeView1.EndUpdate();
            Trace.WriteLine("Заполнение дерева завершено.");
        }

        private void PopulateListView(List<DatabaseFile> dirents, DatabaseFile parent)
        {
            listView1.BeginUpdate();
            listView1.Items.Clear();

            try
            {
                var upDir = listView1.Items.Add("");

                upDir.SubItems.Add("...");
                if (parent != null)
                {
                    upDir.Tag = new NodeTag(parent, NodeType.Dirent);
                }
                else
                {
                    NodeTag nodeTag = (NodeTag)currentClusterNode?.Tag; // Добавлена проверка на null
                    if (nodeTag.Tag != null)
                    {
                        upDir.Tag = new NodeTag(nodeTag.Tag as List<DatabaseFile>, NodeType.Cluster);
                    }
                }

                List<ListViewItem> items = new List<ListViewItem>();
                int index = 1;

                if (dirents != null)
                {
                    foreach (DatabaseFile databaseFile in dirents)
                    {
                        try
                        {
                            ListViewItem item = new ListViewItem(index.ToString());
                            item.Tag = new NodeTag(databaseFile, NodeType.Dirent);

                            item.SubItems.Add(databaseFile.FileName);

                            DateTime creationTime = databaseFile.CreationTime.AsDateTime();
                            DateTime lastWriteTime = databaseFile.LastWriteTime.AsDateTime();
                            DateTime lastAccessTime = databaseFile.LastAccessTime.AsDateTime();

                            string sizeStr = "";
                            if (!databaseFile.IsDirectory())
                            {
                                item.ImageIndex = 1;
                                sizeStr = Utility.FormatBytes(databaseFile.FileSize);
                            }
                            else
                            {
                                item.ImageIndex = 0;
                            }

                            item.SubItems.Add(sizeStr);
                            item.SubItems.Add(creationTime.ToString());
                            item.SubItems.Add(lastWriteTime.ToString());
                            item.SubItems.Add(lastAccessTime.ToString());
                            item.SubItems.Add("0x" + databaseFile.Offset.ToString("x"));
                            item.SubItems.Add(databaseFile.Cluster.ToString());

                            // Защита от выхода за границы массива цветов
                            if (databaseFile.GetRanking() >= 0 && databaseFile.GetRanking() < statusColor.Length)
                            {
                                item.BackColor = statusColor[databaseFile.GetRanking()];
                            }

                            index++;

                            items.Add(item);
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Ошибка при добавлении файла в список: {databaseFile?.FileName}. {ex.Message}");
                        }
                    }
                }

                listView1.Items.AddRange(items.ToArray());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Критическая ошибка при заполнении списка: {ex.Message}");
            }
            finally
            {
                listView1.EndUpdate();
            }
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            try
            {
                currentClusterNode = e.Node;
                while (currentClusterNode.Parent != null)
                {
                    currentClusterNode = currentClusterNode.Parent;
                }

                Trace.WriteLine($"Текущий кластер: {currentClusterNode.Text}");

                NodeTag nodeTag = (NodeTag)e.Node.Tag;
                switch (nodeTag.Type)
                {
                    case NodeType.Cluster:
                        List<DatabaseFile> dirents = (List<DatabaseFile>)nodeTag.Tag;
                        PopulateListView(dirents, null);
                        break;

                    case NodeType.Dirent:
                        DatabaseFile databaseFile = (DatabaseFile)nodeTag.Tag;
                        PopulateListView(databaseFile.Children, databaseFile.GetParent());
                        break;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Ошибка при выборе узла дерева: {ex.Message}");
            }
        }

        private void listView1_DoubleClick(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count > 1)
                return;

            try
            {
                //Trace.WriteLine($"Текущий кластер: {currentClusterNode.Text}");

                NodeTag nodeTag = (NodeTag)listView1.SelectedItems[0].Tag;

                switch (nodeTag.Type)
                {
                    case NodeType.Dirent:
                        DatabaseFile databaseFile = nodeTag.Tag as DatabaseFile;

                        if (databaseFile.IsDirectory())
                        {
                            PopulateListView(databaseFile.Children, databaseFile.GetParent());
                        }

                        break;

                    case NodeType.Cluster:
                        List<DatabaseFile> dirents = nodeTag.Tag as List<DatabaseFile>;
                        PopulateListView(dirents, null);
                        break;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Ошибка при двойном клике: {ex.Message}");
            }
        }

        private long CountFiles(List<DatabaseFile> dirents)
        {
            // DirectoryEntry.CountFiles не считает удаленные файлы
            long numFiles = 0;

            if (dirents == null) return 0;

            foreach (var databaseFile in dirents)
            {
                try
                {
                    if (databaseFile.IsDirectory())
                    {
                        numFiles += CountFiles(databaseFile.Children) + 1;
                    }
                    else
                    {
                        numFiles++;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Ошибка при подсчете файлов: {ex.Message}");
                }
            }

            return numFiles;
        }

        private async void RunRecoverAllTaskAsync(string path, Dictionary<string, List<DatabaseFile>> clusters)
        {
            RecoveryTask recoverTask = null;
            long numFiles = 0;

            try
            {
                foreach (var cluster in clusters)
                {
                    numFiles += CountFiles(cluster.Value);
                }

                _taskRunner.Maximum = numFiles;
                _taskRunner.Interval = 1;

                Trace.WriteLine($"Запуск задачи сохранения всех файлов. Всего файлов: {numFiles}");

                await _taskRunner.RunTaskAsync("Сохранение файлов",
                    (CancellationToken cancellationToken, IProgress<int> progress) =>
                    {
                        recoverTask = new RecoveryTask(this._volume, cancellationToken, progress);
                        foreach (var cluster in clusters)
                        {
                            try
                            {
                                string clusterDir = Path.Combine(path, cluster.Key);
                                Directory.CreateDirectory(clusterDir);
                                recoverTask.SaveAll(clusterDir, cluster.Value);
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"Ошибка при обработке кластера {cluster.Key}: {ex.Message}");
                            }
                        }
                    },
                    (int progress) =>
                    {
                        string currentFile = recoverTask?.GetCurrentFile() ?? "Неизвестно";
                        _taskRunner.UpdateLabel($"{progress}/{numFiles}: {currentFile}");
                        _taskRunner.UpdateProgress(progress);
                    },
                    () =>
                    {
                        Trace.WriteLine("Сохранение файлов завершено.");
                    });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Критическая ошибка в задаче сохранения всех файлов: {ex.Message}");
            }
        }

        private async void RunRecoverDirectoryEntryTaskAsync(string path, DatabaseFile databaseFile)
        {
            RecoveryTask recoverTask = null;

            try
            {
                var numFiles = databaseFile.CountFiles();
                _taskRunner.Maximum = numFiles;
                _taskRunner.Interval = 1;

                Trace.WriteLine($"Запуск задачи сохранения директории: {databaseFile.FileName}");

                await _taskRunner.RunTaskAsync("Сохранение файла",
                    (CancellationToken cancellationToken, IProgress<int> progress) =>
                    {
                        recoverTask = new RecoveryTask(this._volume, cancellationToken, progress);
                        recoverTask.Save(path, databaseFile);
                    },
                    (int progress) =>
                    {
                        string currentFile = recoverTask?.GetCurrentFile() ?? "Неизвестно";
                        _taskRunner.UpdateLabel($"{progress}/{numFiles}: {currentFile}");
                        _taskRunner.UpdateProgress(progress);
                    },
                    () =>
                    {
                        Trace.WriteLine("Сохранение директории завершено.");
                    });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Ошибка сохранения директории {databaseFile.FileName}: {ex.Message}");
            }
        }

        private async void RunRecoverClusterTaskAsync(string path, List<DatabaseFile> dirents)
        {
            RecoveryTask recoverTask = null;

            try
            {
                long numFiles = CountFiles(dirents);

                _taskRunner.Maximum = numFiles;
                _taskRunner.Interval = 1;

                Trace.WriteLine($"Запуск задачи сохранения выбранного. Путь: {path}");

                await _taskRunner.RunTaskAsync("Сохранить все",
                    (CancellationToken cancellationToken, IProgress<int> progress) =>
                    {
                        try
                        {
                            recoverTask = new RecoveryTask(this._volume, cancellationToken, progress);
                            recoverTask.SaveAll(path, dirents);
                        }
                        catch (OperationCanceledException)
                        {
                            Trace.WriteLine("Сохранение отменено пользователем.");
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Ошибка при выполнении сохранения: {ex.Message}");
                            throw; // Пробрасываем дальше, если нужно корректно завершить задачу
                        }
                    },
                    (int progress) =>
                    {
                        string currentFile = recoverTask?.GetCurrentFile() ?? "Неизвестно";
                        _taskRunner.UpdateLabel($"{progress}/{numFiles}: {currentFile}");
                        _taskRunner.UpdateProgress(progress);
                    },
                    () =>
                    {
                        Trace.WriteLine("Сохранение выбранного завершено.");
                    });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Критическая ошибка в задаче сохранения: {ex.Message}");
            }
        }

        private void listRecoverSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 0)
                return;

            try
            {
                var selectedItems = listView1.SelectedItems;
                using (var dialog = new FolderBrowserDialog())
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        List<DatabaseFile> selectedFiles = new List<DatabaseFile>();

                        foreach (ListViewItem selectedItem in selectedItems)
                        {
                            NodeTag nodeTag = (NodeTag)selectedItem.Tag;

                            switch (nodeTag.Type)
                            {
                                case NodeType.Dirent:
                                    DatabaseFile databaseFile = nodeTag.Tag as DatabaseFile;
                                    selectedFiles.Add(databaseFile);
                                    break;
                            }
                        }

                        RunRecoverClusterTaskAsync(dialog.SelectedPath, selectedFiles);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Ошибка при запуске восстановления выбранных: {ex.Message}");
            }
        }

        private void listRecoverCurrentDirectoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        List<DatabaseFile> selectedFiles = new List<DatabaseFile>();

                        foreach (ListViewItem item in listView1.Items)
                        {
                            if (item.Index == 0) continue;

                            NodeTag nodeTag = (NodeTag)item.Tag;

                            switch (nodeTag.Type)
                            {
                                case NodeType.Dirent:
                                    DatabaseFile databaseFile = nodeTag.Tag as DatabaseFile;
                                    selectedFiles.Add(databaseFile);
                                    break;
                            }
                        }

                        RunRecoverClusterTaskAsync(dialog.SelectedPath, selectedFiles);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Ошибка при восстановлении текущей директории: {ex.Message}");
            }
        }

        private void listRecoverAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        RunRecoverClusterTaskAsync(dialog.SelectedPath, _fileDatabase.GetRootFiles());
                        Trace.WriteLine("Команда на восстановление всех файлов отправлена.");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Ошибка при восстановлении всех файлов: {ex.Message}");
            }
        }

        private void listRecoverCurrentClusterToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        var clusterNode = currentClusterNode;
                        if (clusterNode == null) return;

                        NodeTag nodeTag = (NodeTag)clusterNode.Tag;

                        string clusterDir = Path.Combine(dialog.SelectedPath, clusterNode.Text);

                        Directory.CreateDirectory(clusterDir);

                        switch (nodeTag.Type)
                        {
                            case NodeType.Cluster:
                                List<DatabaseFile> dirents = nodeTag.Tag as List<DatabaseFile>;
                                RunRecoverClusterTaskAsync(clusterDir, dirents);
                                break;
                        }

                        Trace.WriteLine($"Восстановление кластера {clusterNode.Text} завершено.");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Ошибка при восстановлении текущего кластера: {ex.Message}");
            }
        }

        private void treeRecoverSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        var selectedNode = treeView1.SelectedNode;
                        if (selectedNode == null) return;

                        NodeTag nodeTag = (NodeTag)selectedNode.Tag;
                        switch (nodeTag.Type)
                        {
                            case NodeType.Cluster:
                                List<DatabaseFile> dirents = nodeTag.Tag as List<DatabaseFile>;

                                string clusterDir = Path.Combine(dialog.SelectedPath, selectedNode.Text);
                                Directory.CreateDirectory(clusterDir);
                                RunRecoverClusterTaskAsync(clusterDir, dirents);
                                break;

                            case NodeType.Dirent:
                                DatabaseFile dirent = nodeTag.Tag as DatabaseFile;
                                RunRecoverClusterTaskAsync(dialog.SelectedPath, new List<DatabaseFile> { dirent });
                                break;
                        }

                        Trace.WriteLine("Восстановление выбранного элемента дерева завершено.");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Ошибка в меню восстановления выбранного (дерево): {ex.Message}");
            }
        }

        private void treeRecoverAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        Dictionary<string, List<DatabaseFile>> clusterList = new Dictionary<string, List<DatabaseFile>>();

                        foreach (TreeNode clusterNode in treeView1.Nodes)
                        {
                            NodeTag nodeTag = (NodeTag)clusterNode.Tag;
                            switch (nodeTag.Type)
                            {
                                case NodeType.Cluster:
                                    List<DatabaseFile> dirents = nodeTag.Tag as List<DatabaseFile>;
                                    clusterList[clusterNode.Text] = dirents;
                                    break;
                            }
                        }

                        RunRecoverAllTaskAsync(dialog.SelectedPath, clusterList);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Ошибка при восстановлении всех (дерево): {ex.Message}");
            }
        }

        private class RecoveryTask
        {
            private CancellationToken cancellationToken;
            private IProgress<int> progress;
            private Volume volume;

            private string currentFile = String.Empty;
            private int numSaved = 0;

            public RecoveryTask(Volume volume, CancellationToken cancellationToken, IProgress<int> progress)
            {
                this.volume = volume;
                this.cancellationToken = cancellationToken;
                this.progress = progress;
            }

            public string GetCurrentFile()
            {
                return currentFile;
            }

            private DialogResult ShowIOErrorDialog(Exception e)
            {
                return MessageBox.Show($"{e.Message}",
                    "Ошибка ввода-вывода", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
            }

            private void WriteFile(string path, DatabaseFile databaseFile)
            {
                using (FileStream outFile = File.OpenWrite(path))
                {
                    uint bytesLeft = databaseFile.FileSize;

                    foreach (uint cluster in databaseFile.ClusterChain)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        byte[] clusterData;

                        try
                        {
                            clusterData = this.volume.ReadCluster(cluster);
                        }
                        catch (IOException exception)
                        {
                            // Не удалось прочитать кластер, записываем нулевые байты вместо них.
                            var position = outFile.Position;
                            Trace.WriteLine($"Ошибка чтения кластера {cluster}: {exception.Message}. Запись нулей.");
                            clusterData = new byte[volume.BytesPerCluster];
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Неожиданная ошибка при чтении кластера {cluster}: {ex.Message}. Запись нулей.");
                            clusterData = new byte[volume.BytesPerCluster];
                        }

                        var writeSize = Math.Min(bytesLeft, this.volume.BytesPerCluster);
                        outFile.Write(clusterData, 0, (int)writeSize);

                        bytesLeft -= writeSize;
                    }
                }
            }

            private void FileSetTimeStamps(string path, DatabaseFile databaseFile)
            {
                try
                {
                    File.SetCreationTime(path, databaseFile.CreationTime.AsDateTime());
                    File.SetLastWriteTime(path, databaseFile.LastWriteTime.AsDateTime());
                    File.SetLastAccessTime(path, databaseFile.LastAccessTime.AsDateTime());
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Не удалось установить метки времени для файла {path}: {ex.Message}");
                }
            }

            private void DirectorySetTimestamps(string path, DatabaseFile databaseFile)
            {
                try
                {
                    Directory.SetCreationTime(path, databaseFile.CreationTime.AsDateTime());
                    Directory.SetLastWriteTime(path, databaseFile.LastWriteTime.AsDateTime());
                    Directory.SetLastAccessTime(path, databaseFile.LastAccessTime.AsDateTime());
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Не удалось установить метки времени для папки {path}: {ex.Message}");
                }
            }

            private void TryIOOperation(Action action)
            {
                try
                {
                    action();
                }
                catch (IOException e)
                {
                    while (true)
                    {
                        var dialogResult = ShowIOErrorDialog(e);

                        if (dialogResult == DialogResult.Retry)
                        {
                            try
                            {
                                action();
                                break; // Успешно
                            }
                            catch (Exception)
                            {
                                // Продолжаем цикл для повторной попытки
                                continue;
                            }
                        }
                        else
                        {
                            // Отмена
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Ошибка при выполнении операции IO: {ex.Message}");
                }
            }

            private void SaveDirectory(DatabaseFile databaseFile, string path)
            {
                path = Path.Combine(path, databaseFile.FileName);
                //Trace.WriteLine($"Обработка директории: {path}");

                currentFile = databaseFile.FileName;
                progress.Report(numSaved++);

                if (!Directory.Exists(path))
                {
                    try
                    {
                        Directory.CreateDirectory(path);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Не удалось создать директорию {path}: {ex.Message}");
                        return; // Некуда сохранять дочерние элементы
                    }
                }

                foreach (DatabaseFile child in databaseFile.Children)
                {
                    Save(path, child);
                }

                TryIOOperation(() =>
                {
                    DirectorySetTimestamps(path, databaseFile);
                });
            }

            private void SaveFile(DatabaseFile databaseFile, string path)
            {
                path = Path.Combine(path, databaseFile.FileName);
                //Trace.WriteLine($"Обработка файла: {path}");

                currentFile = databaseFile.FileName;
                progress.Report(numSaved++);

                try
                {
                    volume.SeekToCluster(databaseFile.FirstCluster);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Ошибка при поиске кластера для {path}: {ex.Message}");
                }

                TryIOOperation(() =>
                {
                    WriteFile(path, databaseFile);
                    FileSetTimeStamps(path, databaseFile);
                });
            }

            public void Save(string path, DatabaseFile databaseFile)
            {
                try
                {
                    if (databaseFile.IsDirectory())
                    {
                        SaveDirectory(databaseFile, path);
                    }
                    else
                    {
                        SaveFile(databaseFile, path);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Критическая ошибка при сохранении элемента {databaseFile.FileName}: {ex.Message}");
                    // Ошибка здесь не должна прерывать весь процесс сохранения остальных файлов
                }
            }

            public void SaveAll(string path, List<DatabaseFile> dirents)
            {
                foreach (var databaseFile in dirents)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    Save(path, databaseFile);
                }
            }
        }

        private void listView1_ColumnClick(object sender, ColumnClickEventArgs e)
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
                int result = 0;

                try
                {
                    ListViewItem itemX = (ListViewItem)x;
                    ListViewItem itemY = (ListViewItem)y;

                    if (itemX.Tag == null || itemY.Tag == null) return result;

                    // Skip "up" item (..)
                    if (itemX.Index == 0 || itemY.Index == 0)
                    {
                        // Если один из них "..", он должен быть всегда первым
                        return itemX.Index == 0 ? -1 : 1;
                    }

                    DatabaseFile direntX = (DatabaseFile)((NodeTag)itemX.Tag).Tag;
                    DatabaseFile direntY = (DatabaseFile)((NodeTag)itemY.Tag).Tag;

                    if (direntX == null || direntY == null) return result;

                    switch (column)
                    {
                        case ColumnIndex.Index:
                            if (uint.TryParse(itemX.Text, out uint valX) && uint.TryParse(itemY.Text, out uint valY))
                                result = valX.CompareTo(valY);
                            break;

                        case ColumnIndex.Name:
                            result = String.Compare(direntX.FileName, direntY.FileName);
                            break;

                        case ColumnIndex.Size:
                            result = direntX.FileSize.CompareTo(direntY.FileSize);
                            break;

                        case ColumnIndex.Created:
                            result = direntX.CreationTime.AsDateTime().CompareTo(direntY.CreationTime.AsDateTime());
                            break;

                        case ColumnIndex.Modified:
                            result = direntX.LastWriteTime.AsDateTime().CompareTo(direntY.LastWriteTime.AsDateTime());
                            break;

                        case ColumnIndex.Accessed:
                            result = direntX.LastAccessTime.AsDateTime().CompareTo(direntY.LastAccessTime.AsDateTime());
                            break;

                        case ColumnIndex.Offset:
                            result = direntX.Offset.CompareTo(direntY.Offset);
                            break;

                        case ColumnIndex.Cluster:
                            result = direntX.Cluster.CompareTo(direntY.Cluster);
                            break;
                    }

                    if (order == SortOrder.Descending)
                    {
                        result = -result;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Ошибка при сортировке: {ex.Message}");
                }

                return result;
            }
        }

        private void viewInformationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 0)
                return;

            try
            {
                NodeTag nodeTag = (NodeTag)listView1.SelectedItems[0].Tag;

                switch (nodeTag.Type)
                {
                    case NodeType.Dirent:
                        DatabaseFile databaseFile = (DatabaseFile)nodeTag.Tag;
                        FileInfoDialog dialog = new FileInfoDialog(this._volume, databaseFile.GetDirent());
                        dialog.ShowDialog();
                        break;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Ошибка при просмотре информации: {ex.Message}");
            }
        }

        private void viewCollisionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 0)
                return;

            try
            {
                NodeTag nodeTag = (NodeTag)listView1.SelectedItems[0].Tag;

                switch (nodeTag.Type)
                {
                    case NodeType.Dirent:
                        DatabaseFile databaseFile = (DatabaseFile)nodeTag.Tag;

                        foreach (var collision in databaseFile.GetCollisions())
                        {
                            Trace.WriteLine($"Кластер: {collision} (Смещение: {_volume.ClusterToPhysicalOffset(collision)})");
                            var occupants = _integrityAnalyzer.GetClusterOccupants(collision);
                            foreach (var occupant in occupants)
                            {
                                var o = occupant.GetDirent();
                                Trace.WriteLine($"{o.GetRootDirectoryEntry().Cluster}/{o.GetFullPath()}");
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Ошибка при просмотре коллизий: {ex.Message}");
            }
        }

        private void editClusterChainToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 0)
                return;

            try
            {
                NodeTag nodeTag = (NodeTag)listView1.SelectedItems[0].Tag;

                switch (nodeTag.Type)
                {
                    case NodeType.Dirent:
                        DatabaseFile file = (DatabaseFile)nodeTag.Tag;
                        ClusterChainDialog dialog = new ClusterChainDialog(this._volume, file);
                        if (dialog.ShowDialog() == DialogResult.OK)
                        {
                            file.ClusterChain = dialog.NewClusterChain;
                            NotifyDatabaseChanged?.Invoke(null, null);
                            RefreshTreeView();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Ошибка при редактировании цепочки кластеров: {ex.Message}");
            }
        }

        private void testToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Target: 291581
            try
            {
                ListViewItem selectedItem = listView1.SelectedItems[0];
                NodeTag nodeTag = (NodeTag)selectedItem.Tag;

                switch (nodeTag.Type)
                {
                    case NodeType.Dirent:
                        DatabaseFile databaseFile = (DatabaseFile)nodeTag.Tag;
                        if (!databaseFile.ClusterChain.Contains(291581))
                            databaseFile.ClusterChain.Add(291581);
                        break;
                }

                _fileDatabase.Update();
                PopulateTreeView(_fileDatabase.GetRootFiles());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Ошибка в тестовой функции: {ex.Message}");
            }
        }
    }
}