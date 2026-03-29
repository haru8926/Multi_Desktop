using System.Collections.Generic;
using System.Windows.Forms;

namespace Multi_Desktop
{
    public static class YoutubeTvWindowManager
    {
        private static YoutubeTvWindow? _mainWindow = null;
        private static List<YoutubeTvCloneWindow> _cloneWindows = new List<YoutubeTvCloneWindow>();

        // ★ Youtubeモードが起動しているかどうかを判定するプロパティ
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
        }

        private static void SetWindowToScreen(System.Windows.Window win, Screen screen)
        {
            win.Left = screen.Bounds.Left;
            win.Top = screen.Bounds.Top;
            win.Width = screen.Bounds.Width;
            win.Height = screen.Bounds.Height;
            win.WindowState = System.Windows.WindowState.Normal;
        }

        public static void CloseAllWindows()
        {
            foreach (var win in _cloneWindows)
            {
                win.Close();
            }
            _cloneWindows.Clear();

            if (_mainWindow != null)
            {
                _mainWindow.Close();
                _mainWindow = null;
            }
        }
    }
}