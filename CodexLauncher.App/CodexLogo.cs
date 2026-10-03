using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CodexLauncher.App;

/// <summary>
/// ⑦Codex 云朵 Logo：1024px 透明底云朵字形作为嵌入资源随程序集分发，
/// 叠加在紫→蓝渐变圆角背景上。缩放时从原始字形重新渲染，不依赖本机安装 Codex；
/// 资源缺失或解码失败时返回 null，不绘制仿制图案。
/// </summary>
internal static class CodexLogo
{
    internal const string ResourceName = "CodexLauncher.App.Assets.codex-logo.png";

    /// <summary>加载云朵字形（透明底）。任何失败返回 null，由调用方走回退。</summary>
    public static Bitmap? LoadGlyph()
    {
        try
        {
            var stream = typeof(CodexLogo).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null) return null;
            using (stream)
            {
                return new Bitmap(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// 渲染 size×size 徽标：渐变圆角背景 + 居中云朵字形。
    /// glyph 传 null 时仅画渐变背景（测试用）；程序集资源缺失时 <see cref="Render(int)"/> 返回 null。
    /// </summary>
    public static Bitmap Render(int size, Bitmap? glyph)
    {
        var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var radius = size / 5F;

            // 紫→蓝对角渐变：圆角矩形裁出徽标轮廓。
            using (var path = RoundedRect(new RectangleF(0, 0, size, size), radius))
            using (var brush = new LinearGradientBrush(
                       new RectangleF(0, 0, size, size),
                       Color.FromArgb(124, 92, 255),
                       Color.FromArgb(43, 124, 255),
                       45F))
            {
                g.FillPath(brush, path);
            }

            if (glyph is null) return bitmap;

            // 云朵按徽标 58% 等比缩放居中：留出四周渐变呼吸位。
            var glyphSize = (int)Math.Round(size * 0.58);
            var scale = glyphSize / (float)Math.Max(glyph.Width, glyph.Height);
            var drawW = Math.Max(1, (int)Math.Round(glyph.Width * scale));
            var drawH = Math.Max(1, (int)Math.Round(glyph.Height * scale));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // 源字形是深色（实测全部不透明像素 RGB≈(13,13,13)）：ColorMatrix 染白，
            // 保留 alpha 抗锯齿轮廓，得到渐变底 + 白云朵的标准 Codex 形态。
            var matrix = new ColorMatrix
            {
                Matrix00 = 0F, Matrix01 = 0F, Matrix02 = 0F,
                Matrix10 = 0F, Matrix11 = 0F, Matrix12 = 0F,
                Matrix20 = 0F, Matrix21 = 0F, Matrix22 = 0F,
                Matrix30 = 0F, Matrix31 = 0F, Matrix32 = 0F, Matrix33 = 1F,
                Matrix40 = 1F, Matrix41 = 1F, Matrix42 = 1F
            };
            var attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix);
            var dest = new Rectangle((size - drawW) / 2, (size - drawH) / 2, drawW, drawH);
            g.DrawImage(glyph, dest, 0, 0, glyph.Width, glyph.Height, GraphicsUnit.Pixel, attributes);
        }

        return bitmap;
    }

    /// <summary>程序集内 1024px 云朵源图渲染徽标；失败（资源缺失/解码异常）返回 null。</summary>
    public static Bitmap? Render(int size)
    {
        var glyph = LoadGlyph();
        if (glyph is null) return null;
        using (glyph)
        {
            return Render(size, glyph);
        }
    }

    /// <summary>从同一 Codex 徽标生成多尺寸 Windows 图标，供标题栏、任务栏和系统托盘共用。</summary>
    public static Icon? CreateIcon()
    {
        try
        {
            using var glyph = LoadGlyph();
            if (glyph is null) return null;

            var frames = new List<(int Size, byte[] Png)>();
            foreach (var size in new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 })
            {
                using var bitmap = Render(size, glyph);
                using var png = new MemoryStream();
                bitmap.Save(png, ImageFormat.Png);
                frames.Add((size, png.ToArray()));
            }

            using var iconData = new MemoryStream();
            using (var writer = new BinaryWriter(iconData, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((ushort)0); // reserved
                writer.Write((ushort)1); // icon resource
                writer.Write((ushort)frames.Count);

                var imageOffset = 6 + 16 * frames.Count;
                foreach (var frame in frames)
                {
                    writer.Write((byte)(frame.Size == 256 ? 0 : frame.Size));
                    writer.Write((byte)(frame.Size == 256 ? 0 : frame.Size));
                    writer.Write((byte)0); // palette entries
                    writer.Write((byte)0); // reserved
                    writer.Write((ushort)1); // color planes
                    writer.Write((ushort)32); // bits per pixel
                    writer.Write((uint)frame.Png.Length);
                    writer.Write((uint)imageOffset);
                    imageOffset += frame.Png.Length;
                }

                foreach (var frame in frames) writer.Write(frame.Png);
            }

            iconData.Position = 0;
            return new Icon(iconData);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException or InvalidOperationException)
        {
            return null;
        }
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = radius * 2F;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
