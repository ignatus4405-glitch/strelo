using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using StreloReminder.Models;
using StreloReminder.Services;

namespace StreloReminder.Forms
{
    public class CloudSyncForm : Form
    {
        private static readonly int[] PresetSeconds = new int[]
        {
            5, 10, 15, 30, 60, 300, 900, 1800, 3600, 0
        };

        private readonly CloudSyncConfig _config;
        private readonly List<ReminderItem> _currentReminders;

        private ComboBox _cmbProvider;

        // Вариант 3: Облако в 1 клик (без токенов)
        private Panel _pnlOneClick;
        private TextBox _txtOneClickRoomId;
        private Button _btnCreateOneClick;

        // Вариант 1: GitHub Gist
        private Panel _pnlGist;
        private TextBox _txtGitHubToken;
        private TextBox _txtGistId;
        private Button _btnCreateGist;

        // Вариант 2: Прямой JSON URL
        private Panel _pnlCustomUrl;
        private TextBox _txtCustomUrl;

        // Локальная синхронизация по Wi-Fi (LAN)
        private CheckBox _chkLanWifiEnabled;
        private Label _lblLanIpInfo;

        // Выбор времени синхронизации между устройствами
        private ComboBox _cmbSyncPreset;
        private NumericUpDown _numSyncValue;
        private ComboBox _cmbSyncUnit;
        private bool _updatingSyncControls;

        // Привязка телефона + QR-код
        private TextBox _txtConnectionCode;
        private PictureBox _picQrCode;
        private Label _lblStatus;

        public bool SyncTriggeredSuccessfully { get; private set; }
        public List<ReminderItem> SyncedReminders { get; private set; }

        public CloudSyncForm(CloudSyncConfig config, List<ReminderItem> currentReminders)
        {
            _config = config ?? new CloudSyncConfig();
            _currentReminders = currentReminders ?? new List<ReminderItem>();
            InitializeComponent();
            LoadConfigToControls();
        }

        private void InitializeComponent()
        {
            Text = "Синхронизация ПК ↔ Android (Облако в 1 клик, QR-код и локальный Wi-Fi)";
            Icon = IconGenerator.GetAppIcon(false);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(780, 650);
            BackColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);

            Panel headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(780, 56),
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(30, 58, 138)
            };

            Label lblHeader = new Label
            {
                Text = "☁📡 Синхронизация ПК и Android: Облако в 1 клик + QR-код + Wi-Fi",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(18, 16)
            };
            headerPanel.Controls.Add(lblHeader);

            // 1. ВЫБОР ОБЛАЧНОГО РЕЖИМА
            Label lblProvider = new Label
            {
                Text = "Режим интернет-облака:",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(18, 70),
                AutoSize = true
            };

