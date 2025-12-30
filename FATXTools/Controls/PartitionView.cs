// Переписано
using FATX.Analyzers;
using FATX.FileSystem;
using FATXTools.Controls;
using FATXTools.Database;
using FATXTools.Utilities;
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.Windows.Forms;

namespace FATXTools
{
    public partial class PartitionView : UserControl
    {
        private TabPage explorerPage;
        private TabPage clusterViewerPage;
        private TabPage carverResultsPage;
        private TabPage recoveryResultsPage;

        private PartitionDatabase partitionDatabase;
        private IntegrityAnalyzer integrityAnalyzer;
        private ClusterViewer clusterViewer;
        private TaskRunner taskRunner;

        private Volume volume;
        private MetadataAnalyzer metadataAnalyzer;
        private FileCarver fileCarver;

        public PartitionView(TaskRunner taskRunner, Volume volume, PartitionDatabase partitionDatabase)
        {
            try
            {
                InitializeComponent();

                // Правило 1: Проверка входных параметров
                if (partitionDatabase == null || volume == null)
                {
                    Trace.WriteLine("[PartitionView] Ошибка: Передан null Volume или PartitionDatabase.");
                    return;
                }

                integrityAnalyzer = new IntegrityAnalyzer(volume, partitionDatabase.GetFileDatabase());

                this.taskRunner = taskRunner;
                this.volume = volume;
                this.partitionDatabase = partitionDatabase;

                // TODO: Use events instead of passing view to database
                partitionDatabase.SetPartitionView(this);
                partitionDatabase.OnLoadRecoveryFromDatabase += PartitionDatabase_OnLoadNewDatabase;

                explorerPage = new TabPage("File Explorer");

                // Инициализация FileExplorer может упасть, если Volume поврежден
                FileExplorer explorer = new FileExplorer(this, taskRunner, volume);
                explorer.Dock = DockStyle.Fill;
                explorer.OnMetadataAnalyzerCompleted += Explorer_OnMetadataAnalyzerCompleted;
                explorer.OnFileCarverCompleted += Explorer_OnFileCarverCompleted;
                explorerPage.Controls.Add(explorer);
                this.tabControl1.TabPages.Add(explorerPage);

                clusterViewerPage = new TabPage("Cluster Viewer");
                clusterViewer = new ClusterViewer(volume, integrityAnalyzer);
                clusterViewer.Dock = DockStyle.Fill;
                clusterViewerPage.Controls.Add(clusterViewer);
                this.tabControl1.TabPages.Add(clusterViewerPage);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PartitionView] Критическая ошибка инициализации PartitionView: {ex.Message}");
                MessageBox.Show($"Не удалось инициализировать представление раздела.\nОшибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PartitionDatabase_OnLoadNewDatabase(object sender, EventArgs e)
        {
            try
            {
                // At this point, files will be loaded into file database.
                var fileDatabase = partitionDatabase.GetFileDatabase();
                if (fileDatabase == null) return;

                // Правило 2 и 3: Trace.WriteLine вместо Console
                Trace.WriteLine($"[PartitionView] Загружено {fileDatabase.Count()} файлов для {PartitionName}.");

                fileDatabase.Update();          // Update() -> file system
                integrityAnalyzer.Update();     // Update() -> integrity analyzer
                clusterViewer.UpdateClusters(); // Update() -> cluster viewer

                CreateRecoveryView();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PartitionView] Ошибка при обновлении после загрузки БД: {ex.Message}");
            }
        }

        public string PartitionName => volume?.Name ?? "Unknown";
        public Volume Volume => volume;

        public void CreateCarverView(FileCarver carver)
        {
            try
            {
                // Правило 1: Проверка на null перед удалением, чтобы не сломать TabControl
                if (carverResultsPage != null)
                {
                    tabControl1.TabPages.Remove(carverResultsPage);
                    // Старую страницу лучше удалить, но здесь просто скрываем от управления
                    // carverResultsPage.Dispose(); 
                    carverResultsPage = null;
                }

                partitionDatabase.SetFileCarver(carver);

                carverResultsPage = new TabPage("Carver View");
                CarverResults carverResults = new CarverResults(carver, this.taskRunner);
                carverResults.Dock = DockStyle.Fill;
                carverResultsPage.Controls.Add(carverResults);
                tabControl1.TabPages.Add(carverResultsPage);
                tabControl1.SelectedTab = carverResultsPage;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PartitionView] Ошибка создания Carver View: {ex.Message}");
            }
        }

