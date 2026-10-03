using CodexLauncher.Core;

namespace CodexLauncher.App;

/// <summary>
/// 8 秒等待结束后询问“继续等待／取消／强制关闭”。
/// 关闭窗口等同于取消：不做任何后续结束动作。
/// </summary>
internal sealed class CloseDecisionDialog : Form
{
    private string? _choice;

    private CloseDecisionDialog(string detail)
    {
        Text = "Codex 没有在预期时间内退出";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = UiTheme.Window;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(460, 196);

        var message = new Label
        {
            Text = detail,
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            AutoSize = false,
            Height = 64
        };

        var hint = new Label
        {
            Text = "继续等待会再等 8 秒；取消不会结束 Codex；强制关闭可能中断正在执行的任务。",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            AutoSize = false,
            Height = 40
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 52,
            BackColor = UiTheme.Window
        };

        foreach (var option in CodexCloseChoice.Options)
        {
            var button = option == CodexCloseChoice.ForceClose
                ? UiTheme.SecondaryButton(option)
                : UiTheme.PrimaryButton(option);
            button.Click += (_, _) =>
            {
                _choice = option;
                Close();
            };
            buttons.Controls.Add(button);
        }

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24, 20, 24, 12),
            BackColor = UiTheme.Window
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.Controls.Add(message, 0, 0);
        layout.Controls.Add(hint, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);

        AcceptButton = buttons.Controls.OfType<Button>().FirstOrDefault(button => button.Text == CodexCloseChoice.KeepWaiting);
        CancelButton = buttons.Controls.OfType<Button>().FirstOrDefault(button => button.Text == CodexCloseChoice.Cancel);
    }

    /// <summary>返回用户选择；关闭对话框时返回 null（等同取消）。</summary>
    public static string? Ask(IWin32Window owner, string detail)
    {
        using var dialog = new CloseDecisionDialog(detail);
        dialog.ShowDialog(owner);
        return dialog._choice;
    }
}
