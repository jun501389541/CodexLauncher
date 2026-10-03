using System.Drawing.Drawing2D;
using System.ComponentModel;
using CodexLauncher.Core;

namespace CodexLauncher.App;

/// <summary>
/// 主题化的界面配色。调色板由 <see cref="ThemeManager"/> 解析后经 <see cref="Use"/> 注入，
/// 所有控件通过 <see cref="Style"/> 从旧调色板重映射到新调色板，因此切换主题不需要重建界面。
/// 高对比度模式下直接采用系统颜色。
/// </summary>
internal static class UiTheme
{
    private static ThemePalette _palette = ThemePalette.Light;
    private static bool _useSystemColors;
    private static Dictionary<int, int> _backgroundMap = BuildBackgroundMap(ThemePalette.Light, ThemePalette.Light);
    private static Dictionary<int, int> _foregroundMap = BuildForegroundMap(ThemePalette.Light, ThemePalette.Light);

    public static ThemePalette Palette => _palette;

    public static bool UseSystemColors => _useSystemColors;

    public static Color Window => From(_palette.Window);
    public static Color Card => From(_palette.Card);
    public static Color Border => From(_palette.Border);
    public static Color Text => From(_palette.Text);
    public static Color Muted => From(_palette.Muted);
    public static Color Primary => From(_palette.Primary);
    public static Color PrimaryHover => From(_palette.PrimaryButton.Hover);
    public static Color OnPrimary => From(_palette.OnPrimary);
    public static Color AccentText => From(_palette.AccentText);
    public static Color Green => From(_palette.SuccessText);
    public static Color Yellow => From(_palette.WarningText);
    public static Color Red => From(_palette.ErrorText);
    public static Color Checking => From(_palette.NeutralText);

    public static Color SuccessText => From(_palette.SuccessText);
    public static Color SuccessSurface => From(_palette.SuccessSurface);
    public static Color WarningText => From(_palette.WarningText);
    public static Color WarningSurface => From(_palette.WarningSurface);
    public static Color ErrorText => From(_palette.ErrorText);
    public static Color ErrorSurface => From(_palette.ErrorSurface);
    public static Color NeutralText => From(_palette.NeutralText);
    public static Color NeutralSurface => From(_palette.NeutralSurface);
    public static Color PrimarySurface => From(_palette.PrimarySurface);

    /// <summary>切换调色板。之前的调色板用于把已有控件颜色重映射到新主题。</summary>
    public static void Use(ThemePalette palette, bool useSystemColors)
    {
        var effective = useSystemColors ? FromSystemColors(palette.Name) : palette;
        _backgroundMap = BuildBackgroundMap(_palette, effective);
        _foregroundMap = BuildForegroundMap(_palette, effective);
        _palette = effective;
        _useSystemColors = useSystemColors;
        RefreshMenus();
    }

    public static ThemedButton PrimaryButton(string text) => new(text, primary: true);

    public static ThemedButton SecondaryButton(string text) => new(text, primary: false);

    /// <summary>行内标签与输入框共用的上下留白；两者必须一致，否则文字会错开。</summary>
    internal const int RowPadding = 4;

    /// <summary>
    /// 状态卡元信息列的最大宽度。四行分别呈现路径、证据、耗时和更新时间；
    /// 上限只用来拦住异常长的路径名，卡片高度随实际行数自动增长。
    /// </summary>
    internal const int StatusMetaMaxWidth = 268;

    /// <summary>
    /// 行内标签。必须是 AutoSize = false：WinForms 的 Label 在 AutoSize 为真时忽略 TextAlign，
    /// 文字贴着标签顶部绘制，看起来就比右侧输入框高出一截（150% DPI 实测差 5.7px）。
    /// 同一个 Dock=Fill 的 <see cref="MainForm"/> 摘要因为显式关掉了 AutoSize 才没出问题。
    /// </summary>
    public static Label RowLabel(string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Text,
            Margin = new Padding(0, RowPadding, 0, RowPadding)
        };

