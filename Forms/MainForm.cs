using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using StreloReminder.Models;
using StreloReminder.Services;

namespace StreloReminder.Forms
{
    public class MainForm : Form
    {
        private readonly List<ReminderItem> _reminders = new List<ReminderItem>();
        private readonly Dictionary<string, PopupNotificationForm> _activePopups = new Dictionary<string, PopupNotificationForm>();

        private System.Windows.Forms.Timer _mainTimer;
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayPauseItem;

        // Элементы шапки
        private Label _lblClock;
        private Label _lblSyncStatus;
        private Button _btnSyncNow;
        private Button _btnCloudSettings;
        private Button _btnCheckUpdate;
        private CheckBox _chkDoNotDisturb;
        private CheckBox _chkAutoStart;

        // Элементы формы добавления / редактирования
        private GroupBox _grpEditor;
        private TextBox _txtTitle;
        private TextBox _txtNotes;
        private RadioButton _rbTimerMode;
        private RadioButton _rbExactTimeMode;

        // Панель «Через сколько» (Годы, Месяцы, Дни, Часы, Минуты, Секунды)
        private Panel _pnlTimerControls;
        private NumericUpDown _numTimerYears;
        private NumericUpDown _numTimerMonths;
        private NumericUpDown _numTimerDays;
        private NumericUpDown _numTimerHours;
        private NumericUpDown _numTimerMinutes;
        private NumericUpDown _numTimerSeconds;
        private Label _lblTimerPreview;

        // Панель «В точное время и дату»
        private Panel _pnlExactTimeControls;
        private DateTimePicker _dtpDate;
        private DateTimePicker _dtpTime;

        private ComboBox _cmbRecurrence;
        private Label _lblIntervalMinutes;
        private NumericUpDown _numIntervalMinutes;
        private ComboBox _cmbPriority;
        private CheckBox _chkPlaySound;
        private Button _btnSaveReminder;
        private Button _btnCancelEdit;
        private Button _btnTestPopup;

        // Таблица напоминаний
        private ListView _lvReminders;
        private Label _lblSummary;

        // Состояние, облачная синхронизация и автообновление
        private CloudSyncConfig _cloudConfig;
        private bool _isSyncInProgress;
        private int _secondsSinceLastSync;

        private bool _isUpdateCheckInProgress;
        private int _secondsSinceStartup;
        private bool _initialUpdateCheckDone;
        private string _notifiedUpdateVersion;
        private UpdateCheckResult _latestAvailableUpdate;

        private string _editingReminderId;
        private bool _isExiting;
        private bool _isDoNotDisturb;
        private bool _trayHintShown;
        private readonly bool _startMinimized;

        public MainForm(bool startMinimized = false)
        {
            _startMinimized = startMinimized;
            _cloudConfig = StorageService.LoadCloudConfig();
            InitializeComponent();
            InitializeTrayIcon();
            LoadSavedReminders();
            UpdateCloudStatusUI();
            UpdateTimerPreviewLabel();
            StartScheduler();

            if (_cloudConfig.IsConfigured())
            {
                TriggerBackgroundCloudSync(false);
            }
        }

        private void InitializeComponent()
        {
            Text = string.Format("Strelo — Напоминалка для Windows v{0} (Синхронизация с Android)", UpdateService.CurrentVersion);
            Icon = IconGenerator.GetAppIcon(false);
            ClientSize = new Size(1060, 700);
            MinimumSize = new Size(1076, 738);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);

            // 1. ВЕРХНЯЯ ПАНЕЛЬ (ШАПКА)
            // Все элементы шапки используют фиксированные координаты слева направо (Anchor Top | Left),
            // чтобы ни при каком масштабировании кнопки не перекрывали чекбоксы
            Panel headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(1060, 62),
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(30, 58, 138)
            };

