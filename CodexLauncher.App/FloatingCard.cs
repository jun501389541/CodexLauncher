using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using CodexLauncher.Core;

namespace CodexLauncher.App;

/// <summary>
/// 悬浮窗卡片的几何与绘制。抽成纯函数是为了能在没有屏幕的测试进程里断言两件历史上真出过事的事：
/// ①尺寸必须按字体度量 × DPI 计算——旧实现把客户区写死成 228x60，150% 缩放下第二行文字被裁掉；
/// ②半透明卡片与任意桌面背景合成后，正文与副行仍要满足 WCAG 4.5:1——系统亚克力实测只有 1.04:1。
/// 颜色全部在绘制时实时读 <see cref="UiTheme"/>，不缓存，避免“构造时把浅色烤死”的同类缺陷。
/// </summary>
internal static class FloatingCard
{
    private const int BaseDpi = 96;
    private const int BasePaddingX = 12;
    private const int BasePaddingY = 8;
    private const int BaseGap = 2;
    private const int BaseIconColumn = 26;
    private const int BaseIcon = 16;
    private const int BaseInset = 4;
    private const int BaseMinimumWidth = 160;
    private const int BaseMinimumHeight = 48;
    private const int BaseRadius = 14;
    private const int BaseShadow = 8;

    /// <summary>
    /// 卡片填充的不透明度（0-255）。透明度越低越像玻璃，正文对比度也越低：
    /// 浅色主题的副行色 0x5B6472 压到 224 以下就会跌破 4.5:1（见 TestFloatingCardContrast）。
    /// </summary>
    internal const int CardAlpha = 236;

    private const int BorderAlpha = 168;

    /// <summary>
    /// 实际生效的填充不透明度。高对比度（系统颜色）下必须完全不透明——任何透底都会把系统配色冲淡，
    /// 也会让「文字色由系统决定」这条契约失效。
    /// </summary>
    internal static int AlphaFor(ThemePalette palette, bool useSystemColors) =>
        useSystemColors || palette.Name == ThemePalette.HighContrast.Name ? 255 : CardAlpha;

