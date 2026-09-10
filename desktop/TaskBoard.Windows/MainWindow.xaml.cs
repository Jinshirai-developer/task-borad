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
    private static readonly Uri DemoOrigin = new("https://taskboard-demo.invalid/");
    private const string RuntimeUrl = "https://developer.microsoft.com/microsoft-edge/webview2/#download-section";
    private readonly string? _smokeOutput;
    private WebView2? _browser;
    private Uri? _server;
    private Uri? _verification;
    private CancellationTokenSource? _loginCancellation;
    private bool _demo;
    private bool _loading;
    private bool _closing;
    private bool _smokeCompleted;

    public MainWindow(string? smokeOutput = null)
    {
        InitializeComponent();
        _smokeOutput = smokeOutput;
        ServerInput.Text = Preferences.Load().ServerUrl ?? "";
        Loaded += async (_, _) =>
        {
            if (_smokeOutput != null) await NavigateAsync(null);
            else if (!string.IsNullOrWhiteSpace(ServerInput.Text)) await ConnectAsync();
        };
        Closed += (_, _) => { _closing = true; _loginCancellation?.Cancel(); _browser?.Dispose(); };
    }

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();
    private async Task ConnectAsync()
    {
        if (_loading) return;
        try { await NavigateAsync(ServerAddress.Parse(ServerInput.Text)); }
        catch (ArgumentException error) { ShowSetup(error.Message); }
    }

    private async void Demo_Click(object sender, RoutedEventArgs e) => await NavigateAsync(null);
    private void Settings_Click(object sender, RoutedEventArgs e) { if (!_loading) ShowSetup(); }
    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || _loginCancellation != null) return;
        if (_browser?.CoreWebView2 != null && BrowserHost.Visibility == Visibility.Visible) _browser.Reload();
        else if (_server != null) _ = NavigateAsync(_server);
    }
    private void Runtime_Click(object sender, RoutedEventArgs e) => OpenBrowser(new Uri(RuntimeUrl));

    private async Task NavigateAsync(Uri? server)
    {
        if (_loading || _closing) return;
        _loading = true;
        _loginCancellation?.Cancel();
        ConnectButton.IsEnabled = false;
        BrowserLoginButton.IsEnabled = false;
        SetupError.Text = "";
        RuntimeButton.Visibility = Visibility.Collapsed;
        StatusText.Text = server == null ? "お試しモードを準備しています…" : "接続先を確認しています…";
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
                        throw new InvalidOperationException("Task Boardの接続先URLを入力してください。");
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
            SetupPanel.Visibility = Visibility.Collapsed;
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
                if (_demo && target.AbsolutePath == "/login.html") { args.Cancel = true; ShowSetup(); }
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
            core.ProcessFailed += (_, _) => ShowSetup("画面を表示するプロセスが終了しました。接続し直してください。");
            core.NavigationCompleted += async (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    if (args.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
                    ShowSetup("画面を読み込めませんでした。接続先と通信状況を確認して、接続し直してください。");
                    CompleteSmoke(false, "Navigation failed: " + args.WebErrorStatus);
                    return;
                }
                StatusText.Text = _demo ? "お試しモード · 操作内容は実際のアカウントに反映されません。" : "接続済み · " + origin.Authority;
                if (_smokeOutput != null) await VerifyDemoAsync(core);
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
            ServerLabel.Text = server?.Authority ?? "お試しモード";
            BrowserLoginButton.IsEnabled = server != null;
            core.Navigate(new Uri(origin, server == null ? "index.html?demo=1" : "index.html").AbsoluteUri);
            if (server != null)
            {
                ServerInput.Text = server.AbsoluteUri.TrimEnd('/');
                try { new Preferences(server.AbsoluteUri).Save(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { StatusText.Text = "接続しました。接続先を保存できなかったため、次回起動時に再入力してください。"; }
            }
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowSetup("画面の表示に必要なMicrosoft Edge WebView2が見つかりません。インストール後にアプリを起動し直してください。");
            RuntimeButton.Visibility = Visibility.Visible;
            CompleteSmoke(false, "WebView2 Runtime is missing.");
        }
        catch (Exception error) when (!_closing)
        {
            ShowSetup(error is HttpRequestException or TaskCanceledException
                ? "接続できませんでした。サーバーが起動しているか、URLと通信状況を確認してください。"
                : error is JsonException ? "Task BoardのサーバーURLを入力してください。" : error.Message);
            CompleteSmoke(false, error.GetType().Name + ": " + error.Message);
        }
        finally { _loading = false; if (!_closing) ConnectButton.IsEnabled = true; }
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
            SetupPanel.Visibility = Visibility.Collapsed;
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
    private void ShowSetup(string message = "")
    {
        if (_closing) return;
        _loginCancellation?.Cancel();
        BrowserHost.Visibility = Visibility.Collapsed;
        SignInPanel.Visibility = Visibility.Collapsed;
        SetupPanel.Visibility = Visibility.Visible;
        SetupError.Text = message;
        StatusText.Text = "接続先を設定するか、お試しモードを開いてください。";
        BrowserLoginButton.IsEnabled = false;
        ServerInput.Focus();
    }
    private void ShowWeb()
    {
        if (_closing) return;
        SetupPanel.Visibility = Visibility.Collapsed;
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
    private void CompleteSmoke(bool passed, string detail)
    {
        if (_smokeOutput == null || _smokeCompleted) return;
        _smokeCompleted = true;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_smokeOutput))!);
        File.WriteAllText(_smokeOutput, JsonSerializer.Serialize(new { passed, detail, platform = Environment.OSVersion.ToString(), verifiedAt = DateTimeOffset.UtcNow }));
        Application.Current.Shutdown(passed ? 0 : 1);
    }
}
