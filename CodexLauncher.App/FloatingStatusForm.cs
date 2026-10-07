using System.Drawing.Imaging;
using CodexLauncher.Core;

namespace CodexLauncher.App;

/// <summary>
/// 共享快照悬浮窗：只渲染主窗推送的状态，自己不采集网络、不启动定时探测。
/// 按设置选择桌面层级或始终置顶；始终不抢焦点、可拖动，位置由调用方保存并按可见工作区约束。
///
/// 整卡自绘后由 <see cref="LayeredSurface"/> 提交给合成器，因此圆角与投影是真正的抗锯齿半透明像素。
/// 尺寸由 <see cref="FloatingCard"/> 按字体度量 × <see cref="Control.DeviceDpi"/> 算出——
/// 旧实现把客户区写死成 228x60，150% 缩放下第二行文字只剩 15px 可见，这是本类不再使用固定尺寸的原因。
/// 这里也不放任何子控件：子控件的不透明背景会盖住玻璃效果。
/// </summary>
internal sealed class FloatingStatusForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;

    private readonly Font _stateFont = new("Microsoft YaHei UI", 10F, FontStyle.Bold);
    private readonly Font _routeFont = new("Microsoft YaHei UI", 9F);

    // 悬浮窗带 WS_EX_NOACTIVATE，永远不会成为活动窗口；ShowAlways=false 时
    // WinForms 会因宿主未激活而拒绝弹出提示，额度与线路详情就永远看不到。
    private readonly ToolTip _toolTip = new()
    {
        AutoPopDelay = 20000,
        InitialDelay = 300,
        ReshowDelay = 120,
        ShowAlways = true
    };
    private readonly ToolStripMenuItem _openItem = new("打开主窗口");
    private readonly ToolStripMenuItem _hideItem = new("隐藏悬浮窗");
    private readonly ToolStripMenuItem _exitItem = new("退出启动器");
    private string _stateText = "正在检测";
    private string _routeText = "";
    private RuntimeHealthState _state = RuntimeHealthState.Checking;
    private Size _surfaceSize;
    private bool _dragging;
    private Point _dragOrigin;
    private bool _moved;

    internal FloatingStatusForm(FloatingWindowMode displayMode = FloatingWindowMode.Desktop)
    {
        Text = "Codex 运行状态";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        SetDisplayMode(displayMode);
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Card;
        // 分层窗口由 UpdateLayeredWindow 全权提交像素，WinForms 的背景擦除会把它抹掉。
        SetStyle(ControlStyles.Opaque, true);
        AccessibleRole = AccessibleRole.StatusBar;

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([_openItem, _hideItem, new ToolStripSeparator(), _exitItem]);
        ContextMenuStrip = menu;
        UiTheme.Style(menu);

        _openItem.Click += (_, _) => RequestOpen();
        _hideItem.Click += (_, _) => RequestHide();
        _exitItem.Click += (_, _) => RequestExit();

        // 拖动：按下记录起点，移动超过阈值才算拖动，避免双击被吃掉。
        MouseDown += OnDragStart;
        MouseMove += OnDragMove;
        MouseUp += OnDragEnd;
        DoubleClick += (_, _) => RequestOpen();
        MouseEnter += (_, _) => _toolTip.SetToolTip(this, _toolTipText);

        ApplyLayout();
    }

    /// <summary>双击或右键菜单要求恢复主窗口。</summary>
    internal event Action? OpenRequested;

    /// <summary>右键菜单要求隐藏悬浮窗（只改变显示，不退出启动器）。</summary>
    internal event Action? HideRequested;

    /// <summary>右键菜单要求显式退出启动器。</summary>
    internal event Action? ExitRequested;

    /// <summary>用户拖动后保存新位置。</summary>
    internal event Action<int, int>? PositionChanged;

    /// <summary>切换桌面显示与始终置顶；悬浮窗保持非激活窗口。</summary>
    internal void SetDisplayMode(FloatingWindowMode displayMode)
    {
        TopMost = displayMode == FloatingWindowMode.AlwaysOnTop;
    }

    private string _toolTipText = "正在检测";
    private RuntimeHealthSnapshot? _lastSnapshot;
    private RuntimeMonitorContext? _lastContext;
    private string _lastPortSummary = "";
    private string _lastQuotaSummary = "";

    /// <summary>是否已按分层窗口创建（像素自绘 + 真透明，而不是拿背景色假装圆角）。</summary>
    internal bool IsLayeredWindow => (CreateParams.ExStyle & WsExLayered) != 0;

    internal string StateText => _stateText;

    internal string RouteText => _routeText;

    /// <summary>整张自绘卡片的尺寸（含投影留白），等于客户区尺寸。</summary>
    internal Size SurfaceSize => _surfaceSize;

    /// <summary>用共享快照刷新显示；悬浮窗不参与采集。</summary>
    internal void Render(RuntimeHealthSnapshot snapshot, RuntimeMonitorContext context, string portSummary, string quotaSummary)
    {
        _lastSnapshot = snapshot;
        _lastContext = context;
        _lastPortSummary = portSummary;
        _lastQuotaSummary = quotaSummary;
        _toolTipText = BuildToolTip(snapshot, context, portSummary, quotaSummary);
        _toolTip.SetToolTip(this, _toolTipText);
        RenderText(
            snapshot.ShortLabel,
            snapshot.RouteConfirmed ? snapshot.RouteLabel : snapshot.RouteLabel + "（未确认）",
            snapshot.State);
    }

    /// <summary>
    /// 只刷新卡片上的两行文字与状态色。尺寸随文字重新量算，因此换一段更长的线路说明会把卡片撑宽，
    /// 而不是把文字裁掉。
    /// </summary>
    internal void RenderText(string stateText, string routeText, RuntimeHealthState state)
    {
        _stateText = stateText ?? "";
        _routeText = routeText ?? "";
        _state = state;
        // 原来这两行文字是真实 Label，控件树自带无障碍名称；改成自绘后必须由窗体自己承担。
        AccessibleName = _routeText.Length == 0 ? _stateText : $"{_stateText}，{_routeText}";
        ApplyLayout();
    }

    /// <summary>额度是独立于网络快照的异步结果：到货时只刷新提示，不重画状态。</summary>
    internal void UpdateQuota(string quotaSummary)
    {
        _lastQuotaSummary = quotaSummary;
        if (_lastSnapshot is not { } snapshot || _lastContext is not { } context) return;
        _toolTipText = BuildToolTip(snapshot, context, _lastPortSummary, quotaSummary);
        _toolTip.SetToolTip(this, _toolTipText);
    }

    /// <summary>把窗口位置约束到当前可见工作区。</summary>
    internal void PlaceAt(int x, int y)
    {
        var screens = new List<ScreenArea>();
        foreach (var screen in Screen.AllScreens)
        {
            var area = screen.WorkingArea;
            screens.Add(new ScreenArea(area.X, area.Y, area.Width, area.Height));
        }

        var (left, top) = FloatingPlacement.Resolve(x, y, Width, Height, screens);
        Location = new Point(left, top);
    }

    internal void ApplyTheme()
    {
        BackColor = UiTheme.Card;
        Present();
        Invalidate(true);
    }

    /// <summary>按当前文字重新量算客户区，再重画整卡。文字或 DPI 一变就必须走这里。</summary>
    private void ApplyLayout()
    {
        if (!IsHandleCreated) _ = Handle;
        if (!IsHandleCreated) return;

        Size size;
        using (var graphics = Graphics.FromHwnd(Handle))
            size = FloatingCard.Measure(graphics, _stateText, _routeText, _stateFont, _routeFont, DeviceDpi);

        _surfaceSize = size;
        if (ClientSize != size) ClientSize = size;
        Present();
    }

    /// <summary>把整卡画进 32 位 ARGB 位图后提交给合成器。</summary>
    private void Present()
    {
        if (!IsHandleCreated) return;
        var size = _surfaceSize.Width > 0 ? _surfaceSize : ClientSize;
        if (size.Width <= 0 || size.Height <= 0) return;

        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        // 位图分辨率必须跟窗口一致，否则 GDI+ 会按 96 DPI 画字，量算与绘制就对不上。
        bitmap.SetResolution(DeviceDpi, DeviceDpi);
        using (var graphics = Graphics.FromImage(bitmap))
            FloatingCard.Paint(graphics, size, _stateText, _routeText, _state, _stateFont, _routeFont, DeviceDpi);
        LayeredSurface.Present(Handle, bitmap);
    }

    private static string BuildToolTip(RuntimeHealthSnapshot snapshot, RuntimeMonitorContext context, string portSummary, string quotaSummary)
    {
        var lines = new List<string>
        {
            snapshot.Title,
            "线路：" + (snapshot.RouteConfirmed ? snapshot.RouteLabel : snapshot.RouteLabel + "（未确认实际线路）"),
            "Party / Verge：" + portSummary,
            "当前端口：" + (context.Proxy?.ToString() ?? "未配置")
        };
        lines.Add("耗时：" + (snapshot.Elapsed is null ? "—" : $"{snapshot.Elapsed.Value.TotalMilliseconds:0} ms"));
        lines.Add("更新：" + snapshot.CheckedAt.ToString("HH:mm:ss"));
        if (!string.IsNullOrWhiteSpace(snapshot.Detail)) lines.Add("原因：" + snapshot.Detail);
        if (!string.IsNullOrWhiteSpace(snapshot.Advice)) lines.Add("建议：" + snapshot.Advice);
        // 额度摘要自带「额度：」前缀，这里只兜底缺失的情况，避免出现两个前缀。
        lines.Add(string.IsNullOrWhiteSpace(quotaSummary) ? "额度：暂不可用" : quotaSummary);
        return string.Join(Environment.NewLine, lines);
    }

    private void OnDragStart(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _moved = false;
        _dragOrigin = new Point(e.X, e.Y);
    }

    private void OnDragMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var deltaX = e.X - _dragOrigin.X;
        var deltaY = e.Y - _dragOrigin.Y;
        if (!_moved && Math.Abs(deltaX) < 3 && Math.Abs(deltaY) < 3) return;
        _moved = true;
        Location = new Point(Location.X + deltaX, Location.Y + deltaY);
    }

    private void OnDragEnd(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        if (_moved) PositionChanged?.Invoke(Location.X, Location.Y);
    }

    private void RequestOpen() => OpenRequested?.Invoke();

    private void RequestHide() => HideRequested?.Invoke();

    private void RequestExit() => ExitRequested?.Invoke();

    protected override bool ShowWithoutActivation => true;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 分层窗口的像素全部来自 UpdateLayeredWindow，任何 GDI 擦除都会把它盖成不透明色块。
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // 不调 base.OnPaint：没有子控件要画，只需要保证像素与当前状态一致。
        Present();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyLayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyLayout();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            // 不抢焦点：置顶工具窗且不激活，避免打断 Codex 输入；分层窗口才能提交半透明像素。
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow | WsExLayered;
            return parameters;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _stateFont.Dispose();
            _routeFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
