using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Multi_Desktop.Models;

namespace Multi_Desktop;

/// <summary>
/// WebView2ベースの音楽プレーヤーウィンドウ
/// コントロールセンターから起動され、独立した常に最前面のウィンドウとして動作
/// </summary>
public partial class MusicPlayerWindow : Window
{
    private static readonly string WebView2DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "MultiDesktop", "WebView2Data");

    private static readonly string MobileUserAgent =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8 Pro) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Mobile Safari/537.36";

    // Amazon MusicはChromeデスクトップUAを使用（モバイルSafariは拒否される）
    private static readonly string ChromeDesktopUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private CoreWebView2Environment? _webViewEnv;
    private string _currentService = "";
    private bool _isWebViewReady;
    private MusicServiceSettings _musicSettings;
    private DispatcherTimer? _nowPlayingTimer;

    // Now Playing 情報を外部に通知するイベント
    public event Action<string, string, string>? NowPlayingChanged;
    // パラメータ: service, title, thumbnailUrl

    // サービスURL定義
    private static readonly Dictionary<string, string> ServiceUrls = new()
    {
        ["YouTube"] = "https://m.youtube.com",
        ["Amazon"] = "https://music.amazon.co.jp",
        ["Spotify"] = "https://open.spotify.com"
    };

    public MusicPlayerWindow(MusicServiceSettings musicSettings)
    {
        InitializeComponent();
        _musicSettings = musicSettings;
        UpdateTabVisibility();
        Loaded += MusicPlayerWindow_Loaded;
    }

    private async void MusicPlayerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(WebView2DataDir);
            _webViewEnv = await CoreWebView2Environment.CreateAsync(
                userDataFolder: WebView2DataDir);

            // 最初の有効なサービスを自動選択
            if (_musicSettings.IsYouTubeEnabled)
                await SwitchService("YouTube");
            else if (_musicSettings.IsAmazonMusicEnabled)
                await SwitchService("Amazon");
            else if (_musicSettings.IsSpotifyEnabled)
                await SwitchService("Spotify");

            // Now Playing ポーリング開始
            _nowPlayingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _nowPlayingTimer.Tick += async (_, _) => await PollNowPlaying();
            _nowPlayingTimer.Start();
        }
        catch
        {
            PlaceholderText.Text = "WebView2の初期化に失敗しました";
            PlaceholderSubText.Text = "WebView2ランタイムがインストールされているか確認してください";
        }
    }

    // ─── タブ表示管理 (有効なサービスのみ) ────────────
    private void UpdateTabVisibility()
    {
        YouTubeTab.Visibility = _musicSettings.IsYouTubeEnabled ? Visibility.Visible : Visibility.Collapsed;
        AmazonTab.Visibility = _musicSettings.IsAmazonMusicEnabled ? Visibility.Visible : Visibility.Collapsed;
        SpotifyTab.Visibility = _musicSettings.IsSpotifyEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTabHighlight(string service)
    {
        var inactiveBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        var activeBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, 0x88, 0xBB, 0xFF));

        YouTubeTab.Background = service == "YouTube" ? activeBrush : inactiveBrush;
        AmazonTab.Background = service == "Amazon" ? activeBrush : inactiveBrush;
        SpotifyTab.Background = service == "Spotify" ? activeBrush : inactiveBrush;

        // PIPボタンはYouTubeの時のみ表示
        PipButton.Visibility = service == "YouTube" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ─── サービス切り替え (Lazy Load) ────────────────
    private async Task SwitchService(string service)
    {
        if (_currentService == service && _isWebViewReady)
            return;

        _currentService = service;
        UpdateTabHighlight(service);

        PlaceholderPanel.Visibility = Visibility.Visible;
        PlaceholderText.Text = $"{service} を読み込み中...";
        PlaceholderSubText.Text = "";

        try
        {
            // 前のWebViewをリセット
            if (_isWebViewReady && MusicWebView.CoreWebView2 != null)
            {
                MusicWebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            }

            // WebView2 を初期化（初回のみ、２回目以降はナビゲートのみ）
            if (!_isWebViewReady)
            {
                if (_webViewEnv == null) return;
                await MusicWebView.EnsureCoreWebView2Async(_webViewEnv);

                MusicWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                MusicWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                MusicWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                // 新しいウィンドウを開くリクエストをブロック（アプリ内で開く）
                MusicWebView.CoreWebView2.NewWindowRequested += (s, args) =>
                {
                    args.Handled = true;
                    MusicWebView.CoreWebView2.Navigate(args.Uri);
                };

                _isWebViewReady = true;
            }

            // サービスごとにUserAgentを切り替え
            MusicWebView.CoreWebView2.Settings.UserAgent = service switch
            {
                "Amazon" => ChromeDesktopUserAgent,  // Amazon MusicはChrome UAでないと拒否される
                _ => MobileUserAgent                  // YouTube/SpotifyはモバイルUA
            };

            // ズームレベルを80%に設定
            MusicWebView.ZoomFactor = 0.8;

            MusicWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

            // ナビゲート
            var url = ServiceUrls.GetValueOrDefault(service, ServiceUrls["YouTube"]);
            MusicWebView.CoreWebView2.Navigate(url);
        }
        catch
        {
            PlaceholderText.Text = "読み込みエラー";
            PlaceholderSubText.Text = "再試行してください";
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess) return;

        MusicWebView.Visibility = Visibility.Visible;
        PlaceholderPanel.Visibility = Visibility.Collapsed;

        // サービスごとのCSS/JS注入
        await InjectCustomizations();
    }

    // ─── カスタムCSS/JS注入 ──────────────────────────
    private async Task InjectCustomizations()
    {
        switch (_currentService)
        {
            case "YouTube":
                await InjectYouTubeCustomizations();
                break;
            case "Amazon":
                await InjectAmazonMusicCustomizations();
                break;
            case "Spotify":
                await InjectSpotifyCustomizations();
                break;
        }
    }

    private async Task InjectYouTubeCustomizations()
    {
        if (MusicWebView.CoreWebView2 == null) return;

    // YouTube 広告ブロック: 
    // ※CSSでのオーバーレイ非表示は、YouTubeの内部状態をおかしくし、本編動画の再生を阻害するため全撤去。
    // JSの早送り＋クリックに特化する。
    var script = @"
(function() {
    if (window.__mdAdBlockApplied) return;
    window.__mdAdBlockApplied = true;

    // ─── バックグラウンド再生の維持 (Page Visibility API を上書き) ───
    // YouTubeが「裏に回った」と判定して動画を止めるのを防ぐ
    Object.defineProperty(document, 'hidden', { value: false, writable: false });
    Object.defineProperty(document, 'visibilityState', { value: 'visible', writable: false });
    document.addEventListener('visibilitychange', e => e.stopImmediatePropagation(), true);

    const adBlockStyle = document.createElement('style');
    adBlockStyle.textContent = `
        /* 小画面最適化 (広告のCSS操作は再生を壊すのでしない) */
        html, body { overflow-x: hidden !important; }
        .mobile-topbar-header { position: sticky !important; top: 0; z-index: 9999; }
        .tab-content { padding-top: 0 !important; }
        .ytm-autonav-bar { display: none !important; }
    `;
    document.head.appendChild(adBlockStyle);

    // ─── スキップ＆早送り用のスクリプト ────────────────
    let wasAdShowing = false;

    const handleAds = () => {
        const video = document.querySelector('video');
        const isAdShowing = document.querySelector('.ad-showing') !== null || document.querySelector('.ytp-ad-player-overlay') !== null;
        
        // 【1】スキップボタンがあれば即クリック (テキスト検索も含む)
        const skipBtn = document.querySelector(
            '.ytp-ad-skip-button, .ytp-ad-skip-button-modern, ' +
            '.ytp-skip-ad-button, button.ytp-ad-skip-button-modern, ' +
            '.ytm-skip-ad-button, .ytp-ad-skip-button-container button'
        );
        if (skipBtn) {
            try { skipBtn.click(); } catch(e) {}
        } else {
            // クラス名で見つからない場合、ボタン内のテキストで「スキップ」を探す
            const buttons = document.querySelectorAll('button, div');
            for (let b of buttons) {
                if (b.innerText && b.innerText.includes('スキップ')) {
                    try { b.click(); } catch(e) {}
                    break;
                }
            }
        }

        // 【2】広告が再生中なら、無音にして超倍速(16倍速)で強制スキップ
        if (video && isAdShowing) {
            wasAdShowing = true;
            if (!video.muted) video.muted = true; // 無音化
            if (video.playbackRate !== 16.0) video.playbackRate = 16.0; // 超倍速
        } 
        // 【3】広告が終わったら元に戻す
        else if (video && wasAdShowing && !isAdShowing) {
            wasAdShowing = false;
            video.muted = false;
            video.playbackRate = 1.0;
        }
    };

    const observer = new MutationObserver(() => handleAds());
    observer.observe(document.body, { childList: true, subtree: true });
    setInterval(handleAds, 500); // 0.5秒間隔で監視
})();
";
        await MusicWebView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private async Task InjectAmazonMusicCustomizations()
    {
        if (MusicWebView.CoreWebView2 == null) return;

        var script = @"
(function() {
    if (window.__mdAmazonStyleApplied) return;
    window.__mdAmazonStyleApplied = true;

    const style = document.createElement('style');
    style.textContent = `
        /* 小画面最適化: 不要UI非表示 */
        #navFooter, .nav-footer, footer,
        #rhf, .navLeftContainer,
        .a-carousel-header-row,
        #nav-upnav { display: none !important; }

        /* ヘッダーをコンパクト化 */
        .headerContent { padding: 4px 8px !important; }
        #navbar { min-height: 36px !important; }

        /* コンテンツ領域最大化 */
        body { font-size: 13px !important; }
        .a-container { max-width: 100% !important; padding: 0 4px !important; }
    `;
    document.head.appendChild(style);
})();
";
        await MusicWebView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private async Task InjectSpotifyCustomizations()
    {
        if (MusicWebView.CoreWebView2 == null) return;

        var script = @"
(function() {
    if (window.__mdSpotifyStyleApplied) return;
    window.__mdSpotifyStyleApplied = true;

    const style = document.createElement('style');
    style.textContent = `
        /* 小画面最適化 */
        .Root__globalNav { display: none !important; }
        .Root__top-container { padding-top: 0 !important; }
        .LayoutResizer__resize-bar { display: none !important; }
        .nav-bar { display: none !important; }

        /* コンテンツ領域最大化 */
        body { font-size: 13px !important; overflow-x: hidden !important; }
        .contentSpacing { padding: 8px !important; }
    `;
    document.head.appendChild(style);
})();
";
        await MusicWebView.CoreWebView2.ExecuteScriptAsync(script);
    }

    // ─── Now Playing 情報取得 ────────────────────────
    private async Task PollNowPlaying()
    {
        if (!_isWebViewReady || MusicWebView.CoreWebView2 == null) return;

        try
        {
            var script = _currentService switch
            {
                "YouTube" => @"
(function() {
    const titleEl = document.querySelector('.slim-video-information-title .yt-core-attributed-string,
        .player-controls-content .media-item-info .title,
        .miniplayer-title,
        h3.slim-video-information-title,
        .ytm-slim-video-information-header .slim-video-information-title').replace(/\n/g, '');
    const title = titleEl ? titleEl.innerText.trim() : '';
    const thumb = document.querySelector('video')?.poster
        || document.querySelector('.player-controls-content .media-item-info img')?.src
        || '';
    return JSON.stringify({title: title, thumb: thumb});
})();
".Replace("\r\n", " ").Replace("\n", " "),

                "Amazon" => @"
(function() {
    const musicImg = document.querySelector('music-image');
    if (musicImg) {
        return JSON.stringify({
            title: musicImg.getAttribute('alt') || '',
            thumb: musicImg.getAttribute('src') || ''
        });
    }
    const title = document.querySelector('[class*=""trackTitle""], .trackInfoContainer .trackTitle, .playbackControlsView .trackTitle, .transportControls .track-text .title')?.innerText?.trim() || document.title || '';
    const thumb = document.querySelector('[class*=""albumArt""] img, .playbackControlsView .artwork img, .transportControls .artwork img')?.src || '';
    return JSON.stringify({title: title, thumb: thumb});
})();
",

                "Spotify" => @"
(function() {
    const title = document.querySelector('[data-testid=""context-item-info-title""], [data-testid=""nowplaying-track-link""], .track-info__name a')?.innerText?.trim() || '';
    const thumb = document.querySelector('[data-testid=""cover-art-image""], .now-playing-bar .cover-art img, .cover-art-image')?.src || '';
    return JSON.stringify({title: title, thumb: thumb});
})();
",
                _ => "JSON.stringify({title:'', thumb:''})"
            };

            var result = await MusicWebView.CoreWebView2.ExecuteScriptAsync(script);

            if (result != null && result != "null" && result.Length > 2)
            {
                // WebView2 returns JSON-encoded string, so we need to unescape
                var unescaped = JsonSerializer.Deserialize<string>(result) ?? "";
                if (!string.IsNullOrEmpty(unescaped))
                {
                    var info = JsonSerializer.Deserialize<NowPlayingInfo>(unescaped);
                    if (info != null && !string.IsNullOrEmpty(info.title))
                    {
                        NowPlayingChanged?.Invoke(_currentService, info.title, info.thumb ?? "");
                    }
                }
            }
        }
        catch { /* ポーリング失敗は無視 */ }
    }

    private record NowPlayingInfo(string title, string thumb);

    // ─── タブクリック ────────────────────────────────
    private async void YouTubeTab_Click(object sender, MouseButtonEventArgs e)
        => await SwitchService("YouTube");

    private async void AmazonTab_Click(object sender, MouseButtonEventArgs e)
        => await SwitchService("Amazon");

    private async void SpotifyTab_Click(object sender, MouseButtonEventArgs e)
        => await SwitchService("Spotify");

    // ─── 再生コントロール ────────────────────────────
    private async void PlayPause_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_isWebViewReady || MusicWebView.CoreWebView2 == null) return;

        var script = _currentService switch
        {
            "YouTube" => @"
(function() {
    const v = document.querySelector('video');
    if (v) { v.paused ? v.play() : v.pause(); }
})();
",
            "Amazon" => @"
(function() {
    const btn = document.querySelector('music-button.playButton, [id=""transport""] button.playButton, button[aria-label=""Play""], button[aria-label=""Pause""], button[aria-label=""再生""], button[aria-label=""一時停止""]');
    if (btn) { btn.click(); return; }
    const el = document.querySelector('audio, video');
    if (el) { el.paused ? el.play() : el.pause(); }
})();
",
            "Spotify" => @"
(function() {
    const btn = document.querySelector('button[data-testid=""control-button-playpause""]');
    if (btn) { btn.click(); return; }
    const el = document.querySelector('audio, video');
    if (el) { el.paused ? el.play() : el.pause(); }
})();
",
            _ => @"
(function() {
    const el = document.querySelector('video, audio');
    if (el) { el.paused ? el.play() : el.pause(); }
})();
"
        };

        await MusicWebView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private async void PrevTrack_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_isWebViewReady || MusicWebView.CoreWebView2 == null) return;

        var script = _currentService switch
        {
            "YouTube" => @"
(function() {
    const btn = document.querySelector('button.previous-button, [aria-label=""前の動画""], .previous-button');
    if (btn) { btn.click(); return; }
    const v = document.querySelector('video');
    if (v) v.currentTime = 0;
})();
",
            "Amazon" => @"
(function() {
    const btn = document.querySelector('music-button.previousButton, [id=""transport""] button.previousButton, button[aria-label=""Previous""], button[aria-label=""前へ""]');
    if (btn) { btn.click(); return; }
    const el = document.querySelector('audio, video');
    if (el) el.currentTime = 0;
})();
",
            "Spotify" => @"
(function() {
    const btn = document.querySelector('button[data-testid=""control-button-skip-back""]');
    if (btn) btn.click();
})();
",
            _ => @"
(function() {
    const el = document.querySelector('video, audio');
    if (el) el.currentTime = 0;
})();
"
        };

        await MusicWebView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private async void NextTrack_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_isWebViewReady || MusicWebView.CoreWebView2 == null) return;

        var script = _currentService switch
        {
            "YouTube" => @"
(function() {
    const btn = document.querySelector('button.next-button, [aria-label=""次の動画""], .next-button');
    if (btn) btn.click();
})();
",
            "Amazon" => @"
(function() {
    const btn = document.querySelector('music-button.nextButton, [id=""transport""] button.nextButton, button[aria-label=""Next""], button[aria-label=""次へ""]');
    if (btn) btn.click();
})();
",
            "Spotify" => @"
(function() {
    const btn = document.querySelector('button[data-testid=""control-button-skip-forward""]');
    if (btn) btn.click();
})();
",
            _ => ""
        };

        if (!string.IsNullOrEmpty(script))
            await MusicWebView.CoreWebView2.ExecuteScriptAsync(script);
    }

    // ─── PiP (YouTube専用) ──────────────────────────
    private async void PiP_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_isWebViewReady || MusicWebView.CoreWebView2 == null) return;

        // モバイル版などで PiP API がブロックされている場合に備え、
        // video 요소の disablePictureInPicture を解除してから呼ぶ
        var script = @"
