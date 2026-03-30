using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Windows.Interop;
using Multi_Desktop.Helpers;
using System.Diagnostics;

namespace Multi_Desktop
{
    public enum YoutubeMode
    {
        FullScreen,
        Background,        // 背景モード（ぼかしあり）
        BackgroundClear    // 背景モード（ぼかしなし）
    }

    public static class YoutubeTvWindowManager
    {
        private static YoutubeTvWindow? _mainWindow = null;
        private static List<YoutubeTvCloneWindow> _cloneWindows = new List<YoutubeTvCloneWindow>();
        private static VirtualDesktopOverlay? _desktopOverlay = null;

        public static bool IsYoutubeModeActive => _mainWindow != null;
        public static bool IsDesktopOverlayActive => _desktopOverlay != null && _desktopOverlay.IsVisible;

        public static void ShowAllWindows()
        {
            if (_mainWindow != null) return;

            // 1. メインウィンドウを作成・表示
            _mainWindow = new YoutubeTvWindow();
            var primaryScreen = Screen.PrimaryScreen;
            SetWindowToScreen(_mainWindow, primaryScreen);
            _mainWindow.Show();

            // 2. サブディスプレイ用にクローンウィンドウを作成（全画面モード用: PrintWindow）
            CreateCloneWindows(CloneCaptureMode.PrintWindow, 16);

            // 3. スマホリモコン用UDPサーバーを起動
            YoutubeTvUdpServer.Start(_mainWindow.webView);
        }

        // ★ モードを切り替えるメソッド
        public static YoutubeMode CurrentMode { get; private set; } = YoutubeMode.FullScreen;

        public static void ChangeMode(YoutubeMode mode)
        {
            if (_mainWindow == null || !_mainWindow.IsLoaded) return;
            if (mode == CurrentMode) return;

            var helper = new WindowInteropHelper(_mainWindow);
            IntPtr mainHwnd = helper.Handle;
            var primaryScreen = Screen.PrimaryScreen;

            if (mode == YoutubeMode.Background || mode == YoutubeMode.BackgroundClear)
            {
                // ---- 背景モードに入る ----
                CurrentMode = mode;

                // 1. 全画面用クローンを閉じる
                CloseCloneWindows();

                // 2. メインウィンドウのぼかし設定
                bool useBlur = (mode == YoutubeMode.Background);
                _mainWindow.Topmost = false;
                _mainWindow.SetBackgroundMode(true, useBlur);

                // 3. メインウィンドウをプライマリディスプレイの背景(WorkerW)に配置
                //    ※ これは以前から正常に動作している方式
                NativeMethods.SetWindowToBackground(mainHwnd,
                    primaryScreen.Bounds.X, primaryScreen.Bounds.Y,
                    primaryScreen.Bounds.Width, primaryScreen.Bounds.Height);

                // 4. サブディスプレイ用クローンを作成
                //    ※ WorkerWには入れず、CapturePreview + HWND_BOTTOM で最背面配置
                CreateCloneWindows(CloneCaptureMode.WebViewCapture, 33);

                // 5. 各クローンウィンドウを最背面（HWND_BOTTOM）に配置
                PushCloneWindowsToBottom();

                // 6. 仮想デスクトップオーバーレイを表示
                ShowDesktopOverlay(primaryScreen);
            }
            else if (mode == YoutubeMode.FullScreen)
            {
                // ---- 全画面モードに復帰 ----

                // 1. オーバーレイを非表示
                HideDesktopOverlay();

                // 2. 背景用クローンを閉じる
                CloseCloneWindows();

                // 3. メインウィンドウを背景から復帰
                RestoreMainFromBackground();

                // 4. メインウィンドウを作り直す（確実にリセット）
                _mainWindow.DisposeWebView();
                _mainWindow.Close();
                _mainWindow = null;
                CurrentMode = YoutubeMode.FullScreen;

                // 5. 全てのウィンドウを再作成
                ShowAllWindows();
            }
        }

