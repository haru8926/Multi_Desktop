using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Multi_Desktop
{
    /// <summary>
    /// デスクトップフォルダの中身を読み取り、WebView2の上にWindows風デスクトップアイコンを表示するオーバーレイウィンドウ。
    /// ファイルはすべて本来のデスクトップファイルへのリンクとして動作するため、SSD書き込みは発生しない。
    /// </summary>
    public partial class VirtualDesktopOverlay : Window
    {
        private FileSystemWatcher? _userDesktopWatcher;
        private FileSystemWatcher? _publicDesktopWatcher;
        private readonly ObservableCollection<DesktopItem> _items = new();

        // コンテキストメニュー
        private ContextMenu? _itemContextMenu;
        private ContextMenu? _desktopContextMenu;

        public VirtualDesktopOverlay()
        {
            InitializeComponent();
            DesktopItemsControl.ItemsSource = _items;
            BuildContextMenus();
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // ウィンドウをクリックしてもアクティブにならないようにする（Alt+Tabに表示しない）
            MakeClickThrough(false);
            RefreshDesktopItems();
            StartFileWatchers();
        }

        /// <summary>
        /// WS_EX_TOOLWINDOW を設定してAlt+Tabに表示されないようにする
        /// </summary>
        private void MakeClickThrough(bool clickThrough)
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero) return;

            const int GWL_EXSTYLE = -20;
            const long WS_EX_TOOLWINDOW = 0x00000080L;

            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern long GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern long SetWindowLongPtr(IntPtr hWnd, int nIndex, long dwNewLong);

        #region デスクトップアイテムの読み取り

        /// <summary>
        /// ユーザーデスクトップとパブリックデスクトップの両方からファイルを読み取り、統合して表示する
        /// </summary>
        public void RefreshDesktopItems()
        {
            try
            {
                var items = new List<DesktopItem>();

                // ユーザーのデスクトップ
                string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (Directory.Exists(userDesktop))
                {
                    items.AddRange(GetItemsFromDirectory(userDesktop));
                }

                // パブリックデスクトップ (共通ショートカット類)
                string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                if (Directory.Exists(publicDesktop) && publicDesktop != userDesktop)
                {
                    items.AddRange(GetItemsFromDirectory(publicDesktop));
                }

                // 重複除去（ファイル名ベース）& ソート
                var uniqueItems = items
                    .GroupBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(i => i.IsDirectory ? 0 : 1) // フォルダ優先
                    .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _items.Clear();
                    foreach (var item in uniqueItems)
                    {
                        _items.Add(item);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RefreshDesktopItems failed: {ex.Message}");
            }
        }

        private List<DesktopItem> GetItemsFromDirectory(string directoryPath)
        {
            var items = new List<DesktopItem>();

            try
            {
                // desktop.ini は除外
                // 隠しファイルも除外
                var entries = Directory.GetFileSystemEntries(directoryPath);
                foreach (var entry in entries)
                {
                    try
                    {
                        var attr = File.GetAttributes(entry);
                        // 隠しファイル/システムファイルをスキップ
                        if ((attr & FileAttributes.Hidden) != 0) continue;
                        if ((attr & FileAttributes.System) != 0) continue;

                        string fileName = Path.GetFileName(entry);
                        if (string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                        bool isDir = (attr & FileAttributes.Directory) != 0;
                        string displayName = isDir ? fileName : Path.GetFileNameWithoutExtension(fileName);

                        // .lnk ファイルの場合は拡張子を表示しない
                        if (Path.GetExtension(fileName).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                        {
                            displayName = Path.GetFileNameWithoutExtension(fileName);
                        }
                        // .url ファイルの場合も拡張子を表示しない
                        else if (Path.GetExtension(fileName).Equals(".url", StringComparison.OrdinalIgnoreCase))
                        {
                            displayName = Path.GetFileNameWithoutExtension(fileName);
                        }

                        var icon = GetFileIcon(entry, isDir);

                        items.Add(new DesktopItem
                        {
                            DisplayName = displayName,
                            FullPath = entry,
                            IsDirectory = isDir,
                            Icon = icon
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Skipping entry {entry}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetItemsFromDirectory failed for {directoryPath}: {ex.Message}");
            }

            return items;
        }

        /// <summary>
        /// Shell32 APIを使ってファイル/フォルダのアイコンを取得する
        /// </summary>
        private static ImageSource? GetFileIcon(string path, bool isDirectory)
        {
            try
            {
                var shinfo = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_LARGEICON;

                if (isDirectory)
                    flags |= SHGFI_USEFILEATTRIBUTES;

                var result = SHGetFileInfo(
                    path,
                    isDirectory ? FILE_ATTRIBUTE_DIRECTORY : 0,
                    ref shinfo,
                    (uint)Marshal.SizeOf(shinfo),
                    flags);

                if (result != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    var icon = System.Drawing.Icon.FromHandle(shinfo.hIcon);
                    var bmpSource = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bmpSource.Freeze();
                    DestroyIcon(shinfo.hIcon);
                    return bmpSource;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetFileIcon failed for {path}: {ex.Message}");
            }
            return null;
        }

        #region Shell32 P/Invoke

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        #endregion

        #endregion

        #region ファイル監視 (FileSystemWatcher)

        private void StartFileWatchers()
        {
            try
            {
                string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (Directory.Exists(userDesktop))
                {
                    _userDesktopWatcher = CreateWatcher(userDesktop);
                }

                string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                if (Directory.Exists(publicDesktop) && publicDesktop != userDesktop)
                {
                    _publicDesktopWatcher = CreateWatcher(publicDesktop);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StartFileWatchers failed: {ex.Message}");
            }
        }

        private FileSystemWatcher CreateWatcher(string path)
        {
            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            // デバウンスタイマーで頻繁な更新を抑制
            System.Threading.Timer? debounceTimer = null;
            void ScheduleRefresh()
            {
                debounceTimer?.Dispose();
                debounceTimer = new System.Threading.Timer(_ =>
                {
                    RefreshDesktopItems();
                }, null, 500, System.Threading.Timeout.Infinite);
            }

            watcher.Created += (s, e) => ScheduleRefresh();
            watcher.Deleted += (s, e) => ScheduleRefresh();
            watcher.Renamed += (s, e) => ScheduleRefresh();
            watcher.Changed += (s, e) => ScheduleRefresh();

            return watcher;
        }

        public void StopFileWatchers()
        {
            if (_userDesktopWatcher != null)
            {
                _userDesktopWatcher.EnableRaisingEvents = false;
                _userDesktopWatcher.Dispose();
                _userDesktopWatcher = null;
            }
            if (_publicDesktopWatcher != null)
            {
                _publicDesktopWatcher.EnableRaisingEvents = false;
                _publicDesktopWatcher.Dispose();
                _publicDesktopWatcher = null;
            }
        }

        #endregion

        #region マウスイベント / アイテム操作

        private void Item_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 255, 255, 255));
            }
        }

        private void Item_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = System.Windows.Media.Brushes.Transparent;
            }
        }

        private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                // ダブルクリック → ファイルを開く
                if (sender is Border border && border.DataContext is DesktopItem item)
                {
                    OpenDesktopItem(item);
                }
            }
        }

        private void Item_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is DesktopItem item)
            {
                _itemContextMenu!.Tag = item;
                _itemContextMenu.IsOpen = true;
            }
            e.Handled = true;
        }

        private void OpenDesktopItem(DesktopItem item)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = item.FullPath,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open {item.FullPath}: {ex.Message}");
                System.Windows.MessageBox.Show($"ファイルを開けませんでした:\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region コンテキストメニュー

        private void BuildContextMenus()
        {
            // アイテム右クリックメニュー
            _itemContextMenu = new ContextMenu();

            var openItem = new MenuItem { Header = "開く(_O)" };
            openItem.Click += (s, e) =>
            {
                if (_itemContextMenu.Tag is DesktopItem item)
                    OpenDesktopItem(item);
            };
            _itemContextMenu.Items.Add(openItem);

            var openLocationItem = new MenuItem { Header = "ファイルの場所を開く(_L)" };
            openLocationItem.Click += (s, e) =>
            {
                if (_itemContextMenu.Tag is DesktopItem item)
                {
                    try
                    {
                        Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
                    }
                    catch { }
                }
            };
            _itemContextMenu.Items.Add(openLocationItem);

            _itemContextMenu.Items.Add(new Separator());

            var propertiesItem = new MenuItem { Header = "プロパティ(_P)" };
            propertiesItem.Click += (s, e) =>
            {
                if (_itemContextMenu.Tag is DesktopItem item)
                {
                    ShowFileProperties(item.FullPath);
                }
            };
            _itemContextMenu.Items.Add(propertiesItem);

            // デスクトップ背景の右クリックメニュー
            _desktopContextMenu = new ContextMenu();

            var refreshItem = new MenuItem { Header = "表示を更新(_R)" };
            refreshItem.Click += (s, e) => RefreshDesktopItems();
            _desktopContextMenu.Items.Add(refreshItem);

            var openDesktopFolder = new MenuItem { Header = "デスクトップフォルダを開く(_D)" };
            openDesktopFolder.Click += (s, e) =>
            {
                try
                {
                    Process.Start("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                }
                catch { }
            };
            _desktopContextMenu.Items.Add(openDesktopFolder);

            // ウィンドウ背景の右クリック
            this.MouseRightButtonDown += (s, e) =>
            {
                _desktopContextMenu.IsOpen = true;
                e.Handled = true;
            };
        }

        private static void ShowFileProperties(string path)
        {
            try
            {
                var info = new SHELLEXECUTEINFO
                {
                    cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                    lpVerb = "properties",
                    lpFile = path,
                    nShow = 5, // SW_SHOW
                    fMask = 0x0000000C // SEE_MASK_INVOKEIDLIST
                };
                ShellExecuteEx(ref info);
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpVerb;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpFile;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpParameters;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            StopFileWatchers();
            base.OnClosed(e);
        }
    }

    /// <summary>
    /// デスクトップに表示する1つのアイテム（ファイルまたはフォルダ）
    /// </summary>
    public class DesktopItem : INotifyPropertyChanged
    {
        private string _displayName = "";
        private string _fullPath = "";
        private bool _isDirectory;
        private ImageSource? _icon;

        public string DisplayName
        {
            get => _displayName;
            set { _displayName = value; OnPropertyChanged(nameof(DisplayName)); }
        }

        public string FullPath
        {
            get => _fullPath;
            set { _fullPath = value; OnPropertyChanged(nameof(FullPath)); }
        }

        public bool IsDirectory
        {
            get => _isDirectory;
            set { _isDirectory = value; OnPropertyChanged(nameof(IsDirectory)); }
        }

        public ImageSource? Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(nameof(Icon)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