(async () => {
    const video = document.querySelector('video');
    if (video) {
        try {
            video.removeAttribute('disablePictureInPicture');
            if (document.pictureInPictureElement) {
                await document.exitPictureInPicture();
            } else {
                await video.requestPictureInPicture();
            }
        } catch(err) {
            console.error('PiP Error:', err);
        }
    }
})();
";
        await MusicWebView.CoreWebView2.ExecuteScriptAsync(script);
    }

    // ─── ナビゲーション ──────────────────────────────
    private void GoBack_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isWebViewReady && MusicWebView.CoreWebView2?.CanGoBack == true)
            MusicWebView.CoreWebView2.GoBack();
    }

    private void Refresh_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isWebViewReady && MusicWebView.CoreWebView2 != null)
            MusicWebView.CoreWebView2.Reload();
    }

    public event EventHandler? OnPlayerHidden;

    // ─── ウィンドウ管理 ──────────────────────────────
    private void MinimizePlayer_Click(object sender, MouseButtonEventArgs e)
    {
        // 最小化: ウィンドウを非表示にするが WebView2 は動き続ける
        Hide();
        OnPlayerHidden?.Invoke(this, EventArgs.Empty);
    }

    private void ClosePlayer_Click(object sender, MouseButtonEventArgs e)
    {
        // 閉じる: WebView2を停止して閉じる
        _nowPlayingTimer?.Stop();
        NowPlayingChanged?.Invoke("", "", "");
        Close();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // フォーカス失った時に自動的に閉じない（バックグラウンド再生のため）
    }

    /// <summary>
    /// コントロールセンターの指定要素に追従して埋め込み表示する
    /// </summary>
    public void AttachTo(FrameworkElement target)
    {
        if (target == null || !target.IsVisible)
        {
            Hide();
            return;
        }

        var source = PresentationSource.FromVisual(target);
        if (source == null) return;

        // PointToScreen は Popup アニメーション中にズレることがあるため、
        // もう少し安定した座標を取得するように努める
        System.Windows.Point ptTopLeft;
        try
        {
            ptTopLeft = target.PointToScreen(new System.Windows.Point(0, 0));
        }
        catch 
        {
            return; // 描画前で取得できない場合
        }
        
        double dpiX = source.CompositionTarget.TransformToDevice.M11;
        double dpiY = source.CompositionTarget.TransformToDevice.M22;

        Left = ptTopLeft.X / dpiX;
        Top = ptTopLeft.Y / dpiY;
        
        // Popup 内で大きくなりすぎないように調整
        Width = target.ActualWidth;
        Height = target.ActualHeight;

        // UIをコントロールセンタータイルに合わせる（影やボーダーを除去）
        WindowBorder.Effect = null;
        WindowBorder.BorderThickness = new Thickness(0);
        WindowBorder.CornerRadius = new CornerRadius(10);
        
        // ズーム比率が巨大化するのを防ぐ
        if (MusicWebView.CoreWebView2 != null)
        {
            MusicWebView.ZoomFactor = 0.75; // 小さめの枠に合わせてさらに縮小
        }

        if (!IsVisible)
        {
            // Focusを奪わないようにShowする (PopupがStaysOpen="False"のため閉じないようにする)
            ShowActivated = false;
            Show();
        }
        
        Topmost = true; // Popupの上に表示するため
        
        // Activate() を呼ぶとPopupが閉じてしまうため呼ばない
        // Activate();
    }

    /// <summary>
    /// 指定位置にフローティング表示する（旧方式）
    /// </summary>
    public void ShowAt(double left, double top)
    {
        Left = left;
        Top = top;
        Show();
        Activate();
    }

    /// <summary>
    /// 設定を更新する
    /// </summary>
    public void UpdateSettings(MusicServiceSettings settings)
    {
        _musicSettings = settings;
        UpdateTabVisibility();
    }

    protected override void OnClosed(EventArgs e)
    {
        _nowPlayingTimer?.Stop();
        try
        {
            if (_isWebViewReady)
            {
                MusicWebView?.Dispose();
            }
        }
        catch { }
        base.OnClosed(e);
    }
}
