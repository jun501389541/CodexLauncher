using CodexLauncher.Core;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.NetworkInformation;

namespace CodexLauncher.App;

internal sealed record BridgeAdapterChoice(string Id,string Name,bool CanListen,string? UnavailableReason)
{
    internal bool Recommended{get;init;}
    internal static BridgeAdapterChoice FromError(string id,string name,string? errorCode)
    {
        var reason=errorCode switch
        {
            null=>null,
            "NETWORK_ADAPTER_NOT_FOUND"=>"网卡不存在或已断开。",
            "NETWORK_ADAPTER_NOT_CONNECTED"=>"网卡未连接，请连接 Wi-Fi 或网线。",
            "UNSUPPORTED_NETWORK_ADAPTER"=>"只支持 Wi-Fi 或以太网网卡。",
            "PRIVATE_IPV4_REQUIRED"=>"没有局域网 IPv4 地址，请连接与手机同一网络的 Wi-Fi 或以太网。",
            "PHYSICAL_ADAPTER_REQUIRED"=>"这是虚拟网卡，请选择实际的 Wi-Fi 或以太网网卡。",
            "NETWORK_PROFILE_UNAVAILABLE"=>"无法确认 Windows 网络类型，请稍后刷新网卡；公用和专用网络均支持。",
            "NETWORK_PROFILE_UNSUPPORTED"=>"当前网络类型暂不支持，请选择公用或专用的 Wi-Fi / 以太网连接。",
            "NETWORK_PROFILE_CHECK_FAILED"=>"Windows 网络类型读取失败；实体网卡检查已通过。",
            "NETWORK_POLICY_ACCESS_DENIED"=>"Windows 拒绝读取网卡权限信息（访问被拒绝）。",
            "NETWORK_POLICY_CHECK_FAILED"=>"无法读取 Windows 网卡安全信息，请稍后重试。",
            "NETWORK_SUSPENDED"=>"网络当前处于挂起状态。",
            "INVALID_PORT"=>"端口号无效。",
            _ when errorCode.StartsWith("NETWORK_HARDWARE_CHECK_FAILED_",StringComparison.Ordinal)=>$"Windows 网卡硬件信息读取失败（错误码 {errorCode["NETWORK_HARDWARE_CHECK_FAILED_".Length..]}）。",
            _=>$"网卡检查失败：{errorCode}。"
        };
        return new(id,name,reason is null,reason);
    }

    public override string ToString()=>CanListen
        ?Recommended?$"{Name}（推荐）":$"{Name}（可用）"
        :$"{Name}（不可用）";
}
internal sealed record BridgeAdapterDescriptor(string Id,string Name,string Description,NetworkInterfaceType Type)
{
    internal BridgeAdapterDescriptor(string id,string name,NetworkInterfaceType type):this(id,name,name,type){}
}
internal static class BridgeAdapterCatalog
{
    private static readonly string[] FilterComponentMarkers=
    [
        "Npcap Packet Driver",
        "QoS Packet Scheduler",
        "WFP Native MAC Layer",
        "WFP 802.3 MAC Layer",
        "Native WiFi Filter Driver",
        "Virtual WiFi Filter Driver",
        "NDIS Light-Weight Filter",
        "NDIS Lightweight Filter",
        "Virtual Switch Extension",
        "WAN Miniport",
        "Microsoft Wi-Fi Direct Virtual Adapter",
        "Hyper-V Virtual Ethernet Adapter",
        "Bluetooth Device (Personal Area Network)",
        "Microsoft Network Adapter Multiplexor Driver"
    ];

