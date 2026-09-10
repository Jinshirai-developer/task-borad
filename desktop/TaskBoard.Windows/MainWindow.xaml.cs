using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TaskBoard.Windows.Core;

namespace TaskBoard.Windows;

public partial class MainWindow : Window
{
    private static readonly Uri ServiceOrigin = new("https://taskboard-8j6.pages.dev/");
    private static readonly Uri DemoOrigin = new("https://taskboard-demo.invalid/");
    private const string RuntimeUrl = "https://developer.microsoft.com/microsoft-edge/webview2/#download-section";
    private readonly string? _smokeOutput;
    private readonly bool _smokeOnline;
    private WebView2? _browser;
    private Uri? _server;
    private Uri? _verification;
    private CancellationTokenSource? _loginCancellation;
    private bool _demo;
    private bool _loading;
    private bool _closing;
    private bool _smokeCompleted;

    public MainWindow(string? smokeOutput = null, bool smokeOnline = false)
    {
        InitializeComponent();
        _smokeOutput = smokeOutput;
        _smokeOnline = smokeOutput != null && smokeOnline;
        Loaded += async (_, _) =>
        {
            if (_smokeOutput != null && !_smokeOnline) await NavigateAsync(null);
            else await ConnectAsync();
        };
        Closed += (_, _) => { _closing = true; _loginCancellation?.Cancel(); _browser?.Dispose(); };
    }

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();
    private Task ConnectAsync() => NavigateAsync(ServiceOrigin);

