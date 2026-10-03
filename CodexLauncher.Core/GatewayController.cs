using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CodexLauncher.Core;

public interface IGatewayController
{
    Task<GatewaySnapshot?> ReadAsync(CancellationToken cancellationToken);
    Task<GatewaySnapshot> SwitchAsync(GatewayUpstream upstream, CancellationToken cancellationToken);
    Task<GatewaySnapshot> ReloadAsync(GatewayPorts ports, GatewayUpstream selected, CancellationToken cancellationToken);
}

public sealed class GatewayController(HttpClient client, Uri baseUri) : IGatewayController
{
    private Uri Endpoint => new(baseUri, "proxies/Upstream");

    public async Task<GatewaySnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        HttpResponseMessage response;
        try { response = await client.GetAsync(Endpoint, timeout.Token); }
        catch (HttpRequestException ex) when (ex.StatusCode is null) { return null; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (GatewayPortOwner.FindCurrentListenerOwner(baseUri.Port) is null) return null;
            throw new TimeoutException("固定入口控制接口没有及时响应。");
        }
        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidDataException($"固定入口控制接口返回 HTTP {(int)response.StatusCode}。");
            try
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var root = json.RootElement;
                if (!root.TryGetProperty("name", out var name) || name.GetString() != "Upstream"
                    || !root.TryGetProperty("all", out var choices) || choices.ValueKind != JsonValueKind.Array
                    || !choices.EnumerateArray().Any(item => item.GetString() == "Party")
                    || !choices.EnumerateArray().Any(item => item.GetString() == "Verge")
                    || !root.TryGetProperty("now", out var selected)
                    || !Enum.TryParse<GatewayUpstream>(selected.GetString(), out var upstream)
                    || !Enum.IsDefined(upstream))
                    throw new InvalidDataException("9098 端口上的服务不是兼容的 Party/Verge 固定入口。");
                return new GatewaySnapshot(upstream);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new InvalidDataException("9098 端口上的服务不是兼容的 Party/Verge 固定入口。", ex);
            }
        }
    }

    public async Task<GatewaySnapshot> SwitchAsync(GatewayUpstream upstream, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var response = await client.PutAsJsonAsync(Endpoint, new { name = upstream.ToString() }, timeout.Token);
        response.EnsureSuccessStatusCode();
        var state = await ReadAsync(cancellationToken);
        if (state?.Selected != upstream) throw new InvalidOperationException("固定入口没有完成上游切换。");
        return state;
    }

    public async Task<GatewaySnapshot> ReloadAsync(GatewayPorts ports, GatewayUpstream selected, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await client.PutAsJsonAsync(
            new Uri(baseUri, "configs?force=true"),
            new { path = "", payload = GatewayConfig.Build(ports) }, timeout.Token);
        response.EnsureSuccessStatusCode();
        return await SwitchAsync(selected, cancellationToken);
    }
}
