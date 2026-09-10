using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TaskBoard.Windows.Core;

public sealed record DesktopChallenge(string DeviceCode, string UserCode, string VerificationUri, int ExpiresIn, int Interval);

public sealed class DesktopLoginClient : IDisposable
{
    private readonly Uri _server;
    private readonly HttpClient _http;
    private readonly CookieContainer _cookies = new();
    private string? _csrf;

    public DesktopLoginClient(Uri server)
    {
        _server = server;
        _http = new HttpClient(new HttpClientHandler { CookieContainer = _cookies, AllowAutoRedirect = false })
        { BaseAddress = server, Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<DesktopChallenge> StartAsync(CancellationToken cancellationToken)
    {
        using var response = await PostAsync("api/auth/desktop/start", new { }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var challenge = await response.Content.ReadFromJsonAsync<DesktopChallenge>(cancellationToken)
            ?? throw new InvalidOperationException("ログインを開始できませんでした。");
        if (!Uri.TryCreate(challenge.VerificationUri, UriKind.Absolute, out var verification)
            || !ServerAddress.SameOrigin(_server, verification) || verification.AbsolutePath != "/desktop.html"
            || challenge.ExpiresIn is < 1 or > 600 || challenge.Interval is < 1 or > 30
            || !System.Text.RegularExpressions.Regex.IsMatch(challenge.UserCode, "^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$")
            || !System.Text.RegularExpressions.Regex.IsMatch(challenge.DeviceCode, "^[A-Za-z0-9_-]{43}$"))
            throw new InvalidOperationException("接続先のログイン設定を確認してください。アプリとサーバーのURLが一致していません。");
        return challenge;
    }

    public async Task<IReadOnlyList<Cookie>> WaitForApprovalAsync(DesktopChallenge challenge, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(challenge.ExpiresIn);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(challenge.Interval), cancellationToken);
            using var response = await PostAsync("api/auth/desktop/exchange", new { challenge.DeviceCode }, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Accepted) continue;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 60)), cancellationToken);
                continue;
            }
            await EnsureSuccessAsync(response, cancellationToken);
            var cookies = _cookies.GetCookies(_server).Cast<Cookie>().Where(cookie =>
                System.Text.RegularExpressions.Regex.IsMatch(cookie.Name, "^(?:__Host-)?TaskBoard\\.Auth(?:C[0-9]+)?$")
                && cookie.HttpOnly && !cookie.Expired && (_server.Scheme != "https" || cookie.Secure)).ToArray();
            if (cookies.Length == 0) throw new InvalidOperationException("ログイン情報を受け取れませんでした。もう一度お試しください。");
            return cookies;
        }
        throw new InvalidOperationException("確認コードの期限が切れました。もう一度ログインしてください。");
    }

    private async Task<HttpResponseMessage> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        if (_csrf == null)
        {
            using var csrfResponse = await _http.GetAsync("api/auth/csrf", cancellationToken);
            await EnsureSuccessAsync(csrfResponse, cancellationToken);
            var json = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            _csrf = json.GetProperty("token").GetString() ?? throw new InvalidOperationException("ログインを準備できませんでした。");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", _csrf);
        return await _http.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        string? detail = null;
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (json.TryGetProperty("detail", out var d)) detail = d.GetString();
            else if (json.TryGetProperty("message", out var m)) detail = m.GetString();
        }
        catch (JsonException) { }
        throw new InvalidOperationException(detail ?? (response.StatusCode == HttpStatusCode.NotFound
            ? "このサーバーはWindows版のブラウザーログインに対応していません。サーバーを更新してください。"
            : "サーバーに接続できませんでした。接続先と通信状況を確認してください。"));
    }

    public void Dispose() => _http.Dispose();
}