    private async void Demo_Click(object sender, RoutedEventArgs e) => await NavigateAsync(null);
    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || _loginCancellation != null) return;
        if (_browser?.CoreWebView2 != null && BrowserHost.Visibility == Visibility.Visible) _browser.Reload();
        else _ = ConnectAsync();
    }
    private void Runtime_Click(object sender, RoutedEventArgs e) => OpenBrowser(new Uri(RuntimeUrl));

    private async Task NavigateAsync(Uri? server)
    {
        if (_loading || _closing) return;
        _loading = true;
        _loginCancellation?.Cancel();
        RetryButton.IsEnabled = false;
        DemoButton.IsEnabled = false;
        OfflineButton.IsEnabled = false;
        OnlineButton.IsEnabled = false;
        BrowserLoginButton.IsEnabled = false;
        BrowserHost.Visibility = Visibility.Collapsed;
        SignInPanel.Visibility = Visibility.Collapsed;
        ConnectionPanel.Visibility = Visibility.Visible;
        ConnectionHeading.Text = server == null ? "お試しモードを開いています" : "Task Boardを開いています";
        ConnectionMessage.Text = "まもなく画面が表示されます。";
        ConnectionProgress.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;
        OfflineButton.Visibility = Visibility.Collapsed;
        RuntimeButton.Visibility = Visibility.Collapsed;
        StatusText.Text = ConnectionHeading.Text + "…";
        try
        {
            if (server != null)
            {
                using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
                using var response = await http.GetAsync(new Uri(server, "api/auth/config"));
                response.EnsureSuccessStatusCode();
                var config = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                using (config)
                    if (!config.RootElement.TryGetProperty("registrationEnabled", out _))
                        throw new InvalidOperationException("サービスを利用できません。時間をおいて再試行してください。");
            }
            if (_closing) return;
            _server = server;
            _demo = server == null;
            var origin = server ?? DemoOrigin;
            _browser?.Dispose();
            BrowserHost.Children.Clear();
            var browser = new WebView2();
            _browser = browser;
            BrowserHost.Children.Add(browser);
            BrowserHost.Visibility = Visibility.Visible;
            ConnectionPanel.Visibility = Visibility.Collapsed;
            SignInPanel.Visibility = Visibility.Collapsed;
            var profile = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(origin.AbsoluteUri)))[..24];
            var userData = _smokeOutput == null
                ? Path.Combine(Preferences.DataDirectory, "WebView2", profile)
                : Path.Combine(Path.GetTempPath(), "TaskBoardSmoke", Guid.NewGuid().ToString("N"));
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await browser.EnsureCoreWebView2Async(environment);
            if (_closing) return;
            var core = browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = server != null;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsBuiltInErrorPageEnabled = false;
            core.PermissionRequested += (_, args) => { args.State = CoreWebView2PermissionState.Deny; args.Handled = true; };
            core.ServerCertificateErrorDetected += (_, args) => args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            core.DownloadStarting += (_, args) => { args.Cancel = true; StatusText.Text = "ファイルのダウンロードはブラウザーで行ってください。"; };
            core.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) || !ServerAddress.SameOrigin(origin, target))
                {
                    args.Cancel = true;
                    if (target != null && args.IsUserInitiated && ServerAddress.IsWebLink(target)) OpenBrowser(target);
                    return;
                }
                if (_demo && target.AbsolutePath == "/login.html") { args.Cancel = true; _ = ConnectAsync(); }
            };
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (args.IsUserInitiated && Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) && ServerAddress.IsWebLink(target))
                    OpenBrowser(target);
            };
            core.WebMessageReceived += async (_, args) =>
            {
                if (_server == null || !Uri.TryCreate(args.Source, UriKind.Absolute, out var source)
                    || !ServerAddress.SameOrigin(_server, source)) return;
                try { if (args.TryGetWebMessageAsString() == "taskboard:sign-in") await SignInAsync(); }
                catch (ArgumentException) { /* Ignore non-string web messages. */ }
            };
            core.ProcessFailed += (_, _) => ShowConnectionError("画面を表示できなくなりました。再試行してください。");
            core.NavigationCompleted += async (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    if (args.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
                    ShowConnectionError("画面を読み込めませんでした。通信状況を確認して再試行してください。");
                    CompleteSmoke(false, "Navigation failed: " + args.WebErrorStatus);
                    return;
                }
                StatusText.Text = _demo ? "お試しモード · 操作内容は実際のアカウントに反映されません。" : "オンライン";
                if (_smokeOutput != null)
                {
                    if (_smokeOnline) await VerifyOnlineAsync(core);
                    else await VerifyDemoAsync(core);
                }
            };
            if (server == null)
            {
                var demoDirectory = Path.Combine(AppContext.BaseDirectory, "demo");
                if (!File.Exists(Path.Combine(demoDirectory, "index.html"))) throw new InvalidOperationException("お試し画面が見つかりません。ZIPをすべて展開して起動してください。");
                core.SetVirtualHostNameToFolderMapping(DemoOrigin.Host, demoDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, args) =>
                {
                    if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var requestUri)
                        || !ServerAddress.SameOrigin(DemoOrigin, requestUri) || requestUri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal))
                        args.Response = environment.CreateWebResourceResponse(null, 403, "Offline demo", "Content-Type: text/plain");
                };
            }
            else
            {
                // Google OAuth must use the system browser. No provider tokens or client secrets enter the WebView.
                var trustedOrigin = JsonSerializer.Serialize(server.GetLeftPart(UriPartial.Authority));
                await core.AddScriptToExecuteOnDocumentCreatedAsync("if (location.origin === " + trustedOrigin + ") { document.addEventListener('click', function(event) { if (!event.target.closest?.('#google-start')) return; event.preventDefault(); event.stopImmediatePropagation(); window.chrome.webview.postMessage('taskboard:sign-in'); }, true); }");
            }
            ServerLabel.Text = server == null ? "お試しモード" : "オンライン";
            OnlineButton.Visibility = server == null ? Visibility.Visible : Visibility.Collapsed;
            BrowserLoginButton.IsEnabled = server != null;
            core.Navigate(new Uri(origin, server == null ? "index.html?demo=1" : "login.html?mode=register").AbsoluteUri);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowConnectionError("画面の表示に必要なMicrosoft Edge WebView2が見つかりません。インストール後にアプリを起動し直してください。");
            RuntimeButton.Visibility = Visibility.Visible;
            CompleteSmoke(false, "WebView2 Runtime is missing.");
        }
        catch (Exception error) when (!_closing)
        {
            ShowConnectionError(error is HttpRequestException or TaskCanceledException
                ? "サーバーに接続できませんでした。通信状況を確認するか、時間をおいて再試行してください。"
                : error is JsonException ? "サービスを利用できません。時間をおいて再試行してください。" : error.Message);
            CompleteSmoke(false, error.GetType().Name + ": " + error.Message);
        }
        finally
        {
            _loading = false;
            if (!_closing) RetryButton.IsEnabled = DemoButton.IsEnabled = OfflineButton.IsEnabled = OnlineButton.IsEnabled = true;
        }
    }

    private async void BrowserLogin_Click(object sender, RoutedEventArgs e) => await SignInAsync();
    private async Task SignInAsync()
    {
        if (_server == null || _demo || _browser?.CoreWebView2 == null || _loginCancellation != null || _loading) return;
        var server = _server;
        var core = _browser.CoreWebView2;
        using var cancellation = new CancellationTokenSource();
        _loginCancellation = cancellation;
        BrowserLoginButton.IsEnabled = false;
        StatusText.Text = "ブラウザーでのログインを準備しています…";
        try
        {
            using var client = new DesktopLoginClient(server);
            var challenge = await client.StartAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _verification = new Uri(challenge.VerificationUri);
            SignInCode.Text = challenge.UserCode;
            BrowserHost.Visibility = Visibility.Collapsed;
            ConnectionPanel.Visibility = Visibility.Collapsed;
            SignInPanel.Visibility = Visibility.Visible;
            OpenBrowser(_verification);
            StatusText.Text = "ブラウザーでの承認を待っています。コードの有効期限は10分です。";
            var cookies = await client.WaitForApprovalAsync(challenge, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            // Copy only application auth cookies into this server's isolated WebView profile.
            // Do not expose cookie values to JavaScript, URLs, logs or settings files.
            var previousCookies = await core.CookieManager.GetCookiesAsync(server.AbsoluteUri);
            cancellation.Token.ThrowIfCancellationRequested();
            foreach (var previous in previousCookies)
                if (previous.Name.StartsWith("TaskBoard.Auth", StringComparison.Ordinal)
                    || previous.Name.StartsWith("__Host-TaskBoard.Auth", StringComparison.Ordinal)) core.CookieManager.DeleteCookie(previous);
            foreach (var cookie in cookies)
            {
                var target = core.CookieManager.CreateCookie(cookie.Name, cookie.Value, server.Host, "/");
                target.IsHttpOnly = true;
                target.IsSecure = cookie.Secure;
                target.SameSite = CoreWebView2CookieSameSiteKind.Lax;
                core.CookieManager.AddOrUpdateCookie(target);
            }
            ShowWeb();
            core.Navigate(new Uri(server, "index.html").AbsoluteUri);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (!_closing)
        {
            ShowWeb();
            StatusText.Text = error is HttpRequestException or TaskCanceledException
                ? "ログイン中に通信が切れました。接続を確認して、もう一度ログインしてください。" : error.Message;
        }
        finally
        {
            if (ReferenceEquals(_loginCancellation, cancellation))
            {
                _loginCancellation = null;
                _verification = null;
                if (!_closing) BrowserLoginButton.IsEnabled = _server != null && !_demo && !_loading
                    && BrowserHost.Visibility == Visibility.Visible;
            }
        }
    }

    private void ReopenLogin_Click(object sender, RoutedEventArgs e) { if (_verification != null) OpenBrowser(_verification); }
    private void CancelLogin_Click(object sender, RoutedEventArgs e)
    {
        _loginCancellation?.Cancel(); ShowWeb(); StatusText.Text = "ログインをキャンセルしました。";
    }
    private void ShowConnectionError(string message)
    {
        if (_closing) return;
        _loginCancellation?.Cancel();
        BrowserHost.Visibility = Visibility.Collapsed;
        SignInPanel.Visibility = Visibility.Collapsed;
        ConnectionPanel.Visibility = Visibility.Visible;
        ConnectionHeading.Text = "Task Boardを開けませんでした";
        ConnectionMessage.Text = message;
        ConnectionProgress.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Visible;
        OfflineButton.Visibility = Visibility.Visible;
        StatusText.Text = "再試行するか、お試しモードをご利用ください。";
        BrowserLoginButton.IsEnabled = false;
        RetryButton.Focus();
    }
    private void ShowWeb()
    {
        if (_closing) return;
        ConnectionPanel.Visibility = Visibility.Collapsed;
        SignInPanel.Visibility = Visibility.Collapsed;
        BrowserHost.Visibility = Visibility.Visible;
    }
    private void OpenBrowser(Uri target)
    {
        if (!ServerAddress.IsWebLink(target)) return;
        try { Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { StatusText.Text = "ブラウザーを開けませんでした。既定のブラウザーを設定してから再度お試しください。"; }
    }

    private async Task VerifyDemoAsync(CoreWebView2 core)
    {
        if (_smokeCompleted) return;
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var result = await core.ExecuteScriptAsync("Boolean(window.TaskDemo?.active && document.getElementById('current-user-name')?.textContent && document.getElementById('current-user-name').textContent !== '読み込み中')");
            if (result == "true") { CompleteSmoke(true, "WebView2 initialized and bundled demo rendered."); return; }
            await Task.Delay(250);
        }
        CompleteSmoke(false, "Bundled demo did not initialize within 20 seconds.");
    }

    private async Task VerifyOnlineAsync(CoreWebView2 core)
    {
        if (_smokeCompleted) return;
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var result = await core.ExecuteScriptAsync("Boolean(location.pathname === '/login.html' && new URLSearchParams(location.search).get('mode') === 'register' && document.getElementById('auth-form')?.getAttribute('aria-busy') === 'false' && document.getElementById('auth-heading')?.textContent === 'アカウントを作成' && document.getElementById('auth-message')?.textContent === '' && !document.getElementById('auth-submit-button')?.disabled)");
            if (result == "true" && ServerAddress.SameOrigin(ServiceOrigin, new Uri(core.Source))
                && ConnectionPanel.Visibility == Visibility.Collapsed)
            {
                CompleteSmoke(true, "Automatic startup opened the public registration screen without a connection form.");
                return;
            }
            await Task.Delay(250);
        }
        CompleteSmoke(false, "Public registration screen did not initialize within 20 seconds.");
    }
    private void CompleteSmoke(bool passed, string detail)
    {
        if (_smokeOutput == null || _smokeCompleted) return;
        _smokeCompleted = true;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_smokeOutput))!);
        File.WriteAllText(_smokeOutput, JsonSerializer.Serialize(new { passed, detail, mode = _smokeOnline ? "online" : "demo", platform = Environment.OSVersion.ToString(), verifiedAt = DateTimeOffset.UtcNow }));
        Application.Current.Shutdown(passed ? 0 : 1);
    }
}
