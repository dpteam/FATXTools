// Переписано
using FATXTools.Utilities;
using System;
using System.Diagnostics; // 1. Подключаем Trace
using System.Windows.Forms;

namespace FATXTools.Dialogs
{
    public partial class ProgressDialog : Form
    {
        private long _maxValue;
        private long _interval;
        private TaskRunner _taskRunner;

        public ProgressDialog(TaskRunner taskRunner, Form owner,
            string title, long maxValue, long interval)
        {
            InitializeComponent();

            try
            {
                this.Owner = owner;
                this.Text = title;
                this._taskRunner = taskRunner;
                this._interval = interval;

                // Правило 1: Защита от деления на ноль
                if (this._interval == 0)
                {
                    Trace.WriteLine("[ProgressDialog] Внимание: Interval равен 0. Установлено значение по умолчанию 1 во избежание деления на ноль.");
                    this._interval = 1;
                }

                this._maxValue = maxValue / this._interval;

                progressBar1.Value = 0;
                progressBar1.Maximum = 10000;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ProgressDialog] Ошибка инициализации диалога прогресса: {ex.Message}");
            }
        }

        // Внутренний метод для обновления текста в UI потоке
        private void SetTextInternal(string text)
        {
            label1.Text = text;
        }

        public void SetText(string text)
        {
            // Правило 1: Потокобезопасность (InvokeRequired)
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(SetTextInternal), text);
            }
            else
            {
                SetTextInternal(text);
            }
        }

        // Внутренний метод для обновления прогресса в UI потоке
        private void UpdateProgressInternal(long currentValue)
        {
            try
            {
                if (_maxValue == 0) return;

                if (currentValue > _maxValue)
                {
                    currentValue = _maxValue;
                }

                var curValue = currentValue;
                var maxValue = _maxValue;

                // Вычисление прогресса
                float percentage = ((float)curValue / (float)maxValue);
                var progress = percentage * 10000;

                progressBar1.Value = (int)progress;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ProgressDialog] Ошибка обновления прогресса: {ex.Message}");
            }
        }

        public void UpdateProgress(long currentValue)
        {
            // Правило 1: Потокобезопасность
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<long>(UpdateProgressInternal), currentValue);
            }
            else
            {
                UpdateProgressInternal(currentValue);
            }
        }

        // Внутренний метод для обновления лейбла в UI потоке
        private void UpdateLabelInternal(string label)
        {
            label1.Text = label;
        }

        public void UpdateLabel(string label)
        {
            // Правило 1: Потокобезопасность
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(UpdateLabelInternal), label);
            }
            else
            {
                UpdateLabelInternal(label);
            }
        }

        private void AnalyzerProgress_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (_taskRunner != null)
                {
                    e.Cancel = !_taskRunner.CancelTask();

                    if (e.Cancel)
                    {
                        Trace.WriteLine("[ProgressDialog] Запрошена отмена задачи...");
                        // Используем безопасный метод обновления
                        SetText("Cancelling. Please wait.");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ProgressDialog] Ошибка при закрытии диалога: {ex.Message}");
            }
        }
    }
}