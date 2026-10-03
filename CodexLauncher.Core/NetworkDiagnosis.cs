using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace CodexLauncher.Core;

public interface IHttpProbe
{
    Task<ProbeResult> CheckAsync(RouteKind route, ProxyAddress? proxy, CancellationToken cancellationToken);
}

public sealed record DiagnosisResult(ProbeResult Direct, ProbeResult? Proxy, RouteKind? Selected);

public sealed class NetworkDiagnosis(IHttpProbe probe)
{
    public async Task<DiagnosisResult> RunAsync(ProxyAddress? proxy, CancellationToken cancellationToken)
    {
        var directTask = probe.CheckAsync(RouteKind.Direct, null, cancellationToken);
        var proxyTask = proxy is null ? null : probe.CheckAsync(RouteKind.Proxy, proxy, cancellationToken);
        var direct = await directTask;
        var viaProxy = proxyTask is null ? null : await proxyTask;
        return new DiagnosisResult(direct, viaProxy, NetworkDecision.Choose(direct, viaProxy));
    }
}

public sealed class HttpProbe : IHttpProbe
{
    private static readonly Uri Target = new("https://chatgpt.com/");
    private readonly TimeSpan _timeout;

    /// <summary>启动前检测默认 8 秒；运行期监测按计划传入 3 秒。</summary>
    public HttpProbe(TimeSpan? timeout = null) => _timeout = timeout ?? TimeSpan.FromSeconds(8);

    public async Task<ProbeResult> CheckAsync(RouteKind route, ProxyAddress? proxy, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_timeout);
        try
        {
            if (route == RouteKind.Proxy)
            {
                if (proxy is null) throw new ArgumentException("代理地址缺失。");
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(proxy.Uri.Host, proxy.Uri.Port, linked.Token);
            }
            using var handler = new SocketsHttpHandler
            {
                UseProxy = true,
                Proxy = route == RouteKind.Proxy ? new WebProxy(proxy!.Uri) : null,
                AllowAutoRedirect = false
            };
            using var client = new HttpClient(handler, true);
            using var request = new HttpRequestMessage(HttpMethod.Get, Target);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var code = (int)response.StatusCode;
            return new ProbeResult(NetworkDecision.ClassifyHttp(code), code, timer.Elapsed, $"HTTP {code}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(ProbeStatus.Failed, null, timer.Elapsed, $"{_timeout.TotalSeconds:0} 秒超时");
        }
        catch (SocketException)
        {
            return new ProbeResult(ProbeStatus.Failed, null, timer.Elapsed, "本地代理端口无法连接");
        }
        catch (HttpRequestException)
        {
            return new ProbeResult(ProbeStatus.Failed, null, timer.Elapsed, "网络或 TLS 连接失败");
        }
    }
}