        // AutoSize 关掉后标签就没有"想要的宽度"了，母表格的 AutoSize 列会按 0 算，
        // 结果把文字裁掉（150% DPI 实测「当前本地 HTTP 入口」需要 173px，只给了 100px）。
        // 所以先量一次真实宽度再关 AutoSize，并用 MinimumSize 把它钉住。
        var preferred = label.PreferredSize;
        label.AutoSize = false;
        label.MinimumSize = new Size(preferred.Width + 1, 0);
        label.Dock = DockStyle.Fill;
        return label;
    }

    /// <summary>行高由输入框自身高度加留白推出，写死数值会在其它 DPI 下重新错开。</summary>
    public static int RowHeight(int preferredInputHeight) => preferredInputHeight + RowPadding * 2;

    /// <summary>
    /// 可换行的内容面板。
    /// <para>
    /// 为什么必须重写：上层 AutoSize 容器问首选尺寸时常常给"无宽度约束"的 <c>(0,0)</c>，
    /// <c>FlowLayoutPanel</c> 在这个约束下按<b>不换行</b>报高度——真机 150% DPI 下展开后的
    /// 高级设置面板换行后实际是 112px，报出来只有 52px，母卡片照这个数字只长了 56px，
    /// 底部自启勾选框被裁掉 31px。这里把可用宽度补进去再量，上层才拿得到真实高度。
    /// </para>
    /// </summary>
    internal sealed class WrapPanel : FlowLayoutPanel
    {
        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = proposedSize.Width > 0 ? proposedSize.Width : Parent?.ClientSize.Width ?? 0;
            return width > 0 ? base.GetPreferredSize(new Size(width, 0)) : base.GetPreferredSize(proposedSize);
        }
    }

    /// <summary>把当前主题应用到单个控件。</summary>
    public static void Style(Control control)
    {
        switch (control)
        {
            case ThemedButton button:
                button.ApplyFace(button.Primary ? _palette.PrimaryButton : _palette.SecondaryButton, _useSystemColors);
                return;
            // 菜单（含子菜单的下拉画布）不在任何窗体的控件树里，ThemeManager.Walk 走不到它，
            // 只能自己登记，等调色板变化时由 RefreshMenus 重新上色。
            // 托盘/悬浮窗菜单与其子菜单（ToolStripDropDownMenu）都不在任何窗体的控件树里，
            // ThemeManager.Walk 走不到：必须自己登记，否则深色模式下子菜单停在浅色底 + 浅色字。
            case ToolStripDropDown menu:
                ApplyMenuColors(menu);
                RememberMenu(menu);
                return;
            case RoundedPanel panel:
                panel.BorderColor = Remap(panel.BorderColor, _backgroundMap);
                break;
            case StatusDot dot:
                dot.Invalidate();
                return;
            case QuotaBarRow row:
                row.ApplyTheme();
                return;
            case QuotaBar bar:
                bar.ApplyTheme();
                return;
            case ScrollHost scroll:
                scroll.ApplyTheme();
                return;
            case ThemeSelector selector:
                selector.ApplyTheme();
                return;
        }

        control.BackColor = Remap(control.BackColor, _backgroundMap);
        control.ForeColor = Remap(control.ForeColor, _foregroundMap);
    }

    private static readonly ThemedMenuRenderer MenuRenderer = new();

    // 弱引用：菜单可能先于主题存活期被释放（悬浮窗关闭会重建右键菜单），
    // 强引用会让已释放的菜单一直留在表里，也会拖住它们的内存。
    private static readonly List<WeakReference<ToolStripDropDown>> Menus = [];

    private static void ApplyMenuColors(ToolStripDropDown menu)
    {
        menu.BackColor = Card;
        menu.ForeColor = Text;
        menu.Renderer = MenuRenderer;
    }

    private static void RememberMenu(ToolStripDropDown menu)
    {
        foreach (var reference in Menus)
        {
            if (reference.TryGetTarget(out var known) && ReferenceEquals(known, menu)) return;
        }

        Menus.Add(new WeakReference<ToolStripDropDown>(menu));
    }

    /// <summary>调色板变化后重新给已登记且仍存活的菜单上色。</summary>
    private static void RefreshMenus()
    {
        for (var index = Menus.Count - 1; index >= 0; index--)
        {
            if (!Menus[index].TryGetTarget(out var menu))
            {
                Menus.RemoveAt(index);
                continue;
            }

            if (menu.IsDisposed) continue;
            ApplyMenuColors(menu);
        }
    }

    private static Color Remap(Color color, Dictionary<int, int> map) =>
        map.TryGetValue(color.ToArgb() & 0xFFFFFF, out var mapped) ? From(mapped) : color;

    private static Color From(int argb) => Color.FromArgb(unchecked((int)(0xFF000000 | (uint)argb)));

    private static Dictionary<int, int> BuildBackgroundMap(ThemePalette from, ThemePalette to) => Pairs(
        (from.Window, to.Window),
        (from.Card, to.Card),
        (from.Border, to.Border),
        (from.Primary, to.Primary),
        (from.SuccessSurface, to.SuccessSurface),
        (from.WarningSurface, to.WarningSurface),
        (from.ErrorSurface, to.ErrorSurface),
        (from.NeutralSurface, to.NeutralSurface),
        (from.PrimarySurface, to.PrimarySurface),
        (from.PrimaryButton.Background, to.PrimaryButton.Background),
        (from.PrimaryButton.Hover, to.PrimaryButton.Hover),
        (from.PrimaryButton.Pressed, to.PrimaryButton.Pressed),
        (from.PrimaryButton.DisabledBackground, to.PrimaryButton.DisabledBackground),
        (from.SecondaryButton.Background, to.SecondaryButton.Background),
        (from.SecondaryButton.Hover, to.SecondaryButton.Hover),
        (from.SecondaryButton.Pressed, to.SecondaryButton.Pressed),
        (from.SecondaryButton.DisabledBackground, to.SecondaryButton.DisabledBackground));

    private static Dictionary<int, int> BuildForegroundMap(ThemePalette from, ThemePalette to) => Pairs(
        (from.Text, to.Text),
        (from.Muted, to.Muted),
        (from.OnPrimary, to.OnPrimary),
        (from.AccentText, to.AccentText),
        (from.SuccessText, to.SuccessText),
        (from.WarningText, to.WarningText),
        (from.ErrorText, to.ErrorText),
        (from.NeutralText, to.NeutralText),
        (from.Primary, to.Primary),
        (from.Border, to.Border),
        (from.PrimaryButton.Foreground, to.PrimaryButton.Foreground),
        (from.PrimaryButton.DisabledText, to.PrimaryButton.DisabledText),
        (from.SecondaryButton.Foreground, to.SecondaryButton.Foreground),
        (from.SecondaryButton.DisabledText, to.SecondaryButton.DisabledText));

    private static Dictionary<int, int> Pairs(params (int From, int To)[] pairs)
    {
        var map = new Dictionary<int, int>();
        foreach (var (from, to) in pairs) map[from & 0xFFFFFF] = to & 0xFFFFFF;
        return map;
    }

    /// <summary>高对比度模式：改用系统颜色，保证系统主题与可访问性设置优先。</summary>
    private static ThemePalette FromSystemColors(string name) => ThemePalette.HighContrast with
    {
        Name = name,
        Window = Argb(SystemColors.Window),
        Card = Argb(SystemColors.Window),
        Border = Argb(SystemColors.WindowFrame),
        Text = Argb(SystemColors.WindowText),
        Muted = Argb(SystemColors.WindowText),
        Primary = Argb(SystemColors.Highlight),
        OnPrimary = Argb(SystemColors.HighlightText),
        AccentText = Argb(SystemColors.HotTrack),
        SuccessText = Argb(SystemColors.WindowText),
        SuccessSurface = Argb(SystemColors.Window),
        WarningText = Argb(SystemColors.WindowText),
        WarningSurface = Argb(SystemColors.Window),
        ErrorText = Argb(SystemColors.WindowText),
        ErrorSurface = Argb(SystemColors.Window),
        NeutralText = Argb(SystemColors.WindowText),
        NeutralSurface = Argb(SystemColors.Window),
        // 高对比度不使用带色调的强调底色，避免与系统配色冲突。
        PrimarySurface = Argb(SystemColors.Window),
        PrimaryButton = new ButtonFace(
            Background: Argb(SystemColors.Highlight),
            Foreground: Argb(SystemColors.HighlightText),
            Hover: Argb(SystemColors.HotTrack),
            Pressed: Argb(SystemColors.ControlDark),
            DisabledBackground: Argb(SystemColors.Control),
            DisabledText: Argb(SystemColors.GrayText),
            Focus: Argb(SystemColors.WindowText)),
        SecondaryButton = new ButtonFace(
            Background: Argb(SystemColors.Control),
            Foreground: Argb(SystemColors.ControlText),
            Hover: Argb(SystemColors.ControlLight),
            Pressed: Argb(SystemColors.ControlDark),
            DisabledBackground: Argb(SystemColors.Control),
            DisabledText: Argb(SystemColors.GrayText),
            Focus: Argb(SystemColors.WindowText))
    };

    private static int Argb(Color color) => color.ToArgb() & 0xFFFFFF;
}

