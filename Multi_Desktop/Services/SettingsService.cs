using System.IO;
using System.Security.Cryptography;
using System.Text;
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
            var settings = JsonSerializer.Deserialize<Models.AppSettings>(json, JsonOptions)
                           ?? new Models.AppSettings();

            // Gemini API キーを復号化（暗号化されている場合のみ）
            if (!string.IsNullOrEmpty(settings.GeminiApiKey))
            {
                settings.GeminiApiKey = DecryptString(settings.GeminiApiKey);
            }

            return settings;
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

        // オリジナルのキーを保持
        var originalKey = settings.GeminiApiKey;
        try
        {
            // 保存前に一時的に暗号化
            if (!string.IsNullOrEmpty(settings.GeminiApiKey))
            {
                settings.GeminiApiKey = EncryptString(settings.GeminiApiKey);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempPath = SettingsFilePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, SettingsFilePath, overwrite: true);
        }
        finally
        {
            // メモリ上の設定が暗号化されたままにならないよう元に戻す
            settings.GeminiApiKey = originalKey;
        }
    }

    private string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;

        try
        {
            var data = Encoding.UTF8.GetBytes(plainText);
            var encryptedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedData);
        }
        catch
        {
            return plainText;
        }
    }

    private string DecryptString(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return string.Empty;

        try
        {
            var data = Convert.FromBase64String(encryptedText);
            var decodedData = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decodedData);
        }
        catch
        {
            // 復号に失敗した場合はそのまま返す (古いプレーンテキスト版との互換性のため)
            return encryptedText;
        }
    }
}