    internal static IReadOnlyList<BridgeAdapterChoice> Build(IEnumerable<BridgeAdapterDescriptor> adapters,Func<string,string?> inspect)
    {
        var choices=adapters.Where(a=>a.Type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            .Where(a=>!IsFilterComponent(a))
            .Select(a=>(Adapter:a,Choice:BridgeAdapterChoice.FromError(a.Id,a.Name,inspect(a.Id))))
            .OrderByDescending(a=>a.Choice.CanListen)
            .ThenBy(a=>a.Adapter.Type==NetworkInterfaceType.Wireless80211?0:1)
            .ThenBy(a=>a.Choice.Name)
            .ThenBy(a=>a.Choice.Id,StringComparer.Ordinal)
            .Select(a=>a.Choice).ToArray();
        if(choices.Length>0&&choices[0].CanListen)choices[0]=choices[0] with{Recommended=true};
        return choices;
    }

    private static bool IsFilterComponent(BridgeAdapterDescriptor adapter)
    {
        var identity=$"{adapter.Name}\n{adapter.Description}";
        return FilterComponentMarkers.Any(marker=>identity.Contains(marker,StringComparison.OrdinalIgnoreCase));
    }
}

internal static class BridgeAdapterDisplay
{
    internal static string ToolTipText(BridgeAdapterChoice choice)
        =>choice.CanListen?$"{choice.Name}\r\n{(choice.Recommended?"推荐用于手机额度共享。":"此网卡满足手机配对监听条件。")}手机与电脑需连接同一局域网。":$"{choice.Name}\r\n当前不能用于配对：{choice.UnavailableReason}";

    internal static string QualificationSummary(IEnumerable<BridgeAdapterChoice> candidates,BridgeAdapterChoice? selected)
    {
        var choices=candidates.ToArray();
        if(choices.Length==0)
            return selected is null
                ?"没有检测到 Wi-Fi 或以太网候选网卡。请先连接 Wi-Fi / 网线，再点“刷新网卡”。"
                :$"没有检测到 Wi-Fi 或以太网候选网卡。已保存的网卡“{selected.Name}”不在本次系统扫描结果中。";

        var usable=choices.Where(choice=>choice.CanListen).ToArray();
        var unavailable=choices.Where(choice=>!choice.CanListen).ToArray();
        var lines=new List<string>
        {
            $"质检完成：已检查 {choices.Length} 个候选，{usable.Length} 个可用，{unavailable.Length} 个不可用。"
        };

        var recommended=choices.FirstOrDefault(choice=>choice.Recommended);
        lines.Add(recommended is null
            ?"没有通过监听检查的网卡。请连接电脑与手机同一 Wi-Fi / 以太网；公用和专用网络均支持，具体原因见下方。"
            :$"推荐网卡：{recommended.Name}。"
        );

        if(selected is not null)
        {
            var scanned=choices.FirstOrDefault(choice=>choice.Id==selected.Id);
            lines.Add(scanned is null
                ?$"当前保存的网卡“{selected.Name}”已不在系统扫描结果中。"
                :scanned.CanListen
                    ?$"当前选择：{scanned.Name}（可用）。"
                    :$"当前选择不可用：{scanned.Name} — {scanned.UnavailableReason}"
            );
        }

        if(unavailable.Length>0)
        {
            var reasons=unavailable
                .GroupBy(choice=>choice.UnavailableReason??"未提供失败原因")
                .OrderByDescending(group=>group.Count())
                .ThenBy(group=>group.Key,StringComparer.Ordinal)
                .Select(group=>$"{group.Key} × {group.Count()}");
            lines.Add("未通过原因统计："+string.Join("；",reasons));
        }

        return string.Join(Environment.NewLine,lines);
    }

    internal static int AvailableDropDownWidth(Rectangle workingArea,Rectangle selectorBounds)
    {
        if(workingArea.Width<1)throw new ArgumentOutOfRangeException(nameof(workingArea));
        var spaceToRight=workingArea.Right-selectorBounds.Left;
        return Math.Max(1,Math.Min(workingArea.Width,spaceToRight));
    }

    internal static int CalculateDropDownWidth(IEnumerable<string> names,int collapsedWidth,int availableWidth,Func<string,int> measureText,int extraWidth)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(measureText);
        if(collapsedWidth<1)throw new ArgumentOutOfRangeException(nameof(collapsedWidth));
        var widest=names.Select(name=>Math.Max(0,measureText(name))).DefaultIfEmpty(0).Max();
        var maximum=Math.Max(1,availableWidth);
        var minimum=Math.Min(collapsedWidth,maximum);
        return Math.Clamp(widest+Math.Max(0,extraWidth),minimum,maximum);
    }
}