/// <summary>
/// 右键菜单与托盘菜单的主题配色。颜色在渲染时从 <see cref="UiTheme"/> 读取，
/// 因此切换主题后菜单不需要重建，勾选标记在深色下也保持可见。
/// </summary>
internal sealed class ThemedColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => UiTheme.Card;
    public override Color MenuItemSelected => UiTheme.PrimarySurface;
    public override Color MenuItemSelectedGradientBegin => UiTheme.PrimarySurface;
    public override Color MenuItemSelectedGradientEnd => UiTheme.PrimarySurface;
    public override Color MenuItemPressedGradientBegin => UiTheme.Card;
    public override Color MenuItemPressedGradientMiddle => UiTheme.Card;
    public override Color MenuItemPressedGradientEnd => UiTheme.Card;
    public override Color MenuItemBorder => UiTheme.Primary;
    public override Color MenuBorder => UiTheme.Border;
    public override Color ImageMarginGradientBegin => UiTheme.Card;
    public override Color ImageMarginGradientMiddle => UiTheme.Card;
    public override Color ImageMarginGradientEnd => UiTheme.Card;
    public override Color SeparatorDark => UiTheme.Border;
    public override Color SeparatorLight => UiTheme.Border;
}

internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    internal ThemedMenuRenderer() : base(new ThemedColorTable()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // 禁用项也要可读：用次要文字色而不是系统灰。
        e.TextColor = e.Item.Enabled ? UiTheme.Text : UiTheme.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var bounds = e.ImageRectangle;
        var x = bounds.Left + bounds.Width / 2f;
        var y = bounds.Top + bounds.Height / 2f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(UiTheme.AccentText, 2f);
        e.Graphics.DrawLines(pen, [
            new PointF(x - 5, y),
            new PointF(x - 1, y + 4),
            new PointF(x + 5, y - 5)
        ]);
    }
}

