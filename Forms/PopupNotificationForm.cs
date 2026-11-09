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
        private readonly Action<ReminderItem, PopupResultAction> _onResolved;
        private Timer _soundTimer;
        private int _flashCount;
        private NumericUpDown _numCustomValue;
        private ComboBox _cmbCustomUnit;
        private bool _isResolved;

        public PopupNotificationForm(
            ReminderItem reminder,
            Action<ReminderItem, PopupResultAction> onResolved)
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
            ClientSize = new Size(540, 365);
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
                Text = string.Format("Назначено на: {0:dd.MM.yyyy HH:mm:ss}  •  Повтор: {1}",
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
                Location = new Point(18, 76),
                Size = new Size(504, 126),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblTitle = new Label
            {
                Text = _reminder.Title,
                ForeColor = Color.FromArgb(15, 23, 42),
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                Location = new Point(14, 12),
                Size = new Size(474, 52),
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
                Size = new Size(470, 46),
                TabStop = false
            };

            contentPanel.Controls.Add(lblTitle);
            contentPanel.Controls.Add(txtNotes);

            // Блок «Отложить» (Snooze)
            Label lblSnoozeCaption = new Label
            {
                Text = "Отложить напоминание через:",
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(18, 212),
                AutoSize = true
            };

            string[] quickLabels = new string[] { "+30 сек", "+5 мин", "+15 мин", "+1 час", "+1 день" };
            int[,] quickOffsets = new int[,]
            {
                // years, months, days, hours, minutes, seconds
                { 0, 0, 0, 0, 0, 30 },
                { 0, 0, 0, 0, 5, 0 },
                { 0, 0, 0, 0, 15, 0 },
                { 0, 0, 0, 1, 0, 0 },
                { 0, 0, 1, 0, 0, 0 }
            };

            int btnX = 18;
            for (int i = 0; i < quickLabels.Length; i++)
            {
                int y = quickOffsets[i, 0];
                int mo = quickOffsets[i, 1];
                int d = quickOffsets[i, 2];
                int h = quickOffsets[i, 3];
                int m = quickOffsets[i, 4];
                int s = quickOffsets[i, 5];

                Button btnQuick = new Button
                {
                    Text = quickLabels[i],
                    Location = new Point(btnX, 236),
                    Size = new Size(64, 32),
                    Padding = new Padding(0),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(226, 232, 240),
                    ForeColor = Color.FromArgb(30, 41, 59),
                    Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                btnQuick.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                btnQuick.Click += delegate { ResolveSnoozeOffset(y, mo, d, h, m, s); };
                Controls.Add(btnQuick);
                btnX += 68;
            }

            // Произвольное количество и выбор единицы (сек, мин, ч, дн, мес, лет)
            _numCustomValue = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 999,
                Value = 10,
                Location = new Point(362, 240),
                Size = new Size(50, 26),
                Font = new Font("Segoe UI", 9f)
            };

            _cmbCustomUnit = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8.75f),
                Location = new Point(416, 240),
                Size = new Size(56, 26)
            };
            _cmbCustomUnit.Items.AddRange(new object[]
            {
                "сек.",
                "мин.",
                "ч.",
                "дн.",
                "мес.",
                "лет"
            });
            _cmbCustomUnit.SelectedIndex = 1; // По умолчанию "мин."

            Button btnCustomSnooze = new Button
            {
                Text = "ОК",
                Location = new Point(476, 236),
                Size = new Size(46, 32),
                Padding = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(219, 234, 254),
                ForeColor = Color.FromArgb(30, 64, 175),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCustomSnooze.FlatAppearance.BorderColor = Color.FromArgb(147, 197, 253);
            btnCustomSnooze.Click += delegate
            {
                int val = (int)_numCustomValue.Value;
                switch (_cmbCustomUnit.SelectedIndex)
                {
                    case 0: ResolveSnoozeOffset(0, 0, 0, 0, 0, val); break;
                    case 1: ResolveSnoozeOffset(0, 0, 0, 0, val, 0); break;
                    case 2: ResolveSnoozeOffset(0, 0, 0, val, 0, 0); break;
                    case 3: ResolveSnoozeOffset(0, 0, val, 0, 0, 0); break;
                    case 4: ResolveSnoozeOffset(0, val, 0, 0, 0, 0); break;
                    case 5: ResolveSnoozeOffset(val, 0, 0, 0, 0, 0); break;
                    default: ResolveSnoozeOffset(0, 0, 0, 0, val, 0); break;
                }
            };

            // Нижняя панель с главной кнопкой «Выполнено / ОК»
            Button btnComplete = new Button
            {
                Text = _reminder.Recurrence == RecurrenceType.None
                    ? "✓  Понятно (Выполнено)"
                    : "✓  ОК (Перенести на следующий повтор)",
                Location = new Point(18, 294),
                Size = new Size(504, 46),
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
            Controls.Add(_numCustomValue);
            Controls.Add(_cmbCustomUnit);
            Controls.Add(btnCustomSnooze);
            Controls.Add(btnComplete);

            // Таймер мигания и повторного звукового сигнала
            _soundTimer = new Timer { Interval = 1000 };
            _soundTimer.Tick += OnSoundTimerTick;
        }

        private void PositionNearBottomRightOrCenter()
        {
            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            int x = workArea.Left + (workArea.Width - Width) / 2;
            int y = workArea.Top + (workArea.Height - Height) / 2;
            Location = new Point(Math.Max(workArea.Left, x), Math.Max(workArea.Top, y));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

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
            _reminder.AdvanceAfterComplete(DateTime.Now);
            if (_onResolved != null)
            {
                _onResolved(_reminder, PopupResultAction.Completed);
            }
            Close();
        }

        private void ResolveSnoozeOffset(int years, int months, int days, int hours, int minutes, int seconds)
        {
            if (_isResolved) return;
            _isResolved = true;
            _soundTimer.Stop();
            _reminder.SnoozeByOffset(years, months, days, hours, minutes, seconds);
            if (_onResolved != null)
            {
                _onResolved(_reminder, PopupResultAction.Snoozed);
            }
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _soundTimer.Stop();
            if (!_isResolved)
            {
                _isResolved = true;
                _reminder.SnoozeByOffset(0, 0, 0, 0, 5, 0);
                if (_onResolved != null)
                {
                    _onResolved(_reminder, PopupResultAction.Snoozed);
                }
            }
            base.OnFormClosing(e);
        }
    }
}
