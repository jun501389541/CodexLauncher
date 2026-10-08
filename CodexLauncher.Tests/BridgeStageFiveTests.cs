using CodexLauncher.App;
using CodexLauncher.Core;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class BridgeStageFiveTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeScrollBarInfo
    {
        public uint Size;
        public NativeRect Rect;
        public int LineButton,ThumbTop,ThumbBottom,Reserved;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=6,ArraySubType=UnmanagedType.U4)] public uint[] States;
    }
    [DllImport("user32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetScrollBarInfo(IntPtr handle,int objectId,ref NativeScrollBarInfo info);
    [DllImport("user32.dll",CharSet=CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);
    private static bool HorizontalScrollbarVisible(Control control)
    {
        var info=new NativeScrollBarInfo{Size=(uint)Marshal.SizeOf<NativeScrollBarInfo>(),States=new uint[6]};
        return GetScrollBarInfo(control.Handle,-6,ref info)&&info.Rect.Right>info.Rect.Left&&info.Rect.Bottom>info.Rect.Top&&
            (info.States[0]&0x00008000)==0;
    }

    internal static IEnumerable<(string Name,Action Run)> All =>
    [
        ("background autostart recognizes the executable rather than the full command", Startup),
        ("background activation signals do not request the main window", Activation)
        ,("bridge runtime preserves local selection and supports revocation while stopped", ()=>Runtime().GetAwaiter().GetResult())
        ,("bridge adapter choices retain candidates and explain failed listener checks", AdapterChoices)
        ,("bridge adapter recommendation prefers eligible Wi-Fi then Ethernet", AdapterRecommendation)
        ,("bridge adapter candidates exclude filter components and virtual adapters", AdapterFilterComponents)
        ,("bridge Network List Manager COM methods declare output parameters correctly", NetworkProfileInteropSignature)
        ,("bridge network profile resolves a concrete category for the active adapter", NetworkProfileRead)
        ,("bridge physical hardware lookup resolves the active WLAN adapter", PhysicalHardwareRead)
        ,("bridge public and private profiles support sharing while unknown profiles fail closed", SupportedProfiles)
        ,("bridge refresh checks every candidate and shows quality results", AdapterQualitySummary)
        ,("bridge refresh replaces unavailable choices and preserves usable manual choices", AdapterSelectionOnRefresh)
        ,("enabling bridge refreshes adapters before applying the selection", AdapterRefreshOnEnable)
        ,("fresh bridge preferences default off and preserve explicit opt-in", BridgePreferenceDefaults)
        ,("bounds layout passes when the bridge expands", ExpansionBoundsLayoutPasses)
        ,("bridge panel exposes themed main-window controls and clears invitation state", Panel)
        ,("bridge panel starts collapsed and toggles layout without changing sharing", CollapsiblePanel)
        ,("bridge configuration row keeps refresh action visible at narrow widths", ResponsiveConfigurationRow)
        ,("bridge approval controls retain focus and show selected requests", ApprovalFocus)
        ,("bridge device lists support checkbox selection and a visible pairing-time column", SelectableDeviceLists)
        ,("bridge device lists avoid horizontal scroll bars with overflowing row counts", DeviceListsDoNotScrollHorizontally)
        ,("bridge batch operations continue and report per-device failures", BatchOperationsContinueAfterFailures)
        ,("background main window stays hidden and explicitly exits its owned bridge", BackgroundWindow)
    ];
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void BridgePreferenceDefaults()
    {
        var fresh=JsonSerializer.Deserialize<LauncherSettings>("{}")!;
        Check(!fresh.BridgeEnabled,"a fresh install should not enable phone quota sharing");
        var saved=JsonSerializer.Deserialize<LauncherSettings>(JsonSerializer.Serialize(fresh with{BridgeEnabled=true}))!;
        Check(saved.BridgeEnabled,"an explicitly enabled sharing preference was not retained");
    }
    private static void AdapterChoices()
    {
        var catalog=BridgeAdapterCatalog.Build(
            [new("wifi","Wi-Fi",NetworkInterfaceType.Wireless80211),new("ethernet","Ethernet",NetworkInterfaceType.Ethernet),new("vpn","VPN",NetworkInterfaceType.Tunnel)],
            id=>id=="wifi"?"NETWORK_POLICY_ACCESS_DENIED":null);
        Check(catalog.Count==2,"supported Wi-Fi and Ethernet candidates were not retained");
        Check(catalog.Single(a=>a.Id=="wifi").UnavailableReason!.Contains("权限"),"failed Wi-Fi policy check did not reach the adapter list");
        Check(catalog.Single(a=>a.Id=="ethernet").CanListen,"eligible Ethernet candidate was rejected");
        var privateProfile=BridgeAdapterChoice.FromError("wifi","Wi-Fi","NETWORK_PROFILE_UNAVAILABLE");
        Check(!privateProfile.CanListen&&privateProfile.UnavailableReason!.Contains("无法确认"),"unverified network profile was not explained");
        Check(privateProfile.ToString()=="Wi-Fi（不可用）","selector text should mark an adapter that failed qualification");
        var tooltip=BridgeAdapterDisplay.ToolTipText(privateProfile);
        Check(tooltip.Contains("Wi-Fi")&&tooltip.Contains(privateProfile.UnavailableReason!),"adapter tooltip omitted the full name or qualification reason");
        const string longName="本地连接* 10-Native Wi-Fi Adapter";
        var measured=BridgeAdapterDisplay.CalculateDropDownWidth(["Wi-Fi",longName],360,900,text=>text.Length*10,32);
        Check(measured>=longName.Length*10+32&&measured<=900,"dropdown width did not fit the full adapter name");
        var constrained=BridgeAdapterDisplay.CalculateDropDownWidth([longName],360,400,text=>text.Length*20,32);
        Check(constrained==400,"dropdown width exceeded the available screen space");
        var nearScreenEdge=BridgeAdapterDisplay.CalculateDropDownWidth([longName],360,120,text=>text.Length*20,32);
        Check(nearScreenEdge<=120,"dropdown width exceeded the small space remaining before the screen edge");
        var workArea=new System.Drawing.Rectangle(0,0,998,900);
        var selector=new System.Drawing.Rectangle(298,100,360,32);
        var remaining=BridgeAdapterDisplay.AvailableDropDownWidth(workArea,selector);
        Check(remaining==700,"available dropdown width did not use the selector's screen position");
        var screenBounded=BridgeAdapterDisplay.CalculateDropDownWidth([longName],selector.Width,remaining,text=>text.Length*100,32);
        Check(selector.Left+screenBounded<=workArea.Right,"dropdown extends past the working-area right edge");
        var secondMonitor=new System.Drawing.Rectangle(1920,0,1000,900);
        var selectorAtEdge=new System.Drawing.Rectangle(2820,100,170,32);
        Check(BridgeAdapterDisplay.AvailableDropDownWidth(secondMonitor,selectorAtEdge)==100,"dropdown bounds were wrong on a monitor with a nonzero screen origin");
        var denied=BridgeAdapterChoice.FromError("wifi","Wi-Fi","NETWORK_POLICY_ACCESS_DENIED");
        Check(!denied.CanListen&&denied.UnavailableReason!.Contains("权限"),"network policy access failure was hidden");
        var available=BridgeAdapterChoice.FromError("wifi","Wi-Fi",null);
        Check(available.CanListen&&available.UnavailableReason is null,"eligible adapter was marked unavailable");
        Check(available.ToString()=="Wi-Fi（可用）","selector text should mark an eligible adapter as usable");
    }
    private static void AdapterRefreshOnEnable()
    {
        Exception? error=null;
        var thread=new Thread(() =>
        {
            try
            {
                var refreshCount=0;
                using var form=new Form{ClientSize=new System.Drawing.Size(900,900),ShowInTaskbar=false,Opacity=0};
                using var panel=new BridgePanel(() =>
                {
                    refreshCount++;
                    return [
                        BridgeAdapterChoice.FromError("ethernet","Ethernet adapter",null),
                        BridgeAdapterChoice.FromError("wifi","Wi-Fi adapter",null) with{Recommended=true}
                    ];
                }){Dock=DockStyle.Top};
                form.Controls.Add(panel);form.Show();Application.DoEvents();
                panel.Controls.Find("BridgeToggle",true).OfType<Button>().Single().PerformClick();
                Application.DoEvents();
                var enabled=(CheckBox)typeof(BridgePanel).GetField("_enabled",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(panel)!;
                Check(!enabled.Checked,"phone quota sharing should begin disabled");
                SendMessage(enabled.Handle,0x00F5,IntPtr.Zero,IntPtr.Zero);
                Application.DoEvents();
                var selectedAfterEnable=panel.Controls.Find("BridgeAdapterSelector",true).OfType<ComboBox>().Single().SelectedItem as BridgeAdapterChoice;
                Check(refreshCount==1,"checking the sharing option should run exactly one adapter refresh");
                Check(selectedAfterEnable?.Id=="wifi","enabling sharing did not select the recommended eligible adapter");
                string? appliedAdapter=null;
                panel.ConfigureRequested+=(_,adapter,_)=>{appliedAdapter=adapter;return Task.CompletedTask;};
                panel.Controls.Find("BridgeApply",true).OfType<Button>().Single().PerformClick();
                Application.DoEvents();
                Check(refreshCount==1,"applying the refreshed selection ran a duplicate adapter refresh");
                Check(appliedAdapter=="wifi","bridge apply did not use the adapter selected by the refresh");

                panel.SetConfiguration(true,43189,"stale");
                panel.RefreshAdapters();
                var selected=panel.Controls.Find("BridgeAdapterSelector",true).OfType<ComboBox>().Single().SelectedItem as BridgeAdapterChoice;
                Check(selected?.Id=="wifi"&&selected.CanListen,"refresh did not replace a missing adapter with an eligible alternative");
            }
            catch(Exception e){error=e;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw error;
    }
    private static void AdapterRecommendation()
    {
        BridgeAdapterDescriptor[] adapters=[new("ethernet","A Ethernet",NetworkInterfaceType.Ethernet),new("wifi","Z Wi-Fi",NetworkInterfaceType.Wireless80211),new("virtual","0 Virtual",NetworkInterfaceType.Wireless80211)];
        var choices=BridgeAdapterCatalog.Build(adapters,id=>id=="virtual"?"PHYSICAL_ADAPTER_REQUIRED":null);
        Check(choices[0].Id=="wifi"&&choices[0].ToString().Contains("推荐"),"eligible Wi-Fi should be recommended before Ethernet and virtual adapters");
        Check(choices.Count(a=>a.ToString().Contains("推荐"))==1,"only one adapter should be recommended");
        var wired=BridgeAdapterCatalog.Build(adapters,id=>id=="ethernet"?null:"NETWORK_ADAPTER_NOT_CONNECTED");
        Check(wired[0].Id=="ethernet"&&wired[0].ToString().Contains("推荐"),"eligible Ethernet should be recommended when Wi-Fi is unavailable");
        var unavailable=BridgeAdapterCatalog.Build(adapters,_=>"NETWORK_PROFILE_UNAVAILABLE");
        Check(unavailable.All(a=>!a.CanListen&&!a.ToString().Contains("推荐")&&a.UnavailableReason is not null),"unavailable adapters must retain reasons and never be recommended");
    }
    private static void AdapterFilterComponents()
    {
        var inspected=new List<string>();
        var choices=BridgeAdapterCatalog.Build(
        [
            new("wifi","WLAN",NetworkInterfaceType.Wireless80211),
            new("ethernet","以太网",NetworkInterfaceType.Ethernet),
            new("npcap","WLAN-Npcap Packet Driver (NPCAP)-0000",NetworkInterfaceType.Wireless80211),
            new("native","WLAN-Native WiFi Filter Driver-0000",NetworkInterfaceType.Wireless80211),
            new("wfp","以太网-WFP Native MAC Layer LightWeight Filter-0000",NetworkInterfaceType.Ethernet),
            new("qos","以太网-QoS Packet Scheduler-0000",NetworkInterfaceType.Ethernet),
            new("vbox","WLAN-VirtualBox NDIS Light-Weight Filter-0000",NetworkInterfaceType.Wireless80211),
            new("wan","本地连接* 6","WAN Miniport (IP)",NetworkInterfaceType.Ethernet),
            new("wifi-direct","本地连接* 10","Microsoft Wi-Fi Direct Virtual Adapter #2",NetworkInterfaceType.Wireless80211),
            new("hyper-v","vEthernet (Default Switch)","Hyper-V Virtual Ethernet Adapter",NetworkInterfaceType.Ethernet),
            new("bluetooth","蓝牙网络连接","Bluetooth Device (Personal Area Network)",NetworkInterfaceType.Ethernet)
        ],id=>{inspected.Add(id);return null;});
        Check(choices.Select(choice=>choice.Id).ToHashSet(StringComparer.Ordinal).SetEquals(["wifi","ethernet"]),"filter components and virtual adapters were shown as pairing choices");
        Check(inspected.ToHashSet(StringComparer.Ordinal).SetEquals(["wifi","ethernet"]),"filter components and virtual adapters were unnecessarily inspected");
    }
    private static void PhysicalHardwareRead()
    {
        Check(System.Runtime.InteropServices.Marshal.SizeOf<BridgeAdapterHardware.InterfaceRow>()==1352,"MIB_IF_ROW2 native size is incorrect");
        Check(System.Runtime.InteropServices.Marshal.OffsetOf<BridgeAdapterHardware.InterfaceRow>("InterfaceAndOperStatusFlags").ToInt32()==1152,"MIB_IF_ROW2 hardware flags offset is incorrect");
        Check(!BridgeAdapterHardware.IsPhysical("not-an-adapter"),"malformed adapter identity was accepted");
        var active=NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n=>n.OperationalStatus==OperationalStatus.Up&&
            n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211&&
            n.GetIPProperties().UnicastAddresses.Any(a=>WindowsBridgeNetworkSource.IsPrivateV4(a.Address)));
        if(active is null)return;
        var reader=typeof(PrivateLanListenerPolicy).GetMethod("IsPhysical",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
        try
        {
            Check(reader.Invoke(null,[active.Id]) is true,"active WLAN hardware was rejected as virtual");
            using var source=new WindowsBridgeNetworkSource();
            var check=source.Inspect(active.Id,43189,null);
            Check(check.Binding is not null,$"active WLAN failed the complete listener check: {check.ErrorCode}");
            Check(check.Binding!.AdapterId==active.Id,"listener check substituted another adapter");
            Console.WriteLine("Verified active WLAN hardware and complete listener policy.");
        }
        catch(System.Reflection.TargetInvocationException e)
        {
            throw new Exception($"Hardware lookup {e.InnerException?.GetType().Name} HRESULT 0x{e.InnerException?.HResult:X8}: {e.InnerException}",e.InnerException);
        }
        foreach(var virtualAdapter in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.Description.Contains("Hyper-V Virtual Ethernet Adapter",StringComparison.OrdinalIgnoreCase)||n.Description.Contains("Microsoft Wi-Fi Direct Virtual Adapter",StringComparison.OrdinalIgnoreCase)))
            Check(!BridgeAdapterHardware.IsPhysical(virtualAdapter.Id),"virtual adapter passed the native hardware check");
    }
    private static void SupportedProfiles()
    {
        var validate=typeof(PrivateLanListenerPolicy).GetMethod("EnsureSupportedCategory",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        Check(validate is not null,"shared LAN policy must accept public and private network profiles");
        validate!.Invoke(null,[0]);
        validate.Invoke(null,[1]);
        foreach(int? category in new int?[]{null,2,99})
        {
            try { validate.Invoke(null,[category]); throw new Exception("unverified or unsupported profile accepted"); }
            catch(System.Reflection.TargetInvocationException e)
            {
                Check(e.InnerException is InvalidOperationException error&&error.Message.StartsWith("NETWORK_PROFILE_",StringComparison.Ordinal),"profile failure lost its specific reason");
            }
        }
    }
    private static void NetworkProfileRead()
    {
        var active=NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n=>
            n.OperationalStatus==OperationalStatus.Up&&
            n.NetworkInterfaceType is (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)&&
            n.GetIPProperties().UnicastAddresses.Any(address=>WindowsBridgeNetworkSource.IsPrivateV4(address.Address)));
        if(active is null)return;
        var reader=typeof(PrivateLanListenerPolicy).GetMethod("ReadCategory",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        Check(reader is not null,"Windows network profile category reader is missing");
        try
        {
            var result=reader!.Invoke(null,[active.Id]);
            Check(result is Enum,"Network List Manager did not resolve the active adapter to a network category");
            Check(Enum.IsDefined(result!.GetType(),result),"Network List Manager returned an unknown network category");
            var category=Convert.ToInt32(result);
            if(category is 0 or 1)PrivateLanListenerPolicy.EnsureSupportedCategory(category);
        }
        catch(System.Reflection.TargetInvocationException error)
        {
            throw new Exception($"Network List Manager profile lookup failed: {error.InnerException?.GetType().Name}: {error.InnerException?.Message}",error.InnerException??error);
        }
    }
    private static void NetworkProfileInteropSignature()
    {
        var flags=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance;
        var connection=typeof(PrivateLanListenerPolicy).GetNestedType("IBridgeNetworkConnection",System.Reflection.BindingFlags.NonPublic);
        var profile=typeof(PrivateLanListenerPolicy).GetNestedType("IBridgeNetworkProfile",System.Reflection.BindingFlags.NonPublic);
        Check(connection is not null&&profile is not null,"Network List Manager COM interfaces are missing");
        var category=typeof(PrivateLanListenerPolicy).GetNestedType("BridgeNetworkCategory",System.Reflection.BindingFlags.NonPublic);
        Check(category is not null,"Network List Manager category enum is missing");
        foreach(var (type,methodName,parameterType) in new[]{(connection!,"GetAdapterId",typeof(Guid)),(profile!,"GetCategory",category!)})
        {
            var method=type.GetMethod(methodName,flags);
            Check(method is not null,$"{methodName} COM method is missing");
            Check(method!.ReturnType==typeof(void),$"{methodName} must surface its HRESULT output as an explicit out parameter");
            var parameters=method.GetParameters();
            Check(parameters.Length==1,$"{methodName} must declare one output parameter");
            Check(parameters[0].IsOut,$"{methodName} parameter must be marked out");
            Check(parameters[0].ParameterType.GetElementType()==parameterType,$"{methodName} output parameter type was incorrect");
        }
    }
    private static void AdapterQualitySummary()
    {
        Exception? error=null;
        var thread=new Thread(() =>
        {
            try
            {
                var inspected=new List<string>();
                using var panel=new BridgePanel(()=>BridgeAdapterCatalog.Build(
                    [new("wifi","Z Wi-Fi",NetworkInterfaceType.Wireless80211),new("ethernet","A Ethernet",NetworkInterfaceType.Ethernet),new("disconnected","Disconnected Wi-Fi",NetworkInterfaceType.Wireless80211),new("vpn","VPN",NetworkInterfaceType.Tunnel)],
                    id=>{inspected.Add(id);return id=="disconnected"?"NETWORK_ADAPTER_NOT_CONNECTED":null;}));
                panel.RefreshAdapters();

                Check(inspected.Count==3&&inspected.ToHashSet(StringComparer.Ordinal).SetEquals(["wifi","ethernet","disconnected"]),"refresh did not inspect every supported network candidate");
                var selector=panel.Controls.Find("BridgeAdapterSelector",true).OfType<ComboBox>().Single();
                var labels=selector.Items.Cast<object>().Select(item=>item.ToString()??"").ToArray();
                Check(labels[0].Contains("推荐"),"recommended adapter status is not visible in the list");
                Check(labels.Any(label=>label.Contains("可用")),"other eligible adapters are not marked usable");
                Check(labels.Any(label=>label.Contains("不可用")),"failed adapters are not marked unavailable");

                var info=(Label)typeof(BridgePanel).GetField("_adapterInfo",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(panel)!;
                Check(info.Text.Contains("已检查 3 个候选")&&info.Text.Contains("2 个可用")&&info.Text.Contains("1 个不可用"),"refresh summary omitted candidate or pass/fail counts");
                Check(info.Text.Contains("推荐网卡：Z Wi-Fi"),"refresh summary omitted the recommended adapter name");
                Check(info.Text.Contains("网卡未连接"),"refresh summary omitted the failed-candidate reason");
            }
            catch(Exception e){error=e;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw error;
    }
    private static void AdapterSelectionOnRefresh()
    {
        Exception? error=null;
        var thread=new Thread(() =>
        {
            try
            {
                var available=true;
                using var panel=new BridgePanel(()=>BridgeAdapterCatalog.Build(
                    [new("ethernet","A Ethernet",NetworkInterfaceType.Ethernet),new("wifi","Z Wi-Fi",NetworkInterfaceType.Wireless80211),new("bad","0 Virtual",NetworkInterfaceType.Ethernet)],
                    id=>!available?"NETWORK_PROFILE_UNAVAILABLE":id=="bad"?"PHYSICAL_ADAPTER_REQUIRED":null));
                var selector=panel.Controls.Find("BridgeAdapterSelector",true).OfType<ComboBox>().Single();
                string? Selected()=> (selector.SelectedItem as BridgeAdapterChoice)?.Id;
                panel.RefreshAdapters();
                Check(Selected()=="wifi","first refresh did not select recommended Wi-Fi");
                panel.SetConfiguration(false,43189,"ethernet");panel.RefreshAdapters();
                Check(Selected()=="ethernet","refresh replaced a usable manual Ethernet choice");
                panel.SetConfiguration(false,43189,"bad");panel.RefreshAdapters();
                Check(Selected()=="wifi","refresh kept an unavailable adapter despite an eligible alternative");
                panel.SetConfiguration(false,43189,"missing");panel.RefreshAdapters();
                Check(Selected()=="wifi","refresh kept a missing adapter despite an eligible alternative");
                available=false;panel.RefreshAdapters();
                Check(Selected()=="wifi"&&selector.SelectedItem is BridgeAdapterChoice{CanListen:false,UnavailableReason:not null},"refresh lost the explanation when no adapter was usable");
            }
            catch(Exception e){error=e;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw error;
    }
    private static void ExpansionBoundsLayoutPasses()
    {
        Exception? error=null;
        var thread=new Thread(() =>
        {
            try
            {
                using var form=new Form{ClientSize=new System.Drawing.Size(900,900),ShowInTaskbar=false,Opacity=0};
                using var panel=new BridgePanel(()=>[]){Dock=DockStyle.Top};
                form.Controls.Add(panel);form.Show();Application.DoEvents();
                var layoutCounts=new Dictionary<Control,int>();
                void Track(Control control)
                {
                    layoutCounts[control]=0;
                    control.Layout+=(_,_)=>layoutCounts[control]++;
                    foreach(Control child in control.Controls)Track(child);
                }
                Track(form);
                var toggle=panel.Controls.Find("BridgeToggle",true).OfType<Button>().Single();
                toggle.PerformClick();
                Application.DoEvents();
                var maxLayouts=layoutCounts.Values.DefaultIfEmpty().Max();
                Check(maxLayouts<=3,$"one expansion triggered {maxLayouts} layout passes on a control ({layoutCounts.Values.Sum()} total)");
            }
            catch(Exception e){error=e;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(error is not null)throw error;
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
                Check(toggle!.Text=="AI 额度桥　›"&&toggle.FlatAppearance.MouseOverBackColor==UiTheme.Card,
                    "collapsed bridge header indicator is visually detached from the title");
                var input=panel.Controls.Find("BridgeNickname",true).OfType<TextBox>().Single();
                Check(!input.Visible,"bridge settings should start collapsed");
                var collapsed=panel.Height;
                toggle.PerformClick();Application.DoEvents();form.PerformLayout();
                Check(toggle.Text=="AI 额度桥　⌄","expanded bridge header indicator did not reflect its state");
                Check(input.Visible&&panel.Height>collapsed+200,"expanded bridge settings are clipped or hidden");
                toggle.PerformClick();Application.DoEvents();form.PerformLayout();
                Check(!input.Visible&&panel.Height==collapsed,"bridge did not return to collapsed layout");
                Check(configured==0,"folding changed sharing configuration");
            }
            catch(Exception e){error=e;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw error;
    }
    private static void ResponsiveConfigurationRow()
    {
        Exception? error=null;
        var thread=new Thread(() =>
        {
            try
            {
                using var form=new Form{ClientSize=new System.Drawing.Size(700,900),ShowInTaskbar=false,Opacity=0};
                using var panel=new BridgePanel{Dock=DockStyle.Top};
                form.Controls.Add(panel);form.Show();Application.DoEvents();
                panel.Controls.Find("BridgeToggle",true).OfType<Button>().Single().PerformClick();
                Application.DoEvents();form.PerformLayout();panel.PerformLayout();
                var row=panel.Controls.Find("BridgeConfigurationRow",true).SingleOrDefault();
                Check(row is FlowLayoutPanel{WrapContents:true},"bridge configuration should wrap instead of extending past the card");
                var refresh=panel.Controls.Find("BridgeRefreshAdapters",true).OfType<Button>().SingleOrDefault();
                Check(refresh is not null&&refresh.Visible,"refresh adapter action is missing or hidden");
                Check(refresh!.Right<=row!.ClientSize.Width&&refresh.Bottom<=row.ClientSize.Height,"refresh adapter action extends beyond the wrapped row");
                Check(refresh.Top>row.Controls[0].Top,"refresh adapter action did not wrap onto a visible continuation row at narrow widths");
            }
            catch(Exception e){error=e;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw error;
    }
    private static void ApprovalFocus()
    {
        Exception? error=null;
        var thread=new Thread(()=>
        {
            try
            {
                using var form=new Form{ClientSize=new System.Drawing.Size(900,1200),ShowInTaskbar=false,Opacity=0};
                using var panel=new BridgePanel(()=>[]){Dock=DockStyle.Top};
                form.Controls.Add(panel);form.Show();Application.DoEvents();
                panel.Controls.Find("BridgeToggle",true).OfType<Button>().Single().PerformClick();
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var pending=(ListView)typeof(BridgePanel).GetField("_pending",flags)!.GetValue(panel)!;
                Check(!pending.HideSelection,"pending request highlight disappears when approval takes focus");
                pending.Items.Add(new ListViewItem(new[]{"Synthetic phone","127.0.0.1","future"}){Tag="request"});
                pending.Items[0].Selected=true;pending.Focus();
                var completion=new TaskCompletionSource();var calls=0;
                Func<Task> action=()=>{calls++;return completion.Task;};
                using var button=(Button)typeof(BridgePanel).GetMethod("Button",flags)!.Invoke(panel,new object?[]{"test approval",action,null})!;
                form.Controls.Add(button);button.BringToFront();button.Focus();button.PerformClick();
                Check(button.Enabled&&button.Focused,"approval processing disabled the button and moved focus to the next action");
                button.PerformClick();Check(calls==1,"pending approval action can execute twice");
                Check(pending.SelectedItems.Count==1,"taking approval focus cleared the selected request");
                completion.SetResult();Application.DoEvents();
                pending.Items.Clear();
                try{typeof(BridgePanel).GetMethod("Decide",flags)!.Invoke(panel,new object[]{true});throw new Exception("unselected approval silently returned");}
                catch(System.Reflection.TargetInvocationException e)
                {Check(e.InnerException is InvalidOperationException&&e.InnerException.Message.Contains("勾选"),"unselected approval did not explain how to check a phone");}
            }
            catch(Exception e){error=e;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw error;
    }
    private static void SelectableDeviceLists()
    {
        Exception? error=null;
        var thread=new Thread(() =>
        {
            try
            {
                using var form=new Form{ClientSize=new System.Drawing.Size(860,1100),ShowInTaskbar=false,Opacity=0};
                using var panel=new BridgePanel(()=>[]){Dock=DockStyle.Top};
                form.Controls.Add(panel);form.Show();Application.DoEvents();
                panel.Controls.Find("BridgeToggle",true).OfType<Button>().Single().PerformClick();
                Application.DoEvents();form.PerformLayout();panel.PerformLayout();
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var pending=(ListView)typeof(BridgePanel).GetField("_pending",flags)!.GetValue(panel)!;
                var devices=(ListView)typeof(BridgePanel).GetField("_devices",flags)!.GetValue(panel)!;
                Check(pending.CheckBoxes&&pending.MultiSelect,"pending requests do not support checkbox multi-selection");
                Check(devices.CheckBoxes&&devices.MultiSelect,"paired devices do not support checkbox multi-selection");
                Check(pending.Columns.Count==3&&pending.Columns[2].Text.Contains("到期"),"pending list does not expose an expiration-time column");
                Check(devices.Columns.Count==3&&devices.Columns[2].Text.Contains("配对时间"),"paired list cannot distinguish devices with the same name");
                foreach(var list in new[]{pending,devices})
                    Check(list.Columns.Cast<ColumnHeader>().Sum(c=>c.Width)<=list.ClientSize.Width,
                        $"{list.Name} columns overflow the visible list and require horizontal scrolling");
                foreach(var list in new[]{pending,devices})
                    Check(!HorizontalScrollbarVisible(list),$"{list.Name} displays a horizontal scroll bar below the rows");
                Check(panel.Controls.Find("BridgeReplaceSelected",true).OfType<Button>().Any(),"manual replace action is missing");
                var approve=panel.Controls.Find("BridgeApproveSelected",true).OfType<Button>().Single();
                var reject=panel.Controls.Find("BridgeRejectSelected",true).OfType<Button>().Single();
                var replaceButton=panel.Controls.Find("BridgeReplaceSelected",true).OfType<Button>().Single();
                var rename=panel.Controls.Find("BridgeRenameSelected",true).OfType<Button>().Single();
                var authorize=panel.Controls.Find("BridgeAuthorizeSelected",true).OfType<Button>().Single();
                var revoke=panel.Controls.Find("BridgeRevokeSelected",true).OfType<Button>().Single();
                Check(!approve.Enabled&&!reject.Enabled&&!replaceButton.Enabled&&!rename.Enabled&&!authorize.Enabled&&!revoke.Enabled,
                    "batch actions are enabled without checked rows");

                pending.Items.Add(new ListViewItem(new[]{"Phone", "192.168.1.10", "2026-10-08 12:00"}){Tag="pending-1"});
                devices.Items.Add(new ListViewItem(new[]{"Phone", "已授权", "2026-10-07 12:00"}){Tag="device-1"});
                Application.DoEvents();
                foreach(var list in new[]{pending,devices})
                {
                    using var uncheckedImage=new System.Drawing.Bitmap(list.Width,list.Height);
                    list.DrawToBitmap(uncheckedImage,list.ClientRectangle);
                    list.Items[0].Checked=true;
                    list.Refresh();Application.DoEvents();
                    using var checkedImage=new System.Drawing.Bitmap(list.Width,list.Height);
                    list.DrawToBitmap(checkedImage,list.ClientRectangle);
                    var rowBounds=list.Items[0].Bounds;
                    var changed=false;
                    for(var y=Math.Max(0,rowBounds.Top);y<Math.Min(list.Height,rowBounds.Bottom);y++)
                        for(var x=0;x<Math.Min(30,list.Width);x++)
                            changed|=uncheckedImage.GetPixel(x,y)!=checkedImage.GetPixel(x,y);
                    Check(changed,$"{list.Name} did not visibly render its checkbox state");
                    list.Items[0].Checked=false;
                }
                pending.Items[0].Selected=true;
                devices.Items[0].Selected=true;
                Application.DoEvents();
                Check(pending.Items[0].Checked&&devices.Items[0].Checked&&approve.Enabled&&reject.Enabled&&approve.Text.Contains("1")&&replaceButton.Enabled&&rename.Enabled&&
                    authorize.Enabled&&authorize.Text.Contains("1")&&revoke.Enabled&&revoke.Text.Contains("1"),
                    "selecting a row did not check it or update the corresponding batch count");
                pending.Items.Add(new ListViewItem(new[]{"Phone 2", "192.168.1.11", "2026-10-08 12:00"}){Tag="pending-2"});
                devices.Items.Add(new ListViewItem(new[]{"Phone 2", "已授权", "2026-10-06 12:00"}){Tag="device-2"});
                pending.Items[1].Selected=true;devices.Items[1].Selected=true;Application.DoEvents();
                Check(approve.Text.Contains("2")&&reject.Text.Contains("2")&&authorize.Text.Contains("2")&&revoke.Text.Contains("2")&&
                    !replaceButton.Enabled&&!rename.Enabled,"multi-selection counts or single-device-only actions are incorrect");
                pending.Items[1].Checked=false;devices.Items[1].Checked=false;Application.DoEvents();

                var update=typeof(BridgePanel).GetMethod("UpdateList",flags)!;
                update.Invoke(panel,new object[]{pending,new[]{("pending-1",new[]{"Phone renamed","192.168.1.10","2026-10-08 12:00"})}});
                update.Invoke(panel,new object[]{devices,new[]{("device-1",new[]{"Phone renamed","已授权","2026-10-07 12:00"})}});
                Check(pending.Items[0].Checked&&pending.Items[0].Selected&&devices.Items[0].Checked&&devices.Items[0].Selected,
                    "list refresh did not preserve still-valid checked and focused rows");
                Check(devices.Items[0].ToolTipText.Contains("device-1"),"paired-device tooltip does not expose the full device ID");

                form.ClientSize=new System.Drawing.Size(520,1100);Application.DoEvents();form.PerformLayout();panel.PerformLayout();
                foreach(var list in new[]{pending,devices})
                {
                    Check(list.Columns.Cast<ColumnHeader>().Sum(c=>c.Width)<=list.ClientSize.Width,
                        $"{list.Name} columns overflow after resizing to a narrow window");
                    Check(!HorizontalScrollbarVisible(list),$"{list.Name} displays a horizontal scroll bar after resizing to a narrow window");
                }
            }
            catch(Exception e){error=e;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw error;
    }
    private static void BatchOperationsContinueAfterFailures()
    {
        var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
        var execute=typeof(BridgePanel).GetMethod("ExecuteBatch",flags);
        Check(execute is not null,"batch operation runner is missing");
        var attempted=new List<string>();
        var result=(System.Collections.IEnumerable)execute!.Invoke(null,new object[]{
            new[]{("one","Phone 1"),("two","Phone 2"),("three","Phone 3")},
            new Func<string,bool>(id=>{attempted.Add(id);if(id=="two")throw new IOException("storage unavailable");return id!="three";})
        })!;
        var outcomes=result.Cast<object>().ToArray();
        Check(attempted.SequenceEqual(new[]{"one","two","three"}),"batch stopped after a failed or rejected device");
        var succeeded=outcomes.Select(value=>(bool)value.GetType().GetProperty("Succeeded")!.GetValue(value)!).ToArray();
        Check(succeeded.SequenceEqual(new[]{true,false,false}),"batch outcomes did not report success, exception, and false result individually");
        Check(outcomes[1].GetType().GetProperty("Error")!.GetValue(outcomes[1])?.ToString()?.Contains("storage unavailable")==true,
            "batch failure summary omitted the per-device error");
    }
    private static void DeviceListsDoNotScrollHorizontally()
    {
        Exception? error=null;
        var thread=new Thread(() =>
        {
            try
            {
                using var form=new Form{ClientSize=new System.Drawing.Size(860,900),ShowInTaskbar=false,Opacity=0};
                using var panel=new BridgePanel(()=>[]){Dock=DockStyle.Top};
                form.Controls.Add(panel);form.Show();Application.DoEvents();
                panel.Controls.Find("BridgeToggle",true).OfType<Button>().Single().PerformClick();
                Application.DoEvents();form.PerformLayout();panel.PerformLayout();
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var lists=new[]{
                    (ListView)typeof(BridgePanel).GetField("_pending",flags)!.GetValue(panel)!,
                    (ListView)typeof(BridgePanel).GetField("_devices",flags)!.GetValue(panel)!
                };
                foreach(var list in lists)
                    for(var index=0;index<16;index++)
                        list.Items.Add(new ListViewItem(new[]{"Long phone name "+index,"192.168.1."+index,"2026-10-08 12:00"}){Tag="row-"+index});
                form.ClientSize=new System.Drawing.Size(520,900);Application.DoEvents();form.PerformLayout();panel.PerformLayout();
                foreach(var list in lists)
                {
                    list.Items[7].Focused=true;
                    list.TopItem=list.Items[5];
                    var topId=list.TopItem?.Tag;
                    var rows=list.Items.Cast<ListViewItem>().Select(item=>((string)item.Tag!,new[]{item.Text+" updated",item.SubItems[1].Text,item.SubItems[2].Text})).ToArray();
                    typeof(BridgePanel).GetMethod("UpdateList",flags)!.Invoke(panel,new object[]{list,rows});
                    Check(Equals(list.FocusedItem?.Tag,"row-7")&&Equals(list.TopItem?.Tag,topId),
                        $"{list.Name} refresh lost the keyboard row or reading position");
                    Check(list.Columns.Cast<ColumnHeader>().Sum(column=>column.Width)<=list.ClientSize.Width,
                        $"{list.Name} columns exceed the viewport while vertical scrolling is active");
                    Check(!HorizontalScrollbarVisible(list),$"{list.Name} displays a useless horizontal scroll bar while rows overflow vertically");
                }

            }
            catch(Exception exception){error=exception;}
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
                Pump(()=>typeof(MainForm).GetField("_monitor",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(form) is not null);
                Check(!form.Visible,"runtime monitoring initialization exposed the background window");
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var savedPorts=(GatewayPorts)typeof(MainForm).GetField("_gatewayPorts",flags)!.GetValue(form)!;
                Check(savedPorts.Party==savedSettings.PartyPort&&savedPorts.Verge==savedSettings.VergePort&&
                    Equals(typeof(MainForm).GetField("_mihomoPath",flags)!.GetValue(form),savedSettings.MihomoPath),
                    "background monitoring started with default connection parameters instead of saved values");
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
