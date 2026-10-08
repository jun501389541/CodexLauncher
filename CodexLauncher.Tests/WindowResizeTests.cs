using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using CodexLauncher.App;
using CodexLauncher.Core;

internal static class WindowResizeTests
{
    internal static IEnumerable<(string Name, Action Run)> All =>
        [("full window resize preserves scroll and bounds without redundant width changes", Resize)];

    private static void Resize()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var scale in Baseline ? new[] { 1f } : new[] { 1f, 1.5f, 2f }) Run(scale);
            }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error is not null) throw error;
    }

    private static bool Baseline => Environment.GetEnvironmentVariable("CODEX_RESIZE_BASELINE") == "1";

    private static void Run(float scale)
    {
        // Transfer the entire UI to a test host: no MainForm.Load or real services run.
        using var main = new MainForm(null, true);
        var snapshot = QuotaResponseParser.Build(
            new(QuotaAccountKind.ChatGpt, "synthetic@example.invalid", "plus", true, null),
            """{"rateLimits":{"limitId":"codex","primary":{"windowDurationMins":300,"usedPercent":20}},"rateLimitResetCredits":{"availableCount":1,"credits":[{"resetType":"codexRateLimits","status":"available","expiresAt":1794038400}]}}""",
            DateTimeOffset.Now);
        typeof(MainForm).GetMethod("RenderQuota", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [snapshot]);
        using var preview = new Form { ClientSize = new Size(1000, 660), ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000) };
        preview.Controls.Add(main.Controls[0]);
        preview.Show(); Application.DoEvents();
        preview.Scale(new SizeF(scale, scale));
        var scroll = Descendants(preview).OfType<ScrollHost>().Single();
        var root = scroll.Controls[0];
        var panels = Descendants(preview).Prepend(preview).ToArray();
        var layouts = new Dictionary<Control, int>();
        foreach (var c in panels) c.Layout += (_, _) => layouts[c] = layouts.GetValueOrDefault(c) + 1;
        var rootWidths = 0;
        var lastWidth = root.Width;
        root.SizeChanged += (_, _) => { if (root.Width != lastWidth) rootWidths++; lastWidth = root.Width; };
        foreach (var expanded in new[] { false, true })
        {
            var bridge = panels.OfType<BridgePanel>().Single();
            if (expanded)
            {
                var toggle = (Button)typeof(BridgePanel).GetField("_toggle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bridge)!;
                toggle.PerformClick(); Application.DoEvents();
            }
            scroll.SetOffset(80);
            var offset = scroll.Offset;
            layouts.Clear(); rootWidths = 0;
            var timings = new List<double>();
            for (var i = 0; i < 80; i++)
            {
                var watch = Stopwatch.StartNew();
                var width = 900 + (i % 40 < 20 ? i % 20 : 20 - i % 20) * 8;
                preview.ClientSize = new Size(width, (int)(660 * scale));
                Application.DoEvents();
                watch.Stop(); timings.Add(watch.Elapsed.TotalMilliseconds);
                if (scroll.Offset != offset) throw new Exception($"resize lost scroll position: {offset} -> {scroll.Offset}");
                if (root.Right > scroll.ClientSize.Width) throw new Exception("root overflowed viewport");
                foreach (Control child in root.Controls)
                    if (child.Visible && child.Bottom + child.Margin.Bottom > root.Height - root.Padding.Bottom)
                        throw new Exception("page height clipped a visible card");
                if (root.Width != scroll.ClientSize.Width - ScrollHost.IndicatorWidth(main.DeviceDpi) - ScrollHost.IndicatorMargin(main.DeviceDpi))
                    throw new Exception("root did not fill the available viewport width");
                foreach (var row in panels.OfType<QuotaResetCreditRow>())
                    if (row.Controls.Cast<Control>().Any(c => c.Right > row.ClientSize.Width || c.Bottom > row.ClientSize.Height))
                        throw new Exception("reset expiration overflowed during resize");
            }
            var ordered = timings.Order().ToArray();
            Console.WriteLine($"Resize scale={scale} expanded={expanded}: 80 transitions; widths={rootWidths}; layouts={layouts.Values.Sum()}; total={timings.Sum():F0}ms; median={ordered[40]:F1}ms; p95={ordered[75]:F1}ms");
            foreach (var c in layouts.OrderByDescending(k => k.Value).Take(6))
                Console.WriteLine($"  {c.Key.GetType().Name} ({c.Key.Name}): {c.Value}");
            if (!Baseline && rootWidths > 80)
                throw new Exception($"root width changed {rootWidths} times for 80 transitions");
            if (Baseline) continue;

            // Simulate multiple native sizing messages in one frame. Release
            // must apply the last width immediately, even before the timer fires.
            rootWidths = 0;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(Form).GetMethod("OnResizeBegin", flags)!.Invoke(main, [EventArgs.Empty]);
            for (var i = 0; i < 20; i++) preview.ClientSize = new Size(920 + i * 4, preview.ClientSize.Height);
            typeof(Form).GetMethod("OnResizeEnd", flags)!.Invoke(main, [EventArgs.Empty]);
            Application.DoEvents();
            if (rootWidths != 1) throw new Exception($"interactive resize performed {rootWidths} width changes for one burst");
            if (root.Width != scroll.ClientSize.Width - ScrollHost.IndicatorWidth(main.DeviceDpi) - ScrollHost.IndicatorMargin(main.DeviceDpi))
                throw new Exception("resize release did not flush the final width");
            if (scroll.Offset != offset) throw new Exception($"interactive resize lost scroll position: {offset} -> {scroll.Offset}; content={root.Height}, viewport={scroll.Height}");

            typeof(Form).GetMethod("OnResizeBegin", flags)!.Invoke(main, [EventArgs.Empty]);
            preview.ClientSize = new Size(960, preview.ClientSize.Height);
            var deadline = Stopwatch.StartNew();
            while (root.Width != scroll.ClientSize.Width - ScrollHost.IndicatorWidth(main.DeviceDpi) - ScrollHost.IndicatorMargin(main.DeviceDpi) && deadline.ElapsedMilliseconds < 1000)
            { Application.DoEvents(); Thread.Sleep(1); }
            if (deadline.ElapsedMilliseconds >= 1000) throw new Exception("interactive resize timer never applied the pending width");
            typeof(Form).GetMethod("OnResizeEnd", flags)!.Invoke(main, [EventArgs.Empty]);
        }
        var expandedHeight = root.Height;
        var collapse = (Button)typeof(BridgePanel).GetField("_toggle", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panels.OfType<BridgePanel>().Single())!;
        collapse.PerformClick(); Application.DoEvents();
        if (root.Height >= expandedHeight) throw new Exception("page failed to shrink after bridge collapse");
    }

    private static IEnumerable<Control> Descendants(Control c) =>
        c.Controls.Cast<Control>().SelectMany(child => Descendants(child).Prepend(child));
}