            Label lblAppTitle = new Label
            {
                Text = "🔔 Strelo Напоминалка",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 7)
            };

            _lblClock = new Label
            {
                Text = string.Format("Сейчас: {0:dd.MM.yyyy HH:mm:ss}  •  v{1}", DateTime.Now, UpdateService.CurrentVersion),
                ForeColor = Color.FromArgb(191, 219, 254),
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(18, 35)
            };

            _btnSyncNow = new Button
            {
                Text = "Синхр.",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(268, 8),
                Size = new Size(76, 25),
                Padding = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnSyncNow.FlatAppearance.BorderColor = Color.FromArgb(96, 165, 250);
            _btnSyncNow.Click += delegate { TriggerBackgroundCloudSync(true); };

            _btnCloudSettings = new Button
            {
                Text = "Облако (Android)",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(350, 8),
                Size = new Size(134, 25),
                Padding = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnCloudSettings.FlatAppearance.BorderColor = Color.FromArgb(96, 165, 250);
            _btnCloudSettings.Click += OnOpenCloudSyncSettingsClick;

            _btnCheckUpdate = new Button
            {
                Text = "Обновление",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(490, 8),
                Size = new Size(116, 25),
                Padding = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnCheckUpdate.FlatAppearance.BorderColor = Color.FromArgb(96, 165, 250);
            _btnCheckUpdate.Click += delegate { CheckForGitHubUpdatesAsync(true); };

            _lblSyncStatus = new Label
            {
                Text = "Облако: не настроено",
                ForeColor = Color.FromArgb(191, 219, 254),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(268, 36)
            };

            _chkDoNotDisturb = new CheckBox
            {
                Text = "Режим «Не беспокоить»",
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(626, 20),
                Cursor = Cursors.Hand
            };
            _chkDoNotDisturb.CheckedChanged += OnDoNotDisturbChanged;

            _chkAutoStart = new CheckBox
            {
                Text = "Автозапуск с Windows",
                ForeColor = Color.FromArgb(219, 234, 254),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(845, 20),
                Cursor = Cursors.Hand,
                Checked = StorageService.IsAutoStartEnabled()
            };
            _chkAutoStart.CheckedChanged += OnAutoStartChanged;

            headerPanel.Controls.Add(lblAppTitle);
            headerPanel.Controls.Add(_lblClock);
            headerPanel.Controls.Add(_btnSyncNow);
            headerPanel.Controls.Add(_btnCloudSettings);
            headerPanel.Controls.Add(_btnCheckUpdate);
            headerPanel.Controls.Add(_lblSyncStatus);
            headerPanel.Controls.Add(_chkDoNotDisturb);
            headerPanel.Controls.Add(_chkAutoStart);

            // 2. ПАНЕЛЬ СОЗДАНИЯ / РЕДАКТИРОВАНИЯ НАПОМИНАНИЯ
            _grpEditor = new GroupBox
            {
                Text = " Новое напоминание ",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                BackColor = Color.White,
                Location = new Point(16, 70),
                Size = new Size(1028, 262),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            Label lblTitle = new Label
            {
                Text = "О чём напомнить:",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(18, 26),
                AutoSize = true
            };

            _txtTitle = new TextBox
            {
                Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
                Location = new Point(18, 48),
                Size = new Size(462, 27)
            };

            Label lblNotes = new Label
            {
                Text = "Дополнительное описание / заметка (необязательно):",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(71, 85, 105),
                Location = new Point(18, 84),
                AutoSize = true
            };

            _txtNotes = new TextBox
            {
                Font = new Font("Segoe UI", 9.25f, FontStyle.Regular),
                Location = new Point(18, 106),
                Size = new Size(462, 82),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };

            Label lblWhen = new Label
            {
                Text = "Когда сработать:",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(504, 25),
                AutoSize = true
            };

            _rbTimerMode = new RadioButton
            {
                Text = "Через сколько",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175),
                Location = new Point(632, 23),
                AutoSize = true,
                Checked = true,
                Cursor = Cursors.Hand
            };
            _rbTimerMode.CheckedChanged += OnTimeModeChanged;

            _rbExactTimeMode = new RadioButton
            {
                Text = "В точное время и дату",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175),
                Location = new Point(778, 23),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            _rbExactTimeMode.CheckedChanged += OnTimeModeChanged;

            // Панель настройки таймера («Годы, Месяцы, Дни, Часы, Минуты, Секунды» + быстрые кнопки + превью)
            _pnlTimerControls = new Panel
            {
                Location = new Point(504, 46),
                Size = new Size(506, 106),
                BackColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.FixedSingle
            };

            _numTimerYears = CreateTimerUnitControl(_pnlTimerControls, "Годы", 10, 0, 100, 0);
            _numTimerMonths = CreateTimerUnitControl(_pnlTimerControls, "Месяцы", 92, 0, 120, 0);
            _numTimerDays = CreateTimerUnitControl(_pnlTimerControls, "Дни", 174, 0, 3650, 0);
            _numTimerHours = CreateTimerUnitControl(_pnlTimerControls, "Часы", 256, 0, 999, 0);
            _numTimerMinutes = CreateTimerUnitControl(_pnlTimerControls, "Минуты", 338, 0, 999, 15);
            _numTimerSeconds = CreateTimerUnitControl(_pnlTimerControls, "Секунды", 420, 0, 999, 0);

            string[] presetLabels = new string[] { "30 сек", "5 мин", "15 мин", "1 час", "1 день", "1 мес", "1 год", "Сброс" };
            int[,] presetOffsets = new int[,]
            {
                { 0, 0, 0, 0, 0, 30 },
                { 0, 0, 0, 0, 5, 0 },
                { 0, 0, 0, 0, 15, 0 },
                { 0, 0, 0, 1, 0, 0 },
                { 0, 0, 1, 0, 0, 0 },
                { 0, 1, 0, 0, 0, 0 },
                { 1, 0, 0, 0, 0, 0 },
                { 0, 0, 0, 0, 0, 0 }
            };

            int presetX = 10;
            for (int i = 0; i < presetLabels.Length; i++)
            {
                int py = presetOffsets[i, 0];
                int pmo = presetOffsets[i, 1];
                int pd = presetOffsets[i, 2];
                int ph = presetOffsets[i, 3];
                int pm = presetOffsets[i, 4];
                int ps = presetOffsets[i, 5];
                bool isReset = (i == presetLabels.Length - 1);

                Button btnPreset = new Button
                {
                    Text = presetLabels[i],
                    Font = new Font("Segoe UI", 8f, isReset ? FontStyle.Bold : FontStyle.Regular),
                    Location = new Point(presetX, 50),
                    Size = new Size(58, 25),
                    Padding = new Padding(0),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = isReset ? Color.FromArgb(254, 226, 226) : Color.FromArgb(226, 232, 240),
                    ForeColor = isReset ? Color.FromArgb(185, 28, 28) : Color.FromArgb(30, 41, 59),
                    Cursor = Cursors.Hand
                };
                btnPreset.FlatAppearance.BorderColor = isReset
                    ? Color.FromArgb(252, 165, 165)
                    : Color.FromArgb(203, 213, 225);
                btnPreset.Click += delegate
                {
                    SetTimerUnitValues(py, pmo, pd, ph, pm, ps);
                };
                _pnlTimerControls.Controls.Add(btnPreset);
                presetX += 61;
            }

            _lblTimerPreview = new Label
            {
                Text = "Сработает: --",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175),
                Location = new Point(10, 80),
                AutoSize = true
            };
            _pnlTimerControls.Controls.Add(_lblTimerPreview);

            // Панель точной даты и времени
            _pnlExactTimeControls = new Panel
            {
                Location = new Point(504, 46),
                Size = new Size(506, 106),
                BackColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };

            Label lblDate = new Label
            {
                Text = "Дата:",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(12, 16),
                AutoSize = true
            };

            _dtpDate = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                Location = new Point(58, 13),
                Size = new Size(135, 25),
                Value = DateTime.Now.Date
            };

            Label lblTime = new Label
            {
                Text = "Время:",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(212, 16),
                AutoSize = true
            };

            _dtpTime = new DateTimePicker
            {
                Format = DateTimePickerFormat.Time,
                ShowUpDown = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(268, 13),
                Size = new Size(125, 25),
                Value = DateTime.Now.AddMinutes(15)
            };

            Button btnTomorrowMorning = new Button
            {
                Text = "Завтра в 09:00",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                Location = new Point(12, 52),
                Size = new Size(135, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(226, 232, 240),
                Cursor = Cursors.Hand
            };
            btnTomorrowMorning.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnTomorrowMorning.Click += delegate
            {
                DateTime tomorrow = DateTime.Now.Date.AddDays(1);
                _dtpDate.Value = tomorrow;
                _dtpTime.Value = tomorrow.AddHours(9);
            };

            Button btnTodayEvening = new Button
            {
                Text = "Сегодня в 18:00",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                Location = new Point(156, 52),
                Size = new Size(135, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(226, 232, 240),
                Cursor = Cursors.Hand
            };
            btnTodayEvening.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnTodayEvening.Click += delegate
            {
                DateTime today = DateTime.Now.Date;
                _dtpDate.Value = today;
                _dtpTime.Value = today.AddHours(18);
            };

            _pnlExactTimeControls.Controls.Add(lblDate);
            _pnlExactTimeControls.Controls.Add(_dtpDate);
            _pnlExactTimeControls.Controls.Add(lblTime);
            _pnlExactTimeControls.Controls.Add(_dtpTime);
            _pnlExactTimeControls.Controls.Add(btnTomorrowMorning);
            _pnlExactTimeControls.Controls.Add(btnTodayEvening);

            // Повтор и важность
            Label lblRecurrence = new Label
            {
                Text = "Повтор:",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(504, 162),
                AutoSize = true
            };

            _cmbRecurrence = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(574, 158),
                Size = new Size(190, 25)
            };
            _cmbRecurrence.Items.AddRange(new object[]
            {
                "Один раз (без повтора)",
                "Каждые N минут",
                "Ежедневно",
                "По будням (Пн–Пт)",
                "Еженедельно"
            });
            _cmbRecurrence.SelectedIndex = 0;
            _cmbRecurrence.SelectedIndexChanged += OnRecurrenceChanged;

            _lblIntervalMinutes = new Label
            {
                Text = "Интервал (мин):",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(776, 162),
                AutoSize = true,
                Visible = false
            };

            _numIntervalMinutes = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 1440,
                Value = 30,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(884, 158),
                Size = new Size(72, 25),
                Visible = false
            };

            Label lblPriority = new Label
            {
                Text = "Важность:",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(504, 192),
                AutoSize = true
            };

            _cmbPriority = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(574, 188),
                Size = new Size(190, 25)
            };
            _cmbPriority.Items.AddRange(new object[]
            {
                "Обычное",
                "Важное (!)"
            });
            _cmbPriority.SelectedIndex = 0;

            _chkPlaySound = new CheckBox
            {
                Text = "Звуковой сигнал",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(780, 190),
                AutoSize = true,
                Checked = true,
                Cursor = Cursors.Hand
            };

            // Кнопки добавления / сохранения / теста
            _btnSaveReminder = new Button
            {
                Text = "+ Добавить напоминание",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Location = new Point(18, 206),
                Size = new Size(280, 42),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnSaveReminder.FlatAppearance.BorderSize = 0;
            _btnSaveReminder.Click += OnSaveReminderClick;

            _btnCancelEdit = new Button
            {
                Text = "Отмена",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(308, 206),
                Size = new Size(110, 42),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(226, 232, 240),
                ForeColor = Color.FromArgb(51, 65, 85),
                Cursor = Cursors.Hand,
                Visible = false
            };
            _btnCancelEdit.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            _btnCancelEdit.Click += delegate { ResetEditor(); };

            _btnTestPopup = new Button
            {
                Text = "Проверить всплывающее окно",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(504, 214),
                Size = new Size(270, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(238, 242, 255),
                ForeColor = Color.FromArgb(30, 64, 175),
                Cursor = Cursors.Hand
            };
            _btnTestPopup.FlatAppearance.BorderColor = Color.FromArgb(165, 180, 252);
            _btnTestPopup.Click += OnTestPopupClick;

            Button btnMinimizeToTray = new Button
            {
                Text = "Свернуть в трей",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(784, 214),
                Size = new Size(226, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(51, 65, 85),
                Cursor = Cursors.Hand
            };
            btnMinimizeToTray.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnMinimizeToTray.Click += delegate { HideToTray(); };

            _grpEditor.Controls.Add(lblTitle);
            _grpEditor.Controls.Add(_txtTitle);
            _grpEditor.Controls.Add(lblNotes);
            _grpEditor.Controls.Add(_txtNotes);
            _grpEditor.Controls.Add(lblWhen);
            _grpEditor.Controls.Add(_rbTimerMode);
            _grpEditor.Controls.Add(_rbExactTimeMode);
            _grpEditor.Controls.Add(_pnlTimerControls);
            _grpEditor.Controls.Add(_pnlExactTimeControls);
            _grpEditor.Controls.Add(lblRecurrence);
            _grpEditor.Controls.Add(_cmbRecurrence);
            _grpEditor.Controls.Add(_lblIntervalMinutes);
            _grpEditor.Controls.Add(_numIntervalMinutes);
            _grpEditor.Controls.Add(lblPriority);
            _grpEditor.Controls.Add(_cmbPriority);
            _grpEditor.Controls.Add(_chkPlaySound);
            _grpEditor.Controls.Add(_btnSaveReminder);
            _grpEditor.Controls.Add(_btnCancelEdit);
            _grpEditor.Controls.Add(_btnTestPopup);
            _grpEditor.Controls.Add(btnMinimizeToTray);

            // 3. ПАНЕЛЬ УПРАВЛЕНИЯ СПИСКОМ НАПОМИНАНИЙ
            // Все кнопки имеют достаточную ширину для размещения в одну строку без переноса слов
            Panel listToolbar = new Panel
            {
                Location = new Point(16, 340),
                Size = new Size(1028, 40),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.Transparent
            };

            _lblSummary = new Label
            {
                Text = "Список напоминаний (Всего: 0, Активных: 0)",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(2, 9),
                AutoSize = true
            };

            Button btnEdit = CreateToolbarButton("Изменить", 368, 92, Color.FromArgb(226, 232, 240), Color.FromArgb(30, 41, 59));
            btnEdit.Click += OnEditSelectedClick;

            Button btnToggle = CreateToolbarButton("Вкл / Выкл", 466, 98, Color.FromArgb(226, 232, 240), Color.FromArgb(30, 41, 59));
            btnToggle.Click += OnToggleSelectedClick;

            Button btnSnooze10 = CreateToolbarButton("+10 мин", 570, 86, Color.FromArgb(219, 234, 254), Color.FromArgb(30, 64, 175));
            btnSnooze10.Click += OnSnooze10SelectedClick;

            Button btnDelete = CreateToolbarButton("Удалить", 662, 90, Color.FromArgb(254, 226, 226), Color.FromArgb(185, 28, 28));
            btnDelete.Click += OnDeleteSelectedClick;

            Button btnClearDone = CreateToolbarButton("Очистить выполненные", 758, 194, Color.FromArgb(226, 232, 240), Color.FromArgb(51, 65, 85));
            btnClearDone.Click += OnClearCompletedClick;

            Button btnOpenFolder = CreateToolbarButton("Папка", 958, 70, Color.FromArgb(226, 232, 240), Color.FromArgb(51, 65, 85));
            btnOpenFolder.Click += OnOpenStorageFolderClick;

            listToolbar.Controls.Add(_lblSummary);
            listToolbar.Controls.Add(btnEdit);
            listToolbar.Controls.Add(btnToggle);
            listToolbar.Controls.Add(btnSnooze10);
            listToolbar.Controls.Add(btnDelete);
            listToolbar.Controls.Add(btnClearDone);
            listToolbar.Controls.Add(btnOpenFolder);

            // 4. ТАБЛИЦА НАПОМИНАНИЙ
            _lvReminders = new ListView
            {
                Location = new Point(16, 384),
                Size = new Size(1028, 300),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                HideSelection = false,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular)
            };

            _lvReminders.Columns.Add("Статус", 120);
            _lvReminders.Columns.Add("Текст напоминания", 275);
            _lvReminders.Columns.Add("Время срабатывания", 155);
            _lvReminders.Columns.Add("Осталось", 165);
            _lvReminders.Columns.Add("Повтор", 135);
            _lvReminders.Columns.Add("Описание / Заметка", 165);

            _lvReminders.DoubleClick += OnEditSelectedClick;

            Controls.Add(headerPanel);
            Controls.Add(_grpEditor);
            Controls.Add(listToolbar);
            Controls.Add(_lvReminders);
        }

        private NumericUpDown CreateTimerUnitControl(
            Panel parent,
            string caption,
            int x,
            int min,
            int max,
            int defaultValue)
        {
            Label lbl = new Label
            {
                Text = caption,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                ForeColor = Color.FromArgb(71, 85, 105),
                Location = new Point(x, 4),
                AutoSize = true
            };

            NumericUpDown num = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = defaultValue,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(x, 22),
                Size = new Size(74, 24)
            };
            num.ValueChanged += delegate { UpdateTimerPreviewLabel(); };

            parent.Controls.Add(lbl);
            parent.Controls.Add(num);
            return num;
        }

        private void SetTimerUnitValues(int years, int months, int days, int hours, int minutes, int seconds)
        {
            _numTimerYears.Value = Math.Max(_numTimerYears.Minimum, Math.Min(_numTimerYears.Maximum, years));
            _numTimerMonths.Value = Math.Max(_numTimerMonths.Minimum, Math.Min(_numTimerMonths.Maximum, months));
            _numTimerDays.Value = Math.Max(_numTimerDays.Minimum, Math.Min(_numTimerDays.Maximum, days));
            _numTimerHours.Value = Math.Max(_numTimerHours.Minimum, Math.Min(_numTimerHours.Maximum, hours));
            _numTimerMinutes.Value = Math.Max(_numTimerMinutes.Minimum, Math.Min(_numTimerMinutes.Maximum, minutes));
            _numTimerSeconds.Value = Math.Max(_numTimerSeconds.Minimum, Math.Min(_numTimerSeconds.Maximum, seconds));
            UpdateTimerPreviewLabel();
        }

        private DateTime CalculateSelectedTimerTarget(DateTime baseTime)
        {
            int years = (int)_numTimerYears.Value;
            int months = (int)_numTimerMonths.Value;
            int days = (int)_numTimerDays.Value;
            int hours = (int)_numTimerHours.Value;
            int mins = (int)_numTimerMinutes.Value;
            int secs = (int)_numTimerSeconds.Value;

            if (years == 0 && months == 0 && days == 0 && hours == 0 && mins == 0 && secs == 0)
            {
                secs = 10;
            }

            return baseTime
                .AddYears(years)
                .AddMonths(months)
                .AddDays(days)
                .AddHours(hours)
                .AddMinutes(mins)
                .AddSeconds(secs);
        }

        private void UpdateTimerPreviewLabel()
        {
            if (_lblTimerPreview == null)
                return;

            int years = (int)_numTimerYears.Value;
            int months = (int)_numTimerMonths.Value;
            int days = (int)_numTimerDays.Value;
            int hours = (int)_numTimerHours.Value;
            int mins = (int)_numTimerMinutes.Value;
            int secs = (int)_numTimerSeconds.Value;

            DateTime target = CalculateSelectedTimerTarget(DateTime.Now);
            if (years == 0 && months == 0 && days == 0 && hours == 0 && mins == 0 && secs == 0)
            {
                _lblTimerPreview.Text = string.Format("По умолчанию (+10 сек) -> сработает: {0:dd.MM.yyyy HH:mm:ss}", target);
            }
            else
            {
                _lblTimerPreview.Text = string.Format("Сработает: {0:dd.MM.yyyy HH:mm:ss}", target);
            }
        }

        private Button CreateToolbarButton(string text, int x, int width, Color backColor, Color foreColor)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, 4),
                Size = new Size(width, 32),
                Padding = new Padding(0),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                FlatStyle = FlatStyle.Flat,
                BackColor = backColor,
                ForeColor = foreColor,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            return btn;
        }

        private void InitializeTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();

            ToolStripMenuItem openItem = new ToolStripMenuItem("Открыть Напоминалку");
            openItem.Font = new Font(openItem.Font, FontStyle.Bold);
            openItem.Click += delegate { RestoreFromTray(); };

            ToolStripMenuItem syncNowItem = new ToolStripMenuItem("Синхронизировать с Android сейчас");
            syncNowItem.Click += delegate { TriggerBackgroundCloudSync(true); };

            ToolStripMenuItem checkUpdatesItem = new ToolStripMenuItem("Проверить обновления на GitHub");
            checkUpdatesItem.Click += delegate { CheckForGitHubUpdatesAsync(true); };

            ToolStripMenuItem quick5Item = new ToolStripMenuItem("Быстрое напоминание через 5 мин");
            quick5Item.Click += delegate { CreateQuickReminder(5); };

            ToolStripMenuItem quick15Item = new ToolStripMenuItem("Быстрое напоминание через 15 мин");
            quick15Item.Click += delegate { CreateQuickReminder(15); };

            ToolStripMenuItem quick30Item = new ToolStripMenuItem("Быстрое напоминание через 30 мин");
            quick30Item.Click += delegate { CreateQuickReminder(30); };

            _trayPauseItem = new ToolStripMenuItem("Режим «Не беспокоить» (Пауза)");
            _trayPauseItem.CheckOnClick = true;
            _trayPauseItem.CheckedChanged += delegate
            {
                _chkDoNotDisturb.Checked = _trayPauseItem.Checked;
            };

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Выход из программы");
            exitItem.Click += delegate { ExitApplication(); };

            _trayMenu.Items.Add(openItem);
            _trayMenu.Items.Add(syncNowItem);
            _trayMenu.Items.Add(checkUpdatesItem);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(quick5Item);
            _trayMenu.Items.Add(quick15Item);
            _trayMenu.Items.Add(quick30Item);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(_trayPauseItem);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon
            {
                Icon = IconGenerator.GetAppIcon(false),
                Text = "Strelo — Напоминалка для Windows v" + UpdateService.CurrentVersion,
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _notifyIcon.DoubleClick += delegate { RestoreFromTray(); };
        }

        private void LoadSavedReminders()
        {
            _reminders.Clear();
            List<ReminderItem> loaded = StorageService.LoadReminders();
            _reminders.AddRange(loaded);
            RefreshListView();
        }

        private void StartScheduler()
        {
            _mainTimer = new System.Windows.Forms.Timer
            {
                Interval = 1000
            };
            _mainTimer.Tick += OnMainTimerTick;
            _mainTimer.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_startMinimized)
            {
                HideToTray();
            }
        }

        private void OnMainTimerTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            _lblClock.Text = string.Format("Сейчас: {0:dd.MM.yyyy HH:mm:ss}  •  v{1}", now, UpdateService.CurrentVersion);

            if (_rbTimerMode.Checked)
            {
                UpdateTimerPreviewLabel();
            }

            // Проверяем, какие напоминания пора показать
            if (!_isDoNotDisturb)
            {
                for (int i = 0; i < _reminders.Count; i++)
                {
                    ReminderItem item = _reminders[i];
                    if (!item.IsDeleted && item.IsEnabled && !item.IsCompleted && !item.IsPopupOpen && item.TriggerTime <= now)
                    {
                        ShowPopupForReminder(item);
                    }
                }
            }

            UpdateListViewCountdowns(now);

            // Фоновая автосинхронизация с облаком (Android <-> ПК)
            if (_cloudConfig != null && _cloudConfig.IsConfigured() && !_isSyncInProgress)
            {
                _secondsSinceLastSync++;
                int interval = Math.Max(10, _cloudConfig.AutoSyncIntervalSeconds);
                if (_secondsSinceLastSync >= interval)
                {
                    _secondsSinceLastSync = 0;
                    TriggerBackgroundCloudSync(false);
                }
            }

            // Автоматическая проверка выхода новой версии на GitHub (через 4 сек после запуска и далее каждые 30 минут)
            _secondsSinceStartup++;
            if (!_initialUpdateCheckDone && _secondsSinceStartup >= 4)
            {
                _initialUpdateCheckDone = true;
                CheckForGitHubUpdatesAsync(false);
            }
            else if (_secondsSinceStartup > 0 && _secondsSinceStartup % 1800 == 0)
            {
                CheckForGitHubUpdatesAsync(false);
            }
        }

        private void CheckForGitHubUpdatesAsync(final_bool_wrapper manualRequest)
        {
            CheckForGitHubUpdatesInternal(manualRequest.Value);
        }

        private struct final_bool_wrapper
        {
            public bool Value;
            public static implicit operator final_bool_wrapper(bool v)
            {
                final_bool_wrapper w = new final_bool_wrapper();
                w.Value = v;
                return w;
            }
        }

        private void CheckForGitHubUpdatesInternal(bool isManualUserClick)
        {
            if (isManualUserClick && _latestAvailableUpdate != null && _latestAvailableUpdate.IsUpdateAvailable)
            {
                PromptInstallUpdate(_latestAvailableUpdate);
                return;
            }

            if (_isUpdateCheckInProgress)
                return;

            _isUpdateCheckInProgress = true;
            if (isManualUserClick)
            {
                _btnCheckUpdate.Text = "Проверка...";
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateCheckResult res = UpdateService.CheckForUpdates();
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _isUpdateCheckInProgress = false;
                            HandleUpdateCheckResult(res, isManualUserClick);
                        });
                    }
                    else
                    {
                        _isUpdateCheckInProgress = false;
                    }
                }
                catch
                {
                    _isUpdateCheckInProgress = false;
                }
            });
        }

        private void HandleUpdateCheckResult(UpdateCheckResult res, bool isManualUserClick)
        {
            if (res == null || !res.Success)
            {
                _btnCheckUpdate.Text = "Обновление";
                if (isManualUserClick)
                {
                    MessageBox.Show(
                        res != null && !string.IsNullOrEmpty(res.ErrorMessage)
                            ? res.ErrorMessage
                            : "Не удалось проверить наличие обновлений на GitHub.",
                        "Проверка обновлений",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                return;
            }

            if (res.IsUpdateAvailable)
            {
                _latestAvailableUpdate = res;
                _btnCheckUpdate.Text = "Обновить v" + res.LatestVersion;
                _btnCheckUpdate.BackColor = Color.FromArgb(22, 163, 74);
                _btnCheckUpdate.FlatAppearance.BorderColor = Color.FromArgb(134, 239, 172);

                bool alreadyNotifiedThisVersion = string.Equals(_notifiedUpdateVersion, res.LatestVersion, StringComparison.OrdinalIgnoreCase);
                if (isManualUserClick || !alreadyNotifiedThisVersion)
                {
                    _notifiedUpdateVersion = res.LatestVersion;
                    _notifyIcon.ShowBalloonTip(
                        5000,
                        "Доступна новая версия Strelo v" + res.LatestVersion,
                        "Вышло обновление программы на GitHub! Нажмите кнопку «Обновить» для автоматической установки.",
                        ToolTipIcon.Info);

                    PromptInstallUpdate(res);
                }
            }
            else
            {
                _btnCheckUpdate.Text = "Обновление";
                if (isManualUserClick)
                {
                    MessageBox.Show(
                        string.Format("У вас установлена самая актуальная версия программы (v{0}).", res.CurrentVersion),
                        "Обновлений нет",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
        }

        private void PromptInstallUpdate(UpdateCheckResult res)
        {
            string msg = string.Format(
                "Доступна новая версия StreloReminder на GitHub!\n\n" +
                "Текущая версия: v{0}\n" +
                "Новая версия: v{1}\n\n" +
                "Скачать и автоматически установить новую версию прямо сейчас?",
                res.CurrentVersion,
                res.LatestVersion);

            DialogResult choice = MessageBox.Show(
                this,
                msg,
                "Доступно обновление StreloReminder",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (choice == DialogResult.Yes)
            {
                if (!string.IsNullOrWhiteSpace(res.ExeDownloadUrl))
                {
                    _btnCheckUpdate.Text = "Загрузка...";
                    Application.DoEvents();

                    string err;
                    if (UpdateService.DownloadAndInstallUpdate(res.ExeDownloadUrl, out err))
                    {
                        ExitApplication();
                        return;
                    }
                    else
                    {
                        MessageBox.Show(
                            err + "\n\nСейчас откроется страница загрузки релиза в браузере.",
                            "Автообновление",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }

                try
                {
                    Process.Start(res.ReleaseUrl);
                }
                catch
                {
                }
            }
        }

        private void UpdateCloudStatusUI()
        {
            if (_cloudConfig == null || !_cloudConfig.IsConfigured())
            {
                _lblSyncStatus.Text = "Облако: не настроено";
                _lblSyncStatus.ForeColor = Color.FromArgb(191, 219, 254);
            }
            else
            {
                string mode = _cloudConfig.ProviderType == CloudProviderType.GitHubGist ? "GitHub Gist" : "Cloud URL";
                _lblSyncStatus.Text = "Облако активно (" + mode + ")";
                _lblSyncStatus.ForeColor = Color.FromArgb(167, 243, 208);
            }
        }

        private void OnOpenCloudSyncSettingsClick(object sender, EventArgs e)
        {
            List<ReminderItem> snapshot = CloneRemindersList(_reminders);
            using (CloudSyncForm dlg = new CloudSyncForm(_cloudConfig, snapshot))
            {
                DialogResult res = dlg.ShowDialog(this);
                if (dlg.SyncTriggeredSuccessfully && dlg.SyncedReminders != null)
                {
                    ApplyMergedRemindersFromCloud(dlg.SyncedReminders);
                }
                else if (res == DialogResult.OK)
                {
                    UpdateCloudStatusUI();
                    if (_cloudConfig.IsConfigured())
                    {
                        TriggerBackgroundCloudSync(true);
                    }
                }
            }
        }

        private void TriggerBackgroundCloudSync(bool forcePush)
        {
            if (_cloudConfig == null || !_cloudConfig.IsConfigured())
            {
                if (forcePush)
                {
                    OnOpenCloudSyncSettingsClick(this, EventArgs.Empty);
                }
                return;
            }
            if (_isSyncInProgress)
            {
                return;
            }

            _isSyncInProgress = true;
            _secondsSinceLastSync = 0;
            _lblSyncStatus.Text = "Синхронизация с облаком...";

            List<ReminderItem> snapshot = CloneRemindersList(_reminders);
            CloudSyncConfig configSnapshot = new CloudSyncConfig
            {
                ProviderType = _cloudConfig.ProviderType,
                GitHubToken = _cloudConfig.GitHubToken,
                GistId = _cloudConfig.GistId,
                CustomCloudUrl = _cloudConfig.CustomCloudUrl,
                AutoSyncIntervalSeconds = _cloudConfig.AutoSyncIntervalSeconds
            };

            ThreadPool.QueueUserWorkItem(delegate
            {
                CloudSyncResult syncRes = CloudSyncService.Synchronize(configSnapshot, snapshot, forcePush);
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _isSyncInProgress = false;
                            if (syncRes.Success)
                            {
                                _lblSyncStatus.Text = syncRes.Message;
                                _lblSyncStatus.ForeColor = Color.FromArgb(167, 243, 208);
                                if (syncRes.LocalListUpdated && syncRes.MergedReminders != null)
                                {
                                    ApplyMergedRemindersFromCloud(syncRes.MergedReminders);
                                }
                            }
                            else
                            {
                                _lblSyncStatus.Text = syncRes.Message;
                                _lblSyncStatus.ForeColor = Color.FromArgb(254, 202, 202);
                            }
                        });
                    }
                    else
                    {
                        _isSyncInProgress = false;
                    }
                }
                catch
                {
                    _isSyncInProgress = false;
                }
            });
        }

        private void ApplyMergedRemindersFromCloud(List<ReminderItem> mergedFromCloud)
        {
            bool dummyRemote;
            bool dummyLocal;
            List<ReminderItem> finalMerged = CloudSyncService.MergeReminders(
                _reminders,
                mergedFromCloud,
                out dummyRemote,
                out dummyLocal);

            _reminders.Clear();
            for (int i = 0; i < finalMerged.Count; i++)
            {
                ReminderItem item = finalMerged[i];
                if (_activePopups.ContainsKey(item.Id))
                {
                    if (item.IsDeleted || item.IsCompleted || !item.IsEnabled || item.TriggerTime > DateTime.Now.AddSeconds(5))
                    {
                        PopupNotificationForm popup = _activePopups[item.Id];
                        _activePopups.Remove(item.Id);
                        item.IsPopupOpen = false;
                        try
                        {
                            popup.Close();
                        }
                        catch
                        {
                        }
                    }
                    else
                    {
                        item.IsPopupOpen = true;
                    }
                }
                _reminders.Add(item);
            }

            StorageService.SaveReminders(_reminders);
            RefreshListView();
        }

        private static List<ReminderItem> CloneRemindersList(List<ReminderItem> source)
        {
            List<ReminderItem> copy = new List<ReminderItem>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                {
                    copy.Add(source[i].Clone());
                }
            }
            return copy;
        }

        private void ShowPopupForReminder(ReminderItem item)
        {
            if (_activePopups.ContainsKey(item.Id))
                return;

            item.IsPopupOpen = true;
            RefreshListView();

            PopupNotificationForm popup = new PopupNotificationForm(item, OnPopupResolved);
            _activePopups[item.Id] = popup;
            popup.Show();
        }

        private void OnPopupResolved(ReminderItem item, PopupResultAction action)
        {
            item.IsPopupOpen = false;
            if (_activePopups.ContainsKey(item.Id))
            {
                _activePopups.Remove(item.Id);
            }

            StorageService.SaveReminders(_reminders);
            RefreshListView();
            TriggerBackgroundCloudSync(true);
        }

        private void OnTimeModeChanged(object sender, EventArgs e)
        {
            bool isTimer = _rbTimerMode.Checked;
            _pnlTimerControls.Visible = isTimer;
            _pnlExactTimeControls.Visible = !isTimer;
            if (isTimer)
            {
                UpdateTimerPreviewLabel();
            }
        }

        private void OnRecurrenceChanged(object sender, EventArgs e)
        {
            bool isInterval = _cmbRecurrence.SelectedIndex == (int)RecurrenceType.EveryMinutes;
            _lblIntervalMinutes.Visible = isInterval;
            _numIntervalMinutes.Visible = isInterval;
        }

        private void OnSaveReminderClick(object sender, EventArgs e)
        {
            string title = _txtTitle.Text.Trim();
            if (string.IsNullOrEmpty(title))
            {
                MessageBox.Show(
                    "Пожалуйста, введите текст напоминания.",
                    "Внимание",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _txtTitle.Focus();
                return;
            }

            DateTime triggerTime;
            if (_rbTimerMode.Checked)
            {
                triggerTime = CalculateSelectedTimerTarget(DateTime.Now);
            }
            else
            {
                DateTime d = _dtpDate.Value.Date;
                DateTime t = _dtpTime.Value;
                triggerTime = new DateTime(d.Year, d.Month, d.Day, t.Hour, t.Minute, t.Second);

                if (triggerTime <= DateTime.Now)
                {
                    DialogResult confirmPast = MessageBox.Show(
                        "Указанное время уже прошло. Сработать прямо сейчас?\n\n(Нажмите «Нет», чтобы перенести на завтра в это же время)",
                        "Время в прошлом",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question);

                    if (confirmPast == DialogResult.Cancel)
                        return;
                    if (confirmPast == DialogResult.No)
                        triggerTime = triggerTime.AddDays(1);
                }
            }

            ReminderItem target = null;
            if (!string.IsNullOrEmpty(_editingReminderId))
            {
                target = FindReminderById(_editingReminderId);
            }

            bool isNew = target == null;
            if (isNew)
            {
                target = new ReminderItem();
            }

            target.Title = title;
            target.Notes = _txtNotes.Text.Trim();
            target.TriggerTime = triggerTime;
            target.Recurrence = (RecurrenceType)_cmbRecurrence.SelectedIndex;
            target.IntervalMinutes = (int)_numIntervalMinutes.Value;
            target.Priority = (ReminderPriority)_cmbPriority.SelectedIndex;
            target.PlaySound = _chkPlaySound.Checked;
            target.IsEnabled = true;
            target.IsCompleted = false;
            target.IsDeleted = false;
            target.Touch();

            if (isNew)
            {
                _reminders.Add(target);
            }

            StorageService.SaveReminders(_reminders);
            ResetEditor();
            RefreshListView();
            TriggerBackgroundCloudSync(true);
        }

        private void ResetEditor()
        {
            _editingReminderId = null;
            _grpEditor.Text = " Новое напоминание ";
            _txtTitle.Text = string.Empty;
            _txtNotes.Text = string.Empty;
            _rbTimerMode.Checked = true;
            SetTimerUnitValues(0, 0, 0, 0, 15, 0);
            _dtpDate.Value = DateTime.Now.Date;
            _dtpTime.Value = DateTime.Now.AddMinutes(15);
            _cmbRecurrence.SelectedIndex = 0;
            _numIntervalMinutes.Value = 30;
            _cmbPriority.SelectedIndex = 0;
            _chkPlaySound.Checked = true;
            _btnSaveReminder.Text = "+ Добавить напоминание";
            _btnSaveReminder.BackColor = Color.FromArgb(37, 99, 235);
            _btnCancelEdit.Visible = false;
        }

        private void OnTestPopupClick(object sender, EventArgs e)
        {
            ReminderItem demo = new ReminderItem
            {
                Title = string.IsNullOrWhiteSpace(_txtTitle.Text)
                    ? "Пример напоминания: Сделать перерыв и размяться!"
                    : _txtTitle.Text.Trim(),
                Notes = string.IsNullOrWhiteSpace(_txtNotes.Text)
                    ? "Это демонстрационное всплывающее окно поверх всех окон. Вы можете отложить его или нажать «Понятно»."
                    : _txtNotes.Text.Trim(),
                TriggerTime = DateTime.Now,
                Recurrence = (RecurrenceType)_cmbRecurrence.SelectedIndex,
                IntervalMinutes = (int)_numIntervalMinutes.Value,
                Priority = (ReminderPriority)_cmbPriority.SelectedIndex,
                PlaySound = _chkPlaySound.Checked
            };

            PopupNotificationForm testForm = new PopupNotificationForm(demo, null);
            testForm.Show();
        }

        private void CreateQuickReminder(int minutes)
        {
            ReminderItem quick = new ReminderItem
            {
                Title = string.Format("Быстрое напоминание ({0} мин)", minutes),
                Notes = "Создано из меню системного трея.",
                TriggerTime = DateTime.Now.AddMinutes(minutes),
                Recurrence = RecurrenceType.None,
                Priority = ReminderPriority.Normal,
                PlaySound = true,
                IsEnabled = true
            };
            quick.Touch();

            _reminders.Add(quick);
            StorageService.SaveReminders(_reminders);
            RefreshListView();
            TriggerBackgroundCloudSync(true);

            _notifyIcon.ShowBalloonTip(
                2500,
                "Напоминание добавлено",
                string.Format("Сработает через {0} мин ({1:HH:mm}).", minutes, quick.TriggerTime),
                ToolTipIcon.Info);
        }

        private ReminderItem GetSelectedReminder()
        {
            if (_lvReminders.SelectedItems.Count == 0)
                return null;

            string id = _lvReminders.SelectedItems[0].Tag as string;
            return FindReminderById(id);
        }

        private ReminderItem FindReminderById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            for (int i = 0; i < _reminders.Count; i++)
            {
                if (_reminders[i].Id == id && !_reminders[i].IsDeleted)
                    return _reminders[i];
            }
            return null;
        }

        private void OnEditSelectedClick(object sender, EventArgs e)
        {
            ReminderItem item = GetSelectedReminder();
            if (item == null)
            {
                MessageBox.Show("Выберите напоминание в таблице для редактирования.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _editingReminderId = item.Id;
            _grpEditor.Text = " Редактирование напоминания ";
            _txtTitle.Text = item.Title;
            _txtNotes.Text = item.Notes;
            _rbExactTimeMode.Checked = true;

            DateTime trigger = item.TriggerTime < new DateTime(2000, 1, 1) ? DateTime.Now : item.TriggerTime;
            _dtpDate.Value = trigger.Date;
            _dtpTime.Value = trigger;

            int y, mo, d, h, m, s;
            ReminderItem.CalculateCalendarDiff(DateTime.Now, trigger, out y, out mo, out d, out h, out m, out s);
            SetTimerUnitValues(y, mo, d, h, m, s);

            _cmbRecurrence.SelectedIndex = (int)item.Recurrence;
            _numIntervalMinutes.Value = Math.Max(1, Math.Min(1440, item.IntervalMinutes));
            _cmbPriority.SelectedIndex = (int)item.Priority;
            _chkPlaySound.Checked = item.PlaySound;

            _btnSaveReminder.Text = "Сохранить изменения";
            _btnSaveReminder.BackColor = Color.FromArgb(22, 163, 74);
            _btnCancelEdit.Visible = true;
            _txtTitle.Focus();
        }

        private void OnToggleSelectedClick(object sender, EventArgs e)
        {
            ReminderItem item = GetSelectedReminder();
            if (item == null) return;

            if (item.IsCompleted)
            {
                item.IsCompleted = false;
                item.IsEnabled = true;
                if (item.TriggerTime <= DateTime.Now)
                {
                    item.TriggerTime = DateTime.Now.AddMinutes(15);
                }
            }
            else
            {
                item.IsEnabled = !item.IsEnabled;
            }
            item.Touch();

            StorageService.SaveReminders(_reminders);
            RefreshListView();
            TriggerBackgroundCloudSync(true);
        }

        private void OnSnooze10SelectedClick(object sender, EventArgs e)
        {
            ReminderItem item = GetSelectedReminder();
            if (item == null) return;

            item.Snooze(10);
            StorageService.SaveReminders(_reminders);
            RefreshListView();
            TriggerBackgroundCloudSync(true);
        }

        private void OnDeleteSelectedClick(object sender, EventArgs e)
        {
            ReminderItem item = GetSelectedReminder();
            if (item == null) return;

            DialogResult res = MessageBox.Show(
                string.Format("Удалить напоминание «{0}»?", item.Title),
                "Подтверждение удаления",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res == DialogResult.Yes)
            {
                item.IsDeleted = true;
                item.IsEnabled = false;
                item.Touch();

                if (_editingReminderId == item.Id)
                {
                    ResetEditor();
                }
                StorageService.SaveReminders(_reminders);
                RefreshListView();
                TriggerBackgroundCloudSync(true);
            }
        }

        private void OnClearCompletedClick(object sender, EventArgs e)
        {
            int count = 0;
            for (int i = 0; i < _reminders.Count; i++)
            {
                ReminderItem r = _reminders[i];
                if (!r.IsDeleted && r.IsCompleted)
                {
                    r.IsDeleted = true;
                    r.IsEnabled = false;
                    r.Touch();
                    count++;
                }
            }

            if (count > 0)
            {
                StorageService.SaveReminders(_reminders);
                RefreshListView();
                TriggerBackgroundCloudSync(true);
            }
        }

        private void OnOpenStorageFolderClick(object sender, EventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", StorageService.GetStorageDirectory());
            }
            catch
            {
            }
        }

        private void OnDoNotDisturbChanged(object sender, EventArgs e)
        {
            _isDoNotDisturb = _chkDoNotDisturb.Checked;
            if (_trayPauseItem.Checked != _isDoNotDisturb)
            {
                _trayPauseItem.Checked = _isDoNotDisturb;
            }

            Icon icon = IconGenerator.GetAppIcon(_isDoNotDisturb);
            Icon = icon;
            _notifyIcon.Icon = icon;
            _notifyIcon.Text = _isDoNotDisturb
                ? "Strelo — Напоминалка (Режим «Не беспокоить»)"
                : "Strelo — Напоминалка для Windows v" + UpdateService.CurrentVersion;
        }

        private void OnAutoStartChanged(object sender, EventArgs e)
        {
            StorageService.SetAutoStart(_chkAutoStart.Checked);
        }

        private void RefreshListView()
        {
            string selectedId = null;
            if (_lvReminders.SelectedItems.Count > 0)
            {
                selectedId = _lvReminders.SelectedItems[0].Tag as string;
            }

            _reminders.Sort(CompareReminders);

            _lvReminders.BeginUpdate();
            _lvReminders.Items.Clear();

            DateTime now = DateTime.Now;
            int totalVisible = 0;
            int activeCount = 0;

            for (int i = 0; i < _reminders.Count; i++)
            {
                ReminderItem r = _reminders[i];
                if (r.IsDeleted)
                    continue;

                totalVisible++;
                if (r.IsEnabled && !r.IsCompleted)
                {
                    activeCount++;
                }

                string statusText = GetStatusText(r);
                ListViewItem lvi = new ListViewItem(statusText);
                lvi.Tag = r.Id;
                lvi.SubItems.Add(r.Title);
                lvi.SubItems.Add(r.TriggerTime.ToString("dd.MM.yyyy HH:mm:ss"));
                lvi.SubItems.Add(r.GetRemainingTimeText(now));
                lvi.SubItems.Add(r.GetRecurrenceDisplayText());
                lvi.SubItems.Add(r.Notes);

                ApplyRowStyling(lvi, r);

                if (selectedId != null && r.Id == selectedId)
                {
                    lvi.Selected = true;
                }

                _lvReminders.Items.Add(lvi);
            }

            _lvReminders.EndUpdate();

            _lblSummary.Text = string.Format("Список напоминаний (Всего: {0}, Активных: {1})",
                totalVisible, activeCount);
        }

        private void UpdateListViewCountdowns(DateTime now)
        {
            for (int i = 0; i < _lvReminders.Items.Count; i++)
            {
                ListViewItem lvi = _lvReminders.Items[i];
                string id = lvi.Tag as string;
                ReminderItem r = FindReminderById(id);
                if (r != null && lvi.SubItems.Count >= 4)
                {
                    string newRemaining = r.GetRemainingTimeText(now);
                    if (lvi.SubItems[3].Text != newRemaining)
                    {
                        lvi.SubItems[3].Text = newRemaining;
                    }
                    string newStatus = GetStatusText(r);
                    if (lvi.SubItems[0].Text != newStatus)
                    {
                        lvi.SubItems[0].Text = newStatus;
                        ApplyRowStyling(lvi, r);
                    }
                }
            }
        }

        private static string GetStatusText(ReminderItem r)
        {
            if (r.IsPopupOpen)
                return "🔔 Сработало!";
            if (r.IsCompleted)
                return "✓ Выполнено";
            if (!r.IsEnabled)
                return "⏸ Пауза";
            if (r.Priority == ReminderPriority.High)
                return "⚡ Важное";
            return "🟢 Активно";
        }

        private static void ApplyRowStyling(ListViewItem lvi, ReminderItem r)
        {
            if (r.IsPopupOpen)
            {
                lvi.BackColor = Color.FromArgb(254, 243, 199);
                lvi.ForeColor = Color.FromArgb(146, 64, 14);
            }
            else if (r.IsCompleted)
            {
                lvi.BackColor = Color.FromArgb(248, 250, 252);
                lvi.ForeColor = Color.FromArgb(148, 163, 184);
            }
            else if (!r.IsEnabled)
            {
                lvi.BackColor = Color.FromArgb(241, 245, 249);
                lvi.ForeColor = Color.FromArgb(100, 116, 139);
            }
            else if (r.Priority == ReminderPriority.High)
            {
                lvi.BackColor = Color.FromArgb(254, 242, 242);
                lvi.ForeColor = Color.FromArgb(153, 27, 27);
            }
            else
            {
                lvi.BackColor = Color.White;
                lvi.ForeColor = Color.FromArgb(15, 23, 42);
            }
        }

        private static int CompareReminders(ReminderItem a, ReminderItem b)
        {
            bool aActive = !a.IsDeleted && a.IsEnabled && !a.IsCompleted;
            bool bActive = !b.IsDeleted && b.IsEnabled && !b.IsCompleted;
            if (aActive != bActive)
            {
                return aActive ? -1 : 1;
            }
            return a.TriggerTime.CompareTo(b.TriggerTime);
        }

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;

            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _notifyIcon.ShowBalloonTip(
                    3000,
                    "Напоминалка работает в фоне",
                    "Программа свёрнута в системный трей и вовремя покажет всплывающее окно напоминания.",
                    ToolTipIcon.Info);
            }
        }

        private void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
        }

        private void ExitApplication()
        {
            _isExiting = true;
            _mainTimer.Stop();
            StorageService.SaveReminders(_reminders);
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            StorageService.SaveReminders(_reminders);
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
            }
            base.OnFormClosing(e);
        }
    }
}
