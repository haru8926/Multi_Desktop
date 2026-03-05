using System.Runtime.InteropServices;
using System.Text;

namespace Multi_Desktop.Helpers;

/// <summary>
/// Windows API 呼び出し用のネイティブメソッド
/// </summary>
internal static partial class NativeMethods
{
    // ─── SHChangeNotify ───────────────────────────────
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST = 0x0000;

    /// <summary>
    /// シェルにデスクトップ変更を通知してアイコン表示を更新する
    /// </summary>
    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static void NotifyDesktopChanged()
    {
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    // ─── SystemParametersInfo (壁紙変更) ──────────────
    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        int uAction, int uParam, string lpvParam, int fuWinIni);

    /// <summary>
    /// デスクトップの壁紙を指定された画像ファイルに変更する
    /// </summary>
    public static bool SetWallpaper(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !System.IO.File.Exists(imagePath))
            return false;

        return SystemParametersInfo(
            SPI_SETDESKWALLPAPER, 0, imagePath,
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }

    // ─── タスクバー表示/非表示 ─────────────────────────
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>タスクバー非表示を維持するタイマー</summary>
    private static System.Threading.Timer? _keepHiddenTimer;

    /// <summary>Windowsタスクバーを非表示にし、全画面を確保する</summary>
    public static void HideTaskbar()
    {
        // 1. 自動的に隠す設定を有効化してWorkAreaを拡張
        SetTaskbarAutoHide(true);

        // 2. タスクバーを画面外に押し出し続ける（マウスが下に行っても見えないようにする）
        _keepHiddenTimer?.Dispose();
        _keepHiddenTimer = new System.Threading.Timer(_ =>
        {
            ForcePushTaskbarOffScreen();
        }, null, 0, 1500); // すぐ実行し、1500ms間隔で監視
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    /// <summary>タスクバーを画面外に押し出す</summary>
    private static void ForcePushTaskbarOffScreen()
    {
        // 1. メインディスプレイのタスクバー
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero)
        {
            ShowWindow(taskbar, SW_HIDE);
            SetWindowPos(taskbar, IntPtr.Zero, 0,
                GetSystemMetrics(SM_CYSCREEN) + 100,
                0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        var startBtn = FindWindow("Button", "Start");
        if (startBtn != IntPtr.Zero) ShowWindow(startBtn, SW_HIDE);

        // 2. サブディスプレイのタスクバー (複数ある可能性を考慮)
        IntPtr secTaskbar = IntPtr.Zero;
        while ((secTaskbar = FindWindowEx(IntPtr.Zero, secTaskbar, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            ShowWindow(secTaskbar, SW_HIDE);
            SetWindowPos(secTaskbar, IntPtr.Zero, 0,
                GetSystemMetrics(SM_CYSCREEN) + 100,
                0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    /// <summary>Windowsタスクバーを表示する</summary>
    public static void ShowTaskbar()
    {
        // タイマーを停止
        _keepHiddenTimer?.Dispose();
        _keepHiddenTimer = null;

        var screenH = GetSystemMetrics(SM_CYSCREEN);
        var screenW = GetSystemMetrics(SM_CXSCREEN);

        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero)
        {
            // タスクバーを元の位置に戻す
            SetWindowPos(taskbar, IntPtr.Zero, 0,
                screenH - 48, screenW, 48, // 概算高さに戻す
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            ShowWindow(taskbar, SW_SHOW);
        }

        var startBtn = FindWindow("Button", "Start");
        if (startBtn != IntPtr.Zero) ShowWindow(startBtn, SW_SHOW);

        // サブディスプレイのタスクバーを復元
        IntPtr secTaskbar = IntPtr.Zero;
        while ((secTaskbar = FindWindowEx(IntPtr.Zero, secTaskbar, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            SetWindowPos(secTaskbar, IntPtr.Zero, 0,
                screenH - 48, screenW, 48,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            ShowWindow(secTaskbar, SW_SHOW);
        }

        // 自動的に隠す設定を解除
        SetTaskbarAutoHide(false);
    }

    // ─── SHAppBarMessage（タスクバー自動非表示制御） ────────
    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    private const uint ABM_SETSTATE = 0x0000000A;
    private const uint ABM_GETSTATE = 0x00000004;
    private const uint ABM_NEW = 0x00000000;
    private const uint ABM_REMOVE = 0x00000001;
    private const uint ABM_SETPOS = 0x00000003;
    private const int ABS_AUTOHIDE = 0x0000001;
    private const int ABS_ALWAYSONTOP = 0x0000002;

    // AppBar エッジ定数
    private const uint ABE_TOP = 1;

    [DllImport("user32.dll")]
    private static extern int RegisterWindowMessage(string msg);

    /// <summary>ウィンドウをAppBarとして登録し、画面上部のワークエリアを予約する</summary>
    public static bool RegisterTopAppBar(IntPtr hWnd, int barHeight, int screenLeft, int screenTop, int screenWidth)
    {
        var callbackMsg = (uint)RegisterWindowMessage("AppBarMessage_MultiDesktop");

        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hWnd,
            uCallbackMessage = callbackMsg
        };

        // AppBarとして登録
        var result = SHAppBarMessage(ABM_NEW, ref abd);

        // 上端の領域を予約
        abd.uEdge = ABE_TOP;
        abd.rc = new RECT
        {
            Left = screenLeft,
            Top = screenTop,
            Right = screenLeft + screenWidth,
            Bottom = screenTop + barHeight
        };
        SHAppBarMessage(ABM_SETPOS, ref abd);

        return result != 0;
    }

    /// <summary>AppBar登録を解除してワークエリアを元に戻す</summary>
    public static void UnregisterAppBar(IntPtr hWnd)
    {
        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hWnd
        };
        SHAppBarMessage(ABM_REMOVE, ref abd);
    }

    private static bool _wasAutoHideBefore;
    private static bool _savedAutoHideState;

    /// <summary>タスクバーの自動非表示を設定</summary>
    private static void SetTaskbarAutoHide(bool autoHide)
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) return;

        // 元の状態を保存（初回のみ）
        if (!_savedAutoHideState)
        {
            var getAbd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
            var state = SHAppBarMessage(ABM_GETSTATE, ref getAbd);
            _wasAutoHideBefore = (state & ABS_AUTOHIDE) != 0;
            _savedAutoHideState = true;
        }

        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = taskbar,
            lParam = (IntPtr)(autoHide ? ABS_AUTOHIDE : ABS_ALWAYSONTOP)
        };
        SHAppBarMessage(ABM_SETSTATE, ref abd);
    }

    // ─── SetWindowPos ──────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    /// <summary>指定されたウィンドウを常に最前面 (HWND_TOPMOST) に強制固定する</summary>
    public static void EnforceTopmost(IntPtr hWnd)
    {
        SetWindowPos(hWnd, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }


    // ─── WorkArea 関連 ──────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    // ─── モニターとDPI ──────────────────────────────────
    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);

    public const uint MONITOR_DEFAULTTONULL = 0;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>
    /// 指定された物理座標を含むモニターの論理（WPF）座標の矩形を取得する
    /// スケーリングやマルチモニターのオフセットによるWPF側の座標ズレを補正する
    /// </summary>
    public static System.Windows.Rect GetLogicalScreenBounds(System.Windows.Point physicalPoint)
    {
        var pt = new System.Drawing.Point((int)physicalPoint.X, (int)physicalPoint.Y);
        var hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);

        if (hMonitor != IntPtr.Zero)
        {
            var mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf<MONITORINFO>();
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                // DPI を取得
                uint dpiX = 96;
                uint dpiY = 96;
                GetDpiForMonitor(hMonitor, 0 /* MDT_EFFECTIVE_DPI */, out dpiX, out dpiY);

                double scaleX = dpiX / 96.0;
                double scaleY = dpiY / 96.0;

                return new System.Windows.Rect(
                    mi.rcMonitor.Left / scaleX,
                    mi.rcMonitor.Top / scaleY,
                    (mi.rcMonitor.Right - mi.rcMonitor.Left) / scaleX,
                    (mi.rcMonitor.Bottom - mi.rcMonitor.Top) / scaleY
                );
            }
        }
        
        // フォールバック
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)physicalPoint.X, (int)physicalPoint.Y));
        return new System.Windows.Rect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
    }

