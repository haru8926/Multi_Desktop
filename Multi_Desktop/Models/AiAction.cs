using System.Text.Json.Serialization;

namespace Multi_Desktop.Models;

/// <summary>
/// AIから返される操作コマンドのモデル
/// </summary>
public class AiAction
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

/// <summary>
/// AIのレスポンス全体
/// </summary>
public class AiResponse
{
    [JsonPropertyName("said")]
    public string Said { get; set; } = string.Empty;

    [JsonPropertyName("actions")]
    public List<AiAction> Actions { get; set; } = new();
}
