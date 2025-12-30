// Переписано
using FATXTools.Dialogs;
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FATXTools.Utilities
{
    public class TaskRunner
    {
        Form _owner;
        Task _task;
        ProgressDialog _progressDialog;

        CancellationToken cancellationToken;
        CancellationTokenSource cancellationTokenSource;

        public TaskRunner(Form owner)
        {
            _owner = owner;
        }

        public long Maximum
        {
            get;
            set;
        }

        public long Interval
        {
            get;
            set;
        }

        public event EventHandler TaskStarted;

        public event EventHandler TaskCompleted;

        public async Task RunTaskAsync(string title, Action<CancellationToken, IProgress<int>> task, Action<int> progressUpdate, Action taskCompleted)
        {
            if (_task != null)
            {
                throw new Exception("A task is already running.");
            }

            cancellationTokenSource = new CancellationTokenSource();
            cancellationToken = cancellationTokenSource.Token;

            TaskStarted?.Invoke(this, null);

            try
            {
                _progressDialog = new ProgressDialog(this, _owner, $"Task - {title}", Maximum, Interval);
                _progressDialog.Show();

                var progress = new Progress<int>(percent =>
                {
                    progressUpdate(percent);
                });

                try
                {
                    _task = Task.Run(() =>
                    {
                        task(cancellationToken, progress);
                    }, cancellationToken);

                    // wait for worker task to finish.
                    await _task;
                }
                catch (TaskCanceledException)
                {
                    // Правило 2: Trace.WriteLine вместо Console
                    Trace.WriteLine("[TaskRunner] Задача была отменена пользователем.");
                }
                catch (Exception exception)
                {
                    // Правило 3: Улучшенное логирование ошибок выполнения задачи
                    Trace.WriteLine($"[TaskRunner] Ошибка выполнения задачи: {exception.Message}");
                    Trace.WriteLine(exception.StackTrace);

                    // MessageBox оставляем для обратной связи с пользователем, но логируем в первую очередь
                    MessageBox.Show(exception.Message);
                }

                SystemSounds.Beep.Play();

                // Правило 1: Защита от падения в коллбэке завершения
                try
                {
                    taskCompleted();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[TaskRunner] Исключение в методе завершения (taskCompleted): {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[TaskRunner] Критическая ошибка при управлении задачей: {ex.Message}");
            }
            finally
            {
                // Убеждаемся, что диалог будет закрыт в любом случае
                _progressDialog?.Close();

                TaskCompleted?.Invoke(this, null);

                _progressDialog = null;
                _task = null;
            }
        }

        public bool CancelTask()
        {
            // Правило 1: Проверка на null перед отменой
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
            }
            else
            {
                Trace.WriteLine("[TaskRunner] Попытка отмены задачи, но CancellationTokenSource равен null.");
            }

            return (_task == null) || _task.IsCompleted;
        }

        public void UpdateProgress(long newValue)
        {
            // Правило 1: Проверка на null перед доступом к диалогу
            if (_progressDialog != null)
            {
                _progressDialog.UpdateProgress(newValue);
            }
            else
            {
                Trace.WriteLine($"[TaskRunner] Попытка обновить прогресс ({newValue}), но диалог не инициализирован.");
            }
        }

        public void UpdateLabel(string newLabel)
        {
            // Правило 1: Проверка на null перед доступом к диалогу
            if (_progressDialog != null)
            {
                _progressDialog.UpdateLabel(newLabel);
            }
            else
            {
                Trace.WriteLine($"[TaskRunner] Попытка обновить метку ('{newLabel}'), но диалог не инициализирован.");
            }
        }
    }
}