/// <summary>Owns bridge resources only; the caller owns the shared quota coordinator.</summary>
internal sealed class BridgeRuntime:IAsyncDisposable
{
    private sealed record Preferences(string? AdapterId);
    private readonly BridgeIdentity _identity;
    private readonly string _preferencesPath;
    private readonly BridgeLanCoordinator _lan;
    private bool _disposed;
    internal BridgeDeviceStore Devices{get;}
    internal BridgeAccountGrantStore Grants{get;}
    internal string? SelectedAdapter{get;private set;}
    internal bool IdentityWasReset=>_identity.WasReset;
    internal string Fingerprint=>_identity.Fingerprint;
    internal BridgeLanStatus Status=>_lan.Status;
    internal BridgePairingService? Pairing=>(_lan.CurrentEndpoint as BridgeLanEndpoint)?.Pairing;
    internal event Action<BridgeLanStatus>? StatusChanged;
    internal BridgeRuntime(string directory,SharedQuotaCoordinator quota,IBridgeNetworkSource? source=null,DiagnosticLogger? logger=null)
    {
        _identity=new BridgeIdentityStore(directory).LoadOrCreate();
        Devices=new(_identity.BridgeId,Path.Combine(directory,"devices.dat"));
        Grants=new(_identity.BridgeId,_identity.MappingKey,quota,new BridgeAccountGrantRepository(_identity.BridgeId,Path.Combine(directory,"grants.dat")));
        _preferencesPath=Path.Combine(directory,"preferences.dat");
        if(File.Exists(_preferencesPath))
        {
            try{var bytes=SecureBridgeFiles.Read(_preferencesPath);try{SelectedAdapter=JsonSerializer.Deserialize<Preferences>(bytes)?.AdapterId;}finally{CryptographicOperations.ZeroMemory(bytes);}}
            catch(Exception e) when(e is CryptographicException or JsonException or InvalidDataException){SelectedAdapter=null;}
        }
        var network=source??new WindowsBridgeNetworkSource();
        _lan=new(_identity.BridgeId,network,(b,l)=>new BridgeLanEndpoint(_identity,quota,Grants,Devices,network,b,l,logger));
        _lan.StatusChanged+=status=>{logger?.Write("bridge-serve",status.ErrorCode??status.DiscoveryErrorCode??status.State,null);StatusChanged?.Invoke(status);};
    }
    internal void SelectAdapter(string id)
    {
        if(string.IsNullOrWhiteSpace(id)||id.Length>256||id.Any(char.IsControl))throw new ArgumentException("ADAPTER_REQUIRED");
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new Preferences(id));
        try{SecureBridgeFiles.Write(_preferencesPath,bytes);SelectedAdapter=id;}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    internal static IReadOnlyList<BridgeAdapterChoice> AvailableAdapters()
    {
        using var source=new WindowsBridgeNetworkSource();
        var adapters=NetworkInterface.GetAllNetworkInterfaces().Select(n=>new BridgeAdapterDescriptor(n.Id,n.Name,n.Description,n.NetworkInterfaceType));
        return BridgeAdapterCatalog.Build(adapters,id=>source.Inspect(id,43189,null).ErrorCode);
    }
    internal Task StartAsync(int port)=>string.IsNullOrWhiteSpace(SelectedAdapter)?Task.CompletedTask:_lan.StartAsync(SelectedAdapter,port);
    internal Task StopAsync()=>_lan.StopAsync();
    internal void RenameDevice(string id,string name){if(!Devices.Rename(id,name))throw new InvalidOperationException("DEVICE_INVALID");}
    internal void RevokeDevice(string id){Devices.Revoke(id);Grants.Revoke(id);Pairing?.RevokeDevice(id);}
    internal void RevokeAll(){Devices.RevokeAll();Grants.RevokeAll();Pairing?.RevokeAll();}
    internal bool ConfirmAccount(string id)=>Devices.List().Any(d=>d.Id==id)&&Grants.ConfirmCurrentAccount(id);
    public async ValueTask DisposeAsync()
    {
        if(_disposed)return;
        await _lan.DisposeAsync();Grants.Dispose();Devices.Dispose();_identity.Dispose();_disposed=true;
    }
}
