using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Multi_Desktop.Models;

namespace Multi_Desktop.Services;

/// <summary>
/// Gemini API と通信するAIアシスタントサービス
/// 会話履歴をセッション中維持し、操作コマンドのJSON解析を行う
/// </summary>
public class AiAssistantService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private readonly List<ChatMessage> _history = new();
    private string _apiKey = string.Empty;

    private string SystemInstruction => $@"あなたはWindows PCの操作を支援するAIアシスタント「Quick AI」です。
現在のユーザー名: {Environment.UserName}
ユーザープロファイルパス: {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}
ダウンロードフォルダ: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")}

ユーザーの要求に応じて、必ず以下のJSON形式""のみ""で応答してください。マークダウンのコードブロックで囲まないでください。純粋なJSONだけを返してください。

{{""said"":""ユーザーへの応答メッセージ"", ""actions"":[]}}

操作が必要な場合は actions 配列に以下の形式でコマンドを追加してください：
- シェルコマンド実行: {{""type"":""shell"",""command"":""実行するコマンド""}}
- フォルダ内容取得: {{""type"":""list_dir"",""path"":""フォルダパス""}}
- ファイル読み取り: {{""type"":""read_file"",""path"":""ファイルパス""}}
- QuickAI関連アプリ操作: {{""type"":""app_action"",""command"":""実行するアプリ内コマンド""}}
  ※利用可能なアプリ内コマンド（commandの値は以下のいずれかを指定）:
    - switch_main : メインモードに切り替え
    - switch_sub : サブモードに切り替え
    - music_play_pause : 音楽の再生/一時停止
    - music_next : 次の曲へスキップ
    - music_prev : 前の曲へ戻る

【重要な指示】
1. 目的のファイルが見つからない場合、ユーザーに尋ねる前に、PowerShellコマンドを使って柔軟な検索を試みてください。（例: `Get-ChildItem -Path 'パス' -Recurse -Filter '*キーワード*' -ErrorAction SilentlyContinue` や、新しい順でのソートなど）
2. 音楽や動画を探すよう指示された場合、名前の一部だけでも検索し、英語名などの別名や、最近ダウンロードされたもの（`Sort-Object LastWriteTime -Descending`）から推測して見つけ出してください。
3. `list_dir` は先頭50件しか取得できないため、多くのファイルがある場所での特定には `shell` を使ったフィルタリング検索を積極的に活用してください。

操作が不要な場合は actions を空配列にしてください。
said には必ずユーザーへの説明メッセージを含めてください。
ユーザーの操作を承認する必要がある場合は、said でその旨を説明してください。
情報が足りない場合でも、まずは可能な限り自己解決のための探索アクションを実行してください。";

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey;
    }

    /// <summary>
    /// 会話履歴をクリアする
    /// </summary>
    public void ClearHistory()
    {
        _history.Clear();
    }

    /// <summary>
    /// ユーザーメッセージを送信してAIレスポンスを取得する
    /// </summary>
    public async Task<AiResponse> SendMessageAsync(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return new AiResponse
            {
                Said = "Gemini API キーが設定されていません。設定画面でAPIキーを入力してください。",
                Actions = new()
            };
        }

        // ユーザーメッセージを履歴に追加
        _history.Add(new ChatMessage { Role = "user", Text = userMessage });

        try
        {
            var response = await CallGeminiApiAsync();
            
            // AIレスポンスを履歴に追加
            _history.Add(new ChatMessage { Role = "model", Text = response.Said });

            return response;
        }
        catch (Exception ex)
        {
            var errorResponse = new AiResponse
            {
                Said = $"APIエラー: {ex.Message}",
                Actions = new()
            };
            return errorResponse;
        }
    }

    /// <summary>
    /// 操作結果をAIに返送して続きを取得する
    /// </summary>
    public async Task<AiResponse> SendOperationResultAsync(string result)
    {
        return await SendMessageAsync($"[操作結果]\n{result}");
    }

    /// <summary>
    /// 操作拒否をAIに通知して続きを取得する
    /// </summary>
    public async Task<AiResponse> SendOperationRejectedAsync()
    {
        return await SendMessageAsync("[システム通知] ユーザーが操作を拒否しました。代替案を提示してください。");
    }

    private async Task<AiResponse> CallGeminiApiAsync()
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite-preview:generateContent?key={_apiKey}";

        // 会話履歴からcontentsを構築
        var contents = new List<object>();
        foreach (var msg in _history)
        {
            contents.Add(new
            {
                role = msg.Role,
                parts = new[] { new { text = msg.Text } }
            });
        }

        var requestBody = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = SystemInstruction } }
            },
            contents = contents,
            generationConfig = new
            {
                temperature = 0.7,
                maxOutputTokens = 2048
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var httpResponse = await _httpClient.PostAsync(url, content);
        var responseText = await httpResponse.Content.ReadAsStringAsync();

        if (!httpResponse.IsSuccessStatusCode)
        {
            throw new Exception($"API応答エラー ({(int)httpResponse.StatusCode}): {responseText}");
        }

        // Gemini APIレスポンスからテキストを抽出
        var geminiResponse = JsonSerializer.Deserialize<JsonElement>(responseText);
        var aiText = geminiResponse
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "";

        // マークダウンのコードブロックが含まれている場合は除去
        aiText = aiText.Trim();
        if (aiText.StartsWith("```json"))
            aiText = aiText.Substring(7);
        else if (aiText.StartsWith("```"))
            aiText = aiText.Substring(3);
        if (aiText.EndsWith("```"))
            aiText = aiText.Substring(0, aiText.Length - 3);
        aiText = aiText.Trim();

        // JSONとしてパース
        try
        {
            var aiResponse = JsonSerializer.Deserialize<AiResponse>(aiText);
            return aiResponse ?? new AiResponse { Said = aiText, Actions = new() };
        }
        catch
        {
            // JSONパースに失敗した場合はテキストとして返す
            return new AiResponse { Said = aiText, Actions = new() };
        }
    }

    private class ChatMessage
    {
        public string Role { get; set; } = "user";
        public string Text { get; set; } = string.Empty;
    }
}
