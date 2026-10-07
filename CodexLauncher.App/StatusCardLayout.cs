using System.Drawing;
using System.Windows.Forms;

namespace CodexLauncher.App;

/// <summary>
/// 状态卡版式：左侧状态圆点、中间正文、右侧徽章与元信息。
/// 元信息固定用路径、证据、耗时、更新时间四行展示；更新时间单独位于最后一行并右对齐。
/// 列宽与卡片高度都由内容推出，只让 <see cref="UiTheme.StatusMetaMaxWidth"/> 拦住异常长的路径名。
/// </summary>
internal static class StatusCardLayout
{
    public static RoundedPanel Build(Control dot, Control title, Control detail, Control notice, Control badge, Control meta)
    {
        var card = new RoundedPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 14)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(22),
            BackColor = Color.White
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var dotHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        dotHost.Controls.Add(dot);
        dotHost.Resize += (_, _) => dot.Location = new Point((dotHost.Width - dot.Width) / 2, (dotHost.Height - dot.Height) / 2);

        var wordsHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.White
        };
        wordsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        wordsHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        wordsHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wordsHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var words = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            BackColor = Color.White
        };
        words.Controls.Add(title);
        words.Controls.Add(detail);
        words.Controls.Add(notice);

        // 元信息列不用流式布局：FlowLayoutPanel 会忽略 Anchor，徽章贴不到右边缘。
        // 零内边距是硬要求——留 1px 内边距就会让 badge.Right 差那 1px。
        var area = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.White
        };
        area.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        badge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        badge.Margin = Padding.Empty;
        meta.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        if (meta is Label metaLabel) metaLabel.TextAlign = ContentAlignment.MiddleRight;
        // ④元信息不焊死在卡片底边：底边与列底之间留 8px 呼吸位；徽章的右贴边契约（零内边距）保持不变。
        meta.Margin = new Padding(0, 0, 0, 8);
        area.Controls.Add(badge, 0, 0);
        area.Controls.Add(meta, 0, 1);

        layout.Controls.Add(dotHost, 0, 0);
        wordsHost.Controls.Add(words, 0, 1);
        layout.Controls.Add(wordsHost, 1, 0);
        layout.Controls.Add(area, 2, 0);

        card.Controls.Add(layout);
        return card;
    }
}
