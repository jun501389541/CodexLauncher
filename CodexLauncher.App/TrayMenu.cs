using System.Windows.Forms;
using CodexLauncher.Core;

namespace CodexLauncher.App;

/// <summary>
/// 托盘菜单：主题、悬浮窗、开机自启、刷新与退出。
/// <para>
/// 它与高级设置是同一份状态的两个入口，所以这里只做两件事：把用户的选择报出去，把真实状态画出来。
/// 勾选一律由 <see cref="SyncTheme"/> / <see cref="SyncFloating"/> / <see cref="SyncAutoStart"/> 回填，
/// 而且回填不发事件——发了就变成"读一次配置又写一次配置"的回写环。
/// <see cref="SyncAutoStart"/> 尤其关键：菜单里的勾必须等于注册表真值，
/// 用户从任务管理器禁用启动项后这里要立刻反映，不能停留在"上次我们写成功过"。
/// </para>
/// </summary>
internal sealed class TrayMenu : IDisposable
{
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _themeRoot = new("主题");
    private readonly Dictionary<ThemeMode, ToolStripMenuItem> _themeItems = [];
    private readonly ToolStripMenuItem _floatingItem = new("显示悬浮窗");
    private readonly ToolStripMenuItem _autoStartItem = new("开机自动启动");
    private ThemeMode _mode = ThemeMode.System;

    public TrayMenu(Action onOpen, Action onRefresh, Action onExit)
    {
        _menu.Items.AddRange([
            new ToolStripMenuItem("打开主窗口", null, (_, _) => onOpen()),
            _floatingItem,
            _themeRoot,
            _autoStartItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("立即刷新", null, (_, _) => onRefresh()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("退出启动器", null, (_, _) => onExit())
        ]);

        foreach (var mode in new[] { ThemeMode.System, ThemeMode.Light, ThemeMode.Dark })
        {
            // CheckOnClick = false：勾只能由真实状态回填，不能鼠标一点就翻。
            var item = new ToolStripMenuItem(Text(mode)) { CheckOnClick = false };
            item.Click += (_, _) => ChooseTheme(mode);
            _themeItems[mode] = item;
            _themeRoot.DropDownItems.Add(item);
        }

        _floatingItem.CheckOnClick = false;
        _floatingItem.Click += (_, _) => FloatingRequested?.Invoke();
        _autoStartItem.CheckOnClick = false;
        _autoStartItem.Click += (_, _) => AutoStartRequested?.Invoke(!_autoStartItem.Checked);

        UiTheme.Style(_menu);
        // 主题子菜单是另一块下拉画布，ThemeManager.Walk 走不到它：不登记就永远停在旧配色。
        UiTheme.Style(_themeRoot.DropDown);
    }

    /// <summary>托盘菜单本体，交给 NotifyIcon 挂在右键上。</summary>
    public ContextMenuStrip Menu => _menu;

    /// <summary>主题子菜单本体：它是一块独立的下拉画布，必须单独上色。</summary>
    public ToolStripMenuItem ThemeRoot => _themeRoot;

    public ToolStripMenuItem FloatingItem => _floatingItem;

    public ToolStripMenuItem AutoStartItem => _autoStartItem;

    public IEnumerable<KeyValuePair<ThemeMode, ToolStripMenuItem>> ThemeItems => _themeItems;

    public ToolStripMenuItem ThemeItem(ThemeMode mode) => _themeItems[mode];

    /// <summary>用户选了某个主题（已排除重复点同一项）。</summary>
    public event Action<ThemeMode>? ThemeRequested;

    /// <summary>用户点了开机自启；参数是"想变成的状态"，由调用方写注册表后回填真值。</summary>
    public event Action<bool>? AutoStartRequested;

    /// <summary>用户点了悬浮窗；具体开还是关由调用方的状态决定。</summary>
    public event Action? FloatingRequested;

    public void SyncTheme(ThemeMode mode)
    {
        _mode = mode;
        foreach (var pair in _themeItems) pair.Value.Checked = pair.Key == mode;
    }

    public void SyncFloating(bool enabled) => _floatingItem.Checked = enabled;

    public void SyncAutoStart(bool enabled) => _autoStartItem.Checked = enabled;

    private void ChooseTheme(ThemeMode mode)
    {
        if (_mode == mode) return;
        ThemeRequested?.Invoke(mode);
    }

    private static string Text(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => "浅色",
        ThemeMode.Dark => "深色",
        _ => "跟随系统"
    };

    public void Dispose() => _menu.Dispose();
}
