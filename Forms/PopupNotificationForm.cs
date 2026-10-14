using System;
using System.Drawing;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using StreloReminder.Models;
using StreloReminder.Services;

namespace StreloReminder.Forms
{
    public enum PopupResultAction
    {
        Completed,
        Snoozed
    }

    public class PopupNotificationForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool FlashWindow(IntPtr hwnd, bool bInvert);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private readonly ReminderItem _reminder;
        private readonly Action<ReminderItem, PopupResultAction, int> _onResolved;
        private Timer _soundTimer;
        private int _flashCount;
        private NumericUpDown _numCustomMinutes;
        private bool _isResolved;

        public PopupNotificationForm(
            ReminderItem reminder,
            Action<ReminderItem, PopupResultAction, int> onResolved)
        {
            _reminder = reminder;
            _onResolved = onResolved;
            _isResolved = false;

            InitializeComponent();
            PositionNearBottomRightOrCenter();
        }

        private void InitializeComponent()
        {
            bool isHigh = _reminder.Priority == ReminderPriority.High;
            Color headerColor = isHigh
                ? Color.FromArgb(185, 28, 28)   // Красный акцент для важных напоминаний
                : Color.FromArgb(30, 64, 175);  // Синий акцент для обычных напоминаний

            Text = isHigh ? "ВАЖНОЕ НАПОМИНАНИЕ — Strelo" : "Напоминание — Strelo";
            Icon = IconGenerator.GetAppIcon(false);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = true;
            BackColor = Color.FromArgb(248, 250, 252);
            ClientSize = new Size(480, 345);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Шапка окна
            Panel headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = headerColor,
                Padding = new Padding(18, 10, 18, 10)
            };

            Label lblBadge = new Label
            {
                Text = isHigh ? "⚡ ВАЖНОЕ НАПОМИНАНИЕ" : "🔔 НАПОМИНАНИЕ",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(18, 11)
            };

            Label lblTimeInfo = new Label
            {
                Text = string.Format("Назначено на: {0:dd.MM.yyyy HH:mm}  •  Повтор: {1}",
                    _reminder.TriggerTime,
                    _reminder.GetRecurrenceDisplayText()),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(18, 36)
            };

            headerPanel.Controls.Add(lblBadge);
            headerPanel.Controls.Add(lblTimeInfo);

