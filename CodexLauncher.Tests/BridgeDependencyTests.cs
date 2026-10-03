using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QRCoder;
using Makaretu.Dns;

internal static class BridgeDependencyTests
{
    internal static IEnumerable<(string Name, Action Run)> All =>
    [
        ("Kestrel dependency serves synthetic HTTPS with a pinned self-signed certificate", () => HttpsProbe().GetAwaiter().GetResult()),
        ("QRCoder dependency renders a synthetic invitation PNG", QrProbe),
        ("mDNS dependency exposes an explicit interface filter without starting discovery", MdnsApiProbe)
    ];

    private static async Task HttpsProbe()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=bridge-synthetic-probe", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        // Schannel 服务端需要具备可用密钥容器的证书；默认导入生成临时用户密钥，Dispose 后删除。
        // 不安装信任根、不持久化探针身份、不把私钥写到项目文件。
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 16 * 1024;
            options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate));
        });
        await using var host = builder.Build();
        // 独立依赖探针，不是产品业务路由；只有合成固定字符串，绑定回环临时端口。
        host.MapGet("/probe", () => "synthetic-only");
        await host.StartAsync();
        try
        {
            var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var handler = new HttpClientHandler
            {
                UseProxy = false,
                ServerCertificateCustomValidationCallback = (_, actual, _, _) =>
                    actual?.GetCertHashString(HashAlgorithmName.SHA256) == fingerprint
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            if (await client.GetStringAsync(address + "/probe") != "synthetic-only") throw new Exception("HTTPS probe response mismatch");
            using var wrongHandler = new HttpClientHandler { UseProxy = false, ServerCertificateCustomValidationCallback = (_, _, _, _) => false };
            using var wrongClient = new HttpClient(wrongHandler) { Timeout = TimeSpan.FromSeconds(5) };
            try { await wrongClient.GetAsync(address + "/probe"); throw new Exception("wrong fingerprint accepted"); }
            catch (HttpRequestException) { }
        }
        catch (HttpRequestException exception)
        {
            throw new Exception($"TLS probe: {exception.GetBaseException().GetType().Name}: {exception.GetBaseException().Message}", exception);
        }
        finally { await host.StopAsync(); }
    }

    private static void QrProbe()
    {
        using var data = QRCodeGenerator.GenerateQrCode("https://192.0.2.1:43189/v1/device#synthetic-only", QRCodeGenerator.ECCLevel.Q);
        using var code = new PngByteQRCode(data);
        var png = code.GetGraphic(4);
        if (!png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new Exception("PNG probe failed");
    }

    private static void MdnsApiProbe()
    {
        // API 兼容性探针不代表指定物理网卡的出站包隔离已通过。
        using var service = new MulticastService(_ => Array.Empty<NetworkInterface>()) { UseIpv4 = true, UseIpv6 = false };
        if (service.UseIpv6 || !service.UseIpv4) throw new Exception("IPv4-only configuration unavailable");
    }
}
