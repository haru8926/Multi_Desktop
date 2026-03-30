using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Windows.Interop;
using Multi_Desktop.Helpers;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Multi_Desktop
{
    public enum YoutubeMode
    {
        FullScreen,
        Background
    }

    public static class YoutubeTvWindowManager
    {
        private static YoutubeTvWindow? _mainWindow = null;
        private static List<YoutubeTvCloneWindow> _cloneWindows = new List<YoutubeTvCloneWindow>();

        public static bool IsYoutubeModeActive => _mainWindow != null;

        public static void ShowAllWindows()
        {
            if (_mainWindow != null) return;

            // 1. メインウィンドウを作成・表示
            _mainWindow = new YoutubeTvWindow();
            var primaryScreen = Screen.PrimaryScreen;
            SetWindowToScreen(_mainWindow, primaryScreen);
            _mainWindow.Show();

            // 2. サブディスプレイ用にクローンウィンドウを作成
            foreach (var screen in Screen.AllScreens)
            {
                if (screen.Primary) continue;

                var cloneWin = new YoutubeTvCloneWindow(_mainWindow.webView);
                SetWindowToScreen(cloneWin, screen);
                cloneWin.Show();
                _cloneWindows.Add(cloneWin);
            }

            // 3. スマホリモコン用UDPサーバーを起動
            YoutubeTvUdpServer.Start(_mainWindow.webView);
        }

        // ★ 新しく追加：モードを切り替えるメソッド
        public static YoutubeMode CurrentMode { get; private set; } = YoutubeMode.FullScreen;
        public static void ChangeMode(YoutubeMode mode)
        {
            CurrentMode = mode;
            if (_mainWindow == null || !_mainWindow.IsLoaded) return;

            var helper = new WindowInteropHelper(_mainWindow);
            IntPtr mainHwnd = helper.Handle;
            var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;

            if (mode == YoutubeMode.Background)
            {
                // Topmostを解除してから背景レイヤーに配置する
                _mainWindow.Topmost = false;
                _mainWindow.SetBackgroundMode(true);

                // クローンウィンドウは背景モードでは不要なので非表示にする
                foreach (var win in _cloneWindows)
                {
                    win.Topmost = false;
                    win.Hide();
                }

                NativeMethods.SetWindowToBackground(mainHwnd,
                    primaryScreen.Bounds.X, primaryScreen.Bounds.Y,
                    primaryScreen.Bounds.Width, primaryScreen.Bounds.Height);
            }
            else if (mode == YoutubeMode.FullScreen)
            {
                // 背景モードから復帰する場合は、まず親ウィンドウを元に戻す
                RestoreFromBackgroundIfNeeded();

                // ウィンドウを閉じて作り直す
                foreach (var win in _cloneWindows)
                    win.Close();
                _cloneWindows.Clear();
                _mainWindow.DisposeWebView();
                _mainWindow.Close();
                _mainWindow = null;
                CurrentMode = YoutubeMode.FullScreen;

                // 新しく起動
                ShowAllWindows();
            }
        }
        private static void SetWindowToScreen(System.Windows.Window win, Screen screen)
        {
            win.Left = screen.Bounds.Left;
            win.Top = screen.Bounds.Top;
            win.Width = screen.Bounds.Width;
            win.Height = screen.Bounds.Height;
            win.WindowState = System.Windows.WindowState.Normal;
        }

        /// <summary>
        /// 背景モード中の場合、ウィンドウの親をデスクトップに戻す
        /// </summary>
        private static void RestoreFromBackgroundIfNeeded()
        {
            if (_mainWindow == null || !_mainWindow.IsLoaded) return;

            try
            {
                var helper = new WindowInteropHelper(_mainWindow);
                IntPtr mainHwnd = helper.Handle;
                if (mainHwnd != IntPtr.Zero)
                {
                    // 親ウィンドウをデスクトップに戻し、非表示のWorkerWも復元する
                    NativeMethods.RestoreWindowFromBackground(mainHwnd);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RestoreFromBackgroundIfNeeded failed: {ex.Message}");
            }
        }

        public static void CloseAllWindows()
        {
            // UDPサーバーを停止
            YoutubeTvUdpServer.Stop();

            // 背景モードの場合は先に親ウィンドウを元に戻す
            RestoreFromBackgroundIfNeeded();

            foreach (var win in _cloneWindows)
                win.Close();
            _cloneWindows.Clear();

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