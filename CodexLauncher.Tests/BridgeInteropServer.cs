using CodexLauncher.App;
using CodexLauncher.Core;
using System.Net;
using System.Text.Json;
using QRCoder;

/// <summary>Temporary Android interoperability target. Synthetic identity/quota, loopback only.</summary>
internal static class BridgeInteropServer
{
    internal static async Task RunAsync()
    {
        var root=Path.Combine(Path.GetTempPath(),"CodexLauncher-Android-Interop-"+Guid.NewGuid().ToString("N"));
        SecureBridgeFiles.PrepareDirectory(root);
        using var identity=new BridgeIdentityStore(root).LoadOrCreate();
        var bridgeId=identity.BridgeId;
        await using var quota=new SharedQuotaCoordinator(new SyntheticQuota());
        await quota.ReadAsync(CancellationToken.None);
        using var devices=new BridgeDeviceStore(bridgeId,Path.Combine(root,"devices-"+bridgeId+".dat"));
        using var grants=new BridgeAccountGrantStore(bridgeId,identity.MappingKey,quota);
        using var pairing=new BridgePairingService(bridgeId,identity.Fingerprint,new Uri("https://127.0.0.1:43190"),devices,grants);
        await using var host=new BridgeHost(identity,quota,grants,devices,new LoopbackPolicy(),null,pairing);
        // Test approval is delayed, so clients must actually poll PendingApproval before delivery.
        pairing.PairRequested+=pending=>_ = Task.Run(async()=>
        {
            await Task.Delay(3000);
            try { if(pairing.Approve(pending.PairId))Console.WriteLine("Synthetic desktop approval completed."); }
            catch(ObjectDisposedException) { }
        });
        await host.StartAsync(IPAddress.Loopback,43190);
        var desktopFile=Path.Combine(root,"desktop-invitation.txt");
        var emulatorFile=Path.Combine(root,"emulator-invitation.txt");
        Console.WriteLine("Synthetic interop listener ready at https://127.0.0.1:43190; no real accounts.");
        Console.WriteLine("Invitation files: "+root+"; credentials are not printed.");
        try
        {
            for(var i=0;i<15;i++)
            {
                var invitation=pairing.CreateInvitation().Value??throw new InvalidOperationException("INTEROP_INVITATION_UNAVAILABLE");
                WriteOffer(desktopFile,invitation);
                WriteOffer(emulatorFile,invitation with{Endpoint="https://10.0.2.2:43190"});
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        }
        finally
        {
            File.Delete(desktopFile);
            File.Delete(emulatorFile);
            File.Delete(emulatorFile+".png");
            File.Delete(desktopFile+".png");
            Console.WriteLine("Synthetic interoperability listener stopped.");
        }
    }

    private static void WriteOffer(string path,BridgeInvitation invitation)
    {
        var value=invitation.Endpoint+"/v1/device#"+Uri.EscapeDataString(JsonSerializer.Serialize(invitation,BridgeJson.Options));
        var temporary=path+".tmp";
        File.WriteAllText(temporary,value);
        File.Move(temporary,path,true);
        using var data=QRCodeGenerator.GenerateQrCode(value,QRCodeGenerator.ECCLevel.Q);
        using var code=new PngByteQRCode(data);
        File.WriteAllBytes(path+".png",code.GetGraphic(6));
    }
    private sealed class LoopbackPolicy : IBridgeListenerPolicy
    {
        public void Validate(IPAddress address,int port)
        { if(!IPAddress.IsLoopback(address)||port!=43190)throw new InvalidOperationException("INTEROP_LOOPBACK_ONLY"); }
    }
    private sealed class SyntheticQuota : IQuotaSessionFactory,IQuotaSession
    {
        public event Action<string>? Notification { add{} remove{} }
        public Task<IQuotaSession> OpenAsync(CancellationToken token)=>Task.FromResult<IQuotaSession>(this);
        public Task<string?> InvokeAsync(string method,string? parameters,CancellationToken token)
        {
            var value=method=="account/read"
                ?JsonSerializer.Serialize(new{account=new{type="chatgpt",id="android-interop-synthetic"}})
                :JsonSerializer.Serialize(new{rateLimits=new{primary=new{usedPercent=22,windowDurationMins=300,resetsAt=DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeSeconds()},secondary=new{usedPercent=81,windowDurationMins=10080,resetsAt=DateTimeOffset.UtcNow.AddDays(6).ToUnixTimeSeconds()}}});
            return Task.FromResult<string?>(value);
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