/// <summary>
/// 主题化按钮：明确正常、悬停、按下、禁用和键盘焦点状态的文字与背景颜色。
/// 禁用态使用独立配色而不是让系统把文字涂灰到不可读。
/// </summary>
internal sealed class ThemedButton : Button
{
    internal ThemedButton(string text, bool primary)
    {
        Primary = primary;
        Text = text;
        AutoSize = true;
        MinimumSize = new Size(0, 42);
        Padding = new Padding(16, 5, 16, 5);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        ApplyFace(primary ? UiTheme.Palette.PrimaryButton : UiTheme.Palette.SecondaryButton, UiTheme.UseSystemColors);
    }

    internal bool Primary { get; private set; }

    internal ButtonFace Face { get; private set; } = ThemePalette.Light.PrimaryButton;

    internal bool UseSystemColors { get; private set; }

    /// <summary>切换主/次按钮外观。主题选择器用它表示"当前选中项"：选中态用主色面。</summary>
    internal void SetPrimary(bool primary)
    {
        Primary = primary;
        ApplyFace(primary ? UiTheme.Palette.PrimaryButton : UiTheme.Palette.SecondaryButton, UiTheme.UseSystemColors);
    }

    internal void ApplyFace(ButtonFace face, bool useSystemColors)
    {
        Face = face;
        UseSystemColors = useSystemColors;
        RefreshVisual();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        RefreshVisual();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        RefreshVisual();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        RefreshVisual();
    }