        /// <summary>
        /// サブディスプレイ用のクローンウィンドウを作成する
        /// </summary>
        private static void CreateCloneWindows(CloneCaptureMode captureMode, int intervalMs)
        {
            if (_mainWindow == null) return;

            foreach (var screen in Screen.AllScreens)
            {
                if (screen.Primary) continue;

                var cloneWin = new YoutubeTvCloneWindow(_mainWindow, _mainWindow.webView);
                cloneWin.TargetScreen = screen;
                cloneWin.CaptureMode = captureMode;
                cloneWin.CaptureIntervalMs = intervalMs;
                SetWindowToScreen(cloneWin, screen);
                cloneWin.Show();
                _cloneWindows.Add(cloneWin);
            }
        }

        /// <summary>
        /// クローンウィンドウを全て閉じる
        /// </summary>
        private static void CloseCloneWindows()
        {
            foreach (var win in _cloneWindows)
            {
                win.StopCapture();
                win.Close();
            }
            _cloneWindows.Clear();
        }

        /// <summary>
        /// クローンウィンドウを最背面（HWND_BOTTOM）に配置する。
        /// WorkerWの子にはしないため、CapturePreviewAsync が正常に動作する。
        /// </summary>
        private static void PushCloneWindowsToBottom()
        {
            foreach (var win in _cloneWindows)
            {
                var cloneHelper = new WindowInteropHelper(win);
                IntPtr cloneHwnd = cloneHelper.Handle;
                if (cloneHwnd != IntPtr.Zero)
                {
                    NativeMethods.SetWindowToBottom(cloneHwnd);
                }
            }
        }

        /// <summary>
        /// メインウィンドウを背景(WorkerW)から復帰させる
        /// </summary>
        private static void RestoreMainFromBackground()
        {
            if (_mainWindow == null || !_mainWindow.IsLoaded) return;
            try
            {
                var helper = new WindowInteropHelper(_mainWindow);
                IntPtr hwnd = helper.Handle;
                if (hwnd != IntPtr.Zero)
                {
                    NativeMethods.RestoreWindowFromBackground(hwnd);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RestoreMainFromBackground failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 仮想デスクトップオーバーレイを表示する
        /// </summary>
        private static void ShowDesktopOverlay(Screen screen)
        {
            try
            {
                if (_desktopOverlay != null)
                {
                    _desktopOverlay.Close();
                    _desktopOverlay = null;
                }

                _desktopOverlay = new VirtualDesktopOverlay();
                _desktopOverlay.WindowState = System.Windows.WindowState.Maximized;
                _desktopOverlay.Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShowDesktopOverlay failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 仮想デスクトップオーバーレイを非表示にする
        /// </summary>
        private static void HideDesktopOverlay()
        {
            try
            {
                if (_desktopOverlay != null)
                {
                    _desktopOverlay.StopFileWatchers();
                    _desktopOverlay.Close();
                    _desktopOverlay = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HideDesktopOverlay failed: {ex.Message}");
            }
        }

        private static void SetWindowToScreen(System.Windows.Window win, Screen screen)
        {
            // DPIスケーリングを考慮してWPF論理座標に変換する
            // Screen.Bounds は物理ピクセル座標だが、WPFの Left/Top/Width/Height は論理座標
            var logicalBounds = NativeMethods.GetLogicalScreenBounds(
                new System.Windows.Point(screen.Bounds.Left + screen.Bounds.Width / 2,
                                         screen.Bounds.Top + screen.Bounds.Height / 2));

            win.Left = logicalBounds.Left;
            win.Top = logicalBounds.Top;
            win.Width = logicalBounds.Width;
            win.Height = logicalBounds.Height;
            win.WindowState = System.Windows.WindowState.Normal;
        }

        public static void CloseAllWindows()
        {
            // UDPサーバーを停止
            YoutubeTvUdpServer.Stop();

            // リソースをクリーンアップ
            HideDesktopOverlay();
            CloseCloneWindows();

            // メインウィンドウを背景から復帰
            RestoreMainFromBackground();

            // メインウィンドウを閉じる
            if (_mainWindow != null)
            {
                _mainWindow.DisposeWebView();
                _mainWindow.Close();
                _mainWindow = null;
            }

            CurrentMode = YoutubeMode.FullScreen;
        }
    }
}