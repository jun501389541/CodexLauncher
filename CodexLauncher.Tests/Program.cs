using CodexLauncher.App;
using CodexLauncher.Core;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Reflection;

// 模拟 App Server：额度测试用它验证 stdio 握手、请求编号关联与只读白名单，
// 不会访问真实账号，也不产生任何对话请求。
if (args.Contains("--bridge-interop-server", StringComparer.OrdinalIgnoreCase))
{
    await BridgeInteropServer.RunAsync();
    return 0;
}
if (args.Contains("--fake-app-server", StringComparer.OrdinalIgnoreCase))
{
    RunFakeAppServer(args);
    return 0;
}

var tests = new List<(string Name, Action Run)>
{
    ("rejects remote or non-HTTP proxy", TestProxyValidation),
    ("selects direct then proxy then none", TestRouteSelection),
    ("reads and switches the fixed gateway upstream", () => TestGatewayControllerAsync().GetAwaiter().GetResult()),
    ("discovers changed Party and Verge mixed ports", TestUpstreamPortDiscovery),
    ("detects simultaneous Party and Verge TUN modes", TestTunModeDiscovery),
    ("keeps a locked TUN configuration unknown", TestTunReadFailureUnknown),
    ("labels the retained TUN state while configuration is unavailable", TestTunUnavailableStatus),
    ("binds launch route evidence to the verified process identity", TestLaunchRouteTracker),
    ("does not confirm a recommendation as the active instance route", TestRouteConfirmationInMainForm),
    ("reloads owned gateway ports while preserving selection", () => TestGatewayPortSyncAsync().GetAwaiter().GetResult()),
    ("reports incompatible controller responses", () => TestInvalidGatewayResponseAsync().GetAwaiter().GetResult()),
    ("treats a timed out but unbound controller port as stopped", () => TestAbsentGatewayTimeoutAsync().GetAwaiter().GetResult()),
    ("reuses an existing gateway without launching another core", () => TestGatewayReuseAsync().GetAwaiter().GetResult()),
    ("refuses to stop an external or Windows proxy gateway", () => TestGatewayStopSafetyAsync().GetAwaiter().GetResult()),
    ("recognizes when Windows still points at the fixed gateway", TestGatewaySystemProxyGuard),
    ("matches both fixed gateway ports to one listener process", TestGatewayPortOwnership),
    ("classifies HTTP response separately from service readiness", TestProbeClassification),
    ("maps layered Codex access health states", TestAccessHealthEvaluation),
    ("classifies normal, slow and failed runtime samples", TestRuntimeSampleClassification),
    ("formats runtime health metadata as four dedicated rows", TestRuntimeMetadataText),
    ("maps runtime health across counting and staleness rules", TestRuntimeHealthEvaluation),
    ("discards results from a changed runtime route", () => TestRuntimeMonitorRouteChangeAsync().GetAwaiter().GetResult()),
    ("runs one runtime sample at a time and stops on cancel", () => TestRuntimeMonitorLoopAsync().GetAwaiter().GetResult()),
    ("restores a stable line right after the machine wakes", () => TestRuntimeMonitorResumeAsync().GetAwaiter().GetResult()),
    ("normalizes only whitelisted diagnostic events for the current instance", () => TestDiagnosticSourceAsync().GetAwaiter().GetResult()),
    ("falls back to line-only detection when the diagnostic source is unusable", () => TestDiagnosticSourceFallbackAsync().GetAwaiter().GetResult()),
    ("verifies the Codex desktop process by package identity and path boundary", TestDesktopDiscovery),
    ("reports a permission failure instead of claiming Codex is running", TestDesktopPermissionFailure),
    ("refuses to close a Codex process whose id was reused", () => TestDesktopPidReuseAsync().GetAwaiter().GetResult()),
    ("requests a graceful close and reports when Codex does not exit", () => TestDesktopGracefulCloseAsync().GetAwaiter().GetResult()),
    ("continues waiting and cancels without force closing", () => TestDesktopWaitAndCancelAsync().GetAwaiter().GetResult()),
    ("force closes only the verified instance and its children", () => TestDesktopForceCloseAsync().GetAwaiter().GetResult()),
    ("offers keep waiting, cancel and confirmed force close after the grace period", () => TestCloseWorkflowDecisionAsync().GetAwaiter().GetResult()),
    ("keeps waiting for another grace period and cancels without force closing", () => TestCloseWorkflowKeepWaitingAsync().GetAwaiter().GetResult()),
    ("closes while background sampling still holds the refresh gate", () => TestCloseDuringSamplingAsync().GetAwaiter().GetResult()),
    ("explains why the close button is disabled", TestCloseButtonReason),
    ("does not report a running Codex from a sibling path or a denied process list", TestDetectorIdentity)
    ,("renders the embedded Codex cloud logo and creates the matching app icon", TestCodexLogo)
    ,("keeps theme, floating window and quota preferences across restarts", TestThemeSettings),
    ("resolves the theme and keeps every button state readable", TestThemeResolution),
    ("keeps the floating window inside a visible work area", TestFloatingWindowPlacement),
    ("signals an already running launcher instead of starting a second one", TestSingleInstanceActivation),
    ("reads multi-bucket quota windows and converts reset times", TestQuotaParsing),
    ("keeps quota unknown instead of reporting zero percent", TestQuotaUnknownValues),
    ("labels the quota account, plan and unsupported login methods", TestQuotaAccountReporting),
    ("refreshes quota on the five minute cycle with failure backoff", () => TestQuotaScheduleAsync().GetAwaiter().GetResult()),
    ("keeps the last quota result and marks it stale after failures", () => TestQuotaStaleAsync().GetAwaiter().GetResult()),
    ("queries the app server over stdio with only read-only methods", () => TestQuotaStdioAsync().GetAwaiter().GetResult()),
    ("reports quota as unavailable when the CLI is missing or times out", () => TestQuotaUnavailableAsync().GetAwaiter().GetResult()),
    ("passes the resolved proxy environment into the quota CLI process", () => TestQuotaSessionProxyEnvironmentAsync().GetAwaiter().GetResult()),
    ("reopens the quota session when proxy configuration changes", () => TestQuotaProxyChangeAsync().GetAwaiter().GetResult()),
    ("selects quota proxy sources in process, settings and windows order", TestQuotaProxyEnvironmentSelection),
    ("serializes refresh operations", TestAsyncOperationGate),
    ("restores only injected registry values", TestRecovery),
    ("recovers persisted proxy changes after interruption", TestPersistedRecovery),
    ("parses installed application id from manifest", TestManifest),
    ("chooses proxy when direct HTTP probe fails", TestNetworkDiagnosis),
    ("finds newest bundled Codex CLI", TestCliDiscovery),
    ("finds local proxy in Windows proxy settings", TestProxyDiscovery)
    ,("leaves default route process environment untouched", TestDefaultEnvironment)
    ,("locates MSIX app from package metadata and manifest", TestAppMetadata)
    ,("saves and reloads configured proxy", TestSettings)
    ,("reports locked settings without changing the original file", TestSettingsReadFailure)
    ,("retries core initialization after settings become readable", TestCoreInitializationRetry)
    ,("reports settings write failures without throwing", TestSettingsWriteFailure)
    ,("launch injects only temporarily and restores on failure", TestLaunchRestore)
    ,("successful launch also restores proxy variables", TestLaunchSuccessRestore)
    ,("existing desktop process prevents second launch", TestAlreadyRunning)
    ,("launch lock is released when its owner exits", TestLaunchLock)
    ,("recovery does not create a lock when no journal exists", TestRecoveryWithoutJournal)
    ,("recovery restores environment when a journal exists", TestAppLauncherRecovery)
    ,("closed local proxy port fails HTTPS diagnosis", () => TestClosedProxyPortAsync().GetAwaiter().GetResult())
    ,("cancelling probe terminates its child process", () => TestProcessCancellationAsync().GetAwaiter().GetResult())
    ,("re-styles every registered menu when the palette changes", TestThemeMenuRefresh)
    ,("lays out quota bars from the remaining percent", TestQuotaBarGeometry)
    ,("formats the reset countdown the way the quota bars show it", TestQuotaCountdownLabels)
    ,("formats quota reset details in local time", TestQuotaResetDetailLabel)
    ,("renders a quota bar row that follows the theme", TestQuotaBarRow)
    ,("keeps the quota account verification hint on one line", TestQuotaAccountSingleLine)
    ,("sizes the floating card from the measured text and dpi", TestFloatingCardLayout)
    ,("centers the floating card icon on the two text rows", TestFloatingIconInk)
    ,("keeps the floating card readable over any desktop backdrop", TestFloatingCardContrast)
    ,("hands the layered window premultiplied pixels", TestLayeredSurfacePixels)
    ,("passes a real source point to the layered window", TestLayeredSurfaceSourcePoint)
    ,("keeps the HTTP label aligned with its input box", TestProxyRowAlignment)
    ,("keeps the status badge and the meta column inside the card", TestStatusCardLayout)
    ,("grows the network card with the expanded advanced panel", TestExpandedAdvancedPanel)
    ,("plans the window chrome from the live palette", TestWindowChromePlan)
    ,("applies the chrome colors to a real window handle", TestWindowChromeApply)
    ,("scrolls the page with a themed indicator instead of a system scrollbar", TestScrollHost)
    ,("reduces page top padding while preserving header-to-card spacing", TestTopPaddingReduction)
    ,("paints a flat diagnostics header instead of a themed system bar", TestDiagnosticsHeader)
    ,("fits all diagnostic rows without an internal system scrollbar", TestDiagnosticsRowsFit)
    ,("expands diagnostics after rows were added while the list was hidden", TestDiagnosticsExpandAfterHiddenRows)
    ,("preserves diagnostic layout when the native list handle is recreated", TestDiagnosticsHandleRecreation)
    ,("exposes the theme choice inside the advanced settings", TestThemeSelector)
    ,("keeps both theme entry points and the auto start toggle in sync", TestTrayMenuContract)
    ,("keeps the auto start registration in sync with the setting", TestAutoStartRegistration)
};
if (args.Contains("--integration", StringComparer.OrdinalIgnoreCase))
{
    tests.Add(("discovers installed Codex MSIX and bundled CLI", TestLocalInstallation));
    tests.Add(("reads the signed-in quota account over stdio", () => TestLocalQuotaAsync().GetAwaiter().GetResult()));
    tests.Add(("reads the installed diagnostic database read-only", () => TestLocalDiagnosticSourceAsync().GetAwaiter().GetResult()));
    tests.Add(("generated gateway config is accepted by installed Mihomo", TestGatewayConfigWithMihomo));
    tests.Add(("reads the existing gateway without switching it", () => TestExistingGatewayAsync().GetAwaiter().GetResult()));
    tests.Add(("starts and stops only its own isolated Mihomo gateway", () => TestManagedGatewayLifecycleAsync().GetAwaiter().GetResult()));
}

tests.AddRange(BridgeStageFiveTests.All);
tests.AddRange(BridgeStageFourTests.All);
tests.AddRange(BridgeStageThreeTests.All);
tests.AddRange(BridgeStageTwoTests.All);
tests.AddRange(BridgeStageOneTests.All);
tests.AddRange(BridgeDependencyTests.All);
tests.AddRange(LauncherDataPathsTests.All);
if (args.Contains("--bridge-only", StringComparer.OrdinalIgnoreCase))
    tests = BridgeStageOneTests.All.Concat(BridgeDependencyTests.All).Concat(BridgeStageTwoTests.All).Concat(BridgeStageThreeTests.All).Concat(BridgeStageFourTests.All).ToList();
if (args.Contains("--bridge-stage3", StringComparer.OrdinalIgnoreCase))
    tests = BridgeStageThreeTests.All.ToList();
if (args.Contains("--bridge-stage4", StringComparer.OrdinalIgnoreCase))
    tests = BridgeStageFourTests.All.ToList();
if (args.Contains("--bridge-stage5", StringComparer.OrdinalIgnoreCase))
    tests = BridgeStageFiveTests.All.ToList();
// D-24 keeps the phone-flow client in this existing test project (no standalone
// console project). These scenarios use real loopback Kestrel/HttpClient and
// synthetic credentials, with no host account, LAN adapter or persisted grants.
if (args.Contains("--bridge-protocol-client", StringComparer.OrdinalIgnoreCase))
{
    tests = BridgeStageTwoTests.All
        .Where(test => test.Name.StartsWith("bridge HTTPS ", StringComparison.Ordinal))
        .Concat(BridgeStageThreeTests.All
            .Where(test => test.Name.StartsWith("Kestrel pairing ", StringComparison.Ordinal)))
        .ToList();
}
var filterIndex = Array.FindIndex(args, arg => arg.Equals("--filter", StringComparison.OrdinalIgnoreCase));
if (filterIndex >= 0 && filterIndex + 1 < args.Length)
    tests = tests.Where(test => test.Name.Contains(args[filterIndex + 1], StringComparison.OrdinalIgnoreCase)).ToList();
var failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed");
return failures == 0 ? 0 : 1;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"expected {expected}, got {actual}");
}

static void TestProxyValidation()
{
    Equal("http://127.0.0.1:7890/", ProxyAddress.Parse("127.0.0.1:7890").ToString());
    Equal("http://[::1]:8080/", ProxyAddress.Parse("http://[::1]:8080").ToString());
    foreach (var input in new[] { "http://example.com:8080", "socks5://127.0.0.1:1080", "127.0.0.1:0", "http://127.0.0.1:8080/path" })
    {
        try { ProxyAddress.Parse(input); throw new Exception($"accepted {input}"); }
        catch (ArgumentException) { }
    }
}

static void TestRouteSelection()
{
    var pass = new ProbeResult(ProbeStatus.Reachable, null, TimeSpan.FromMilliseconds(100), "OK");
    var rejected = new ProbeResult(ProbeStatus.HttpRejected, 403, TimeSpan.FromMilliseconds(100), "HTTP 403");
    var fail = new ProbeResult(ProbeStatus.Failed, null, TimeSpan.FromMilliseconds(100), "timeout");
    Equal(RouteKind.Direct, NetworkDecision.Choose(pass, pass));
    Equal(RouteKind.Proxy, NetworkDecision.Choose(fail, pass));
    Equal<RouteKind?>(null, NetworkDecision.Choose(fail, fail));
    Equal(RouteKind.Proxy, NetworkDecision.Choose(pass, pass, preferProxy: true));
    Equal(RouteKind.Direct, NetworkDecision.Choose(pass, fail, preferProxy: true));
    Equal(RouteKind.Proxy, NetworkDecision.Choose(fail, rejected, preferProxy: true));
}

static async Task TestGatewayControllerAsync()
{
    var handler = new GatewayHttpHandler();
    var controller = new GatewayController(new HttpClient(handler), new Uri("http://127.0.0.1:9098/"));
    Equal(GatewayUpstream.Verge, (await controller.ReadAsync(CancellationToken.None))?.Selected);
    Equal(GatewayUpstream.Party, (await controller.SwitchAsync(GatewayUpstream.Party, CancellationToken.None)).Selected);
    Equal(GatewayUpstream.Party, handler.Selected);
}

static void TestUpstreamPortDiscovery()
{
    var dir = Path.Combine(Path.GetTempPath(), "codex-upstream-ports-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var party = Path.Combine(dir, "mihomo.yaml");
        var verge = Path.Combine(dir, "verge.yaml");
        var discovery = new UpstreamPortDiscovery(party, verge);
        Equal(GatewayPorts.Default, discovery.Discover(GatewayPorts.Default));
        File.WriteAllText(party, "mixed-port: 8010\nport: 8012\n");
        File.WriteAllText(verge, "verge_mixed_port: 8020\nverge_port: 8022\n");
        Equal(new GatewayPorts(7896, 9098, 8010, 8020), discovery.Discover(GatewayPorts.Default));
        File.WriteAllText(party, "mixed-port: 0\nport: 8012\n");
        File.WriteAllText(verge, "verge_mixed_port: 0\nverge_port: 8022\n");
        Equal(new GatewayPorts(7896, 9098, 8012, 8022), discovery.Discover(GatewayPorts.Default));
        File.WriteAllText(verge, "verge_mixed_port: 7896\nverge_port: 8022\n");
        try { discovery.Discover(GatewayPorts.Default); throw new Exception("Expected invalid port collision"); }
        catch (ArgumentException) { }
    }
    finally { Directory.Delete(dir, true); }
}

static void TestTunModeDiscovery()
{
    var dir = Path.Combine(Path.GetTempPath(), "codex-tun-mode-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var party = Path.Combine(dir, "mihomo.yaml");
        var verge = Path.Combine(dir, "verge.yaml");
        File.WriteAllText(party, "mixed-port: 7890\ntun:\n  enable: true\ndns:\n  enable: true\n");
        File.WriteAllText(verge, "enable_tun_mode: true\n");
        var state = new TunModeDiscovery(party, verge).Read();
        Equal(true, state.PartyEnabled);
        Equal(true, state.VergeEnabled);
        Equal(true, state.HasConflict);
        File.WriteAllText(verge, "enable_tun_mode: false\n");
        state = new TunModeDiscovery(party, verge).Read();
        Equal(false, state.HasConflict);
    }
    finally { Directory.Delete(dir, true); }
}

static void TestTunReadFailureUnknown()
{
    var dir = Path.Combine(Path.GetTempPath(), "codex-tun-locked-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var party = Path.Combine(dir, "mihomo.yaml");
        var verge = Path.Combine(dir, "verge.yaml");
        File.WriteAllText(party, "tun:\n  enable: true\n");
        File.WriteAllText(verge, "enable_tun_mode: false\n");
        using var locked = new FileStream(party, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var state = new TunModeDiscovery(party, verge).Read();
        Equal(null, state.GetType().GetProperty("PartyEnabled")!.GetValue(state));
        Equal(false, state.GetType().GetProperty("VergeEnabled")!.GetValue(state));
        Equal(false, state.IsKnown);
        var merged = new TunModeState(true, false).MergeKnown(state);
        Equal(true, merged.PartyEnabled);
        Equal(false, merged.VergeEnabled);
    }
    finally { Directory.Delete(dir, true); }
}

static void TestTunUnavailableStatus()
{
    Exception? error = null;
    var thread = new Thread(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), $"codex-tun-status-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var services = new LauncherServices(dir,
                (_, token) => new SharedQuotaCoordinator(null, monitoringEnabled: false, lifetimeToken: token),
                _ => throw new Exception("bridge should remain disabled"), new MemoryAutoStartRegistry());
            using var form = new MainForm(null, true, services);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var merge = typeof(MainForm).GetMethod("MergeTunState", flags)!;
            var describe = typeof(MainForm).GetMethod("DescribeTunStatus", flags)!;
            merge.Invoke(form, [new TunModeState(true, false)]);
            merge.Invoke(form, [new TunModeState(null, false)]);

            var current = (TunModeState)typeof(MainForm).GetField("_currentTun", flags)!.GetValue(form)!;
            Equal(true, current.PartyEnabled);
            Equal(false, current.VergeEnabled);
            var stale = (string)describe.Invoke(form, null)!;
            Equal(true, stale.Contains("Party 开启 / Verge 关闭", StringComparison.Ordinal));
            Equal(true, stale.Contains("暂时不可读", StringComparison.Ordinal));

            typeof(MainForm).GetField("_installation", flags)!.SetValue(form,
                new CodexInstallation("synthetic", @"C:\Codex", "synthetic!App"));
            var updateAvailability = typeof(MainForm).GetMethod("UpdateActionAvailability", flags)!;
            updateAvailability.Invoke(form, null);
            var launchButton = typeof(MainForm).GetField("_launchButton", flags)!.GetValue(form)!;
            Equal(false, (bool)launchButton.GetType().GetProperty("Enabled")!.GetValue(launchButton)!);
            var disabledReason = typeof(MainForm).GetMethod("LaunchDisabledReason", flags)!.Invoke(form, [true]) as string;
            Equal(true, disabledReason!.Contains("暂时不可读", StringComparison.Ordinal));

            merge.Invoke(form, [new TunModeState(false, false)]);
            var recovered = (string)describe.Invoke(form, null)!;
            Equal(false, recovered.Contains("暂时不可读", StringComparison.Ordinal));
            updateAvailability.Invoke(form, null);
            Equal(true, (bool)launchButton.GetType().GetProperty("Enabled")!.GetValue(launchButton)!);
        }
        catch (Exception ex) { error = ex; }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (error is not null) throw error;
}

static void TestLaunchRouteTracker()
{
    var tracker = new LaunchRouteTracker();
    var started = new CodexInstance
    {
        ProcessId = 1234,
        StartedAt = DateTimeOffset.Parse("2026-10-07T01:02:03Z"),
        ExecutablePath = @"C:\Program Files\WindowsApps\OpenAI.Codex\ChatGPT.exe"
    };
    Equal(null, tracker.For(started)); // An externally started process has no launcher evidence.

    var proxy = ProxyAddress.Parse("127.0.0.1:7890");
    tracker.Record(started, RouteKind.Proxy, proxy);
    Equal(RouteKind.Proxy, tracker.For(started)!.Route);
    Equal(proxy, tracker.For(started)!.Proxy);

    var reusedPid = started with { StartedAt = started.StartedAt.AddSeconds(1) };
    Equal(null, tracker.For(reusedPid));
    Equal(null, tracker.For(started)); // PID reuse invalidates the old evidence.
}

static void TestRouteConfirmationInMainForm()
{
    Exception? error = null;
    var thread = new Thread(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), $"codex-route-evidence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var services = new LauncherServices(dir,
                (_, token) => new SharedQuotaCoordinator(null, monitoringEnabled: false, lifetimeToken: token),
                _ => throw new Exception("bridge should remain disabled"), new MemoryAutoStartRegistry());
            using var form = new MainForm(null, true, services);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var tracker = (LaunchRouteTracker)typeof(MainForm).GetField("_launchRouteTracker", flags)!.GetValue(form)!;
            var instance = new CodexInstance
            {
                ProcessId = 2345,
                StartedAt = DateTimeOffset.Parse("2026-10-07T01:02:03Z"),
                ExecutablePath = @"C:\Program Files\WindowsApps\OpenAI.Codex\ChatGPT.exe"
            };
            var discovery = new CodexDesktopDiscovery(CodexDesktopState.Running, instance, "synthetic", true);
            var reachable = new ProbeResult(ProbeStatus.Reachable, 200, TimeSpan.FromMilliseconds(100), "HTTP 200");
            void SetDiagnosis(RouteKind selected) => typeof(MainForm).GetField("_diagnosis", flags)!.SetValue(form,
                new DiagnosisResult(reachable, reachable, selected));
            typeof(MainForm).GetField("_desktop", flags)!.SetValue(form, discovery);
            SetDiagnosis(RouteKind.Direct);
            var sync = typeof(MainForm).GetMethod("SyncMonitorContext", flags)!;
            var contextField = typeof(MainForm).GetField("_monitorContext", flags)!;
            sync.Invoke(form, null);
            var context = (RuntimeMonitorContext)contextField.GetValue(form)!;
            Equal(false, context.RouteConfirmed);
            Equal(RouteKind.Direct, context.Route);

            tracker.Record(instance, RouteKind.Proxy, ProxyAddress.Parse("127.0.0.1:7890"));
            sync.Invoke(form, null);
            context = (RuntimeMonitorContext)contextField.GetValue(form)!;
            Equal(true, context.RouteConfirmed);
            Equal(RouteKind.Proxy, context.Route);

            var reused = instance with { StartedAt = instance.StartedAt.AddSeconds(1) };
            typeof(MainForm).GetField("_desktop", flags)!.SetValue(form,
                new CodexDesktopDiscovery(CodexDesktopState.Running, reused, "synthetic reused PID", true));
            sync.Invoke(form, null);
            context = (RuntimeMonitorContext)contextField.GetValue(form)!;
            Equal(false, context.RouteConfirmed);
            Equal(RouteKind.Direct, context.Route);
        }
        catch (Exception ex) { error = ex; }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (error is not null) throw error;
}

static async Task TestGatewayPortSyncAsync()
{
    var controller = new FakeGatewayController { Selected = GatewayUpstream.Party };
    var host = new FakeGatewayHost { Owned = true };
    var gateway = new GatewayCoordinator(controller, host, new FakeGatewayProxyGuard(false), GatewayPorts.Default);
    var changed = new GatewayPorts(7896, 9098, 8010, 8020);
    Equal(GatewaySyncStatus.Updated, await gateway.SyncPortsAsync(changed, CancellationToken.None));
    Equal(changed, host.ConfiguredPorts);
    Equal(GatewayUpstream.Party, controller.Selected);
    Equal(1, controller.ReloadCount);
    Equal(GatewaySyncStatus.Current, await gateway.SyncPortsAsync(changed, CancellationToken.None));
    Equal(1, controller.ReloadCount);
    host.Controls = false;
    Equal(GatewaySyncStatus.ExternalProcess, await gateway.SyncPortsAsync(GatewayPorts.Default, CancellationToken.None));
    Equal(1, controller.ReloadCount);
    host.Controls = true;
    host.Owned = false;
    Equal(GatewaySyncStatus.ExternalProcess, await gateway.SyncPortsAsync(GatewayPorts.Default, CancellationToken.None));
    Equal(1, controller.ReloadCount);
}

static async Task TestAbsentGatewayTimeoutAsync()
{
    var port = FreePort();
    using var client = new HttpClient(new SlowGatewayHttpHandler());
    var controller = new GatewayController(client, new Uri($"http://127.0.0.1:{port}/"));
    Equal<GatewaySnapshot?>(null, await controller.ReadAsync(CancellationToken.None));
}

static async Task TestInvalidGatewayResponseAsync()
{
    foreach (var response in new[]
    {
        "not json",
        "{\"name\":5,\"all\":[\"Party\",\"Verge\"],\"now\":\"Party\"}",
        "{\"name\":\"Upstream\",\"all\":[\"Party\",\"Verge\"],\"now\":\"999\"}"
    })
    {
        using var client = new HttpClient(new StaticGatewayHttpHandler(response));
        var controller = new GatewayController(client, new Uri("http://127.0.0.1:9098/"));
        try
        {
            await controller.ReadAsync(CancellationToken.None);
            throw new Exception("Expected an incompatible controller response");
        }
        catch (InvalidDataException) { }
    }
}

static async Task TestGatewayReuseAsync()
{
    var controller = new FakeGatewayController { Selected = GatewayUpstream.Verge };
    var host = new FakeGatewayHost();
    var gateway = new GatewayCoordinator(controller, host, new FakeGatewayProxyGuard(false), GatewayPorts.Default);
    Equal(GatewayStartStatus.AlreadyRunning, await gateway.StartAsync("unused.exe", CancellationToken.None));
    Equal(false, host.Started);
    Equal(GatewayUpstream.Party, (await gateway.SwitchAsync(GatewayUpstream.Party, CancellationToken.None)).Selected);
}

static async Task TestGatewayStopSafetyAsync()
{
    var controller = new FakeGatewayController { Selected = GatewayUpstream.Verge };
    var host = new FakeGatewayHost();
    var gateway = new GatewayCoordinator(controller, host, new FakeGatewayProxyGuard(false), GatewayPorts.Default);
    Equal(GatewayStopStatus.ExternalProcess, await gateway.StopAsync("mihomo.exe", CancellationToken.None));
    Equal(false, host.Stopped);
    host.ExternalStoppable = true;
    Equal(GatewayStopStatus.Stopped, await gateway.StopAsync("mihomo.exe", CancellationToken.None));
    Equal(true, host.Stopped);
    host.Stopped = false;
    host.Owned = true;
    gateway = new GatewayCoordinator(controller, host, new FakeGatewayProxyGuard(true), GatewayPorts.Default);
    Equal(GatewayStopStatus.WindowsProxyActive, await gateway.StopAsync("mihomo.exe", CancellationToken.None));
    Equal(false, host.Stopped);
}

static void TestGatewayPortOwnership()
{
    const string matching = "TCP 127.0.0.1:7896 0.0.0.0:0 LISTENING 10100\nTCP 127.0.0.1:9098 0.0.0.0:0 LISTENING 10100";
    const string different = "TCP 127.0.0.1:7896 0.0.0.0:0 LISTENING 10100\nTCP 127.0.0.1:9098 0.0.0.0:0 LISTENING 20200";
    Equal<int?>(10100, GatewayPortOwner.FindCommonOwner(matching, 7896, 9098));
    Equal<int?>(null, GatewayPortOwner.FindCommonOwner(different, 7896, 9098));
}

static void TestGatewaySystemProxyGuard()
{
    Equal(true, GatewaySystemProxyGuard.IsEnabledForGateway(1, "127.0.0.1:7896", 7896));
    Equal(false, GatewaySystemProxyGuard.IsEnabledForGateway(0, "127.0.0.1:7896", 7896));
    Equal(false, GatewaySystemProxyGuard.IsEnabledForGateway(1, "127.0.0.1:7897", 7896));
}

static void TestGatewayConfigWithMihomo()
{
    var binary = @"D:\Software\Mihomo Party\Clash Party\resources\sidecar\mihomo.exe";
    if (!File.Exists(binary)) throw new Exception("Mihomo binary not found");
    var dir = Path.Combine(Path.GetTempPath(), "codex-gateway-config-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var config = Path.Combine(dir, "config.yaml");
        File.WriteAllText(config, GatewayConfig.Build(GatewayPorts.Default));
        var result = new ProcessCommandRunner().RunAsync(new CommandSpec(binary, ["-t", "-d", dir, "-f", config], dir,
            new Dictionary<string, string?>(), TimeSpan.FromSeconds(10)), CancellationToken.None).GetAwaiter().GetResult();
        Equal(false, result.TimedOut);
        Equal(0, result.ExitCode);
    }
    finally { Directory.Delete(dir, true); }
}

static async Task TestExistingGatewayAsync()
{
    using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
    var controller = new GatewayController(client, new Uri("http://127.0.0.1:9098/"));
    var state = await controller.ReadAsync(CancellationToken.None);
    Console.WriteLine(state is null ? "  No existing gateway is running" : "  Gateway selected: " + state.Selected);
}

