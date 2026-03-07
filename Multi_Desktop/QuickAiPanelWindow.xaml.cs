using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace Multi_Desktop;

/// <summary>
/// Quick AI パネル (Web版)
/// 選択されたAIサービス(ChatGPT等)をWebView2で表示する
/// </summary>
public partial class QuickAiPanelWindow : Window
{
    private static readonly string WebView2DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "MultiDesktop", "WebView2Data");

    // ChromeデスクトップUAを使用
    private static readonly string ChromeDesktopUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private CoreWebView2Environment? _webViewEnv;
    private bool _isWebViewReady;
    private string _currentServiceUrl = "https://chatgpt.com"; // デフォルト
    private string _currentServiceName = "ChatGPT";

    public event EventHandler? OnPanelHidden;

    public QuickAiPanelWindow(string initialServiceUrl, string serviceName)
    {
        InitializeComponent();
        _currentServiceUrl = initialServiceUrl;
        _currentServiceName = serviceName;
        PanelTitle.Text = $"Quick AI ({_currentServiceName})";
        Loaded += QuickAiPanelWindow_Loaded;
    }

    private async void QuickAiPanelWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(WebView2DataDir);
            _webViewEnv = await CoreWebView2Environment.CreateAsync(
                userDataFolder: WebView2DataDir);

            await InitializeWebView();
        }
        catch
        {
            PlaceholderText.Text = "WebView2の初期化に失敗しました";
            PlaceholderSubText.Text = "WebView2ランタイムがインストールされているか確認してください";
        }
    }

    private async Task InitializeWebView()
    {
        if (_isWebViewReady || _webViewEnv == null) return;

        PlaceholderPanel.Visibility = Visibility.Visible;
        PlaceholderText.Text = $"{_currentServiceName} を読み込み中...";

        try
        {
            await AiWebView.EnsureCoreWebView2Async(_webViewEnv);

            AiWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            AiWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            AiWebView.CoreWebView2.Settings.UserAgent = ChromeDesktopUserAgent;

            // 新しいウィンドウを開くリクエストをブロックしてアプリ内で開く
            AiWebView.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
                AiWebView.CoreWebView2.Navigate(args.Uri);
            };

            AiWebView.CoreWebView2.NavigationCompleted += (s, args) =>
            {
                if (args.IsSuccess)
                {
                    AiWebView.Visibility = Visibility.Visible;
                    PlaceholderPanel.Visibility = Visibility.Collapsed;
                }
            };

            _isWebViewReady = true;

            // 初回ナビゲート
            AiWebView.CoreWebView2.Navigate(_currentServiceUrl);
        }
        catch
        {
            PlaceholderText.Text = "読み込みエラー";
            PlaceholderSubText.Text = "再試行してください";
        }
    }



    // ─── ナビゲーション ──────────────────────────────
    private void GoBack_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isWebViewReady && AiWebView.CoreWebView2?.CanGoBack == true)
            AiWebView.CoreWebView2.GoBack();
    }

    private void Refresh_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isWebViewReady && AiWebView.CoreWebView2 != null)
            AiWebView.CoreWebView2.Reload();
    }

    // ─── ウィンドウ管理 ──────────────────────────────
    private void MinimizePanel_Click(object sender, MouseButtonEventArgs e)
    {
        // 最小化: ウィンドウを隠すが裏で動作し続ける（セッション維持）
        Hide();
        OnPanelHidden?.Invoke(this, EventArgs.Empty);
    }

    private void ClosePanel_Click(object sender, MouseButtonEventArgs e)
    {
        // 閉じる: WebView2リソースを解放して終了
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            if (_isWebViewReady)
            {
                AiWebView?.Dispose();
            }
        }
        catch { }
        base.OnClosed(e);
    }

    /// <summary>指定位置にフローティング表示する</summary>
    public void ShowAt(double left, double top)
    {
        Left = left;
        Top = top;
        Show();
        Activate();
    }

    public void UpdateService(string url, string name)
    {
        if (_currentServiceUrl == url) return;

        _currentServiceUrl = url;
        _currentServiceName = name;
        PanelTitle.Text = $"Quick AI ({_currentServiceName})";

        if (_isWebViewReady && AiWebView.CoreWebView2 != null)
        {
            AiWebView.CoreWebView2.Navigate(url);
        }
    }
}
