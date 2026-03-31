using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Multi_Desktop
{
    public partial class YoutubeTvWindow : Window
    {
        public YoutubeTvWindow()
        {
            InitializeComponent();
            InitializeWebView();
        }

        private async void InitializeWebView()
        {
            var appName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName, "YoutubeTVProfile");

            // ★ ここを修正！WebView2のバックグラウンド省エネ機能を無効化する引数を追加
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--disable-background-timer-throttling --disable-backgrounding-occluded-windows --disable-renderer-backgrounding"
            };

            // options を渡して初期化
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            await webView.EnsureCoreWebView2Async(env);

            webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Web0S; SmartTV) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.5283.0 Safari/537.36 SmartTV";

            // ★ ここからJavaScriptインジェクションの追加 ★
            // Webページが読み込まれる直前に、閉じるボタンをDOMに強制追加するスクリプト
            string script = @"
                const btn = document.createElement('button');
                btn.innerHTML = '✕';
                btn.style.position = 'fixed';
                btn.style.top = '20px';
                btn.style.right = '20px';
                btn.style.width = '50px';
                btn.style.height = '50px';
                btn.style.fontSize = '24px';
                btn.style.color = 'white';
                btn.style.backgroundColor = 'rgba(0,0,0,0.5)';
                btn.style.border = 'none';
                btn.style.borderRadius = '25px';
                btn.style.zIndex = '2147483647'; // 確実に最前面へ
                btn.style.cursor = 'pointer';
                // クリックされたらC#側へ 'close_app' というメッセージを送信
                btn.onclick = () => window.chrome.webview.postMessage('close_app');
                document.documentElement.appendChild(btn);
            ";
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);

            // C#側でJavaScriptからのメッセージを受信してウィンドウを閉じる
            webView.CoreWebView2.WebMessageReceived += (sender, args) =>
            {
                if (args.TryGetWebMessageAsString() == "close_app")
                {
                    YoutubeTvWindowManager.CloseAllWindows();
                }
            };
            // ★ ここまで ★

            webView.CoreWebView2.Navigate("https://www.youtube.com/tv");
        }
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        public async void SetBackgroundMode(bool isBackground, bool useBlur = true)
        {
            if (webView?.CoreWebView2 == null) return;

            // Win32 APIでクリック透過スタイルを付与/解除
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                if (isBackground)
                {
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
                }
                else
                {
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                }
            }

            // 念のためWPF側も設定
            this.IsHitTestVisible = !isBackground;

            string script;
            if (!isBackground)
            {
                script = "document.body.style.transition = 'filter 0.5s'; document.body.style.filter = 'none';";
            }
            else if (useBlur)
            {
                script = "document.body.style.transition = 'filter 0.5s'; document.body.style.filter = 'blur(20px) brightness(0.6)';";
            }
            else
            {
                script = "document.body.style.transition = 'filter 0.5s'; document.body.style.filter = 'brightness(0.7)';";
            }

            await webView.CoreWebView2.ExecuteScriptAsync(script);
        }
        /// <summary>
        /// WebView2リソースを確実に解放する。ウィンドウを閉じる前に呼び出すこと。
        /// </summary>
        public void DisposeWebView()
        {
            try
            {
                if (webView != null)
                {
                    if (webView.CoreWebView2 != null)
                    {
                        webView.CoreWebView2.Navigate("about:blank");
                    }
                    webView.Dispose();
                    webView = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DisposeWebView failed: {ex.Message}");
            }
        }
    }
}