    // 量算与绘制共用同一份 StringFormat，否则 MeasureString 与 DrawString 会各自加上不同的内边距，
    // 出现“算出来的高度装不下实际画出来的文字”——那正是这次要修的病。
    private static readonly StringFormat Tight = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap,
        Trimming = StringTrimming.None,
        HotkeyPrefix = HotkeyPrefix.None
    };

    internal static int Scale(int logical, int dpi) =>
        (int)Math.Round(logical * (dpi / (double)BaseDpi), MidpointRounding.AwayFromZero);

    /// <summary>
    /// 窗口客户区尺寸 = 内边距 + 图标列 + 两行文字（取较宽者），再取最小尺寸，最后加一圈投影留白。
    /// </summary>
    internal static Size CardSize(Size stateSize, Size routeSize, int dpi)
    {
        var textWidth = Math.Max(stateSize.Width, routeSize.Width);
        var contentWidth = Scale(BasePaddingX, dpi) * 2 + Scale(BaseIconColumn, dpi) + Scale(BaseInset, dpi) + textWidth;
        var contentHeight = Scale(BasePaddingY, dpi) * 2 + stateSize.Height + Scale(BaseGap, dpi) + routeSize.Height;
        var shadow = Scale(BaseShadow, dpi);
        return new Size(
            Math.Max(Scale(BaseMinimumWidth, dpi), contentWidth) + shadow * 2,
            Math.Max(Scale(BaseMinimumHeight, dpi), contentHeight) + shadow * 2);
    }

    /// <summary>投影留白之内的卡片矩形；文字与图标都排在这个矩形里。</summary>
    internal static Rectangle CardBounds(Size size, int dpi)
    {
        var shadow = Scale(BaseShadow, dpi);
        return new Rectangle(
            shadow,
            shadow,
            Math.Max(1, size.Width - shadow * 2),
            Math.Max(1, size.Height - shadow * 2));
    }

    internal static Size Measure(Graphics graphics, string stateText, string routeText, Font stateFont, Font routeFont, int dpi) =>
        CardSize(MeasureText(graphics, stateText, stateFont), MeasureText(graphics, routeText, routeFont), dpi);

    /// <summary>卡片填充：主题卡片色 + 该主题对应的不透明度。透明才能透出桌面，主题色保证与当前主题一致。</summary>
    internal static Color Surface(ThemePalette palette, bool useSystemColors = false) =>
        Color.FromArgb(AlphaFor(palette, useSystemColors), Rgb(palette.Card));

    internal static Color BorderColor(ThemePalette palette, bool useSystemColors = false) =>
        Color.FromArgb(useSystemColors ? 255 : BorderAlpha, Rgb(palette.Border));

    internal static Color TextColor(ThemePalette palette) => Rgb(palette.Text);

    internal static Color MutedColor(ThemePalette palette) => Rgb(palette.Muted);

    /// <summary>把半透明卡片与桌面背景合成，得到实际看到的底色；测试用它验证最坏背景下的可读性。</summary>
    internal static Color Composite(Color surface, Color backdrop)
    {
        var alpha = surface.A / 255.0;
        return Color.FromArgb(255,
            (int)Math.Round(surface.R * alpha + backdrop.R * (1 - alpha)),
            (int)Math.Round(surface.G * alpha + backdrop.G * (1 - alpha)),
            (int)Math.Round(surface.B * alpha + backdrop.B * (1 - alpha)));
    }

    /// <summary>把整张卡片画进 32 位 ARGB 位图：投影 → 圆角填充 → 描边 → 顶部高光 → 图标 → 两行文字。
    /// 返回两行文字实际占据的矩形，供调用方与测试核对“第二行有没有被裁掉”。</summary>
    internal static (Rectangle State, Rectangle Route) Paint(
        Graphics graphics,
        Size size,
        string stateText,
        string routeText,
        RuntimeHealthState state,
        Font stateFont,
        Font routeFont,
        int dpi,
        bool transparentBackground = true)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        // 分层窗口要真透明，普通窗口回退路径则不能把客户区清成黑色。
        if (transparentBackground) graphics.Clear(Color.Transparent);

        var shadow = Scale(BaseShadow, dpi);
        var radius = Scale(BaseRadius, dpi);
        var card = CardBounds(size, dpi);
        var palette = UiTheme.Palette;
        var useSystemColors = UiTheme.UseSystemColors;

        // 柔和外投影：由外向内叠同心圆角矩形，alpha 逐层累加，避免额外的模糊实现。
        for (var step = shadow; step >= 1; step--)
        {
            var alpha = 4 + (int)Math.Round(18.0 * (shadow - step) / Math.Max(1, shadow));
            using var ring = Rounded(Rectangle.Inflate(card, step, step), radius + step);
            using var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
            graphics.FillPath(brush, ring);
        }

        using (var path = Rounded(card, radius))
        {
            using (var brush = new SolidBrush(Surface(palette, useSystemColors)))
                graphics.FillPath(brush, path);
            using (var pen = new Pen(BorderColor(palette, useSystemColors), Math.Max(1, Scale(1, dpi))))
                graphics.DrawPath(pen, path);
        }

        // 玻璃高光：只在左上角点一小片，取样点（右下角内侧）不受影响。
        // 高对比度下不加高光——任何半透明装饰都会冲淡系统配色。
        if (!useSystemColors)
        {
            using var sheen = Rounded(new Rectangle(card.Left + radius / 2, card.Top + radius / 3, card.Width / 3, radius), radius / 2);
            using var brush = new SolidBrush(Color.FromArgb(20, 255, 255, 255));
            graphics.FillPath(brush, sheen);
        }

        var paddingX = Scale(BasePaddingX, dpi);
        var paddingY = Scale(BasePaddingY, dpi);
        var gap = Scale(BaseGap, dpi);
        var iconColumn = Scale(BaseIconColumn, dpi);

        // 先量出两行文字，图标才有得对齐：以前图标直接贴在第一行顶上，
        // 两行文字的块中心比它低半个图标（150% DPI 实测差 14px），看起来就是"没对齐"。
        var stateSize = stateText.Length > 0 ? MeasureText(graphics, stateText, stateFont) : Size.Empty;
        var routeSize = routeText.Length > 0 ? MeasureText(graphics, routeText, routeFont) : Size.Empty;
        var iconBounds = IconBounds(card, stateSize, routeSize, dpi);
        PaintDot(graphics, iconBounds, state);

        var textLeft = card.Left + paddingX + iconColumn + Scale(BaseInset, dpi);
        var textWidth = Math.Max(1, card.Right - paddingX - textLeft);
        var top = card.Top + paddingY;
        var stateRow = Rectangle.Empty;
        var routeRow = Rectangle.Empty;
        if (stateText.Length > 0)
        {
            stateRow = new Rectangle(textLeft, top, textWidth, Math.Max(1, stateSize.Height));
            using (var brush = new SolidBrush(TextColor(palette)))
                graphics.DrawString(stateText, stateFont, brush, stateRow, Tight);
            top += stateSize.Height + gap;
        }

        if (routeText.Length > 0)
        {
            routeRow = new Rectangle(textLeft, top, textWidth, Math.Max(1, routeSize.Height));
            using var brush = new SolidBrush(MutedColor(palette));
            graphics.DrawString(routeText, routeFont, brush, routeRow, Tight);
        }

        return (stateRow, routeRow);
    }

    /// <summary>
    /// 图标位置：垂直居中于两行文字组成的整块，而不是贴在第一行的顶边。
    /// </summary>
    public static Rectangle IconBounds(Rectangle card, Size stateSize, Size routeSize, int dpi)
    {
        var paddingX = Scale(BasePaddingX, dpi);
        var paddingY = Scale(BasePaddingY, dpi);
        var iconColumn = Scale(BaseIconColumn, dpi);
        var iconSize = Scale(BaseIcon, dpi);
        var block = stateSize.Height;
        if (stateSize.Height > 0 && routeSize.Height > 0) block += Scale(BaseGap, dpi);
        block += routeSize.Height;

        return new Rectangle(
            card.Left + paddingX + Math.Max(0, (iconColumn - iconSize) / 2),
            card.Top + paddingY + Math.Max(0, (block - iconSize) / 2),
            iconSize,
            iconSize);
    }

    /// <summary>状态圆点：颜色之外还用符号区分状态，不只依赖颜色。</summary>
    internal static void PaintDot(Graphics graphics, Rectangle bounds, RuntimeHealthState state)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = state switch
        {
            RuntimeHealthState.Stable => UiTheme.Green,
            RuntimeHealthState.Confirming or RuntimeHealthState.Fluctuating => UiTheme.Yellow,
            RuntimeHealthState.Down => UiTheme.Red,
            RuntimeHealthState.NotRunning => UiTheme.Muted,
            _ => UiTheme.Checking
        };
        var size = Math.Max(4, Math.Min(bounds.Width, bounds.Height));
        var circle = new Rectangle(bounds.Left, bounds.Top, size, size);
        using (var brush = new SolidBrush(color))
            graphics.FillEllipse(brush, circle);

        using var pen = new Pen(Readable(color), Math.Max(1F, size / 8F));
        var centre = new PointF(circle.Left + size / 2F, circle.Top + size / 2F);
        var radius = size / 2F;
        switch (state)
        {
            case RuntimeHealthState.Stable:
                graphics.DrawLines(pen,
                [
                    new PointF(centre.X - radius * 0.42F, centre.Y),
                    new PointF(centre.X - radius * 0.1F, centre.Y + radius * 0.3F),
                    new PointF(centre.X + radius * 0.45F, centre.Y - radius * 0.34F)
                ]);
                break;
            case RuntimeHealthState.Down:
                graphics.DrawLine(pen, centre.X - radius * 0.36F, centre.Y - radius * 0.36F, centre.X + radius * 0.36F, centre.Y + radius * 0.36F);
                graphics.DrawLine(pen, centre.X + radius * 0.36F, centre.Y - radius * 0.36F, centre.X - radius * 0.36F, centre.Y + radius * 0.36F);
                break;
            case RuntimeHealthState.NotRunning:
                // 未启动用横杠表示，避免与“异常”的叉号混淆。
                graphics.DrawLine(pen, centre.X - radius * 0.4F, centre.Y, centre.X + radius * 0.4F, centre.Y);
                break;
            case RuntimeHealthState.Confirming:
            case RuntimeHealthState.Fluctuating:
                graphics.DrawLine(pen, centre.X, centre.Y - radius * 0.42F, centre.X, centre.Y + radius * 0.06F);
                using (var dot = new SolidBrush(Readable(color)))
                    graphics.FillEllipse(dot, centre.X - radius * 0.14F, centre.Y + radius * 0.24F, radius * 0.28F, radius * 0.28F);
                break;
            default:
                using (var dot = new SolidBrush(Readable(color)))
                    graphics.FillEllipse(dot, centre.X - radius * 0.22F, centre.Y - radius * 0.22F, radius * 0.44F, radius * 0.44F);
                break;
        }
    }

    private static Size MeasureText(Graphics graphics, string text, Font font)
    {
        if (string.IsNullOrEmpty(text)) return Size.Empty;
        var size = graphics.MeasureString(text, font, PointF.Empty, Tight);
        return new Size((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height));
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Rgb(int argb) => Color.FromArgb(unchecked((int)(0xFF000000 | (uint)argb)));

    private static Color Readable(Color background) =>
        background.GetBrightness() > 0.55F ? Color.Black : Color.White;
}