    private void RefreshVisual()
    {
        // 禁用态：明确的浅底深字，保证“禁用操作也须可读”。
        BackColor = Enabled ? From(Face.Background) : From(Face.DisabledBackground);
        ForeColor = Enabled ? From(Face.Foreground) : From(Face.DisabledText);
        FlatAppearance.BorderColor = Enabled
            ? (Focused ? From(Face.Focus) : From(Primary ? Face.Background : Face.Pressed))
            : From(Face.DisabledBackground);
        FlatAppearance.BorderSize = 1;
        FlatAppearance.MouseOverBackColor = Enabled ? From(Face.Hover) : BackColor;
        FlatAppearance.MouseDownBackColor = Enabled ? From(Face.Pressed) : BackColor;
        Invalidate();
    }

    private static Color From(int argb) => Color.FromArgb(unchecked((int)(0xFF000000 | (uint)argb)));
}

internal sealed class RoundedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int CornerRadius { get; set; } = 16;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color BorderColor { get; set; } = UiTheme.Border;

    public RoundedPanel()
    {
        DoubleBuffered = true;
        BackColor = UiTheme.Card;
        Padding = new Padding(1);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        using var path = RoundedRectangle(ClientRectangle, CornerRadius);
        Region?.Dispose();
        Region = new Region(path);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = ClientRectangle;
        rectangle.Width -= 1;
        rectangle.Height -= 1;
        using var path = RoundedRectangle(rectangle, CornerRadius);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
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

/// <summary>
/// 额度条：按剩余百分比填充的细长进度条，外观对齐 Codex 官方额度面板。
/// 颜色全部在绘制时实时读取 <see cref="UiTheme"/>，所以切换主题不需要重建控件
/// （额度行是运行期新建的，走不到构造期的主题应用，这里必须自己保证）。
/// </summary>
internal sealed class QuotaBar : Control
{
    private const int BaseBarHeight = 6;
    private const int BaseDpi = 96;
    private int? _remainingPercent;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int? RemainingPercent
    {
        get => _remainingPercent;
        set { _remainingPercent = value; AccessibleName = DescribeAccessible(); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color FillColor => UiTheme.AccentText;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color TrackColor => UiTheme.Border;

    public QuotaBar()
    {
        DoubleBuffered = true;
        BackColor = UiTheme.Card;
        Size = new Size(120, BaseBarHeight);
        AccessibleRole = AccessibleRole.ProgressBar;
        AccessibleName = DescribeAccessible();
    }

    /// <summary>填充长度按剩余百分比线性换算；未知百分比不画填充，也不画满。</summary>
    internal static int FillWidth(int trackWidth, int? percent)
    {
        if (trackWidth <= 0 || percent is null) return 0;
        var clamped = Math.Clamp(percent.Value, 0, 100);
        return (int)Math.Round(trackWidth * (clamped / 100.0), MidpointRounding.AwayFromZero);
    }

    /// <summary>条高随 DPI 缩放：96 DPI 下 6 像素，150% 下 9 像素（与 Codex 参考截图一致）。</summary>
    internal static int BarHeight(int dpi) =>
        Math.Max(4, (int)Math.Round(BaseBarHeight * (dpi / (double)BaseDpi), MidpointRounding.AwayFromZero));

    internal void ApplyTheme()
    {
        BackColor = UiTheme.Card;
        AccessibleName = DescribeAccessible();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var width = Width;
        var height = Math.Max(1, Height);
        if (width <= 0) return;

        var track = TrackColor;
        var fill = FillColor;
        using (var path = Pill(0, width, height))
        using (var brush = new SolidBrush(track))
            e.Graphics.FillPath(brush, path);

        var filled = FillWidth(width, _remainingPercent);
        if (filled <= 0) return;
        using (var path = Pill(0, filled, height))
        using (var brush = new SolidBrush(fill))
            e.Graphics.FillPath(brush, path);

        // 高对比度等配色下填充与轨道亮度接近，补一圈描边，保证“用了多少”仍然看得见。
        if (Contrast(fill, track) < 3.0)
        {
            using var pen = new Pen(UiTheme.Card);
            using var path = Pill(0, filled, height);
            e.Graphics.DrawPath(pen, path);
        }
    }

    private static GraphicsPath Pill(int left, int width, int height)
    {
        var radius = Math.Max(1, Math.Min(width, height) / 2);
        var diameter = radius * 2;
        var bounds = new Rectangle(left, 0, Math.Max(diameter, width), Math.Max(diameter, height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static double Contrast(Color first, Color second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        var (high, low) = a >= b ? (a, b) : (b, a);
        return (high + 0.05) / (low + 0.05);
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(int value)
    {
        var channel = value / 255.0;
        return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private string DescribeAccessible() =>
        _remainingPercent is null ? "额度剩余未知" : $"额度剩余 {_remainingPercent}%";
}

/// <summary>
/// 一行额度：标题（窗口长度）＋左侧重置倒计时＋右侧剩余百分比，下面一条 <see cref="QuotaBar"/>。
/// 全部自绘并实时读主题，避免出现“构造时把浅色烤死、切到深色就看不见”的同类缺陷。
/// </summary>
internal sealed class QuotaBarRow : Panel
{
    private readonly Font _titleFont = new("Microsoft YaHei UI", 10F, FontStyle.Bold);
    private readonly Font _bodyFont = new("Microsoft YaHei UI", 9F);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal QuotaBar Bar { get; } = new();

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string TitleText { get; private set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string CountdownText { get; private set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string RemainingText { get; private set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Font TitleFont => _titleFont;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color TitleColor => UiTheme.Text;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color MutedColor => UiTheme.Muted;

    public QuotaBarRow()
    {
        DoubleBuffered = true;
        BackColor = UiTheme.Card;
        Margin = new Padding(0, 0, 0, 10);
        AccessibleRole = AccessibleRole.Grouping;
        Controls.Add(Bar);
        Arrange();
    }

    /// <summary>用一条额度窗口刷新这一行；未知百分比与未知重置时间都原样表达，不伪造成 0% 或 100%。</summary>
    internal void Render(QuotaWindow window, string title, DateTimeOffset now)
    {
        TitleText = title;
        CountdownText = window.ResetCountdownLabel(now);
        RemainingText = window.RemainingSummary;
        Bar.RemainingPercent = window.RemainingPercent;
        AccessibleName = $"{title}，{RemainingText}，{CountdownText}";
        Arrange();
        Invalidate();
    }

    internal void ApplyTheme()
    {
        BackColor = UiTheme.Card;
        ForeColor = UiTheme.Text;
        Bar.ApplyTheme();
        Invalidate(true);
    }

    /// <summary>行高只由字体行高与条高决定，与宽度无关，所以父容器量算时不会出现循环依赖。</summary>
    public override Size GetPreferredSize(Size proposedSize) =>
        new(proposedSize.Width > 0 ? proposedSize.Width : Width, RowHeight(DeviceDpi));

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Arrange();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Arrange();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (TitleText.Length > 0)
        {
            using var brush = new SolidBrush(TitleColor);
            e.Graphics.DrawString(TitleText, _titleFont, brush, 0, 0);
        }

        var bodyTop = BodyTop;
        if (CountdownText.Length > 0)
        {
            using var brush = new SolidBrush(MutedColor);
            e.Graphics.DrawString(CountdownText, _bodyFont, brush, 0, bodyTop);
        }

        if (RemainingText.Length > 0)
        {
            var size = e.Graphics.MeasureString(RemainingText, _bodyFont);
            using var brush = new SolidBrush(TitleColor);
            e.Graphics.DrawString(RemainingText, _bodyFont, brush, Math.Max(0f, Width - size.Width), bodyTop);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _bodyFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private float BodyTop => _titleFont.Height + 4f;

    private int BarTop => _titleFont.Height + 4 + _bodyFont.Height + 8;

    private int RowHeight(int dpi) => BarTop + QuotaBar.BarHeight(dpi) + 2;

    /// <summary>标题 → 副行 → 进度条：条贴着行底，宽度跟随行宽。</summary>
    private void Arrange()
    {
        var barHeight = QuotaBar.BarHeight(DeviceDpi);
        var height = RowHeight(DeviceDpi);
        Bar.SetBounds(0, height - barHeight - 2, Math.Max(0, ClientSize.Width), barHeight);
        if (Height != height) Height = height;
    }
}

/// <summary>
/// 状态圆点。同时使用符号和文字（<see cref="Control.AccessibleName"/> 与旁边的标题文字），
/// 不依赖颜色单独表达状态。
/// </summary>
internal sealed class StatusDot : Control
{
    private RuntimeHealthState _state = RuntimeHealthState.Checking;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal RuntimeHealthState State
    {
        get => _state;
        set { _state = value; AccessibleName = LabelFor(value); Invalidate(); }
    }

    public StatusDot()
    {
        DoubleBuffered = true;
        Size = new Size(64, 64);
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = LabelFor(_state);
    }

    /// <summary>启动前诊断使用同一套五色语义。</summary>
    internal static RuntimeHealthState FromAccess(AccessHealthState state) => state switch
    {
        AccessHealthState.Available => RuntimeHealthState.Stable,
        AccessHealthState.NeedsVerification => RuntimeHealthState.Fluctuating,
        AccessHealthState.Unavailable => RuntimeHealthState.Down,
        _ => RuntimeHealthState.Checking
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = State switch
        {
            RuntimeHealthState.Stable => UiTheme.Green,
            RuntimeHealthState.Confirming or RuntimeHealthState.Fluctuating => UiTheme.Yellow,
            RuntimeHealthState.Down => UiTheme.Red,
            RuntimeHealthState.NotRunning => UiTheme.Muted,
            _ => UiTheme.Checking
        };
        var rectangle = new Rectangle(5, 5, Math.Min(Width, Height) - 10, Math.Min(Width, Height) - 10);
        using var brush = new SolidBrush(color);
        e.Graphics.FillEllipse(brush, rectangle);
        // 符号在深色主题下必须仍然可见，因此按背景亮度选择前景色。
        using var pen = new Pen(Readable(color), 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (State)
        {
            case RuntimeHealthState.Stable:
                e.Graphics.DrawLines(pen,
                [
                    new Point(rectangle.Left + 14, rectangle.Top + 27),
                    new Point(rectangle.Left + 23, rectangle.Top + 36),
                    new Point(rectangle.Left + 40, rectangle.Top + 18)
                ]);
                break;
            case RuntimeHealthState.Confirming:
            case RuntimeHealthState.Fluctuating:
                e.Graphics.DrawLine(pen, rectangle.Left + 27, rectangle.Top + 14, rectangle.Left + 27, rectangle.Top + 31);
                e.Graphics.DrawLine(pen, rectangle.Left + 27, rectangle.Top + 40, rectangle.Left + 27, rectangle.Top + 40);
                break;
            case RuntimeHealthState.Down:
                e.Graphics.DrawLine(pen, rectangle.Left + 18, rectangle.Top + 18, rectangle.Left + 36, rectangle.Top + 36);
                e.Graphics.DrawLine(pen, rectangle.Left + 36, rectangle.Top + 18, rectangle.Left + 18, rectangle.Top + 36);
                break;
            case RuntimeHealthState.NotRunning:
                // 未启动用横杠表示，避免与“异常”的叉号混淆。
                e.Graphics.DrawLine(pen, rectangle.Left + 18, rectangle.Top + 27, rectangle.Left + 36, rectangle.Top + 27);
                break;
            default:
                using (var small = new SolidBrush(Readable(color)))
                    e.Graphics.FillEllipse(small, rectangle.Left + 23, rectangle.Top + 23, 8, 8);
                break;
        }
    }

    private static Color Readable(Color background)
    {
        var luminance = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255.0;
        return luminance > 0.55 ? Color.Black : Color.White;
    }

    private static string LabelFor(RuntimeHealthState state) => state switch
    {
        RuntimeHealthState.Stable => "绿色：线路稳定",
        RuntimeHealthState.Confirming => "黄色：正在确认线路",
        RuntimeHealthState.Fluctuating => "黄色：网络波动",
        RuntimeHealthState.Down => "红色：连接异常，影响使用",
        RuntimeHealthState.NotRunning => "灰色：Codex 未启动",
        _ => "正在检测"
    };
}

/// <summary>
/// 主题选择器：三个按钮表示跟随系统 / 浅色 / 深色，当前项用主色面。
/// <para>
/// 从托盘菜单搬进高级设置，是为了让"主题"只有一个真相来源：托盘和高级设置各留一套开关时，
/// 漏同步就会出现"改了没生效"，而托盘菜单本身在深色主题下还额外需要额外维护配色。
/// </para>
/// <para>
/// 颜色实时读 <see cref="UiTheme"/>（选中态由 <see cref="ThemedButton.SetPrimary"/> 决定），
/// 所以主题切换不需要重建控件，也不会像深色菜单那样把颜色烤死在构造期。
/// </para>
/// </summary>
internal sealed class ThemeSelector : FlowLayoutPanel
{
    private readonly List<ThemedButton> _buttons;
    private ThemeMode _mode = ThemeMode.System;
    private bool _syncing;

    public ThemeSelector()
    {
        DoubleBuffered = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = false;
        BackColor = UiTheme.Card;
        Margin = new Padding(0, 4, 0, 0);

        _buttons =
        [
            MakeButton("跟随系统", ThemeMode.System),
            MakeButton("浅色", ThemeMode.Light),
            MakeButton("深色", ThemeMode.Dark)
        ];
        Controls.AddRange([.. _buttons]);
        RefreshSelection();
    }

    /// <summary>用户在界面上选择主题时触发；重复选择同一项不触发。</summary>
    internal event Action<ThemeMode>? ModeChanged;

    internal IReadOnlyList<ThemedButton> Buttons => _buttons;

    /// <summary>当前选择的模式。改写请走 <see cref="SetMode"/>，以便区分"用户点选"和"外部同步"。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal ThemeMode Mode => _mode;

    /// <summary>
    /// 外部同步（读取配置、跟随系统变化后回填）。不触发 <see cref="ModeChanged"/>，
    /// 否则会把"读配置"变成"写配置"，形成回写环。
    /// </summary>
    internal void SetMode(ThemeMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        RefreshSelection();
    }

    internal void ApplyTheme()
    {
        BackColor = UiTheme.Card;
        foreach (var button in _buttons)
        {
            button.ApplyFace(
                button.Primary ? UiTheme.Palette.PrimaryButton : UiTheme.Palette.SecondaryButton,
                UiTheme.UseSystemColors);
        }
        Invalidate(true);
    }

    private ThemedButton MakeButton(string text, ThemeMode mode)
    {
        var button = UiTheme.SecondaryButton(text);
        button.AutoSize = true;
        button.Margin = new Padding(0, 0, 8, 0);
        button.Click += (_, _) => Choose(mode);
        return button;
    }

    private void Choose(ThemeMode mode)
    {
        if (_syncing || _mode == mode) return;
        _mode = mode;
        RefreshSelection();
        ModeChanged?.Invoke(mode);
    }

    private void RefreshSelection()
    {
        _syncing = true;
        try
        {
            for (var index = 0; index < _buttons.Count; index++)
            {
                var selected = (ThemeMode)index == _mode;
                if (_buttons[index].Primary != selected) _buttons[index].SetPrimary(selected);
            }
        }
        finally
        {
            _syncing = false;
        }
    }
}
