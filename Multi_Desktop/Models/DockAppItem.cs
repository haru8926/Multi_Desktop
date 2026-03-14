using System.Windows.Media;

namespace Multi_Desktop.Models;

public class WindowInfo
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// Dockに表示するアプリケーション情報
/// </summary>
public class DockAppItem
{
    /// <summary>アプリ表示名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>実行ファイルパス</summary>
    public string ExePath { get; set; } = string.Empty;

    /// <summary>アプリアイコン（WPF用 ImageSource）</summary>
    public ImageSource? Icon { get; set; }

    /// <summary>現在実行中かどうか</summary>
    public bool IsRunning { get; set; }

    /// <summary>ピン留めされているかどうか</summary>
    public bool IsPinned { get; set; }

    /// <summary>ウィンドウハンドル（実行中の場合）</summary>
    public IntPtr WindowHandle { get; set; }

    /// <summary>このアプリに属するすべてのウィンドウ</summary>
    public List<WindowInfo> Windows { get; set; } = new();
}
