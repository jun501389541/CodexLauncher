using System.Drawing.Drawing2D;
using System.ComponentModel;
using CodexLauncher.Core;

namespace CodexLauncher.App;

/// <summary>
/// 页面滚动容器，用自绘的细指示条替代系统滚动条。
/// <para>
/// 为什么不用 <c>Panel.AutoScroll</c>：系统滚动条在深色主题下没有可用的着色方式。
/// <c>SetWindowTheme(handle, "DarkMode_Explorer")</c> 实测只把滑块涂成 #2E2E42，
/// 轨道仍是 #C2C2C2..#DDDDDD 的浅色渐变——深浅混搭比不改还难看，而且它由
/// comctl32 直接绘制，绕不过去。这里自己管理内容偏移，右边缘画一条主题色的圆角指示条。
/// </para>
/// <para>
/// 内容宽度由调用方决定（主窗体要给指示条留出空隙），这里只管纵向偏移。
/// 两个人都去设 Width 会互相覆盖，所以本控件绝不改内容的宽度。
/// </para>
/// </summary>
internal sealed class ScrollHost : Panel, IMessageFilter
{
    private const int BaseIndicatorWidth = 6;
    private const int BaseDpi = 96;
    private const int BaseIndicatorMargin = 4;
    private const int LineHeight = 16;
    internal const int WmMouseWheel = 0x020A;

    internal const int MinimumThumbLength = 24;

    private Control? _content;
    private int _offset;
    private bool _arranging;

