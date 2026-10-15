using System.ComponentModel;
using System.Diagnostics;

namespace PortalMonitor;

public sealed class MainForm : Form
{
    private readonly BindingList<MonitorTarget> _targets;
    private readonly TargetStore _legacyStore = new();
    private readonly ProfileStore _profileStore = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly AvailabilityChecker _checker = new();
    private readonly LogWriter _logWriter = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 * 60 * 1000 };
    private readonly DataGridView _grid = new();
    private readonly TextBox _nameInput = new();
    private readonly TextBox _addressInput = new();
    private readonly ComboBox _profileBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly TextBox _newProfileInput = new() { Width = 135, PlaceholderText = "Nazwa nowego profilu" };
    private readonly NumericUpDown _intervalInput = new() { Minimum = 1, Maximum = 1440, Width = 62 };
    private readonly NumericUpDown _portInput = new() { Minimum = 1, Maximum = 65535, Value = 80 };
    private readonly CheckBox _usePortInput = new() { Text = "Sprawdź port TCP", AutoSize = true };
    private readonly TextBox _logBox = new();
    private readonly Label _scheduleLabel = new() { AutoSize = true };
    private readonly Label _targetsSummary = new();
    private readonly Label _availableSummary = new();
    private readonly Label _issuesSummary = new();
    private readonly NotifyIcon _trayIcon = new();
    private CancellationTokenSource? _checkCancellation;
    private bool _checking;
    private bool _allowExit;
    private string _activeProfile = "Domyślny";
    private AppSettings _settings = new();

    public MainForm()
    {
        Text = "Portal Monitor";
        MinimumSize = new Size(950, 620);
        Size = new Size(1120, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 23, 42);
        ForeColor = Color.FromArgb(226, 232, 240);
        Font = new Font("Segoe UI", 9F);
        var appIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "portal-monitor.ico");
        if (File.Exists(appIconPath)) Icon = new Icon(appIconPath);

        _settings = _settingsStore.Load();
        _profileStore.EnsureDefaultProfile(_legacyStore.Load());
        _targets = new BindingList<MonitorTarget>(_profileStore.Load("Domyślny"));
        BuildLayout();
        ConfigureTray();
        RefreshProfileList("Domyślny");
        UpdateDashboard();
        SetInterval(_settings.IntervalMinutes, save: false);
        _intervalInput.ValueChanged += (_, _) => SetInterval((int)_intervalInput.Value, save: true);
        _timer.Tick += async (_, _) => await CheckAllAsync();
        _timer.Start();
        Shown += async (_, _) =>
        {
            await CheckAllAsync();
        };
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized) HideToTray(showHint: false);
        };
        FormClosing += (_, e) =>
        {
            SaveCurrentProfile();
            if (!_allowExit)
            {
                e.Cancel = true;
                HideToTray(showHint: true);
            }
            else
            {
                _checkCancellation?.Cancel();
            }
        };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(22), BackColor = BackColor };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        Controls.Add(root);

        var titlePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42) };
        var headerIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "portal-monitor.png");
        if (File.Exists(headerIconPath))
        {
            titlePanel.Controls.Add(new PictureBox
            {
                Image = Image.FromFile(headerIconPath), SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(0, 7), Size = new Size(40, 40)
            });
        }
        titlePanel.Controls.Add(new Label
        {
            Text = "PORTAL MONITOR",
            Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = Color.FromArgb(248, 250, 252),
            AutoSize = true,
            Location = new Point(50, 1)
        });
        titlePanel.Controls.Add(new Label
        {
            Text = "Monitoring dostępności portali, IP i portów TCP",
            ForeColor = Color.FromArgb(148, 163, 184),
            AutoSize = true,
            Location = new Point(52, 37)
        });
        titlePanel.Controls.Add(new Label
        {
            Text = "  ●  MONITORING AKTYWNY  ", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(850, 17), BackColor = Color.FromArgb(6, 78, 59), ForeColor = Color.FromArgb(167, 243, 208),
            Font = new Font("Segoe UI Semibold", 9F), Padding = new Padding(7, 4, 7, 4)
        });
        root.Controls.Add(titlePanel, 0, 0);

        var stats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 3, 0, 7) };
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        stats.Controls.Add(StatCard("MONITOROWANE CELE", _targetsSummary, Color.FromArgb(96, 165, 250)), 0, 0);
        stats.Controls.Add(StatCard("DOSTĘPNE", _availableSummary, Color.FromArgb(52, 211, 153)), 1, 0);
        stats.Controls.Add(StatCard("WYMAGAJĄ UWAGI", _issuesSummary, Color.FromArgb(251, 113, 133)), 2, 0);
        root.Controls.Add(stats, 0, 1);

        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 0, 0, 8) };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        var profileControls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, BackColor = Color.Transparent };
        profileControls.Controls.AddRange([
            LabelFor("Co:"), _intervalInput, LabelFor("minut"), LabelFor("Profil:"), _profileBox,
            ButtonFor("Wczytaj", (_, _) => LoadSelectedProfile()), ButtonFor("Zapisz", (_, _) => SaveCurrentProfile()),
            _newProfileInput, ButtonFor("+ Nowy", (_, _) => CreateProfile()), ButtonFor("Usuń", (_, _) => DeleteSelectedProfile(), Color.FromArgb(190, 24, 93))
        ]);
        var targetControls = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, BackColor = Color.Transparent };
        targetControls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        targetControls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        targetControls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        targetControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        targetControls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        targetControls.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        targetControls.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        var nameLabel = LabelFor("Nazwa:");
        nameLabel.Margin = Padding.Empty;
        nameLabel.Anchor = AnchorStyles.Left;
        var addressLabel = LabelFor("Adres URL / IP:");
        addressLabel.Margin = Padding.Empty;
        addressLabel.Anchor = AnchorStyles.Left;
        var addButton = ButtonFor("+ Dodaj", (_, _) => AddTarget(), Color.FromArgb(5, 150, 105));
        _nameInput.Dock = DockStyle.Fill;
        _addressInput.Dock = DockStyle.Fill;
        _portInput.Dock = DockStyle.Left;
        _portInput.Width = 70;
        addButton.Dock = DockStyle.Fill;
        targetControls.Controls.Add(nameLabel, 0, 0);
        targetControls.Controls.Add(_nameInput, 1, 0);
        targetControls.Controls.Add(addressLabel, 2, 0);
        targetControls.Controls.Add(_addressInput, 3, 0);
        targetControls.Controls.Add(addButton, 4, 0);
        targetControls.SetRowSpan(addButton, 2);
        targetControls.Controls.Add(_usePortInput, 0, 1);
        targetControls.SetColumnSpan(_usePortInput, 3);
        targetControls.Controls.Add(_portInput, 3, 1);
        _portInput.Enabled = false;
        _usePortInput.CheckedChanged += (_, _) => _portInput.Enabled = _usePortInput.Checked;
        StyleInput(_nameInput); StyleInput(_addressInput); StyleInput(_newProfileInput); StyleInput(_profileBox); StyleInput(_intervalInput); StyleInput(_portInput);
        _usePortInput.ForeColor = Color.FromArgb(203, 213, 225);
        settings.Controls.Add(Section("HARMONOGRAM I PROFILE", profileControls), 0, 0);
        settings.Controls.Add(Section("DODAJ NOWY CEL", targetControls), 1, 0);
        root.Controls.Add(settings, 0, 2);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 4, 0, 4) };
        actions.Controls.AddRange([
            ButtonFor("↻  Sprawdź teraz", async (_, _) => await CheckAllAsync(), Color.FromArgb(37, 99, 235)),
            ButtonFor("Otwórz folder logów", (_, _) => OpenLogFolder(), Color.FromArgb(71, 85, 105))
        ]);
        root.Controls.Add(actions, 0, 3);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.BackgroundColor = Color.FromArgb(30, 41, 59);
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.GridColor = Color.FromArgb(51, 65, 85);
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = 36;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(51, 65, 85), ForeColor = Color.FromArgb(241, 245, 249),
            Font = new Font("Segoe UI Semibold", 9F), Padding = new Padding(6, 5, 6, 5)
        };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.FromArgb(226, 232, 240),
            SelectionBackColor = Color.FromArgb(59, 130, 246), SelectionForeColor = Color.White, Padding = new Padding(6, 4, 6, 4)
        };
        _grid.RowTemplate.Height = 42;
        _grid.RowsDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.RowPrePaint += ColorStatusRow;
        _grid.CellContentClick += ShowTargetMenu;
        _grid.DataSource = _targets;
        AddColumn("Nazwa", nameof(MonitorTarget.DisplayName), 15, 75);
        AddColumn("Adres", nameof(MonitorTarget.Address), 19, 85);
        AddColumn("Stan", nameof(MonitorTarget.Status), 11, 82);
        AddColumn("Przyczyna / odpowiedź", nameof(MonitorTarget.Reason), 28, 145);
        AddColumn("Ostatnia kontrola", nameof(MonitorTarget.LastCheck), 18, 125);
        AddColumn("Czas", nameof(MonitorTarget.ResponseTime), 9, 60);
        _grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "targetMenu", HeaderText = "", Text = "⋮", UseColumnTextForButtonValue = true,
            Width = 42, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, FlatStyle = FlatStyle.Flat, DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(51, 65, 85), ForeColor = Color.White,
                SelectionBackColor = Color.FromArgb(71, 85, 105), SelectionForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 14F), Alignment = DataGridViewContentAlignment.MiddleCenter
            }
        });
        root.Controls.Add(_grid, 0, 4);

        _scheduleLabel.ForeColor = Color.FromArgb(148, 163, 184);
        _scheduleLabel.Padding = new Padding(4, 3, 3, 5);

        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.ReadOnly = true;
        _logBox.BackColor = Color.FromArgb(15, 23, 42);
        _logBox.ForeColor = Color.FromArgb(203, 213, 225);
        _logBox.BorderStyle = BorderStyle.FixedSingle;
        _logBox.Font = new Font("Cascadia Mono", 9);
        var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 27, 0, 0) };
        var logTitle = new Label { Text = "DZIENNIK ZDARZEŃ", Dock = DockStyle.Top, Height = 25, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("Segoe UI Semibold", 9F), Padding = new Padding(3, 4, 0, 0) };
        logPanel.Controls.Add(_logBox);
        logPanel.Controls.Add(logTitle);
        logPanel.Controls.Add(_scheduleLabel);
        _scheduleLabel.Dock = DockStyle.Top;
        logTitle.BringToFront();
        root.Controls.Add(logPanel, 0, 5);
    }

    private Label LabelFor(string text) => new() { Text = text, AutoSize = true, ForeColor = Color.FromArgb(203, 213, 225), Margin = new Padding(8, 8, 2, 0) };

    private Button ButtonFor(string text, EventHandler handler, Color? color = null)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, Margin = new Padding(8, 3, 0, 3), FlatStyle = FlatStyle.Flat,
            BackColor = color ?? Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatAppearance = { BorderSize = 0 }, Padding = new Padding(8, 3, 8, 3), Font = new Font("Segoe UI Semibold", 9F)
        };
        button.Click += handler;
        return button;
    }

    private void ConfigureTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Otwórz Portal Monitor", null, (_, _) => ShowFromTray());
        menu.Items.Add("Sprawdź teraz", null, async (_, _) => await CheckAllAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Zakończ", null, (_, _) => ExitApplication());

        var trayIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "portal-monitor.ico");
        _trayIcon.Icon = File.Exists(trayIconPath) ? new Icon(trayIconPath) : Icon ?? SystemIcons.Application;
        _trayIcon.Text = "Portal Monitor — monitoring aktywny";
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void HideToTray(bool showHint)
    {
        if (!Visible) return;
        ShowInTaskbar = false;
        Hide();
        if (showHint)
        {
            _trayIcon.BalloonTipTitle = "Portal Monitor działa w tle";
            _trayIcon.BalloonTipText = "Kliknij dwukrotnie ikonę w zasobniku, aby otworzyć okno.";
            _trayIcon.ShowBalloonTip(3000);
        }
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _allowExit = true;
        _trayIcon.Visible = false;
        Close();
    }

    private void AddColumn(string header, string property, float fillWeight, int minimumWidth) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = header, DataPropertyName = property, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = fillWeight, MinimumWidth = minimumWidth
        });

    private void ShowTargetMenu(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "targetMenu") return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is not MonitorTarget target) return;

        _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.FromArgb(241, 245, 249),
            Font = new Font("Segoe UI", 9F)
        };
        var remove = new ToolStripMenuItem("Usuń cel")
        {
            ForeColor = Color.FromArgb(251, 113, 133),
            Image = SystemIcons.Error.ToBitmap()
        };
        remove.Click += (_, _) => RemoveTarget(target);
        menu.Items.Add(remove);
        menu.Show(Cursor.Position);
    }

    private Panel Section(string title, Control content)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 41, 59), Padding = new Padding(12, 30, 10, 8), Margin = new Padding(0, 0, 8, 0) };
        panel.Controls.Add(content);
        panel.Controls.Add(new Label
        {
            Text = title, AutoSize = true, ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI Semibold", 8.5F), Location = new Point(12, 8)
        });
        return panel;
    }

    private Panel StatCard(string title, Label value, Color accent)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 41, 59), Margin = new Padding(0, 0, 8, 0), Padding = new Padding(16, 10, 10, 8) };
        card.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 4, BackColor = accent });
        value.Text = "0";
        value.Font = new Font("Segoe UI Semibold", 21F);
        value.ForeColor = Color.FromArgb(248, 250, 252);
        value.AutoSize = true;
        value.Location = new Point(23, 26);
        card.Controls.Add(value);
        card.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("Segoe UI Semibold", 8.5F), Location = new Point(23, 10) });
        return card;
    }

    private static void StyleInput(Control control)
    {
        control.BackColor = Color.FromArgb(15, 23, 42);
        control.ForeColor = Color.FromArgb(241, 245, 249);
        control.Margin = new Padding(5, 4, 3, 3);
    }

    private void ColorStatusRow(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not MonitorTarget target) return;
        var style = _grid.Rows[e.RowIndex].DefaultCellStyle;
        if (target.Status == "DOSTĘPNY")
        {
            style.BackColor = Color.FromArgb(6, 78, 59);
            style.ForeColor = Color.FromArgb(209, 250, 229);
            style.SelectionBackColor = Color.FromArgb(6, 78, 59);
            style.SelectionForeColor = Color.White;
        }
        else if (target.Status is "NIEDOSTĘPNY" or "BŁĄD HTTP" or "BŁĄD TLS" or "BŁĄD" or "BŁĘDNA KONFIGURACJA")
        {
            style.BackColor = Color.FromArgb(127, 29, 29);
            style.ForeColor = Color.FromArgb(254, 226, 226);
            style.SelectionBackColor = Color.FromArgb(127, 29, 29);
            style.SelectionForeColor = Color.White;
        }
        else
        {
            style.BackColor = Color.FromArgb(30, 41, 59);
            style.ForeColor = Color.FromArgb(226, 232, 240);
            style.SelectionBackColor = Color.FromArgb(51, 65, 85);
            style.SelectionForeColor = Color.White;
        }
    }

    private void SetInterval(int minutes, bool save)
    {
        _settings.IntervalMinutes = minutes;
        _timer.Interval = minutes * 60 * 1000;
        _intervalInput.Value = minutes;
        if (save) _settingsStore.Save(_settings);
        _scheduleLabel.Text = $"Automatyczne sprawdzanie: co {minutes} min. Następny cykl ruszy po zakończeniu bieżącego.";
    }

    private void AddTarget()
    {
        var address = _addressInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            MessageBox.Show(this, "Podaj adres portalu (np. https://example.com) albo IP/host.", "Brak adresu", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_usePortInput.Checked && Uri.TryCreate(address, UriKind.Absolute, out _))
        {
            MessageBox.Show(this, "Dla adresu URL port jest pobierany z URL. Opcję TCP stosuj dla IP lub hosta.", "Nieprawidłowa kombinacja", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _targets.Add(new MonitorTarget { Name = _nameInput.Text.Trim(), Address = address, Port = _usePortInput.Checked ? (int)_portInput.Value : null });
        SaveCurrentProfile();
        _nameInput.Clear();
        _addressInput.Clear();
        _grid.ResetBindings();
        UpdateDashboard();
    }

    private void RemoveSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not MonitorTarget target) return;
        RemoveTarget(target);
    }

    private void RemoveTarget(MonitorTarget target)
    {
        if (MessageBox.Show(this, $"Usunąć cel „{target.DisplayName}”?", "Usuwanie celu", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        _targets.Remove(target);
        SaveCurrentProfile();
        UpdateDashboard();
    }

    private async Task CheckAllAsync()
    {
        if (_checking || _targets.Count == 0) return;
        _checking = true;
        _checkCancellation = new CancellationTokenSource();
        _scheduleLabel.Text = $"Trwa sprawdzanie {DateTime.Now:HH:mm:ss}…";
        try
        {
            foreach (var target in _targets)
            {
                var result = await _checker.CheckAsync(target, _checkCancellation.Token);
                target.Status = result.Status;
                target.Reason = result.Reason;
                target.LastCheck = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                target.ResponseTime = result.Duration is null ? "" : $"{result.Duration.Value.TotalMilliseconds:F0} ms";
                AppendLog(_logWriter.Write(target, result));
                _grid.Refresh();
                UpdateDashboard();
            }
        }
        finally
        {
            _checking = false;
            _checkCancellation?.Dispose();
            _checkCancellation = null;
            _scheduleLabel.Text = $"Ostatni cykl: {DateTime.Now:yyyy-MM-dd HH:mm:ss}. Automatyczne sprawdzanie: co {_settings.IntervalMinutes} min.";
        }
    }

    private void AppendLog(string line)
    {
        _logBox.AppendText(line + Environment.NewLine);
        if (_logBox.Lines.Length > 500)
            _logBox.Lines = _logBox.Lines[^500..];
    }

    private void UpdateDashboard()
    {
        var available = _targets.Count(target => target.Status == "DOSTĘPNY");
        var issues = _targets.Count(target => target.Status is "NIEDOSTĘPNY" or "BŁĄD HTTP" or "BŁĄD TLS" or "BŁĄD" or "BŁĘDNA KONFIGURACJA");
        _targetsSummary.Text = _targets.Count.ToString();
        _availableSummary.Text = available.ToString();
        _issuesSummary.Text = issues.ToString();
    }

    private void OpenLogFolder()
    {
        Process.Start(new ProcessStartInfo { FileName = _logWriter.DirectoryPath, UseShellExecute = true });
    }

    private string? SelectedProfile => _profileBox.SelectedItem as string;

    private void SaveCurrentProfile()
    {
        _profileStore.Save(_activeProfile, _targets);
    }

    private void LoadSelectedProfile()
    {
        var profile = SelectedProfile;
        if (string.IsNullOrWhiteSpace(profile)) return;

        SaveCurrentProfile();
        ReplaceTargets(_profileStore.Load(profile));
        _activeProfile = profile;
        AppendLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | PROFIL | Wczytano profil: {profile}");
    }

    private void CreateProfile()
    {
        var profile = _newProfileInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(profile))
        {
            MessageBox.Show(this, "Podaj nazwę nowego profilu.", "Brak nazwy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (profile.Length > 60)
        {
            MessageBox.Show(this, "Nazwa profilu może mieć maksymalnie 60 znaków.", "Za długa nazwa", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SaveCurrentProfile();
        if (!_profileStore.Create(profile))
        {
            MessageBox.Show(this, "Profil o takiej nazwie już istnieje.", "Profil istnieje", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        RefreshProfileList(profile);
        ReplaceTargets([]);
        _activeProfile = profile;
        _newProfileInput.Clear();
        AppendLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | PROFIL | Utworzono profil: {profile}");
    }

    private void DeleteSelectedProfile()
    {
        var profile = SelectedProfile;
        if (string.IsNullOrWhiteSpace(profile)) return;
        if (string.Equals(profile, "Domyślny", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Profil Domyślny nie może zostać usunięty.", "Chroniony profil", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, $"Usunąć profil „{profile}”?", "Usuwanie profilu", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _profileStore.Delete(profile);
        if (string.Equals(profile, _activeProfile, StringComparison.OrdinalIgnoreCase))
        {
            _activeProfile = "Domyślny";
            RefreshProfileList(_activeProfile);
            ReplaceTargets(_profileStore.Load(_activeProfile));
        }
        else
        {
            RefreshProfileList(_activeProfile);
        }
        AppendLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | PROFIL | Usunięto profil: {profile}");
    }

    private void RefreshProfileList(string selectProfile)
    {
        _profileBox.BeginUpdate();
        _profileBox.Items.Clear();
        _profileBox.Items.AddRange(_profileStore.GetNames().Cast<object>().ToArray());
        _profileBox.SelectedItem = selectProfile;
        if (_profileBox.SelectedIndex < 0 && _profileBox.Items.Count > 0) _profileBox.SelectedIndex = 0;
        _profileBox.EndUpdate();
    }

    private void ReplaceTargets(IEnumerable<MonitorTarget> targets)
    {
        _targets.Clear();
        foreach (var target in targets) _targets.Add(target);
        _grid.Refresh();
        UpdateDashboard();
    }
}