            // Основная карточка с текстом напоминания
            Panel contentPanel = new Panel
            {
                Location = new Point(18, 78),
                Size = new Size(444, 128),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblTitle = new Label
            {
                Text = _reminder.Title,
                ForeColor = Color.FromArgb(15, 23, 42),
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                Location = new Point(14, 12),
                Size = new Size(414, 52),
                AutoEllipsis = true
            };

            TextBox txtNotes = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(_reminder.Notes)
                    ? "Дополнительное описание не указано."
                    : _reminder.Notes,
                ForeColor = string.IsNullOrWhiteSpace(_reminder.Notes)
                    ? Color.FromArgb(148, 163, 184)
                    : Color.FromArgb(51, 65, 85),
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 9.75f, FontStyle.Regular),
                Location = new Point(16, 68),
                Size = new Size(410, 48),
                TabStop = false
            };

            contentPanel.Controls.Add(lblTitle);
            contentPanel.Controls.Add(txtNotes);

            // Блок «Отложить» (Snooze)
            Label lblSnoozeCaption = new Label
            {
                Text = "Отложить напоминание:",
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(18, 216),
                AutoSize = true
            };

            int[] quickMins = new int[] { 5, 10, 15, 30, 60 };
            string[] quickLabels = new string[] { "+5 мин", "+10 мин", "+15 мин", "+30 мин", "+1 час" };

            int btnX = 18;
            for (int i = 0; i < quickMins.Length; i++)
            {
                int mins = quickMins[i];
                Button btnQuick = new Button
                {
                    Text = quickLabels[i],
                    Location = new Point(btnX, 240),
                    Size = new Size(64, 32),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(226, 232, 240),
                    ForeColor = Color.FromArgb(30, 41, 59),
                    Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                btnQuick.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                btnQuick.Click += delegate { ResolveSnooze(mins); };
                Controls.Add(btnQuick);
                btnX += 69;
            }

            // Произвольное количество минут
            _numCustomMinutes = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 1440,
                Value = 20,
                Location = new Point(365, 244),
                Size = new Size(48, 26),
                Font = new Font("Segoe UI", 9.5f)
            };

            Button btnCustomSnooze = new Button
            {
                Text = "мин",
                Location = new Point(416, 240),
                Size = new Size(46, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(219, 234, 254),
                ForeColor = Color.FromArgb(30, 64, 175),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCustomSnooze.FlatAppearance.BorderColor = Color.FromArgb(147, 197, 253);
            btnCustomSnooze.Click += delegate
            {
                ResolveSnooze((int)_numCustomMinutes.Value);
            };

            // Нижняя панель с главной кнопкой «Выполнено / ОК»
            Button btnComplete = new Button
            {
                Text = _reminder.Recurrence == RecurrenceType.None
                    ? "✓  Понятно (Выполнено)"
                    : "✓  ОК (Перенести на следующий повтор)",
                Location = new Point(18, 286),
                Size = new Size(444, 44),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(22, 163, 74),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnComplete.FlatAppearance.BorderSize = 0;
            btnComplete.Click += delegate { ResolveComplete(); };

            AcceptButton = btnComplete;

            Controls.Add(headerPanel);
            Controls.Add(contentPanel);
            Controls.Add(lblSnoozeCaption);
            Controls.Add(_numCustomMinutes);
            Controls.Add(btnCustomSnooze);
            Controls.Add(btnComplete);

            // Таймер мигания и повторного звукового сигнала
            _soundTimer = new Timer { Interval = 1000 };
            _soundTimer.Tick += OnSoundTimerTick;
        }

        private void PositionNearBottomRightOrCenter()
        {
            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            // Показываем окно ближе к центру с небольшим смещением, чтобы его было невозможно пропустить
            int x = workArea.Left + (workArea.Width - Width) / 2;
            int y = workArea.Top + (workArea.Height - Height) / 2;
            Location = new Point(Math.Max(workArea.Left, x), Math.Max(workArea.Top, y));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // Поднимаем окно поверх всех окон
            TopMost = true;
            BringToFront();
            Activate();
            try
            {
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }
            catch
            {
            }

            PlayAlertSound();
            _soundTimer.Start();
        }

        private void OnSoundTimerTick(object sender, EventArgs e)
        {
            _flashCount++;
            if (_flashCount <= 6)
            {
                try
                {
                    FlashWindow(Handle, true);
                }
                catch
                {
                }
            }

            // Повторяем звук каждые 15 секунд, пока пользователь не нажмёт кнопку
            if (_flashCount % 15 == 0)
            {
                PlayAlertSound();
            }
        }

        private void PlayAlertSound()
        {
            if (!_reminder.PlaySound)
                return;

            try
            {
                if (_reminder.Priority == ReminderPriority.High)
                {
                    SystemSounds.Exclamation.Play();
                }
                else
                {
                    SystemSounds.Asterisk.Play();
                }
            }
            catch
            {
            }
        }

        private void ResolveComplete()
        {
            if (_isResolved) return;
            _isResolved = true;
            _soundTimer.Stop();
            if (_onResolved != null)
            {
                _onResolved(_reminder, PopupResultAction.Completed, 0);
            }
            Close();
        }

        private void ResolveSnooze(int minutes)
        {
            if (_isResolved) return;
            _isResolved = true;
            _soundTimer.Stop();
            if (_onResolved != null)
            {
                _onResolved(_reminder, PopupResultAction.Snoozed, minutes);
            }
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _soundTimer.Stop();
            // Если пользователь закрыл окно крестиком, откладываем на 5 минут, чтобы важное дело не потерялось,
            // либо если это было явное нажатие кнопки — ничего дополнительно не делаем.
            if (!_isResolved)
            {
                _isResolved = true;
                if (_onResolved != null)
                {
                    _onResolved(_reminder, PopupResultAction.Snoozed, 5);
                }
            }
            base.OnFormClosing(e);
        }
    }
}