    public ScrollHost()
    {
        DoubleBuffered = true;
        AutoScroll = false;
        BackColor = UiTheme.Window;
        // AutoScroll 关掉后，滚轮消息不再有默认接收者：单行 TextBox 拿到 WM_MOUSEWHEEL
        // 会直接丢掉，页面就彻底滚不动了。用消息过滤器把光标下的滚轮交给本控件。
        Application.AddMessageFilter(this);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    // ⑥指示条改为半透明柔和色；高对比度（调色板本身或系统色注入）保持不透明纯色，透明度会伤可读性。
    internal Color ThumbColor =>
        UiTheme.Palette == ThemePalette.HighContrast || UiTheme.UseSystemColors
            ? UiTheme.Muted
            : Color.FromArgb(140, UiTheme.Muted);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color TrackColor => UiTheme.Border;

    internal int Offset => _offset;

    internal int ContentHeight => _content?.Height ?? 0;

    internal int ViewportHeight => ClientSize.Height > 0 ? ClientSize.Height : Height;

    internal bool IsOverflowing => MaxOffset(ContentHeight, ViewportHeight) > 0;

    /// <summary>
    /// 是否画那条细轨道。轨道是纯粹的装饰性暗示；当它与指示条或背景同色时必须放弃它，
    /// 因为高对比度调色板下 Muted 与 Border 都是纯白——两者同色时，指示条长度
    /// （= "当前看到多少"这条信息）会被轨道吃掉，看不出来了。
    /// 宁可少一个装饰，也不能少一条信息。
    /// </summary>
    internal bool ShowsRail
    {
        get
        {
            if (!IsOverflowing) return false;
            var thumb = ThumbColor;
            return thumb != TrackColor && TrackColor != BackColor;
        }
    }

    /// <summary>指示条在控件坐标里的位置；内容放得下时为空。</summary>
    internal Rectangle ThumbBounds
    {
        get
        {
            var thumb = Thumb(ViewportHeight, ContentHeight, _offset);
            if (thumb.IsEmpty) return Rectangle.Empty;
            var width = IndicatorWidth(DeviceDpi);
            var x = Math.Max(0, ClientSize.Width - width - IndicatorMargin(DeviceDpi));
            return new Rectangle(x, thumb.Y, width, thumb.Height);
        }
    }

    internal static int IndicatorWidth(int dpi) =>
        Math.Max(2, (int)Math.Round(BaseIndicatorWidth * (dpi / (double)BaseDpi), MidpointRounding.AwayFromZero));

    internal static int IndicatorMargin(int dpi) =>
        (int)Math.Round(BaseIndicatorMargin * (dpi / (double)BaseDpi), MidpointRounding.AwayFromZero);

    /// <summary>内容能滚动的最大距离；视口或内容无效时为 0（不能滚）。</summary>
    internal static int MaxOffset(int contentHeight, int viewportHeight) =>
        viewportHeight <= 0 || contentHeight <= 0 ? 0 : Math.Max(0, contentHeight - viewportHeight);

    /// <summary>
    /// 指示条的几何：长度按"视口 / 内容"比例，位置按"已滚 / 可滚"比例。
    /// 必须严格线性，否则用户读不出"还剩多少没看到"。
    /// </summary>
    internal static Rectangle Thumb(int viewportHeight, int contentHeight, int offset)
    {
        if (viewportHeight <= 0 || contentHeight <= viewportHeight) return Rectangle.Empty;

        var track = viewportHeight;
        var maxOffset = MaxOffset(contentHeight, viewportHeight);
        var height = (int)Math.Round(track * (viewportHeight / (double)contentHeight), MidpointRounding.AwayFromZero);
        height = Math.Clamp(height, Math.Min(MinimumThumbLength, track), track);
        var travel = track - height;
        var clamped = Math.Clamp(offset, 0, maxOffset);
        var y = maxOffset <= 0 || travel <= 0
            ? 0
            : (int)Math.Round(travel * (clamped / (double)maxOffset), MidpointRounding.AwayFromZero);
        return new Rectangle(0, Math.Clamp(y, 0, travel), track, height);
    }

    internal void SetContent(Control content)
    {
        if (ReferenceEquals(_content, content)) return;

        if (_content is not null)
        {
            _content.SizeChanged -= OnContentSizeChanged;
            Controls.Remove(_content);
        }

        _content = content;
        content.SizeChanged += OnContentSizeChanged;
        Controls.Add(content);
        _offset = 0;
        Arrange();
    }

    internal void ScrollBy(int delta) => SetOffset(_offset + delta);

    internal void SetOffset(int offset)
    {
        var clamped = Math.Clamp(offset, 0, MaxOffset(ContentHeight, ViewportHeight));
        if (clamped == _offset) return;
        _offset = clamped;
        Arrange();
    }

    internal void ApplyTheme()
    {
        BackColor = UiTheme.Window;
        Invalidate();
    }

    /// <summary>
    /// 把光标下的滚轮交给本控件。坐标是屏幕坐标：消息里的 HWND 属于"焦点窗口"，
    /// 不一定是鼠标所在的窗口，只有坐标能说明用户正指着哪里。
    /// </summary>
    internal bool TryHandleWheel(int screenX, int screenY, int delta)
    {
        if (delta == 0 || !IsOverflowing || !IsHandleCreated || !Visible) return false;
        // ComboBox 的原生列表 popup 不在 WinForms 子控件树里，但会覆盖页面坐标。
        // 下拉打开期间放行滚轮，让系统列表自己滚动，避免页面移动后 popup 留在旧位置。
        if (_content is not null && ContainsOpenDropDown(_content)) return false;
        if (!RectangleToScreen(ClientRectangle).Contains(screenX, screenY)) return false;

        // 一个刻度是 120；每格滚多少行由系统设置决定（0 表示整页，这里按一行处理）。
        var lines = SystemInformation.MouseWheelScrollLines <= 0 ? 1 : SystemInformation.MouseWheelScrollLines;
        ScrollBy(-delta * lines / 120 * LineHeight);
        return true;
    }

    private static bool ContainsOpenDropDown(Control control)
    {
        if (control is ComboBox combo && combo.DroppedDown) return true;
        foreach (Control child in control.Controls)
            if (ContainsOpenDropDown(child)) return true;
        return false;
    }

    bool IMessageFilter.PreFilterMessage(ref Message m)
    {
        if (m.Msg != WmMouseWheel) return false;
        var packed = m.LParam.ToInt32();
        var x = (short)(packed & 0xFFFF);
        var y = (short)((packed >> 16) & 0xFFFF);
        var delta = (short)((long)m.WParam >> 16 & 0xFFFF);
        return TryHandleWheel(x, y, delta);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var screen = PointToScreen(e.Location);
        TryHandleWheel(screen.X, screen.Y, e.Delta);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Arrange();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!IsOverflowing) return;

        var thumb = ThumbBounds;
        if (thumb.IsEmpty) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // 轨道只是"这里还能滚"的暗示，所以画得很细；承担信息的是指示条本身。
        // 与指示条同色时不画（高对比度下两者都是纯白），见 ShowsRail。
        if (ShowsRail)
        {
            var railWidth = Math.Max(2, thumb.Width / 3);
            var rail = new Rectangle(thumb.X + (thumb.Width - railWidth) / 2, 0, railWidth, ViewportHeight);
            using var railBrush = new SolidBrush(TrackColor);
            using var railPath = Rounded(rail, Math.Max(1, railWidth / 2));
            e.Graphics.FillPath(railBrush, railPath);
        }

        using (var brush = new SolidBrush(ThumbColor))
        using (var path = Rounded(thumb, Math.Max(2, thumb.Width / 2)))
            e.Graphics.FillPath(brush, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Application.RemoveMessageFilter(this);
        base.Dispose(disposing);
    }

    private void OnContentSizeChanged(object? sender, EventArgs e) => Arrange();

    private void Arrange()
    {
        if (_arranging || _content is null) return;
        _arranging = true;
        try
        {
            // 内容放得下时回到顶部，不能留下一个悬空的偏移。
            var maxOffset = MaxOffset(ContentHeight, ViewportHeight);
            if (_offset > maxOffset) _offset = maxOffset;

            var location = new Point(0, -_offset);
            if (_content.Location != location) _content.Location = location;
        }
        finally
        {
            _arranging = false;
        }

        Invalidate();
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