/// <summary>
/// 分层窗口（WS_EX_LAYERED）的像素提交。悬浮窗整卡自绘成一张 32 位 ARGB 位图后由它推给合成器，
/// 因此圆角与投影是真正的抗锯齿半透明像素，而不是拿窗体背景色去“假装”圆角。
/// </summary>
internal static class LayeredSurface
{
    private const int AcSrcOver = 0;
    private const int AcSrcAlpha = 1;
    private const int UlwAlpha = 2;

    internal static bool Present(IntPtr handle, Bitmap bitmap)
    {
        if (handle == IntPtr.Zero || bitmap.Width <= 0 || bitmap.Height <= 0) return false;

        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var native = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            // 必须用 CreateDIBSection 而不是 Bitmap.GetHbitmap：后者交出来的是不带 alpha 的 DDB，
            // UpdateLayeredWindow 会把整块矩形当成不透明，圆角与投影全变成实心色块。
            var info = BitmapInfo.For(bitmap.Width, bitmap.Height);
            native = CreateDIBSection(screen, ref info, DibRgbColors, out var bits, IntPtr.Zero, 0);
            if (native == IntPtr.Zero || bits == IntPtr.Zero) return false;
            Marshal.Copy(PremultipliedBgra(bitmap), 0, bits, bitmap.Width * 4 * bitmap.Height);

            previous = SelectObject(memory, native);
            var size = new NativeSize { Width = bitmap.Width, Height = bitmap.Height };
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };
            // 位置交给 WinForms 的 Location 管，这里只提交尺寸与像素。
            // pptSrc 必须指向一个有效的 POINT。本机（Windows 10.0.26220）矩阵实验实测：
            // 传 NULL 时调用照样返回 True、GetLastError=0、GetLayeredWindowAttributes 也不报错，
            // 但合成器一个像素都不画——四窗口对照里 &POINT{0,0} 整块上屏、NULL 全屏零像素，
            // 两者返回值完全相同。所以这里不能退回 IntPtr.Zero，签名也用 ref 结构体锁死。
            var source = new NativePoint { X = 0, Y = 0 };
            return UpdateLayeredWindow(handle, screen, IntPtr.Zero, ref size, memory, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (native != IntPtr.Zero) DeleteObject(native);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>按 GDI+ 的 BGRA 行序取出像素，并转成 ULW_ALPHA 要求的自左乘格式。</summary>
    internal static byte[] PremultipliedBgra(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = bitmap.Width * 4;
            var bytes = new byte[rowBytes * bitmap.Height];
            for (var y = 0; y < bitmap.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * rowBytes, rowBytes);
            Premultiply(bytes);
            return bytes;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>
    /// ULW_ALPHA 要求源位图自左乘：每个颜色通道先乘 alpha 再提交。
    /// 少了这一步，合成器会按“颜色 + 独立 alpha”重复叠加，半透明边缘出现亮边甚至整块变实心。
    /// </summary>
    internal static void Premultiply(byte[] bgra)
    {
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            var alpha = bgra[i + 3];
            if (alpha == 255) continue;
            if (alpha == 0)
            {
                bgra[i] = 0;
                bgra[i + 1] = 0;
                bgra[i + 2] = 0;
                continue;
            }

            bgra[i] = (byte)(bgra[i] * alpha / 255);
            bgra[i + 1] = (byte)(bgra[i + 1] * alpha / 255);
            bgra[i + 2] = (byte)(bgra[i + 2] * alpha / 255);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    /// <summary>原生 POINT，用于 UpdateLayeredWindow 的 pptSrc（不能传 NULL，见 Present 的说明）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private const int DibRgbColors = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ColorsUsed;
        public int ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public int Colors;

        internal static BitmapInfo For(int width, int height) => new()
        {
            Header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                // 负高度 = 自上而下的行序，与 GDI+ 的 LockBits 输出一致，省掉一次翻转。
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = 0
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, int usage, out IntPtr bits, IntPtr section, int offset);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr destinationDc,
        IntPtr destinationPoint,
        ref NativeSize size,
        IntPtr sourceDc,
        ref NativePoint sourcePoint,
        int colorKey,
        ref BlendFunction blend,
        int flags);
}
