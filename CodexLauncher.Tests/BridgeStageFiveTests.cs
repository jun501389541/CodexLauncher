using CodexLauncher.App;
using CodexLauncher.Core;
using System.Net.NetworkInformation;

internal static class BridgeStageFiveTests
{
    internal static IEnumerable<(string Name,Action Run)> All =>
    [
        ("background autostart recognizes the executable rather than the full command", Startup),
        ("background activation signals do not request the main window", Activation)
        ,("bridge runtime preserves local selection and supports revocation while stopped", ()=>Runtime().GetAwaiter().GetResult())
        ,("bridge adapter choices retain candidates and explain failed listener checks", AdapterChoices)
        ,("bridge panel exposes themed main-window controls and clears invitation state", Panel)
        ,("bridge panel starts collapsed and toggles layout without changing sharing", CollapsiblePanel)
        ,("background main window stays hidden and explicitly exits its owned bridge", BackgroundWindow)
    ];
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void AdapterChoices()
    {
        var catalog=BridgeAdapterCatalog.Build(
            [new("wifi","Wi-Fi",NetworkInterfaceType.Wireless80211),new("ethernet","Ethernet",NetworkInterfaceType.Ethernet),new("vpn","VPN",NetworkInterfaceType.Tunnel)],
            id=>id=="wifi"?"NETWORK_POLICY_ACCESS_DENIED":null);
        Check(catalog.Count==2,"supported Wi-Fi and Ethernet candidates were not retained");
        Check(catalog.Single(a=>a.Id=="wifi").UnavailableReason!.Contains("权限"),"failed Wi-Fi policy check did not reach the adapter list");
        Check(catalog.Single(a=>a.Id=="ethernet").CanListen,"eligible Ethernet candidate was rejected");
        var privateProfile=BridgeAdapterChoice.FromError("wifi","Wi-Fi","PRIVATE_NETWORK_REQUIRED");
        Check(!privateProfile.CanListen&&privateProfile.UnavailableReason!.Contains("专用"),"public network profile was not explained");
        Check(privateProfile.ToString().Contains("Wi-Fi")&&privateProfile.ToString().Contains("专用"),"ineligible adapter disappeared from the selector label");
        var denied=BridgeAdapterChoice.FromError("wifi","Wi-Fi","NETWORK_POLICY_ACCESS_DENIED");
        Check(!denied.CanListen&&denied.UnavailableReason!.Contains("权限"),"network policy access failure was hidden");
        var available=BridgeAdapterChoice.FromError("wifi","Wi-Fi",null);
        Check(available.CanListen&&available.UnavailableReason is null,"eligible adapter was marked unavailable");
    }
    private sealed class Registry:IAutoStartRegistry
    {internal string? Value;public string? Read(string name)=>Value;public void Write(string name,string? value)=>Value=value;}
    private static void Startup()
    {
        const string exe=@"D:\Apps With Spaces\CodexLauncher.exe";
        Check(AutoStartRegistration.Command(exe)=="\""+exe+"\" --background","autostart lacks background flag");
        var registry=new Registry{Value="\""+exe+"\" --background"};var registration=new AutoStartRegistration(registry);
        Check(registration.IsEnabled(exe),"background arguments hid enabled registration");
        registry.Value="\""+exe+"\"";Check(registration.IsEnabled(exe),"legacy command not recognized");
        registry.Value="\""+exe+".other\" --background";Check(!registration.IsEnabled(exe),"wrong executable accepted");
        Check(LauncherStartup.IsBackground(["--background"])&&!LauncherStartup.IsBackground(["--background-other"]),"background parsing widened");
    }
    private static void Activation()
    {
        var name=@"Local\CodexLauncher.Test.Background."+Guid.NewGuid();
        using var first=new SingleInstanceGate(name);Check(first.TryAcquire(),"owner acquisition failed");
        using var second=new SingleInstanceGate(name);Check(!second.TryAcquire(),"second acquired");
        second.SignalBackground();Check(first.WaitForBackground(TimeSpan.FromSeconds(1)),"background signal lost");
        Check(!first.WaitForActivation(TimeSpan.Zero),"background signal woke window");
        second.SignalActivation();Check(first.WaitForActivation(TimeSpan.FromSeconds(1)),"foreground activation lost");
    }
    private static async Task Runtime()
    {
        var root=Path.Combine(Path.GetTempPath(),"bridge-ui-test-"+Guid.NewGuid());
        try
        {
            await using var quota=new SharedQuotaCoordinator(null,monitoringEnabled:false);
            await using(var runtime=new BridgeRuntime(root,quota,new FakeSource()))
            {
                Check(runtime.Status.State=="STOPPED","runtime default enabled");
                runtime.SelectAdapter("selected");
                await runtime.StartAsync(43189);Check(runtime.Status.State=="PAUSED","missing network did not pause");
                var device=runtime.Devices.AddApproved("Synthetic",out var token);runtime.Devices.CompleteDelivery(device.Id);
                runtime.RenameDevice(device.Id,"Renamed");Check(runtime.Devices.List().Single().Name=="Renamed","rename missing");
                await runtime.StopAsync();runtime.RevokeDevice(device.Id);Check(runtime.Devices.Authenticate(token) is null,"stopped revocation ineffective");
            }
            await using var restored=new BridgeRuntime(root,quota,new FakeSource());
            Check(restored.SelectedAdapter=="selected"&&restored.Devices.List().Count==0,"local state did not persist");
            Check((await quota.ReadAsync(CancellationToken.None)).Availability==QuotaAvailability.Unavailable,"runtime disposed quota owner");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private sealed class FakeSource:IBridgeNetworkSource
    {
        internal BridgeNetworkBinding? Binding;
        public event Action<bool>? Changed{add{}remove{}}
        public bool Suspended=>false;
        public BridgeNetworkBinding? Resolve(string adapterId,int port,System.Net.IPAddress? preferred)=>Binding?.AdapterId==adapterId?Binding with{Port=port}:null;
        public void Dispose(){}
    }
    private static void CollapsiblePanel()
    {
        Exception? error=null;
        var thread=new Thread(()=>
        {
            try
            {
                using var form=new Form{ClientSize=new System.Drawing.Size(900,900),ShowInTaskbar=false,Opacity=0};
                using var panel=new BridgePanel{Dock=DockStyle.Top};
                form.Controls.Add(panel);
                var configured=0;
                panel.ConfigureRequested+=(_,_,_)=>{configured++;return Task.CompletedTask;};
                form.Show();Application.DoEvents();
                var toggle=panel.Controls.Find("BridgeToggle",true).OfType<Button>().SingleOrDefault();
                Check(toggle is not null,"bridge collapse toggle missing");
                var input=panel.Controls.Find("BridgeNickname",true).OfType<TextBox>().Single();
                Check(!input.Visible,"bridge settings should start collapsed");
                var collapsed=panel.Height;
                toggle!.PerformClick();Application.DoEvents();form.PerformLayout();
                Check(input.Visible&&panel.Height>collapsed+200,"expanded bridge settings are clipped or hidden");
                toggle.PerformClick();Application.DoEvents();form.PerformLayout();
                Check(!input.Visible&&panel.Height==collapsed,"bridge did not return to collapsed layout");
                Check(configured==0,"folding changed sharing configuration");
            }
            catch(Exception e){error=e;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw error;
    }
    private static void Panel()
    {
        Exception? error=null;var thread=new Thread(()=>
        {
            try
            {
                using var form=new Form{ClientSize=new System.Drawing.Size(900,900)};
                using var panel=new BridgePanel();form.Controls.Add(panel);panel.Dock=DockStyle.Top;
                panel.SetConfiguration(false,43189,null);
                Check(panel.Controls.Find("BridgeApply",true).Length==1&&panel.Controls.Find("BridgeDevices",true).Length==1,"main-window controls missing");
                ThemeManager.ApplyTo(form,ThemePalette.Dark,false);form.CreateControl();form.PerformLayout();
                Check(panel.ForeColor.ToArgb()==UiTheme.Text.ToArgb(),"bridge theme not applied");
                var nickname=panel.Controls.Find("BridgeNickname",true).OfType<TextBox>().Single();
                Check(nickname.ForeColor.ToArgb()==UiTheme.Text.ToArgb(),"dark theme leaves nickname input unreadable");
                var embeddedInputs=FindTextBoxes(panel).ToArray();
                Check(embeddedInputs.All(input=>input.ForeColor.ToArgb()==UiTheme.Text.ToArgb()),"dark theme leaves an embedded input unreadable");
                panel.Scale(new System.Drawing.SizeF(2,2));form.PerformLayout();
                panel.ClearInvitation();Check(!panel.HasInvitation,"cleared QR retained secret state");
            }
            catch(Exception e){error=e;}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw error;
    }
    private static void BackgroundWindow()
    {
        Exception? error=null;var thread=new Thread(()=>
        {
            var root=Path.Combine(Path.GetTempPath(),"bridge-background-test-"+Guid.NewGuid());
            try
            {
                Directory.CreateDirectory(root);
                using var reserve=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);reserve.Start();var port=((System.Net.IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();
                var savedSettings = new LauncherSettings(ProxyUrl:"http://127.0.0.1:19876", MihomoPath:@"D:\Custom\mihomo.exe", PartyPort:17890, VergePort:17897, BridgeEnabled:false,BridgePort:port,QuotaMonitoringEnabled:false);
                new LauncherSettingsStore(Path.Combine(root,"settings.json")).Save(savedSettings);
                var quotaCreated=0;var registry=new Registry();BridgeRuntime? runtime=null;
                var services=new LauncherServices(root,(enabled,token)=>{Check(!enabled,"background changed quota switch");quotaCreated++;return new(null,monitoringEnabled:false,lifetimeToken:token);},q=>{runtime=new(root,q,new FakeSource{Binding=new("selected",System.Net.IPAddress.Loopback,1,port)});runtime.SelectAdapter("selected");return runtime;},registry);
                using var owner=new SingleInstanceGate(@"Local\CodexLauncher.Test.UI."+Guid.NewGuid());Check(owner.TryAcquire(),"test single instance unavailable");
                using var form=new MainForm(owner,true,services);
                Check(!form.IsHandleCreated,"background test did not begin before the main-window handle");
                owner.SignalBackground();Thread.Sleep(450);
                form.Show();
                Pump(()=>form.CoreInitialization?.IsCompleted==true);
                Check(!form.Visible&&!form.ShowInTaskbar,"background displayed main window");
                Check(quotaCreated==1&&registry.Value is null,"background launched extra work or wrote autostart");
                Pump(()=>runtime?.Status.State=="RUNNING");
                Check(!form.Visible&&quotaCreated==1&&new LauncherSettingsStore(Path.Combine(root,"settings.json")).Load().BridgeEnabled,"background signal woke UI or ignored bridge");
                Check(new LauncherSettingsStore(Path.Combine(root,"settings.json")).Load() == savedSettings with { BridgeEnabled = true }, "background bridge signal overwrote unrelated settings");
                using var handler=new HttpClientHandler{UseProxy=false,ServerCertificateCustomValidationCallback=(_,certificate,_,_)=>certificate?.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256)==runtime!.Fingerprint};
                using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(3)};
                var health=client.GetStringAsync(runtime!.Status.Endpoint+"/v1/health");Pump(()=>health.IsCompleted);Check(health.GetAwaiter().GetResult().Contains("READY"),"background HTTPS unavailable");
                var panel=FindPanel(form);panel.ShowInvitation().GetAwaiter().GetResult();Check(panel.HasInvitation,"QR not displayed");
                var invite=runtime!.Pairing!.CreateInvitation().Value!;runtime.Pairing.RequestPair(new(invite.PairToken,"Synthetic phone"),System.Net.IPAddress.Loopback);
                panel.RefreshState();Check(!panel.HasInvitation,"consumed invitation retained QR");panel.ShowInvitation().GetAwaiter().GetResult();
                form.Close();Check(!form.IsDisposed&&runtime is not null,"ordinary close exited rather than hiding");
                Check(!panel.HasInvitation&&runtime!.Status.State=="RUNNING","hide retained QR or stopped sharing");
                typeof(MainForm).GetMethod("RequestExit",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(form,null);
                Pump(()=>form.IsDisposed);Check(runtime!.Status.State=="STOPPED","explicit exit retained bridge");
                using var free=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,port);free.Start();free.Stop();
            }
            catch(Exception e){error=e;}
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();
        if(!thread.Join(TimeSpan.FromSeconds(15)))throw new Exception("background UI cleanup deadlocked");if(error is not null)throw error;
    }
    private static void Pump(Func<bool> done)
    {var deadline=DateTime.UtcNow.AddSeconds(5);while(!done()&&DateTime.UtcNow<deadline){Application.DoEvents();Thread.Sleep(10);}Check(done(),"UI operation timed out");}
    private static BridgePanel FindPanel(Control parent)
    {foreach(Control child in parent.Controls){if(child is BridgePanel panel)return panel;try{return FindPanel(child);}catch(InvalidOperationException){}}throw new InvalidOperationException("bridge panel missing");}
    private static IEnumerable<TextBox> FindTextBoxes(Control parent)
    {foreach(Control child in parent.Controls){if(child is TextBox box)yield return box;foreach(var nested in FindTextBoxes(child))yield return nested;}}
}
