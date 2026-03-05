using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Multi_Desktop.Helpers;

/// <summary>
/// Windows通知を読み取るヘルパー
/// WinRT UserNotificationListener API を使用
/// </summary>
internal static class NotificationHelper
{
    /// <summary>アクセス許可のキャッシュ（一度許可されたら再チェック不要）</summary>
    private static bool? _accessGranted;

    /// <summary>通知アクセスが許可されているか確認・リクエスト</summary>
    public static async Task<bool> RequestAccessAsync()
    {
        try
        {
            var listener = UserNotificationListener.Current;
            var access = await listener.RequestAccessAsync();
            _accessGranted = access == UserNotificationListenerAccessStatus.Allowed;
            return _accessGranted.Value;
        }
        catch
        {
            _accessGranted = false;
            return false;
        }
    }

    /// <summary>現在の通知一覧を取得</summary>
    public static async Task<List<NotificationInfo>> GetNotificationsAsync()
    {
        var result = new List<NotificationInfo>();

        try
        {
            var listener = UserNotificationListener.Current;

            // アクセス許可はキャッシュを使い、未取得の場合のみリクエスト
            if (_accessGranted == null)
            {
                var access = await listener.RequestAccessAsync();
                _accessGranted = access == UserNotificationListenerAccessStatus.Allowed;
            }

            if (_accessGranted != true)
                return result;

            var notifications = await listener.GetNotificationsAsync(
                NotificationKinds.Toast);

            foreach (var notification in notifications)
            {
                try
                {
                    var binding = notification.Notification?.Visual?.GetBinding(
                        KnownNotificationBindings.ToastGeneric);

                    if (binding == null) continue;

                    var texts = binding.GetTextElements();
                    string title = "";
                    string body = "";

                    int i = 0;
                    foreach (var text in texts)
                    {
                        if (i == 0) title = text.Text ?? "";
                        else if (i == 1) body = text.Text ?? "";
                        i++;
                    }

                    if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
                        continue;

                    result.Add(new NotificationInfo
                    {
                        Id = notification.Id,
                        AppName = notification.AppInfo?.DisplayInfo?.DisplayName ?? "",
                        AppUserModelId = notification.AppInfo?.AppUserModelId ?? "",
                        Title = title,
                        Body = body,
                        Timestamp = notification.CreationTime.LocalDateTime
                    });
                }
                catch { continue; }
            }
        }
        catch { }

        // 新しい順に並べ替え、最大10件
        result.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        if (result.Count > 10)
            result = result.GetRange(0, 10);

        return result;
    }

    /// <summary>通知を既読にする</summary>
    public static void DismissNotification(uint id)
    {
        try
        {
            var listener = UserNotificationListener.Current;
            listener.RemoveNotification(id);
        }
        catch { }
    }

    /// <summary>すべての通知を既読(クリア)にする</summary>
    public static void ClearAllNotifications()
    {
        try
        {
            var listener = UserNotificationListener.Current;
            listener.ClearNotifications();
        }
        catch { }
    }
}

/// <summary>通知情報</summary>
internal class NotificationInfo
{
    public uint Id { get; set; }
    public string AppName { get; set; } = "";
    public string AppUserModelId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
