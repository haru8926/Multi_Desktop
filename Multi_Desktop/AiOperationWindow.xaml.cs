using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Multi_Desktop.Models;
using Multi_Desktop.Services;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Multi_Desktop;

/// <summary>
/// AI 操作パネルウィンドウ
///</summary>
public partial class AiOperationWindow : Window
{
    private readonly AiAssistantService _aiService = new();
    private List<AiAction>? _pendingActions;

    /// <summary>パネル非表示時のイベント</summary>
    public event EventHandler? OnPanelHidden;

    public AiOperationWindow(string apiKey)
    {
        InitializeComponent();
        _aiService.SetApiKey(apiKey);
    }



    // ─── ウィンドウ管理 ──────────────────────────────
    private void MinimizePanel_Click(object sender, MouseButtonEventArgs e)
    {
        // 最小化: ウィンドウを非表示にするが AiService は維持（会話履歴も保持）
        Hide();
        OnPanelHidden?.Invoke(this, EventArgs.Empty);
    }

    private void ClosePanel_Click(object sender, MouseButtonEventArgs e)
    {
        // 閉じる: 会話履歴をクリアして閉じる
        _aiService.ClearHistory();
        Close();
    }

    // ─── メッセージ送信 ──────────────────────────────
    private void MessageInput_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(MessageInput.Text))
        {
            SendMessage();
            e.Handled = true;
        }
    }

    private void SendMessage_Click(object sender, MouseButtonEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(MessageInput.Text))
            SendMessage();
    }

    private async void SendMessage()
    {
        var userText = MessageInput.Text.Trim();
        if (string.IsNullOrEmpty(userText)) return;

        MessageInput.Text = "";
        MessageInput.IsEnabled = false;

        // ユーザーメッセージを表示
        AddChatBubble(userText, isUser: true);

        // ローディング表示
        var loadingBubble = AddChatBubble("考え中...", isUser: false, isLoading: true);

        try
        {
            var response = await _aiService.SendMessageAsync(userText);

            // ローディングを除去
            ChatPanel.Children.Remove(loadingBubble);

            // AIレスポンスを表示
            AddChatBubble(response.Said, isUser: false);

            // 操作がある場合は承認バナーを表示
            if (response.Actions.Count > 0)
            {
                if (AreActionsSafe(response.Actions))
                {
                    await ExecuteActionsAsync(response.Actions, showApprovedMessage: false);
                }
                else
                {
                    ShowApprovalBanner(response.Actions);
                }
            }
        }
        catch (Exception ex)
        {
            ChatPanel.Children.Remove(loadingBubble);
            AddChatBubble($"エラー: {ex.Message}", isUser: false);
        }
        finally
        {
            MessageInput.IsEnabled = true;
            MessageInput.Focus();
        }
    }

    // ─── 操作承認 ────────────────────────────────────
    private void ShowApprovalBanner(List<AiAction> actions)
    {
        _pendingActions = actions;

        var details = new System.Text.StringBuilder();
        foreach (var action in actions)
        {
            switch (action.Type)
            {
                case "shell":
                    details.AppendLine($"🖥️ コマンド実行: {action.Command}");
                    break;
                case "list_dir":
                    details.AppendLine($"📁 フォルダ参照: {action.Path}");
                    break;
                case "read_file":
                    details.AppendLine($"📄 ファイル読取: {action.Path}");
                    break;
                default:
                    details.AppendLine($"❓ 不明な操作: {action.Type}");
                    break;
            }
        }

        ApprovalDetails.Text = details.ToString().TrimEnd();
        ApprovalBanner.Visibility = Visibility.Visible;
    }

    private async void ApproveAction_Click(object sender, MouseButtonEventArgs e)
    {
        if (_pendingActions == null) return;

        ApprovalBanner.Visibility = Visibility.Collapsed;
        var actions = _pendingActions;
        _pendingActions = null;

        await ExecuteActionsAsync(actions, showApprovedMessage: true);
    }

    private async Task ExecuteActionsAsync(List<AiAction> actions, bool showApprovedMessage)
    {
        MessageInput.IsEnabled = false;

        if (showApprovedMessage)
        {
            AddChatBubble("✅ 操作を承認しました。実行中...", isUser: false);
        }
        else
        {
            AddChatBubble("⚡ 安全な操作のため自動実行中...", isUser: false);
        }

        var results = new System.Text.StringBuilder();
        foreach (var action in actions)
        {
            try
            {
                switch (action.Type)
                {
                    case "shell":
                        var shellResult = await ExecuteShellAsync(action.Command ?? "");
                        results.AppendLine($"[shell: {action.Command}]\n{shellResult}");
                        break;
                    case "list_dir":
                        var dirResult = ExecuteListDir(action.Path ?? "");
                        results.AppendLine($"[list_dir: {action.Path}]\n{dirResult}");
                        break;
                    case "read_file":
                        var fileResult = ExecuteReadFile(action.Path ?? "");
                        results.AppendLine($"[read_file: {action.Path}]\n{fileResult}");
                        break;
                }
            }
            catch (Exception ex)
            {
                results.AppendLine($"[{action.Type}エラー] {ex.Message}");
            }
        }

        // 実行結果をAIに返して続きを取得
        var loadingBubble = AddChatBubble("結果を分析中...", isUser: false, isLoading: true);

        try
        {
            var response = await _aiService.SendOperationResultAsync(results.ToString());
            ChatPanel.Children.Remove(loadingBubble);
            AddChatBubble(response.Said, isUser: false);

            if (response.Actions.Count > 0)
            {
                if (AreActionsSafe(response.Actions))
                {
                    await ExecuteActionsAsync(response.Actions, showApprovedMessage: false);
                }
                else
                {
                    ShowApprovalBanner(response.Actions);
                }
            }
        }
        catch (Exception ex)
        {
            ChatPanel.Children.Remove(loadingBubble);
            AddChatBubble($"エラー: {ex.Message}", isUser: false);
        }
        finally
        {
            MessageInput.IsEnabled = true;
            MessageInput.Focus();
        }
    }

    private bool AreActionsSafe(List<AiAction> actions)
    {
        return actions.All(IsActionSafe);
    }

    private bool IsActionSafe(AiAction action)
    {
        // フォルダ一覧やファイル読み取りは内容をAIに送信するため安全とみなさない（要承認）
        if (action.Type == "list_dir" || action.Type == "read_file")
            return false;

        if (action.Type == "shell")
        {
            var cmd = action.Command?.ToLowerInvariant() ?? "";
            
            // 危険なコマンドキーワードを定義
            string[] dangerousKeywords = {
                "del", "rm", "rmdir", "remove-item", "erase",
                "curl", "wget", "invoke-webrequest", "iwr",
                "format", "rename", "ren", "move", "net", 
                "copy", "cp", "robocopy", "takeown", "icacls",
                "mklink", "cipher", "diskpart"
            };

            // コマンド文字列内に危険なキーワードが単語として含まれているかチェック
            // 簡易的にスペースで区切って判定
            var tokens = cmd.Split(new[] { ' ', '\t', '\n', '\r', '|', '&', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (dangerousKeywords.Contains(token))
                {
                    return false;
                }
                
                // コマンドのエイリアスやパスの可能性もあるため、部分一致も警戒
                if (token.EndsWith(".exe") || token.EndsWith(".ps1") || token.EndsWith(".bat") || token.EndsWith(".cmd"))
                {
                    if (dangerousKeywords.Any(k => token.Contains(k)))
                        return false;
                }
            }

            return true;
        }

        return false; // 未知の操作は要承認
    }

    private async void RejectAction_Click(object sender, MouseButtonEventArgs e)
    {
        ApprovalBanner.Visibility = Visibility.Collapsed;
        _pendingActions = null;

        AddChatBubble("❌ 操作を拒否しました。", isUser: false);
        MessageInput.IsEnabled = false;

        var loadingBubble = AddChatBubble("代替案を検討中...", isUser: false, isLoading: true);

        try
        {
            var response = await _aiService.SendOperationRejectedAsync();
            ChatPanel.Children.Remove(loadingBubble);
            AddChatBubble(response.Said, isUser: false);

            if (response.Actions.Count > 0)
            {
                ShowApprovalBanner(response.Actions);
            }
        }
        catch (Exception ex)
        {
            ChatPanel.Children.Remove(loadingBubble);
            AddChatBubble($"エラー: {ex.Message}", isUser: false);
        }
        finally
        {
            MessageInput.IsEnabled = true;
            MessageInput.Focus();
        }
    }

    // ─── 操作実行 ────────────────────────────────────
    private async Task<string> ExecuteShellAsync(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return "プロセスの起動に失敗しました";

        var output = (await process.StandardOutput.ReadToEndAsync()).Trim();
        var error = (await process.StandardError.ReadToEndAsync()).Trim();
        await process.WaitForExitAsync();

        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(output))
        {
            sb.AppendLine("[stdout]");
            sb.AppendLine(output);
        }
        if (!string.IsNullOrEmpty(error))
        {
            sb.AppendLine("[stderr]");
            sb.AppendLine(error);
        }
        if (process.ExitCode != 0)
        {
            sb.AppendLine($"[ExitCode: {process.ExitCode}]");
        }

        var result = sb.ToString().Trim();
        
        if (string.IsNullOrEmpty(result))
        {
            result = "(結果: 正常に完了しましたが、コンソール出力はありませんでした)";
        }

        // 出力を制限
        if (result.Length > 2000)
            result = result.Substring(0, 2000) + "\n...(出力が長いため省略)";

        return result;
    }

    private string ExecuteListDir(string path)
    {
        if (!Directory.Exists(path))
            return $"フォルダが見つかりません: {path}";

        var entries = Directory.GetFileSystemEntries(path);
        var sb = new System.Text.StringBuilder();
        foreach (var entry in entries.Take(50))
        {
            var isDir = Directory.Exists(entry);
            var name = Path.GetFileName(entry);
            sb.AppendLine(isDir ? $"[DIR]  {name}" : $"[FILE] {name}");
        }

        if (entries.Length > 50)
            sb.AppendLine($"... 他 {entries.Length - 50} 件");

        return sb.ToString();
    }

    private string ExecuteReadFile(string path)
    {
        if (!File.Exists(path))
            return $"ファイルが見つかりません: {path}";

        var content = File.ReadAllText(path);
        if (content.Length > 3000)
            content = content.Substring(0, 3000) + "\n...(内容が長いため省略)";

        return content;
    }

    // ─── UI ヘルパー ─────────────────────────────────
    private Border AddChatBubble(string text, bool isUser, bool isLoading = false)
    {
        var bubble = new Border
        {
            Background = new SolidColorBrush(isUser
                ? System.Windows.Media.Color.FromArgb(0x33, 0x55, 0xAA, 0xFF)
                : System.Windows.Media.Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = isUser
                ? new Thickness(40, 0, 0, 6)
                : new Thickness(0, 0, 40, 6),
            HorizontalAlignment = isUser
                ? System.Windows.HorizontalAlignment.Right
                : System.Windows.HorizontalAlignment.Left,
            MaxWidth = 280
        };

        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = new SolidColorBrush(isLoading
                ? System.Windows.Media.Color.FromArgb(0x77, 0xFF, 0xFF, 0xFF)
                : System.Windows.Media.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap,
            FontStyle = isLoading ? FontStyles.Italic : FontStyles.Normal
        };

        bubble.Child = tb;
        ChatPanel.Children.Add(bubble);

        // スクロールを最下部に
        ChatScrollViewer.ScrollToEnd();

        return bubble;
    }

    // ─── 外部からの設定更新 ──────────────────────────
    public void UpdateApiKey(string apiKey)
    {
        _aiService.SetApiKey(apiKey);
    }

    /// <summary>
    /// 指定位置にフローティング表示する
    /// </summary>
    public void ShowAt(double left, double top)
    {
        Left = left;
        Top = top;
        Show();
        Activate();
        MessageInput.Focus();
    }
}