        public void CreateRecoveryView()
        {
            try
            {
                if (recoveryResultsPage != null)
                {
                    tabControl1.TabPages.Remove(recoveryResultsPage);
                    recoveryResultsPage = null;
                }

                recoveryResultsPage = new TabPage("Recovery View");
                var fileDatabase = partitionDatabase.GetFileDatabase();

                // Передаем null во второй параметр так как IntegrityAnalyzer уже есть в классе
                RecoveryResults recoveryResults = new RecoveryResults(fileDatabase, integrityAnalyzer, taskRunner);
                recoveryResults.Dock = DockStyle.Fill;
                recoveryResults.NotifyDatabaseChanged += RecoveryResults_NotifyDatabaseChanged;
                recoveryResultsPage.Controls.Add(recoveryResults);
                tabControl1.TabPages.Add(recoveryResultsPage);
                tabControl1.SelectedTab = recoveryResultsPage;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PartitionView] Ошибка создания Recovery View: {ex.Message}");
            }
        }

        private void RecoveryResults_NotifyDatabaseChanged(object sender, EventArgs e)
        {
            RefreshViews();
        }

        private void RefreshViews()
        {
            try
            {
                var fileDatabase = partitionDatabase.GetFileDatabase();
                if (fileDatabase == null) return;

                fileDatabase.Update();          // Update() -> file system
                integrityAnalyzer.Update();     // Update() -> integrity analyzer
                clusterViewer.UpdateClusters(); // Update() -> cluster viewer
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PartitionView] Ошибка обновления представлений (RefreshViews): {ex.Message}");
            }
        }

        private void Explorer_OnFileCarverCompleted(object sender, EventArgs e)
        {
            try
            {
                // Правило 1: Безопасное приведение типов
                FileCarverResults results = e as FileCarverResults;

                if (results != null)
                {
                    fileCarver = results.carver;
                    CreateCarverView(fileCarver);
                }
                else
                {
                    Trace.WriteLine("[PartitionView] Ошибка приведения типов в Explorer_OnFileCarverCompleted.");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PartitionView] Ошибка обработки завершения Carver: {ex.Message}");
            }
        }

        private void Explorer_OnMetadataAnalyzerCompleted(object sender, EventArgs e)
        {
            try
            {
                // Правило 1: Безопасное приведение типов
                MetadataAnalyzerResults results = e as MetadataAnalyzerResults;

                if (results?.analyzer == null)
                {
                    Trace.WriteLine("[PartitionView] В событии MetadataAnalyzerResults analyzer равен null.");
                    return;
                }

                metadataAnalyzer = results.analyzer;
                partitionDatabase.SetMetadataAnalyzer(true);

                var fileDatabase = partitionDatabase.GetFileDatabase();

                // We've got new analysis results, we need to clear any previous work
                fileDatabase.Reset();

                // Add in -> new results
                // Правило 1: Защита при добавлении файлов, если один из них "битый"
                foreach (var dirent in metadataAnalyzer.GetDirents())
                {
                    try
                    {
                        fileDatabase.AddFile(dirent, true);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"[PartitionView] Ошибка добавления файла из анализа метаданных ({dirent.FileName}): {ex.Message}");
                    }
                }

                fileDatabase.Update();          // Update() -> file system
                integrityAnalyzer.Update();     // Update() -> integrity analyzer
                clusterViewer.UpdateClusters(); // Update() -> cluster viewer

                CreateRecoveryView();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PartitionView] Критическая ошибка завершения анализа метаданных: {ex.Message}");
            }
        }
    }
}