static async Task TestManagedGatewayLifecycleAsync()
{
    var binary = @"D:\Software\Mihomo Party\Clash Party\resources\sidecar\mihomo.exe";
    if (!File.Exists(binary)) throw new Exception("Mihomo binary not found");
    var dir = Path.Combine(Path.GetTempPath(), "codex-gateway-managed-" + Guid.NewGuid().ToString("N"));
    var host = new GatewayProcessHost(dir);
    var entryPort = FreePort();
    var controllerPort = FreePort();
    while (controllerPort == entryPort) controllerPort = FreePort();
    var ports = new GatewayPorts(entryPort, controllerPort, 7890, 7897);
    try
    {
        host.Start(binary, ports);
        using var client = new HttpClient();
        var controller = new GatewayController(client, new Uri($"http://127.0.0.1:{controllerPort}/"));
        GatewaySnapshot? state = null;
        for (var i = 0; i < 30 && state is null; i++)
        {
            await Task.Delay(200);
            state = await controller.ReadAsync(CancellationToken.None);
        }
        Equal(GatewayUpstream.Verge, state?.Selected);
        Equal(true, host.IsOwnedRunning());
        await controller.SwitchAsync(GatewayUpstream.Party, CancellationToken.None);
        var partyPort = FreePort();
        var vergePort = FreePort();
        while (vergePort == partyPort || vergePort == entryPort || vergePort == controllerPort) vergePort = FreePort();
        var changed = new GatewayPorts(entryPort, controllerPort, partyPort, vergePort);
        var coordinator = new GatewayCoordinator(controller, host, new FakeGatewayProxyGuard(false), ports);
        Equal(GatewaySyncStatus.Updated, await coordinator.SyncPortsAsync(changed, CancellationToken.None));
        Equal(GatewayUpstream.Party, (await controller.ReadAsync(CancellationToken.None))?.Selected);
        Equal(true, host.IsConfigCurrent(changed));
        var recordPath = Path.Combine(dir, "gateway-process.json");
        var originalRecord = File.ReadAllText(recordPath);
        var forged = System.Text.Json.Nodes.JsonNode.Parse(originalRecord)!;
        forged["StartedAtUtcTicks"] = 1;
        File.WriteAllText(recordPath, forged.ToJsonString());
        var refused = false;
        try { host.StopOwned(); }
        catch (InvalidOperationException) { refused = true; }
        Equal(true, refused);
        File.WriteAllText(recordPath, originalRecord);
        Equal(true, host.IsOwnedRunning());
        host.StopOwned();
        Equal(false, host.IsOwnedRunning());
    }
    finally
    {
        if (host.IsOwnedRunning()) host.StopOwned();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}

static int FreePort()
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static void TestProbeClassification()
{
    Equal(ProbeStatus.Reachable, NetworkDecision.ClassifyHttp(200));
    Equal(ProbeStatus.HttpRejected, NetworkDecision.ClassifyHttp(403));
    Equal(ProbeStatus.ProxyAuthRequired, NetworkDecision.ClassifyHttp(407));
    Equal(ProbeStatus.HttpRejected, NetworkDecision.ClassifyHttp(503));
}

static void TestAccessHealthEvaluation()
{
    var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    var denied = new ProbeResult(ProbeStatus.HttpRejected, 403, TimeSpan.FromMilliseconds(450), "HTTP 403");
    var failed = new ProbeResult(ProbeStatus.Failed, null, TimeSpan.FromSeconds(8), "8 秒超时");
    var slow = new ProbeResult(ProbeStatus.Reachable, 200, TimeSpan.FromMilliseconds(6000), "HTTP 200");
    var reachable = new DiagnosisResult(denied, failed, RouteKind.Direct);

    // 移除深度检测后，收到任意 HTTP 回应即视为线路可达，不再要求付费验证。
    var ready = AccessHealthEvaluator.Evaluate(true, new TunModeState(false, false), reachable,
        "系统默认网络", null, now);
    Equal(AccessHealthState.Available, ready.State);
    Equal("线路可达，可以尝试启动", ready.Title);
    Equal(RouteKind.Direct, ready.Route);

    var slowReachable = AccessHealthEvaluator.Evaluate(true, new TunModeState(false, false),
        new DiagnosisResult(slow, null, RouteKind.Direct), "系统默认网络", null, now);
    Equal(AccessHealthState.NeedsVerification, slowReachable.State);
    Equal("网络可达，但响应较慢", slowReachable.Title);

    var unavailable = AccessHealthEvaluator.Evaluate(true, new TunModeState(false, false),
        new DiagnosisResult(failed, failed, null), "未找到可用路径", "127.0.0.1:7896", now);
    Equal(AccessHealthState.Unavailable, unavailable.State);
    Equal("127.0.0.1:7896", unavailable.ProxyEndpoint);

    Equal(AccessHealthState.Unavailable,
        AccessHealthEvaluator.Evaluate(true, new TunModeState(true, true), reachable,
            "系统默认网络", null, now).State);
    Equal(AccessHealthState.Unavailable,
        AccessHealthEvaluator.Evaluate(false, new TunModeState(false, false), reachable,
            "系统默认网络", null, now).State);
}

static void TestRuntimeSampleClassification()
{
    static ProbeResult Sample(ProbeStatus status, int? code, double milliseconds) =>
        new(status, code, TimeSpan.FromMilliseconds(milliseconds), code is { } value ? $"HTTP {value}" : "连接失败");

    // 收到回应且低于 2000 ms 算正常；达到阈值算慢；没有回应算失败。
    Equal(RuntimeSampleKind.Normal, RuntimeSampleClassifier.Classify(Sample(ProbeStatus.Reachable, 200, 120)));
    Equal(RuntimeSampleKind.Normal, RuntimeSampleClassifier.Classify(Sample(ProbeStatus.HttpRejected, 403, 450)));
    Equal(RuntimeSampleKind.Normal, RuntimeSampleClassifier.Classify(Sample(ProbeStatus.HttpRejected, 503, 1999)));
    Equal(RuntimeSampleKind.Slow, RuntimeSampleClassifier.Classify(Sample(ProbeStatus.Reachable, 200, 2000)));
    Equal(RuntimeSampleKind.Slow, RuntimeSampleClassifier.Classify(Sample(ProbeStatus.HttpRejected, 503, 2500)));
    Equal(RuntimeSampleKind.Failed, RuntimeSampleClassifier.Classify(Sample(ProbeStatus.Failed, null, 3000)));
    Equal(RuntimeSampleKind.Failed, RuntimeSampleClassifier.Classify(Sample(ProbeStatus.ProxyAuthRequired, 407, 40)));
}

static void TestRuntimeHealthEvaluation()
{
    var now = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    static RuntimeHealthSnapshot Run(RuntimeHealthInput input) => RuntimeHealthEvaluator.Evaluate(input);

    var stopped = Run(new RuntimeHealthInput { CodexRunning = false, Now = now });
    Equal(RuntimeHealthState.NotRunning, stopped.State);
    Equal("未启动", stopped.ShortLabel);

    Equal(RuntimeHealthState.Checking, Run(new RuntimeHealthInput { Now = now }).State);
    Equal(RuntimeHealthState.Confirming, Run(new RuntimeHealthInput { RouteChanged = true, Now = now }).State);

    var confirming = Run(new RuntimeHealthInput
    {
        HasSampled = true,
        ConsecutiveNormal = 1,
        LastSample = RuntimeSampleKind.Normal,
        LastElapsed = TimeSpan.FromMilliseconds(120),
        SinceLastSample = TimeSpan.FromSeconds(1),
        Now = now
    });
    Equal(RuntimeHealthState.Confirming, confirming.State);
    Equal("正在确认线路", confirming.Title);

    var stable = Run(new RuntimeHealthInput
    {
        HasSampled = true,
        ConsecutiveNormal = 3,
        LastSample = RuntimeSampleKind.Normal,
        LastElapsed = TimeSpan.FromMilliseconds(120),
        SinceLastSample = TimeSpan.FromSeconds(1),
        Now = now
    });
    Equal(RuntimeHealthState.Stable, stable.State);
    Equal("线路稳定", stable.Title);
    Equal("空闲 · 线路稳定", stable.IdleLabel);
    Equal(RuntimeEvidenceSource.LineOnly, stable.Evidence);
    Equal("仅依据线路检测", stable.Note);

    var oneFailed = Run(new RuntimeHealthInput
    {
        HasSampled = true,
        ConsecutiveFailed = 1,
        LastSample = RuntimeSampleKind.Failed,
        LastDetail = "3 秒超时",
        SinceLastSample = TimeSpan.FromSeconds(1),
        Now = now
    });
    Equal(RuntimeHealthState.Fluctuating, oneFailed.State);

    var twoFailed = Run(new RuntimeHealthInput
    {
        HasSampled = true,
        ConsecutiveFailed = 2,
        LastSample = RuntimeSampleKind.Failed,
        SinceLastSample = TimeSpan.FromSeconds(1),
        Now = now
    });
    Equal(RuntimeHealthState.Fluctuating, twoFailed.State);

    var threeFailed = Run(new RuntimeHealthInput
    {
        HasSampled = true,
        ConsecutiveFailed = 3,
        LastSample = RuntimeSampleKind.Failed,
        LastDetail = "3 秒超时",
        SinceLastSample = TimeSpan.FromSeconds(1),
        Now = now
    });
    Equal(RuntimeHealthState.Down, threeFailed.State);
    Equal("连接异常", threeFailed.ShortLabel);

    // 红灯之后必须连续 3 次正常样本才恢复绿灯，第 1、2 次仍为黄灯。
    for (var normal = 1; normal <= 3; normal++)
    {
        var recovering = Run(new RuntimeHealthInput
        {
            HasSampled = true,
            ConsecutiveNormal = normal,
            LastSample = RuntimeSampleKind.Normal,
            LastElapsed = TimeSpan.FromMilliseconds(150),
            SinceLastSample = TimeSpan.FromSeconds(1),
            Now = now
        });
        Equal(normal < 3 ? RuntimeHealthState.Confirming : RuntimeHealthState.Stable, recovering.State);
    }

    var slow = Run(new RuntimeHealthInput
    {
        HasSampled = true,
        LastSample = RuntimeSampleKind.Slow,
        LastElapsed = TimeSpan.FromMilliseconds(2400),
        SinceLastSample = TimeSpan.FromSeconds(1),
        Now = now
    });
    Equal(RuntimeHealthState.Fluctuating, slow.State);
    Equal("网络波动", slow.Title);

    // 超过 15 秒没有有效采样时，旧绿灯必须让位给“状态未更新”。
    var stale = Run(new RuntimeHealthInput
    {
        HasSampled = true,
        ConsecutiveNormal = 3,
        LastSample = RuntimeSampleKind.Normal,
        SinceLastSample = TimeSpan.FromSeconds(16),
        Now = now
    });
    Equal(RuntimeHealthState.Fluctuating, stale.State);
    Equal("状态未更新", stale.Title);
    Equal(true, stale.Stale);

    Equal(RuntimeHealthState.Down, Run(new RuntimeHealthInput { TunConflict = true, Now = now }).State);

    var reconnecting = Run(new RuntimeHealthInput
    {
        HasSampled = true,
        ConsecutiveNormal = 3,
        LastSample = RuntimeSampleKind.Normal,
        SinceLastSample = TimeSpan.FromSeconds(1),
        DiagnosticSourceAvailable = true,
        Events = RuntimeDiagnosticState.From(new NetworkDiagnosticEvent(NetworkEventKind.Reconnect, now.AddSeconds(-4))).Events,
        Now = now
    });
    Equal(RuntimeHealthState.Fluctuating, reconnecting.State);
    Equal(RuntimeEvidenceSource.LineAndDiagnostics, reconnecting.Evidence);
    Equal<string?>(null, reconnecting.Note);

    var recovered = Run(new RuntimeHealthInput
    {
        HasSampled = true,
        ConsecutiveNormal = 3,
        LastSample = RuntimeSampleKind.Normal,
        SinceLastSample = TimeSpan.FromSeconds(1),
        DiagnosticSourceAvailable = true,
        Events = RuntimeDiagnosticState.From(
            new NetworkDiagnosticEvent(NetworkEventKind.Reconnect, now.AddSeconds(-30)),
            new NetworkDiagnosticEvent(NetworkEventKind.Recovery, now.AddSeconds(-2))).Events,
        Now = now
    });
    Equal(RuntimeHealthState.Stable, recovered.State);

    var longReconnect = Run(new RuntimeHealthInput
    {
        HasSampled = true,
        LastSample = RuntimeSampleKind.Normal,
        SinceLastSample = TimeSpan.FromSeconds(1),
        DiagnosticSourceAvailable = true,
        Events = RuntimeDiagnosticState.From(new NetworkDiagnosticEvent(NetworkEventKind.Reconnect, now.AddSeconds(-25))).Events,
        Now = now
    });
    Equal(RuntimeHealthState.Down, longReconnect.State);

    // 诊断源失效后，无法继续确认的重连状态必须被移除，只保留线路检测。
    var degraded = Run(new RuntimeHealthInput
    {
        HasSampled = true,
        LastSample = RuntimeSampleKind.Normal,
        SinceLastSample = TimeSpan.FromSeconds(1),
        DiagnosticSourceAvailable = false,
        Events = RuntimeDiagnosticState.From(new NetworkDiagnosticEvent(NetworkEventKind.Reconnect, now.AddSeconds(-40))).Events,
        Now = now
    });
    Equal(RuntimeHealthState.Confirming, degraded.State);
    Equal(RuntimeEvidenceSource.LineOnly, degraded.Evidence);
}

static async Task TestRuntimeMonitorRouteChangeAsync()
{
    var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var probe = new ScriptedProbe(async (_, _, _) =>
    {
        started.TrySetResult(true);
        await release.Task;
        return new ProbeResult(ProbeStatus.Reachable, 200, TimeSpan.FromMilliseconds(80), "HTTP 200");
    });
    var fingerprint = "direct";
    using var monitor = new RuntimeMonitor(probe, () => new RuntimeMonitorContext
    {
        Route = RouteKind.Direct,
        Fingerprint = fingerprint,
        RouteLabel = "系统默认网络"
    });

    var refresh = monitor.RefreshAsync(CancellationToken.None);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    fingerprint = "proxy:7896:Party";
    release.TrySetResult(true);

    var snapshot = await refresh.WaitAsync(TimeSpan.FromSeconds(5));
    Equal(RuntimeHealthState.Confirming, snapshot.State);
    Equal(0, snapshot.ConsecutiveNormal);
    Equal<RuntimeSampleKind?>(null, snapshot.LastSample);
}

static async Task TestRuntimeMonitorLoopAsync()
{
    var probe = new ScriptedProbe((_, _, _) =>
        Task.FromResult(new ProbeResult(ProbeStatus.Reachable, 200, TimeSpan.FromMilliseconds(20), "HTTP 200")));
    using var monitor = new RuntimeMonitor(probe, () => new RuntimeMonitorContext { Fingerprint = "direct" },
        interval: TimeSpan.FromMilliseconds(40), sampleTimeout: TimeSpan.FromSeconds(2));

    monitor.Start();
    await WaitUntilAsync(() => probe.Calls >= 3, "运行期监测没有持续采样");
    Equal(1, probe.MaxConcurrent);

    await monitor.StopAsync(CancellationToken.None);
    Equal(false, monitor.IsRunning);
    var callsAfterStop = probe.Calls;
    await Task.Delay(200);
    Equal(callsAfterStop, probe.Calls);
}

static async Task TestRuntimeMonitorResumeAsync()
{
    var now = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    var probe = new ScriptedProbe((_, _, _) =>
        Task.FromResult(new ProbeResult(ProbeStatus.Reachable, 200, TimeSpan.FromMilliseconds(30), "HTTP 200")));
    using var monitor = new RuntimeMonitor(probe, () => new RuntimeMonitorContext { Fingerprint = "direct" },
        sampleTimeout: TimeSpan.FromSeconds(2), clock: () => now);

    RuntimeHealthSnapshot snapshot = monitor.Snapshot;
    for (var index = 0; index < 3; index++)
    {
        now = now.AddSeconds(5);
        snapshot = await monitor.RefreshAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }
    Equal(RuntimeHealthState.Stable, snapshot.State);
    Equal(3, snapshot.ConsecutiveNormal);

    // 模拟睡眠 60 秒：唤醒后的旧绿灯必须重新取证。
    now = now.AddSeconds(60);
    var afterWake = await monitor.RefreshAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    Equal(RuntimeHealthState.Confirming, afterWake.State);
    Equal(1, afterWake.ConsecutiveNormal);
}

static async Task TestDiagnosticSourceAsync()
{
    var path = Path.Combine(Path.GetTempPath(), $"codex-diag-{Guid.NewGuid():N}.sqlite");
    try
    {
        using (var connection = OpenDiagnosticDatabase(path, schemaVersion: 2))
        {
            // 属于当前实例、可归一化的白名单记录。
            InsertLog(connection, 1, 200, 0, "codex_http_client::client", "pid:4242:abc",
                "list_models{refresh_strategy=online}:endpoint_session.execute_with{http.method=GET api.path=\"models\"}: Request completed method=GET url=https://chatgpt.com/backend-api/codex/models status=200 OK");
            InsertLog(connection, 2, 210, 0, "codex_app_server_transport::transport::remote_control::websocket", "pid:4242:abc",
                "remote control websocket status changed previous_status=Disabled next_status=Connecting installation_id=90ec8016 server_name=DESKTOP");
            InsertLog(connection, 3, 220, 0, "codex_app_server_transport::transport::remote_control::websocket", "pid:4242:abc",
                "remote control websocket status changed previous_status=Connecting next_status=Connected installation_id=90ec8016 server_name=DESKTOP");
            InsertLog(connection, 4, 230, 0, "codex_http_client::client", "pid:4242:abc",
                "Request failed method=POST url=https://chatgpt.com/backend-api/codex/analytics-events/events is_timeout=true is_connect=false");
            InsertLog(connection, 5, 240, 0, "codex_http_client::client", "pid:4242:abc",
                "Request completed method=POST url=https://chatgpt.com/backend-api/codex/responses status=503 Service Unavailable");
            InsertLog(connection, 6, 250, 0, "codex_app_server_transport::transport::remote_control::websocket", "pid:4242:abc",
                "failed to connect to app-server remote control websocket websocket_url=wss://chatgpt.com/backend-api/wham/remote/control/server reconnect_attempt=3");
            // 必须被排除：其他实例、非白名单来源、结构不符、4xx 与对话内容。
            InsertLog(connection, 7, 260, 0, "codex_http_client::client", "pid:9999:other",
                "Request completed method=GET url=https://chatgpt.com/backend-api/codex/models status=200 OK");
            InsertLog(connection, 8, 270, 0, "codex_core::session::handlers", "pid:4242:abc",
                "session_loop{thread_id=01a0}: Submission sub=Submission { op: TurnInput { input: UserInput { text: \"timeout Reconnecting 网络失败\" } } }");
            InsertLog(connection, 9, 280, 0, "codex_http_client::client", "pid:4242:abc",
                "Request completed method=POST url=https://chatgpt.com/backend-api/codex/responses status=403 Forbidden");
            InsertLog(connection, 10, 290, 0, "codex_http_client::client", "pid:4242:abc",
                "Request completed without a status field");
            InsertLog(connection, 11, 300, 0, "codex_core::stream_events_utils", "pid:4242:abc",
                "turn{thread.id=01a0}: tool output Reconnecting timeout");
        }

        var instance = new CodexInstance
        {
            ProcessId = 13836,
            StartedAt = DateTimeOffset.Now.AddMinutes(-30),
            ExecutablePath = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.928.2636.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe",
            PackageFamilyName = "OpenAI.Codex_2p2nqsd0c76g0",
            BackendProcessIds = [4242]
        };

        var source = new CodexDiagnosticSource(path);

        // 第一次读取只对齐游标，不回扫历史。
        var first = await source.ReadSinceAsync(DiagnosticCursor.Empty, instance, CancellationToken.None);
        Equal(true, first.SourceAvailable);
        Equal(0, first.Events.Count);
        Equal(11L, first.Cursor.LastId);
        Equal(true, first.Cursor.Initialized);

        using (var connection = OpenDiagnosticDatabase(path, schemaVersion: 2))
        {
            InsertLog(connection, 12, 310, 0, "codex_http_client::client", "pid:4242:abc",
                "Request completed method=GET url=https://chatgpt.com/backend-api/codex/models status=200 OK");
        }

        var second = await source.ReadSinceAsync(first.Cursor, instance, CancellationToken.None);
        Equal(true, second.SourceAvailable);
        Equal(1, second.Events.Count);
        Equal(NetworkEventKind.NetworkSuccess, second.Events[0].Kind);
        Equal("http-2xx", second.Events[0].Category);
        Equal(12L, second.Cursor.LastId);
        Equal(DateTimeOffset.FromUnixTimeSeconds(310).ToLocalTime(), second.Events[0].Timestamp);

        // 实例不变时累积事件而不重复：未恢复的重连起点必须跨轮次保留，
        // 否则每一轮都会把旧重连当成刚开始，20 秒规则永远不成立。
        var third = await source.ReadSinceAsync(second.Cursor, instance, CancellationToken.None);
        Equal(1, third.Events.Count);
        Equal(NetworkEventKind.NetworkSuccess, third.Events[0].Kind);
        Equal(12L, third.Cursor.LastId);

        using (var connection = OpenDiagnosticDatabase(path, schemaVersion: 2))
        {
            InsertLog(connection, 13, 320, 0, "codex_app_server_transport::transport::remote_control::websocket", "pid:4242:abc",
                "remote control websocket status changed previous_status=Connected next_status=Errored installation_id=90ec8016 server_name=DESKTOP");
        }

        var reconnecting = await source.ReadSinceAsync(third.Cursor, instance, CancellationToken.None);
        Equal(2, reconnecting.Events.Count);
        Equal(NetworkEventKind.NetworkSuccess, reconnecting.Events[0].Kind);
        Equal(NetworkEventKind.Reconnect, reconnecting.Events[1].Kind);
        Equal("websocket-errored", reconnecting.Events[1].Category);
        Equal(13L, reconnecting.Cursor.LastId);

        // 换一个实例后不继承上一个实例的事件与游标。
        using (var connection = OpenDiagnosticDatabase(path, schemaVersion: 2))
        {
            InsertLog(connection, 14, 330, 0, "codex_http_client::client", "pid:4242:abc",
                "Request completed method=GET url=https://chatgpt.com/backend-api/codex/models status=200 OK");
            InsertLog(connection, 15, 340, 0, "codex_http_client::client", "pid:5555:abc",
                "Request failed method=POST url=https://chatgpt.com/backend-api/codex/responses is_timeout=false is_connect=true");
        }

        var other = instance with { ProcessId = 20000, StartedAt = DateTimeOffset.Now.AddMinutes(-1), BackendProcessIds = [5555] };
        var switched = await source.ReadSinceAsync(reconnecting.Cursor, other, CancellationToken.None);
        Equal(true, switched.SourceAvailable);
        Equal(0, switched.Events.Count);
        Equal(15L, switched.Cursor.LastId);
    }
    finally { File.Delete(path); }
}

static async Task TestDiagnosticSourceFallbackAsync()
{
    var instance = new CodexInstance
    {
        ProcessId = 13836,
        StartedAt = DateTimeOffset.Now.AddMinutes(-30),
        ExecutablePath = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.928.2636.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe",
        PackageFamilyName = "OpenAI.Codex_2p2nqsd0c76g0",
        BackendProcessIds = [4242]
    };
    var cursor = new DiagnosticCursor(10, true);

    // 实例无法确认时不得读取日志，直接退回线路检测。
    var unverified = new CodexInstance { ProcessId = 0, ExecutablePath = "", BackendProcessIds = [] };
    var noIdentity = await new CodexDiagnosticSource(Path.Combine(Path.GetTempPath(), "missing.sqlite"))
        .ReadSinceAsync(cursor, unverified, CancellationToken.None);
    Equal(false, noIdentity.SourceAvailable);
    Equal(0, noIdentity.Events.Count);
    Equal(10L, noIdentity.Cursor.LastId);
    Equal("未能确认 Codex 桌面实例，仅依据线路检测", noIdentity.UnavailableReason);

    var missingPath = Path.Combine(Path.GetTempPath(), $"codex-diag-missing-{Guid.NewGuid():N}.sqlite");
    var missing = await new CodexDiagnosticSource(missingPath).ReadSinceAsync(cursor, instance, CancellationToken.None);
    Equal(false, missing.SourceAvailable);
    Equal("未找到 Codex 诊断库，仅依据线路检测", missing.UnavailableReason);

    var path = Path.Combine(Path.GetTempPath(), $"codex-diag-fallback-{Guid.NewGuid():N}.sqlite");
    try
    {
        // 字段变化：缺少必需列时停用适配器。
        using (OpenDiagnosticDatabase(path, schemaVersion: 2)) { }
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE logs DROP COLUMN process_uuid;";
            command.ExecuteNonQuery();
        }
        var changed = await new CodexDiagnosticSource(path).ReadSinceAsync(cursor, instance, CancellationToken.None);
        Equal(false, changed.SourceAvailable);
        Equal("诊断库字段已变化，仅依据线路检测", changed.UnavailableReason);
    }
    finally { File.Delete(path); }

    var lockedPath = Path.Combine(Path.GetTempPath(), $"codex-diag-locked-{Guid.NewGuid():N}.sqlite");
    try
    {
        // 库被其他进程独占锁定：必须快速返回降级结果，不抛异常、不冻结调用方。
        using (OpenDiagnosticDatabase(lockedPath, schemaVersion: 2)) { }
        var holder = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={lockedPath};Pooling=False");
        holder.Open();
        using (var exclusive = holder.CreateCommand())
        {
            exclusive.CommandText = "BEGIN EXCLUSIVE;";
            exclusive.ExecuteNonQuery();
        }
        try
        {
            var source = new CodexDiagnosticSource(lockedPath);
            var started = Environment.TickCount64;
            var locked = await source.ReadSinceAsync(new DiagnosticCursor(1, true), instance, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            var elapsed = Environment.TickCount64 - started;
            Console.WriteLine($"  Locked database read returned in {elapsed} ms: available={locked.SourceAvailable}");
            Equal(false, locked.SourceAvailable);
            Equal("诊断库当前不可读，仅依据线路检测", locked.UnavailableReason);
            Equal(true, elapsed < 5000);
        }
        finally
        {
            using (var rollback = holder.CreateCommand())
            {
                rollback.CommandText = "ROLLBACK;";
                rollback.ExecuteNonQuery();
            }
            holder.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
    }
    finally { TryDelete(lockedPath); }

    var rebuiltPath = Path.Combine(Path.GetTempPath(), $"codex-diag-rebuilt-{Guid.NewGuid():N}.sqlite");
    try
    {
        // 截断/重建后 id 会回到较小的值：游标必须重新对齐，不能把新记录当成历史。
        using (var connection = OpenDiagnosticDatabase(rebuiltPath, schemaVersion: 2))
        {
            InsertLog(connection, 100, 1000, 0, "codex_http_client::client", "pid:4242:abc",
                "Request completed method=GET url=https://chatgpt.com/backend-api/codex/models status=200 OK");
        }
        var source = new CodexDiagnosticSource(rebuiltPath);
        var opened = await source.ReadSinceAsync(new DiagnosticCursor(9999, true), instance, CancellationToken.None);
        Equal(true, opened.SourceAvailable);
        Equal(0, opened.Events.Count);
        Equal(100L, opened.Cursor.LastId);

        using (var connection = OpenDiagnosticDatabase(rebuiltPath, schemaVersion: 2))
        {
            InsertLog(connection, 101, 1010, 0, "codex_http_client::client", "pid:4242:abc",
                "Request failed method=POST url=https://chatgpt.com/backend-api/codex/responses is_timeout=true is_connect=false");
        }
        var resumed = await source.ReadSinceAsync(opened.Cursor, instance, CancellationToken.None);
        Equal(1, resumed.Events.Count);
        Equal(NetworkEventKind.NetworkFailure, resumed.Events[0].Kind);
        Equal("http-timeout", resumed.Events[0].Category);
        Equal(101L, resumed.Cursor.LastId);
    }
    finally { File.Delete(rebuiltPath); }

    var futurePath = Path.Combine(Path.GetTempPath(), $"codex-diag-future-{Guid.NewGuid():N}.sqlite");
    try
    {
        // 结构版本高于已验证样本：默认禁用该版本适配器。
        using (OpenDiagnosticDatabase(futurePath, schemaVersion: 9)) { }
        var future = await new CodexDiagnosticSource(futurePath).ReadSinceAsync(cursor, instance, CancellationToken.None);
        Equal(false, future.SourceAvailable);
        Equal("诊断库版本不受支持，仅依据线路检测", future.UnavailableReason);
    }
    finally { File.Delete(futurePath); }

    // 路径位于目录内时才算同一实例，前缀相同的其他目录必须排除。
    Equal(true, CodexInstance.IsPathWithin(
        @"C:\Program Files\WindowsApps\OpenAI.Codex_26.928.2636.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe",
        @"C:\Program Files\WindowsApps\OpenAI.Codex_26.928.2636.0_x64__2p2nqsd0c76g0"));
    Equal(false, CodexInstance.IsPathWithin(
        @"C:\Program Files\WindowsApps\OpenAI.Codex_26.928.2636.0_x64__2p2nqsd0c76g0-backup\app\ChatGPT.exe",
        @"C:\Program Files\WindowsApps\OpenAI.Codex_26.928.2636.0_x64__2p2nqsd0c76g0"));
    Equal(false, CodexInstance.IsPathWithin(null, @"C:\Program Files\WindowsApps"));
}

static void TryDelete(string path)
{
    // 临时库文件可能仍被 SQLite 原生句柄短暂占用；清理失败不影响测试结论。
    try { if (File.Exists(path)) File.Delete(path); }
    catch (IOException) { }
}

static Microsoft.Data.Sqlite.SqliteConnection OpenDiagnosticDatabase(string path, long schemaVersion)
{
    var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE IF NOT EXISTS logs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            ts INTEGER NOT NULL,
            ts_nanos INTEGER NOT NULL,
            level TEXT NOT NULL,
            target TEXT NOT NULL,
            feedback_log_body TEXT,
            module_path TEXT,
            file TEXT,
            line INTEGER,
            thread_id TEXT,
            process_uuid TEXT,
            estimated_bytes INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS _sqlx_migrations (
            version BIGINT PRIMARY KEY,
            description TEXT,
            installed_on TIMESTAMP,
            success BOOLEAN,
            checksum BLOB,
            execution_time BIGINT
        );
        """;
    command.ExecuteNonQuery();
    using var migration = connection.CreateCommand();
    migration.CommandText = "INSERT OR REPLACE INTO _sqlx_migrations (version, description, success) VALUES ($version, 'logs', 1);";
    migration.Parameters.AddWithValue("$version", schemaVersion);
    migration.ExecuteNonQuery();
    return connection;
}

static void InsertLog(Microsoft.Data.Sqlite.SqliteConnection connection, long id, long timestamp, long nanos, string target, string processUuid, string body)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO logs (id, ts, ts_nanos, level, target, feedback_log_body, process_uuid, estimated_bytes)
        VALUES ($id, $ts, $nanos, 'INFO', $target, $body, $uuid, 0);
        """;
    command.Parameters.AddWithValue("$id", id);
    command.Parameters.AddWithValue("$ts", timestamp);
    command.Parameters.AddWithValue("$nanos", nanos);
    command.Parameters.AddWithValue("$target", target);
    command.Parameters.AddWithValue("$body", body);
    command.Parameters.AddWithValue("$uuid", processUuid);
    command.ExecuteNonQuery();
}

static async Task WaitUntilAsync(Func<bool> condition, string message, int timeoutMs = 5000)
{
    var deadline = Environment.TickCount64 + timeoutMs;
    while (Environment.TickCount64 < deadline)
    {
        if (condition()) return;
        await Task.Delay(20);
    }
    throw new Exception(message);
}

static void TestCodexLogo()
{
    // 嵌入资源必须随程序集分发：资源缺失时不应绘制伪造 Logo。
    using var glyph = CodexLogo.LoadGlyph();
    Equal(true, glyph is not null);
    AtLeast(512, glyph!.Width, "云朵源图宽度");
    AtLeast(512, glyph.Height, "云朵源图高度");

    // 徽标：渐变圆角背景 + 居中云朵。四角必须透明（圆角裁切），中心列必须有云朵墨迹。
    using var badge = CodexLogo.Render(96, glyph);
    var corner = badge.GetPixel(1, 1);
    Equal(true, corner.A == 0);
    var edgeCenter = badge.GetPixel(48, 2);
    Equal(true, edgeCenter.A == 255);

    // 云朵是白色字形：中心区域必须存在接近白的墨迹（允许抗锯齿边缘，阈值放宽到 200）。
    var whiteInk = 0;
    for (var x = 24; x < 72; x++)
    for (var y = 24; y < 72; y++)
    {
        var p = badge.GetPixel(x, y);
        if (p.A > 128 && p.R > 200 && p.G > 200 && p.B > 200) whiteInk++;
    }

    AtLeast(30, whiteInk, "云朵白色墨迹像素");

    // 渐变背景本身要有色：中心行两端色调必须不同（紫→蓝对角渐变）。
    var left = badge.GetPixel(6, 48);
    var right = badge.GetPixel(89, 48);
    Equal(true, left.A == 255 && right.A == 255);
    Equal(true, Math.Abs(left.R - right.R) + Math.Abs(left.G - right.G) + Math.Abs(left.B - right.B) > 20);

    // 窗口、任务栏和托盘图标从同一渲染结果生成，资源失败时返回 null 供调用者保留系统图标。
    var iconFactory = typeof(CodexLogo).GetMethod("CreateIcon", BindingFlags.Public | BindingFlags.Static);
    Equal(true, iconFactory is not null);
    using var icon = (Icon?)iconFactory!.Invoke(null, null);
    Equal(true, icon is not null);
    using var iconBitmap = icon!.ToBitmap();
    AtLeast(16, iconBitmap.Width, "窗口图标位图宽度");
    AtLeast(16, iconBitmap.Height, "窗口图标位图高度");
}

static void TestRuntimeMetadataText()
{
    var snapshot = new RuntimeHealthSnapshot(
        RuntimeHealthState.Stable,
        "线路稳定",
        "稳定",
        "连续响应正常。",
        "",
        RuntimeEvidenceSource.LineAndDiagnostics,
        null,
        true,
        "固定入口 · Verge",
        50,
        0,
        RuntimeSampleKind.Normal,
        TimeSpan.FromMilliseconds(301),
        TimeSpan.FromSeconds(5),
        false,
        new DateTimeOffset(2026, 10, 2, 14, 57, 50, TimeSpan.FromHours(8)));

    var lines = snapshot.MetaText.Split('\n');
    Equal(4, lines.Length);
    Equal("路径：固定入口·Verge", lines[0]);
    Equal("证据：线路 + 诊断事件", lines[1]);
    Equal("耗时：301 ms", lines[2]);
    Equal("更新时间：14:57:50", lines[3]);
}

static void TestAsyncOperationGate()
{
    var gate = new AsyncOperationGate();
    Equal(true, gate.TryEnter());
    Equal(true, gate.IsBusy);
    Equal(false, gate.TryEnter());
    gate.Exit();
    Equal(false, gate.IsBusy);
    Equal(true, gate.TryEnter());
    gate.Exit();
}

static void TestRecovery()
{
    var store = new MemoryEnvironmentStore();
    store.Set("HTTPS_PROXY", "original");
    var journal = ProxyEnvironmentJournal.Capture(store, new Dictionary<string, string> { ["HTTPS_PROXY"] = "injected", ["HTTP_PROXY"] = "injected" });
    journal.Apply(store);
    Equal("injected", store.Get("HTTPS_PROXY"));
    store.Set("HTTP_PROXY", "external-change");
    journal.Restore(store);
    Equal("original", store.Get("HTTPS_PROXY"));
    Equal("external-change", store.Get("HTTP_PROXY"));
}

static void TestPersistedRecovery()
{
    var path = Path.Combine(Path.GetTempPath(), $"codex-launcher-test-{Guid.NewGuid():N}.json");
    try
    {
        var store = new MemoryEnvironmentStore();
        store.Set("HTTPS_PROXY", "before");
        var transaction = new ProxyEnvironmentTransaction(store, path);
        transaction.Begin(new Dictionary<string, string> { ["HTTPS_PROXY"] = "http://127.0.0.1:7890" });
        Equal("http://127.0.0.1:7890", store.Get("HTTPS_PROXY"));
        new ProxyEnvironmentTransaction(store, path).Recover();
        Equal("before", store.Get("HTTPS_PROXY"));
        Equal(false, File.Exists(path));
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void TestManifest()
{
    const string xml = "<Package xmlns='http://schemas.microsoft.com/appx/manifest/foundation/windows10'><Applications><Application Id='App' Executable='app/ChatGPT.exe' /></Applications></Package>";
    Equal("OpenAI.Codex_abc!App", CodexInstallation.BuildAumid("OpenAI.Codex_abc", xml));
}

static void TestNetworkDiagnosis()
{
    var probe = new FakeProbe();
    var diagnosis = new NetworkDiagnosis(probe);
    var result = diagnosis.RunAsync(ProxyAddress.Parse("127.0.0.1:7890"), CancellationToken.None).GetAwaiter().GetResult();
    Equal(RouteKind.Proxy, result.Selected);
    Equal(ProbeStatus.Failed, result.Direct.Status);
    Equal(ProbeStatus.Reachable, result.Proxy!.Status);
}

static void TestCliDiscovery()
{
    var root = Path.Combine(Path.GetTempPath(), $"codex-cli-test-{Guid.NewGuid():N}");
    try
    {
        var old = Directory.CreateDirectory(Path.Combine(root, "old"));
        var recent = Directory.CreateDirectory(Path.Combine(root, "new"));
        File.WriteAllText(Path.Combine(old.FullName, "codex.exe"), "old");
        File.WriteAllText(Path.Combine(recent.FullName, "codex.exe"), "new");
        File.SetLastWriteTimeUtc(Path.Combine(old.FullName, "codex.exe"), DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(Path.Combine(recent.FullName, "codex.exe"), DateTime.UtcNow);
        Equal(Path.Combine(recent.FullName, "codex.exe"), BundledCliFinder.Find(root));
    }
    finally { Directory.Delete(root, true); }
}

static async Task TestClosedProxyPortAsync()
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    var result = await new HttpProbe().CheckAsync(RouteKind.Proxy, ProxyAddress.Parse($"127.0.0.1:{port}"), CancellationToken.None);
    Equal(ProbeStatus.Failed, result.Status);
}

static void TestProxyDiscovery()
{
    Equal("http://127.0.0.1:7897/", ProxyDiscovery.FromWindowsProxyServer("http=127.0.0.1:7897;https=127.0.0.1:7897")?.ToString());
    Equal<ProxyAddress?>(null, ProxyDiscovery.FromWindowsProxyServer("https=proxy.example.com:8080"));
}

static void TestDefaultEnvironment()
{
    Equal(0, ProxyEnvironment.ForProcess(RouteKind.Direct, null).Count);
}

static void TestAppMetadata()
{
    var root = Path.Combine(Path.GetTempPath(), $"codex-app-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        File.WriteAllText(Path.Combine(root, "AppxManifest.xml"), "<Package xmlns='http://schemas.microsoft.com/appx/manifest/foundation/windows10'><Applications><Application Id='App' Executable='app/ChatGPT.exe' /></Applications></Package>");
        var json = System.Text.Json.JsonSerializer.Serialize(new { PackageFamilyName = "OpenAI.Codex_abc", InstallLocation = root });
        var app = CodexAppLocator.Parse(json);
        Equal("OpenAI.Codex_abc!App", app!.Aumid);
    }
    finally { Directory.Delete(root, true); }
}

static void TestSettings()
{
    var path = Path.Combine(Path.GetTempPath(), $"codex-settings-test-{Guid.NewGuid():N}.json");
    try
    {
        var store = new LauncherSettingsStore(path);
        store.Save(new LauncherSettings("http://127.0.0.1:7890/", @"D:\Tools\mihomo.exe", 8010, 8020));
        Equal("http://127.0.0.1:7890/", store.Load().ProxyUrl);
        Equal(@"D:\Tools\mihomo.exe", store.Load().MihomoPath);
        Equal(8010, store.Load().PartyPort);
        Equal(8020, store.Load().VergePort);
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void TestSettingsReadFailure()
{
    var dir = Path.Combine(Path.GetTempPath(), $"codex-settings-read-locked-{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    try
    {
        var path = Path.Combine(dir, "settings.json");
        const string original = "{\"ProxyUrl\":\"http://127.0.0.1:7890\"}";
        File.WriteAllText(path, original);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var method = typeof(LauncherSettingsStore).GetMethod("TryLoad");
            if (method is null) throw new Exception("LauncherSettingsStore.TryLoad is missing");
            var result = method.Invoke(new LauncherSettingsStore(path), null)!;
            Equal(false, (bool)result.GetType().GetProperty("Succeeded")!.GetValue(result)!);
            Equal("IOException", result.GetType().GetProperty("ErrorType")!.GetValue(result));
        }
        Equal(original, File.ReadAllText(path));
    }
    finally { Directory.Delete(dir, true); }
}

static void TestCoreInitializationRetry()
{
    Exception? error = null;
    var thread = new Thread(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), $"codex-init-retry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{}");
            var registry = new MemoryAutoStartRegistry();
            var quotaCreated = 0;
            var services = new LauncherServices(dir, (_, token) =>
            {
                quotaCreated++;
                return new SharedQuotaCoordinator(null, monitoringEnabled: false, lifetimeToken: token);
            }, _ => throw new Exception("bridge should remain disabled"), registry);
            using var form = new MainForm(null, true, services);
            var initialize = typeof(MainForm).GetMethod("InitializeCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var first = (Task)initialize.Invoke(form, null)!;
                try { first.GetAwaiter().GetResult(); }
                catch (Exception) { }
            }
            var retry = (Task)initialize.Invoke(form, null)!;
            retry.GetAwaiter().GetResult();
            Equal(true, retry.IsCompletedSuccessfully);
            Equal(1, quotaCreated);
        }
        catch (Exception ex) { error = ex; }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (error is not null) throw error;
}

static void TestSettingsWriteFailure()
{
    var dir = Path.Combine(Path.GetTempPath(), $"codex-settings-blocked-{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    try
    {
        var path = Path.Combine(dir, "settings.json");
        Directory.CreateDirectory(path + ".tmp");
        var result = new LauncherSettingsStore(path).TrySave(new LauncherSettings("http://127.0.0.1:7890/"));
        Equal(false, result.Succeeded);
        Equal("UnauthorizedAccessException", result.ErrorType);
    }
    finally { Directory.Delete(dir, true); }
}

static void TestLaunchRestore()
{
    var path = Path.Combine(Path.GetTempPath(), $"codex-launch-test-{Guid.NewGuid():N}.json");
    try
    {
        var store = new MemoryEnvironmentStore();
        store.Set("HTTPS_PROXY", "old");
        var activator = new FakeActivator(store);
        var launcher = new AppLauncher(store, path, new FakeDetector(false), activator);
        var app = new CodexInstallation("family", "location", "family!App");
        Equal(LaunchStatus.Failed, launcher.LaunchAsync(app, RouteKind.Proxy, ProxyAddress.Parse("127.0.0.1:7890"), CancellationToken.None).GetAwaiter().GetResult());
        Equal("http://127.0.0.1:7890/", activator.SeenProxy);
        Equal("old", store.Get("HTTPS_PROXY"));
        Equal(false, File.Exists(path));
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void TestLaunchSuccessRestore()
{
    var path = Path.Combine(Path.GetTempPath(), $"codex-launch-success-{Guid.NewGuid():N}.json");
    var store = new MemoryEnvironmentStore();
    store.Set("HTTPS_PROXY", "original");
    var activator = new FakeActivator(store, success: true);
    var launcher = new AppLauncher(store, path, new FakeDetector(false), activator);
    var app = new CodexInstallation("family", "location", "family!App");
    try
    {
        Equal(LaunchStatus.Launched, launcher.LaunchAsync(app, RouteKind.Proxy, ProxyAddress.Parse("127.0.0.1:7890"), CancellationToken.None).GetAwaiter().GetResult());
        Equal("http://127.0.0.1:7890/", activator.SeenProxy);
        Equal("original", store.Get("HTTPS_PROXY"));
        Equal(false, File.Exists(path));
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void TestAlreadyRunning()
{
    var store = new MemoryEnvironmentStore();
    var activator = new FakeActivator(store);
    var launcher = new AppLauncher(store, Path.Combine(Path.GetTempPath(), "unused-journal.json"), new FakeDetector(true), activator);
    var app = new CodexInstallation("family", "location", "family!App");
    Equal(LaunchStatus.AlreadyRunning, launcher.LaunchAsync(app, RouteKind.Direct, null, CancellationToken.None).GetAwaiter().GetResult());
    Equal(false, activator.Called);
}

static void TestLaunchLock()
{
    var path = Path.Combine(Path.GetTempPath(), $"codex-launch-lock-{Guid.NewGuid():N}.json");
    var app = new CodexInstallation("family", "location", "family!App");
    var launcher = new AppLauncher(new MemoryEnvironmentStore(), path, new FakeDetector(false), new FakeActivator(new MemoryEnvironmentStore()));
    using (var held = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        Equal(LaunchStatus.Busy, launcher.LaunchAsync(app, RouteKind.Direct, null, CancellationToken.None).GetAwaiter().GetResult());
    Equal(LaunchStatus.Failed, launcher.LaunchAsync(app, RouteKind.Direct, null, CancellationToken.None).GetAwaiter().GetResult());
    if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
}

static void TestRecoveryWithoutJournal()
{
    var path = Path.Combine(Path.GetTempPath(), $"codex-recovery-empty-{Guid.NewGuid():N}", "proxy-recovery.json");
    var launcher = new AppLauncher(new MemoryEnvironmentStore(), path, new FakeDetector(false), new FakeActivator(new MemoryEnvironmentStore()));
    launcher.Recover();
    Equal(false, File.Exists(path));
    Equal(false, File.Exists(path + ".lock"));
}

static void TestAppLauncherRecovery()
{
    var dir = Path.Combine(Path.GetTempPath(), $"codex-recovery-journal-{Guid.NewGuid():N}");
    var path = Path.Combine(dir, "proxy-recovery.json");
    try
    {
        var store = new MemoryEnvironmentStore();
        store.Set("HTTPS_PROXY", "before");
        new ProxyEnvironmentTransaction(store, path).Begin(new Dictionary<string, string> { ["HTTPS_PROXY"] = "injected" });
        var launcher = new AppLauncher(store, path, new FakeDetector(false), new FakeActivator(store));
        launcher.Recover();
        Equal("before", store.Get("HTTPS_PROXY"));
        Equal(false, File.Exists(path));
    }
    finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
}

static async Task TestProcessCancellationAsync()
{
    var pidPath = Path.Combine(Path.GetTempPath(), $"codex-child-{Guid.NewGuid():N}.txt");
    var command = "$PID | Set-Content -LiteralPath '" + pidPath.Replace("'", "''") + "'; Start-Sleep -Seconds 10";
    using var cancel = new CancellationTokenSource();
    var task = new ProcessCommandRunner().RunAsync(new CommandSpec("powershell.exe", ["-NoProfile", "-Command", command], null,
        new Dictionary<string, string?>(), TimeSpan.FromSeconds(20)), cancel.Token);
    try
    {
        int? pid = null;
        for (var i = 0; i < 40 && pid is null; i++)
        {
            if (File.Exists(pidPath))
            {
                try { pid = int.Parse(File.ReadAllText(pidPath).Trim()); }
                catch (IOException) { /* PowerShell has not finished writing its PID. */ }
            }
            if (pid is null) await Task.Delay(50);
        }
        if (pid is null) throw new Exception("child did not start");
        cancel.Cancel();
        try { await task; throw new Exception("cancellation was ignored"); }
        catch (OperationCanceledException) { }
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid.Value);
            if (!process.WaitForExit(2000)) throw new Exception("cancelled child is still running");
        }
        catch (ArgumentException) { /* Exited before inspection. */ }
    }
    finally { if (File.Exists(pidPath)) File.Delete(pidPath); }
}

static CodexInstallation TestInstallation(string root) =>
    new("OpenAI.Codex_2p2nqsd0c76g0", root, "OpenAI.Codex_2p2nqsd0c76g0!App");

static DesktopProcessSnapshot DesktopProcess(
    int id,
    string path,
    DateTimeOffset startedAt,
    int parent = 11380,
    string name = "ChatGPT") =>
    new(id, parent, name, path, startedAt);

static void TestDesktopDiscovery()
{
    var root = Path.Combine(Path.GetTempPath(), "codex-msix-root");
    var app = TestInstallation(root);
    var desktopPath = Path.Combine(root, "app", "ChatGPT.exe");
    var startedAt = new DateTimeOffset(2026, 10, 1, 8, 7, 11, TimeSpan.FromHours(8));
    var backendPath = Path.Combine(Path.GetTempPath(), "codex-cli", "c6fe824d725f02d7", "codex.exe");

    // 目录边界：安装目录的兄弟前缀目录不算 Codex。
    var boundary = new FakeProcessControl();
    boundary.Add(DesktopProcess(11, Path.Combine(root + "-other", "ChatGPT.exe"), startedAt));
    var boundaryDiscovery = new CodexDesktopController(app, boundary).DiscoverAsync(CancellationToken.None)
        .GetAwaiter().GetResult();
    Equal(CodexDesktopState.NotRunning, boundaryDiscovery.State);
    Equal(false, boundaryDiscovery.PossiblyRunning);

    // 不相关 CLI 与子进程不参与桌面身份判定。
    var unrelated = new FakeProcessControl();
    unrelated.Add(DesktopProcess(12, backendPath, startedAt, name: "codex"));
    unrelated.Add(DesktopProcess(13, @"C:\Program Files\Other\ChatGPT.exe", startedAt));
    Equal(CodexDesktopState.NotRunning, new CodexDesktopController(app, unrelated)
        .DiscoverAsync(CancellationToken.None).GetAwaiter().GetResult().State);

    // 正常路径：只有带主窗口的那个进程被确认为目标。
    var control = new FakeProcessControl();
    control.Add(DesktopProcess(21, desktopPath, startedAt.AddMinutes(-1)));
    control.Add(DesktopProcess(22, desktopPath, startedAt));
    control.Add(DesktopProcess(23, backendPath, startedAt, parent: 22, name: "codex"));
    control.Add(DesktopProcess(24, backendPath, startedAt, parent: 22, name: "codex"));
    control.Add(DesktopProcess(25, backendPath, startedAt, parent: 99, name: "codex"));
    control.SetWindow(22, new IntPtr(592008), "ChatGPT");

    var discovery = new CodexDesktopController(app, control).DiscoverAsync(CancellationToken.None)
        .GetAwaiter().GetResult();
    Equal(CodexDesktopState.Running, discovery.State);
    Equal(true, discovery.CanClose);
    Equal(true, discovery.HasMainWindow);
    Equal(22, discovery.Instance!.ProcessId);
    Equal(desktopPath, discovery.Instance.ExecutablePath);
    Equal(app.PackageFamilyName, discovery.Instance.PackageFamilyName);
    Equal(true, discovery.Instance.HasVerifiedIdentity);
    // 只收集父进程属于目标实例、且身份可验证的后端进程。
    Equal("23,24", string.Join(",", discovery.Instance.BackendProcessIds));

    // 身份已确认但没有主窗口时不允许请求关闭，仍按“可能正在运行”处理。
    var windowless = new FakeProcessControl();
    windowless.Add(DesktopProcess(31, desktopPath, startedAt));
    var windowlessDiscovery = new CodexDesktopController(app, windowless).DiscoverAsync(CancellationToken.None)
        .GetAwaiter().GetResult();
    Equal(CodexDesktopState.Running, windowlessDiscovery.State);
    Equal(false, windowlessDiscovery.CanClose);
    Equal(true, windowlessDiscovery.PossiblyRunning);
    Equal(true, windowlessDiscovery.Detail.Contains("未找到主窗口", StringComparison.Ordinal));
}

static void TestDesktopPermissionFailure()
{
    var app = TestInstallation(Path.Combine(Path.GetTempPath(), "codex-msix-root"));
    var startedAt = new DateTimeOffset(2026, 10, 1, 8, 7, 11, TimeSpan.FromHours(8));

    // 路径不可读：既不认定正在运行，也不得出“未运行”的结论。
    var unreadable = new FakeProcessControl();
    unreadable.Add(DesktopProcess(41, "", startedAt));
    var unreadableDiscovery = new CodexDesktopController(app, unreadable)
        .DiscoverAsync(CancellationToken.None).GetAwaiter().GetResult();
    Equal(CodexDesktopState.Unverified, unreadableDiscovery.State);
    Equal(false, unreadableDiscovery.CanClose);
    Equal(true, unreadableDiscovery.PossiblyRunning);
    Equal(true, unreadableDiscovery.Detail.Contains("权限", StringComparison.Ordinal));

    // 枚举本身失败：报告权限原因，不抛异常、不结束任何进程。
    var denied = new FakeProcessControl { ListThrows = true };
    var deniedDiscovery = new CodexDesktopController(app, denied)
        .DiscoverAsync(CancellationToken.None).GetAwaiter().GetResult();
    Equal(CodexDesktopState.PermissionDenied, deniedDiscovery.State);
    Equal(false, deniedDiscovery.CanClose);
    Equal(0, denied.Terminated.Count);
}

static async Task TestDesktopPidReuseAsync()
{
    var root = Path.Combine(Path.GetTempPath(), "codex-msix-root");
    var app = TestInstallation(root);
    var desktopPath = Path.Combine(root, "app", "ChatGPT.exe");
    var startedAt = new DateTimeOffset(2026, 10, 1, 8, 7, 11, TimeSpan.FromHours(8));

    // 进程编号复用：同一 PID 的启动时间已经不同，必须拒绝结束动作。
    var reused = new FakeProcessControl();
    reused.Add(DesktopProcess(51, desktopPath, startedAt.AddMinutes(30)));
    var reusedResult = await new CodexDesktopController(app, reused)
        .RequestCloseAsync(VerifiedInstance(51, startedAt, desktopPath, app), CancellationToken.None);
    Equal(CodexCloseOutcome.IdentityMismatch, reusedResult.Outcome);
    Equal(false, reusedResult.Exited);
    Equal(0, reused.ClosedWindows.Count);

    // 同一 PID 的可执行文件已经不在安装目录内，同样拒绝。
    var replaced = new FakeProcessControl();
    replaced.Add(DesktopProcess(52, @"C:\Program Files\Other\ChatGPT.exe", startedAt));
    var replacedResult = await new CodexDesktopController(app, replaced)
        .RequestCloseAsync(VerifiedInstance(52, startedAt, desktopPath, app), CancellationToken.None);
    Equal(CodexCloseOutcome.IdentityMismatch, replacedResult.Outcome);
    Equal(0, replaced.ClosedWindows.Count);

    // 身份信息不完整时连核对都不做。
    var incomplete = new FakeProcessControl();
    incomplete.Add(DesktopProcess(53, desktopPath, startedAt));
    var incompleteResult = await new CodexDesktopController(app, incomplete)
        .RequestCloseAsync(new CodexInstance { ProcessId = 53 }, CancellationToken.None);
    Equal(CodexCloseOutcome.IdentityMismatch, incompleteResult.Outcome);
    Equal(0, incomplete.ListCalls);

    // 进程已退出：视为不需要结束动作，而不是复用。
    var gone = new FakeProcessControl();
    var goneResult = await new CodexDesktopController(app, gone)
        .RequestCloseAsync(VerifiedInstance(54, startedAt, desktopPath, app), CancellationToken.None);
    Equal(CodexCloseOutcome.IdentityMismatch, goneResult.Outcome);
    Equal(true, goneResult.Detail.Contains("已退出", StringComparison.Ordinal));
}

static async Task TestDesktopGracefulCloseAsync()
{
    var root = Path.Combine(Path.GetTempPath(), "codex-msix-root");
    var app = TestInstallation(root);
    var desktopPath = Path.Combine(root, "app", "ChatGPT.exe");
    var startedAt = new DateTimeOffset(2026, 10, 1, 8, 7, 11, TimeSpan.FromHours(8));
    var instance = VerifiedInstance(61, startedAt, desktopPath, app);

    // 正常关闭：只请求关闭窗口，不结束进程。
    var graceful = new FakeProcessControl();
    graceful.Add(DesktopProcess(61, desktopPath, startedAt));
    graceful.SetWindow(61, new IntPtr(700001), "ChatGPT");
    graceful.ExitOnClose = true;
    var result = await new CodexDesktopController(app, graceful, TimeSpan.FromSeconds(2))
        .RequestCloseAsync(instance, CancellationToken.None);
    Equal(CodexCloseOutcome.Closed, result.Outcome);
    Equal(true, result.Exited);
    Equal(1, graceful.ClosedWindows.Count);
    Equal(700001, graceful.ClosedWindows[0].ToInt32());
    Equal(0, graceful.Terminated.Count);

    // 8 秒等待到点仍未退出：报告仍在运行，不自行强退。
    var stubborn = new FakeProcessControl();
    stubborn.Add(DesktopProcess(62, desktopPath, startedAt));
    stubborn.SetWindow(62, new IntPtr(700002), "ChatGPT");
    var stubbornResult = await new CodexDesktopController(app, stubborn, TimeSpan.FromMilliseconds(300))
        .RequestCloseAsync(instance with { ProcessId = 62 }, CancellationToken.None);
    Equal(CodexCloseOutcome.StillRunning, stubbornResult.Outcome);
    Equal(false, stubbornResult.Exited);
    Equal(1, stubborn.ClosedWindows.Count);
    Equal(0, stubborn.Terminated.Count);
    Equal(true, stubbornResult.Detail.Contains("0.3 秒", StringComparison.Ordinal));

    // 没有主窗口：不结束进程，给出原因。
    var windowless = new FakeProcessControl();
    windowless.Add(DesktopProcess(63, desktopPath, startedAt));
    var windowlessResult = await new CodexDesktopController(app, windowless, TimeSpan.FromMilliseconds(200))
        .RequestCloseAsync(instance with { ProcessId = 63 }, CancellationToken.None);
    Equal(CodexCloseOutcome.WindowUnavailable, windowlessResult.Outcome);
    Equal(0, windowless.Terminated.Count);

    // 发送关闭请求失败：报告失败，不升级为结束进程。
    var refuses = new FakeProcessControl { CloseSucceeds = false };
    refuses.Add(DesktopProcess(64, desktopPath, startedAt));
    refuses.SetWindow(64, new IntPtr(700004), "ChatGPT");
    var refusesResult = await new CodexDesktopController(app, refuses, TimeSpan.FromMilliseconds(200))
        .RequestCloseAsync(instance with { ProcessId = 64 }, CancellationToken.None);
    Equal(CodexCloseOutcome.Failed, refusesResult.Outcome);
    Equal(0, refuses.Terminated.Count);
}

static async Task TestDesktopWaitAndCancelAsync()
{
    var root = Path.Combine(Path.GetTempPath(), "codex-msix-root");
    var app = TestInstallation(root);
    var desktopPath = Path.Combine(root, "app", "ChatGPT.exe");
    var startedAt = new DateTimeOffset(2026, 10, 1, 8, 7, 11, TimeSpan.FromHours(8));
    var instance = VerifiedInstance(71, startedAt, desktopPath, app);

    // “继续等待”追加一轮等待：第二轮内退出就算正常关闭。
    var control = new FakeProcessControl();
    control.Add(DesktopProcess(71, desktopPath, startedAt));
    control.SetWindow(71, new IntPtr(800001), "ChatGPT");
    control.ExitAfter = TimeSpan.FromMilliseconds(400);
    var controller = new CodexDesktopController(app, control, TimeSpan.FromMilliseconds(200));

    var first = await controller.RequestCloseAsync(instance, CancellationToken.None);
    Equal(CodexCloseOutcome.StillRunning, first.Outcome);
    var second = await controller.WaitForExitAsync(instance, TimeSpan.FromSeconds(2), CancellationToken.None);
    Equal(true, second);
    Equal(0, control.Terminated.Count);

    // 取消不做后续结束动作。
    var cancelled = new FakeProcessControl();
    cancelled.Add(DesktopProcess(72, desktopPath, startedAt));
    cancelled.SetWindow(72, new IntPtr(800002), "ChatGPT");
    using var cancellation = new CancellationTokenSource();
    var pending = new CodexDesktopController(app, cancelled, TimeSpan.FromSeconds(5))
        .RequestCloseAsync(instance with { ProcessId = 72 }, cancellation.Token);
    await WaitUntilAsync(() => cancelled.ClosedWindows.Count == 1, "关闭请求没有发出");
    cancellation.Cancel();
    var cancelledException = await CaptureAsync(async () => await pending);
    Equal(true, cancelledException is OperationCanceledException);
    Equal(0, cancelled.Terminated.Count);

    // 取消强退流程同样不结束任何进程。
    using var forceCancellation = new CancellationTokenSource();
    forceCancellation.Cancel();
    var forceException = await CaptureAsync(async () => await new CodexDesktopController(app, cancelled)
        .ForceCloseAsync(instance with { ProcessId = 72 }, forceCancellation.Token));
    Equal(true, forceException is OperationCanceledException);
    Equal(0, cancelled.Terminated.Count);
}

static async Task TestDesktopForceCloseAsync()
{
    var root = Path.Combine(Path.GetTempPath(), "codex-msix-root");
    var app = TestInstallation(root);
    var desktopPath = Path.Combine(root, "app", "ChatGPT.exe");
    var backendPath = Path.Combine(Path.GetTempPath(), "codex-cli", "c6fe824d725f02d7", "codex.exe");
    var otherRoot = @"C:\Program Files\Other";
    var startedAt = new DateTimeOffset(2026, 10, 1, 8, 7, 11, TimeSpan.FromHours(8));

    var control = new FakeProcessControl();
    control.Add(DesktopProcess(81, desktopPath, startedAt));
    control.Add(DesktopProcess(82, desktopPath, startedAt, parent: 81));
    control.Add(DesktopProcess(83, desktopPath, startedAt, parent: 81));
    // 不属于本实例：父进程是别的进程、路径在安装目录外、或不在已确认后端清单内。
    control.Add(DesktopProcess(84, backendPath, startedAt, parent: 99, name: "codex"));
    control.Add(DesktopProcess(85, Path.Combine(otherRoot, "ChatGPT.exe"), startedAt, parent: 81));
    control.Add(DesktopProcess(86, backendPath, startedAt, parent: 81, name: "codex"));
    control.Add(DesktopProcess(87, backendPath, startedAt, parent: 81, name: "codex"));
    control.Add(DesktopProcess(88, @"D:\Software\Mihomo Party\mihomo.exe", startedAt, parent: 81, name: "mihomo"));

    var instance = VerifiedInstance(81, startedAt, desktopPath, app) with { BackendProcessIds = [86] };
    var result = await new CodexDesktopController(app, control, TimeSpan.FromMilliseconds(300))
        .ForceCloseAsync(instance, CancellationToken.None);

    Equal(true, result.Exited);
    // 目标实例 + 已验证子进程；86 在已确认后端清单内。
    Equal("81,82,83,86", string.Join(",", control.Terminated.OrderBy(id => id)));
    Equal(true, result.Detail.Contains("4 个", StringComparison.Ordinal));
    // 不相关的 CLI、其他安装路径、Party/Verge/固定入口都不受影响。
    Equal(false, control.Terminated.Contains(84));
    Equal(false, control.Terminated.Contains(85));
    Equal(false, control.Terminated.Contains(87));
    Equal(false, control.Terminated.Contains(88));

    // 强退前身份核对失败：一个进程都不结束。
    var reused = new FakeProcessControl();
    reused.Add(DesktopProcess(91, desktopPath, startedAt.AddMinutes(30)));
    var reusedResult = await new CodexDesktopController(app, reused, TimeSpan.FromMilliseconds(200))
        .ForceCloseAsync(VerifiedInstance(91, startedAt, desktopPath, app), CancellationToken.None);
    Equal(false, reusedResult.Exited);
    Equal(0, reusedResult.TerminatedProcesses);
    Equal(0, reused.Terminated.Count);
}

static async Task TestCloseWorkflowDecisionAsync()
{
    var root = Path.Combine(Path.GetTempPath(), "codex-msix-root");
    var app = TestInstallation(root);
    var desktopPath = Path.Combine(root, "app", "ChatGPT.exe");
    var startedAt = new DateTimeOffset(2026, 10, 1, 8, 7, 11, TimeSpan.FromHours(8));

    var control = new FakeProcessControl();
    control.Add(DesktopProcess(101, desktopPath, startedAt));
    control.SetWindow(101, new IntPtr(710001), "ChatGPT");
    var workflow = new CodexCloseWorkflow(
        new CodexDesktopController(app, control, TimeSpan.FromMilliseconds(300)),
        TimeSpan.FromMilliseconds(300));
    var instance = VerifiedInstance(101, startedAt, desktopPath, app);

    // 正常关闭请求发出、等待一个周期仍未退出：进入等待用户决定的状态。
    var attempt = await workflow.StartAsync(instance, CancellationToken.None);
    Equal(CodexCloseStage.AwaitingDecision, attempt.Stage);
    Equal(true, attempt.AwaitingDecision);
    Equal(false, attempt.Exited);
    Equal(true, attempt.Detail.Contains("没有退出", StringComparison.Ordinal));
    Equal(1, control.ClosedWindows.Count);
    Equal(0, control.Terminated.Count);
    Equal("继续等待,取消,强制关闭", string.Join(",", CodexCloseChoice.Options));

    // 未经确认的强退不结束任何进程。
    var refused = await workflow.ForceCloseAsync(confirmed: false, CancellationToken.None);
    Equal(false, refused.Exited);
    Equal(0, refused.TerminatedProcesses);
    Equal(0, control.Terminated.Count);
    Equal(true, refused.Detail.Contains("中断", StringComparison.Ordinal));

    // 确认后强退只作用于已确认属于该实例的进程。
    var forced = await workflow.ForceCloseAsync(confirmed: true, CancellationToken.None);
    Equal(true, forced.Exited);
    Equal("101", string.Join(",", control.Terminated));
    Equal(false, workflow.IsBusy);
}

static async Task TestCloseWorkflowKeepWaitingAsync()
{
    var root = Path.Combine(Path.GetTempPath(), "codex-msix-root");
    var app = TestInstallation(root);
    var desktopPath = Path.Combine(root, "app", "ChatGPT.exe");
    var startedAt = new DateTimeOffset(2026, 10, 1, 8, 7, 11, TimeSpan.FromHours(8));

    // 关闭请求发出后进程稍晚才退出：继续等待追加一个周期即可判定为已关闭。
    // 退出时刻落在“第一个等待周期之后、两个周期之和之前”，才能验证追加等待确实生效。
    var control = new FakeProcessControl();
    control.Add(DesktopProcess(111, desktopPath, startedAt));
    control.SetWindow(111, new IntPtr(710101), "ChatGPT");
    control.ExitAfter = TimeSpan.FromMilliseconds(600);
    var workflow = new CodexCloseWorkflow(
        new CodexDesktopController(app, control, TimeSpan.FromMilliseconds(200)),
        TimeSpan.FromMilliseconds(800));
    var instance = VerifiedInstance(111, startedAt, desktopPath, app);

    var first = await workflow.StartAsync(instance, CancellationToken.None);
    Equal(CodexCloseStage.AwaitingDecision, first.Stage);

    var second = await workflow.KeepWaitingAsync(CancellationToken.None);
    Equal(CodexCloseStage.Closed, second.Stage);
    Equal(true, second.Exited);
    Equal(0, control.Terminated.Count);
    Equal(1, control.ClosedWindows.Count);

    // 取消后不再做任何后续结束动作。
    var cancelControl = new FakeProcessControl();
    cancelControl.Add(DesktopProcess(112, desktopPath, startedAt));
    cancelControl.SetWindow(112, new IntPtr(710102), "ChatGPT");
    var cancelWorkflow = new CodexCloseWorkflow(
        new CodexDesktopController(app, cancelControl, TimeSpan.FromMilliseconds(150)),
        TimeSpan.FromMilliseconds(150));
    var cancelInstance = VerifiedInstance(112, startedAt, desktopPath, app);

    var pending = await cancelWorkflow.StartAsync(cancelInstance, CancellationToken.None);
    Equal(CodexCloseStage.AwaitingDecision, pending.Stage);

    var cancelled = cancelWorkflow.Cancel();
    Equal(CodexCloseStage.Cancelled, cancelled.Stage);
    Equal(false, cancelWorkflow.IsBusy);

    // 取消之后再确认强退也不结束进程。
    var afterCancel = await cancelWorkflow.ForceCloseAsync(confirmed: true, CancellationToken.None);
    Equal(false, afterCancel.Exited);
    Equal(0, afterCancel.TerminatedProcesses);
    Equal(0, cancelControl.Terminated.Count);

    // 取消之后的继续等待不再改变结果。
    var afterCancelWait = await cancelWorkflow.KeepWaitingAsync(CancellationToken.None);
    Equal(CodexCloseStage.Cancelled, afterCancelWait.Stage);
}

static async Task TestCloseDuringSamplingAsync()
{
    var root = Path.Combine(Path.GetTempPath(), "codex-msix-root");
    var app = TestInstallation(root);
    var desktopPath = Path.Combine(root, "app", "ChatGPT.exe");
    var startedAt = new DateTimeOffset(2026, 10, 1, 8, 7, 11, TimeSpan.FromHours(8));

    // 背景采样占着刷新闸门时，关闭流程仍独立完成，且不打断采样。
    var sampling = new AsyncOperationGate();
    Equal(true, sampling.TryEnter());

    var control = new FakeProcessControl();
    control.Add(DesktopProcess(121, desktopPath, startedAt));
    control.SetWindow(121, new IntPtr(710201), "ChatGPT");
    control.ExitOnClose = true;
    var workflow = new CodexCloseWorkflow(
        new CodexDesktopController(app, control, TimeSpan.FromMilliseconds(300)),
        TimeSpan.FromMilliseconds(300));
    var instance = VerifiedInstance(121, startedAt, desktopPath, app);

    var attempt = await workflow.StartAsync(instance, CancellationToken.None);
    Equal(CodexCloseStage.Closed, attempt.Stage);

    // 关闭按钮可用性不读取刷新闸门：采样仍占着闸门时按钮依然可用。
    Equal(true, sampling.IsBusy);
    var running = new CodexDesktopDiscovery(CodexDesktopState.Running, instance, "已确认桌面进程 121。", true);
    var available = CodexCloseButtonPolicy.Evaluate(running, closeInProgress: false);
    Equal(true, available.Enabled);
    sampling.Exit();
    Equal(false, sampling.IsBusy);

    // 同一时间只允许一个关闭流程：重复请求复用当前流程，不重复发送窗口关闭消息。
    var slow = new FakeProcessControl();
    slow.Add(DesktopProcess(122, desktopPath, startedAt));
    slow.SetWindow(122, new IntPtr(710202), "ChatGPT");
    var slowWorkflow = new CodexCloseWorkflow(
        new CodexDesktopController(app, slow, TimeSpan.FromMilliseconds(600)),
        TimeSpan.FromMilliseconds(600));
    var slowInstance = VerifiedInstance(122, startedAt, desktopPath, app);

    var first = slowWorkflow.StartAsync(slowInstance, CancellationToken.None);
    await WaitUntilAsync(() => slow.ClosedWindows.Count == 1, "关闭请求没有发出");
    var second = await slowWorkflow.StartAsync(slowInstance, CancellationToken.None);
    Equal(CodexCloseStage.InProgress, second.Stage);
    Equal(true, slowWorkflow.IsBusy);
    Equal(1, slow.ClosedWindows.Count);

    var finished = await first;
    Equal(CodexCloseStage.AwaitingDecision, finished.Stage);
    Equal(false, slowWorkflow.IsBusy);
}

static void TestThemeSettings()
{
    var path = Path.Combine(Path.GetTempPath(), $"codex-theme-test-{Guid.NewGuid():N}.json");
    try
    {
        var store = new LauncherSettingsStore(path);
        store.Save(new LauncherSettings(
            "http://127.0.0.1:7890/", @"D:\Tools\mihomo.exe", 8010, 8020,
            Theme: ThemeMode.Dark,
            FloatingWindowEnabled: true,
            FloatingLeft: 120,
            FloatingTop: 80,
            QuotaMonitoringEnabled: false,
            AutoStartEnabled: true));

        var loaded = store.Load();
        Equal("http://127.0.0.1:7890/", loaded.ProxyUrl);
        Equal(@"D:\Tools\mihomo.exe", loaded.MihomoPath);
        Equal(8010, loaded.PartyPort);
        Equal(8020, loaded.VergePort);
        Equal(ThemeMode.Dark, loaded.Theme);
        Equal(true, loaded.FloatingWindowEnabled);
        Equal(120, loaded.FloatingLeft);
        Equal(80, loaded.FloatingTop);
        Equal(false, loaded.QuotaMonitoringEnabled);
        Equal(true, loaded.AutoStartEnabled);

        // 旧版本配置文件只有原有字段：新增字段必须回落到计划默认值，而不是让整份配置作废。
        File.WriteAllText(path, "{\"ProxyUrl\":\"http://127.0.0.1:7890/\",\"MihomoPath\":null,\"PartyPort\":8010,\"VergePort\":8020}");
        var legacy = store.Load();
        Equal(8010, legacy.PartyPort);
        Equal(ThemeMode.System, legacy.Theme);
        Equal(false, legacy.FloatingWindowEnabled);
        Equal(null, legacy.FloatingLeft);
        Equal(null, legacy.FloatingTop);
        Equal(true, legacy.QuotaMonitoringEnabled);
        // 没听过"开机自启"的旧配置必须默认不开：自启是系统级副作用，绝不能默认打开。
        Equal(false, legacy.AutoStartEnabled);
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void TestThemeResolution()
{
    // 跟随系统：系统深色时用深色，浅色时用浅色。
    Equal(ThemePalette.Light, ThemeResolver.Resolve(ThemeMode.System, systemUsesDark: false, highContrast: false).Palette);
    Equal(ThemePalette.Dark, ThemeResolver.Resolve(ThemeMode.System, systemUsesDark: true, highContrast: false).Palette);
    // 手动选择优先于系统设置。
    Equal(ThemePalette.Light, ThemeResolver.Resolve(ThemeMode.Light, systemUsesDark: true, highContrast: false).Palette);
    Equal(ThemePalette.Dark, ThemeResolver.Resolve(ThemeMode.Dark, systemUsesDark: false, highContrast: false).Palette);
    Equal(false, ThemeResolver.Resolve(ThemeMode.Dark, systemUsesDark: false, highContrast: false).UseSystemColors);
    // Windows 高对比度模式优先采用系统颜色。
    Equal(true, ThemeResolver.Resolve(ThemeMode.Light, systemUsesDark: false, highContrast: true).UseSystemColors);
    Equal(true, ThemeResolver.Resolve(ThemeMode.Dark, systemUsesDark: true, highContrast: true).UseSystemColors);

    foreach (var palette in new[] { ThemePalette.Light, ThemePalette.Dark, ThemePalette.HighContrast })
    {
        var name = palette.Name;
        AtLeast(4.5, Contrast(palette.Text, palette.Card), $"{name} 卡片正文");
        AtLeast(4.5, Contrast(palette.Text, palette.Window), $"{name} 窗口正文");
        AtLeast(4.5, Contrast(palette.Muted, palette.Card), $"{name} 次要文字");
        AtLeast(4.5, Contrast(palette.OnPrimary, palette.Primary), $"{name} 主色文字");
        AtLeast(4.5, Contrast(palette.SuccessText, palette.SuccessSurface), $"{name} 可用徽章");
        AtLeast(4.5, Contrast(palette.WarningText, palette.WarningSurface), $"{name} 提醒徽章");
        AtLeast(4.5, Contrast(palette.ErrorText, palette.ErrorSurface), $"{name} 异常徽章");
        AtLeast(4.5, Contrast(palette.NeutralText, palette.NeutralSurface), $"{name} 中性徽章");

        foreach (var (face, label) in new[] { (palette.PrimaryButton, "主按钮"), (palette.SecondaryButton, "次按钮") })
        {
            AtLeast(4.5, Contrast(face.Foreground, face.Background), $"{name} {label}正常态");
            AtLeast(4.5, Contrast(face.Foreground, face.Hover), $"{name} {label}悬停态");
            AtLeast(4.5, Contrast(face.Foreground, face.Pressed), $"{name} {label}按下态");
            AtLeast(4.5, Contrast(face.DisabledText, face.DisabledBackground), $"{name} {label}禁用态");
            // 焦点环必须与正常背景可区分，否则键盘用户看不到焦点位置。
            if (face.Focus == face.Background) throw new Exception($"{name} {label}焦点环与背景同色");
            if (face.DisabledText == face.Foreground && face.DisabledBackground == face.Background)
                throw new Exception($"{name} {label}禁用态与正常态无法区分");
        }
    }
}

// 菜单（ContextMenuStrip）不在任何窗体的控件树里，ThemeManager.Walk 永远走不到它，
// 所以"切换主题后菜单必须重新上色"是一条独立的契约，必须单独验证。
static void TestThemeMenuRefresh()
{
    Exception? failure = null;
    // WinForms 控件需要 STA：主线程是 MTA，这里开一条专用线程。
    var thread = new Thread(() =>
    {
        try { RunThemeMenuRefresh(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

static void RunThemeMenuRefresh()
{
    var menu = new ContextMenuStrip();
    menu.Items.Add(new ToolStripMenuItem("打开主窗口"));
    var disposable = new ContextMenuStrip();
    disposable.Items.Add(new ToolStripMenuItem("临时"));
    try
    {
        UiTheme.Use(ThemePalette.Light, useSystemColors: false);
        UiTheme.Style(menu);
        UiTheme.Style(disposable);
        Equal(ThemePalette.Light.Card, menu.BackColor.ToArgb() & 0xFFFFFF);
        Equal(ThemePalette.Light.Text, menu.ForeColor.ToArgb() & 0xFFFFFF);

        // 切换调色板：菜单必须跟着换色，否则深色主题下就是白底 + 深色文字（对比度 1.19:1）。
        UiTheme.Use(ThemePalette.Dark, useSystemColors: false);
        Equal(ThemePalette.Dark.Card, menu.BackColor.ToArgb() & 0xFFFFFF);
        Equal(ThemePalette.Dark.Text, menu.ForeColor.ToArgb() & 0xFFFFFF);
        AtLeast(4.5, Contrast(menu.ForeColor.ToArgb(), menu.BackColor.ToArgb()), "深色主题菜单文字");

        // 渲染器必须实时读主题，而不是构造时快照。
        Equal(true, menu.Renderer is ThemedMenuRenderer);

        // 重复上色不能把菜单登记多次，也不能改变结果。
        UiTheme.Style(menu);
        UiTheme.Use(ThemePalette.Light, useSystemColors: false);
        Equal(ThemePalette.Light.Card, menu.BackColor.ToArgb() & 0xFFFFFF);

        // 已释放的菜单不能再被触碰：切换主题时不得抛异常（悬浮窗关闭后会重建菜单）。
        disposable.Dispose();
        UiTheme.Use(ThemePalette.Dark, useSystemColors: false);
        Equal(ThemePalette.Dark.Card, menu.BackColor.ToArgb() & 0xFFFFFF);
    }
    finally
    {
        menu.Dispose();
        disposable.Dispose();
        UiTheme.Use(ThemePalette.Light, useSystemColors: false);
    }
}

// 条状图必须严格线性对应剩余百分比：99% 与 98% 在 1033 像素轨道上相差 11 像素，
// 与 Codex 参考截图的实测值（填充末端 1077 vs 1066）一致。
static void TestQuotaBarGeometry()
{
    Equal(990, QuotaBar.FillWidth(1000, 99));
    Equal(980, QuotaBar.FillWidth(1000, 98));
    Equal(11, QuotaBar.FillWidth(1033, 99) - QuotaBar.FillWidth(1033, 98));
    Equal(1033, QuotaBar.FillWidth(1033, 100));
    Equal(0, QuotaBar.FillWidth(1033, 0));

    // 未知额度不画填充，也不能画满。
    Equal(0, QuotaBar.FillWidth(1033, null));

    // 越界百分比必须夹紧，不能画出轨道外。
    Equal(0, QuotaBar.FillWidth(1033, -20));
    Equal(1033, QuotaBar.FillWidth(1033, 400));

    // 轨道还没布局时不能出现负宽度。
    Equal(0, QuotaBar.FillWidth(0, 50));
    Equal(0, QuotaBar.FillWidth(-10, 50));

    // 条高随 DPI 缩放，96 DPI 下是 6 逻辑像素（150% 下 9 像素，与参考截图一致）。
    Equal(6, QuotaBar.BarHeight(96));
    Equal(9, QuotaBar.BarHeight(144));
    AtLeast(4, QuotaBar.BarHeight(48), "极端缩放下条高");
}

// Codex 风格额度行的文案：标题写窗口长度，副行写“X 后重置”，右侧写剩余百分比。
static void TestQuotaCountdownLabels()
{
    var now = DateTimeOffset.Parse("2026-10-01T12:00:00+08:00");
    var window = new QuotaWindow("主要窗口", "5 小时", 300, 99, now.AddHours(4).AddMinutes(38));
    Equal("5 小时限额", window.TitleLabel);
    Equal("每周限额", new QuotaWindow("次要窗口", "每周", 10080, 98, now.AddDays(6).AddHours(18)).TitleLabel);
    Equal("4小时38分钟后重置", window.ResetCountdownLabel(now));
    Equal("25分钟后重置", new QuotaWindow("主要窗口", "5 小时", 300, 60, now.AddMinutes(25)).ResetCountdownLabel(now));
    Equal("剩余 99%", window.RemainingSummary);

    // 未知值不得伪造成 0% 或 100%。
    var unknown = new QuotaWindow("主要窗口", "窗口长度未知", null, null, null);
    Equal("主要窗口", unknown.TitleLabel);
    Equal("剩余未知", unknown.RemainingSummary);
    Equal("重置时间未知", unknown.ResetCountdownLabel(now));

    // 到点后不自行填成 100%，沿用既有“等待确认重置”。
    Equal("等待确认重置", new QuotaWindow("主要窗口", "5 小时", 300, 40, now.AddSeconds(-1)).ResetCountdownLabel(now));
}

static void TestQuotaResetDetailLabel()
{
    var now = DateTimeOffset.Parse("2026-10-01T12:00:00+08:00");
    var window = new QuotaWindow("主要窗口", "5 小时", 300, 99, now.AddHours(4).AddMinutes(38));
    var china = TimeZoneInfo.CreateCustomTimeZone("Test China", TimeSpan.FromHours(8), "中国标准时间", "中国标准时间");
    var west = TimeZoneInfo.CreateCustomTimeZone("Test West", TimeSpan.FromHours(-7), "西部时间", "西部时间");
    Equal("4小时38分钟后重置 · 10-01 16:38", window.ResetDetailLabel(now, china));
    Equal("4小时38分钟后重置 · 10-01 01:38", window.ResetDetailLabel(now, west));
    var localReset = TimeZoneInfo.ConvertTime(window.ResetsAt!.Value, TimeZoneInfo.Local);
    Equal($"4小时38分钟后重置 · {localReset:MM-dd HH:mm}", window.ResetDetailLabel(now));
    Equal("重置时间未知", new QuotaWindow("主要窗口", "5 小时", 300, null, null).ResetDetailLabel(now));
    Equal("等待确认重置 · 10-01 11:59",
        new QuotaWindow("主要窗口", "5 小时", 300, 40, now.AddMinutes(-1)).ResetDetailLabel(now, china));
}

static void TestQuotaAccountSingleLine()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            using var form = new MainForm(null, true);
            var account = (Label)typeof(MainForm).GetField("_quotaAccount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            account.Text = "账号：jun501389541@gmail.com · Plus · 请核对桌面账号。";
            form.PerformLayout();
            account.PerformLayout();
            var singleLineSize = TextRenderer.MeasureText(account.Text, account.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (account.MaximumSize.Width < singleLineSize.Width)
                throw new Exception($"account label max width {account.MaximumSize.Width} is smaller than single-line text width {singleLineSize.Width}");
            var preferredSize = account.GetPreferredSize(Size.Empty);
            if (preferredSize.Height > singleLineSize.Height + 4)
                throw new Exception($"account label preferred size {preferredSize.Width}x{preferredSize.Height} wraps text measured at {singleLineSize.Width}x{singleLineSize.Height}");
        }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

// 额度条行必须整体跟随主题：这是深色菜单缺陷的同类契约（构造时烤死颜色），不能重犯。
static void TestQuotaBarRow()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { RunQuotaBarRow(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

static void RunQuotaBarRow()
{
    var now = DateTimeOffset.Parse("2026-10-01T12:00:00+08:00");
    var window = new QuotaWindow("主要窗口", "5 小时", 300, 99, now.AddHours(4).AddMinutes(38));
    var row = new QuotaBarRow();
    try
    {
        ThemeManager.ApplyTo(row, ThemePalette.Light, useSystemColors: false);
        row.Render(window, "5 小时限额", now);
        Equal(ThemePalette.Light.Card, row.BackColor.ToArgb() & 0xFFFFFF);
        Equal("5 小时限额", row.TitleText);
        Equal("4小时38分钟后重置", row.CountdownText);
        Equal(window.ResetDetailLabel(now), row.ResetDetailText);
        Equal("剩余 99%", row.RemainingText);
        Equal(99, row.Bar.RemainingPercent);
        Equal(true, row.TitleFont.Bold);
        Equal(true, row.Height > 0);
        Equal(true, row.Bar.Height > 0);
        Equal(true, row.ResetDetailTop >= row.Bar.Bottom);
        AtLeast(4.5, Contrast(row.TitleColor.ToArgb(), row.BackColor.ToArgb()), "浅色主题额度条标题");
        AtLeast(3.0, Contrast(row.Bar.FillColor.ToArgb(), row.Bar.TrackColor.ToArgb()), "浅色主题额度条填充");

        // 切换调色板：整行必须换色，不能停留在构造时的浅色。
        ThemeManager.ApplyTo(row, ThemePalette.Dark, useSystemColors: false);
        Equal(ThemePalette.Dark.Card, row.BackColor.ToArgb() & 0xFFFFFF);
        Equal(ThemePalette.Dark.Text, row.TitleColor.ToArgb() & 0xFFFFFF);
        Equal(ThemePalette.Dark.Muted, row.MutedColor.ToArgb() & 0xFFFFFF);
        AtLeast(4.5, Contrast(row.TitleColor.ToArgb(), row.BackColor.ToArgb()), "深色主题额度条标题");
        AtLeast(4.5, Contrast(row.MutedColor.ToArgb(), row.BackColor.ToArgb()), "深色主题额度条副行");
        AtLeast(3.0, Contrast(row.Bar.FillColor.ToArgb(), row.Bar.TrackColor.ToArgb()), "深色主题额度条填充");
        Equal(ThemePalette.Dark.Border, row.Bar.TrackColor.ToArgb() & 0xFFFFFF);

        // 未知额度：不画填充，但文案仍要给出“未知”。
        row.Render(new QuotaWindow("主要窗口", "窗口长度未知", null, null, null), "主要窗口", now);
        Equal(0, QuotaBar.FillWidth(row.Bar.Width, row.Bar.RemainingPercent));
        Equal("剩余未知", row.RemainingText);
        Equal("重置时间未知", row.CountdownText);
        Equal("重置时间未知", row.ResetDetailText);
    }
    finally
    {
        row.Dispose();
        UiTheme.Use(ThemePalette.Light, useSystemColors: false);
    }
}

// 悬浮窗卡片的尺寸契约：必须由字体度量 × DPI 算出。旧实现把客户区写死成 228x60，
// 150% 缩放下第二行文字只剩 15px 可见——“文字不全”就是这个原因。
static void TestFloatingCardLayout()
{
    var state = new Size(40, 27);
    var route = new Size(198, 24);

    // 150% 实测：内容 279x78（探针 glassprobe4 逐像素一致），外加 8*1.5=12px 投影留白。
    var at150 = FloatingCard.CardSize(state, route, 144);
    Equal(303, at150.Width);
    Equal(102, at150.Height);

    // 100%：同一段文字在 96 DPI 下更小，卡片跟着变小（24+26+4+132=186，加 8*2 投影留白）。
    var at100 = FloatingCard.CardSize(new Size(27, 18), new Size(132, 16), 96);
    Equal(202, at100.Width);
    Equal(68, at100.Height);

    // 200%：不能出现负数或比内容还小的高度。
    var at200 = FloatingCard.CardSize(new Size(54, 36), new Size(264, 32), 192);
    Equal(24, FloatingCard.Scale(12, 192));
    Equal(true, at200.Height > 36 + 32);

    // 极短文字也要有可点击的最小尺寸，且始终装得下两行。
    var minimum = FloatingCard.CardSize(new Size(4, 4), new Size(4, 4), 96);
    Equal(true, minimum.Width >= 160);
    Equal(true, minimum.Height >= 48);
    Equal(true, minimum.Height > 4 + 4);

    // 卡片矩形必须是整窗内缩一圈投影，且随 DPI 缩放。
    var bounds = FloatingCard.CardBounds(at150, 144);
    Equal(12, bounds.X);
    Equal(12, bounds.Y);
    Equal(303 - 24, bounds.Width);
    Equal(102 - 24, bounds.Height);

    // 空文字只占最小尺寸，不产生负宽。
    Equal(0, FloatingCard.Scale(0, 144));
    Equal(true, FloatingCard.CardSize(Size.Empty, Size.Empty, 96).Width > 0);

    // 图标必须垂直居中于两行文字组成的整块，而不是贴在第一行的顶边：
    // 以前图标中心比文字块中心高 14px（150% DPI 实测），看起来就是"两行没对齐"。
    var iconCard = FloatingCard.CardBounds(at150, 144);
    var icon = FloatingCard.IconBounds(iconCard, state, route, 144);
    var inset = FloatingCard.Scale(8, 144);
    var gap = FloatingCard.Scale(2, 144);
    var blockTop = iconCard.Top + inset;
    var blockBottom = blockTop + state.Height + gap + route.Height;
    Equal(true, icon.Top >= blockTop);
    Equal(true, icon.Bottom <= blockBottom);
    Equal((blockTop + blockBottom) / 2, icon.Top + icon.Height / 2);

    // 只有一行文字时按这一行居中，不能算出负偏移。
    var single = FloatingCard.IconBounds(iconCard, state, Size.Empty, 144);
    Equal(true, single.Top >= blockTop);
    Equal(blockTop + state.Height / 2, single.Top + single.Height / 2);

    // 文字极短（图标比文字块高）时同样不能把图标顶出卡片。
    var tiny = FloatingCard.IconBounds(iconCard, new Size(4, 2), new Size(4, 2), 144);
    Equal(true, tiny.Top >= iconCard.Top);
    Equal(true, tiny.Bottom <= iconCard.Bottom);
}

// 真机 150% DPI 实测：圆点画出来的墨迹中心 32.9，两行文字墨迹中心 29.7，差 3.2px。
// 原因是圆点有描边且按整块居中，文字按行排列——两者"墨迹重心"本来就不重合。
// 这里把卡片里实际的几何摆出来，让"对齐"有可核对的数字，而不是靠肉眼。
static void TestFloatingIconInk()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { RunFloatingIconInk(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

static void RunFloatingIconInk()
{
    using var bitmap = new Bitmap(303, 102);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        var (stateRow, routeRow) = FloatingCard.Paint(
            graphics, new Size(303, 102), "波动", "固定入口 · Verge",
            RuntimeHealthState.Fluctuating,
            new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            new Font("Microsoft YaHei UI", 9F), 144);

        // 两行文字必须都画进了卡片，且第二行紧贴第一行下方不重叠。
        Equal(true, stateRow.Height > 0);
        Equal(true, routeRow.Height > 0);
        Equal(true, routeRow.Top >= stateRow.Bottom);
    }

    // 墨迹重心：图标列（圆点）与文字列各自纵向质心，差值必须在 2px 内。
    var card = FloatingCard.CardBounds(new Size(303, 102), 144);
    var background = bitmap.GetPixel(card.Left + card.Width / 2, card.Bottom - 2);
    var paddingX = FloatingCard.Scale(12, 144);
    var iconColumn = FloatingCard.Scale(26, 144);
    var inset = FloatingCard.Scale(4, 144);
    var iconLeft = card.Left + paddingX;
    var split = iconLeft + iconColumn;
    var iconInk = InkCentroid(bitmap, iconLeft, split, card, background);
    var textInk = InkCentroid(bitmap, split + inset, card.Right - paddingX, card, background);
    Equal(true, iconInk >= 0);
    Equal(true, textInk >= 0);
    Equal(true, Math.Abs(iconInk - textInk) <= 4.0);
}

// 一列内"非背景像素"的纵向质心；没有墨迹时返回 -1。
static double InkCentroid(Bitmap bitmap, int left, int right, Rectangle card, Color background)
{
    var sum = 0.0;
    var count = 0;
    for (var y = card.Top + 2; y < card.Bottom - 2; y++)
        for (var x = left; x < right; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            var distance = Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) + Math.Abs(pixel.B - background.B);
            if (distance > 90) { sum += y; count++; }
        }

    return count == 0 ? -1 : sum / count;
}

// 玻璃卡片是半透明的，透出的是任意桌面背景；正文与副行在最坏背景（纯黑/纯白）下也必须可读。
// 系统亚克力实测只有 1.04:1，这条契约就是当初否掉亚克力方案的依据。
static void TestFloatingCardContrast()
{
    foreach (var palette in new[] { ThemePalette.Light, ThemePalette.Dark, ThemePalette.HighContrast })
    {
        var surface = FloatingCard.Surface(palette);
        // 普通主题是半透明玻璃；高对比度必须完全不透明，否则系统配色会被桌面冲淡。
        Equal(FloatingCard.AlphaFor(palette, useSystemColors: false), surface.A);
        Equal(palette.Name == ThemePalette.HighContrast.Name ? 255 : FloatingCard.CardAlpha, surface.A);

        foreach (var backdrop in new[] { Color.Black, Color.White })
        {
            var composited = FloatingCard.Composite(surface, backdrop);
            AtLeast(4.5, Contrast(FloatingCard.TextColor(palette).ToArgb(), composited.ToArgb()), $"{palette.Name} 主题正文（背景 {backdrop.Name}）");
            AtLeast(4.5, Contrast(FloatingCard.MutedColor(palette).ToArgb(), composited.ToArgb()), $"{palette.Name} 主题副行（背景 {backdrop.Name}）");
        }

        // 描边是纯装饰，不承担“识别组件/状态”的信息职责，因此不适用 3:1 对比度门槛；
        // 但要守住另一条契约——颜色必须在绘制时取自当前调色板，而不是像深色菜单那样在构造时烤死。
        Equal(palette.Card & 0xFFFFFF, FloatingCard.Surface(palette).ToArgb() & 0xFFFFFF);
        Equal(palette.Border & 0xFFFFFF, FloatingCard.BorderColor(palette).ToArgb() & 0xFFFFFF);
        Equal(palette.Text & 0xFFFFFF, FloatingCard.TextColor(palette).ToArgb() & 0xFFFFFF);
        Equal(palette.Muted & 0xFFFFFF, FloatingCard.MutedColor(palette).ToArgb() & 0xFFFFFF);
    }

    // 高对比度必须完全不透明：此时任何透底都会破坏系统配色。
    Equal(255, FloatingCard.AlphaFor(ThemePalette.HighContrast, useSystemColors: true));
    Equal(FloatingCard.CardAlpha, FloatingCard.AlphaFor(ThemePalette.Light, useSystemColors: false));
    // 用系统颜色时描边同样不透明，否则描边会被桌面冲淡。
    Equal(255, FloatingCard.BorderColor(ThemePalette.Light, useSystemColors: true).A);
    Equal(255, FloatingCard.Surface(ThemePalette.Light, useSystemColors: true).A);
}

static void TestLayeredSurfaceSourcePoint()
{
    // 根因回归：UpdateLayeredWindow 的 pptSrc 必须是有效的 POINT 指针，不能传 NULL。
    // 本机（Windows 10.0.26220）矩阵实验实测：pptSrc=NULL 时调用返回 True、GetLastError=0、
    // GetLayeredWindowAttributes 也不报错，但合成器一个像素都不画——四窗口对照里
    // A 组（&POINT{0,0}）整块 #FF0000 上屏，B 组（NULL）全屏零像素，两者返回值完全相同。
    // 因此这条契约只能靠签名固定：pptSrc 必须是 ref 结构体，不能退化成 IntPtr。
    var method = typeof(LayeredSurface).GetMethod(
        "UpdateLayeredWindow",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    Equal(true, method is not null);

    var parameters = method!.GetParameters();
    Equal(9, parameters.Length);

    // 参数序：hwnd, hdcDst, pptDst, psize, hdcSrc, pptSrc, crKey, pblend, dwFlags
    var sourcePoint = parameters[5].ParameterType;
    Equal(true, sourcePoint.IsByRef);
    var element = sourcePoint.GetElementType()!;
    Equal("NativePoint", element.Name);

    // 结构体必须是 {int X; int Y}，与原生 POINT 逐字段对齐。
    var fields = element.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
    Equal(2, fields.Length);
    Equal("X", fields[0].Name);
    Equal("Int32", fields[0].FieldType.Name);
    Equal("Y", fields[1].Name);
    Equal("Int32", fields[1].FieldType.Name);
}

static void TestLayeredSurfacePixels()
{
    // 分层窗口整卡自绘：像素必须自带 alpha 并已自左乘，否则 UpdateLayeredWindow
    // 会把整块矩形当不透明提交，圆角与投影退化成实心白块（真机已复现过）。
    using var bitmap = new Bitmap(4, 2);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.Transparent);
        graphics.FillRectangle(new SolidBrush(Color.FromArgb(128, 255, 0, 0)), new Rectangle(0, 0, 2, 1));
        graphics.FillRectangle(new SolidBrush(Color.FromArgb(255, 0, 0, 255)), new Rectangle(2, 0, 2, 1));
    }

    var bytes = LayeredSurface.PremultipliedBgra(bitmap);
    Equal(4 * 2 * 4, bytes.Length);

    // 全透明像素：颜色必须被清零，否则合成器会叠出灰边。
    Equal(0, bytes[16]);
    Equal(0, bytes[17]);
    Equal(0, bytes[18]);
    Equal(0, bytes[19]);

    // 半透明像素：通道按 alpha/255 自左乘（GDI+ 的 BGRA 行序：B, G, R, A）。
    Equal((byte)0, bytes[0]);
    Equal((byte)0, bytes[1]);
    Equal((byte)128, bytes[2]);
    Equal((byte)128, bytes[3]);

    // 不透明像素原样保留。
    Equal((byte)255, bytes[8]);
    Equal((byte)0, bytes[9]);
    Equal((byte)0, bytes[10]);
    Equal((byte)255, bytes[11]);
}

// ---------------------------------------------------------------------------
// 四项界面改造：上边栏/侧边栏深色统一、HTTP 行对齐、主题入口收进高级设置、开机自启。
// ---------------------------------------------------------------------------

// HTTP 行对齐的根因：标签沿用了 Label 的默认 AutoSize=true。WinForms 在 AutoSize 为真时
// 不使用 TextAlign，文字贴着标签顶部画；真机探针（150% DPI）实测标签墨迹中心比输入框文字高 5.7px，
// 而同样 Dock=Fill 的 _summary 因为显式写了 AutoSize=false 就没有这个问题。
// 契约三条：标签不自动尺寸、标签与输入框共用同一套 Margin、行高由输入框 PreferredHeight 推导
// （写死 40 在 100% DPI 下又会错开——那时输入框只有约 22px 高）。
static void TestProxyRowAlignment()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { RunProxyRowAlignment(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

static void RunProxyRowAlignment()
{
    var factory = UiTheme.RowLabel("当前本地 HTTP 入口");
    try
    {
        Equal(false, factory.AutoSize);
        Equal(true, factory.Dock == DockStyle.Fill);
        Equal(ContentAlignment.MiddleLeft, factory.TextAlign);
        Equal(new Padding(0, UiTheme.RowPadding, 0, UiTheme.RowPadding), factory.Margin);
    }
    finally { factory.Dispose(); }

    // 行高 = 输入框自身高度 + 上下留白，这样标签盒与输入框盒在每个 DPI 下都逐像素重合。
    Equal(38, UiTheme.RowHeight(30));
    Equal(0, UiTheme.RowHeight(0) - UiTheme.RowPadding * 2);

    using var host = new Form
    {
        Text = "行对齐验证",
        Font = new Font("Microsoft YaHei UI", 9F),
        AutoScaleMode = AutoScaleMode.Dpi,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-4000, -4000),
        ClientSize = new Size(720, 160)
    };
    host.Show();
    try
    {
        var input = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "例如 127.0.0.1:7890；留空时只检测系统默认网络",
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, UiTheme.RowPadding, 0, UiTheme.RowPadding)
        };
        var label = UiTheme.RowLabel("当前本地 HTTP 入口");
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            Height = UiTheme.RowHeight(input.PreferredHeight)
        };
        // 与产品一致：标签列按文字实际宽度留位置，写死 155 会裁掉「入口」两字。
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(label, 0, 0);
        row.Controls.Add(input, 1, 0);
        host.Controls.Add(row);
        host.PerformLayout();
        row.PerformLayout();

        Equal(true, row.Height > 0);
        Equal(input.PreferredHeight, label.Height);
        Equal(label.Height, input.Height);
        Equal(label.Top, input.Top);
        Equal(
            true,
            Math.Abs(label.Top + label.Height / 2 - (input.Top + input.Height / 2)) <= 1);

        // 不裁字：列宽必须放得下整行文字（150% DPI 实测需要 173px）。
        if (label.Width < label.PreferredSize.Width)
            throw new Exception($"clipped w={label.Width} pref={label.PreferredSize.Width} text={System.Windows.Forms.TextRenderer.MeasureText(label.Text, label.Font)}");
    }
    finally
    {
        host.Close();
    }
}

// 状态卡元信息必须按四行明确展示；更新时间单独作为最后一行，整体靠右排列。
// 卡片高度由布局自动增长，确保四行在不同 DPI 下都包含在卡片内。
static void TestStatusCardLayout()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { RunStatusCardLayout(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

static void RunStatusCardLayout()
{
    using var host = new Form
    {
        Text = "状态卡验证",
        Font = new Font("Microsoft YaHei UI", 9F),
        AutoScaleMode = AutoScaleMode.Dpi,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-4000, -4000),
        ClientSize = new Size(972, 260)
    };
    host.Show();
    try
    {
        var dot = new StatusDot();
        var title = new Label { Text = "波动", AutoSize = true, Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
        var detail = new Label { Text = "三次取样里出现一次超时，正在继续观察。", AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 2, 0, 2) };
        var notice = new Label { Text = "Codex 已在运行；切换线路前请先正常退出。", AutoSize = true, Visible = false, Margin = new Padding(0, 4, 0, 0) };
        var badge = new Label { Text = "!  波动", AutoSize = true, Padding = new Padding(10, 5, 10, 5), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) };
        var meta = new Label
        {
            Text = "路径：固定入口·Verge\n证据：线路 + 诊断事件\n耗时：3012 ms\n更新时间：20:12:12",
            AutoSize = true,
            MaximumSize = new Size(UiTheme.StatusMetaMaxWidth, 0),
            Margin = new Padding(0, 6, 0, 0)
        };

        var card = StatusCardLayout.Build(dot, title, detail, notice, badge, meta);
        card.Dock = DockStyle.Top;
        host.Controls.Add(card);
        host.PerformLayout();
        card.PerformLayout();
        foreach (Control child in card.Controls) child.PerformLayout();

        var area = meta.Parent!;

        // 更新时间是独立末行；所有元信息按右边缘对齐。
        Equal(ContentAlignment.MiddleRight, meta.TextAlign);
        Equal("更新时间：20:12:12", meta.Text.Split('\n')[^1]);

        // ①徽章靠右：右边缘贴住元信息列右边缘。
        Equal(true, badge.Right >= area.ClientSize.Width - 2);

        // ②元信息整块可见：底边与右边缘都必须落在列内。
        Equal(true, meta.Bottom <= area.ClientSize.Height);
        // ④元信息不焊死在卡片底边：底边与列底之间必须留出呼吸位——徽章在上、元信息在下，
        // 直接贴边看起来像被裁掉。徽章的右贴边契约（margin 全零）不受影响。
        AtLeast(4, area.ClientSize.Height - meta.Bottom, "元信息底部呼吸位");
        Equal(true, meta.Right <= area.ClientSize.Width);

        // 超长字符串由 MaximumSize 兜底，不能把正文列挤没。
        Equal(true, meta.Width <= UiTheme.StatusMetaMaxWidth);
        Equal(true, TextRenderer.MeasureText(meta.Text.Split('\n')[0], meta.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width <= UiTheme.StatusMetaMaxWidth);

        // 卡片高度必须装得下内容，不能靠写死 180 碰运气。
        var layout = (TableLayoutPanel)card.Controls[0];
        Equal(true, card.Height >= layout.PreferredSize.Height + 2);
    }
    finally
    {
        host.Close();
    }
}
// 展开高级设置后卡片必须跟着长高：真机 150% DPI 下卡片只长了 56px，
// 而展开后的面板需要约 102px，底部自启勾选框被裁掉 31px。
static void TestExpandedAdvancedPanel()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { RunExpandedAdvancedPanel(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

// 主窗 RelayoutFrom 的等价物：从被改的控件一路向上 PerformLayout。
static void RelayoutTwice(Control control)
{
    for (var pass = 0; pass < 2; pass++)
        for (var current = control; current is not null; current = current.Parent)
            current.PerformLayout();
}

// 面板用的是产品里的 UiTheme.WrapPanel：可换行的面板在上层用"无宽度约束"问首选尺寸时
// 会按不换行报数（真机 150% DPI 下报 52px，实际换行后是 112px，卡片因此少长 56px）。

static void RunExpandedAdvancedPanel()
{
    using var host = new Form
    {
        Text = "展开验证",
        Font = new Font("Microsoft YaHei UI", 9F),
        AutoScaleMode = AutoScaleMode.Dpi,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-4000, -4000),
        ClientSize = new Size(972, 200)
    };
    host.Show();
    try
    {
        // 结构与主窗一致：ScrollHost → root（AutoSize 行）→ 卡片（Dock=Fill）→ 版式 → 面板。
        // 视口故意比内容矮，逼出真机"内容溢出时才出现"的排版路径。
        var scroll = new ScrollHost { Dock = DockStyle.Fill, BackColor = UiTheme.Window };
        var root = new TableLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(24, 18, 24, 24),
            BackColor = UiTheme.Window
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // 主窗给自绘指示条留位后把宽度钉死：Width 与 MaximumSize 同时设，
        // 缺一个就无法复现真机"展开后卡片不长高"。
        var reserved = ScrollHost.IndicatorWidth(96) + ScrollHost.IndicatorMargin(96);
        var width = Math.Max(1, scroll.ClientSize.Width - reserved);
        root.MaximumSize = new Size(width, 0);
        root.Width = width;

        var card = new RoundedPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(22, 18, 22, 18), BackColor = Color.White };
        var heading = new Label { Text = "网络路径", AutoSize = true };
        layout.Controls.Add(heading);
        var panel = new UiTheme.WrapPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Visible = false, Margin = new Padding(0, 4, 0, 0) };
        var start = new Button { Text = "启动固定入口", AutoSize = true };
        var stop = new Button { Text = "停止固定入口", AutoSize = true };
        var choose = new Button { Text = "选择 Mihomo 程序", AutoSize = true };
        var selector = new Panel { Size = new Size(330, 46) };
        var check = new CheckBox { Text = "开机自动启动", AutoSize = true };
        panel.Controls.AddRange([start, stop, choose, selector, check]);
        layout.Controls.Add(panel);
        card.Controls.Add(layout);
        root.Controls.Add(card, 0, 0);
        scroll.SetContent(root);
        host.Controls.Add(scroll);
        host.PerformLayout();

        var collapsed = card.Height;

        panel.Visible = true;
        RelayoutTwice(panel);

        // 失败信息要带数字：光是"expected True, got False"看不出差了多少像素。
        if (Math.Max(0, panel.Bottom - layout.Height) > 0 ||
            Math.Max(0, layout.PreferredSize.Height - layout.Height) > 0 ||
            Math.Max(0, layout.PreferredSize.Height - card.Height) > 0 ||
            Math.Max(0, collapsed + panel.Height - card.Height) > 0)
        {
            throw new Exception($"collapsed={collapsed} card={card.Height} cardPref={card.PreferredSize.Height} layout={layout.Height} layoutPref={layout.PreferredSize.Height} layoutPrefW={layout.GetPreferredSize(new Size(layout.Width, 0)).Height} panel={panel.Height} panelBottom={panel.Bottom} panelPref={panel.PreferredSize.Height} panelPrefW={panel.GetPreferredSize(new Size(panel.Width, 0)).Height} heading={heading.Bottom} root={root.Height} rootPref={root.PreferredSize.Height} scroll={scroll.ClientSize.Height}");
        }
        Equal(0, Math.Max(0, panel.Bottom - layout.Height));
        Equal(0, Math.Max(0, layout.PreferredSize.Height - layout.Height));
        Equal(0, Math.Max(0, layout.PreferredSize.Height - card.Height));
        Equal(0, Math.Max(0, collapsed + panel.Height - card.Height));
    }
    finally
    {
        host.Close();
    }
}

// 标题栏由 DWM 上色。COLORREF 是 0x00BBGGRR，通道顺序与 ARGB 相反；
// 探针曾把绿色通道写成 -band 0xFF00 丢掉低位，送 0x11151A 实际发出 0x1A1111，
// 读回 #11111A，差点被误判成“系统会对颜色取整”。这条断言就是防止重犯。
static void TestWindowChromePlan()
{
    Equal(0x1A1511, WindowChrome.ColorRef(0x11151A));
    Equal(0xFFFFFF, WindowChrome.ColorRef(0xFFFFFF));
    Equal(0x000000, WindowChrome.ColorRef(0x000000));
    Equal(0xFF0000, WindowChrome.ColorRef(0x0000FF));
    Equal(0x0000FF, WindowChrome.ColorRef(0xFF0000));
    Equal(0xE0E4EA, WindowChrome.ColorRef(0xEAE4E0));

    foreach (var palette in new[] { ThemePalette.Light, ThemePalette.Dark })
    {
        var plan = WindowChrome.Plan(palette, useSystemColors: false);
        Equal(true, plan.Apply);
        Equal(WindowChrome.ColorRef(palette.Window), plan.Caption);
        Equal(WindowChrome.ColorRef(palette.Text), plan.Text);
        Equal(WindowChrome.ColorRef(palette.Border), plan.Border);
        Equal(WindowChrome.ColorRef(palette.Window), plan.Border == plan.Caption ? plan.Border : plan.Caption);
    }

    // 深色主题需要 attr 20 兜底（旧系统不认 35），浅色主题则必须关掉它，否则标题栏会变黑。
    Equal(true, WindowChrome.Plan(ThemePalette.Dark, useSystemColors: false).DarkFallback);
    Equal(false, WindowChrome.Plan(ThemePalette.Light, useSystemColors: false).DarkFallback);

    // 高对比度：交给系统，不覆写，否则系统配色会被自绘颜色冲掉。
    var high = WindowChrome.Plan(ThemePalette.HighContrast, useSystemColors: true);
    Equal(false, high.Apply);
    Equal(true, high.DarkFallback);

    // 属性号必须与 dwmapi.h 一致，否则会静默写到别的属性上而没有任何报错。
    Equal(35, WindowChrome.CaptionColorAttribute);
    Equal(36, WindowChrome.TextColorAttribute);
    Equal(34, WindowChrome.BorderColorAttribute);
    Equal(20, WindowChrome.ImmersiveDarkModeAttribute);
}

// 真窗口验证。这里<b>不</b>断言标题栏像素，原因是实测排除的：
//  - CopyFromScreen 会被压在上面的窗口挡住（本机屏幕点归属 Chrome_RenderWidgetHostHWND，
//    即使测试窗口 TopMost 且是前台窗口也照样被盖），一次绿一次红；
//  - PrintWindow 对 DWM 合成的标题栏一律返回纯黑（flags=0/2 都是 #000000x688）；
//  - DwmGetWindowAttribute 对只写的 attr 35/36/34 一律返回 E_INVALIDARG，读不回来。
// 所以唯一确定的信号是三个只写属性的写入 HRESULT：全部 S_OK 才说明本机 DWM 接受了这套颜色。
static void TestWindowChromeApply()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { RunWindowChromeApply(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

static void RunWindowChromeApply()
{
    using var form = new Form
    {
        Text = "Codex 启动器",
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-4000, -4000),
        ClientSize = new Size(420, 200)
    };
    form.Show();
    try
    {
        var handle = form.Handle;

        // 无效句柄必须安全跳过，不能抛异常炸掉界面。
        var missing = WindowChrome.Apply(IntPtr.Zero, ThemePalette.Dark, useSystemColors: false);
        Equal(false, missing.Applies);
        Equal(false, missing.Applied);

        // 深色主题：本机 DWM 必须接受整套颜色（8 次写入实测全部 S_OK）。
        var dark = WindowChrome.Apply(handle, ThemePalette.Dark, useSystemColors: false);
        Equal(true, dark.Applies);
        Equal(true, dark.Accepted);
        Equal(true, dark.Applied);

        // 切主题必须重发并且同样被接受：停在深色上就是"改了没生效"。
        var light = WindowChrome.Apply(handle, ThemePalette.Light, useSystemColors: false);
        Equal(true, light.Applies);
        Equal(true, light.Applied);

        var again = WindowChrome.Apply(handle, ThemePalette.Dark, useSystemColors: false);
        Equal(true, again.Applied);
        Equal(true, WindowChrome.Apply(handle, ThemePalette.Light, useSystemColors: false).Applied);

        // 高对比度：策略上不覆写，系统配色必须原样保留。
        var high = WindowChrome.Apply(handle, ThemePalette.HighContrast, useSystemColors: true);
        Equal(false, high.Applies);
        Equal(false, high.Applied);

        // 高对比度下即使传入"深色"调色板，只要 useSystemColors 为真也必须跳过：
        // 判定依据是"是否使用系统颜色"，不是调色板名字。
        Equal(false, WindowChrome.Apply(handle, ThemePalette.Dark, useSystemColors: true).Applied);

        // 窗口销毁后句柄失效：不能抛异常。
        var disposedHandle = handle;
        form.Close();
        var dead = WindowChrome.Apply(disposedHandle, ThemePalette.Dark, useSystemColors: false);
        Equal(false, dead.Applied);
    }
    finally
    {
        if (!form.IsDisposed) form.Close();
    }
}

// 侧边栏：系统滚动条在深色主题下没有可用的着色方式——SetWindowTheme("DarkMode_Explorer")
// 实测只把滑块涂成 #2E2E42，轨道仍是 #C2C2C2..#DDDDDD 的浅色渐变，比不改还难看。
// 所以改为自绘细指示条，几何必须严格由比例算出，颜色实时取自 UiTheme。
static void TestScrollHost()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { RunScrollHost(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

static void RunScrollHost()
{
    Equal(600, ScrollHost.MaxOffset(900, 300));
    Equal(0, ScrollHost.MaxOffset(300, 300));
    Equal(0, ScrollHost.MaxOffset(200, 300));
    Equal(0, ScrollHost.MaxOffset(900, 0));
    Equal(6, ScrollHost.IndicatorWidth(96));
    Equal(9, ScrollHost.IndicatorWidth(144));

    // 指示条长度与位置严格由比例决定，“看得见位置”才有意义。
    var bottom = ScrollHost.Thumb(300, 900, 600);
    Equal(100, bottom.Height);
    Equal(200, bottom.Y);
    Equal(0, ScrollHost.Thumb(300, 900, 0).Y);
    Equal(100, ScrollHost.Thumb(300, 900, 0).Height);
    Equal(100, ScrollHost.Thumb(300, 900, 300).Y);

    // 内容不溢出时不画指示条。
    Equal(Rectangle.Empty, ScrollHost.Thumb(300, 200, 0));
    Equal(Rectangle.Empty, ScrollHost.Thumb(300, 300, 0));
    Equal(Rectangle.Empty, ScrollHost.Thumb(0, 900, 0));

    // 极长内容也要留下可抓取的最小长度；越界偏移不能把指示条推出轨道。
    Equal(ScrollHost.MinimumThumbLength, ScrollHost.Thumb(300, 300000, 0).Height);
    var clamped = ScrollHost.Thumb(300, 900, 99999);
    Equal(200, clamped.Y);
    Equal(true, clamped.Bottom <= 300);

    var host = new ScrollHost { Size = new Size(400, 300) };
    var content = new Panel { Size = new Size(400, 900) };
    try
    {
        host.SetContent(content);
        Equal(false, host.AutoScroll);
        Equal(1, host.Controls.Count);
        Equal(true, host.IsOverflowing);
        Equal(900, host.ContentHeight);
        Equal(300, host.ViewportHeight);
        Equal(0, host.Offset);

        host.ScrollBy(100);
        Equal(100, host.Offset);
        Equal(-100, content.Location.Y);
        host.ScrollBy(99999);
        Equal(600, host.Offset);
        Equal(-600, content.Location.Y);
        host.ScrollBy(-99999);
        Equal(0, host.Offset);
        Equal(0, content.Location.Y);

        // 内容缩到放得下时必须回到顶部，不能留下一个悬空的偏移。
        host.ScrollBy(200);
        content.Size = new Size(400, 100);
        Equal(0, host.Offset);
        Equal(0, content.Location.Y);
        Equal(false, host.IsOverflowing);
        Equal(Rectangle.Empty, host.ThumbBounds);

        content.Size = new Size(400, 900);
        Equal(true, host.IsOverflowing);
        Equal(100, host.ThumbBounds.Height);

        // 主题跟随：指示条颜色必须实时读 UiTheme（不能像深色菜单那样在构造期烤死），
        // 且与页面背景保持 3:1 以上，否则“滚到哪里”这条信息读不出来。
        ThemeManager.ApplyTo(host, ThemePalette.Dark, useSystemColors: false);
        Equal(ThemePalette.Dark.Window, host.BackColor.ToArgb() & 0xFFFFFF);
        // ⑥指示条改为半透明柔和色：不透明度固定 140，色相取自各主题 Muted（高对比度保持不透明纯色）。
        // 对比度契约不变：半透明混合进页面背景的实际色仍须 3:1 可读。
        Equal(140, (host.ThumbColor.ToArgb() >> 24) & 0xFF);
        Equal(ThemePalette.Dark.Muted, host.ThumbColor.ToArgb() & 0xFFFFFF);
        Equal(ThemePalette.Dark.Border, host.TrackColor.ToArgb() & 0xFFFFFF);
        AtLeast(3.0, Contrast(host.ThumbColor.ToArgb(), host.BackColor.ToArgb()), "深色主题滚动指示条");

        ThemeManager.ApplyTo(host, ThemePalette.Light, useSystemColors: false);
        Equal(140, (host.ThumbColor.ToArgb() >> 24) & 0xFF);
        Equal(ThemePalette.Light.Muted, host.ThumbColor.ToArgb() & 0xFFFFFF);
        AtLeast(3.0, Contrast(host.ThumbColor.ToArgb(), host.BackColor.ToArgb()), "浅色主题滚动指示条");

        ThemeManager.ApplyTo(host, ThemePalette.HighContrast, useSystemColors: false);
        AtLeast(3.0, Contrast(host.ThumbColor.ToArgb(), host.BackColor.ToArgb()), "高对比度滚动指示条");

        // 指示条与轨道不能同色：同色时"滚到哪里"这条信息会被装饰吃掉。
        // 高对比度调色板下 Muted 与 Border 都是纯白，正确做法是不画轨道，而不是另造一种颜色。
        foreach (var (palette, railExpected) in new[]
        {
            (ThemePalette.Light, true), (ThemePalette.Dark, true), (ThemePalette.HighContrast, false)
        })
        {
            ThemeManager.ApplyTo(host, palette, useSystemColors: false);
            if (host.ThumbColor == host.BackColor) throw new Exception($"{palette.Name} 指示条与页面背景同色");
            if (host.ShowsRail && host.TrackColor == host.ThumbColor)
                throw new Exception($"{palette.Name} 画了轨道却与指示条同色");
            if (host.ShowsRail && host.TrackColor == host.BackColor)
                throw new Exception($"{palette.Name} 画了轨道却与页面背景同色");
            // 高对比度必须放弃装饰性轨道，保住指示条长度这条信息；浅色/深色应当保留。
            Equal(railExpected, host.ShowsRail);
            Equal(true, host.IsOverflowing);
        }

        // 内容宽度由调用方决定（主窗体要按 DPI 给指示条留空隙）。
        // 本控件若去改内容宽度，就会和 ResizeRoot 互相覆盖，右侧永远少一块或多一块。
        content.Size = new Size(400, 900);
        host.Size = new Size(500, 300);
        Equal(400, content.Width);
        Equal(true, host.IsOverflowing);
        Equal(300, host.ViewportHeight);

        // 滚轮路由：AutoScroll 关掉后系统不再派发，必须由本控件接管（单行 TextBox 会吞掉它）。
        host.Size = new Size(400, 300);
        content.Size = new Size(400, 900);
        host.SetOffset(0);
        var inside = host.PointToScreen(new Point(10, 10));
        Equal(true, host.TryHandleWheel(inside.X, inside.Y, -120));
        Equal(true, host.Offset > 0);
        // 光标不在本控件上时不能抢别人的滚轮。
        Equal(false, host.TryHandleWheel(0, 0, -120));
        // delta 为 0 不是有效滚轮事件。
        Equal(false, host.TryHandleWheel(inside.X, inside.Y, 0));
        // 内容放得下时滚轮无事可做，也不能拦截。
        content.Size = new Size(400, 100);
        Equal(false, host.TryHandleWheel(inside.X, inside.Y, -120));

        // 原生 ComboBox 下拉列表是独立 popup；它覆盖在 ScrollHost 上时，滚轮应留给列表，
        // 不能被全局消息过滤器按屏幕坐标误判为主页面滚动。
        using (var dropdownForm = new Form { ClientSize = new Size(400, 300), ShowInTaskbar = false, Opacity = 0 })
        using (var dropdownHost = new ScrollHost { Dock = DockStyle.Fill })
        {
            var dropdownContent = new Panel { Size = new Size(400, 900) };
            using var selector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(8, 8) };
            selector.Items.AddRange(["Wi-Fi", "Ethernet"]);
            dropdownContent.Controls.Add(selector);
            dropdownHost.SetContent(dropdownContent);
            dropdownForm.Controls.Add(dropdownHost);
            dropdownForm.Show();
            Application.DoEvents();
            selector.Focus();
            selector.DroppedDown = true;
            Application.DoEvents();
            Equal(true, selector.DroppedDown);
            var popupPoint = dropdownHost.PointToScreen(new Point(10, 10));
            var packedPoint = unchecked((popupPoint.X & 0xFFFF) | ((popupPoint.Y & 0xFFFF) << 16));
            var wheelMessage = Message.Create(dropdownHost.Handle, ScrollHost.WmMouseWheel, new IntPtr(120 << 16), new IntPtr(packedPoint));
            var beforePopupWheel = dropdownHost.Offset;
            Equal(false, ((IMessageFilter)dropdownHost).PreFilterMessage(ref wheelMessage));
            Equal(beforePopupWheel, dropdownHost.Offset);
            selector.DroppedDown = false;
            Application.DoEvents();
            Equal(true, dropdownHost.TryHandleWheel(popupPoint.X, popupPoint.Y, -120));
        }

        // 滚轮必须真的接到 ScrollHost 上：覆盖 OnMouseWheel 是入口之一，消息过滤是另一个，
        // 都不能被静默删掉（系统滚动条已经关掉了，少一条就少一条滚动路径）。
        var wheel = typeof(ScrollHost).GetMethod(
            "OnMouseWheel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Equal(true, wheel is not null && wheel.DeclaringType == typeof(ScrollHost));
        Equal(true, typeof(IMessageFilter).IsAssignableFrom(typeof(ScrollHost)));
    }
    finally
    {
        host.Dispose();
        UiTheme.Use(ThemePalette.Light, useSystemColors: false);
    }
}

// 顶部空白收缩到 12px，其他 root 留白保持 24px，页头和状态卡之间的 12px 间距不变。
// 断言走真实 MainForm 构造链（BuildInterface），防止有人把 root 换壳绕过契约。
static void TestTopPaddingReduction()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { RunTopPaddingReduction(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

static void RunTopPaddingReduction()
{
    MainForm? form = null;
    try
    {
        form = new MainForm((SingleInstanceGate?)null);
        // root 挂在 shell→scroll→root 三层里：Shell 的第 0 行就是 ScrollHost。
        var shell = form.Controls[0];
        var scroll = (ScrollHost)shell.Controls[0];
        var root = (TableLayoutPanel)scroll.Controls[0];
        Equal(12, root.Padding.Top);
        Equal(24, root.Padding.Bottom);
        Equal(24, root.Padding.Left);
        Equal(24, root.Padding.Right);
        var header = (TableLayoutPanel)root.GetControlFromPosition(0, 0)!;
        Equal(12, header.Margin.Bottom);
    }
    finally
    {
        form?.Dispose();
        UiTheme.Use(ThemePalette.Light, useSystemColors: false);
    }
}

// ⑤诊断表头自绘像素契约：SysHeader32 在深色主题下是亮色栏，自绘后必须
// 卡片同色背景（无背景栏）+ 1px 底边框 + Muted 文本（对比度 ≥3:1）。
// 直接画位图断言，不依赖真机截图（真机像素会被 DPI 污染）。
static void TestDiagnosticsHeader()
{
    foreach (var palette in new[] { ThemePalette.Light, ThemePalette.Dark })
    {
        // 先切主题再画：绘制用色必须实时读 UiTheme，这正是表头自绘的契约。
        UiTheme.Use(palette, useSystemColors: false);
        using var bitmap = new Bitmap(120, 28);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            DiagnosticsHeaderPainter.PaintColumn(
                graphics, new Rectangle(0, 0, 120, 28), "项目",
                UiTheme.Muted, UiTheme.Card, UiTheme.Border,
                new Font("Microsoft YaHei UI", 9F));
        }

        // 背景必须是卡片色（1px 底边框之上、文字行距之外的采样点）。
        var backdrop = bitmap.GetPixel(60, 4);
        Equal(true, backdrop.ToArgb() == UiTheme.Card.ToArgb());

        // 底边 1px 必须是边框色。
        var border = bitmap.GetPixel(60, 27);
        Equal(true, border.ToArgb() == UiTheme.Border.ToArgb());

        // 文字墨迹必须真实落进画布：列内非背景像素必须存在。
        var ink = 0;
        for (var x = 2; x < 118; x++)
        for (var y = 2; y < 26; y++)
        {
            if (bitmap.GetPixel(x, y).ToArgb() != UiTheme.Card.ToArgb()) ink++;
        }

        AtLeast(20, ink, "表头文字墨迹像素");
        AtLeast(3.0, Contrast(UiTheme.Muted.ToArgb(), UiTheme.Card.ToArgb()), $"{palette.Name} 表头文字对比度");

        // 主题跟随：画图时的用色必须与当前调色板一致，不能烤死。
        if (palette == ThemePalette.Dark)
        {
            Equal(ThemePalette.Dark.Card, backdrop.ToArgb() & 0xFFFFFF);
            Equal(ThemePalette.Dark.Border, border.ToArgb() & 0xFFFFFF);
            Equal(ThemePalette.Dark.Muted, UiTheme.Muted.ToArgb() & 0xFFFFFF);
        }
        else
        {
            Equal(ThemePalette.Light.Card, backdrop.ToArgb() & 0xFFFFFF);
            Equal(ThemePalette.Light.Border, border.ToArgb() & 0xFFFFFF);
        }
    }

    UiTheme.Use(ThemePalette.Light, useSystemColors: false);
}

static void TestDiagnosticsRowsFit()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            using var form = new MainForm((SingleInstanceGate?)null);
            var results = (ListView)typeof(MainForm).GetField("_results", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            var setRow = typeof(MainForm).GetMethod("SetRow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            // 原生 ListView 在隐藏期间延迟提交新增行；使用透明测试宿主让它走真实展开流程。
            using var host = new Form { ClientSize = new Size(800, 750), ShowInTaskbar = false, Opacity = 0 };
            host.Controls.Add(results);
            results.Visible = true;
            host.Show();
            foreach (var size in new[] { 9f, 11.25f, 13.5f, 18f })
            {
                using var font = new Font("Microsoft YaHei UI", size);
                results.Font = font;
                foreach (var palette in new[] { ThemePalette.Light, ThemePalette.Dark })
                {
                    ThemeManager.ApplyTo(form, palette, false);
                    ThemeManager.ApplyTo(host, palette, false);
                    for (var i = 0; i < 16; i++) setRow.Invoke(form, [$"诊断 {i}", "当前检测结果", null]);
                    Application.DoEvents();
                    var first = results.GetItemRect(0);
                    if (first.Height <= 0 || results.ClientSize.Height < first.Top + first.Height * results.Items.Count)
                        throw new Exception($"diagnostic layout clipped: size={results.ClientSize}; first={first}; count={results.Items.Count}");
                    Equal(false, results.Scrollable);
                    Equal(true, results.Height >= results.MinimumSize.Height);
                    Equal(true, results.MinimumSize.Height >= first.Top + first.Height * results.Items.Count);
                }
            }
        }
        catch (Exception ex) { failure = new Exception(ex.ToString(), ex); }
        finally { UiTheme.Use(ThemePalette.Light, useSystemColors: false); }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

static void TestDiagnosticsHandleRecreation()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, true);
            using var form = new MainForm((SingleInstanceGate?)null);
            var results = (ListView)typeof(MainForm).GetField("_results", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            var setRow = typeof(MainForm).GetMethod("SetRow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var host = new Form { ClientSize = new Size(800, 750), ShowInTaskbar = false, Opacity = 0 };
            host.Controls.Add(results);
            results.Visible = true;
            host.Show();
            for (var i = 0; i < 16; i++) setRow.Invoke(form, [$"句柄诊断 {i}", "检测结果", null]);
            Application.DoEvents();
            var count = results.Items.Count;
            typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(results, null);
            typeof(MainForm).GetMethod("ResizeResultsHeight", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            Application.DoEvents();
            Equal(count, results.Items.Count);
            Equal(true, results.ClientSize.Height >= results.Font.Height * count);
        }
        catch (Exception ex) { failure = new Exception(ex.ToString(), ex); }
        finally { UiTheme.Use(ThemePalette.Light, useSystemColors: false); }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

static void TestDiagnosticsExpandAfterHiddenRows()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            using var form = new MainForm((SingleInstanceGate?)null);
            var results = (ListView)typeof(MainForm).GetField("_results", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            var toggle = (Button)typeof(MainForm).GetField("_diagnosticsToggle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            var setRow = typeof(MainForm).GetMethod("SetRow", BindingFlags.Instance | BindingFlags.NonPublic)!;

            // 模拟程序已运行、诊断列表仍折叠的状态。
            using var host = new Form { ClientSize = new Size(800, 750), ShowInTaskbar = false, Opacity = 0 };
            results.Dock = DockStyle.Fill;
            toggle.Dock = DockStyle.Top;
            host.Controls.Add(results);
            host.Controls.Add(toggle);
            host.Show();
            Application.DoEvents();

            // 程序先在折叠状态收集诊断，用户随后点击按钮展开。
            for (var i = 0; i < 16; i++) setRow.Invoke(form, [$"隐藏诊断 {i}", "当前检测结果", null]);
            Equal(false, results.Visible);
            Equal(true, results.Items.Cast<ListViewItem>().Any(item => item.Text == "隐藏诊断 0"));
            var rowCount = results.Items.Count;

            toggle.PerformClick();
            Application.DoEvents();

            Equal(true, results.Visible);
            Equal(rowCount, results.Items.Count);
        }
        catch (Exception ex) { failure = new Exception(ex.ToString(), ex); }
        finally { UiTheme.Use(ThemePalette.Light, useSystemColors: false); }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

// 主题入口有两处：高级设置里的选择器，以及托盘菜单里的主题子菜单。
// 两处必须共用同一条同步路径，否则同一个设置有两个真相来源，漏同步就会出现“改了没生效”。
static void TestThemeSelector()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { RunThemeSelector(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

static void RunThemeSelector()
{
    const System.Reflection.BindingFlags Flags =
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    Equal(true, typeof(MainForm).GetField("_themeSelector", Flags) is not null);
    // 主题与自启都有两个入口（高级设置 + 托盘菜单）。两个入口必须落在同一份状态上：
    // 托盘菜单抽成 TrayMenu，就是为了能在不实例化 MainForm 的前提下断言这条同步契约。
    Equal(true, typeof(MainForm).GetField("_tray", Flags) is not null);

    var selector = new ThemeSelector();
    try
    {
        Equal(3, selector.Buttons.Count);
        Equal("跟随系统", selector.Buttons[0].Text);
        Equal("浅色", selector.Buttons[1].Text);
        Equal("深色", selector.Buttons[2].Text);
        Equal(ThemeMode.System, selector.Mode);
        Equal(true, selector.Buttons[0].Primary);
        Equal(false, selector.Buttons[1].Primary);

        var changes = new List<ThemeMode>();
        selector.ModeChanged += mode => changes.Add(mode);

        selector.Buttons[2].PerformClick();
        Equal(ThemeMode.Dark, selector.Mode);
        Equal(1, changes.Count);
        Equal(ThemeMode.Dark, changes[0]);
        Equal(true, selector.Buttons[2].Primary);
        Equal(false, selector.Buttons[0].Primary);

        selector.Buttons[1].PerformClick();
        Equal(ThemeMode.Light, selector.Mode);
        Equal(2, changes.Count);
        Equal(true, selector.Buttons[1].Primary);
        Equal(false, selector.Buttons[2].Primary);

        // 重复点同一项不得再发事件（否则会白写一次配置并重建一次主题）。
        selector.Buttons[1].PerformClick();
        Equal(2, changes.Count);

        // 外部同步（读配置后回填）必须只改显示、不发事件：
        // 发了就会变成"读配置→写配置"的回写环（SyncThemeMenu 由 SetThemeMode 调用）。
        selector.SetMode(ThemeMode.Light);
        Equal(2, changes.Count);
        Equal(ThemeMode.Light, selector.Mode);

        selector.SetMode(ThemeMode.System);
        Equal(2, changes.Count);
        Equal(ThemeMode.System, selector.Mode);
        Equal(true, selector.Buttons[0].Primary);

        // 主题跟随：按钮颜色必须实时取当前调色板，选中态用主色面、未选中用次级面。
        selector.SetMode(ThemeMode.Dark);
        ThemeManager.ApplyTo(selector, ThemePalette.Dark, useSystemColors: false);
        Equal(ThemePalette.Dark.PrimaryButton.Background, selector.Buttons[2].BackColor.ToArgb() & 0xFFFFFF);
        Equal(ThemePalette.Dark.SecondaryButton.Background, selector.Buttons[1].BackColor.ToArgb() & 0xFFFFFF);
        AtLeast(4.5, Contrast(selector.Buttons[2].ForeColor.ToArgb(), selector.Buttons[2].BackColor.ToArgb()), "深色主题选中项");

        ThemeManager.ApplyTo(selector, ThemePalette.Light, useSystemColors: false);
        Equal(ThemePalette.Light.PrimaryButton.Background, selector.Buttons[2].BackColor.ToArgb() & 0xFFFFFF);
        Equal(ThemePalette.Light.SecondaryButton.Background, selector.Buttons[0].BackColor.ToArgb() & 0xFFFFFF);
    }
    finally
    {
        selector.Dispose();
        UiTheme.Use(ThemePalette.Light, useSystemColors: false);
    }
}

// 开机自启：注册表是唯一真相，不是“我们最后一次写过的值”。
// 用户从任务管理器禁用启动项后，界面必须立刻反映真实状态，而不是继续显示已启用。
static void TestAutoStartRegistration()
{
    Equal("CodexLauncher", AutoStartRegistration.ValueName);
    Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", RegistryAutoStartRegistry.RunKeyPath);
    Equal("\"C:\\Tools\\CodexLauncher.exe\" --background", AutoStartRegistration.Command(@"C:\Tools\CodexLauncher.exe"));
    Equal(
        "\"C:\\Program Files\\Codex Launcher\\CodexLauncher.exe\" --background",
        AutoStartRegistration.Command(@"C:\Program Files\Codex Launcher\CodexLauncher.exe"));

    var registry = new MemoryAutoStartRegistry();
    var registration = new AutoStartRegistration(registry);
    const string current = @"C:\Apps\CodexLauncher.exe";
    const string moved = @"D:\Tools\CodexLauncher\CodexLauncher.exe";

    // 默认不开机自启：注册表里没有值时必须报“未启用”。
    Equal(false, registration.IsEnabled(current));
    Equal(null, registration.CurrentCommand);

    Equal(true, registration.TryApply(true, current).Succeeded);
    Equal(true, registration.IsEnabled(current));
    Equal(AutoStartRegistration.Command(current), registration.CurrentCommand);

    // Windows 路径不区分大小写。
    Equal(true, registration.IsEnabled(@"c:\apps\CODEXLAUNCHER.EXE"));

    // 已经是目标状态就不许再写注册表：每次启动都重写会惊动安全软件，也说明状态判断没生效。
    var writesBefore = registry.Writes;
    Equal(true, registration.TryApply(true, current).Succeeded);
    Equal(writesBefore, registry.Writes);

    // 换了安装位置必须重写，不能因为“曾经写过”就认为已启用。
    Equal(false, registration.IsEnabled(moved));
    Equal(true, registration.TryApply(true, moved).Succeeded);
    Equal(AutoStartRegistration.Command(moved), registration.CurrentCommand);
    Equal(true, registration.IsEnabled(moved));
    Equal(false, registration.IsEnabled(current));

    // 用户从外部删掉自启项：状态必须跟着回到未启用。
    registry.Value = null;
    Equal(false, registration.IsEnabled(moved));

    // 关闭自启必须删掉键值，不能写空字符串（空值会让 Windows 登录时报错）。
    Equal(true, registration.TryApply(true, moved).Succeeded);
    Equal(true, registration.TryApply(false, moved).Succeeded);
    Equal(null, registration.CurrentCommand);
    Equal(false, registration.IsEnabled(moved));
    Equal(null, registry.Value);

    // 注册表写不进去（权限/组策略）时不能抛异常炸掉界面，要能报告失败原因。
    var denied = new AutoStartRegistration(new DeniedAutoStartRegistry());
    var result = denied.TryApply(true, current);
    Equal(false, result.Succeeded);
    Equal(nameof(UnauthorizedAccessException), result.ErrorType);
    Equal(false, denied.IsEnabled(current));
}

// 主题与自启各有两个入口（高级设置 + 托盘菜单）。两个入口必须落在同一份状态上，
// 否则"在托盘里改了主题、高级设置里还显示跟随系统"就是必然结果。
static void TestTrayMenuContract()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { RunTrayMenuContract(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
}

static void RunTrayMenuContract()
{
    using var selector = new ThemeSelector();
    var opened = 0;
    var refreshed = 0;
    var exited = 0;
    var themes = new List<ThemeMode>();
    var autoStarts = new List<bool>();
    var floats = new List<bool>();

    using var tray = new TrayMenu(() => opened++, () => refreshed++, () => exited++);
    tray.ThemeRequested += mode => themes.Add(mode);
    tray.AutoStartRequested += enabled => autoStarts.Add(enabled);
    tray.FloatingRequested += () => floats.Add(true);

    // 托盘里的主题文案必须与高级设置逐字一致：两处说法不同，用户会以为是两套设置。
    Equal(selector.Buttons[0].Text, tray.ThemeItem(ThemeMode.System).Text);
    Equal(selector.Buttons[1].Text, tray.ThemeItem(ThemeMode.Light).Text);
    Equal(selector.Buttons[2].Text, tray.ThemeItem(ThemeMode.Dark).Text);
    Equal(3, tray.ThemeRoot.DropDownItems.Count);

    // 勾只能由真实状态回填。CheckOnClick = true 会让鼠标一点就把菜单里的勾翻过来，
    // 而注册表有没有写成功还不知道——等 SyncAutoStart 回填时界面已经先撒了谎。
    Equal(false, tray.AutoStartItem.CheckOnClick);
    Equal(false, tray.FloatingItem.CheckOnClick);
    foreach (var pair in tray.ThemeItems) Equal(false, tray.ThemeItem(pair.Key).CheckOnClick);

    // 回填只改显示、不发事件：发了就是"读配置→写配置"的回写环。
    tray.SyncTheme(ThemeMode.Dark);
    Equal(0, themes.Count);
    Equal(true, tray.ThemeItem(ThemeMode.Dark).Checked);
    Equal(false, tray.ThemeItem(ThemeMode.System).Checked);

    // 点已选中的那一项不必再写一次配置。
    tray.ThemeItem(ThemeMode.Dark).PerformClick();
    Equal(0, themes.Count);

    tray.ThemeItem(ThemeMode.Light).PerformClick();
    Equal(1, themes.Count);
    Equal(ThemeMode.Light, themes[0]);
    tray.SyncTheme(ThemeMode.Light);
    tray.ThemeItem(ThemeMode.Light).PerformClick();
    Equal(1, themes.Count);

    // 自启：勾选框是注册表真值的回显，点击只报告"想变成什么"。
    tray.SyncAutoStart(false);
    Equal(0, autoStarts.Count);
    tray.AutoStartItem.PerformClick();
    Equal(1, autoStarts.Count);
    Equal(true, autoStarts[0]);

    tray.SyncAutoStart(true);
    Equal(true, tray.AutoStartItem.Checked);
    tray.AutoStartItem.PerformClick();
    Equal(2, autoStarts.Count);
    Equal(false, autoStarts[1]);

    // 写入失败（权限被拒）后回填未选中，且不得自动重试第二次写入。
    tray.SyncAutoStart(false);
    Equal(false, tray.AutoStartItem.Checked);
    Equal(2, autoStarts.Count);

    tray.SyncFloating(true);
    Equal(true, tray.FloatingItem.Checked);
    Equal(0, floats.Count);
    tray.FloatingItem.PerformClick();
    Equal(1, floats.Count);
    tray.SyncFloating(false);
    Equal(false, tray.FloatingItem.Checked);

    // 托盘固定三项。
    Click(tray.Menu, "打开主窗口");
    Equal(1, opened);
    Click(tray.Menu, "立即刷新");
    Equal(1, refreshed);
    Click(tray.Menu, "退出启动器");
    Equal(1, exited);

    // 主题子菜单也必须跟着换色：它是另一套下拉画布，不注册就永远是浅色。
    UiTheme.Use(ThemePalette.Dark, useSystemColors: false);
    Equal(UiTheme.Card, tray.Menu.BackColor);
    Equal(UiTheme.Text, tray.Menu.ForeColor);
    Equal(UiTheme.Card, ((ToolStripDropDown)tray.ThemeItem(ThemeMode.System).Owner!).BackColor);
    UiTheme.Use(ThemePalette.Light, useSystemColors: false);
    Equal(UiTheme.Card, ((ToolStripDropDown)tray.ThemeItem(ThemeMode.System).Owner!).BackColor);
}

static void Click(ToolStrip menu, string text)
{
    foreach (ToolStripItem item in menu.Items)
        if (item is ToolStripMenuItem found && found.Text == text)
        {
            found.PerformClick();
            return;
        }

    throw new Exception($"托盘菜单里找不到“{text}”");
}

static void TestFloatingWindowPlacement()
{
    var primary = new ScreenArea(0, 0, 1920, 1040);
    var secondary = new ScreenArea(1920, -200, 2560, 1440);
    var screens = new[] { primary, secondary };

    // 记录位置仍在可见工作区内：保持不动。
    var kept = FloatingPlacement.Resolve(1650, 900, 220, 56, screens);
    Equal(1650, kept.X);
    Equal(900, kept.Y);

    // 完全在屏幕外：拉回最近的可见工作区。
    var offscreen = FloatingPlacement.Resolve(-400, 3000, 220, 56, screens);
    Equal(true, primary.Contains(offscreen.X, offscreen.Y));
    Equal(true, primary.Contains(offscreen.X + 220 - 1, offscreen.Y + 56 - 1));

    // 副屏被拔掉：回落到主屏可见区域，而不是留在不存在的坐标上。
    var removed = FloatingPlacement.Resolve(2400, 100, 220, 56, new[] { primary });
    Equal(true, primary.Contains(removed.X, removed.Y));

    // 缩放变化后窗口变大：必须整体落在某一台屏幕的工作区内。
    var scaled = FloatingPlacement.Resolve(1750, 1000, 360, 90, screens);
    Equal(true, screens.Any(s => s.Contains(scaled.X, scaled.Y) && s.Contains(scaled.X + 360 - 1, scaled.Y + 90 - 1)));

    // 拿不到屏幕信息时不抛异常，回落到原点。
    var empty = FloatingPlacement.Resolve(500, 500, 220, 56, Array.Empty<ScreenArea>());
    Equal(0, empty.X);
    Equal(0, empty.Y);
}

static void TestSingleInstanceActivation()
{
    var name = $"CodexLauncher.Test.{Guid.NewGuid():N}";
    using var first = new SingleInstanceGate(name);
    Equal(true, first.TryAcquire());

    // 第二次启动不能开启第二个后台监测，而是通知已有实例。
    using (var second = new SingleInstanceGate(name))
    {
        Equal(false, second.TryAcquire());
        Equal(false, second.WaitForActivation(TimeSpan.FromMilliseconds(50)));
        first.SignalActivation();
        Equal(true, second.WaitForActivation(TimeSpan.FromSeconds(5)));
    }

    // 已有实例退出后必须能重新启动。
    first.Dispose();
    using var third = new SingleInstanceGate(name);
    Equal(true, third.TryAcquire());
}

static void AtLeast(double minimum, double actual, string what)
{
    if (actual < minimum) throw new Exception($"{what}: expected >= {minimum}, got {actual:0.00}");
}

static double Contrast(int foreground, int background)
{
    var a = RelativeLuminance(foreground);
    var b = RelativeLuminance(background);
    var (high, low) = a >= b ? (a, b) : (b, a);
    return (high + 0.05) / (low + 0.05);
}

static double RelativeLuminance(int argb)
{
    var r = Linear((argb >> 16) & 0xFF);
    var g = Linear((argb >> 8) & 0xFF);
    var b = Linear(argb & 0xFF);
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;

    static double Linear(int value)
    {
        var channel = value / 255.0;
        return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}

static void TestCloseButtonReason()
{
    var root = Path.Combine(Path.GetTempPath(), "codex-msix-root");
    var app = TestInstallation(root);
    var desktopPath = Path.Combine(root, "app", "ChatGPT.exe");
    var startedAt = new DateTimeOffset(2026, 10, 1, 8, 7, 11, TimeSpan.FromHours(8));
    var instance = VerifiedInstance(131, startedAt, desktopPath, app);

    var unknown = CodexCloseButtonPolicy.Evaluate(null, closeInProgress: false);
    Equal(false, unknown.Enabled);
    Equal("关闭 Codex", unknown.Text);
    Equal(true, unknown.Reason.Contains("尚未确认", StringComparison.Ordinal));

    var notRunning = CodexCloseButtonPolicy.Evaluate(
        new CodexDesktopDiscovery(CodexDesktopState.NotRunning, null, "未发现 Codex 桌面进程。", false),
        closeInProgress: false);
    Equal(false, notRunning.Enabled);
    Equal("Codex 未运行", notRunning.Reason);

    var unverified = CodexCloseButtonPolicy.Evaluate(
        new CodexDesktopDiscovery(
            CodexDesktopState.Unverified, null, "发现同名进程，但无法读取它的可执行文件路径（权限不足）；不会结束任何进程。", false),
        closeInProgress: false);
    Equal(false, unverified.Enabled);
    Equal(true, unverified.Reason.Contains("无法确认", StringComparison.Ordinal));

    var denied = CodexCloseButtonPolicy.Evaluate(
        new CodexDesktopDiscovery(
            CodexDesktopState.PermissionDenied, null, "无法读取 Codex 桌面进程信息（权限不足）；不会结束任何进程。", false),
        closeInProgress: false);
    Equal(false, denied.Enabled);
    Equal(true, denied.Reason.Contains("权限", StringComparison.Ordinal));

    var noWindow = CodexCloseButtonPolicy.Evaluate(
        new CodexDesktopDiscovery(
            CodexDesktopState.Running, instance, "已确认桌面进程 131，但未找到主窗口；不会请求关闭。", false),
        closeInProgress: false);
    Equal(false, noWindow.Enabled);
    Equal(true, noWindow.Reason.Contains("主窗口", StringComparison.Ordinal));

    var running = CodexCloseButtonPolicy.Evaluate(
        new CodexDesktopDiscovery(CodexDesktopState.Running, instance, "已确认桌面进程 131。", true),
        closeInProgress: false);
    Equal(true, running.Enabled);
    Equal("关闭 Codex", running.Text);

    var closing = CodexCloseButtonPolicy.Evaluate(
        new CodexDesktopDiscovery(CodexDesktopState.Running, instance, "已确认桌面进程 131。", true),
        closeInProgress: true);
    Equal(false, closing.Enabled);
    Equal(true, closing.Reason.Contains("正在关闭", StringComparison.Ordinal));
}

static void TestDetectorIdentity()
{
    var root = Path.Combine(Path.GetTempPath(), "codex-msix-root");
    var app = TestInstallation(root);
    var desktopPath = Path.Combine(root, "app", "ChatGPT.exe");
    var siblingPath = root + "-other" + Path.DirectorySeparatorChar + "app" + Path.DirectorySeparatorChar + "ChatGPT.exe";
    var foreignPath = Path.Combine("C:" + Path.DirectorySeparatorChar, "Program Files", "Other", "ChatGPT.exe");
    var startedAt = new DateTimeOffset(2026, 10, 1, 8, 7, 11, TimeSpan.FromHours(8));

    var sibling = new FakeProcessControl();
    sibling.Add(DesktopProcess(41, siblingPath, startedAt));
    var siblingDetector = new CodexProcessDetector(sibling);
    Equal(false, siblingDetector.IsRunning(app));
    Equal(CodexDesktopState.NotRunning, siblingDetector.Discover(app).State);

    var foreign = new FakeProcessControl();
    foreign.Add(DesktopProcess(42, foreignPath, startedAt));
    var foreignDetector = new CodexProcessDetector(foreign);
    Equal(false, foreignDetector.IsRunning(app));
    Equal(CodexDesktopState.NotRunning, foreignDetector.Discover(app).State);

    var denied = new FakeProcessControl { ListThrows = true };
    var deniedDetector = new CodexProcessDetector(denied);
    var deniedDiscovery = deniedDetector.Discover(app);
    Equal(CodexDesktopState.PermissionDenied, deniedDiscovery.State);
    Equal(true, deniedDiscovery.PossiblyRunning);
    Equal(true, deniedDetector.IsRunning(app));

    var genuine = new FakeProcessControl();
    genuine.Add(DesktopProcess(43, desktopPath, startedAt));
    genuine.SetWindow(43, new IntPtr(880001), "ChatGPT");
    var genuineDetector = new CodexProcessDetector(genuine);
    Equal(true, genuineDetector.IsRunning(app));
    var genuineDiscovery = genuineDetector.Discover(app);
    Equal(CodexDesktopState.Running, genuineDiscovery.State);
    Equal(43, genuineDiscovery.Instance?.ProcessId ?? 0);

    var unreadable = new FakeProcessControl();
    unreadable.Add(DesktopProcess(44, string.Empty, startedAt));
    var unreadableDetector = new CodexProcessDetector(unreadable);
    Equal(CodexDesktopState.Unverified, unreadableDetector.Discover(app).State);
    Equal(true, unreadableDetector.IsRunning(app));
}

static CodexInstance VerifiedInstance(int processId, DateTimeOffset startedAt, string path, CodexInstallation app) =>
    new()
    {
        ProcessId = processId,
        StartedAt = startedAt,
        ExecutablePath = path,
        PackageFamilyName = app.PackageFamilyName,
        BackendProcessIds = []
    };

static async Task<Exception?> CaptureAsync(Func<Task> work)
{
    try
    {
        await work();
        return null;
    }
    catch (Exception exception)
    {
        return exception;
    }
}

static async Task TestLocalDiagnosticSourceAsync()
{
    var app = new CodexAppLocator(new ProcessCommandRunner()).FindAsync(CancellationToken.None).GetAwaiter().GetResult();
    if (app is null) throw new Exception("Codex MSIX not installed");

    var database = CodexDiagnosticSource.ResolveDatabasePath();
    if (database is null) throw new Exception("未找到本机 Codex 诊断库");
    Console.WriteLine($"  Diagnostic database: {database}");

    // 用真实桌面进程构造可验证实例身份，再按只读增量方式读取一次。
    var desktop = System.Diagnostics.Process.GetProcessesByName("ChatGPT")
        .FirstOrDefault(process => process.MainModule?.FileName is { } file
            && CodexInstance.IsPathWithin(file, app.InstallLocation));
    if (desktop is null) throw new Exception("未找到运行中的 Codex 桌面进程");
    using (desktop)
    {
        var backends = System.Diagnostics.Process.GetProcessesByName("codex")
            .Where(process => process.Id != desktop.Id)
            .Select(process => process.Id).ToArray();
        Console.WriteLine($"  Desktop pid {desktop.Id}, backend pids [{string.Join(", ", backends)}]");

        var instance = new CodexInstance
        {
            ProcessId = desktop.Id,
            StartedAt = desktop.StartTime,
            ExecutablePath = desktop.MainModule?.FileName ?? "",
            PackageFamilyName = app.PackageFamilyName,
            BackendProcessIds = backends
        };
        Equal(true, instance.HasVerifiedIdentity);

        var source = new CodexDiagnosticSource(database);
        var first = await source.ReadSinceAsync(DiagnosticCursor.Empty, instance, CancellationToken.None);
        Console.WriteLine($"  First read: available={first.SourceAvailable} cursor={first.Cursor.LastId} reason={first.UnavailableReason ?? "none"}");
        if (!first.SourceAvailable)
            throw new Exception($"诊断源不可用：{first.UnavailableReason}");

        // 真实运行中的库应能读到本实例的记录；给 Codex 一点时间产生新的网络记录。
        var second = await source.ReadSinceAsync(first.Cursor, instance, CancellationToken.None);
        Console.WriteLine($"  Immediate second read: events={second.Events.Count} cursor={second.Cursor.LastId}");
        await Task.Delay(4000);
        var third = await source.ReadSinceAsync(second.Cursor, instance, CancellationToken.None);
        Console.WriteLine($"  After 4s: events={third.Events.Count} cursor={third.Cursor.LastId}");
        foreach (var item in third.Events.Take(5)) Console.WriteLine($"    {item.Timestamp:HH:mm:ss} {item.Kind} {item.Category}");
    }
}

static void TestLocalInstallation()
{
    var app = new CodexAppLocator(new ProcessCommandRunner()).FindAsync(CancellationToken.None).GetAwaiter().GetResult();
    if (app is null) throw new Exception("Codex MSIX not installed");
    Equal(true, app.Aumid.EndsWith("!App", StringComparison.Ordinal));
    var bin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
    Equal(true, File.Exists(BundledCliFinder.Find(bin)));
    Console.WriteLine($"  Found {app.Aumid}; active process: {new CodexProcessDetector().IsRunning(app)}");
}

/// <summary>实机额度：读取本机已登录账号，核对窗口、百分比、重置时间与账号归属。</summary>
static async Task TestLocalQuotaAsync()
{
    var factory = CodexAppServerSessionFactory.ForCurrentUser();
    if (factory is null) throw new Exception("未找到配套 Codex CLI");
    Console.WriteLine($"  CLI: {CodexAppServerSessionFactory.LocateCli()}");

    // 代理解析链取证：本机必须能解析出可用代理，并把它注入配额 CLI 子进程。
    var resolved = QuotaProxyEnvironment.Resolve();
    Console.WriteLine($"  ProxySource: {resolved.Source ?? "(none)"} · Proxy: {resolved.Proxy?.ToString() ?? "(direct)"}");
    Console.WriteLine($"  InjectedKeys: {resolved.Environment.Count}");
    Equal(true, resolved.Environment.Count > 0);

    await using var provider = new CodexQuotaProvider(factory);
    var snapshot = await provider.ReadAsync(CancellationToken.None);
    Console.WriteLine($"  Availability: {snapshot.Availability} · {snapshot.AccountLabel} · {snapshot.LevelLabel}");
    Console.WriteLine($"  Detail: {snapshot.Detail}");
    if (snapshot.FailureCategory is { } category) Console.WriteLine($"  FailureCategory: {category}");
    foreach (var bucket in snapshot.Buckets)
        Console.WriteLine($"  Bucket {bucket.DisplayName} ({bucket.LimitId}) restricted={bucket.Restricted}");

    Equal(true, snapshot.Availability is QuotaAvailability.Available or QuotaAvailability.Unsupported
        or QuotaAvailability.NotLoggedIn);
    Equal(false, snapshot.CheckedAt == default);
    Equal(true, snapshot.AccountLabel.Length > 0);

    // 有数据时逐窗口核对：百分比必须来自接口，未知字段保持未知而不是 0%。
    if (snapshot.HasData)
    {
        Console.WriteLine($"  Windows: {snapshot.DescribeWindows(DateTimeOffset.Now).Replace("\n", " | ")}");
        Equal(true, snapshot.Buckets.Count > 0);
        foreach (var bucket in snapshot.Buckets)
        {
            Equal(true, bucket.Windows.Count > 0);
            foreach (var window in bucket.Windows)
            {
                Equal(true, window.Name.Length > 0);
                Equal(true, window.DurationLabel.Length > 0);
                if (window.RemainingPercent is { } percent) Equal(true, percent is >= 0 and <= 100);
                Console.WriteLine($"    {window.Describe(DateTimeOffset.Now)}");
            }
        }
    }
}

// ---------------------------------------------------------------------------
// 额度：解析、账号归属、刷新周期与 stdio 会话
// ---------------------------------------------------------------------------

static void TestQuotaParsing()
{
    var now = DateTimeOffset.Parse("2026-10-01T12:00:00+08:00");
    var primaryReset = DateTimeOffset.FromUnixTimeSeconds(1790832653);
    var secondaryReset = DateTimeOffset.FromUnixTimeSeconds(1791419453);

    // 多桶：优先 rateLimitsByLimitId，两个桶都要读出来。
    var buckets = QuotaResponseParser.ReadBuckets(FakeQuotaJson.RateLimits, now);
    Equal(2, buckets.Count);
    Equal("codex", buckets[0].LimitId);
    Equal("codex", buckets[0].DisplayName);
    Equal(2, buckets[0].Windows.Count);
    Equal("主要窗口", buckets[0].Windows[0].Name);
    Equal("次要窗口", buckets[0].Windows[1].Name);
    Equal("5 小时", buckets[0].Windows[0].DurationLabel);
    Equal(300L, buckets[0].Windows[0].DurationMinutes);
    Equal(85, buckets[0].Windows[0].RemainingPercent);
    Equal("每周", buckets[0].Windows[1].DurationLabel);
    Equal(10080L, buckets[0].Windows[1].DurationMinutes);
    Equal(98, buckets[0].Windows[1].RemainingPercent);
    Equal("扩展额度", buckets[1].DisplayName);
    Equal(12, buckets[1].Windows[0].RemainingPercent);
    Equal("1 天", buckets[1].Windows[0].DurationLabel);

    // resetsAt 是 Unix 秒：必须换算成同一个时刻，不能当成毫秒。
    Equal(primaryReset, buckets[0].Windows[0].ResetsAt!.Value.ToUniversalTime());
    Equal(secondaryReset, buckets[0].Windows[1].ResetsAt!.Value.ToUniversalTime());
    Equal($"重置 {primaryReset.ToLocalTime():MM-dd HH:mm}", buckets[0].Windows[0].ResetLabel);

    // 旧协议：没有 rateLimitsByLimitId 时回落到 rateLimits 单桶。
    var legacy = QuotaResponseParser.ReadBuckets(FakeQuotaJson.LegacyRateLimits, now);
    Equal(1, legacy.Count);
    Equal("codex", legacy[0].LimitId);
    Equal(60, legacy[0].Windows[0].RemainingPercent);
    Equal(1, legacy[0].Windows.Count);

    // 窗口长度只在匹配时使用易懂名称，不把示例写死。
    Equal("5 小时", QuotaResponseParser.DurationLabel(300));
    Equal("每周", QuotaResponseParser.DurationLabel(10080));
    Equal("1 天", QuotaResponseParser.DurationLabel(1440));
    Equal("2 小时", QuotaResponseParser.DurationLabel(120));
    Equal("90 分钟", QuotaResponseParser.DurationLabel(90));
    Equal("窗口长度未知", QuotaResponseParser.DurationLabel(null));

    // 倒计时到点后显示“等待确认重置”，不自行填成 100%。
    Equal("还剩 1 小时 30 分", new QuotaWindow("主要窗口", "5 小时", 300, 85, primaryReset.ToLocalTime()).CountdownLabel(now));
    Equal("还剩 25 分", new QuotaWindow("主要窗口", "5 小时", 300, 85, now.AddMinutes(25)).CountdownLabel(now));
    Equal("等待确认重置", new QuotaWindow("主要窗口", "5 小时", 300, 85, now.AddSeconds(-1)).CountdownLabel(now));
    Equal("倒计时未知", new QuotaWindow("主要窗口", "窗口长度未知", null, null, null).CountdownLabel(now));

    // 快照汇总：最低剩余决定等级，查询账号与计划进入账号标签。
    var account = QuotaResponseParser.ReadAccount(FakeQuotaJson.Account);
    Equal(QuotaAccountKind.ChatGpt, account.Kind);
    Equal("test-workspace", account.WorkspaceAccountId);
    Equal(true, account.RequiresOpenAiAuth);
    var snapshot = QuotaResponseParser.Build(account, FakeQuotaJson.RateLimits, now);
    Equal(QuotaAvailability.Available, snapshot.Availability);
    Equal(true, snapshot.HasData);
    Equal(2, snapshot.Buckets.Count);
    Equal(12, snapshot.LowestRemainingPercent);
    Equal(QuotaLevel.Low, snapshot.Level);
    Equal("额度偏低", snapshot.LevelLabel);
    Equal("quota-test@example.com · Plus", snapshot.AccountLabel);
    Equal("额度：主要窗口 85% · 次要窗口 98% · 主要窗口 12%", snapshot.Summary);
    Equal(true, snapshot.DescribeWindows(now).Contains("主要窗口（5 小时）剩余 85%"));
    Equal(true, snapshot.Detail.Contains("clamp(100 - usedPercent, 0, 100)"));
    Equal(false, snapshot.IsStale);
    Equal(false, snapshot.ResetPending);
}

static void TestQuotaUnknownValues()
{
    var now = DateTimeOffset.Parse("2026-10-01T12:00:00+08:00");
    var account = QuotaResponseParser.ReadAccount(FakeQuotaJson.Account);

    // 缺字段：保持未知，界面显示“暂不可用”，绝不填成 0%。
    var missing = QuotaResponseParser.ReadBuckets(
        """{"rateLimitsByLimitId":{"codex":{"limitId":"codex","primary":{}}}}""", now);
    Equal(1, missing.Count);
    Equal(1, missing[0].Windows.Count);
    Equal(false, missing[0].Windows[0].IsKnown);
    Equal("暂不可用", missing[0].Windows[0].RemainingLabel);
    Equal("窗口长度未知", missing[0].Windows[0].DurationLabel);
    Equal("重置时间未知", missing[0].Windows[0].ResetLabel);
    Equal("倒计时未知", missing[0].Windows[0].CountdownLabel(now));
    Equal("主要窗口（窗口长度未知）剩余 暂不可用 · 重置时间未知 · 倒计时未知", missing[0].Windows[0].Describe(now));

    var unknown = QuotaResponseParser.Build(account,
        """{"rateLimitsByLimitId":{"codex":{"limitId":"codex","primary":{}}}}""", now);
    Equal(true, unknown.HasData);
    Equal(true, unknown.LowestRemainingPercent is null);
    Equal(QuotaLevel.Unknown, unknown.Level);
    Equal("额度未知", unknown.LevelLabel);
    Equal("额度：主要窗口 暂不可用", unknown.Summary);

    // 超范围百分比：按 clamp 收进 0—100。
    var clamped = QuotaResponseParser.ReadBuckets(
        """{"rateLimitsByLimitId":{"codex":{"limitId":"codex","primary":{"usedPercent":140,"windowDurationMins":300},"secondary":{"usedPercent":-20,"windowDurationMins":300}}}}""", now);
    Equal(0, clamped[0].Windows[0].RemainingPercent);
    Equal(100, clamped[0].Windows[1].RemainingPercent);

    // 接口真的返回 100 已用才是 0%，这是真实数据而不是伪造。
    var exhausted = QuotaResponseParser.ReadBuckets(
        """{"rateLimitsByLimitId":{"codex":{"limitId":"codex","primary":{"usedPercent":100,"windowDurationMins":300}}}}""", now);
    Equal(0, exhausted[0].Windows[0].RemainingPercent);
    Equal("0%", exhausted[0].Windows[0].RemainingLabel);

    // resetsAt 为 0 或负数视为未提供。
    var zeroReset = QuotaResponseParser.ReadBuckets(
        """{"rateLimitsByLimitId":{"codex":{"limitId":"codex","primary":{"usedPercent":10,"resetsAt":0}}}}""", now);
    Equal(true, zeroReset[0].Windows[0].ResetsAt is null);
    Equal("重置时间未知", zeroReset[0].Windows[0].ResetLabel);

    // 重置时间已过：显示等待确认重置，百分比保持接口原值。
    var passed = QuotaResponseParser.Build(account,
        """{"rateLimitsByLimitId":{"codex":{"limitId":"codex","primary":{"usedPercent":50,"windowDurationMins":300,"resetsAt":1000}}}}""", now);
    Equal(true, passed.ResetPending);
    Equal("等待确认重置", passed.Buckets[0].Windows[0].Note);
    Equal("等待确认重置", passed.Buckets[0].Windows[0].CountdownLabel(now));
    Equal(50, passed.Buckets[0].Windows[0].RemainingPercent);

    // 没有数据时一律未知，不能显示成 0%。
    var unavailable = QuotaSnapshot.Unavailable("额度查询失败，暂不可用。", now, "test");
    Equal(false, unavailable.HasData);
    Equal(true, unavailable.LowestRemainingPercent is null);
    Equal(QuotaLevel.Unknown, unavailable.Level);
    Equal("额度未知", unavailable.LevelLabel);
    Equal("额度：暂不可用", unavailable.Summary);
    Equal(0, unavailable.Buckets.Count);

    // 空桶（接口没有返回窗口）同样不算有数据。
    var empty = QuotaResponseParser.Build(account, """{"rateLimitsByLimitId":{}}""", now);
    Equal(false, empty.HasData);
    Equal(true, empty.Detail.Contains("接口未返回额度窗口"));
}

static void TestQuotaAccountReporting()
{
    var now = DateTimeOffset.Parse("2026-10-01T12:00:00+08:00");

    // API Key 登录：不提供订阅额度，也不推算余额。
    var apiKey = QuotaResponseParser.ReadAccount("""{"account":{"type":"apiKey"},"requiresOpenaiAuth":true}""");
    Equal(QuotaAccountKind.ApiKey, apiKey.Kind);
    var apiKeySnapshot = QuotaResponseParser.Build(apiKey, null, now);
    Equal(QuotaAvailability.Unsupported, apiKeySnapshot.Availability);
    Equal("额度：此登录方式不提供订阅额度", apiKeySnapshot.Summary);
    Equal(false, apiKeySnapshot.HasData);
    Equal("API Key 登录 · 计划未知", apiKeySnapshot.AccountLabel);
    Equal(true, apiKeySnapshot.Detail.Contains("不推算余额"));

    // 未登录：提示需要先登录配套 CLI，启动器不自动登录。
    var anonymous = QuotaResponseParser.ReadAccount("""{"account":null,"requiresOpenaiAuth":true}""");
    Equal(QuotaAccountKind.Unknown, anonymous.Kind);
    var anonymousSnapshot = QuotaResponseParser.Build(anonymous, null, now);
    Equal(QuotaAvailability.NotLoggedIn, anonymousSnapshot.Availability);
    Equal("额度：需要先登录配套 CLI", anonymousSnapshot.Summary);
    Equal(true, anonymousSnapshot.Detail.Contains("不会自动登录"));

    // Bedrock 登录同样不提供订阅额度。
    var bedrock = QuotaResponseParser.ReadAccount("""{"account":{"type":"amazonBedrock","usesCodexManagedCredentials":true}}""");
    Equal(QuotaAccountKind.Bedrock, bedrock.Kind);
    Equal(QuotaAvailability.Unsupported, QuotaResponseParser.Build(bedrock, null, now).Availability);

    // 计划名称：匹配时用易懂名称，未知计划原样显示。
    Equal("Plus", QuotaResponseParser.PlanLabel("plus"));
    Equal("Pro Lite", QuotaResponseParser.PlanLabel("prolite"));
    Equal("Pro Max", QuotaResponseParser.PlanLabel("promax"));
    Equal("Free", QuotaResponseParser.PlanLabel("free"));
    Equal("enterprise_cbp_automation", QuotaResponseParser.PlanLabel("enterprise_cbp_automation"));
    Equal("计划未知", QuotaResponseParser.PlanLabel(null));

    // 与桌面账号的一致性默认未知：必须提示核对，不得声称一致。
    var chatgpt = QuotaResponseParser.ReadAccount(FakeQuotaJson.Account);
    var snapshot = QuotaResponseParser.Build(chatgpt, FakeQuotaJson.RateLimits, now);
    Equal("quota-test@example.com", snapshot.AccountEmail);
    Equal("plus", snapshot.PlanType);
    Equal(QuotaAccountConsistency.Unconfirmed, snapshot.Consistency);
    Equal("请核对桌面账号。", snapshot.ConsistencyHint);
    Equal(true, (snapshot with { Consistency = QuotaAccountConsistency.Confirmed }).ConsistencyHint is null);
    Equal("额度账号与桌面账号不一致，请核对桌面账号。",
        (snapshot with { Consistency = QuotaAccountConsistency.Mismatch }).ConsistencyHint);

    // 只有 ChatGPT 登录才请求订阅额度接口。
    var apiKeyFactory = new FakeQuotaFactory { Account = """{"account":{"type":"apiKey"},"requiresOpenaiAuth":true}""" };
    var apiKeyProvider = new CodexQuotaProvider(apiKeyFactory, () => now);
    var gated = apiKeyProvider.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
    Equal(QuotaAvailability.Unsupported, gated.Availability);
    Equal(1, apiKeyFactory.AccountReads);
    Equal(0, apiKeyFactory.RateLimitReads);
    Equal("""{"refreshToken":false}""", apiKeyFactory.LastSession!.LastParameters);
    Equal(true, apiKeyFactory.LastSession!.Methods.All(QuotaRequestWhitelist.IsAllowed));

    var chatGptFactory = new FakeQuotaFactory();
    var chatGptProvider = new CodexQuotaProvider(chatGptFactory, () => now);
    var allowed = chatGptProvider.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
    Equal(QuotaAvailability.Available, allowed.Availability);
    Equal(1, chatGptFactory.RateLimitReads);
    Equal(true, chatGptFactory.LastSession!.Methods.SequenceEqual(new[] { "account/read", "account/rateLimits/read", "account/read" }));
}

static async Task TestQuotaScheduleAsync()
{
    var clock = new TestClock(DateTimeOffset.Parse("2026-10-01T12:00:00+08:00"));

    // 周期常量：5 分钟刷新、10 秒手动间隔、10 秒单次超时、10 分钟新鲜度。
    Equal(TimeSpan.FromMinutes(5), QuotaSchedule.RefreshInterval);
    Equal(TimeSpan.FromSeconds(10), QuotaSchedule.ManualInterval);
    Equal(TimeSpan.FromSeconds(10), QuotaSchedule.RequestTimeout);
    Equal(TimeSpan.FromMinutes(10), QuotaSchedule.StaleThreshold);

    var schedule = new QuotaSchedule(() => clock.Now);
    Equal(QuotaSchedule.RefreshInterval, schedule.CurrentInterval);
    schedule.RecordAttempt();
    Equal(clock.Now + TimeSpan.FromMinutes(5), schedule.NextDue!.Value);
    Equal(TimeSpan.FromMinutes(5), schedule.DelayUntilDue(null));

    // 失败退避：5、10、20、30 分钟，封顶 30 分钟。
    schedule.RecordFailure();
    Equal(TimeSpan.FromMinutes(5), schedule.CurrentInterval);
    schedule.RecordFailure();
    Equal(TimeSpan.FromMinutes(10), schedule.CurrentInterval);
    schedule.RecordFailure();
    Equal(TimeSpan.FromMinutes(20), schedule.CurrentInterval);
    schedule.RecordFailure();
    Equal(TimeSpan.FromMinutes(30), schedule.CurrentInterval);
    schedule.RecordFailure();
    Equal(TimeSpan.FromMinutes(30), schedule.CurrentInterval);
    Equal(5, schedule.FailureCount);

    // 成功后恢复 5 分钟周期。
    schedule.RecordSuccess();
    Equal(0, schedule.FailureCount);
    Equal(QuotaSchedule.RefreshInterval, schedule.CurrentInterval);

    // 手动刷新至少间隔 10 秒。
    Equal(true, schedule.CanRequestManually);
    schedule.RecordManual();
    Equal(false, schedule.CanRequestManually);
    Equal(TimeSpan.FromSeconds(10), schedule.ManualRetryAfter);
    clock.Advance(TimeSpan.FromSeconds(9));
    Equal(false, schedule.CanRequestManually);
    clock.Advance(TimeSpan.FromSeconds(1));
    Equal(true, schedule.CanRequestManually);

    // 重置到点时提前一次查询；有退避时不提前。
    schedule.RecordSuccess();
    schedule.RecordAttempt();
    Equal(TimeSpan.FromMinutes(5), schedule.DelayUntilDue(null));
    Equal(TimeSpan.FromMinutes(2), schedule.DelayUntilDue(clock.Now + TimeSpan.FromMinutes(2)));
    Equal(TimeSpan.FromMinutes(5), schedule.DelayUntilDue(clock.Now + TimeSpan.FromMinutes(30)));
    Equal(TimeSpan.Zero, schedule.DelayUntilDue(clock.Now - TimeSpan.FromMinutes(1)));
    schedule.RecordFailure();
    schedule.RecordFailure();
    Equal(TimeSpan.FromMinutes(10), schedule.DelayUntilDue(clock.Now + TimeSpan.FromMinutes(2)));

    // 新鲜度：从未成功或超过 10 分钟即为过期。
    var freshness = new QuotaSchedule(() => clock.Now);
    Equal(true, freshness.IsStale);
    Equal(true, freshness.Age is null);
    freshness.RecordSuccess();
    Equal(false, freshness.IsStale);
    clock.Advance(TimeSpan.FromMinutes(11));
    Equal(true, freshness.IsStale);
    Equal(true, freshness.Age >= TimeSpan.FromMinutes(11));

    // 提供者按 5 分钟周期自动刷新，并在通知到达时提前唤醒。
    var loopClock = new TestClock(DateTimeOffset.Parse("2026-10-01T12:00:00+08:00"));
    var factory = new FakeQuotaFactory();
    var delays = new List<TimeSpan>();
    var firstDelay = new TaskCompletionSource();
    await using var provider = new CodexQuotaProvider(factory, () => loopClock.Now, async (span, token) =>
    {
        lock (delays) delays.Add(span);
        firstDelay.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
    });
    provider.Start();
    await firstDelay.Task.WaitAsync(TimeSpan.FromSeconds(10));
    lock (delays) Equal(TimeSpan.FromMinutes(5), delays[0]);
    Equal(2, factory.AccountReads);

    // 手动刷新：10 秒内重复请求不再查询，10 秒后放行。
    var reads = factory.AccountReads;
    await provider.RequestManualAsync(CancellationToken.None);
    Equal(reads + 2, factory.AccountReads);
    await provider.RequestManualAsync(CancellationToken.None);
    Equal(reads + 2, factory.AccountReads);
    loopClock.Advance(TimeSpan.FromSeconds(10));
    await provider.RequestManualAsync(CancellationToken.None);
    Equal(reads + 4, factory.AccountReads);

    // 服务端推送 account/rateLimits/updated：唤醒循环重新查询。
    var before = factory.AccountReads;
    factory.LastSession!.Raise("account/rateLimits/updated");
    await WaitUntilAsync(() => factory.AccountReads > before, "额度更新通知应触发一次重新查询");
    await provider.StopAsync(CancellationToken.None);
    Equal(false, provider.Schedule.FailureCount > 0);
}

static async Task TestQuotaStaleAsync()
{
    var clock = new TestClock(DateTimeOffset.Parse("2026-10-01T12:00:00+08:00"));
    var factory = new FakeQuotaFactory();
    await using var provider = new CodexQuotaProvider(factory, () => clock.Now);

    var first = await provider.ReadAsync(CancellationToken.None);
    Equal(QuotaAvailability.Available, first.Availability);
    Equal(true, first.HasData);
    Equal(85, first.Buckets[0].Windows[0].RemainingPercent);
    Equal(false, first.IsStale);
    Equal(true, first.FreshnessNote is null);
    Equal(clock.Now, first.CheckedAt);

    // 失败时保留当前账号上次结果，标明数据未更新且不刷新检查时间。
    factory.Fail = true;
    clock.Advance(TimeSpan.FromMinutes(6));
    var second = await provider.ReadAsync(CancellationToken.None);
    Equal(true, second.HasData);
    Equal(85, second.Buckets[0].Windows[0].RemainingPercent);
    Equal(false, second.IsStale);
    Equal(true, second.FreshnessNote!.Contains("数据未更新"));
    Equal(first.CheckedAt, second.CheckedAt);
    Equal("fake-failure", second.FailureCategory);

    // 超过 10 分钟标记为可能已过期，摘要同步提示。
    clock.Advance(TimeSpan.FromMinutes(11));
    var third = await provider.ReadAsync(CancellationToken.None);
    Equal(true, third.IsStale);
    Equal(true, third.FreshnessNote!.Contains("数据可能已过期"));
    Equal(true, third.Summary.Contains("数据可能已过期"));
    Equal(85, third.Buckets[0].Windows[0].RemainingPercent);

    // 恢复成功后清除过期标记并回到 5 分钟周期。
    factory.Fail = false;
    clock.Advance(TimeSpan.FromMinutes(1));
    var fourth = await provider.ReadAsync(CancellationToken.None);
    Equal(false, fourth.IsStale);
    Equal(true, fourth.FreshnessNote is null);
    Equal(0, provider.Schedule.FailureCount);
    Equal(QuotaSchedule.RefreshInterval, provider.Schedule.CurrentInterval);

    // 账号切换：立即清除上个账号的额度缓存，旧数字不得挂到新账号上。
    var switchClock = new TestClock(DateTimeOffset.Parse("2026-10-01T12:00:00+08:00"));
    var switcher = new FakeQuotaFactory { Email = "first@example.com" };
    await using var switching = new CodexQuotaProvider(switcher, () => switchClock.Now);
    var published = new List<QuotaSnapshot>();
    switching.QuotaChanged += snapshot => { lock (published) published.Add(snapshot); };

    var before = await switching.ReadAsync(CancellationToken.None);
    Equal("first@example.com", before.AccountEmail);
    Equal(85, before.Buckets[0].Windows[0].RemainingPercent);

    switcher.Email = "second@example.com";
    switcher.RateLimitsFail = true;
    var after = await switching.ReadAsync(CancellationToken.None);
    Equal(false, after.HasData);
    Equal(QuotaAvailability.Unavailable, after.Availability);
    Equal(true, after.LowestRemainingPercent is null);
    lock (published) Equal(true, published.Any(snapshot => snapshot.Detail.Contains("检测到额度账号已切换")));
}

static async Task TestQuotaStdioAsync()
{
    // 只读白名单：只有握手与两个账号接口，对话、登录、购买、兑换、邮件全部拒绝。
    Equal(3, QuotaRequestWhitelist.AllowedMethods.Count);
    foreach (var allowed in QuotaRequestWhitelist.AllowedMethods)
    {
        Equal(true, QuotaRequestWhitelist.IsAllowed(allowed));
        Equal(false, QuotaRequestWhitelist.IsForbidden(allowed));
    }
    foreach (var forbidden in new[]
             {
                 "turn/start", "turn/steer", "thread/start", "review/start", "account/login/start",
                 "account/login/cancel", "account/logout", "account/rateLimitResetCredit/consume",
                 "account/sendAddCreditsNudgeEmail", "account/gatewayOAuth/login", "account/workspaceMessages/read",
                 "feedback/upload"
             })
    {
        Equal(false, QuotaRequestWhitelist.IsAllowed(forbidden));
        Equal(true, QuotaRequestWhitelist.IsForbidden(forbidden));
    }
    foreach (var forbidden in QuotaRequestWhitelist.ForbiddenMethods)
        Equal(false, QuotaRequestWhitelist.IsAllowed(forbidden));

    // 日志路径用环境变量传递：App Server 会话按空格拆分参数，路径含空格会拆坏。
    var log = Path.Combine(Path.GetTempPath(), $"cl-quota-{Guid.NewGuid():N}.log");
    var (executable, arguments) = FakeServerCommand("--mode noisy");
    Environment.SetEnvironmentVariable("CL_QUOTA_FAKE_LOG", log);
    try
    {
        var factory = new CodexAppServerSessionFactory(executable, arguments,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        await using var provider = new CodexQuotaProvider(factory);
        var snapshot = await provider.ReadAsync(CancellationToken.None);
        Equal(QuotaAvailability.Available, snapshot.Availability);
        Equal("quota-test@example.com", snapshot.AccountEmail);
        Equal("plus", snapshot.PlanType);
        Equal(2, snapshot.Buckets.Count);
        Equal(85, snapshot.Buckets[0].Windows[0].RemainingPercent);
        Equal(98, snapshot.Buckets[0].Windows[1].RemainingPercent);
        Equal("扩展额度", snapshot.Buckets[1].DisplayName);
        Equal(12, snapshot.Buckets[1].Windows[0].RemainingPercent);

        // 请求编号关联：无关通知与未知编号都不能干扰真正的响应；
        // 只有这三个白名单方法；account/read 再核对一次，防止读取之间换号。
        var lines = await WaitForLinesAsync(log, 4);
        Equal(4, lines.Count);
        Equal("initialize", LogMethod(lines[0]));
        Equal("account/read", LogMethod(lines[1]));
        Equal("account/rateLimits/read", LogMethod(lines[2]));
        Equal("account/read", LogMethod(lines[3]));
        Equal("""{"refreshToken":false}""", LogParameters(lines[1]));
        Equal(true, LogParameters(lines[0])!.Contains("\"name\":\"codex-launcher\""));
        Equal(true, LogParameters(lines[0])!.Contains("\"experimentalApi\":false"));

        // 会话本身也必须拒绝白名单外的方法，而不是只靠调用方自律。
        var (directExe, directArgs) = FakeServerCommand("--mode ok");
        var direct = await CodexAppServerSession.OpenAsync(directExe, directArgs,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        await using (direct)
        {
            var turn = await CaptureAsync(() => direct.InvokeAsync("turn/start", null, CancellationToken.None));
            Equal(true, turn is InvalidOperationException);
            Equal(true, turn!.Message.Contains("白名单"));
            var logout = await CaptureAsync(() => direct.InvokeAsync("account/logout", null, CancellationToken.None));
            Equal(true, logout is InvalidOperationException);
        }

        // 旧协议：只有 rateLimits 单桶字段时仍能解析。
        var (legacyExe, legacyArgs) = FakeServerCommand("--mode legacy");
        var legacyFactory = new CodexAppServerSessionFactory(legacyExe, legacyArgs,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        await using var legacyProvider = new CodexQuotaProvider(legacyFactory);
        var legacySnapshot = await legacyProvider.ReadAsync(CancellationToken.None);
        Equal(QuotaAvailability.Available, legacySnapshot.Availability);
        Equal(1, legacySnapshot.Buckets.Count);
        Equal(60, legacySnapshot.Buckets[0].Windows[0].RemainingPercent);
    }
    finally { TryDelete(log); }
}

static async Task TestQuotaUnavailableAsync()
{
    var clock = new TestClock(DateTimeOffset.Parse("2026-10-01T12:00:00+08:00"));

    // CLI 缺失：如实说明，不伪造额度。
    await using (var missing = new CodexQuotaProvider(null, () => clock.Now))
    {
        var snapshot = await missing.ReadAsync(CancellationToken.None);
        Equal(QuotaAvailability.Unavailable, snapshot.Availability);
        Equal("cli-missing", snapshot.FailureCategory);
        Equal(true, snapshot.Detail.Contains("未找到配套 Codex CLI"));
        Equal(false, snapshot.HasData);
        Equal(QuotaLevel.Unknown, snapshot.Level);
        Equal("额度：暂不可用", snapshot.Summary);
        Equal(1, missing.Schedule.FailureCount);
    }

    // 单次请求超时：降级为暂不可用，不保留半截数据。
    var (timeoutExe, timeoutArgs) = FakeServerCommand("--mode timeout");
    await using (var slow = new CodexQuotaProvider(
        new CodexAppServerSessionFactory(timeoutExe, timeoutArgs,
            TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(1500)),
        () => clock.Now))
    {
        var snapshot = await slow.ReadAsync(CancellationToken.None);
        Equal(QuotaAvailability.Unavailable, snapshot.Availability);
        Equal("timeout", snapshot.FailureCategory);
        Equal(true, snapshot.Detail.Contains("超时"));
        Equal(true, snapshot.Detail.Contains("读取额度失败", StringComparison.Ordinal));
        Equal(true, snapshot.Detail.Contains("诊断码：timeout", StringComparison.Ordinal));
        Equal(false, snapshot.HasData);
        Equal(true, snapshot.Detail.Contains("10 秒"));
    }

    // 查询进程退出：降级为暂不可用；stderr 原文不得进入快照。
    var (exitExe, exitArgs) = FakeServerCommand("--mode exit");
    await using (var exited = new CodexQuotaProvider(
        new CodexAppServerSessionFactory(exitExe, exitArgs, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)),
        () => clock.Now))
    {
        var snapshot = await exited.ReadAsync(CancellationToken.None);
        Equal(QuotaAvailability.Unavailable, snapshot.Availability);
        Equal("process-exited", snapshot.FailureCategory);
        Equal(true, snapshot.Detail.Contains("已退出"));
        Equal(false, snapshot.HasData);
        var text = string.Join("|", snapshot.Title, snapshot.Detail, snapshot.Summary, snapshot.AccountLabel,
            snapshot.FreshnessNote, snapshot.FailureCategory);
        Equal(false, text.Contains("stderr-secret"));
        Equal(false, text.Contains("@example.com"));
    }
}

static async Task TestQuotaSessionProxyEnvironmentAsync()
{
    // 配额 CLI 是启动器自有短命子进程：代理环境只进子进程，绝不污染启动器自身进程。
    var log = Path.Combine(Path.GetTempPath(), $"cl-quota-{Guid.NewGuid():N}.log");
    var (executable, arguments) = FakeServerCommand("--mode ok");
    var previousProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY");
    Environment.SetEnvironmentVariable("CL_QUOTA_FAKE_ENV", log);
    Environment.SetEnvironmentVariable("CL_QUOTA_FAKE_LOG", log);
    Environment.SetEnvironmentVariable("HTTPS_PROXY", null);
    try
    {
        var environment = ProxyEnvironment.ForUser(RouteKind.Proxy, ProxyAddress.Parse("http://127.0.0.1:7890"));
        var factory = new CodexAppServerSessionFactory(executable, arguments,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), environment);
        await using var provider = new CodexQuotaProvider(factory);
        var snapshot = await provider.ReadAsync(CancellationToken.None);
        Equal(QuotaAvailability.Available, snapshot.Availability);

        var lines = await WaitForLinesAsync(log, 4);
        var envLine = lines.FirstOrDefault(line => line.StartsWith("#env|", StringComparison.Ordinal));
        if (envLine is null) throw new Exception("模拟服务器没有记录子进程代理环境");
        Equal(true, envLine.Contains("HTTPS_PROXY=http://127.0.0.1:7890/"));
        Equal(true, envLine.Contains("NO_PROXY=localhost,127.0.0.1,::1"));
        Equal(null, Environment.GetEnvironmentVariable("HTTPS_PROXY"));
    }
    finally
    {
        Environment.SetEnvironmentVariable("CL_QUOTA_FAKE_ENV", null);
        Environment.SetEnvironmentVariable("CL_QUOTA_FAKE_LOG", null);
        Environment.SetEnvironmentVariable("HTTPS_PROXY", previousProxy);
        TryDelete(log);
    }
}

static void TestQuotaProxyEnvironmentSelection()
{
    // ① 进程环境已有代理：原样继承，不覆盖用户自己配置的代理。
    var inherited = ProxyEnvironment.FromSettings("http://127.0.0.1:7896/",
        key => key == "HTTPS_PROXY" ? "http://127.0.0.1:7890" : null, 0, null);
    Equal("http://127.0.0.1:7890/", inherited["HTTPS_PROXY"]);
    Equal("http://127.0.0.1:7890/", inherited["https_proxy"]);
    Equal("localhost,127.0.0.1,::1", inherited["NO_PROXY"]);

    // ② 环境没有代理：回退启动器设置里的本地 HTTP 入口。
    var fromSettings = ProxyEnvironment.FromSettings("http://127.0.0.1:7896/", _ => null, 0, null);
    Equal("http://127.0.0.1:7896/", fromSettings["HTTPS_PROXY"]);
    Equal(true, fromSettings["NO_PROXY"]!.Contains("localhost", StringComparison.Ordinal));

    // ③ 设置也没有：回退 Windows 系统代理（仅 ProxyEnable=1 时）。
    var fromWindows = ProxyEnvironment.FromSettings(null, _ => null, 1, "127.0.0.1:7797");
    Equal("http://127.0.0.1:7797/", fromWindows["HTTPS_PROXY"]);
    var windowsOff = ProxyEnvironment.FromSettings(null, _ => null, 0, "127.0.0.1:7797");
    Equal(0, windowsOff.Count);

    // ④ 全部缺失：空字典，直连不注入任何键。
    var direct = ProxyEnvironment.FromSettings(null, _ => null, 0, null);
    Equal(0, direct.Count);

    // ⑤ 设置里的无效代理串：跳过继续走系统代理，而不是抛异常。
    var invalid = ProxyEnvironment.FromSettings("http://93.184.216.34:8080", _ => null, 1, "127.0.0.1:7797");
    Equal("http://127.0.0.1:7797/", invalid["HTTPS_PROXY"]);
}

static async Task TestQuotaProxyChangeAsync()
{
    var log = Path.Combine(Path.GetTempPath(), $"cl-quota-route-{Guid.NewGuid():N}.log");
    var (executable, arguments) = FakeServerCommand("--mode ok");
    var oldLog = Environment.GetEnvironmentVariable("CL_QUOTA_FAKE_LOG");
    var oldEnvLog = Environment.GetEnvironmentVariable("CL_QUOTA_FAKE_ENV");
    var parentProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY");
    Environment.SetEnvironmentVariable("CL_QUOTA_FAKE_LOG", log);
    Environment.SetEnvironmentVariable("CL_QUOTA_FAKE_ENV", log);
    try
    {
        var port = 7890;
        var factory = new CodexAppServerSessionFactory(executable, arguments,
            environmentResolver: () => ProxyEnvironment.ForUser(RouteKind.Proxy,
                ProxyAddress.Parse($"http://127.0.0.1:{port}")));
        await using var provider = new CodexQuotaProvider(factory);
        Equal(true, (await provider.ReadAsync(CancellationToken.None)).HasData);
        Equal(true, (await provider.ReadAsync(CancellationToken.None)).HasData);
        var unchanged = await WaitForLinesAsync(log, 8);
        Equal(1, unchanged.Count(line => line.StartsWith("#env|", StringComparison.Ordinal)));
        port = 7891;
        Equal(true, (await provider.ReadAsync(CancellationToken.None)).HasData);
        var changed = await WaitForLinesAsync(log, 13);
        var environments = changed.Where(line => line.StartsWith("#env|", StringComparison.Ordinal)).ToArray();
        Equal(2, environments.Length);
        Equal(true, environments[1].Contains("HTTPS_PROXY=http://127.0.0.1:7891/", StringComparison.Ordinal));
        Equal(parentProxy, Environment.GetEnvironmentVariable("HTTPS_PROXY"));
    }
    finally
    {
        Environment.SetEnvironmentVariable("CL_QUOTA_FAKE_LOG", oldLog);
        Environment.SetEnvironmentVariable("CL_QUOTA_FAKE_ENV", oldEnvLog);
        TryDelete(log);
    }
}

// ---------------------------------------------------------------------------
// 额度测试替身与模拟 App Server
// ---------------------------------------------------------------------------

static void RunFakeAppServer(string[] args)
{
    var mode = FakeOption(args, "--mode") ?? "ok";
    var logPath = Environment.GetEnvironmentVariable("CL_QUOTA_FAKE_LOG");
    var envLogPath = Environment.GetEnvironmentVariable("CL_QUOTA_FAKE_ENV");
    if (envLogPath is not null)
    {
        // 以子进程自己的视角记录收到的代理环境，供测试核对注入结果。
        try
        {
            var pairs = new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy", "NO_PROXY", "no_proxy" }
                .Select(key => (Key: key, Value: Environment.GetEnvironmentVariable(key)))
                .Where(entry => entry.Value is not null)
                .Select(entry => $"{entry.Key}={entry.Value}");
            File.AppendAllText(envLogPath, $"#env|{string.Join("|", pairs)}{Environment.NewLine}");
        }
        catch (IOException) { }
    }

    // 重定向时控制台编码跟随系统代码页，握手参数里的中文会被读成乱码并让 JSON 解析崩溃；
    // 这里显式按 UTF-8 读写，与启动器会话的编码保持一致。
    var utf8 = new UTF8Encoding(false);
    using var input = new StreamReader(Console.OpenStandardInput(), utf8);
    using var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
    using var errors = new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true };

    void Respond(long id, string result) => output.WriteLine($"{{\"id\":{id},\"result\":{FakeCompact(result)}}}");

    while (true)
    {
        var line = input.ReadLine();
        if (line is null) break;
        if (line.Length == 0) continue;

        long id;
        string method;
        string? parameters;
        using (var document = JsonDocument.Parse(line))
        {
            var root = document.RootElement;
            id = root.GetProperty("id").GetInt64();
            method = root.GetProperty("method").GetString() ?? "";
            parameters = root.TryGetProperty("params", out var value) ? value.GetRawText() : null;
        }

        if (logPath is not null)
        {
            try { File.AppendAllText(logPath, $"{method}|{parameters}{Environment.NewLine}"); }
            catch (IOException) { }
        }

        switch (method)
        {
            case "initialize":
                if (mode == "handshake-timeout") break;
                Respond(id, """{"userAgent":"fake-app-server","codexHome":"C:\\fake","platformFamily":"windows","platformOs":"windows"}""");
                if (mode == "exit")
                {
                    errors.WriteLine("stderr-secret-quota-test@example.com");
                    errors.Flush();
                    Environment.Exit(3);
                }
                break;

            case "account/read":
                if (mode == "noisy")
                {
                    // 无关通知与未知编号：客户端必须按编号关联，不能被它们干扰。
                    output.WriteLine("""{"id":999999,"result":{"ignored":true}}""");
                    output.WriteLine("""{"method":"account/rateLimits/updated"}""");
                }
                Respond(id, mode == "api-key"
                    ? """{"account":{"type":"apiKey"},"requiresOpenaiAuth":true}"""
                    : FakeQuotaJson.Account);
                break;

            case "account/rateLimits/read":
                if (mode == "timeout") break;
                Respond(id, mode == "legacy" ? FakeQuotaJson.LegacyRateLimits : FakeQuotaJson.RateLimits);
                break;

            default:
                output.WriteLine($"{{\"id\":{id},\"error\":{{\"code\":\"not-allowed\"}}}}");
                break;
        }
    }
}

static string FakeCompact(string json) => JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);

static string? FakeOption(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
        if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
    return null;
}

static (string Executable, string Arguments) FakeServerCommand(string options)
{
    var process = Environment.ProcessPath ?? throw new Exception("无法确定测试进程路径");
    if (process.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
    {
        var assembly = Path.Combine(AppContext.BaseDirectory, "CodexLauncher.Tests.dll");
        if (!File.Exists(assembly)) throw new Exception("找不到 CodexLauncher.Tests.dll，无法启动模拟 App Server");
        return (process, $"{assembly} --fake-app-server {options}");
    }
    return (process, $"--fake-app-server {options}");
}

static async Task<List<string>> WaitForLinesAsync(string path, int count)
{
    var lines = new List<string>();
    for (var attempt = 0; attempt < 100; attempt++)
    {
        lines = ReadLogLines(path);
        if (lines.Count >= count) return lines;
        await Task.Delay(50);
    }
    throw new Exception($"等待日志 {count} 行超时，实际 {lines.Count} 行");
}

static List<string> ReadLogLines(string path)
{
    try
    {
        return File.Exists(path) ? [.. File.ReadAllLines(path).Where(line => line.Length > 0)] : [];
    }
    catch (IOException) { return []; }
}

static string LogMethod(string line)
{
    var index = line.IndexOf('|');
    return index < 0 ? line : line[..index];
}

static string? LogParameters(string line)
{
    var index = line.IndexOf('|');
    return index < 0 ? null : line[(index + 1)..];
}

sealed class FakeDetector(bool running) : IAppProcessDetector
{
    public bool IsRunning(CodexInstallation app) => running;
}

sealed class FakeActivator(IEnvironmentStore store, bool success = false) : IAppActivator
{
    public bool Called { get; private set; }
    public string? SeenProxy { get; private set; }
    public Task<bool> ActivateAsync(CodexInstallation app, CancellationToken cancellationToken)
    {
        Called = true;
        SeenProxy = store.Get("HTTPS_PROXY");
        return Task.FromResult(success);
    }
}

sealed class ScriptedProbe(Func<RouteKind, ProxyAddress?, CancellationToken, Task<ProbeResult>> handler) : IHttpProbe
{
    private int _concurrent;
    public int Calls { get; private set; }
    public int MaxConcurrent { get; private set; }

    public async Task<ProbeResult> CheckAsync(RouteKind route, ProxyAddress? proxy, CancellationToken cancellationToken)
    {
        Calls++;
        var current = Interlocked.Increment(ref _concurrent);
        MaxConcurrent = Math.Max(MaxConcurrent, current);
        try { return await handler(route, proxy, cancellationToken); }
        finally { Interlocked.Decrement(ref _concurrent); }
    }
}

sealed class FakeProbe : IHttpProbe
{
    public Task<ProbeResult> CheckAsync(RouteKind route, ProxyAddress? proxy, CancellationToken cancellationToken) =>
        Task.FromResult(route == RouteKind.Proxy
            ? new ProbeResult(ProbeStatus.Reachable, 200, TimeSpan.Zero, "OK")
            : new ProbeResult(ProbeStatus.Failed, null, TimeSpan.Zero, "timeout"));
}

sealed class MemoryEnvironmentStore : IEnvironmentStore
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
    public string? Get(string name) => _values.GetValueOrDefault(name);
    public void Set(string name, string? value) => _values[name] = value;
}

sealed class GatewayHttpHandler : HttpMessageHandler
{
    public GatewayUpstream Selected { get; private set; } = GatewayUpstream.Verge;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Put)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (body.Contains("Party", StringComparison.Ordinal)) Selected = GatewayUpstream.Party;
            else if (body.Contains("Verge", StringComparison.Ordinal)) Selected = GatewayUpstream.Verge;
            else return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
            return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        }
        var json = "{\"name\":\"Upstream\",\"type\":\"Selector\",\"now\":\"" + Selected + "\",\"all\":[\"Party\",\"Verge\"]}";
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}

sealed class SlowGatewayHttpHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("unreachable");
    }
}

sealed class StaticGatewayHttpHandler(string response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(response) });
}

sealed class FakeGatewayController : IGatewayController
{
    public GatewayUpstream? Selected { get; set; }
    public int ReloadCount { get; private set; }
    public Task<GatewaySnapshot?> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Selected is { } selected ? new GatewaySnapshot(selected) : null);
    public Task<GatewaySnapshot> SwitchAsync(GatewayUpstream upstream, CancellationToken cancellationToken)
    {
        Selected = upstream;
        return Task.FromResult(new GatewaySnapshot(upstream));
    }
    public Task<GatewaySnapshot> ReloadAsync(GatewayPorts ports, GatewayUpstream upstream, CancellationToken cancellationToken)
    {
        ReloadCount++;
        Selected = upstream;
        return Task.FromResult(new GatewaySnapshot(upstream));
    }
}

sealed class FakeGatewayHost : IGatewayProcessHost
{
    public bool Owned { get; set; }
    public bool Controls { get; set; } = true;
    public bool ExternalStoppable { get; set; }
    public bool Started { get; private set; }
    public bool Stopped { get; set; }
    public GatewayPorts ConfiguredPorts { get; private set; } = GatewayPorts.Default;
    public bool IsOwnedRunning() => Owned;
    public bool ControlsPorts(GatewayPorts ports) => Owned && Controls;
    public void Start(string binaryPath, GatewayPorts ports) { Started = true; Owned = true; }
    public void StopOwned() { Stopped = true; Owned = false; }
    public bool IsConfigCurrent(GatewayPorts ports) => ConfiguredPorts == ports;
    public void SaveOwnedConfig(GatewayPorts ports) => ConfiguredPorts = ports;
    public bool TryStopCompatibleExternal(string binaryPath, GatewayPorts ports)
    {
        if (!ExternalStoppable) return false;
        Stopped = true;
        return true;
    }
}

sealed class FakeGatewayProxyGuard(bool active) : IGatewayProxyGuard
{
    public bool IsGatewaySystemProxy(int entryPort) => active;
}

sealed class FakeProcessControl : IDesktopProcessControl
{
    private readonly List<DesktopProcessSnapshot> _processes = [];
    private readonly Dictionary<int, (IntPtr Handle, string Title)> _windows = new();
    private readonly Dictionary<int, DateTimeOffset> _exitAt = new();

    public List<int> Terminated { get; } = [];
    public List<IntPtr> ClosedWindows { get; } = [];
    public int ListCalls { get; private set; }
    public bool ListThrows { get; set; }
    public bool CloseSucceeds { get; set; } = true;
    public bool ExitOnClose { get; set; }
    public TimeSpan? ExitAfter { get; set; }

    public void Add(DesktopProcessSnapshot process) => _processes.Add(process);

    public void SetWindow(int processId, IntPtr handle, string title) => _windows[processId] = (handle, title);

    public IReadOnlyList<DesktopProcessSnapshot> ListByName(string processName)
    {
        ListCalls++;
        if (ListThrows) throw new System.ComponentModel.Win32Exception(5, "拒绝访问");
        return _processes.Where(process => process.Name.Equals(processName, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public bool TryReadMainWindow(int processId, out IntPtr handle, out string title)
    {
        if (_windows.TryGetValue(processId, out var window))
        {
            handle = window.Handle;
            title = window.Title;
            return true;
        }
        handle = IntPtr.Zero;
        title = "";
        return false;
    }

    public bool TryCloseWindow(IntPtr handle)
    {
        if (!CloseSucceeds) return false;
        ClosedWindows.Add(handle);
        foreach (var pair in _windows.Where(pair => pair.Value.Handle == handle).ToArray())
        {
            if (ExitOnClose) _exitAt[pair.Key] = DateTimeOffset.UtcNow;
            else if (ExitAfter is { } delay) _exitAt[pair.Key] = DateTimeOffset.UtcNow + delay;
        }
        return true;
    }

    public bool TryTerminate(int processId)
    {
        Terminated.Add(processId);
        _exitAt.Remove(processId);
        _processes.RemoveAll(process => process.ProcessId == processId);
        _windows.Remove(processId);
        return true;
    }

    public bool IsAlive(int processId)
    {
        if (!_processes.Any(process => process.ProcessId == processId)) return false;
        if (_exitAt.TryGetValue(processId, out var exitAt) && DateTimeOffset.UtcNow >= exitAt)
        {
            _processes.RemoveAll(process => process.ProcessId == processId);
            _windows.Remove(processId);
            return false;
        }
        return true;
    }
}

sealed class TestClock(DateTimeOffset start)
{
    private readonly object _sync = new();
    private DateTimeOffset _now = start;

    public DateTimeOffset Now { get { lock (_sync) return _now; } }

    public void Advance(TimeSpan span) { lock (_sync) _now += span; }
}

static class FakeQuotaJson
{
    public const string Account =
        """{"account":{"type":"chatgpt","email":"quota-test@example.com","planType":"plus"},"requiresOpenaiAuth":true,"workspaceRouting":{"chatgptAccountId":"test-workspace","backendOrigin":"https://chatgpt.com","accountRoutingOverride":"NO_CONSTRAINT"}}""";

    public const string RateLimits = """
    {
      "ordinaryUsageAllowed": true,
      "rateLimits": {
        "limitId": "codex",
        "primary": { "usedPercent": 15, "windowDurationMins": 300, "resetsAt": 1790832653 },
        "secondary": { "usedPercent": 2, "windowDurationMins": 10080, "resetsAt": 1791419453 }
      },
      "rateLimitsByLimitId": {
        "codex": {
          "limitId": "codex",
          "primary": { "usedPercent": 15, "windowDurationMins": 300, "resetsAt": 1790832653 },
          "secondary": { "usedPercent": 2, "windowDurationMins": 10080, "resetsAt": 1791419453 }
        },
        "codex_extra": {
          "limitId": "codex_extra",
          "limitName": "扩展额度",
          "primary": { "usedPercent": 88, "windowDurationMins": 1440, "resetsAt": 1790832653 }
        }
      },
      "accountId": "test-workspace",
      "rateLimitResetCredits": { "availableCount": 0, "credits": [] }
    }
    """;

    public const string LegacyRateLimits =
        """{"ordinaryUsageAllowed":true,"rateLimits":{"limitId":"codex","primary":{"usedPercent":40,"windowDurationMins":300,"resetsAt":1790832653}},"accountId":"test-workspace"}""";
}

// 内存版自启注册表：只用来验证 AutoStartRegistration 与“注册表真实状态”的契约，
// 不写用户注册表，因此测试可以随便跑。
sealed class MemoryAutoStartRegistry : IAutoStartRegistry
{
    public string? Value { get; set; }

    public int Writes { get; private set; }

    public string? Read(string name) => Value;

    public void Write(string name, string? value)
    {
        Writes++;
        Value = value;
    }
}

sealed class DeniedAutoStartRegistry : IAutoStartRegistry
{
    public string? Read(string name) => null;

    public void Write(string name, string? value) => throw new UnauthorizedAccessException("fake-denied");
}

sealed class FakeQuotaFactory : IQuotaSessionFactory
{
    private readonly List<FakeQuotaSession> _sessions = [];

    public string Account { get; set; } = FakeQuotaJson.Account;
    public string RateLimits { get; set; } = FakeQuotaJson.RateLimits;
    public string? Email { get; set; }
    public bool Fail { get; set; }
    public bool RateLimitsFail { get; set; }
    public int AccountReads { get; private set; }
    public int RateLimitReads { get; private set; }

    public FakeQuotaSession? LastSession { get { lock (_sessions) return _sessions.Count == 0 ? null : _sessions[^1]; } }

    public string AccountJson() => Email is null
        ? Account
        : "{\"account\":{\"type\":\"chatgpt\",\"email\":\"" + Email
          + "\",\"planType\":\"plus\"},\"requiresOpenaiAuth\":true,\"workspaceRouting\":{\"chatgptAccountId\":\"test-workspace\"}}";

    public void RecordAccountRead() => AccountReads++;

    public void RecordRateLimitRead() => RateLimitReads++;

    public Task<IQuotaSession> OpenAsync(CancellationToken cancellationToken)
    {
        var session = new FakeQuotaSession(this);
        lock (_sessions) _sessions.Add(session);
        return Task.FromResult<IQuotaSession>(session);
    }
}

sealed class FakeQuotaSession(FakeQuotaFactory factory) : IQuotaSession
{
    public event Action<string>? Notification;

    public List<string> Methods { get; } = [];
    public string? LastParameters { get; private set; }

    public Task<string?> InvokeAsync(string method, string? paramsJson, CancellationToken cancellationToken)
    {
        Methods.Add(method);
        LastParameters = paramsJson;
        if (!QuotaRequestWhitelist.IsAllowed(method))
            throw new InvalidOperationException($"方法不在只读白名单内：{method}");
        if (factory.Fail) throw new QuotaSessionException("fake-failure");

        return method switch
        {
            "account/read" => Respond(factory.RecordAccountRead, factory.AccountJson()),
            "account/rateLimits/read" => factory.RateLimitsFail
                ? throw new QuotaSessionException("fake-rate-limit-failure")
                : Respond(factory.RecordRateLimitRead, factory.RateLimits),
            _ => throw new InvalidOperationException($"模拟会话不接受 {method}")
        };
    }

    private static Task<string?> Respond(Action record, string payload)
    {
        record();
        return Task.FromResult<string?>(payload);
    }

    public void Raise(string method) => Notification?.Invoke(method);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}




