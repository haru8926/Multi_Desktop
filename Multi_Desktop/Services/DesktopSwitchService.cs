using System.Diagnostics;
using System.IO;
using Multi_Desktop.Helpers;
using Multi_Desktop.Models;

namespace Multi_Desktop.Services;

/// <summary>
/// デスクトップ環境の切り替えサービス
/// ジャンクション操作と壁紙変更を非同期で実行する
/// </summary>
public class DesktopSwitchService
{
    private static readonly string DesktopPath =
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    /// <summary>
    /// 起動時にデータフォルダ（C:\Decktop_date\main, sub）を自動作成する
    /// </summary>
    public static void EnsureDataFoldersExist(AppSettings settings)
    {
        if (!Directory.Exists(settings.MainFolderPath))
            Directory.CreateDirectory(settings.MainFolderPath);

        if (!Directory.Exists(settings.SubFolderPath))
            Directory.CreateDirectory(settings.SubFolderPath);
    }

    /// <summary>
    /// 指定モードへデスクトップを切り替える
    /// </summary>
    /// <param name="mode">切り替え先モード</param>
    /// <param name="settings">アプリケーション設定</param>
    /// <returns>切り替え結果メッセージ</returns>
    public async Task<string> SwitchToAsync(DesktopMode mode, AppSettings settings)
    {
        var targetFolder = settings.GetFolderPath(mode);

        // ターゲットフォルダの存在確認
        if (!Directory.Exists(targetFolder))
        {
            return $"エラー: フォルダが見つかりません — {targetFolder}";
        }

        try
        {
            // 1. ジャンクション切り替え（非同期）
            await Task.Run(() => SwitchJunction(targetFolder));

            // 2. 壁紙変更
            var wallpaperPath = settings.GetWallpaperPath(mode);
            if (!string.IsNullOrWhiteSpace(wallpaperPath))
            {
                NativeMethods.SetWallpaper(wallpaperPath);
            }

            // 3. デスクトップ表示更新
            NativeMethods.NotifyDesktopChanged();

            var modeName = mode == DesktopMode.Main ? "Main" : "Sub";
            return $"{modeName} モードに切り替えました";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    /// <summary>
    /// ジャンクションの切り替え処理
    /// </summary>
    private static void SwitchJunction(string targetFolder)
    {
        // 既存のデスクトップリンク/フォルダを削除
        if (Directory.Exists(DesktopPath))
        {
            var dirInfo = new DirectoryInfo(DesktopPath);

            // ジャンクション（リパースポイント）の場合は直接削除
            if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                dirInfo.Delete();
            }
            else
            {
                // 通常フォルダの場合（初回実行時など）
                // 安全のため、中身を保持しつつバックアップ
                var backupPath = DesktopPath + "_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                dirInfo.MoveTo(backupPath);
            }
        }

        // 新しいジャンクションを作成
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{DesktopPath}\" \"{targetFolder}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"ジャンクション作成に失敗: {error}");
        }
    }

    /// <summary>
    /// 現在のデスクトップがどのモードを指しているか判定する
    /// </summary>
    public static DesktopMode? DetectCurrentMode(AppSettings settings)
    {
        var dirInfo = new DirectoryInfo(DesktopPath);

        if (!dirInfo.Exists || !dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return null;

        // リパースポイントのターゲットを取得
        var target = dirInfo.ResolveLinkTarget(false)?.FullName
                     ?? dirInfo.LinkTarget;

        if (target == null) return null;

        var normalizedTarget = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        var normalizedMain = Path.GetFullPath(settings.MainFolderPath).TrimEnd(Path.DirectorySeparatorChar);
        var normalizedSub = Path.GetFullPath(settings.SubFolderPath).TrimEnd(Path.DirectorySeparatorChar);

        if (string.Equals(normalizedTarget, normalizedMain, StringComparison.OrdinalIgnoreCase))
            return DesktopMode.Main;

        if (string.Equals(normalizedTarget, normalizedSub, StringComparison.OrdinalIgnoreCase))
            return DesktopMode.Sub;

        return null;
    }
}
