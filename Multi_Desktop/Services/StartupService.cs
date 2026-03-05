using System.Diagnostics;
using System.Reflection;

namespace Multi_Desktop.Services;

/// <summary>
/// Windows Startup (自動起動) 管理サービス
/// 管理者権限アプリのためタスクスケジューラを使用する。
/// （requireAdministrator マニフェストのアプリは HKCU\Run では自動起動しない）
/// </summary>
public static class StartupService
{
    private const string TaskName = "MultiDesktopStartup";

    /// <summary>
    /// 自動起動の登録/解除を設定
    /// </summary>
    /// <param name="enabled">有効にするかどうか</param>
    public static void SetStartup(bool enabled)
    {
        try
        {
            if (enabled)
            {
                RegisterScheduledTask();
            }
            else
            {
                UnregisterScheduledTask();
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"スタートアップ設定に失敗しました:\n{ex.Message}",
                "Multi Desktop",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 現在自動起動が有効かどうかを確認
    /// </summary>
    public static bool IsStartupEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Query /TN \"{TaskName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 実行ファイルのパスを取得する
    /// </summary>
    private static string GetExePath()
    {
        // Environment.ProcessPath → Assembly.Location → AppContext.BaseDirectory の順で試す
        string? path = Environment.ProcessPath;

        if (string.IsNullOrEmpty(path))
        {
            path = Assembly.GetEntryAssembly()?.Location;
        }

        if (string.IsNullOrEmpty(path))
        {
            path = Process.GetCurrentProcess().MainModule?.FileName;
        }

        return path ?? string.Empty;
    }

    /// <summary>
    /// タスクスケジューラにログオン時起動タスクを登録する
    /// </summary>
    private static void RegisterScheduledTask()
    {
        // 既存タスクを一旦削除（更新のため）
        UnregisterScheduledTask();

        string exePath = GetExePath();
        if (string.IsNullOrEmpty(exePath))
        {
            throw new InvalidOperationException("実行ファイルのパスを取得できませんでした。");
        }

        // schtasks コマンドでタスクを作成
        // /SC ONLOGON : ログオン時に実行
        // /RL HIGHEST : 最上位の特権で実行（管理者権限）
        // /F          : 既に存在する場合は上書き
        // /DELAY      : 5秒遅延
        // Note: /TR の引数で exe パスにスペースがある場合を考慮
        var args = $"/Create /TN \"{TaskName}\" /TR \"'{exePath}' --minimized\" /SC ONLOGON /RL HIGHEST /F /DELAY 0000:05";

        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("schtasks.exe を起動できませんでした。");
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"タスク登録に失敗しました (exit code: {process.ExitCode})\n" +
                $"コマンド: schtasks.exe {args}\n" +
                $"出力: {stdout}\n" +
                $"エラー: {stderr}");
        }
    }

    /// <summary>
    /// タスクスケジューラからタスクを削除する
    /// </summary>
    private static void UnregisterScheduledTask()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Delete /TN \"{TaskName}\" /F",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        process?.WaitForExit(5000);
        // 削除失敗（存在しない場合など）は無視
    }
}
