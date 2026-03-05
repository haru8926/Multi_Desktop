using System.IO;
using System.Text.Json;

namespace Multi_Desktop.Services;

/// <summary>
/// アプリケーション設定の読み書きサービス
/// </summary>
public class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiDesktop");

    private static readonly string SettingsFilePath =
        Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 設定ファイルを読み込む。存在しなければデフォルト値を返す。
    /// </summary>
    public async Task<Models.AppSettings> LoadAsync()
    {
        if (!File.Exists(SettingsFilePath))
            return new Models.AppSettings();

        try
        {
            var json = await File.ReadAllTextAsync(SettingsFilePath);
            return JsonSerializer.Deserialize<Models.AppSettings>(json, JsonOptions)
                   ?? new Models.AppSettings();
        }
        catch
        {
            return new Models.AppSettings();
        }
    }

    /// <summary>
    /// 設定ファイルに保存する
    /// </summary>
    public async Task SaveAsync(Models.AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(SettingsFilePath, json);
    }
}
