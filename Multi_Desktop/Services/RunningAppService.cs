using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Multi_Desktop.Helpers;
using Multi_Desktop.Models;

namespace Multi_Desktop.Services;

/// <summary>
/// 実行中のアプリケーション情報を列挙・管理するサービス
/// アイコンキャッシュと差分更新で効率化
/// </summary>
public class RunningAppService : IDisposable
{
    private readonly DispatcherTimer _refreshTimer;
    private readonly List<string> _pinnedApps;

    /// <summary>EXEパスをキーにしたアイコンキャッシュ（Freeze済みBitmapSource）</summary>
    private static readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>前回のDockアイテムのスナップショット（差分検出用）</summary>
    private List<(string exePath, bool isRunning, bool isPinned, IntPtr handle, int windowCount)> _lastSnapshot = new();

    /// <summary>Dock に表示するアプリ一覧（ピン留め + 実行中）</summary>
    public ObservableCollection<DockAppItem> DockItems { get; } = new();

    /// <summary>アプリ一覧が更新された時に発火</summary>
    public event EventHandler? ItemsUpdated;

    /// <summary>Dock の完全リロードが必要な時（ピン留め変更など）</summary>
    public event EventHandler? DockReloadRequested;

    public RunningAppService(List<string> pinnedApps)
    {
        _pinnedApps = pinnedApps;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _refreshTimer.Tick += (_, _) => Refresh();
    }

    /// <summary>監視を開始する</summary>
    public void Start()
    {
        Refresh();
        _refreshTimer.Start();
    }

    /// <summary>監視を停止する</summary>
    public void Stop()
    {
        _refreshTimer.Stop();
    }

    /// <summary>ピン留めアプリの一覧を更新し、完全リロードを要求する</summary>
    public void UpdatePinnedApps(List<string> pinnedApps)
    {
        _pinnedApps.Clear();
        _pinnedApps.AddRange(pinnedApps);
        FullReload();
    }

    /// <summary>完全リロード（差分比較をスキップして強制的にUIを再構築）</summary>
    public void FullReload()
    {
        _lastSnapshot.Clear(); // 差分比較をリセット
        Refresh();
        DockReloadRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>アプリ一覧を最新に更新する（差分がない場合はスキップ）</summary>
    public async void Refresh()
    {
        // 1. ピン留めされたリストのコピーを作成（別スレッドで安全に反復可能にする）
        var pinnedAppsList = _pinnedApps.ToList();

        // 別スレッドで重いウィンドウ列挙とアイコン取得を実行
        var newItems = await Task.Run(() =>
        {
            var runningWindows = GetVisibleWindows();
            var items = new List<DockAppItem>();

            // 1. ピン留めされたアプリを先に追加
            foreach (var pinPath in pinnedAppsList)
            {
                if (string.IsNullOrWhiteSpace(pinPath)) continue;

                var running = runningWindows.FirstOrDefault(w =>
                    string.Equals(w.ExePath, pinPath, StringComparison.OrdinalIgnoreCase));

                if (running != null)
                {
                    running.IsPinned = true;
                    items.Add(running);
                    runningWindows.Remove(running);
                }
                else
                {
                    // ピン留めされているが未起動
                    // GetCachedIcon もこのTask内で実行されるため同期問題なし
                    items.Add(new DockAppItem
                    {
                        Name = Path.GetFileNameWithoutExtension(pinPath),
                        ExePath = pinPath,
                        Icon = GetCachedIcon(pinPath),
                        IsRunning = false,
                        IsPinned = true
                    });
                }
            }

            // 2. 実行中だがピン留めされていないアプリを追加
            foreach (var item in runningWindows)
            {
                items.Add(item);
            }
            
            return items;
        });

        // 3. 差分検出: 変更がなければ UI更新をスキップ
        var newSnapshot = newItems.Select(i => (i.ExePath, i.IsRunning, i.IsPinned, i.WindowHandle, i.Windows.Count)).ToList();
        if (IsSnapshotEqual(newSnapshot, _lastSnapshot))
        {
            return; // 変更なし → UIにタッチしない
        }
        _lastSnapshot = newSnapshot;

        // 4. ObservableCollection の更新 (メインスレッドへ)
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            DockItems.Clear();
            foreach (var item in newItems)
                DockItems.Add(item);
        });

        ItemsUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>スナップショットが等しいか判定</summary>
    private static bool IsSnapshotEqual(
        List<(string exePath, bool isRunning, bool isPinned, IntPtr handle, int windowCount)> a,
        List<(string exePath, bool isRunning, bool isPinned, IntPtr handle, int windowCount)> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].exePath, b[i].exePath, StringComparison.OrdinalIgnoreCase)
                || a[i].isRunning != b[i].isRunning
                || a[i].isPinned != b[i].isPinned
                || a[i].handle != b[i].handle
                || a[i].windowCount != b[i].windowCount)
                return false;
        }
        return true;
    }

    /// <summary>表示可能なウィンドウ一覧を取得</summary>
    public static List<DockAppItem> GetVisibleWindows()
    {
        var itemsMap = new Dictionary<string, DockAppItem>(StringComparer.OrdinalIgnoreCase);

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsAltTabWindow(hWnd))
                return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);

            Process? proc = null;
            try
            {
                proc = Process.GetProcessById((int)pid);
                var exePath = proc.MainModule?.FileName ?? string.Empty;
                var procName = proc.ProcessName;

                // 自分自身は除外
                if (procName.Equals("Multi_Desktop", StringComparison.OrdinalIgnoreCase))
                    return true;

                // ウィンドウタイトルを取得
                var titleLen = NativeMethods.GetWindowTextLength(hWnd);
                var sb = new StringBuilder(titleLen + 1);
                NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
                var title = sb.ToString();

                if (string.IsNullOrWhiteSpace(title))
                    return true;

                string groupKey = !string.IsNullOrEmpty(exePath) ? exePath : procName;

                if (itemsMap.TryGetValue(groupKey, out var existingItem))
                {
                    existingItem.Windows.Add(new WindowInfo { Handle = hWnd, Title = title });
                    return true;
                }

                // アイコン取得（キャッシュ優先）
                ImageSource? icon = null;
                try
                {
                    var iconHandle = NativeMethods.GetWindowIconHandle(hWnd);
                    if (iconHandle != IntPtr.Zero)
                    {
                        // ウィンドウハンドルから取得したアイコンはキャッシュにも保存
                        if (!string.IsNullOrEmpty(exePath) && !_iconCache.ContainsKey(exePath))
                        {
                            var bmp = Imaging.CreateBitmapSourceFromHIcon(
                                iconHandle, Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            bmp.Freeze();
                            _iconCache[exePath] = bmp;
                        }
                        icon = !string.IsNullOrEmpty(exePath) ? _iconCache.GetValueOrDefault(exePath) : null;
                        if (icon == null)
                        {
                            icon = Imaging.CreateBitmapSourceFromHIcon(
                                iconHandle, Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            ((BitmapSource)icon).Freeze();
                        }
                    }
                    else if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    {
                        icon = GetCachedIcon(exePath);
                    }
                }
                catch { /* アイコン取得失敗は無視 */ }

                var newItem = new DockAppItem
                {
                    Name = title,
                    ExePath = exePath,
                    Icon = icon,
                    IsRunning = true,
                    IsPinned = false,
                    WindowHandle = hWnd
                };
                newItem.Windows.Add(new WindowInfo { Handle = hWnd, Title = title });
                itemsMap[groupKey] = newItem;
            }
            catch { /* プロセス情報取得失敗は無視 */ }
            finally
            {
                proc?.Dispose(); // Process オブジェクトを確実に解放
            }

            return true;
        }, IntPtr.Zero);

        return itemsMap.Values.ToList();
    }

    /// <summary>キャッシュからアイコンを取得。未キャッシュならEXEから取得してキャッシュ</summary>
    private static ImageSource? GetCachedIcon(string exePath)
    {
        if (_iconCache.TryGetValue(exePath, out var cached))
            return cached;

        var icon = GetIconFromExe(exePath);
        _iconCache[exePath] = icon;
        return icon;
    }

    /// <summary>EXEファイルからアイコンを取得（取得後にIconをDispose）</summary>
    private static ImageSource? GetIconFromExe(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;

            var bmp = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze(); // クロススレッドアクセスとGC防止
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
    }
}

