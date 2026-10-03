using System.Drawing.Drawing2D;
using CodexLauncher.Core;

namespace CodexLauncher.App;

/// <summary>
/// ⑤诊断列表表头自绘：原生 SysHeader32 在深色主题下是一条没涂上主题的亮色栏，
/// 改为无背景栏样式——卡片同色背景、Muted 文本、1px 底边框，画法集中在纯函数里，
/// 像素契约由单元测试直接画位图验证，不依赖真机截图。
/// </summary>
internal static class DiagnosticsHeaderPainter
{
    /// <summary>表头文字与卡片底色必须达到的对比度下限（与滚动指示条同一口径）。</summary>
    internal const double MinimumContrast = 3.0;

    /// <summary>画一列表头。background=卡片色，borderColor 画在单元格底部 1px。</summary>
    public static void PaintColumn(Graphics g, Rectangle bounds, string? text, Color textColor, Color background, Color borderColor, Font font)
    {
        using var backdrop = new SolidBrush(background);
        g.FillRectangle(backdrop, bounds);
        using var border = new SolidBrush(borderColor);
        g.FillRectangle(border, bounds.Left, bounds.Bottom - 1, Math.Max(1, bounds.Width), 1);
        if (string.IsNullOrEmpty(text)) return;
        TextRenderer.DrawText(
            g, text, font, bounds, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}