            _cmbProvider = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(210, 66),
                Size = new Size(552, 26)
            };
            _cmbProvider.Items.AddRange(new object[]
            {
                "Отключено (только локальная синхронизация по Wi-Fi)",
                "GitHub Gist (облако через личный токен GitHub)",
                "Прямой облачный JSON URL (Firebase / свой сервер)",
                "⭐ Облако в 1 клик (без токенов и регистрации — рекомендуется)"
            });
            _cmbProvider.SelectedIndexChanged += OnProviderChanged;

            // ПАНЕЛЬ 1: ОБЛАКО В 1 КЛИК (БЕЗ ТОКЕНОВ)
            _pnlOneClick = new Panel
            {
                Location = new Point(18, 102),
                Size = new Size(744, 140),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };

            Label lblOneClickTitle = new Label
            {
                Text = "Облако в 1 клик — мгновенная синхронизация через интернет без регистрации и токенов:",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175),
                Location = new Point(14, 12),
                AutoSize = true
            };

            _btnCreateOneClick = new Button
            {
                Text = "☁  Создать новое Облако в 1 клик (автоматически)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(14, 36),
                Size = new Size(430, 36),
                Padding = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnCreateOneClick.FlatAppearance.BorderSize = 0;
            _btnCreateOneClick.Click += OnCreateOneClickCloudClick;

            Label lblRoomId = new Label
            {
                Text = "ID облачной комнаты (создаётся автоматически или вводится с телефона):",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(14, 80),
                AutoSize = true
            };

            _txtOneClickRoomId = new TextBox
            {
                Font = new Font("Consolas", 9.75f, FontStyle.Bold),
                Location = new Point(14, 102),
                Size = new Size(714, 26)
            };
            _txtOneClickRoomId.TextChanged += delegate { UpdateConnectionCodeAndQrPreview(); };

            _pnlOneClick.Controls.Add(lblOneClickTitle);
            _pnlOneClick.Controls.Add(_btnCreateOneClick);
            _pnlOneClick.Controls.Add(lblRoomId);
            _pnlOneClick.Controls.Add(_txtOneClickRoomId);

            // ПАНЕЛЬ 2: GITHUB GIST
            _pnlGist = new Panel
            {
                Location = new Point(18, 102),
                Size = new Size(744, 140),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };

            Label lblToken = new Label
            {
                Text = "GitHub Personal Access Token (с правом «gist»):",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(14, 10),
                AutoSize = true
            };

            _txtGitHubToken = new TextBox
            {
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(14, 32),
                Size = new Size(714, 26),
                UseSystemPasswordChar = true
            };
            _txtGitHubToken.TextChanged += delegate { UpdateConnectionCodeAndQrPreview(); };

            Label lblGistId = new Label
            {
                Text = "ID облачного Gist (оставьте пустым и нажмите «Создать облако Gist»):",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(14, 64),
                AutoSize = true
            };

            _txtGistId = new TextBox
            {
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(14, 88),
                Size = new Size(470, 26)
            };
            _txtGistId.TextChanged += delegate { UpdateConnectionCodeAndQrPreview(); };

            _btnCreateGist = new Button
            {
                Text = "Создать новое облако Gist",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(494, 86),
                Size = new Size(234, 30),
                Padding = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(219, 234, 254),
                ForeColor = Color.FromArgb(30, 64, 175),
                Cursor = Cursors.Hand
            };
            _btnCreateGist.FlatAppearance.BorderColor = Color.FromArgb(147, 197, 253);
            _btnCreateGist.Click += OnCreateGistClick;

            Label lblGistHint = new Label
            {
                Text = "Совет: если вы не хотите создавать токен GitHub, выберите режим «⭐ Облако в 1 клик» выше.",
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(14, 118),
                AutoSize = true
            };

            _pnlGist.Controls.Add(lblToken);
            _pnlGist.Controls.Add(_txtGitHubToken);
            _pnlGist.Controls.Add(lblGistId);
            _pnlGist.Controls.Add(_txtGistId);
            _pnlGist.Controls.Add(_btnCreateGist);
            _pnlGist.Controls.Add(lblGistHint);

            // ПАНЕЛЬ 3: ПРЯМОЙ CLOUD JSON URL
            _pnlCustomUrl = new Panel
            {
                Location = new Point(18, 102),
                Size = new Size(744, 140),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };

            Label lblUrl = new Label
            {
                Text = "HTTPS URL облачного JSON-хранилища (поддерживающего GET и PUT):",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(14, 16),
                AutoSize = true
            };

            _txtCustomUrl = new TextBox
            {
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(14, 42),
                Size = new Size(714, 26)
            };
            _txtCustomUrl.TextChanged += delegate { UpdateConnectionCodeAndQrPreview(); };

            Label lblUrlHint = new Label
            {
                Text = "Например: https://<ваш-проект>.firebaseio.com/strelo_reminders.json\nили любой другой REST JSON URL.",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(14, 78),
                Size = new Size(714, 46)
            };

            _pnlCustomUrl.Controls.Add(lblUrl);
            _pnlCustomUrl.Controls.Add(_txtCustomUrl);
            _pnlCustomUrl.Controls.Add(lblUrlHint);

            // 2. БЛОК ЛОКАЛЬНОЙ СИНХРОНИЗАЦИИ ПО WI-FI (БЕЗ ОБРЕЗАНИЯ ТЕКСТА)
            GroupBox grpLan = new GroupBox
            {
                Text = " 📡 Локальная синхронизация по домашнему Wi-Fi (с автопоиском ПК без внешних серверов) ",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                BackColor = Color.White,
                Location = new Point(18, 250),
                Size = new Size(744, 96)
            };

            _chkLanWifiEnabled = new CheckBox
            {
                Text = "Включить локальную синхронизацию по Wi-Fi и автообнаружение этого ПК в сети",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(22, 101, 52),
                Location = new Point(14, 24),
                AutoSize = true,
                Checked = true,
                Cursor = Cursors.Hand
            };
            _chkLanWifiEnabled.CheckedChanged += delegate { UpdateConnectionCodeAndQrPreview(); };

            string primaryIp = LanSyncService.GetPrimaryLanIp();
            _lblLanIpInfo = new Label
            {
                Text = string.Format(
                    "Адрес этого ПК в локальной сети: {0}:{1} (Автопоиск UDP порт {2}).\n" +
                    "В приложении на телефоне нажмите кнопку «📡 Найти компьютер в сети Wi-Fi автоматически».",
                    primaryIp,
                    LanSyncService.DefaultHttpPort,
                    LanSyncService.DiscoveryUdpPort),
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                ForeColor = Color.FromArgb(71, 85, 105),
                Location = new Point(14, 48),
                Size = new Size(714, 40),
                AutoSize = false
            };

            grpLan.Controls.Add(_chkLanWifiEnabled);
            grpLan.Controls.Add(_lblLanIpInfo);

            // 3. БЛОК ВЫБОРА ВРЕМЕНИ СИНХРОНИЗАЦИИ, ПРИВЯЗКИ И QR-КОДА
            GroupBox grpPairing = new GroupBox
            {
                Text = " 📱 Выбор времени синхронизации и привязка телефона Android по QR-коду ",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                BackColor = Color.White,
                Location = new Point(18, 354),
                Size = new Size(744, 204)
            };

            Label lblInterval = new Label
            {
                Text = "Время синхронизации между устройствами:",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(14, 24),
                AutoSize = true
            };

            _cmbSyncPreset = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(14, 46),
                Size = new Size(276, 26)
            };
            _cmbSyncPreset.Items.AddRange(new object[]
            {
                "Каждые 5 секунд (мгновенно)",
                "Каждые 10 секунд",
                "Каждые 15 секунд (рекомендуется)",
                "Каждые 30 секунд",
                "Каждую 1 минуту",
                "Каждые 5 минут",
                "Каждые 15 минут",
                "Каждые 30 минут",
                "Каждый 1 час",
                "Свой интервал..."
            });
            _cmbSyncPreset.SelectedIndexChanged += OnSyncPresetChanged;

            Label lblCustomVal = new Label
            {
                Text = "или каждые:",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(300, 50),
                AutoSize = true
            };

            _numSyncValue = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 3600,
                Value = 15,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(382, 46),
                Size = new Size(68, 26)
            };
            _numSyncValue.ValueChanged += OnCustomSyncValueOrUnitChanged;

            _cmbSyncUnit = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(456, 46),
                Size = new Size(76, 26)
            };
            _cmbSyncUnit.Items.AddRange(new object[] { "сек.", "мин.", "ч." });
            _cmbSyncUnit.SelectedIndex = 0;
            _cmbSyncUnit.SelectedIndexChanged += OnCustomSyncValueOrUnitChanged;

            Label lblCodeHint = new Label
            {
                Text = "Отсканируйте QR-код справа камерой телефона ИЛИ скопируйте код ниже:",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 64, 175),
                Location = new Point(14, 82),
                AutoSize = true
            };

            _txtConnectionCode = new TextBox
            {
                Font = new Font("Consolas", 8.75f, FontStyle.Regular),
                Location = new Point(14, 104),
                Size = new Size(518, 24),
                ReadOnly = true,
                BackColor = Color.FromArgb(248, 250, 252)
            };

            Button btnCopyCode = new Button
            {
                Text = "Скопировать код для телефона",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(14, 136),
                Size = new Size(252, 34),
                Padding = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(219, 234, 254),
                ForeColor = Color.FromArgb(30, 64, 175),
                Cursor = Cursors.Hand
            };
            btnCopyCode.FlatAppearance.BorderColor = Color.FromArgb(147, 197, 253);
            btnCopyCode.Click += OnCopyCodeClick;

            Button btnPasteCode = new Button
            {
                Text = "Вставить код из буфера обмена",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(276, 136),
                Size = new Size(256, 34),
                Padding = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(226, 232, 240),
                ForeColor = Color.FromArgb(30, 41, 59),
                Cursor = Cursors.Hand
            };
            btnPasteCode.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnPasteCode.Click += OnPasteCodeClick;

            Label lblQrNote = new Label
            {
                Text = "QR-код содержит одновременно настройки Облака, интервал и локальный Wi-Fi IP вашего ПК!",
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(14, 176),
                AutoSize = true
            };

            _picQrCode = new PictureBox
            {
                Location = new Point(554, 22),
                Size = new Size(172, 172),
                Padding = new Padding(4),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom
            };

            grpPairing.Controls.Add(lblInterval);
            grpPairing.Controls.Add(_cmbSyncPreset);
            grpPairing.Controls.Add(lblCustomVal);
            grpPairing.Controls.Add(_numSyncValue);
            grpPairing.Controls.Add(_cmbSyncUnit);
            grpPairing.Controls.Add(lblCodeHint);
            grpPairing.Controls.Add(_txtConnectionCode);
            grpPairing.Controls.Add(btnCopyCode);
            grpPairing.Controls.Add(btnPasteCode);
            grpPairing.Controls.Add(lblQrNote);
            grpPairing.Controls.Add(_picQrCode);

            _lblStatus = new Label
            {
                Text = "Готово к настройке.",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Location = new Point(18, 566),
                Size = new Size(744, 22),
                AutoEllipsis = true
            };

            Button btnTestSync = new Button
            {
                Text = "Проверить и синхронизировать сейчас",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(18, 594),
                Size = new Size(310, 42),
                Padding = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(219, 234, 254),
                ForeColor = Color.FromArgb(30, 64, 175),
                Cursor = Cursors.Hand
            };
            btnTestSync.FlatAppearance.BorderColor = Color.FromArgb(147, 197, 253);
            btnTestSync.Click += OnTestSyncClick;

            Button btnSave = new Button
            {
                Text = "Сохранить настройки",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(340, 594),
                Size = new Size(288, 42),
                Padding = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(22, 163, 74),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += OnSaveClick;

            Button btnClose = new Button
            {
                Text = "Закрыть",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(640, 594),
                Size = new Size(122, 42),
                Padding = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(226, 232, 240),
                ForeColor = Color.FromArgb(51, 65, 85),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnClose.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(headerPanel);
            Controls.Add(lblProvider);
            Controls.Add(_cmbProvider);
            Controls.Add(_pnlOneClick);
            Controls.Add(_pnlGist);
            Controls.Add(_pnlCustomUrl);
            Controls.Add(grpLan);
            Controls.Add(grpPairing);
            Controls.Add(_lblStatus);
            Controls.Add(btnTestSync);
            Controls.Add(btnSave);
            Controls.Add(btnClose);
        }

        private void SetSyncIntervalControlsFromSeconds(int totalSeconds)
        {
            if (totalSeconds < 3) totalSeconds = 15;
            _updatingSyncControls = true;
            try
            {
                int matchedPresetIdx = PresetSeconds.Length - 1;
                for (int i = 0; i < PresetSeconds.Length - 1; i++)
                {
                    if (PresetSeconds[i] == totalSeconds)
                    {
                        matchedPresetIdx = i;
                        break;
                    }
                }
                _cmbSyncPreset.SelectedIndex = matchedPresetIdx;

                if (totalSeconds % 3600 == 0 && totalSeconds >= 3600)
                {
                    _cmbSyncUnit.SelectedIndex = 2; // ч.
                    _numSyncValue.Value = Math.Max(1, Math.Min(3600, totalSeconds / 3600));
                }
                else if (totalSeconds % 60 == 0 && totalSeconds >= 60)
                {
                    _cmbSyncUnit.SelectedIndex = 1; // мин.
                    _numSyncValue.Value = Math.Max(1, Math.Min(3600, totalSeconds / 60));
                }
                else
                {
                    _cmbSyncUnit.SelectedIndex = 0; // сек.
                    _numSyncValue.Value = Math.Max(3, Math.Min(3600, totalSeconds));
                }
            }
            finally
            {
                _updatingSyncControls = false;
            }
        }

        private int GetSelectedSyncIntervalSeconds()
        {
            int val = (int)_numSyncValue.Value;
            int unit = _cmbSyncUnit.SelectedIndex;
            int seconds = val;
            if (unit == 1) seconds = val * 60;
            else if (unit == 2) seconds = val * 3600;
            return Math.Max(3, Math.Min(86400, seconds));
        }

        private void OnSyncPresetChanged(object sender, EventArgs e)
        {
            if (_updatingSyncControls) return;
            int idx = _cmbSyncPreset.SelectedIndex;
            if (idx >= 0 && idx < PresetSeconds.Length - 1)
            {
                SetSyncIntervalControlsFromSeconds(PresetSeconds[idx]);
                UpdateConnectionCodeAndQrPreview();
            }
        }

        private void OnCustomSyncValueOrUnitChanged(object sender, EventArgs e)
        {
            if (_updatingSyncControls) return;
            int totalSec = GetSelectedSyncIntervalSeconds();
            _updatingSyncControls = true;
            try
            {
                if (_cmbSyncUnit.SelectedIndex == 0 && _numSyncValue.Value < 3)
                {
                    _numSyncValue.Value = 3;
                    totalSec = 3;
                }
                int matchedPresetIdx = PresetSeconds.Length - 1;
                for (int i = 0; i < PresetSeconds.Length - 1; i++)
                {
                    if (PresetSeconds[i] == totalSec)
                    {
                        matchedPresetIdx = i;
                        break;
                    }
                }
                _cmbSyncPreset.SelectedIndex = matchedPresetIdx;
            }
            finally
            {
                _updatingSyncControls = false;
            }
            UpdateConnectionCodeAndQrPreview();
        }

        private void LoadConfigToControls()
        {
            int providerIdx = (int)_config.ProviderType;
            if (providerIdx < 0 || providerIdx > 3) providerIdx = 3;
            if (_config.ProviderType == CloudProviderType.Disabled && !_config.IsCloudConfigured())
            {
                providerIdx = (int)CloudProviderType.OneClickCloud;
            }

            _cmbProvider.SelectedIndex = providerIdx;
            string roomId = _config.OneClickRoomId ?? string.Empty;
            if (providerIdx == (int)CloudProviderType.OneClickCloud && string.IsNullOrWhiteSpace(roomId))
            {
                roomId = CloudSyncService.GenerateNewRoomId();
            }
            _txtOneClickRoomId.Text = roomId;
            _txtGitHubToken.Text = _config.GitHubToken ?? string.Empty;
            _txtGistId.Text = _config.GistId ?? string.Empty;
            _txtCustomUrl.Text = _config.CustomCloudUrl ?? string.Empty;
            _chkLanWifiEnabled.Checked = _config.LanWifiSyncEnabled;
            SetSyncIntervalControlsFromSeconds(_config.AutoSyncIntervalSeconds <= 0 ? 15 : _config.AutoSyncIntervalSeconds);

            OnProviderChanged(this, EventArgs.Empty);
            UpdateConnectionCodeAndQrPreview();
        }

        private void ApplyControlsToConfig(CloudSyncConfig target)
        {
            target.ProviderType = (CloudProviderType)_cmbProvider.SelectedIndex;
            if (target.ProviderType == CloudProviderType.OneClickCloud && string.IsNullOrWhiteSpace(_txtOneClickRoomId.Text))
            {
                _txtOneClickRoomId.Text = CloudSyncService.GenerateNewRoomId();
            }
            target.OneClickRoomId = CloudSyncConfig.SanitizeRoomId(_txtOneClickRoomId.Text);
            target.GitHubToken = _txtGitHubToken.Text.Trim();
            target.GistId = _txtGistId.Text.Trim();
            target.CustomCloudUrl = _txtCustomUrl.Text.Trim();
            target.LanWifiSyncEnabled = _chkLanWifiEnabled.Checked;
            target.LanPcIp = LanSyncService.GetPrimaryLanIp();
            target.LanPort = LanSyncService.DefaultHttpPort;
            target.AutoSyncIntervalSeconds = GetSelectedSyncIntervalSeconds();
        }

        private void OnProviderChanged(object sender, EventArgs e)
        {
            CloudProviderType provider = (CloudProviderType)_cmbProvider.SelectedIndex;
            _pnlOneClick.Visible = (provider == CloudProviderType.OneClickCloud);
            _pnlGist.Visible = (provider == CloudProviderType.GitHubGist);
            _pnlCustomUrl.Visible = (provider == CloudProviderType.CustomJsonUrl);
            UpdateConnectionCodeAndQrPreview();
        }

        private void UpdateConnectionCodeAndQrPreview()
        {
            if (_txtConnectionCode == null || _picQrCode == null)
                return;

            CloudSyncConfig temp = new CloudSyncConfig();
            ApplyControlsToConfig(temp);

            string code = temp.ToConnectionCode();
            _txtConnectionCode.Text = code;

            string qrPayload = temp.ToQrDeepLink();
            if (string.IsNullOrEmpty(qrPayload))
            {
                qrPayload = code;
            }

            try
            {
                Image oldImg = _picQrCode.Image;
                _picQrCode.Image = QrCodeGenerator.GenerateQrBitmap(qrPayload, 160);
                if (oldImg != null)
                {
                    oldImg.Dispose();
                }
            }
            catch
            {
            }
        }

        private void OnCreateOneClickCloudClick(object sender, EventArgs e)
        {
            _lblStatus.ForeColor = Color.FromArgb(30, 64, 175);
            _lblStatus.Text = "Создание новой облачной комнаты в 1 клик...";
            _btnCreateOneClick.Enabled = false;
            Application.DoEvents();

            string roomId;
            string cloudUrl;
            string error;
            bool ok = CloudSyncService.CreateOneClickCloudRoom(_currentReminders, out roomId, out cloudUrl, out error);
            _btnCreateOneClick.Enabled = true;

            if (ok)
            {
                _cmbProvider.SelectedIndex = (int)CloudProviderType.OneClickCloud;
                _txtOneClickRoomId.Text = roomId;
                ApplyControlsToConfig(_config);
                StorageService.SaveCloudConfig(_config);
                UpdateConnectionCodeAndQrPreview();

                _lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                _lblStatus.Text = "✓ Облако в 1 клик создано! Комната: " + roomId + ". Отсканируйте QR-код телефоном!";

                MessageBox.Show(
                    "Облако в 1 клик успешно создано и сохранено!\n\n" +
                    "ID вашей комнаты: " + roomId + "\n\n" +
                    "Теперь просто наведите камеру телефона на QR-код в этом окне (или нажмите «Скопировать код для телефона»). Никаких токенов вводить не нужно!",
                    "Облако в 1 клик готово",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                _lblStatus.ForeColor = Color.FromArgb(185, 28, 28);
                _lblStatus.Text = error;
                MessageBox.Show(error, "Ошибка создания облака", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnCreateGistClick(object sender, EventArgs e)
        {
            string token = _txtGitHubToken.Text.Trim();
            if (string.IsNullOrEmpty(token))
            {
                MessageBox.Show(
                    "Сначала введите ваш GitHub Personal Access Token (с правом gist), либо выберите режим «Облако в 1 клик» без токенов.",
                    "Внимание",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _txtGitHubToken.Focus();
                return;
            }

            _lblStatus.ForeColor = Color.FromArgb(30, 64, 175);
            _lblStatus.Text = "Создание нового секретного Gist в облаке GitHub...";
            Application.DoEvents();

            string createdId;
            string error;
            if (CloudSyncService.CreateNewGist(token, _currentReminders, out createdId, out error))
            {
                _txtGistId.Text = createdId;
                UpdateConnectionCodeAndQrPreview();
                _lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                _lblStatus.Text = "Облако Gist успешно создано! ID: " + createdId;
                MessageBox.Show(
                    "Новое облачное хранилище Gist успешно создано!\n\nGist ID: " + createdId +
                    "\n\nТеперь отсканируйте QR-код справа или скопируйте код для телефона.",
                    "Облако создано",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                _lblStatus.ForeColor = Color.FromArgb(185, 28, 28);
                _lblStatus.Text = error;
                MessageBox.Show(error, "Ошибка создания облака", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnCopyCodeClick(object sender, EventArgs e)
        {
            CloudSyncConfig temp = new CloudSyncConfig();
            ApplyControlsToConfig(temp);
            string code = temp.ToConnectionCode();
            try
            {
                Clipboard.SetText(code);
                _lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                _lblStatus.Text = "✓ Код подключения скопирован в буфер обмена! Вставьте его на телефоне.";
            }
            catch
            {
            }
        }

        private void OnPasteCodeClick(object sender, EventArgs e)
        {
            try
            {
                string clip = Clipboard.GetText();
                CloudSyncConfig parsed = CloudSyncConfig.FromConnectionCode(clip);
                if (parsed == null)
                {
                    MessageBox.Show(
                        "В буфере обмена не найден корректный код подключения STRELO1:... или ID комнаты.",
                        "Ошибка",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                _cmbProvider.SelectedIndex = (int)parsed.ProviderType;
                _txtOneClickRoomId.Text = parsed.OneClickRoomId ?? string.Empty;
                _txtGitHubToken.Text = parsed.GitHubToken ?? string.Empty;
                _txtGistId.Text = parsed.GistId ?? string.Empty;
                _txtCustomUrl.Text = parsed.CustomCloudUrl ?? string.Empty;
                _chkLanWifiEnabled.Checked = parsed.LanWifiSyncEnabled;
                SetSyncIntervalControlsFromSeconds(parsed.AutoSyncIntervalSeconds <= 0 ? 15 : parsed.AutoSyncIntervalSeconds);
                UpdateConnectionCodeAndQrPreview();

                _lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                _lblStatus.Text = "✓ Настройки успешно загружены из буфера обмена!";
            }
            catch
            {
            }
        }

        private void OnTestSyncClick(object sender, EventArgs e)
        {
            ApplyControlsToConfig(_config);
            StorageService.SaveCloudConfig(_config);

            if (!_config.IsCloudConfigured())
            {
                if (_config.LanWifiSyncEnabled)
                {
                    _lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                    _lblStatus.Text = string.Format(
                        "✓ Локальный сервер Wi-Fi активен ({0}:{1}). Нажмите «Найти ПК в Wi-Fi» на телефоне!",
                        LanSyncService.GetPrimaryLanIp(),
                        LanSyncService.DefaultHttpPort);
                    return;
                }

                MessageBox.Show(
                    "Пожалуйста, нажмите «Создать новое Облако в 1 клик» или включите локальную синхронизацию по Wi-Fi.",
                    "Внимание",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _lblStatus.ForeColor = Color.FromArgb(30, 64, 175);
            _lblStatus.Text = "Выполняется синхронизация с облаком...";
            Application.DoEvents();

            CloudSyncResult res = CloudSyncService.Synchronize(_config, _currentReminders, true);
            if (res.Success)
            {
                SyncTriggeredSuccessfully = true;
                SyncedReminders = res.MergedReminders;
                StorageService.SaveCloudConfig(_config);
                StorageService.SaveReminders(res.MergedReminders);

                _lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                _lblStatus.Text = "✓ Успешно: " + res.Message;
            }
            else
            {
                _lblStatus.ForeColor = Color.FromArgb(185, 28, 28);
                _lblStatus.Text = res.Message;
            }
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            ApplyControlsToConfig(_config);
            StorageService.SaveCloudConfig(_config);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
