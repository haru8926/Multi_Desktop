using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace Multi_Desktop
{
    public static class YoutubeTvUdpServer
    {
        private static UdpClient? _udpClient;
        private static CancellationTokenSource? _cts;

        // ★ 短時間入力の重複排除用変数
        private static string _lastCommand = "";
        private static DateTime _lastTime = DateTime.MinValue;

        public static void Start(WebView2 webView)
        {
            Stop();
            _cts = new CancellationTokenSource();
            Task.Run(() => ListenAsync(webView, _cts.Token));
        }

        public static void Stop()
        {
            _cts?.Cancel();
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient = null;
            }
            DisposeNotifyIcon();
        }

        private static async Task ListenAsync(WebView2 webView, CancellationToken token)
        {
            try
            {
                _udpClient = new UdpClient(4242);

                while (!token.IsCancellationRequested)
                {
                    var result = await _udpClient.ReceiveAsync();
                    var message = Encoding.UTF8.GetString(result.Buffer);
                    var senderEndpoint = result.RemoteEndPoint;

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        HandleCommand(webView, message, senderEndpoint);
                    });
                }
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"UDP Listener Error: {ex.Message}");
            }
        }

        // ★ Windows通知（バルーン通知）を表示
        private static System.Windows.Forms.NotifyIcon? _notifyIcon;

        private static void ShowWindowsNotification(string title, string message, System.Windows.Forms.ToolTipIcon icon = System.Windows.Forms.ToolTipIcon.Info)
        {
            if (_notifyIcon == null)
            {
                _notifyIcon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = System.Drawing.SystemIcons.Application,
                    Visible = true
                };
            }
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(5000);
        }

        private static void DisposeNotifyIcon()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }

        private static async void HandleCommand(WebView2 webView, string command, IPEndPoint senderEndpoint)
        {
            if (webView?.CoreWebView2 == null) return;

            // ★ 150ミリ秒以内の連続した同じコマンドは1回として扱う（Flutter版と同一ロジック）
            var now = DateTime.Now;
            if (_lastCommand == command && (now - _lastTime).TotalMilliseconds < 150)
            {
                return;
            }
            _lastCommand = command;
            _lastTime = now;

            Debug.WriteLine($"UDP Command Received: {command}");

            // ★ 公式Youtubeアプリ連携ボタン (TVコード取得) の処理
            if (command == "GET_TV_CODE")
            {
                // Windows通知を表示：TVコード取得開始
                ShowWindowsNotification(
                    "📺 YouTube TV 連携",
                    "設定 → TVコードでリンクを30秒以内に開いてください\nTVコードを取得中...",
                    System.Windows.Forms.ToolTipIcon.Info);

                // ペアリング設定画面へ遷移
                await webView.ExecuteScriptAsync("window.location.hash = '#/settings/pairing';");

                bool found = false;

                // TVコードが表示されるまでポーリング待機 (最大30秒)
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(1000);

                    // DOMからTVコードを抽出
                    string result = await webView.ExecuteScriptAsync("document.querySelector('yt-formatted-string.XGffTd.ZtI8zc')?.innerText || ''");
                    result = result.Trim('"');

                    if (!string.IsNullOrEmpty(result) && result != "null")
                    {
                        // 数字だけを抜き出して送信
                        var cleanCode = Regex.Replace(result, "[^0-9]", "");
                        var replyBytes = Encoding.UTF8.GetBytes($"TVCODE:{cleanCode}");

                        if (_udpClient != null)
                        {
                            // ★ スマホ側はポート4242で受信待ちしているため、返信先はsenderのIPアドレス＋ポート4242
                            var replyEndpoint = new IPEndPoint(senderEndpoint.Address, 4242);
                            await _udpClient.SendAsync(replyBytes, replyBytes.Length, replyEndpoint);
                        }

                        // Windows通知：TVコード取得成功
                        ShowWindowsNotification(
                            "✅ TVコード送信完了",
                            $"TVコード「{cleanCode}」をスマホに送信しました！\nスマホのYouTubeアプリでペアリングしてください",
                            System.Windows.Forms.ToolTipIcon.Info);

                        Debug.WriteLine($"TV Code sent: {cleanCode}");
                        found = true;
                        break;
                    }
                }

                // TVコードが見つからなかった場合のエラー通知
                if (!found)
                {
                    ShowWindowsNotification(
                        "❌ TVコード取得失敗",
                        "TVコードを取得できませんでした。\nもう一度お試しください。",
                        System.Windows.Forms.ToolTipIcon.Warning);
                }
            }
            else if (command.StartsWith("WM:"))
            {
                // ★ WM:コマンドは別アプリ(Flutter版)向けなので無視
                Debug.WriteLine($"WM command ignored (not applicable): {command}");
            }
            else if (command.StartsWith("TYPE:"))
            {
                // テキスト入力コマンド：スマホから送られた文字列をWebViewに入力
                string text = command.Substring(5);
                if (!string.IsNullOrEmpty(text))
                {
                    // JavaScript経由でテキストを入力（エスケープ処理を含む）
                    string escapedText = text.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "");
                    string script = $@"
                        (function() {{
                            const target = document.activeElement;
                            if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable)) {{
                                // 入力フィールドにフォーカスがある場合は直接値を設定
                                const text = '{escapedText}';
                                target.value = (target.value || '') + text;
                                target.dispatchEvent(new Event('input', {{ bubbles: true }}));
                                target.dispatchEvent(new Event('change', {{ bubbles: true }}));
                            }} else {{
                                // フォーカスが入力フィールドにない場合は1文字ずつキーイベントを送信
                                const text = '{escapedText}';
                                for (let i = 0; i < text.length; i++) {{
                                    const char = text[i];
                                    const opts = {{
                                        key: char,
                                        keyCode: char.charCodeAt(0),
                                        which: char.charCodeAt(0),
                                        bubbles: true,
                                        cancelable: true,
                                        view: window
                                    }};
                                    const t = document.activeElement || document.body;
                                    t.dispatchEvent(new KeyboardEvent('keydown', opts));
                                    t.dispatchEvent(new KeyboardEvent('keypress', opts));
                                    t.dispatchEvent(new KeyboardEvent('keyup', opts));
                                }}
                            }}
                        }})();
                    ";
                    await webView.ExecuteScriptAsync(script);
                    Debug.WriteLine($"Text typed: {text}");
                }
            }
            else
            {
                SendKeyToWebView(webView, command);
            }
        }

        private static void SendKeyToWebView(WebView2 webView, string key)
        {
            int keyCode = key switch
            {
                "ArrowUp" => 38,
                "ArrowDown" => 40,
                "ArrowLeft" => 37,
                "ArrowRight" => 39,
                "Enter" => 13,
                "Escape" => 27,
                _ => 0
            };

            if (keyCode == 0)
            {
                Debug.WriteLine($"Unknown key command ignored: {key}");
                return;
            }

            string script = $@"
                (function() {{
                    const target = document.activeElement || document.body;
                    const opts = {{ 
                        key: '{key}', 
                        keyCode: {keyCode}, 
                        which: {keyCode}, 
                        bubbles: true, 
                        cancelable: true,
                        view: window
                    }};
                    target.dispatchEvent(new KeyboardEvent('keydown', opts));
                    setTimeout(() => target.dispatchEvent(new KeyboardEvent('keyup', opts)), 10);
                }})();
            ";

            webView.ExecuteScriptAsync(script);
        }
    }
}