using System.Runtime.InteropServices;
using CodexLauncher.Core;

namespace CodexLauncher.App;

/// <summary>
/// 窗口上边栏（标题栏）的 DWM 着色。
/// <para>
/// 深色主题下 Windows 仍然按浅色画标题栏，实测 #F3F3F3，与主页面 #11151A 差出一整片亮块。
/// attr 20（沉浸式深色）只能把它变成系统色 #202020，仍不是页面底色；attr 35/36/34
/// 才能把标题栏、标题文字、边框设成调色板里的颜色（真机像素采样确认标题栏精确变成请求值，
/// 且 attr 35 优先于 attr 20）。
/// </para>
/// <para>
/// 高对比度模式下必须交给系统：此时任何自绘颜色都会把系统配色冲淡，用户也就失去了对比度设置的意义。
/// </para>
/// <para>
/// 注意 attr 35/36/34 是<b>只写</b>属性：真机实测 <c>DwmGetWindowAttribute</c> 对它们一律返回
/// E_INVALIDARG（0x80070057），读回来永远是 0。所以这里不提供 Read 系列方法——
/// 它们只会给出"设成功了吗"的假答案。要验证效果就采样标题栏像素。
/// </para>
/// </summary>
internal static class WindowChrome
{
    internal const int ImmersiveDarkModeAttribute = 20;
    internal const int BorderColorAttribute = 34;
    internal const int CaptionColorAttribute = 35;
    internal const int TextColorAttribute = 36;

    /// <summary>
    /// ARGB → COLORREF。COLORREF 是 0x00BBGGRR，通道顺序与 ARGB 相反。
    /// 探针曾把绿色通道写成只取高位（-band 0xFF00），送 0x11151A 实际发出 0x1A1111，
    /// 读回 #11111A，差点被误判成"系统会对颜色取整"。三个通道都必须逐位搬。
    /// </summary>
    internal static int ColorRef(int argb) =>
        ((argb & 0xFF) << 16) | (argb & 0xFF00) | ((argb >> 16) & 0xFF);

    internal readonly record struct ChromePlan(bool Apply, int Caption, int Text, int Border, bool DarkFallback);

    /// <summary>
    /// 写入结果。<paramref name="Applies"/> 表示按策略是否应当覆写（高对比度下为 false）；
    /// <paramref name="Accepted"/> 表示本机 DWM 接受了全部三个只写属性——
    /// 这是"attr 35/36/34 到底生效了吗"唯一可确定性读到的信号（读回一律 E_INVALIDARG）。
    /// </summary>
    internal readonly record struct ChromeOutcome(bool Applies, bool Accepted)
    {
        public bool Applied => Applies && Accepted;
    }

    internal static ChromePlan Plan(ThemePalette palette, bool useSystemColors) => new(
        Apply: !useSystemColors,
        Caption: ColorRef(palette.Window),
        Text: ColorRef(palette.Text),
        Border: ColorRef(palette.Border),
        // 旧系统不认 attr 35/36/34，只能退回 attr 20。是否深色由背景亮度决定，
        // 而不是按调色板名字硬编码——高对比度的黑底本质上也是深色。
        DarkFallback: IsDark(palette.Window));

    private static bool IsDark(int argb)
    {
        var r = (argb >> 16) & 0xFF;
        var g = (argb >> 8) & 0xFF;
        var b = argb & 0xFF;
        return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0 < 0.5;
    }

    /// <summary>把标题栏刷成当前主题。</summary>
    internal static ChromeOutcome Apply(IntPtr handle, ThemePalette palette, bool useSystemColors)
    {
        if (handle == IntPtr.Zero) return new ChromeOutcome(false, false);
        var plan = Plan(palette, useSystemColors);
        if (!plan.Apply) return new ChromeOutcome(false, false);

        // 先写兜底属性，再写精确颜色：attr 35 覆盖 attr 20，顺序反了旧系统会看到系统色。
        var accepted = Set(handle, ImmersiveDarkModeAttribute, plan.DarkFallback ? 1 : 0) == 0;
        accepted &= Set(handle, CaptionColorAttribute, plan.Caption) == 0;
        accepted &= Set(handle, TextColorAttribute, plan.Text) == 0;
        accepted &= Set(handle, BorderColorAttribute, plan.Border) == 0;
        return new ChromeOutcome(true, accepted);
    }

    private static int Set(IntPtr handle, int attribute, int value)
    {
        try { return DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)); }
        catch (DllNotFoundException) { return unchecked((int)0x8007007E); }
        catch (EntryPointNotFoundException) { return unchecked((int)0x8007007F); }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
