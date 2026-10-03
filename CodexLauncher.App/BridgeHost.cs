using CodexLauncher.Core;
using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CodexLauncher.App;

internal interface IBridgeDeviceAuthenticator { string? Authenticate(string token); }
internal interface IBridgeListenerPolicy { void Validate(IPAddress address, int port); }

internal sealed class BridgeHost(BridgeIdentity identity, SharedQuotaCoordinator quota,
    BridgeAccountGrantStore grants, IBridgeDeviceAuthenticator devices,
    IBridgeListenerPolicy? listenerPolicy = null, DiagnosticLogger? logger = null,
    BridgePairingService? pairing = null,
    Func<WebApplication,Task>? stopHost = null, Func<WebApplication,ValueTask>? disposeHost = null) : IAsyncDisposable
{
    private readonly IBridgeListenerPolicy _policy = listenerPolicy ?? new PrivateLanListenerPolicy();
    private readonly object _limitSync = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _requests = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private WebApplication? _host;
    private bool _disposed;
    private volatile bool _closing;
    public string? Address { get; private set; }

    public async Task StartAsync(IPAddress selectedAddress, int port = 43189)
    {
        await _lifecycle.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_closing) throw new InvalidOperationException("BRIDGE_CLOSING");
            if (_host is not null) throw new InvalidOperationException("BRIDGE_ALREADY_STARTED");
            _policy.Validate(selectedAddress, port);
            if (identity.Certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
                throw new InvalidOperationException("BRIDGE_CERTIFICATE_EXPIRED");
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders(); // 不让默认ASP.NET日志收集账号URL或请求信息。
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxRequestBodySize = 16 * 1024;
                options.Limits.MaxRequestHeaderCount = 32;
                options.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
                options.Listen(selectedAddress, port, listen => listen.UseHttps(identity.Certificate));
            });
            var host = builder.Build();
            host.Use(async (context, next) =>
            {
                if (_closing) { await Error(context, 503, "BRIDGE_UNAVAILABLE"); return; }
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                // 每次访问重新验证选中地址；网络变成Public时禁止任何响应数据。
                try { _policy.Validate(selectedAddress, port); }
                catch { await Error(context, 503, "NETWORK_UNAVAILABLE"); return; }
                try { pairing?.CleanupExpired(); }
                catch { logger?.Write("bridge-pair", "expiry-cleanup-failed", null); await Error(context, 503, "BRIDGE_UNAVAILABLE"); return; }
                var origin = context.Request.Headers.Origin;
                var expectedOrigin = $"https://{selectedAddress}:{context.Connection.LocalPort}";
                if ((origin.Count > 0 && (origin.Count != 1 || !string.Equals(origin[0], expectedOrigin, StringComparison.OrdinalIgnoreCase))) ||
                    context.Request.Headers["Sec-Fetch-Site"].Any(value => value is "cross-site" or "same-site"))
                { await Error(context, 403, "CROSS_ORIGIN_DENIED"); return; }
                if (context.Request.ContentLength > 16 * 1024)
                { await Error(context, 413, "REQUEST_TOO_LARGE"); return; }
                var isInvite = context.Request.Path == "/v1/pair" && HttpMethods.IsPost(context.Request.Method);
                var isPairSession = context.Request.Path.StartsWithSegments("/v1/pair", out var pairPath) && pairPath.HasValue &&
                    (HttpMethods.IsGet(context.Request.Method) && !pairPath.Value!.EndsWith("/ack", StringComparison.Ordinal) ||
                     HttpMethods.IsPost(context.Request.Method) && pairPath.Value!.EndsWith("/ack", StringComparison.Ordinal));
                if (context.Request.Path != "/v1/health" && context.Request.Path != "/v1/device" && !isInvite && !isPairSession)
                {
                    var auth = context.Request.Headers.Authorization;
                    var token = auth.Count == 1 && auth[0]?.StartsWith("Bearer ", StringComparison.Ordinal) == true ? auth[0]![7..] : null;
                    var device = token is { Length: > 0 and <= 256 } ? devices.Authenticate(token) : null;
                    if (device is null) { await Error(context, 401, "DEVICE_INVALID"); return; }
                    try { pairing?.ObserveDeviceAuthenticated(device); }
                    catch { logger?.Write("bridge-pair", "delivery-commit-failed", null); await Error(context, 503, "BRIDGE_UNAVAILABLE"); return; }
                    context.Items["bridge-device"] = device;
                    var retry = Limit(device);
                    if (retry > 0) { logger?.Write("bridge-limit", "device-rate-limited", null); await Error(context, 429, "DEVICE_RATE_LIMITED", retry); return; }
                }
                // Kestrel仅在读取正文时执行大小限制；覆盖未知Content-Length/分块正文。
                if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
                {
                    context.Request.EnableBuffering(16 * 1024, 16 * 1024);
                    try
                    {
                        var buffer = new byte[1024];
                        while (await context.Request.Body.ReadAsync(buffer, context.RequestAborted) > 0) { }
                        context.Request.Body.Position = 0;
                    }
                    catch (BadHttpRequestException exception) when (exception.StatusCode == 413)
                    { await Error(context, 413, "REQUEST_TOO_LARGE"); return; }
                    catch (IOException)
                    { await Error(context, 413, "REQUEST_TOO_LARGE"); return; }
                }
                try { await next(context); }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
                catch { logger?.Write("bridge-serve", "request-failed", null); if (!context.Response.HasStarted) await Error(context, 500, "BRIDGE_UNAVAILABLE"); }
            });
            host.MapGet("/v1/health", () => Results.Json(new { schemaVersion = 1, bridgeId = identity.BridgeId, version = "1.0", status = "READY" }, BridgeJson.Options));
            host.MapGet("/v1/device", () =>
            {
                const string page = """
                    <!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>AI 额度桥</title>
                    <style>body{font:16px system-ui;max-width:40rem;margin:2rem auto;padding:0 1rem}code{overflow-wrap:anywhere}button,input{font:inherit;padding:.6rem}#pin{overflow-wrap:anywhere;color:#075c34}#warning{color:#873d00}</style>
                    <h1>AI 额度桥</h1><p>连接地址：<code id="address"></code></p><p>服务证书指纹：<code id="pin"></code></p>
                    <p id="warning">在浏览器证书查看器核对实际 TLS 证书指纹后再配对。此页面显示的是配置值，不能证明 TLS 对端证书；浏览器无法强制证书 pinning。</p>
                    <form id="pair" hidden><label>设备名称 <input id="name" maxlength="80" required></label> <button>请求配对</button></form><p id="status"></p>
                    <script>
                    const el=id=>document.getElementById(id), status=el('status');
                    el('address').textContent=location.origin; el('pin').textContent='@PIN@';
                    let invite=null;
                    try { if(location.hash.length>1) invite=JSON.parse(decodeURIComponent(location.hash.slice(1))); } catch {}
                    history.replaceState(null,'',location.pathname);
                    if(invite && invite.schemaVersion===1 && invite.endpoint===location.origin && invite.certificateSha256===el('pin').textContent && Date.parse(invite.expiresAt)>Date.now()) {
                      el('pair').hidden=false; el('name').value=navigator.userAgent.includes('Android')?'Android设备':'手机设备';
                      el('pair').addEventListener('submit',async event=>{
                        event.preventDefault(); el('pair').hidden=true;
                        try {
                          const accepted=await fetch('/v1/pair',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({pairToken:invite.pairToken,deviceName:el('name').value})}).then(r=>{if(!r.ok)throw Error('配对请求失败：'+r.status);return r.json()});
                          invite=null; status.textContent='等待电脑确认…';
                          for(;;){
                            await new Promise(r=>setTimeout(r,1500));
                            const response=await fetch('/v1/pair/'+encodeURIComponent(accepted.pairId),{headers:{Authorization:'Bearer '+accepted.sessionToken}});
                            if(response.status===410)throw Error('配对已过期，请重新扫描二维码。');
                            if(!response.ok)throw Error('配对会话无效。');
                            const result=await response.json();
                            if(result.state==='Rejected')throw Error('电脑拒绝了配对请求。');
                            if(result.state==='Expired')throw Error('配对已过期。');
                            if(result.state==='Approved' && result.deviceToken){
                              localStorage.setItem('codex-bridge-device-'+accepted.pairId,result.deviceToken);
                              await fetch('/v1/pair/'+encodeURIComponent(accepted.pairId)+'/ack',{method:'POST',headers:{Authorization:'Bearer '+accepted.sessionToken}});
                              status.textContent='设备已配对，可以查看额度。'; return;
                            }
                            if(result.state==='Delivered'){status.textContent='设备已配对。';return;}
                          }
                        } catch(error){status.textContent=error.message;}
                      });
                    } else if(location.hash) status.textContent='二维码无效或已过期，请重新扫描。';
                    </script></html>
                    """;
                return Results.Content(page.Replace("@PIN@", HtmlEncoder.Default.Encode(identity.Fingerprint), StringComparison.Ordinal), "text/html; charset=utf-8");
            });
            host.MapGet("/v1/providers", () => Results.Json(new[] { new { providerId = "codex", capabilities = new[] { "usage", "refresh" } } }, BridgeJson.Options));
            host.MapGet("/v1/accounts", (HttpContext context) => Respond(grants.Accounts(Device(context))));
            host.MapGet("/v1/accounts/{id}/usage", (HttpContext context, string id) => Respond(grants.Usage(Device(context), id)));
            host.MapPost("/v1/pair", (HttpContext context, BridgePairRequest request) =>
            {
                if (pairing is null) return ErrorResult(503, "PAIRING_UNAVAILABLE");
                if (context.Connection.RemoteIpAddress is not { } source) return ErrorResult(400, "PAIR_SOURCE_UNAVAILABLE");
                return PairResult(context, pairing.RequestPair(request, source));
            });
            host.MapGet("/v1/pair/{id}", (HttpContext context, string id) => pairing is null
                ? ErrorResult(503, "PAIRING_UNAVAILABLE")
                : PairResult(context, pairing.GetStatus(id, ReadBearer(context))));
            host.MapPost("/v1/pair/{id}/ack", (HttpContext context, string id) =>
            {
                if (pairing is null) return ErrorResult(503, "PAIRING_UNAVAILABLE");
                var result = pairing.Acknowledge(id, ReadBearer(context));
                if (result.ErrorCode is not null) return PairResult(context, result);
                return Results.NoContent();
            });
            host.MapPost("/v1/accounts/{id}/refresh", async (HttpContext context, string id) =>
            {
                var snapshot = grants.Usage(Device(context), id);
                if (snapshot.Error is not null) { await Respond(snapshot).ExecuteAsync(context); return; }
                var request = quota.TryRequestManual();
                if (!request.Accepted)
                { await Error(context, 429, "REFRESH_RATE_LIMITED", Math.Max(1, (int)Math.Ceiling(request.RetryAfter.TotalSeconds))); return; }
                _ = ObserveRefresh(request.Completion);
                logger?.Write("bridge-refresh", "accepted", null);
                await Results.Json(snapshot.Value, BridgeJson.Options, statusCode: 202).ExecuteAsync(context);
            });
            try
            {
                await host.StartAsync();
                Address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                _host = host;
                logger?.Write("bridge-serve", "started", null);
            }
            catch { await host.DisposeAsync(); logger?.Write("bridge-serve", "listen-failed", null); throw; }
        }
        finally { _lifecycle.Release(); }
    }
    private static string Device(HttpContext context) => (string)context.Items["bridge-device"]!;
    private static string? ReadBearer(HttpContext context)
    {
        var header = context.Request.Headers.Authorization;
        return header.Count == 1 && header[0]?.StartsWith("Bearer ", StringComparison.Ordinal) == true ? header[0]![7..] : null;
    }
    private static IResult ErrorResult(int status, string code) => Results.Json(new BridgeError(code), BridgeJson.Options, statusCode: status);
    private static IResult PairResult<T>(HttpContext context, BridgePairOperation<T> operation)
    {
        if (operation.RetryAfter is { } retry) context.Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return operation.ErrorCode is null
            ? operation.StatusCode == 204 ? Results.NoContent() : Results.Json(operation.Value, BridgeJson.Options, statusCode: operation.StatusCode)
            : Results.Json(new BridgeError(operation.ErrorCode), BridgeJson.Options, statusCode: operation.StatusCode);
    }
    private static IResult Respond<T>(BridgeReadResult<T> result) where T : class => result.Error is null
        ? Results.Json(result.Value, BridgeJson.Options, statusCode: result.StatusCode)
        : Results.Json(result.Error, BridgeJson.Options, statusCode: result.StatusCode);
    private async Task ObserveRefresh(Task<QuotaSnapshot> completion)
    {
        try { await completion.ConfigureAwait(false); logger?.Write("bridge-refresh", "completed", null); }
        catch (OperationCanceledException) { logger?.Write("bridge-refresh", "cancelled", null); }
        catch { logger?.Write("bridge-refresh", "upstream-unavailable", null); }
    }
    private int Limit(string device)
    {
        lock (_limitSync)
        {
            var now = DateTimeOffset.UtcNow;
            if (!_requests.TryGetValue(device, out var queue)) _requests.Add(device, queue = new());
            while (queue.TryPeek(out var head) && now - head >= TimeSpan.FromMinutes(1)) queue.Dequeue();
            if (queue.Count >= 60) return Math.Max(1, (int)Math.Ceiling((queue.Peek().AddMinutes(1) - now).TotalSeconds));
            queue.Enqueue(now); return 0;
        }
    }
    private static Task Error(HttpContext context, int status, string code, int? retry = null)
    {
        context.Response.StatusCode = status;
        if (status == 413 && context.Request.Protocol == "HTTP/1.1") context.Response.Headers.Connection = "close";
        if (retry is not null) context.Response.Headers.RetryAfter = retry.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return context.Response.WriteAsJsonAsync(new BridgeError(code), BridgeJson.Options, context.RequestAborted);
    }
    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_disposed) return;
            _closing = true;
            try
            {
                if (_host is not null)
                {
                    try { await (stopHost?.Invoke(_host) ?? _host.StopAsync()); }
                    catch { logger?.Write("bridge-serve", "stop-failed-disposing", null); }
                    // A failed dispose retains the reference and permits another cleanup attempt.
                    await (disposeHost?.Invoke(_host) ?? _host.DisposeAsync());
                    _host = null;
                }
            }
            finally { pairing?.CancelPending(); }
            _disposed = true;
            Address = null;
            lock (_limitSync) _requests.Clear();
            logger?.Write("bridge-serve", "stopped", null);
        }
        finally { _lifecycle.Release(); }
    }
}
