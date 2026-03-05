namespace Multi_Desktop.Models;

/// <summary>
/// 音楽ストリーミングサービスの設定モデル
/// </summary>
public class MusicServiceSettings
{
    /// <summary>YouTube の有効/無効</summary>
    public bool IsYouTubeEnabled { get; set; }

    /// <summary>Amazon Music の有効/無効</summary>
    public bool IsAmazonMusicEnabled { get; set; }

    /// <summary>Spotify の有効/無効</summary>
    public bool IsSpotifyEnabled { get; set; }
}