    // ─── ウィンドウ列挙 ────────────────────────────────
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    /// <summary>ウィンドウを前面に出す（最小化されていた場合は復元）</summary>
    public static void BringWindowToFront(IntPtr hWnd)
    {
        if (IsIconic(hWnd))
            ShowWindowAsync(hWnd, SW_RESTORE);
        SetForegroundWindow(hWnd);
    }

    // ─── ウィンドウスタイル ─────────────────────────────
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_APPWINDOW = 0x00040000L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;

    [DllImport("user32.dll")]
    private static extern long GetWindowLongPtr(IntPtr hWnd, int nIndex);

    // ─── DWM 関連（隠しUWPアプリ等の判定） ────────────────────────
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
    private const int DWMWA_CLOAKED = 14;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    /// <summary>タスクバーに表示すべきウィンドウかどうか判定</summary>
    public static bool IsAltTabWindow(IntPtr hWnd)
    {
        if (!IsWindowVisible(hWnd)) return false;

        var titleLen = GetWindowTextLength(hWnd);
        if (titleLen == 0) return false;

        var exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
        if ((exStyle & WS_EX_TOOLWINDOW) != 0) return false;
        if ((exStyle & WS_EX_NOACTIVATE) != 0) return false;

        // DWM Cloaked Check: 別仮想デスクトップのアプリやバックグラウンドのUWPプロセスを除外
        if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0)
        {
            if (cloaked != 0) return false;
        }

