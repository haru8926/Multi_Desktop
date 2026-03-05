using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Multi_Desktop;

/// <summary>
/// 音楽サービスのログイン用WebView2ウィンドウ
/// Cookie を共有するため UserDataFolder を統一パスに設定
/// </summary>
public partial class MusicLoginWindow : Window
{
    private static readonly string WebView2DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "MultiDesktop", "WebView2Data");

    private readonly string _serviceUrl;
    private readonly string _serviceName;

    public MusicLoginWindow(string serviceName, string serviceUrl)
    {
        InitializeComponent();
        _serviceName = serviceName;
        _serviceUrl = serviceUrl;
        Title = $"ログイン — {serviceName}";
        HeaderText.Text = $"{serviceName} にログイン";
        Loaded += MusicLoginWindow_Loaded;
    }

    private async void MusicLoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "読み込み中...";
            Directory.CreateDirectory(WebView2DataDir);

            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: WebView2DataDir);
            await LoginWebView.EnsureCoreWebView2Async(env);

            // サービスに応じたUA を設定
            // Amazon Music はiPhone Safari UA を拒否するため Chrome UA を使用
            if (_serviceName == "Amazon Music")
            {
                LoginWebView.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
            }
            else
            {
                LoginWebView.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Linux; Android 14; Pixel 8 Pro) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Mobile Safari/537.36";
            }

            // ズームレベル80%
            LoginWebView.ZoomFactor = 0.8;

            LoginWebView.CoreWebView2.NavigationCompleted += (s, args) =>
            {
                StatusText.Text = args.IsSuccess ? "ログインしてください" : "読み込みエラー";
            };

            LoginWebView.CoreWebView2.Navigate(_serviceUrl);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"エラー: {ex.Message}";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        try { LoginWebView?.Dispose(); } catch { }
        base.OnClosed(e);
    }
}
