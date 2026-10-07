using CodexLauncher.Core;
using Microsoft.Win32;

namespace CodexLauncher.App;

public sealed class MainForm : Form
{
    private readonly TextBox _proxyInput = new()
    {
        Dock = DockStyle.Fill,
        PlaceholderText = "例如 127.0.0.1:7890；留空时只检测系统默认网络",
        BorderStyle = BorderStyle.FixedSingle,
        // ②输入文字用主题正文色：默认继承的黑字在深色主题下不可读。
        ForeColor = UiTheme.Text,
        Margin = new Padding(0, 4, 0, 4)
    };
    private readonly Button _checkButton = UiTheme.SecondaryButton("重新检测");
    private readonly Button _launchButton = UiTheme.PrimaryButton("启动 Codex");
    private readonly Button _closeButton = UiTheme.SecondaryButton(CodexCloseButtonPolicy.Label);
    private readonly Button _gatewayStartButton = UiTheme.SecondaryButton("启动 / 使用固定入口");
    private readonly Button _partyButton = UiTheme.SecondaryButton("Party");
    private readonly Button _vergeButton = UiTheme.SecondaryButton("Verge");
    private readonly Button _gatewayStopButton = UiTheme.SecondaryButton("停止固定入口");
    private readonly Button _chooseMihomoButton = UiTheme.SecondaryButton("选择 Mihomo 程序");
    private readonly Button _advancedToggle = new()
    {
        Text = "高级设置  ›",
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        ForeColor = UiTheme.Primary,
        BackColor = Color.White,
        Cursor = Cursors.Hand
    };
    private readonly Button _diagnosticsToggle = new()
    {
        Text = "查看详细诊断  ›",
        Dock = DockStyle.Top,
        Height = 46,
        TextAlign = ContentAlignment.MiddleLeft,
        FlatStyle = FlatStyle.Flat,
        ForeColor = UiTheme.Text,
        BackColor = Color.White,
        Cursor = Cursors.Hand
    };
    private readonly Label _summary = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        ForeColor = UiTheme.Muted,
        Text = "正在检查本机环境…",
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = Padding.Empty
    };
    private readonly Label _statusTitle = new()
    {
        AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold),
        ForeColor = UiTheme.Text,
        Margin = new Padding(0, 0, 0, 4)
    };
    private readonly Label _statusDetail = new()
    {
        AutoSize = true,
        MaximumSize = new Size(560, 0),
        ForeColor = UiTheme.Muted,
        Margin = new Padding(0, 2, 0, 2)
    };
    private readonly Label _statusMeta = new()
    {
        AutoSize = true,
        MaximumSize = new Size(UiTheme.StatusMetaMaxWidth, 0),
        ForeColor = UiTheme.Muted,
        TextAlign = ContentAlignment.MiddleRight,
        Margin = new Padding(0, 6, 0, 0)
    };
    private readonly Label _statusBadge = new()
    {
        AutoSize = true,
        Padding = new Padding(10, 5, 10, 5),
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
    };
    private readonly Label _quotaTitle = new()
    {
        AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
        ForeColor = UiTheme.Text,
        Margin = new Padding(0, 0, 0, 3)
    };
    private readonly Label _quotaBadge = new()
    {
        AutoSize = true,
        Padding = new Padding(10, 4, 10, 4),
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
    };
    private readonly Label _quotaDetail = new()
    {
        AutoSize = true,
        MaximumSize = new Size(620, 0),
        ForeColor = UiTheme.Muted,
        Margin = new Padding(0, 0, 0, 6)
    };
    private readonly Label _quotaAccount = new()
    {
        AutoSize = true,
        MaximumSize = new Size(620, 0),
        ForeColor = UiTheme.Muted,
        Margin = new Padding(0, 0, 0, 6)
    };
    private readonly TableLayoutPanel _quotaBars = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1,
        BackColor = UiTheme.Card,
        Margin = new Padding(0, 0, 0, 6)
    };
    private readonly Label _quotaFreshness = new()
    {
        AutoSize = true,
        MaximumSize = new Size(620, 0),
        ForeColor = UiTheme.Yellow,
        Visible = false,
        Margin = new Padding(0, 0, 0, 6)
    };
    private readonly Button _quotaRefreshButton = UiTheme.SecondaryButton("刷新额度");
    private readonly Label _processNotice = new()
    {
        AutoSize = true,
        ForeColor = UiTheme.Yellow,
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
        Visible = false,
        Margin = new Padding(0, 4, 0, 0)
    };
    private readonly StatusDot _statusDot = new();
    private readonly PictureBox _logo = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 10, 12, 10) };
    // 必须是 WrapPanel：普通 FlowLayoutPanel 在无宽度约束下按"不换行"报高度，
    // 展开后母卡片只长一半，底部自启勾选框会被裁掉（真机 150% DPI 实测裁 31px）。
    private readonly UiTheme.WrapPanel _advancedPanel = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        WrapContents = true,
        Visible = false,
        BackColor = Color.White,
        Margin = new Padding(0, 4, 0, 0)
    };
    private readonly ListView _results = new()
    {
        Dock = DockStyle.Top,
        Height = 235,
        View = View.Details,
        FullRowSelect = true,
        GridLines = false,
        BorderStyle = BorderStyle.None,
        BackColor = UiTheme.Card,
        ForeColor = UiTheme.Text,
        OwnerDraw = true,
        // 所有诊断行交给页面统一滚动，避免原生 ListView 的浅色滚动条。
        Scrollable = false,
        Visible = false
    };
    private readonly Dictionary<string, ListViewItem> _rows = new();
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 12000, InitialDelay = 350, ReshowDelay = 100 };
    private readonly CancellationTokenSource _closing = new();
    private readonly SemaphoreSlim _portSyncLock = new(1, 1);
    private readonly AsyncOperationGate _operationGate = new();
    private readonly ICommandRunner _commands = new ProcessCommandRunner();
    private readonly LauncherSettingsStore _settings;
    private LauncherSettings? _lastSavedSettings;
    private readonly AppLauncher _launcher;
    private readonly LaunchRouteTracker _launchRouteTracker = new();
    private readonly DiagnosticLogger _logger;
    private readonly HttpClient _gatewayHttp = new(new SocketsHttpHandler { UseProxy = false });
    private readonly UpstreamPortDiscovery _portDiscovery = UpstreamPortDiscovery.ForCurrentUser();
    private readonly TunModeDiscovery _tunDiscovery = TunModeDiscovery.ForCurrentUser();
    private readonly System.Windows.Forms.Timer _portTimer = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer _healthTimer = new() { Interval = 30000 };
    private readonly System.Windows.Forms.Timer _proxyDebounceTimer = new() { Interval = 700 };
    private readonly GatewayCoordinator _gateway;
    private readonly GatewayProcessHost _gatewayHost;
    private readonly CodexProcessDetector _detector;
    private GatewayPorts _gatewayPorts = GatewayPorts.Default;
    private string _mihomoPath = @"D:\Software\Mihomo Party\Clash Party\resources\sidecar\mihomo.exe";
    private CodexInstallation? _installation;
    private DiagnosisResult? _diagnosis;
    private TunModeState _currentTun = new(null, null);
    private GatewayUpstream? _activeGateway;
    private AccessHealthSnapshot? _health;
    private bool _refreshingPorts;
    private bool _tunReadUnavailable;
    private bool _tunUnavailableLogged;
    private bool _suppressProxyChange;
    private bool _codexRunning;
    private bool _settingsWritable = true;
    private bool _bridgeEnabled;
    private int? _bridgePort;
    private readonly BridgePanel _bridgePanel=new(){Dock=DockStyle.Top};
    private BridgeRuntime? _bridge;
    private readonly bool _background;
    private bool _allowVisible,_desktopInitialized;
    private Task? _coreInitialization;
    private bool _shutdownStarted,_shutdownCompleted;
    private bool _activationListening;
    private bool _desktopSettingsLoaded;
    private readonly LauncherServices? _services;
    internal Task? CoreInitialization=>_coreInitialization;
    internal BridgeRuntime? Bridge=>_bridge;
    private CodexDesktopDiscovery? _desktop;
    private CodexCloseWorkflow? _closeWorkflow;

    private readonly ThemeManager _theme;
    private readonly Icon? _codexIcon;
    private readonly Icon _launcherIcon;
    private readonly NotifyIcon _notifyIcon = new();
    // 主题与自启各有两个入口（托盘菜单 + 高级设置）。两个入口都必须落在同一份状态上：
    // 托盘只负责"报出用户的选择"和"画出真实状态"，写入与回填统一走 SetThemeMode / ApplyAutoStart，
    // 因此托盘里改了主题，高级设置里的选择器也会跟着亮。
    private readonly TrayMenu _tray;
    private readonly ThemeSelector _themeSelector = new();
    private readonly CheckBox _autoStartCheck = new()
    {
        Text = "开机自动启动",
        AutoSize = true,
        BackColor = UiTheme.Card,
        ForeColor = UiTheme.Text,
        Margin = new Padding(0, 10, 16, 0)
    };
    private readonly AutoStartRegistration _autoStart = new(new RegistryAutoStartRegistry());
    private readonly System.Windows.Forms.Timer _activationTimer = new() { Interval = 400 };
    private readonly System.Windows.Forms.Timer _diagnosticTimer = new() { Interval = 5000 };
    private readonly CodexDiagnosticSource _diagnosticSource = new();
    private readonly SingleInstanceGate? _singleInstance;
    private RuntimeMonitor? _monitor;
    private RuntimeMonitorContext _monitorContext = new()
    {
        CodexRunning = false,
        Route = null,
        Fingerprint = "",
        RouteLabel = "待检测",
        RouteConfirmed = false
    };
    private FloatingStatusForm? _floating;
    private IReadOnlyList<NetworkDiagnosticEvent> _diagnosticEvents = Array.Empty<NetworkDiagnosticEvent>();
    private DiagnosticCursor _diagnosticCursor = DiagnosticCursor.Empty;
    private bool _diagnosticAvailable;
    private bool _readingDiagnostics;
    private bool _exitRequested;
    private bool _trayHintShown;
    private bool _syncingAutoStart;
    private bool _floatingEnabled;
    private int? _floatingLeft;
    private int? _floatingTop;
    private bool _quotaEnabled = true;
    private SharedQuotaCoordinator? _quota;
    private QuotaSnapshot? _quotaSnapshot;
    private bool _readingQuota;

    public MainForm(SingleInstanceGate? singleInstance = null,bool background=false)
        :this(singleInstance,background,null){}
    internal MainForm(SingleInstanceGate? singleInstance,bool background,LauncherServices? services)
    {
        _services=services;if(services is not null)_autoStart=new(services.AutoStartRegistry);
        _background=background;_allowVisible=!background;
        if(background)ShowInTaskbar=false;
        _singleInstance = singleInstance;
        _tray = new TrayMenu(RestoreFromTray, RequestRefresh, RequestExit);
        _theme = new ThemeManager(ThemeMode.System);
        Text = "Codex 启动器";
        _codexIcon = CodexLogo.CreateIcon();
        _launcherIcon = _codexIcon ?? SystemIcons.Application;
        Icon = _launcherIcon;
        Disposed += (_, _) => _codexIcon?.Dispose();
        MinimumSize = new Size(860, 660);
        Size = new Size(1020, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Window;

        var dataDir = services?.DataDirectory??LauncherDataPaths.DataDirectory;
        _settings = new LauncherSettingsStore(Path.Combine(dataDir, "settings.json"));
        var detector = new CodexProcessDetector();
        _detector = detector;
        _launcher = new AppLauncher(new RegistryEnvironmentStore(), Path.Combine(dataDir, "proxy-recovery.json"), detector, new WindowsAppActivator(detector));
        _logger = new DiagnosticLogger(Path.Combine(dataDir, "diagnostics.log"));
        _gatewayHost = new GatewayProcessHost(Path.Combine(dataDir, "gateway"));
        _gateway = new GatewayCoordinator(new GatewayController(_gatewayHttp, new Uri($"http://127.0.0.1:{_gatewayPorts.Controller}/")),
            _gatewayHost, new GatewaySystemProxyGuard(), _gatewayPorts);

        BuildInterface();
        BuildTray();
        WireEvents();
        _theme.StartListening();
        ThemeManager.ApplyTo(this, _theme.Palette, _theme.UseSystemColors);
        RenderHealth(new AccessHealthSnapshot(AccessHealthState.Checking, "正在检测", "正在读取 Codex、代理和网络状态。",
            null, "待检测", null, null, DateTimeOffset.Now));
        RenderRuntime(new RuntimeHealthSnapshot(RuntimeHealthState.Checking, "正在检测", "波动", "正在对当前线路取样。",
            "", RuntimeEvidenceSource.LineOnly, "仅依据线路检测", false, "待检测", 0, 0, null, null, null, false, DateTimeOffset.Now));
        RenderQuota(QuotaSnapshot.Unavailable("额度查询尚未开始。", DateTimeOffset.Now, null));
    }

    private void BuildTray()
    {
        _notifyIcon.Text = "Codex 启动器";
        _notifyIcon.Icon = _launcherIcon;
        _notifyIcon.ContextMenuStrip = _tray.Menu;
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void BuildInterface()
    {
        // ①悬停克制：两个折叠开关不再挂主题外悬停底色（FlatStyle 自带 1px 细边即有反馈），
        // 避免切主题后残留冻结的浅色 hover 背景形成"白底白字"。
        _advancedToggle.FlatAppearance.BorderSize = 0;
        _diagnosticsToggle.FlatAppearance.BorderSize = 0;
        _partyButton.Dock = DockStyle.Fill;
        _partyButton.AutoSize = false;
        _partyButton.Height = 58;
        _partyButton.TextAlign = ContentAlignment.MiddleLeft;
        _vergeButton.Dock = DockStyle.Fill;
        _vergeButton.AutoSize = false;
        _vergeButton.Height = 58;
        _vergeButton.TextAlign = ContentAlignment.MiddleLeft;

        _results.Columns.Add("项目", 230);
        _results.Columns.Add("结果", 500);
        _results.Columns.Add("耗时", 90);
        // 表头和行都自绘，否则 ListView 的系统绘制会在深色主题下留下浅色单元格。
        _results.DrawColumnHeader += DrawResultsHeader;
        _results.DrawSubItem += DrawResultsSubItem;
        _results.HandleCreated += (_, _) => ResizeResultsHeight();
        _results.FontChanged += (_, _) => ResizeResultsHeight();
        _results.DpiChangedAfterParent += (_, _) => ResizeResultsHeight();
        _results.VisibleChanged += (_, _) =>
        {
            ResizeResultColumns();
            ResizeResultsHeight();
        };

        var scroll = new ScrollHost { Dock = DockStyle.Fill, BackColor = UiTheme.Window };
        var root = new TableLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(24, 12, 24, 24),
            BackColor = UiTheme.Window
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < root.RowCount; row++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildStatusCard(), 0, 1);
        root.Controls.Add(BuildNetworkCard(), 0, 2);
        root.Controls.Add(BuildQuotaCard(), 0, 3);
        root.Controls.Add(_bridgePanel, 0, 4);
        root.Controls.Add(BuildDiagnosticsCard(), 0, 5);
        void ResizeRoot()
        {
            // 右侧给自绘指示条留出位置：以前写死 -4，指示条会被卡片压住。
            var reserved = ScrollHost.IndicatorWidth(DeviceDpi) + ScrollHost.IndicatorMargin(DeviceDpi);
            var width = Math.Max(1, scroll.ClientSize.Width - reserved);
            root.MaximumSize = new Size(width, 0);
            root.Width = width;
        }
        scroll.SizeChanged += (_, _) => ResizeRoot();
        ResizeRoot();
        scroll.SetContent(root);
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.Window
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.Controls.Add(scroll, 0, 0);
        shell.Controls.Add(BuildActionCard(), 0, 1);
        Controls.Add(shell);

        _toolTip.SetToolTip(_partyButton, "把固定入口切换到 Party 当前自动发现的 mixed 端口，切换后会立即重新检测。");
        _toolTip.SetToolTip(_vergeButton, "把固定入口切换到 Verge 当前自动发现的 mixed 端口，切换后会立即重新检测。");
        _toolTip.SetToolTip(_proxyInput, "这里填写本机 HTTP 或 mixed 代理入口，例如 127.0.0.1:7890。");
    }

    private void DrawResultsHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        // ⑤列头配色实时读 UiTheme：主题切换后下一帧自动换色，不烤死在构造期。
        DiagnosticsHeaderPainter.PaintColumn(
            e.Graphics, e.Bounds, e.Header?.Text,
            UiTheme.Muted, UiTheme.Card, UiTheme.Border, _results.Font);
    }

    private void DrawResultsSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.Item is null || e.SubItem is null) return;
        var selected = e.Item.Selected;
        var background = selected ? UiTheme.PrimarySurface : UiTheme.Card;
        var foreground = selected ? UiTheme.AccentText : UiTheme.Text;
        var bounds = e.Bounds;
        if (e.ColumnIndex == _results.Columns.Count - 1)
            bounds.Width = Math.Max(bounds.Right, _results.ClientRectangle.Right) - bounds.Left;

        using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, bounds);
        var inset = Math.Max(6, (int)Math.Ceiling(8 * _results.DeviceDpi / 96d));
        var textBounds = Rectangle.FromLTRB(
            e.Bounds.Left + inset,
            e.Bounds.Top,
            Math.Max(e.Bounds.Left + inset, e.Bounds.Right - inset),
            e.Bounds.Bottom);
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, _results.Font, textBounds, foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    /// <summary>注册表是自启状态的唯一真相：用户可能从任务管理器禁用，界面必须跟着变。</summary>
    private void SyncAutoStart()
    {
        _syncingAutoStart = true;
        try
        {
            var enabled = _autoStart.IsEnabled(Application.ExecutablePath);
            if (_autoStartCheck.Checked != enabled) _autoStartCheck.Checked = enabled;
            // 两个入口同步：托盘里的勾同样只是注册表真值的回显。
            _tray.SyncAutoStart(enabled);
        }
        finally
        {
            _syncingAutoStart = false;
        }
    }

    private void ApplyAutoStart(bool enabled)
    {
        var result = _autoStart.TryApply(enabled, Application.ExecutablePath);
        if (!result.Succeeded)
        {
            _logger.Write("autostart-failed", result.ErrorType ?? "unknown", null);
            MessageBox.Show(this,
                "开机自启写入注册表失败：" + result.ErrorType + "。可能是权限或安全软件拦截，其余功能不受影响。",
                "开机自启", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // 无论成功与否都按注册表的真实值回填，避免界面显示一个并不存在的状态。
        SyncAutoStart();
        SavePreferences(SafeReadProxy()?.ToString());
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 88,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 12),
            BackColor = UiTheme.Window
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var titles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = UiTheme.Window };
        titles.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        titles.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        titles.Controls.Add(new Label
        {
            Text = "Codex 启动器",
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 19F, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        titles.Controls.Add(new Label
        {
            Text = "第三方网络启动助手 · 自动识别 Party / Verge 端口",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.TopLeft
        }, 0, 1);
        header.Controls.Add(_logo, 0, 0);
        header.Controls.Add(titles, 1, 0);
        // ⑦Codex 云朵徽标：页头、窗口与托盘都使用同一份嵌入品牌素材；资源缺失时不绘制替代字样。
        _logo.Image = CodexLogo.Render(192);
        return header;
    }

    private Control BuildStatusCard()
    {
        // 版式抽到 StatusCardLayout：徽章靠右、元信息不折行、卡片高度由内容推出。
        var card = StatusCardLayout.Build(_statusDot, _statusTitle, _statusDetail, _processNotice, _statusBadge, _statusMeta);
        card.Dock = DockStyle.Fill;
        return card;
    }

    private Control BuildNetworkCard()
    {
        var card = new RoundedPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 14) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(22, 18, 22, 18), BackColor = Color.White };
        layout.Controls.Add(new Label { Text = "网络路径", AutoSize = true, Font = new Font(Font.FontFamily, 12F, FontStyle.Bold), ForeColor = UiTheme.Text, Margin = new Padding(0, 0, 0, 3) });
        layout.Controls.Add(new Label { Text = "Party / Verge 端口会自动同步。", AutoSize = true, ForeColor = UiTheme.Muted, Margin = new Padding(0, 0, 0, 12) });
        var routes = new TableLayoutPanel { Dock = DockStyle.Top, Height = 62, ColumnCount = 2, Margin = new Padding(0, 0, 0, 12) };
        routes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        routes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _partyButton.Margin = new Padding(0, 0, 6, 0);
        _vergeButton.Margin = new Padding(6, 0, 0, 0);
        routes.Controls.Add(_partyButton, 0, 0);
        routes.Controls.Add(_vergeButton, 1, 0);
        layout.Controls.Add(routes);
        // 行高由输入框自身高度推出：写死 40 会在 100% DPI 下重新错开（那时输入框只有约 22px 高）。
        // 以前标签漏了 AutoSize = false，WinForms 在 AutoSize 为真时忽略 TextAlign，
        // 文字贴着标签顶部画，150% DPI 实测比右侧输入框高 5.7px。
        var proxyRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = UiTheme.RowHeight(_proxyInput.PreferredHeight),
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 6)
        };
        // 标签列按文字实际宽度留位置：写死 155 在 150% DPI 下会把「当前本地 HTTP 入口」
        // 裁掉 18px（实测需要 173px），写死更大的值又会在低 DPI 下空出一块。
        proxyRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        proxyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        proxyRow.Controls.Add(UiTheme.RowLabel("当前本地 HTTP 入口"), 0, 0);
        proxyRow.Controls.Add(_proxyInput, 1, 0);
        layout.Controls.Add(proxyRow);
        layout.Controls.Add(_advancedToggle);
        _advancedPanel.Controls.AddRange([_gatewayStartButton, _gatewayStopButton, _chooseMihomoButton]);
        _advancedPanel.Controls.Add(_themeSelector);
        _advancedPanel.Controls.Add(_autoStartCheck);
        layout.Controls.Add(_advancedPanel);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildQuotaCard()
    {
        var card = new RoundedPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 14) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(22, 18, 22, 18), BackColor = Color.White };
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 4) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, BackColor = Color.White };
        heading.Controls.Add(_quotaTitle);
        heading.Controls.Add(_quotaBadge);
        header.Controls.Add(heading, 0, 0);
        header.Controls.Add(_quotaRefreshButton, 1, 0);
        layout.Controls.Add(header);
        layout.Controls.Add(_quotaDetail);
        layout.Controls.Add(_quotaBars);
        layout.Controls.Add(_quotaAccount);
        layout.Controls.Add(_quotaFreshness);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildDiagnosticsCard()
    {
        var card = new RoundedPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 14) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(16, 4, 16, 10), BackColor = Color.White };
        layout.Controls.Add(_diagnosticsToggle);
        layout.Controls.Add(_results);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildActionCard()
    {
        var card = new RoundedPanel { Dock = DockStyle.Fill, Height = 126, MinimumSize = new Size(0, 126), Margin = new Padding(24, 0, 24, 16) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(22, 10, 22, 10), BackColor = Color.White };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Right, BackColor = Color.White };
        actions.Controls.AddRange([_checkButton, _launchButton, _closeButton]);
        layout.Controls.Add(_summary, 0, 0);
        layout.Controls.Add(actions, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private void WireEvents()
    {
        _checkButton.Click += async (_, _) => await GuardedAsync(CheckAsync);
        _launchButton.Click += async (_, _) => await GuardedAsync(LaunchAsync);
        // 关闭流程不经过 AsyncOperationGate：后台采样持锁时也必须能关闭。
        _closeButton.Click += async (_, _) => await CloseCodexAsync();
        _gatewayStartButton.Click += async (_, _) => await GuardedAsync(StartGatewayAsync);
        _partyButton.Click += async (_, _) => await GuardedAsync(() => SwitchGatewayAsync(GatewayUpstream.Party));
        _vergeButton.Click += async (_, _) => await GuardedAsync(() => SwitchGatewayAsync(GatewayUpstream.Verge));
        _gatewayStopButton.Click += async (_, _) => await GuardedAsync(StopGatewayAsync);
        _chooseMihomoButton.Click += (_, _) => ChooseMihomo();
        _advancedToggle.Click += (_, _) =>
        {
            _advancedPanel.Visible = !_advancedPanel.Visible;
            _advancedToggle.Text = _advancedPanel.Visible ? "高级设置  ⌄" : "高级设置  ›";
            // 只改 Visible 不会重排：面板是 FlowLayoutPanel 且母卡片按内容算高，
            // 不重排就会出现"展开后底部被裁掉"（真机 150% DPI 下裁掉 48px）。
            RelayoutFrom(_advancedPanel);
        };
        _diagnosticsToggle.Click += (_, _) =>
        {
            _results.Visible = !_results.Visible;
            _diagnosticsToggle.Text = _results.Visible ? "收起详细诊断  ⌄" : "查看详细诊断  ›";
            RelayoutFrom(_results);
        };
        _themeSelector.ModeChanged += SetThemeMode;
        // 托盘是主题与自启的第二个入口：它只报出选择，写入仍旧走 SetThemeMode / ApplyAutoStart，
        // 因此两个入口共享同一条写路径和同一份回填。
        _tray.ThemeRequested += SetThemeMode;
        _tray.AutoStartRequested += ApplyAutoStart;
        _tray.FloatingRequested += () => SetFloatingWindow(!_floatingEnabled);
        _autoStartCheck.CheckedChanged += (_, _) =>
        {
            // 程序化回显不能触发写注册表，否则读状态会变成写状态。
            if (_syncingAutoStart) return;
            ApplyAutoStart(_autoStartCheck.Checked);
        };
        _results.Resize += (_, _) => ResizeResultColumns();
        _portTimer.Tick += async (_, _) => await OnPortTimerAsync();
        _proxyDebounceTimer.Tick += async (_, _) =>
        {
            _proxyDebounceTimer.Stop();
            if (_installation is not null) await GuardedAsync(CheckAsync);
        };
        _healthTimer.Tick += async (_, _) =>
        {
            // 最小化或隐藏到托盘后继续后台监测：不再按窗口状态停表。
            if (_installation is not null) await GuardedAsync(CheckAsync);
        };
        _diagnosticTimer.Tick += async (_, _) => await ReadDiagnosticsAsync();
        _quotaRefreshButton.Click += async (_, _) => await GuardedAsync(RefreshQuotaAsync);
        // 系统主题或高对比度变化时立即重绘整窗，不需要重启。
        _theme.Changed += () =>
        {
            ThemeManager.ApplyTo(this, _theme.Palette, _theme.UseSystemColors);
            _floating?.ApplyTheme();
            ApplyWindowChrome();
        };
        _activationTimer.Tick += (_, _) =>
        {
            _activationTimer.Stop();
            RestoreFromTray();
        };
        _proxyInput.TextChanged += (_, _) =>
        {
            if (_suppressProxyChange) return;
            _diagnosis = null;
            SetRow("推荐启动路径", "代理设置已变化，等待重新检测", null);
            ShowChecking("代理设置已变化，点击“重新检测”或等待自动刷新。");
            _proxyDebounceTimer.Stop();
            _proxyDebounceTimer.Start();
        };
        Resize += (_, _) => HandleWindowResize();
        Load += async (_, _) => {if(!_background)await GuardedAsync(InitializeAsync);};
        HandleCreated += (_,_)=>
        {
            if (!_activationListening)
            {
                _activationListening = true;
                ListenForActivation(_closing.Token);
            }
            if(_background&&_coreInitialization is null)BeginInvoke(async()=>{try{await InitializeCoreAsync();}catch(Exception e){_logger.Write("bridge-serve","background-init-"+e.GetType().Name,null);}});
        };
        _bridgePanel.ConfigureRequested+=ApplyBridgeAsync;
        // 系统注销或关机时按显式退出处理，先释放自有资源，不动 Codex 与固定入口。
        SystemEvents.SessionEnding += OnSessionEnding;
        // 屏幕数量、分辨率或缩放变化后重新约束悬浮窗位置。
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        FormClosing += async (_, e) =>
        {
            // 点击关闭按钮默认隐藏到托盘继续监测；只有显式退出或系统关机才真正结束。
            if (!_exitRequested && _closing.IsCancellationRequested == false)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            _portTimer.Stop();
            _healthTimer.Stop();
            _proxyDebounceTimer.Stop();
            _diagnosticTimer.Stop();
            _activationTimer.Stop();
            _closing.Cancel();
            if(!_shutdownCompleted)
            {
                e.Cancel=true;if(_shutdownStarted)return;_shutdownStarted=true;
                try{if(_coreInitialization is not null)await _coreInitialization;}
                catch(Exception ex){_logger.Write("bridge-serve","init-exit-"+ex.GetType().Name,null);}
                try{if(_bridge is not null)await _bridge.DisposeAsync();}
                catch(Exception ex){_logger.Write("bridge-serve","shutdown-"+ex.GetType().Name,null);}
                finally{if(_quota is not null)await _quota.DisposeAsync();}
                _shutdownCompleted=true;Close();
            }
        };
        FormClosed += (_, _) =>
        {
            _portTimer.Dispose();
            _healthTimer.Dispose();
            _proxyDebounceTimer.Dispose();
            _diagnosticTimer.Dispose();
            _activationTimer.Dispose();
            _bridgePanel.ClearInvitation();
            _gatewayHttp.Dispose();
            _logo.Image?.Dispose();
            _toolTip.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _tray.Dispose();
            _floating?.Dispose();
            _monitor?.Dispose();
            // 额度查询进程是启动器自有的：退出时必须释放，不留下孤儿子进程。
            _theme.Dispose();
            SystemEvents.SessionEnding -= OnSessionEnding;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _singleInstance?.Dispose();
        };

        // 句柄创建后才接收重复启动信号；命名事件会保留提前到达的信号。
    }

    protected override void SetVisibleCore(bool value)
    {
        if(value&&_background&&!_allowVisible){if(!IsHandleCreated)CreateHandle();value=false;}
        base.SetVisibleCore(value);
    }
    private Task InitializeCoreAsync()
    {
        if (_coreInitialization is { IsFaulted: false, IsCanceled: false } current) return current;
        return _coreInitialization = InitializeCoreOnceAsync();
    }
    private async Task InitializeCoreOnceAsync()
    {
        var load = _settings.TryLoad();
        if (!load.Succeeded) throw new IOException("启动器设置暂时不可读；原文件已保留，请重试。", load.Error);
        if (load.Status == SettingsLoadStatus.Corrupt) _logger.Write("settings-load", "corrupt-defaults", null);
        ApplyPreferences(load.Settings);StartQuotaProvider();
        _bridgePanel.SetConfiguration(_bridgeEnabled,_bridgePort??43189,null);
        if(_bridgeEnabled)
        {
            EnsureBridge();_bridgePanel.SetConfiguration(true,_bridgePort??43189,_bridge!.SelectedAdapter);
            await _bridge.StartAsync(_bridgePort??43189);_bridgePanel.RefreshState();
        }
    }
    private void EnsureBridge()
    {
        if(_bridge is not null)return;
        if(_quota is null)throw new InvalidOperationException("QUOTA_OWNER_REQUIRED");
        _bridge=_services?.BridgeFactory(_quota)??new BridgeRuntime(LauncherDataPaths.BridgeDirectory,_quota,logger:_logger);
        _bridgePanel.Bind(_bridge);
    }
    private async Task ApplyBridgeAsync(bool enabled,string? adapter,int port)
    {
        await InitializeCoreAsync();
        if(_closing.IsCancellationRequested)return;
        EnsureBridge();
        if(enabled&&adapter is null)throw new InvalidOperationException("请明确选择一张物理 Private IPv4 网卡。");
        if(adapter is not null)_bridge!.SelectAdapter(adapter);
        _bridgeEnabled=enabled;_bridgePort=port;SavePreferences(_proxyInput.Text);
        if(enabled)await _bridge!.StartAsync(port);else await _bridge!.StopAsync();
        _bridgePanel.SetConfiguration(enabled,port,_bridge!.SelectedAdapter);_bridgePanel.RefreshState();
    }
    private async Task ApplyBackgroundSignalAsync()
    {
        try
        {
            await InitializeCoreAsync();if(_closing.IsCancellationRequested)return;EnsureBridge();
            if(_bridge!.SelectedAdapter is null){_logger.Write("bridge-serve","selected-adapter-required",null);return;}
            _bridgeEnabled=true;SavePreferences(_proxyInput.Text);await _bridge.StartAsync(_bridgePort??43189);
            _bridgePanel.SetConfiguration(true,_bridgePort??43189,_bridge.SelectedAdapter);_bridgePanel.RefreshState();
        }
        catch(Exception e){_logger.Write("bridge-serve","background-signal-"+e.GetType().Name,null);}
    }
    private async Task InitializeAsync()
    {
        await InitializeCoreAsync();
        if(_desktopInitialized)return;
        _desktopInitialized=true;
        _launcher.Recover();
        var settings = _settings.Load();
        _mihomoPath = settings.MihomoPath ?? _mihomoPath;
        ApplyPreferences(settings);
        if (settings.PartyPort is > 0 and <= 65535 && settings.VergePort is > 0 and <= 65535)
        {
            var savedPorts = _gatewayPorts with { Party = settings.PartyPort.Value, Verge = settings.VergePort.Value };
            try { GatewayConfig.Build(savedPorts); _gatewayPorts = savedPorts; }
            catch (ArgumentException) { }
        }
        await RefreshUpstreamPortsAsync(force: true);
        var savedProxy = settings.ProxyUrl ?? DetectLocalProxy()?.ToString();
        GatewaySnapshot? gateway = null;
        try { gateway = await _gateway.StatusAsync(_closing.Token); }
        catch (Exception ex) when (ex is InvalidDataException or TimeoutException)
        {
            SetRow("固定入口", "状态未知：" + ex.Message, null);
        }
        if (gateway is not null)
        {
            SetGatewayRow(gateway);
            if (savedProxy is null || IsKnownUpstreamProxy(savedProxy)) savedProxy = _gatewayPorts.EntryAddress.ToString();
        }
        else SetRow("固定入口", "未运行；可在高级设置中启动", null);

        SetProxyText(savedProxy ?? "");
        _desktopSettingsLoaded = true;
        SavePreferences(savedProxy);
        _installation = await new CodexAppLocator(_commands).FindAsync(_closing.Token);
        SetRow("Codex 安装", _installation is null ? "未找到 OpenAI.Codex MSIX 安装包" : "已找到 Windows 桌面版", null);
        RefreshDesktop();
        SetProcessRow();
        UpdateRouteButtons();
        if (_installation is not null) await CheckAsync();
        else UpdateHealth();
        _portTimer.Start();
        _healthTimer.Start();
        StartRuntimeMonitor();
        _diagnosticTimer.Start();
        StartQuotaProvider();
        await ReadDiagnosticsAsync();
    }

    /// <summary>读取并应用主题、悬浮窗、额度与自启四项偏好；缺失字段使用计划默认值。</summary>
    private void ApplyPreferences(LauncherSettings settings)
    {
        _bridgeEnabled = settings.BridgeEnabled;
        _bridgePort = settings.BridgePort;
        _theme.SetMode(settings.Theme);
        _floatingEnabled = settings.FloatingWindowEnabled;
        _floatingLeft = settings.FloatingLeft;
        _floatingTop = settings.FloatingTop;
        _quotaEnabled = settings.QuotaMonitoringEnabled;
        ThemeManager.ApplyTo(this, _theme.Palette, _theme.UseSystemColors);
        ApplyWindowChrome();
        SyncThemeMenu();
        _tray.SyncFloating(_floatingEnabled);
        if (_floatingEnabled&&(!_background||_allowVisible)) ShowFloatingWindow();

        // 自启状态以注册表为准，不看配置里的 AutoStartEnabled：
        // 用户可能从任务管理器禁用启动项，配置里那个值只是"上次的意图"。
        // 这里只读回显，绝不按配置自动补写注册表——那会把用户手动禁用的启动项又装回去。
        SyncAutoStart();
    }

    /// <summary>把当前偏好写回配置，保留主题、悬浮窗位置、额度开关与自启意图。</summary>
    private void SavePreferences(string? proxyUrl)
    {
        var settings = new LauncherSettings(
        proxyUrl,
        _mihomoPath,
        _gatewayPorts.Party,
        _gatewayPorts.Verge,
        _theme.Mode,
        _floatingEnabled,
        _floatingLeft,
        _floatingTop,
        _quotaEnabled,
        _autoStart.IsEnabled(Application.ExecutablePath),
        _bridgeEnabled,
        _bridgePort);
        // 后台尚未初始化代理控件与网关参数时，仅保存已加载的偏好。
        // 托盘操作和后台启动信号都不能用控件默认值覆盖已有连接设置。
        if (!_desktopSettingsLoaded)
        {
            var saved = _settings.Load();
            settings = settings with
            {
                ProxyUrl = saved.ProxyUrl,
                MihomoPath = saved.MihomoPath,
                PartyPort = saved.PartyPort,
                VergePort = saved.VergePort
            };
        }
        SaveLauncherSettings(settings);
    }

    /// <summary>
    /// 上边栏（标题栏）跟随主题。DWM 的颜色是"设一次就一直有效"的即时状态，
    /// 所以主题变化、窗口重建、配置载入后都要重发，否则会停在旧色上。
    /// </summary>
    private void ApplyWindowChrome() =>
        _ = WindowChrome.Apply(Handle, _theme.Palette, _theme.UseSystemColors);

    private void StartRuntimeMonitor()
    {
        if (_monitor is not null) return;
        _monitor = new RuntimeMonitor(new HttpProbe(RuntimeHealthEvaluator.SampleTimeout), () => _monitorContext);
        _monitor.SnapshotChanged += OnSnapshotChanged;
        _monitor.Start();
    }

    /// <summary>主窗、托盘和悬浮窗共用同一个快照，不各自启动定时探测。</summary>
    private void OnSnapshotChanged(RuntimeHealthSnapshot snapshot) =>
        BeginInvoke(() => RenderRuntime(snapshot));

    private void RenderRuntime(RuntimeHealthSnapshot snapshot)
    {
        _statusDot.State = snapshot.State;
        _statusTitle.Text = snapshot.Title;
        _statusDetail.Text = string.IsNullOrWhiteSpace(snapshot.Detail) ? snapshot.Title : snapshot.Detail;
        // ④元信息文案收口到 RuntimeHealthSnapshot.MetaText（Core 层单测可覆盖）。
        _statusMeta.Text = snapshot.MetaText;
        var (badgeText, badgeBack, badgeFore) = snapshot.State switch
        {
            RuntimeHealthState.Stable => ("✓  稳定", UiTheme.SuccessSurface, UiTheme.SuccessText),
            RuntimeHealthState.Confirming => ("•  确认中", UiTheme.NeutralSurface, UiTheme.NeutralText),
            RuntimeHealthState.Fluctuating => ("!  波动", UiTheme.WarningSurface, UiTheme.WarningText),
            RuntimeHealthState.Down => ("×  连接异常", UiTheme.ErrorSurface, UiTheme.ErrorText),
            RuntimeHealthState.NotRunning => ("—  未启动", UiTheme.NeutralSurface, UiTheme.NeutralText),
            _ => ("•  检测中", UiTheme.NeutralSurface, UiTheme.NeutralText)
        };
        _statusBadge.Text = badgeText;
        _statusBadge.BackColor = badgeBack;
        _statusBadge.ForeColor = badgeFore;
        _notifyIcon.Text = Trim(snapshot.Title, 60);
        _tray.SyncFloating(_floatingEnabled);
        _floating?.Render(snapshot, _monitorContext, $"Party {_gatewayPorts.Party} · Verge {_gatewayPorts.Verge}", QuotaSummary());
    }

    private string QuotaSummary() => _quotaEnabled
        ? _quotaSnapshot?.Summary ?? "额度：正在查询"
        : "额度：额度查询已关闭";

    /// <summary>额度查询独立于网络快照：结果到货时直接刷新悬浮窗提示。</summary>
    private void RefreshFloatingToolTip() => _floating?.UpdateQuota(QuotaSummary());

    /// <summary>额度卡只用共享快照渲染；未知字段与失败数据一律显示「暂不可用」，不折算成 0%。</summary>
    private void RenderQuota(QuotaSnapshot snapshot)
    {
        if (_quotaSnapshot != snapshot)
            _logger.Write("quota-query", snapshot.FailureCategory ?? snapshot.Availability.ToString(), null);
        _quotaSnapshot = snapshot;
        SetRow("额度查询", snapshot.HasData
            ? (snapshot.FailureCategory is null ? "查询成功" : "刷新失败，保留上次数据")
            : snapshot.Detail, null);
        _quotaTitle.Text = snapshot.HasData ? "额度与重置时间" : snapshot.Title;
        var (badgeText, badgeBack, badgeFore) = snapshot.Level switch
        {
            QuotaLevel.Normal => ($"✓  {snapshot.LevelLabel}", UiTheme.SuccessSurface, UiTheme.SuccessText),
            QuotaLevel.Low => ($"!  {snapshot.LevelLabel}", UiTheme.WarningSurface, UiTheme.WarningText),
            QuotaLevel.Critical => ($"×  {snapshot.LevelLabel}", UiTheme.ErrorSurface, UiTheme.ErrorText),
            _ => ($"—  {snapshot.LevelLabel}", UiTheme.NeutralSurface, UiTheme.NeutralText)
        };
        _quotaBadge.Text = badgeText;
        _quotaBadge.BackColor = badgeBack;
        _quotaBadge.ForeColor = badgeFore;

        _quotaDetail.Text = snapshot.HasData
            ? $"共 {snapshot.Buckets.Count} 个额度桶 · 检查于 {snapshot.CheckedAt:HH:mm:ss}"
            : snapshot.Detail;
        RenderQuotaBars(snapshot, DateTimeOffset.Now);

        // 账号归属：始终显示查询账号与计划，无法确认与桌面账号一致时提示核对。
        var account = "账号：" + snapshot.AccountLabel;
        if (snapshot.ConsistencyHint is { } hint) account += " · " + hint;
        _quotaAccount.Text = account;
        _quotaAccount.Visible = snapshot.HasData || snapshot.Availability is QuotaAvailability.Unsupported or QuotaAvailability.NotLoggedIn;

        var note = snapshot.FreshnessNote;
        _quotaFreshness.Text = note ?? "";
        _quotaFreshness.Visible = !string.IsNullOrWhiteSpace(note);
        _quotaRefreshButton.Enabled = _quotaEnabled;

        // 悬浮窗与托盘提示共用同一份摘要，额度到货即更新，不必等下一次网络采样。
        RefreshFloatingToolTip();
    }

    /// <summary>
    /// 把每个额度窗口画成一条 Codex 风格的条状图。行控件按需增删并复用，
    /// 避免每次刷新都重建控件树（额度是定时轮询的）。
    /// </summary>
    private void RenderQuotaBars(QuotaSnapshot snapshot, DateTimeOffset now)
    {
        var windows = snapshot.HasData
            ? snapshot.Buckets.SelectMany(bucket => bucket.Windows).ToList()
            : [];

        while (_quotaBars.Controls.Count > windows.Count)
        {
            var last = _quotaBars.Controls[^1];
            _quotaBars.Controls.Remove(last);
            last.Dispose();
        }

        while (_quotaBars.Controls.Count < windows.Count)
        {
            var row = new QuotaBarRow { Dock = DockStyle.Top };
            _quotaBars.Controls.Add(row);
        }

        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            var row = (QuotaBarRow)_quotaBars.Controls[index];
            // 同一窗口长度可能出现在多个额度桶里，标题带上桶名以免看起来重复。
            var duplicate = windows.Count(w => w.TitleLabel == window.TitleLabel) > 1;
            var title = duplicate ? $"{window.TitleLabel}（{BucketName(snapshot, window)}）" : window.TitleLabel;
            row.Render(window, title, now);
            UiTheme.Style(row);
        }

        _quotaBars.Visible = windows.Count > 0;
        _quotaBars.PerformLayout();
    }

    private static string BucketName(QuotaSnapshot snapshot, QuotaWindow window)
    {
        foreach (var bucket in snapshot.Buckets)
            if (bucket.Windows.Contains(window))
                return string.IsNullOrWhiteSpace(bucket.DisplayName) ? bucket.LimitId : bucket.DisplayName;
        return "额度";
    }

    private void StartQuotaProvider()
    {
        if (_quota is not null) return;
        _quota = _services?.QuotaFactory(_quotaEnabled,_closing.Token)??new SharedQuotaCoordinator(_quotaEnabled?CodexAppServerSessionFactory.ForCurrentUser():null,monitoringEnabled:_quotaEnabled,lifetimeToken: _closing.Token);
        _quota.QuotaChanged += OnQuotaChanged;
        _quota.Start();
    }

    private void OnQuotaChanged(QuotaSnapshot snapshot)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() => RenderQuota(snapshot));
    }

    private async Task RefreshQuotaAsync()
    {
        if (_quota is null || _readingQuota) return;
        _readingQuota = true;
        try { RenderQuota(await _quota.RequestManualAsync(_closing.Token)); }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception ex) { _logger.Write("quota-refresh", ex.GetType().Name, null); }
        finally { _readingQuota = false; }
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>线路指纹变化时立即重新采样，并把新的事实交给监测服务。</summary>
    private void SyncMonitorContext()
    {
        var launchEvidence = _launchRouteTracker.For(_desktop?.Instance);
        var proxy = launchEvidence?.Route == RouteKind.Proxy ? launchEvidence.Proxy : SafeReadProxy();
        var route = launchEvidence?.Route ?? _diagnosis?.Selected;
        var fingerprint = RouteFingerprint(route, proxy);
        var changed = fingerprint != _monitorContext.Fingerprint;
        _monitorContext = new RuntimeMonitorContext
        {
            CodexRunning = _codexRunning,
            TunConflict = _currentTun.HasConflict,
            Route = route,
            Proxy = proxy,
            Fingerprint = fingerprint,
            RouteLabel = launchEvidence is null
                ? RouteLabel(route, proxy)
                : "启动时选择 · " + RouteLabel(route, proxy),
            RouteConfirmed = launchEvidence is not null,
            Events = _diagnosticEvents,
            DiagnosticSourceAvailable = _diagnosticAvailable
        };
        if (changed) _monitor?.RequestRefresh();
    }

    /// <summary>后台只读读取诊断事件；失败时退回线路检测，不冻结界面。</summary>
    private async Task ReadDiagnosticsAsync()
    {
        if (_readingDiagnostics) return;
        var instance = _desktop?.Instance;
        if (instance is null || !instance.HasVerifiedIdentity || instance.BackendProcessIds.Count == 0)
        {
            _diagnosticAvailable = false;
            _diagnosticEvents = Array.Empty<NetworkDiagnosticEvent>();
            SyncMonitorContext();
            return;
        }

        _readingDiagnostics = true;
        try
        {
            var result = await _diagnosticSource.ReadSinceAsync(_diagnosticCursor, instance, _closing.Token);
            _diagnosticCursor = result.Cursor;
            _diagnosticAvailable = result.SourceAvailable;
            _diagnosticEvents = result.Events;
            SyncMonitorContext();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _diagnosticAvailable = false;
            _logger.Write("diagnostic-read", ex.GetType().Name, null);
            SyncMonitorContext();
        }
        finally { _readingDiagnostics = false; }
    }

    private async Task OnPortTimerAsync()
    {
        if (_operationGate.IsBusy || _refreshingPorts) return;
        _refreshingPorts = true;
        try
        {
            var portsChanged = await RefreshUpstreamPortsAsync();
            var tunChanged = RefreshTunState();
            if (tunChanged)
            {
                SetRow("TUN 状态", DescribeTunStatus(), null);
            }
            if ((portsChanged || tunChanged) && _installation is not null)
                await GuardedAsync(CheckAsync);
        }
        catch (Exception ex)
        {
            _logger.Write("port-monitor", ex.GetType().Name, null);
            SetRow("后台监测", "暂时无法刷新状态；下次轮询会重试", null);
        }
        finally { _refreshingPorts = false; }
    }

    /// <summary>最小化或隐藏到托盘都继续后台监测，定时器不再随窗口状态停表。</summary>
    private void HandleWindowResize()
    {
        // 屏幕数量、缩放或分辨率变化后把悬浮窗拉回可见工作区，避免停在已不存在的坐标。
        if (_floating is { Visible: true }) _floating.PlaceAt(_floating.Location.X, _floating.Location.Y);
    }

    private async Task<bool> RefreshUpstreamPortsAsync(bool force = false)
    {
        var entered = false;
        try
        {
            await _portSyncLock.WaitAsync(_closing.Token);
            entered = true;
            var latest = _portDiscovery.Discover(_gatewayPorts);
            if (!force && latest == _gatewayPorts) return false;
            var changed = latest != _gatewayPorts;
            var result = await _gateway.SyncPortsAsync(latest, _closing.Token);
            _gatewayPorts = latest;
            UpdateRouteButtons();
            var detail = $"Party {latest.Party} / Verge {latest.Verge}";
            detail += result switch
            {
                GatewaySyncStatus.Updated => "；固定入口已同步",
                GatewaySyncStatus.ExternalProcess => "；旧脚本入口未同步，请先停止旧入口",
                GatewaySyncStatus.NotRunning => "；入口启动时生效",
                _ => "；已是最新配置"
            };
            SetRow("上游端口", detail, null);
            if (changed || result == GatewaySyncStatus.Updated)
            {
                _diagnosis = null;
                SetRow("推荐启动路径", "上游端口已变化，等待重新检测", null);
            }
            _logger.Write("gateway-port-sync", result.ToString(), null);
            return changed || result == GatewaySyncStatus.Updated;
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            SetRow("上游端口", "自动同步失败：" + ex.Message, null);
            _logger.Write("gateway-port-sync-error", ex.GetType().Name, null);
            return false;
        }
        finally { if (entered) _portSyncLock.Release(); }
    }

    private async Task CheckAsync()
    {
        await RefreshUpstreamPortsAsync();
        RefreshTunState();
        SetRow("TUN 状态", DescribeTunStatus(), null);
        ProxyAddress? proxy;
        try { proxy = ReadProxy(); }
        catch (ArgumentException ex)
        {
            _diagnosis = null;
            var health = new AccessHealthSnapshot(AccessHealthState.Unavailable, "代理地址格式不正确",
                ex.Message + " 例如 127.0.0.1:7890。", null, "代理地址有误", _proxyInput.Text,
                null, DateTimeOffset.Now);
            SetRow("本地代理", "地址格式不正确", null);
            RenderHealth(health);
            return;
        }
        SavePreferences(proxy?.ToString());
        SetRow("本地代理", proxy is null ? "未配置" : proxy.ToString(), null);
        ShowChecking("正在测试系统默认网络和本地代理…");

        if (IsGatewayEntry(proxy))
        {
            try
            {
                var gateway = await _gateway.StatusAsync(_closing.Token);
                if (gateway is not null) SetGatewayRow(gateway);
            }
            catch (Exception ex) when (ex is InvalidDataException or TimeoutException)
            {
                SetRow("固定入口", "状态未知：" + ex.Message, null);
            }
        }

        _diagnosis = await new NetworkDiagnosis(new HttpProbe()).RunAsync(proxy, _closing.Token);
        if (IsGatewayEntry(proxy))
            _diagnosis = _diagnosis with { Selected = NetworkDecision.Choose(_diagnosis.Direct, _diagnosis.Proxy, preferProxy: true) };
        SetRow("系统默认网络 HTTPS", Describe(_diagnosis.Direct), _diagnosis.Direct.Elapsed);
        if (_diagnosis.Proxy is not null) SetRow("本地代理 HTTPS", Describe(_diagnosis.Proxy), _diagnosis.Proxy.Elapsed);
        else SetRow("本地代理 HTTPS", "未测试", null);
        SetRow("推荐启动路径", RouteLabel(_diagnosis.Selected, proxy), null);
        RefreshDesktop();
        SetProcessRow();
        UpdateHealth();
        _logger.Write("network-default", _diagnosis.Direct.Status + "/" + (_diagnosis.Direct.HttpStatus?.ToString() ?? "-"), _diagnosis.Direct.Elapsed);
        if (_diagnosis.Proxy is { } proxyResult)
            _logger.Write("network-proxy", proxyResult.Status + "/" + (proxyResult.HttpStatus?.ToString() ?? "-"), proxyResult.Elapsed);
        _logger.Write("network", _diagnosis.Selected?.ToString() ?? "none", null);
    }

    private async Task StartGatewayAsync()
    {
        await RefreshUpstreamPortsAsync();
        ShowChecking("正在启动并检查固定入口…");
        var result = await _gateway.StartAsync(_mihomoPath, _closing.Token);
        if (result == GatewayStartStatus.Failed)
        {
            SetRow("固定入口", "Mihomo 启动失败；请检查端口是否被占用", null);
            _summary.Text = "固定入口未能启动，请检查 7896 和 9098 端口。";
            return;
        }
        var state = await _gateway.StatusAsync(_closing.Token)
            ?? throw new InvalidOperationException("固定入口未能回应状态查询。");
        SetGatewayRow(state);
        SetProxyText(_gatewayPorts.EntryAddress.ToString());
        await CheckAsync();
        _logger.Write("gateway-start", result.ToString(), null);
    }

    private async Task SwitchGatewayAsync(GatewayUpstream upstream)
    {
        await RefreshUpstreamPortsAsync();
        if (await _gateway.StatusAsync(_closing.Token) is null)
        {
            var started = await _gateway.StartAsync(_mihomoPath, _closing.Token);
            if (started == GatewayStartStatus.Failed)
                throw new InvalidOperationException("固定入口未能启动；请检查 Mihomo 程序和 7896、9098 端口。");
        }
        var state = await _gateway.SwitchAsync(upstream, _closing.Token);
        SetGatewayRow(state);
        SetProxyText(_gatewayPorts.EntryAddress.ToString());
        await CheckAsync();
        _logger.Write("gateway-switch", upstream.ToString(), null);
    }

    private async Task StopGatewayAsync()
    {
        var result = await _gateway.StopAsync(_mihomoPath, _closing.Token);
        var message = result switch
        {
            GatewayStopStatus.Stopped => "固定入口已停止。",
            GatewayStopStatus.NotRunning => "固定入口本来就未运行。",
            GatewayStopStatus.WindowsProxyActive => "Windows 系统代理仍指向固定入口，请先在 Windows 代理设置中关闭它。",
            _ => "无法确认旧入口的进程身份，已保留它。请检查 Mihomo 程序位置。"
        };
        _summary.Text = message;
        if (result is GatewayStopStatus.Stopped or GatewayStopStatus.NotRunning)
        {
            _activeGateway = null;
            UpdateRouteButtons();
            SetRow("固定入口", "未运行", null);
            if (ReadProxy()?.Uri.Port == _gatewayPorts.Entry)
            {
                SetProxyText("");
                await CheckAsync();
            }
        }
        _logger.Write("gateway-stop", result.ToString(), null);
    }

    private void ChooseMihomo()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 mihomo.exe",
            Filter = "Mihomo 程序 (mihomo.exe)|mihomo.exe",
            CheckFileExists = true,
            FileName = _mihomoPath
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _mihomoPath = dialog.FileName;
        SavePreferences(ReadProxy()?.ToString());
        _summary.Text = "已保存 Mihomo 程序位置。";
    }

    private void SetGatewayRow(GatewaySnapshot state)
    {
        _activeGateway = state.Selected;
        UpdateRouteButtons();
        var origin = _gatewayHost.IsOwnedRunning() ? "启动器管理" : "现有脚本入口";
        SetRow("固定入口", $"运行中 · {state.Selected} · 127.0.0.1:{_gatewayPorts.Entry}（{origin}）", null);
    }

    private bool IsKnownUpstreamProxy(string address)
    {
        try
        {
            var port = ProxyAddress.Parse(address).Uri.Port;
            return port == _gatewayPorts.Party || port == _gatewayPorts.Verge || port == _gatewayPorts.Entry;
        }
        catch (ArgumentException) { return false; }
    }

    private async Task LaunchAsync()
    {
        await RefreshUpstreamPortsAsync();
        RefreshTunState();
        SetRow("TUN 状态", DescribeTunStatus(), null);
        if (_tunReadUnavailable || !_currentTun.IsKnown)
        {
            SetRow("Codex 启动", "未启动：代理软件的 TUN 状态暂时不可读，请重试检测", null);
            return;
        }
        if (_currentTun.HasConflict)
        {
            SetRow("Codex 启动", "未启动：检测到双 TUN 冲突", null);
            UpdateHealth();
            _logger.Write("launch", "BlockedTunConflict", null);
            return;
        }
        if (_installation is null)
        {
            _summary.Text = "未找到 Codex 桌面版。";
            return;
        }
        if (_diagnosis is null) await CheckAsync();
        var route = _diagnosis?.Selected ?? RouteKind.Direct;
        var usedFallback = _diagnosis?.Selected is null;
        var launchProxy = ReadProxy();
        var result = await _launcher.LaunchAsync(_installation, route, launchProxy, _closing.Token);
        var text = result switch
        {
            LaunchStatus.Launched when usedFallback => "桌面版已启动，使用系统默认网络尝试连接。若仍显示重连，请先配置本地代理后重新启动。",
            LaunchStatus.Launched => "桌面版已启动。可以直接在 Codex 中开始工作。",
            LaunchStatus.AlreadyRunning => "Codex 已在运行；如需切换线路，请先正常退出后重试。",
            LaunchStatus.Busy => "启动操作暂不可用：另一个启动器可能正在运行，或无法写入 Codex 启动器的本地恢复记录。请稍后重试；若持续出现，请检查用户目录权限。",
            _ => "启动失败，请检查安装状态。"
        };
        SetRow("Codex 启动", text, null);
        _summary.Text = text;
        if (result is LaunchStatus.Launched or LaunchStatus.AlreadyRunning)
        {
            RefreshDesktop();
            if (result == LaunchStatus.Launched && _desktop?.Instance is { } launchedInstance)
                _launchRouteTracker.Record(launchedInstance, route, route == RouteKind.Proxy ? launchProxy : null);
            SyncMonitorContext();
            SetProcessRow();
            UpdateActionAvailability();
        }
        _logger.Write("launch", result.ToString(), null);
    }

    private void UpdateHealth()
    {
        var proxy = SafeReadProxy();
        var route = _diagnosis?.Selected;
        _health = AccessHealthEvaluator.Evaluate(_installation is not null, _currentTun, _diagnosis,
            RouteLabel(route, proxy), proxy?.Uri.Authority, DateTimeOffset.Now);
        RenderHealth(_health);
        // 把最新事实交给运行期监测：线路变化会立即触发重新采样。
        SyncMonitorContext();
        if (_monitor is not null && _installation is null) _monitor.RequestRefresh();
    }

    /// <summary>
    /// 启动前线路检测：只更新操作条建议和禁用原因，不写状态卡。
    /// 状态卡由运行期共享快照（主窗·托盘·悬浮窗同一份）驱动，避免两个写入方互相覆盖。
    /// </summary>
    private void RenderHealth(AccessHealthSnapshot health)
    {
        _health = health;
        _processNotice.Visible = _codexRunning;
        _processNotice.Text = _codexRunning ? "Codex 已在运行；切换线路前请先正常退出。" : "";
        _summary.Text = !_settingsWritable
            ? "本次设置无法保存；下次启动时需要重新配置。"
            : _codexRunning
                ? "Codex 已在运行；切换线路前请先正常退出。"
                : health.State switch
                {
                    AccessHealthState.Available => "基础线路可达，可以尝试启动 Codex。",
                    AccessHealthState.NeedsVerification => "网络可达，但响应较慢；启动后由运行期监测继续观察。",
                    AccessHealthState.Unavailable => "当前线路不可用；检查代理软件和端口后重新检测。",
                    _ => "正在检查线路，请稍候。"
                };
        UpdateActionAvailability();
    }

    private void ShowChecking(string detail)
    {
        var proxy = SafeReadProxy();
        RenderHealth(new AccessHealthSnapshot(AccessHealthState.Checking, "正在检测", detail,
            _diagnosis?.Selected, RouteLabel(_diagnosis?.Selected, proxy), proxy?.Uri.Authority,
            null, DateTimeOffset.Now));
    }

    private void UpdateActionAvailability()
    {
        var enabled = !_operationGate.IsBusy;
        _checkButton.Enabled = enabled;
        _launchButton.Enabled = enabled && _installation is not null && !_tunReadUnavailable && _currentTun.IsKnown && !_currentTun.HasConflict && !_codexRunning;
        _gatewayStartButton.Enabled = enabled;
        _partyButton.Enabled = enabled;
        _vergeButton.Enabled = enabled;
        _gatewayStopButton.Enabled = enabled;
        _chooseMihomoButton.Enabled = enabled;

        // 禁用操作也要可读，并给出具体原因，而不是只把按钮涂灰。
        _toolTip.SetToolTip(_checkButton, enabled ? "重新检测当前网络与 Codex 状态。" : BusyReason());
        _toolTip.SetToolTip(_launchButton, _launchButton.Enabled ? "启动 Codex 桌面版。" : LaunchDisabledReason(enabled));
        _toolTip.SetToolTip(_gatewayStartButton, enabled ? "启动或复用 7896 固定入口。" : BusyReason());
        _toolTip.SetToolTip(_partyButton, enabled ? "把固定入口切换到 Party 当前自动发现的 mixed 端口，切换后会立即重新检测。" : BusyReason());
        _toolTip.SetToolTip(_vergeButton, enabled ? "把固定入口切换到 Verge 当前自动发现的 mixed 端口，切换后会立即重新检测。" : BusyReason());
        _toolTip.SetToolTip(_gatewayStopButton, enabled ? "停止由启动器管理的固定入口。" : BusyReason());
        _toolTip.SetToolTip(_chooseMihomoButton, enabled ? "选择 mihomo.exe 的位置。" : BusyReason());

        // 关闭按钮只受进程身份与关闭流程影响，不受后台采样持锁影响。
        var close = CodexCloseButtonPolicy.Evaluate(_desktop, _closeWorkflow?.IsBusy == true);
        _closeButton.Enabled = close.Enabled;
        _closeButton.Text = close.Text;
        _toolTip.SetToolTip(_closeButton, close.Enabled ? "向已确认的 Codex 主窗口请求正常关闭" : close.Reason);
        if (!close.Enabled && close.Reason.Length > 0 && _desktop is not null && _desktop.State != CodexDesktopState.NotRunning)
            _processNotice.Text = close.Reason;
    }

    private static string BusyReason() => "正在执行上一项操作，完成后再试。";

    private string LaunchDisabledReason(bool enabled)
    {
        if (!enabled) return BusyReason();
        if (_installation is null) return "未找到 OpenAI.Codex 安装包，无法启动。";
        if (_tunReadUnavailable || !_currentTun.IsKnown) return "Party 或 Verge 的 TUN 状态暂时不可读，请重试检测。";
        if (_currentTun.HasConflict) return "Party 和 Verge 的 TUN 同时开启，先关闭其中一个再启动。";
        if (_codexRunning) return "Codex 已在运行；如需切换线路请先正常退出。";
        return "启动 Codex 桌面版。";
    }

    private void RefreshDesktop()
    {
        if (_installation is null)
        {
            _desktop = null;
            _codexRunning = false;
            _launchRouteTracker.Clear();
            return;
        }

        _desktop = _detector.Discover(_installation);
        _codexRunning = _desktop.PossiblyRunning;
        _closeWorkflow ??= new CodexCloseWorkflow(new CodexDesktopController(_installation, new DesktopProcessControl()));
    }

    /// <summary>正常关闭 → 8 秒等待 → 继续等待／取消／确认强退；全程不占用操作闸门。</summary>
    private async Task CloseCodexAsync()
    {
        if (_installation is null || _desktop?.Instance is null)
        {
            UpdateActionAvailability();
            return;
        }

        RefreshDesktop();
        var close = CodexCloseButtonPolicy.Evaluate(_desktop, _closeWorkflow?.IsBusy == true);
        if (!close.Enabled)
        {
            _summary.Text = close.Reason;
            UpdateActionAvailability();
            return;
        }

        var workflow = _closeWorkflow ??= new CodexCloseWorkflow(new CodexDesktopController(_installation, new DesktopProcessControl()));
        var instance = _desktop.Instance;
        UpdateActionAvailability();
        var attempt = await workflow.StartAsync(instance, _closing.Token);
        _summary.Text = attempt.Detail;
        _logger.Write("close", attempt.Stage.ToString(), null);

        while (attempt.AwaitingDecision)
        {
            var choice = CloseDecisionDialog.Ask(this, attempt.Detail);
            if (choice == CodexCloseChoice.ForceClose)
            {
                var confirm = MessageBox.Show(
                    this,
                    "强制关闭可能中断 Codex 正在执行的任务。确定要结束该实例的进程吗？",
                    "确认强制关闭",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                var force = await workflow.ForceCloseAsync(confirm, _closing.Token);
                _summary.Text = force.Detail;
                _logger.Write("close-force", force.Exited ? "exited" : "still-running", null);
                attempt = new CodexCloseAttempt(force.Exited ? CodexCloseStage.Closed : CodexCloseStage.Failed, force.Detail);
                break;
            }

            if (choice == CodexCloseChoice.Cancel || choice is null)
            {
                attempt = workflow.Cancel();
                break;
            }

            attempt = await workflow.KeepWaitingAsync(_closing.Token);
            _summary.Text = attempt.Detail;
        }

        _summary.Text = attempt.Detail;
        RefreshDesktop();
        SetProcessRow();
        UpdateActionAvailability();

        // 关闭完成后回到启动前诊断，便于直接重新启动。
        if (attempt.Exited && _installation is not null) await GuardedAsync(CheckAsync);
    }

    private void UpdateRouteButtons()
    {
        StyleRouteButton(_partyButton, "Party", _gatewayPorts.Party, _activeGateway == GatewayUpstream.Party);
        StyleRouteButton(_vergeButton, "Verge", _gatewayPorts.Verge, _activeGateway == GatewayUpstream.Verge);
    }

    private static void StyleRouteButton(Button button, string name, int port, bool selected)
    {
        button.Text = $"{(selected ? "●" : "○")}  {name} · 自动端口 {port}";
        button.BackColor = selected ? UiTheme.PrimarySurface : UiTheme.Card;
        button.ForeColor = selected ? UiTheme.AccentText : UiTheme.Text;
        button.FlatAppearance.BorderColor = selected ? UiTheme.Primary : UiTheme.Border;
        button.FlatAppearance.MouseOverBackColor = UiTheme.PrimarySurface;
    }

    private void SetProcessRow() =>
        SetRow("现有桌面进程", _codexRunning ? "运行中；切换代理前请正常退出" : "未运行", null);

    private string RouteFingerprint(RouteKind? route, ProxyAddress? proxy)
    {
        var tun = $"tun:{_currentTun.PartyEnabled}:{_currentTun.VergeEnabled}";
        return route switch
        {
            RouteKind.Direct => "direct|" + tun,
            RouteKind.Proxy when IsGatewayEntry(proxy) =>
                $"gateway:{proxy}:{_activeGateway?.ToString() ?? "unknown"}:{_gatewayPorts.Party}:{_gatewayPorts.Verge}|{tun}",
            RouteKind.Proxy => $"proxy:{proxy}|{tun}",
            _ => "none|" + tun
        };
    }

    private string RouteLabel(RouteKind? route, ProxyAddress? proxy) => route switch
    {
        RouteKind.Direct => "系统默认网络",
        RouteKind.Proxy when IsGatewayEntry(proxy) && _activeGateway is not null => $"固定入口 · {_activeGateway}",
        RouteKind.Proxy when IsGatewayEntry(proxy) => "固定入口 · Party/Verge",
        RouteKind.Proxy => "本地代理",
        _ => "未找到可用路径"
    };

    private ProxyAddress? ReadProxy() => string.IsNullOrWhiteSpace(_proxyInput.Text) ? null : ProxyAddress.Parse(_proxyInput.Text);

    private ProxyAddress? SafeReadProxy()
    {
        try { return ReadProxy(); }
        catch (ArgumentException) { return null; }
    }

    private void SetProxyText(string value)
    {
        _suppressProxyChange = true;
        _proxyInput.Text = value;
        _suppressProxyChange = false;
    }

    private bool IsGatewayEntry(ProxyAddress? proxy) => proxy?.Uri.Port == _gatewayPorts.Entry;

    private static string DescribeTun(TunModeState state) =>
        $"Party {DescribeTunValue(state.PartyEnabled)} / Verge {DescribeTunValue(state.VergeEnabled)}" +
        (state.HasConflict ? "（冲突）" : "");

    private bool RefreshTunState() => MergeTunState(_tunDiscovery.Read());

    private bool MergeTunState(TunModeState observed)
    {
        var previous = _currentTun;
        var wasUnavailable = _tunReadUnavailable;
        _currentTun = _currentTun.MergeKnown(observed);
        _tunReadUnavailable = !observed.IsKnown;

        if (_tunReadUnavailable && !_tunUnavailableLogged)
        {
            _logger.Write("tun-config", "temporarily-unavailable", null);
            _tunUnavailableLogged = true;
        }
        else if (!_tunReadUnavailable && _tunUnavailableLogged)
        {
            _logger.Write("tun-config", "recovered", null);
            _tunUnavailableLogged = false;
        }

        return previous != _currentTun || wasUnavailable != _tunReadUnavailable;
    }

    private string DescribeTunStatus()
    {
        var description = DescribeTun(_currentTun);
        if (!_tunReadUnavailable) return description;
        return _currentTun.IsKnown
            ? description + "（配置暂时不可读，显示上次有效状态）"
            : description + "（配置暂时不可读）";
    }

    private static string DescribeTunValue(bool? enabled) => enabled switch
    {
        true => "开启",
        false => "关闭",
        _ => "未知"
    };

    private static ProxyAddress? DetectLocalProxy()
    {
        foreach (var key in new[] { "HTTPS_PROXY", "https_proxy", "HTTP_PROXY", "http_proxy" })
        {
            try { return ProxyAddress.Parse(Environment.GetEnvironmentVariable(key) ?? ""); }
            catch (ArgumentException) { }
        }
        using var settings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
        return Convert.ToInt32(settings?.GetValue("ProxyEnable") ?? 0) == 1
            ? ProxyDiscovery.FromWindowsProxyServer(settings?.GetValue("ProxyServer") as string)
            : null;
    }

    private static string Describe(ProbeResult result) => result.Status switch
    {
        ProbeStatus.Reachable => "HTTPS 可达（" + result.Detail + "）",
        ProbeStatus.ProxyAuthRequired => "代理要求认证（" + result.Detail + "）",
        ProbeStatus.HttpRejected when result.HttpStatus is 401 or 403 => "已到达服务器，检测请求被拒绝（" + result.Detail + "）",
        ProbeStatus.HttpRejected => "已到达服务器（" + result.Detail + "）",
        _ => result.Detail
    };

    private void SetRow(string title, string value, TimeSpan? elapsed)
    {
        if (!_rows.TryGetValue(title, out var row))
        {
            row = new ListViewItem([title, value, ""]);
            _rows.Add(title, row);
            _results.Items.Add(row);
            ResizeResultsHeight();
        }
        row.SubItems[1].Text = value;
        row.SubItems[2].Text = elapsed is null ? "" : $"{elapsed.Value.TotalMilliseconds:0} ms";
    }

    private void ResizeResultColumns()
    {
        if (_results.Columns.Count != 3 || _results.ClientSize.Width < 420) return;
        var scale = _results.DeviceDpi / 96d;
        var firstWidth = Math.Min((int)Math.Round(230 * scale), _results.ClientSize.Width / 3);
        var durationWidth = (int)Math.Round(90 * scale);
        var resultWidth = _results.ClientSize.Width - firstWidth - durationWidth;
        if (resultWidth < (int)Math.Round(180 * scale))
        {
            durationWidth = Math.Max((int)Math.Round(64 * scale), durationWidth - ((int)Math.Round(180 * scale) - resultWidth));
            resultWidth = _results.ClientSize.Width - firstWidth - durationWidth;
        }
        _results.Columns[0].Width = firstWidth;
        _results.Columns[1].Width = Math.Max(1, resultWidth);
        _results.Columns[2].Width = durationWidth;
    }

    private void ResizeResultsHeight()
    {
        if (_results.IsDisposed || _results.Disposing) return;
        var padding = Math.Max(2, (int)Math.Ceiling(2 * _results.DeviceDpi / 96d));
        // HandleCreated/FontChanged 期间托管行已存在，但原生列表项还未提交。
        // 不读取 GetItemRect：按当前字体与 DPI 预留行高及表头，可安全用于隐藏和句柄重建路径。
        var rowHeight = _results.Font.Height + (int)Math.Ceiling(6 * _results.DeviceDpi / 96d);
        var headerHeight = _results.Font.Height + (int)Math.Ceiling(12 * _results.DeviceDpi / 96d);
        var bottom = headerHeight + rowHeight * _results.Items.Count;
        var height = Math.Max((int)Math.Ceiling(235 * _results.DeviceDpi / 96d), bottom + padding);
        _results.MinimumSize = new Size(0, height);
        if (_results.Height == height) return;
        _results.Height = height;
        RelayoutFrom(_results);
    }

    /// <summary>
    /// 从被切换可见性的控件向上逐级重排到窗体。改 Visible 不会让母容器重新算高，
    /// 展开/收起后必须显式重排，否则新增的行会被母卡片裁掉。
    /// </summary>
    private static void RelayoutFrom(Control control)
    {
        for (var current = control; current is not null; current = current.Parent)
            current.PerformLayout();
    }

    private void SaveLauncherSettings(LauncherSettings settings)
    {
        if (_lastSavedSettings == settings) return;
        var result = _settings.TrySave(settings);
        if (result.Succeeded) { _lastSavedSettings = settings; return; }
        _settingsWritable = false;
        SetRow("本地设置", "写入失败；本次运行仍可使用，设置下次不会保留", null);
        _logger.Write("settings-save", result.ErrorType ?? "Failed", null);
        if (_health is not null) RenderHealth(_health);
    }

    private void SetThemeMode(ThemeMode mode)
    {
        _theme.SetMode(mode);
        ThemeManager.ApplyTo(this, _theme.Palette, _theme.UseSystemColors);
        ApplyWindowChrome();
        SyncThemeMenu();
        _floating?.ApplyTheme();
        SavePreferences(SafeReadProxy()?.ToString());
    }

    /// <summary>把当前主题同步到两个入口：高级设置里的选择器与托盘菜单。</summary>
    private void SyncThemeMenu()
    {
        _themeSelector.SetMode(_theme.Mode);
        _tray.SyncTheme(_theme.Mode);
    }

    private void SetFloatingWindow(bool enabled)
    {
        _floatingEnabled = enabled;
        _tray.SyncFloating(enabled);
        if (enabled) ShowFloatingWindow();
        else HideFloatingWindow();
        SavePreferences(SafeReadProxy()?.ToString());
    }

    private void ShowFloatingWindow()
    {
        _floating ??= BuildFloatingWindow();
        _floating.PlaceAt(_floatingLeft ?? DefaultFloatingLeft(), _floatingTop ?? DefaultFloatingTop());
        _floating.Show();
    }

    private void HideFloatingWindow() => _floating?.Hide();

    private FloatingStatusForm BuildFloatingWindow()
    {
        var window = new FloatingStatusForm();
        window.OpenRequested += RestoreFromTray;
        window.HideRequested += () =>
        {
            _floatingEnabled = false;
            _tray.SyncFloating(false);
            HideFloatingWindow();
            SavePreferences(SafeReadProxy()?.ToString());
        };
        window.ExitRequested += RequestExit;
        window.PositionChanged += (x, y) =>
        {
            _floatingLeft = x;
            _floatingTop = y;
            SavePreferences(SafeReadProxy()?.ToString());
        };
        return window;
    }

    private static int DefaultFloatingLeft()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        return Math.Max(area.X, area.Right - 260);
    }

    private static int DefaultFloatingTop()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        return area.Y + 24;
    }

    /// <summary>隐藏到托盘继续后台监测；首次隐藏时提示恢复与退出方式。</summary>
    private void HideToTray()
    {
        _bridgePanel.ClearInvitation();
        Hide();
        ShowInTaskbar = false;
        if (_trayHintShown) return;
        _trayHintShown = true;
        _notifyIcon.BalloonTipTitle = "Codex 启动器仍在后台监测";
        _notifyIcon.BalloonTipText = "双击托盘图标可恢复主窗口；右键托盘图标可打开主窗口、切换悬浮窗或退出启动器。";
        _notifyIcon.ShowBalloonTip(5000);
    }

    private void RestoreFromTray()
    {
        _allowVisible=true;
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        if(!_desktopInitialized)_=GuardedAsync(InitializeAsync);
    }

    /// <summary>显式退出：先停监测与请求，再结束启动器自己创建的查询进程。</summary>
    private void RequestExit()
    {
        if (_exitRequested) return;
        _exitRequested = true;
        _logger.Write("exit", "explicit", null);
        Close();
    }

    private void RequestRefresh()
    {
        if (_installation is null) return;
        _ = GuardedAsync(async () =>
        {
            await CheckAsync();
            _monitor?.RequestRefresh();
        });
    }

    /// <summary>
    /// 系统注销或关机时按显式退出处理：取消在途请求、释放自有资源，
    /// 但不关闭 Codex、代理软件或固定入口。
    /// </summary>
    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        if (_exitRequested) return;
        _exitRequested = true;
        _logger.Write("exit", "session-ending", null);
        // 复用显式退出的释放路径：停表、取消在途请求、释放自有资源，
        // 避免注销时把在途请求和启动器自己创建的查询进程留给系统清理。
        if (IsDisposed || Disposing) return;
        try
        {
            if (InvokeRequired) BeginInvoke(Close);
            else Close();
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>屏幕数量、分辨率或缩放变化后，把悬浮窗重新约束到可见工作区。</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        try
        {
            BeginInvoke(() =>
            {
                if (_floating is { Visible: true }) _floating.PlaceAt(_floating.Location.X, _floating.Location.Y);
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>等待重复启动信号；收到后由主线程恢复已有主窗口。</summary>
    internal void ListenForActivation(CancellationToken cancellationToken)
    {
        if (_singleInstance is null) return;
        _ = Task.Run(() =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var foreground=_singleInstance.WaitForActivation(TimeSpan.FromMilliseconds(300));
                var background=_singleInstance.WaitForBackground(TimeSpan.Zero);
                if(!foreground&&!background)continue;
                if (IsDisposed || Disposing) return;
                try { BeginInvoke(async()=>{if(foreground)RestoreFromTray();if(background)await ApplyBackgroundSignalAsync();}); }
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { return; }
            }
        }, cancellationToken);
    }

    private async Task GuardedAsync(Func<Task> work)
    {
        if (!_operationGate.TryEnter()) return;
        UpdateActionAvailability();
        try { await work(); }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _summary.Text = "操作失败：" + ex.Message;
            _logger.Write("error", ex.GetType().Name, null);
        }
        finally
        {
            _operationGate.Exit();
            UpdateActionAvailability();
        }
    }
}