        // ウィンドウやクラス名による明示的な除外ルール
        var sbTitle = new StringBuilder(titleLen + 1);
        GetWindowText(hWnd, sbTitle, sbTitle.Capacity);
        var title = sbTitle.ToString();

        var sbClass = new StringBuilder(256);
        GetClassName(hWnd, sbClass, sbClass.Capacity);
        var className = sbClass.ToString();

        // Windows 10/11 の特殊システムウィンドウを除外
        if (title == "Windows 入力エクスペリエンス"
            || title == "Program Manager"
            || title == "Settings" // 必要に応じて
            || className == "Windows.UI.Core.CoreWindow"
            || className == "ApplicationFrameWindow" && title == "")
        {
            return false;
        }

        return true;
    }

    // ─── アイコン取得 ──────────────────────────────────
    private const int WM_GETICON = 0x007F;
    private const int ICON_BIG = 1;
    private const int ICON_SMALL2 = 2;
    private const int GCL_HICON = -14;

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

    /// <summary>ウィンドウのアイコンハンドルを取得</summary>
    public static IntPtr GetWindowIconHandle(IntPtr hWnd)
    {
        var icon = SendMessage(hWnd, WM_GETICON, ICON_BIG, 0);
        if (icon == IntPtr.Zero)
            icon = SendMessage(hWnd, WM_GETICON, ICON_SMALL2, 0);
        if (icon == IntPtr.Zero)
            icon = GetClassLongPtr(hWnd, GCL_HICON);
        return icon;
    }

    // ─── フォアグラウンドウィンドウ取得 ──────────────────────
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    // ─── メッセージ送信 ──────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>現在フォアグラウンドにあるウィンドウのタイトルを取得</summary>
    public static string GetForegroundWindowTitle()
    {
        var hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return string.Empty;

        var length = GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;

        var sb = new StringBuilder(length + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // ─── フルスクリーン判定・ウィンドウ矩形 ──────────────────────
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    // ─── スクリーンキャプチャ用 (PrintWindow) ────────────────────
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    /// <summary>ウィンドウのスクリーンショット(サムネイル用)を取得</summary>
    public static System.Windows.Media.ImageSource? GetWindowSnapshot(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return null;

        if (!GetWindowRect(hWnd, out var rect))
            return null;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0) return null;

        IntPtr hdcSrc = IntPtr.Zero;
        IntPtr hdcDest = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            hdcSrc = GetWindowDC(hWnd);
            if (hdcSrc == IntPtr.Zero) return null;

            hdcDest = CreateCompatibleDC(hdcSrc);
            hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
            hOld = SelectObject(hdcDest, hBitmap);

            bool success = PrintWindow(hWnd, hdcDest, PW_RENDERFULLCONTENT);

            SelectObject(hdcDest, hOld);
            hOld = IntPtr.Zero;

            if (!success) return null;

            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            source.Freeze(); // クロススレッドアクセス用
            return source;
        }
        catch { return null; }
        finally
        {
            // GDI リソースを確実に解放
            if (hOld != IntPtr.Zero && hdcDest != IntPtr.Zero) SelectObject(hdcDest, hOld);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (hdcDest != IntPtr.Zero) DeleteDC(hdcDest);
            if (hdcSrc != IntPtr.Zero) ReleaseDC(hWnd, hdcSrc);
        }
    }

    /// <summary>フォアグラウンドウィンドウが全画面表示か判定する</summary>
    public static bool IsForegroundFullScreen()
    {
        var hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return false;
        if (hWnd == GetDesktopWindow() || hWnd == GetShellWindow()) return false;

        // クラス名でWorkerW(デスクトップ背景)等は除外
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        string className = sb.ToString();
        if (className == "WorkerW" || className == "Progman") return false;

        if (!GetWindowRect(hWnd, out var rect)) return false;

        var screen = System.Windows.Forms.Screen.FromHandle(hWnd);

        // ウィンドウの矩形がスクリーンの境界以上かどうか
        return rect.Left <= screen.Bounds.Left &&
               rect.Top <= screen.Bounds.Top &&
               rect.Right >= screen.Bounds.Right &&
               rect.Bottom >= screen.Bounds.Bottom;
    }

    // ─── キーボードイベント（スタートメニュー表示用）──────────
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_LWIN = 0x5B;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>Windowsキーを押下してスタートメニューを開く</summary>
    public static void OpenStartMenu()
    {
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // ─── 音量ミキサー ──────────────────────────────────
    /// <summary>Windows音量ミキサーを表示</summary>
    public static void OpenVolumeMixer()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sndvol.exe",
                UseShellExecute = true
            });
        }
        catch { }
    }

    // ─── Bluetooth状態検出 ───────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_FIND_RADIO_PARAMS
    {
        public int dwSize;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(
        ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp, out IntPtr phRadio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindRadioClose(IntPtr hFind);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothIsConnectable(IntPtr hRadio);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>Bluetoothの状態を取得 (hasRadio, isOn)</summary>
    public static (bool HasRadio, bool IsOn) GetBluetoothStatus()
    {
        var param = new BLUETOOTH_FIND_RADIO_PARAMS
        {
            dwSize = Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>()
        };

        var findHandle = BluetoothFindFirstRadio(ref param, out var radioHandle);
        if (findHandle == IntPtr.Zero)
            return (false, false);

        bool isOn = BluetoothIsConnectable(radioHandle);
        CloseHandle(radioHandle);
        BluetoothFindRadioClose(findHandle);
        return (true, isOn);
    }

    // ─── 通知センター（アクションセンター）────────────
    private const byte VK_N = 0x4E;

    /// <summary>Windows通知センターを開く (Win+N)</summary>
    public static void OpenNotificationCenter()
    {
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_N, 0, 0, UIntPtr.Zero);
        keybd_event(VK_N, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
