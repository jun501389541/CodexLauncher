namespace CodexLauncher.App;

/// <summary>Viewport owns width; height follows the preferred table rows.
/// The host must not recursively measure the entire page on each layout.</summary>
internal sealed class PageContentLayout : TableLayoutPanel
{
    private bool _arranging;

    internal PageContentLayout() => DoubleBuffered = true;

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (_arranging) return;
        _arranging = true;
        try
        {
            // Non-AutoSize tables give spare height to their final row. Reading
            // allocated row heights would therefore prevent the page shrinking.
            var height = base.GetPreferredSize(new Size(Width, 0)).Height;
            if (Height != height) Height = height;
            base.OnLayout(e);
        }
        finally { _arranging = false; }
    }
}
