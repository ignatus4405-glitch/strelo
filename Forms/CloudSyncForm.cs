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
        private readonly CloudSyncConfig _config;
        private readonly List<ReminderItem> _currentReminders;

        private ComboBox _cmbProvider;
        private Panel _pnlGist;
        private TextBox _txtGitHubToken;
        private TextBox _txtGistId;
        private Button _btnCreateGist;

        private Panel _pnlCustomUrl;
        private TextBox _txtCustomUrl;

        private NumericUpDown _numSyncInterval;
        private TextBox _txtConnectionCode;
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
            Text = "Облачная синхронизация (ПК ↔ Android)";
            Icon = IconGenerator.GetAppIcon(false);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 510);
            BackColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);

            Panel headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(620, 56),
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(30, 58, 138)
            };

            Label lblHeader = new Label
            {
                Text = "☁ Двусторонняя синхронизация с Android через облако",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(18, 16)
            };
            headerPanel.Controls.Add(lblHeader);

            Label lblProvider = new Label
            {
                Text = "Способ облачной синхронизации:",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(18, 72),
                AutoSize = true
            };

            _cmbProvider = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                Location = new Point(250, 68),
                Size = new Size(350, 26)
            };
            _cmbProvider.Items.AddRange(new object[]
            {
                "Отключено (только локально на ПК)",
                "GitHub Gist (бесплатное облако через токен GitHub)",
                "Прямой облачный JSON URL (Firebase / npoint.io / свой сервер)"
            });
            _cmbProvider.SelectedIndexChanged += OnProviderChanged;

            // Панель настроек GitHub Gist
            _pnlGist = new Panel
            {
                Location = new Point(18, 106),
                Size = new Size(582, 148),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblToken = new Label
            {
                Text = "GitHub Personal Access Token (с правом «gist»):",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(14, 12),
                AutoSize = true
            };

            _txtGitHubToken = new TextBox
            {
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(14, 34),
                Size = new Size(552, 26),
                UseSystemPasswordChar = true
            };
            _txtGitHubToken.TextChanged += delegate { UpdateConnectionCodePreview(); };

            Label lblGistId = new Label
            {
                Text = "ID облачного Gist (оставьте пустым и нажмите «Создать облако Gist»):",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(14, 68),
                AutoSize = true
            };

            _txtGistId = new TextBox
            {
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(14, 92),
                Size = new Size(330, 26)
            };
            _txtGistId.TextChanged += delegate { UpdateConnectionCodePreview(); };

            _btnCreateGist = new Button
            {
                Text = "Создать новое облако Gist",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                Location = new Point(354, 90),
                Size = new Size(212, 30),
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
                Text = "Совет: создайте токен на GitHub (Settings -> Developer settings -> Personal access tokens -> галочка gist).",
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(14, 124),
                AutoSize = true
            };

            _pnlGist.Controls.Add(lblToken);
            _pnlGist.Controls.Add(_txtGitHubToken);
            _pnlGist.Controls.Add(lblGistId);
            _pnlGist.Controls.Add(_txtGistId);
            _pnlGist.Controls.Add(_btnCreateGist);
            _pnlGist.Controls.Add(lblGistHint);

            // Панель прямого JSON URL
            _pnlCustomUrl = new Panel
            {
                Location = new Point(18, 106),
                Size = new Size(582, 148),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };

            Label lblUrl = new Label
            {
                Text = "HTTPS URL облачного JSON-хранилища (поддерживающего GET и PUT):",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(14, 18),
                AutoSize = true
            };

            _txtCustomUrl = new TextBox
            {
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(14, 44),
                Size = new Size(552, 26)
            };
            _txtCustomUrl.TextChanged += delegate { UpdateConnectionCodePreview(); };

            Label lblUrlHint = new Label
            {
                Text = "Например: https://<ваш-проект>.firebaseio.com/strelo_reminders.json\nили любой другой облачный JSON-эндпоинт для синхронизации между ПК и телефоном.",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(14, 82),
                Size = new Size(552, 46)
            };

            _pnlCustomUrl.Controls.Add(lblUrl);
            _pnlCustomUrl.Controls.Add(_txtCustomUrl);
            _pnlCustomUrl.Controls.Add(lblUrlHint);

            // Интервал автосинхронизации
            Label lblInterval = new Label
            {
                Text = "Интервал автосинхронизации в фоне (сек.):",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Location = new Point(18, 268),
                AutoSize = true
            };

            _numSyncInterval = new NumericUpDown
            {
                Minimum = 10,
                Maximum = 3600,
                Value = 20,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(290, 264),
                Size = new Size(80, 26)
            };
            _numSyncInterval.ValueChanged += delegate { UpdateConnectionCodePreview(); };

            // Блок быстрого подключения Android-телефона по коду
            GroupBox grpPairing = new GroupBox
            {
                Text = " Быстрое подключение телефона Android (код синхронизации) ",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                BackColor = Color.White,
                Location = new Point(18, 302),
                Size = new Size(582, 114)
            };

            Label lblCodeHint = new Label
            {
                Text = "Скопируйте этот код и вставьте его в мобильном приложении Strelo на Android (или наоборот):",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                Location = new Point(14, 24),
                AutoSize = true
            };

            _txtConnectionCode = new TextBox
            {
                Font = new Font("Consolas", 9f, FontStyle.Regular),
                Location = new Point(14, 46),
                Size = new Size(552, 24),
                ReadOnly = true,
                BackColor = Color.FromArgb(248, 250, 252)
            };

            Button btnCopyCode = new Button
            {
                Text = "Скопировать код для телефона",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(14, 76),
                Size = new Size(265, 28),
                Padding = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(226, 232, 240),
                ForeColor = Color.FromArgb(30, 41, 59),
                Cursor = Cursors.Hand
            };
            btnCopyCode.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnCopyCode.Click += OnCopyCodeClick;

            Button btnPasteCode = new Button
            {
                Text = "Вставить код из буфера обмена",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(290, 76),
                Size = new Size(276, 28),
                Padding = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(226, 232, 240),
                ForeColor = Color.FromArgb(30, 41, 59),
                Cursor = Cursors.Hand
            };
            btnPasteCode.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnPasteCode.Click += OnPasteCodeClick;

            grpPairing.Controls.Add(lblCodeHint);
            grpPairing.Controls.Add(_txtConnectionCode);
            grpPairing.Controls.Add(btnCopyCode);
            grpPairing.Controls.Add(btnPasteCode);

            _lblStatus = new Label
            {
                Text = "Готово к настройке.",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 85, 105),
                Location = new Point(18, 424),
                Size = new Size(582, 22),
                AutoEllipsis = true
            };

            Button btnTestSync = new Button
            {
                Text = "Проверить и синхронизировать сейчас",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(18, 454),
                Size = new Size(280, 40),
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
                Location = new Point(310, 454),
                Size = new Size(185, 40),
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
                Location = new Point(505, 454),
                Size = new Size(95, 40),
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
            Controls.Add(_pnlGist);
            Controls.Add(_pnlCustomUrl);
            Controls.Add(lblInterval);
            Controls.Add(_numSyncInterval);
            Controls.Add(grpPairing);
            Controls.Add(_lblStatus);
            Controls.Add(btnTestSync);
            Controls.Add(btnSave);
            Controls.Add(btnClose);
        }

        private void LoadConfigToControls()
        {
            _cmbProvider.SelectedIndex = (int)_config.ProviderType;
            _txtGitHubToken.Text = _config.GitHubToken ?? string.Empty;
            _txtGistId.Text = _config.GistId ?? string.Empty;
            _txtCustomUrl.Text = _config.CustomCloudUrl ?? string.Empty;
            _numSyncInterval.Value = Math.Max(10, Math.Min(3600, _config.AutoSyncIntervalSeconds <= 0 ? 20 : _config.AutoSyncIntervalSeconds));
            OnProviderChanged(this, EventArgs.Empty);
            UpdateConnectionCodePreview();
        }

        private void ApplyControlsToConfig(CloudSyncConfig target)
        {
            target.ProviderType = (CloudProviderType)_cmbProvider.SelectedIndex;
            target.GitHubToken = _txtGitHubToken.Text.Trim();
            target.GistId = _txtGistId.Text.Trim();
            target.CustomCloudUrl = _txtCustomUrl.Text.Trim();
            target.AutoSyncIntervalSeconds = (int)_numSyncInterval.Value;
        }

        private void OnProviderChanged(object sender, EventArgs e)
        {
            CloudProviderType provider = (CloudProviderType)_cmbProvider.SelectedIndex;
            _pnlGist.Visible = (provider == CloudProviderType.GitHubGist);
            _pnlCustomUrl.Visible = (provider == CloudProviderType.CustomJsonUrl);
            UpdateConnectionCodePreview();
        }

        private void UpdateConnectionCodePreview()
        {
            if (_txtConnectionCode == null) return;
            CloudSyncConfig temp = new CloudSyncConfig();
            ApplyControlsToConfig(temp);
            _txtConnectionCode.Text = temp.IsConfigured()
                ? temp.ToConnectionCode()
                : "Заполните параметры облака выше, чтобы получить код для телефона";
        }

        private void OnCreateGistClick(object sender, EventArgs e)
        {
            string token = _txtGitHubToken.Text.Trim();
            if (string.IsNullOrEmpty(token))
            {
                MessageBox.Show(
                    "Сначала введите ваш GitHub Personal Access Token (с правом gist).",
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
                UpdateConnectionCodePreview();
                _lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                _lblStatus.Text = "Облако Gist успешно создано! ID: " + createdId;
                MessageBox.Show(
                    "Новое облачное хранилище Gist успешно создано!\n\nGist ID: " + createdId +
                    "\n\nТеперь вы можете нажать «Скопировать код для телефона» и вставить его в приложении на Android.",
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
            if (!temp.IsConfigured())
            {
                MessageBox.Show("Сначала настройте параметры облака.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string code = temp.ToConnectionCode();
            try
            {
                Clipboard.SetText(code);
                _lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                _lblStatus.Text = "Код подключения скопирован в буфер обмена! Отправьте его на телефон.";
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
                        "В буфере обмена не найден корректный код подключения STRELO1:...",
                        "Ошибка",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                _cmbProvider.SelectedIndex = (int)parsed.ProviderType;
                _txtGitHubToken.Text = parsed.GitHubToken ?? string.Empty;
                _txtGistId.Text = parsed.GistId ?? string.Empty;
                _txtCustomUrl.Text = parsed.CustomCloudUrl ?? string.Empty;
                _numSyncInterval.Value = Math.Max(10, Math.Min(3600, parsed.AutoSyncIntervalSeconds <= 0 ? 20 : parsed.AutoSyncIntervalSeconds));
                UpdateConnectionCodePreview();

                _lblStatus.ForeColor = Color.FromArgb(22, 163, 74);
                _lblStatus.Text = "Настройки облака успешно загружены из кода подключения!";
            }
            catch
            {
            }
        }

        private void OnTestSyncClick(object sender, EventArgs e)
        {
            ApplyControlsToConfig(_config);
            if (!_config.IsConfigured())
            {
                MessageBox.Show("Пожалуйста, заполните настройки выбранного облачного провайдера.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                _lblStatus.Text = "Успешно: " + res.Message;
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
