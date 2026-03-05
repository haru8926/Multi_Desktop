namespace Multi_Desktop.Models;

/// <summary>
/// アプリケーション設定モデル
/// </summary>
public class AppSettings
{
    /// <summary>現在のデスクトップモード</summary>
    public DesktopMode CurrentMode { get; set; } = DesktopMode.Main;

    /// <summary>Windows 起動時に自動起動するかどうか</summary>
    public bool IsStartupEnabled { get; set; } = false;

    /// <summary>Main モードの元データフォルダパス</summary>
    public string MainFolderPath { get; set; } = @"C:\Decktop_date\main";

    /// <summary>Sub モードの元データフォルダパス</summary>
    public string SubFolderPath { get; set; } = @"C:\Decktop_date\sub";

    /// <summary>Main モードの壁紙画像パス</summary>
    public string MainWallpaperPath { get; set; } = string.Empty;

    /// <summary>Sub モードの壁紙画像パス</summary>
    public string SubWallpaperPath { get; set; } = string.Empty;

    /// <summary>Main モードのタスクバー設定</summary>
    public TaskbarSettings MainTaskbarSettings { get; set; } = new();

    /// <summary>Sub モードのタスクバー設定</summary>
    public TaskbarSettings SubTaskbarSettings { get; set; } = new();

    /// <summary>音楽ストリーミングサービス設定</summary>
    public MusicServiceSettings MusicSettings { get; set; } = new();

    /// <summary>指定モードのタスクバー設定を取得</summary>
    public TaskbarSettings GetTaskbarSettings(DesktopMode mode) => mode switch
    {
        DesktopMode.Main => MainTaskbarSettings,
        DesktopMode.Sub => SubTaskbarSettings,
        _ => MainTaskbarSettings
    };

    /// <summary>指定モードのフォルダパスを取得</summary>
    public string GetFolderPath(DesktopMode mode) => mode switch
    {
        DesktopMode.Main => MainFolderPath,
        DesktopMode.Sub => SubFolderPath,
        _ => MainFolderPath
    };

    /// <summary>指定モードの壁紙パスを取得</summary>
    public string GetWallpaperPath(DesktopMode mode) => mode switch
    {
        DesktopMode.Main => MainWallpaperPath,
        DesktopMode.Sub => SubWallpaperPath,
        _ => MainWallpaperPath
    };

    /// <summary>反対のモードを取得</summary>
    public static DesktopMode ToggleMode(DesktopMode current) =>
        current == DesktopMode.Main ? DesktopMode.Sub : DesktopMode.Main;
}
