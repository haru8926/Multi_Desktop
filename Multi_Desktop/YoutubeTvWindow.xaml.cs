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
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, null);

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
    }
}