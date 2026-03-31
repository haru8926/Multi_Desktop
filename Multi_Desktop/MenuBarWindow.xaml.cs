using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Multi_Desktop.Helpers;
using Multi_Desktop.Models;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Multi_Desktop;

/// <summary>
/// macOS風メニューバーのコードビハインド
/// 各ディスプレイごとに1インスタンス生成される
/// </summary>
public partial class MenuBarWindow : Window
{
    private DispatcherTimer? _clockTimer;
    private DispatcherTimer? _activeWindowTimer;
    private DispatcherTimer? _statusTimer;
    private DispatcherTimer? _topmostTimer;
    private bool _isAppBarRegistered;
    private HwndSource? _hwndSource;
    private bool _suppressVolumeEvent;
    private MusicPlayerWindow? _musicPlayerWindow;
    private MusicServiceSettings _musicSettings = new();
    private QuickAiPanelWindow? _quickAiWindow;
    private string _quickAiService = "ChatGPT";

    // ─── 一時保存トレイのデータ ──────────────────────────
    private object? _tempStorageData;
    private bool _isTempStorageDragging;
    private System.Windows.Point _tempStorageDragStartPoint;

    // 対象ディスプレイ（物理座標）
    private readonly System.Windows.Forms.Screen _targetScreen;

    // カレンダー表示用
    private DateTime _calendarDisplayMonth;

    public MenuBarWindow(System.Windows.Forms.Screen targetScreen)
    {
        _targetScreen = targetScreen;
        _calendarDisplayMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        InitializeComponent();
        Loaded += MenuBarWindow_Loaded;
    }

    // ─── 初期化 ──────────────────────────────────────
    private void MenuBarWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PositionMenuBar();

        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WndProc);

        RegisterAsAppBar();
        Dispatcher.InvokeAsync(() => PositionMenuBar(), DispatcherPriority.Render);

        StartClockTimer();
        StartActiveWindowTimer();
        StartStatusTimer();
        StartTopmostTimer();
        UpdateNetworkStatus();
        UpdateBatteryStatus();

        // ── プラグインUIの登録 ──
        if (_targetScreen.Primary)
        {
            App.PluginHost.OnMenuItemAdded += InjectMenuItem;
            App.PluginHost.OnTrayPopupViewAdded += InjectTrayPopupView;
            
            // 既にロードされたプラグインUIを注入
            App.PluginHost.InjectBufferedUI(this);
        }
    }

    public void InjectMenuItem(string header, Action? action)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var border = new Border
            {
                Style = (Style)FindResource("MenuBarButton"),
                Padding = new System.Windows.Thickness(7, 3, 7, 3),
                Margin = new System.Windows.Thickness(4, 0, 0, 0)
            };
            border.MouseLeftButtonUp += (s, ev) => action?.Invoke();
            border.MouseEnter += MenuBarItem_MouseEnter;
            border.MouseLeave += MenuBarItem_MouseLeave;

            var text = new TextBlock
            {
                Text = header,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontSize = 12.5,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            border.Child = text;

            PluginMenuItems.Children.Add(border);
            PluginSeparator.Visibility = Visibility.Visible;
        }, DispatcherPriority.Normal);
    }

    public void InjectTrayPopupView(UIElement view)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (view is FrameworkElement fe)
            {
                if (fe.Parent is System.Windows.Controls.Panel p) p.Children.Remove(view);
                else if (fe.Parent is ContentControl c) c.Content = null;
                else if (fe.Parent is Border b) b.Child = null;
            }

            if (!PluginTrayContainer.Children.Contains(view))
                PluginTrayContainer.Children.Add(view);
        }, DispatcherPriority.Normal);
    }

    // ─── WndProc（自己リポジショニング防止）──────────
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_WINDOWPOSCHANGING = 0x0046;
        const int WM_SETTINGCHANGE = 0x001A;

        if (msg == WM_WINDOWPOSCHANGING)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            wp.x = (int)Left;
            wp.y = (int)Top;
            wp.flags |= 0x0002; // SWP_NOMOVE
            Marshal.StructureToPtr(wp, lParam, true);
        }
        else if (msg == WM_SETTINGCHANGE)
        {
            Dispatcher.InvokeAsync(() => PositionMenuBar(), DispatcherPriority.Render);
        }

        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy, flags;
    }

    // ─── AppBar ──────────────────────────────────────
    private void RegisterAsAppBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        double dpiScale = 1.0;
        var pt = new System.Drawing.Point(_targetScreen.Bounds.X + 1, _targetScreen.Bounds.Y + 1);
        IntPtr hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hMonitor != IntPtr.Zero && NativeMethods.GetDpiForMonitor(hMonitor, 0, out uint _, out uint dpiY) == 0)
        {
            dpiScale = dpiY / 96.0;
        }

        var physicalBarHeight = (int)(28 * dpiScale);

        NativeMethods.RegisterTopAppBar(hwnd, physicalBarHeight,
            _targetScreen.Bounds.Left, _targetScreen.Bounds.Top, _targetScreen.Bounds.Width);
        _isAppBarRegistered = true;
    }
    private void UnregisterAsAppBar()
    {
        if (!_isAppBarRegistered) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.UnregisterAppBar(hwnd);
        _isAppBarRegistered = false;
    }

    private void PositionMenuBar()
    {
        var screen = GetLogicalScreenBounds();
        Left = screen.Left;
        Top = screen.Top;
        Width = screen.Width;
        Height = 28;
    }

    private Rect GetLogicalScreenBounds()
    {
        // ターゲットスクリーンの左上座標を渡し、モニター固有のDPIで計算された論理矩形を取得する
        return NativeMethods.GetLogicalScreenBounds(
            new System.Windows.Point(_targetScreen.Bounds.X + 1, _targetScreen.Bounds.Y + 1));
    }
    // ─── 時計 ────────────────────────────────────────
    private void StartClockTimer()
    {
        UpdateClock();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var dayOfWeek = now.ToString("ddd", System.Globalization.CultureInfo.GetCultureInfo("ja-JP"));
        ClockText.Text = $"{now:M月d日}({dayOfWeek}) {now:H:mm}";
    }

    // ─── アクティブウィンドウ監視 ─────────────────────
    private void StartActiveWindowTimer()
    {
        _activeWindowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _activeWindowTimer.Tick += (_, _) => UpdateActiveWindowTitle();
        _activeWindowTimer.Start();
    }

    private void UpdateActiveWindowTitle()
    {
        bool isFullScreen = NativeMethods.IsForegroundFullScreen();

        // ★ 追加: 仮想デスクトップオーバーレイが表示中の場合は隠さない
        if (YoutubeTvWindowManager.IsDesktopOverlayActive)
        {
            isFullScreen = false;
        }

        this.Visibility = isFullScreen ? Visibility.Collapsed : Visibility.Visible;
        var title = NativeMethods.GetForegroundWindowTitle();
        if (string.IsNullOrEmpty(title)
            || title == "macOS MenuBar"
            || title == "MacOS Dock"
            || title == "Multi Desktop Switcher")
            return;

        ActiveWindowTitle.Text = title;
    }

    private void StartTopmostTimer()
    {
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _topmostTimer.Tick += (_, _) =>
        {
            bool isFullScreen = NativeMethods.IsForegroundFullScreen();

            // ★ 追加: 仮想デスクトップオーバーレイが表示中の場合は最前面を維持する
            if (YoutubeTvWindowManager.IsDesktopOverlayActive)
            {
                isFullScreen = false;
            }

            if (isFullScreen) return;

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.EnforceTopmost(hwnd);
            }
        };
        _topmostTimer.Start();
    }
    // ─── ステータス更新 ──────────────────────────────
    private void StartStatusTimer()
    {
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _statusTimer.Tick += (_, _) =>
        {
            UpdateNetworkStatus();
            UpdateBatteryStatus();
        };
        _statusTimer.Start();
    }

    private void UpdateNetworkStatus()
    {
        try
        {
            bool isConnected = NetworkInterface.GetIsNetworkAvailable();
            NetworkIcon.Text = isConnected ? "\uE701" : "\uE871";
            CC_WifiStatus.Text = isConnected ? "接続済み" : "未接続";
        }
        catch
        {
            NetworkIcon.Text = "\uE871";
            CC_WifiStatus.Text = "不明";
        }

        // Bluetooth状態も更新
        UpdateBluetoothStatus();
    }

    private void UpdateBluetoothStatus()
    {
        try
        {
            var (hasRadio, isOn) = NativeMethods.GetBluetoothStatus();
            if (!hasRadio)
            {
                CC_BluetoothTile.Visibility = Visibility.Collapsed;
                return;
            }

            CC_BluetoothTile.Visibility = Visibility.Visible;
            CC_BluetoothStatus.Text = isOn ? "オン" : "オフ";

            // タイルのスタイルを切替
            CC_BluetoothTile.Style = isOn
                ? (Style)FindResource("CCTileActive")
                : (Style)FindResource("CCTile");
        }
        catch
        {
            CC_BluetoothStatus.Text = "不明";
        }
    }

    private void UpdateBatteryStatus()
    {
        try
        {
            var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus;
            if (powerStatus.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
            {
                BatteryButton.Visibility = Visibility.Collapsed;
                return;
            }

            BatteryButton.Visibility = Visibility.Visible;
            var percent = (int)(powerStatus.BatteryLifePercent * 100);
            BatteryPercent.Text = $"{percent}%";
            BatteryIcon.Text = percent switch
            {
                >= 90 => "\uEBAA",
                >= 60 => "\uEBA8",
                >= 30 => "\uEBA6",
                >= 10 => "\uEBA4",
                _ => "\uEBA2"
            };
            if (powerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online)
                BatteryIcon.Text = "\uEBB5";
        }
        catch { BatteryButton.Visibility = Visibility.Collapsed; }
    }

    // ─── カスタムカレンダー ──────────────────────────
    private void BuildCalendar()
    {
        CalendarDaysGrid.Children.Clear();
        var today = DateTime.Today;

        CalendarMonthLabel.Text = _calendarDisplayMonth.ToString("yyyy年 M月",
            System.Globalization.CultureInfo.GetCultureInfo("ja-JP"));

        // 月の最初の日の曜日（日曜=0）
        var firstDay = new DateTime(_calendarDisplayMonth.Year, _calendarDisplayMonth.Month, 1);
        int startDow = (int)firstDay.DayOfWeek;
        int daysInMonth = DateTime.DaysInMonth(_calendarDisplayMonth.Year, _calendarDisplayMonth.Month);

        // 前月の埋め
        int prevMonthDays = startDow;
        var prevMonth = firstDay.AddMonths(-1);
        int prevMonthTotal = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
        for (int i = 0; i < prevMonthDays; i++)
        {
            var day = prevMonthTotal - prevMonthDays + 1 + i;
            AddCalendarDay(day, "#33ffffff", false, false);
        }

        // 当月
        for (int d = 1; d <= daysInMonth; d++)
        {
            var date = new DateTime(_calendarDisplayMonth.Year, _calendarDisplayMonth.Month, d);
            bool isToday = date == today;
            bool isSunday = date.DayOfWeek == DayOfWeek.Sunday;
            bool isSaturday = date.DayOfWeek == DayOfWeek.Saturday;

            string fg = isToday ? "#FFFFFF"
                : isSunday ? "#FF7777"
                : isSaturday ? "#7799FF"
                : "#ccffffff";

            AddCalendarDay(d, fg, isToday, true);
        }

        // 次月の埋め（6行を埋める）
        int totalCells = prevMonthDays + daysInMonth;
        int remaining = (totalCells % 7 == 0) ? 0 : 7 - (totalCells % 7);
        for (int i = 1; i <= remaining; i++)
        {
            AddCalendarDay(i, "#33ffffff", false, false);
        }
    }

    private void AddCalendarDay(int day, string foreground, bool isToday, bool isCurrentMonth)
    {
        var tb = new TextBlock
        {
            Text = day.ToString(),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 12,
            Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(foreground)!,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2),
            FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal
        };

        if (isToday)
        {
            var container = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 58, 120, 220)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 1),
                Child = tb
            };
            tb.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            tb.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            CalendarDaysGrid.Children.Add(container);
        }
        else
        {
            var container = new Border
            {
                Height = 28,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                Child = tb
            };
            CalendarDaysGrid.Children.Add(container);
        }
    }

    private void CalendarPrev_Click(object sender, MouseButtonEventArgs e)
    {
        _calendarDisplayMonth = _calendarDisplayMonth.AddMonths(-1);
        BuildCalendar();
    }

    private void CalendarNext_Click(object sender, MouseButtonEventArgs e)
    {
        _calendarDisplayMonth = _calendarDisplayMonth.AddMonths(1);
        BuildCalendar();
    }

    // ─── 音量スライダー ──────────────────────────────
    private void UpdateVolumeUI()
    {
        _suppressVolumeEvent = true;
        try
        {
            var level = VolumeHelper.GetVolume();
            var muted = VolumeHelper.GetMute();
            var percent = (int)(level * 100);

            VolumeSlider.Value = percent;
            CC_VolumeSlider.Value = percent;
            VolumePercent.Text = muted ? "ミュート" : $"{percent}%";
            CC_VolumePercent.Text = muted ? "ミュート" : $"{percent}%";

            // アイコン更新
            string icon = muted ? "\uE74F"
                : percent == 0 ? "\uE992"
                : percent < 33 ? "\uE993"
                : percent < 66 ? "\uE994"
                : "\uE767";

            VolumeIcon.Text = icon;
            VolumePopupIcon.Text = icon;
        }
        catch { }
        finally { _suppressVolumeEvent = false; }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressVolumeEvent) return;
        var level = (float)(e.NewValue / 100.0);
        VolumeHelper.SetVolume(level);
        _suppressVolumeEvent = true;
        CC_VolumeSlider.Value = e.NewValue;
        _suppressVolumeEvent = false;
        VolumePercent.Text = $"{(int)e.NewValue}%";
        CC_VolumePercent.Text = $"{(int)e.NewValue}%";
        UpdateVolumeIcon((int)e.NewValue);
    }

    private void CC_VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressVolumeEvent) return;
        var level = (float)(e.NewValue / 100.0);
        VolumeHelper.SetVolume(level);
        _suppressVolumeEvent = true;
        VolumeSlider.Value = e.NewValue;
        _suppressVolumeEvent = false;
        VolumePercent.Text = $"{(int)e.NewValue}%";
        CC_VolumePercent.Text = $"{(int)e.NewValue}%";
        UpdateVolumeIcon((int)e.NewValue);
    }

    private void UpdateVolumeIcon(int percent)
    {
        string icon = percent == 0 ? "\uE992"
            : percent < 33 ? "\uE993"
            : percent < 66 ? "\uE994"
            : "\uE767";
        VolumeIcon.Text = icon;
        VolumePopupIcon.Text = icon;
    }

    private void VolumeMute_Click(object sender, MouseButtonEventArgs e)
    {
        VolumeHelper.ToggleMute();
        UpdateVolumeUI();
    }

    // ─── ホバーアニメーション ─────────────────────────
    internal void MenuBarItem_MouseEnter(object sender, WpfMouseEventArgs e)
    {
        if (sender is Border border)
        {
            var anim = new ColorAnimation(
                System.Windows.Media.Color.FromArgb(35, 255, 255, 255),
                new Duration(TimeSpan.FromMilliseconds(80)));
            // 既存の Brush が SolidColorBrush なら再利用、そうでなければ新規作成
            if (border.Background is not SolidColorBrush brush || brush.IsFrozen)
            {
                brush = new SolidColorBrush(Colors.Transparent);
                border.Background = brush;
            }
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }
    }

    internal void MenuBarItem_MouseLeave(object sender, WpfMouseEventArgs e)
    {
        if (sender is Border border)
        {
            var anim = new ColorAnimation(Colors.Transparent,
                new Duration(TimeSpan.FromMilliseconds(80)));
            if (border.Background is SolidColorBrush brush && !brush.IsFrozen)
                brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }
    }

    // ─── クリックイベント ────────────────────────────
    private void WindowsLogo_Click(object sender, MouseButtonEventArgs e)
        => NativeMethods.OpenStartMenu();

    private void Volume_Click(object sender, MouseButtonEventArgs e)
    {
        UpdateVolumeUI();
        VolumePopup.IsOpen = !VolumePopup.IsOpen;
    }

    private void Battery_Click(object sender, MouseButtonEventArgs e)
        => OpenWindowsSettings("ms-settings:batterysaver");

    private void Help_Click(object sender, MouseButtonEventArgs e)
    {
        var helpWin = new HelpWindow();
        helpWin.Show();
    }

    private async void Clock_Click(object sender, MouseButtonEventArgs e)
    {
        var now = DateTime.Now;
        PopupDateText.Text = now.ToString("yyyy年M月d日 dddd",
            System.Globalization.CultureInfo.GetCultureInfo("ja-JP"));
        PopupTimeText.Text = now.ToString("H:mm");

        _calendarDisplayMonth = new DateTime(now.Year, now.Month, 1);
        BuildCalendar();

        // 通知を非同期で読み込み
        CalendarPopup.IsOpen = !CalendarPopup.IsOpen;
        if (CalendarPopup.IsOpen)
        {
            await LoadNotificationsAsync();
        }
    }

    /// <summary>Windows通知を読み込んでUIに表示</summary>
    private async Task LoadNotificationsAsync()
    {
        try
        {
            var notifications = await NotificationHelper.GetNotificationsAsync();
            NotificationList.Children.Clear();

            if (notifications.Count == 0)
            {
                NotificationList.Children.Add(new Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Child = new TextBlock
                    {
                        Text = "通知はありません",
                        FontSize = 11.5,
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                    }
                });
                return;
            }

            var appGroups = notifications.GroupBy(x => x.AppName);

            foreach (var group in appGroups)
            {
                // グループのヘッダー（アプリ名）
                NotificationList.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(group.Key) ? "その他" : group.Key,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(4, 6, 0, 4)
                });

                foreach (var n in group)
                {
                    var card = CreateNotificationCard(n);
                    NotificationList.Children.Add(card);
                }
            }
        }
        catch
        {
            // 通知アクセス不可の場合
            NotificationList.Children.Clear();
            NotificationList.Children.Add(new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Child = new TextBlock
                {
                    Text = "通知へのアクセスを許可してください",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                }
            });
        }
    }

    /// <summary>通知カードUIを生成</summary>
    private Border CreateNotificationCard(NotificationInfo n)
    {
        var timeDiff = DateTime.Now - n.Timestamp;
        var timeAgo = timeDiff.TotalMinutes < 1 ? "たった今"
            : timeDiff.TotalMinutes < 60 ? $"{(int)timeDiff.TotalMinutes}分前"
            : timeDiff.TotalHours < 24 ? $"{(int)timeDiff.TotalHours}時間前"
            : $"{(int)timeDiff.TotalDays}日前";

        var content = new StackPanel();

        // 時間
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var timeText = new TextBlock
        {
            Text = timeAgo,
            FontSize = 9.5,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        DockPanel.SetDock(timeText, Dock.Right);
        header.Children.Add(timeText);
        content.Children.Add(header);

        // タイトル
        if (!string.IsNullOrEmpty(n.Title))
        {
            content.Children.Add(new TextBlock
            {
                Text = n.Title,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        // 本文
        if (!string.IsNullOrEmpty(n.Body))
        {
            content.Children.Add(new TextBlock
            {
                Text = n.Body,
                FontSize = 11,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 34,
                TextWrapping = TextWrapping.Wrap
            });
        }

        var border = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 4),
            Child = content,
            Cursor = System.Windows.Input.Cursors.Hand
        };

        // 通知クリックで該当アプリを起動
        if (!string.IsNullOrEmpty(n.AppUserModelId))
        {
            border.MouseLeftButtonUp += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $@"shell:AppsFolder\{n.AppUserModelId}",
                        UseShellExecute = true
                    });
                    CalendarPopup.IsOpen = false;
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"通知のアプリを開けませんでした。\nエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
        }

        return border;
    }

    private void ControlCenter_Click(object sender, MouseButtonEventArgs e)
    {
        UpdateVolumeUI();
        UpdateNetworkStatus();
        ControlCenterPopup.IsOpen = !ControlCenterPopup.IsOpen;
    }

    private void ControlCenterPopup_Opened(object sender, EventArgs e)
    {
        bool isYtActive = YoutubeTvWindowManager.IsYoutubeModeActive;
        CC_YoutubeController.Visibility = isYtActive ? Visibility.Visible : Visibility.Collapsed;
        CC_YoutubeSeparator.Visibility = isYtActive ? Visibility.Visible : Visibility.Collapsed;
    }
    private void YtUp_Click(object sender, MouseButtonEventArgs e) => YoutubeTvWindowManager.SendYouTubeKey("ArrowUp", "ArrowUp", 38);
    private void YtDown_Click(object sender, MouseButtonEventArgs e) => YoutubeTvWindowManager.SendYouTubeKey("ArrowDown", "ArrowDown", 40);
    private void YtLeft_Click(object sender, MouseButtonEventArgs e) => YoutubeTvWindowManager.SendYouTubeKey("ArrowLeft", "ArrowLeft", 37);
    private void YtRight_Click(object sender, MouseButtonEventArgs e) => YoutubeTvWindowManager.SendYouTubeKey("ArrowRight", "ArrowRight", 39);
    private void YtEnter_Click(object sender, MouseButtonEventArgs e) => YoutubeTvWindowManager.SendYouTubeKey("Enter", "Enter", 13);
    private void YtBack_Click(object sender, MouseButtonEventArgs e) => YoutubeTvWindowManager.SendYouTubeKey("Escape", "Escape", 27);
    private void ControlCenterPopup_Closed(object sender, EventArgs e)
    {
        CC_MusicPlayerPlaceholder.Visibility = Visibility.Collapsed;
        // ミュージックプレーヤーはPopupと独立して動作するため、ここではHideしない
    }

    // ─── コントロールセンター内 ──────────────────────
    private void CC_Wifi_Click(object sender, MouseButtonEventArgs e)
    {
        OpenWindowsSettings("ms-settings:network-wifi");
        ControlCenterPopup.IsOpen = false;
    }

    private void CC_Bluetooth_Click(object sender, MouseButtonEventArgs e)
    {
        OpenWindowsSettings("ms-settings:bluetooth");
        ControlCenterPopup.IsOpen = false;
    }

    private void CC_NightLight_Click(object sender, MouseButtonEventArgs e)
    {
        OpenWindowsSettings("ms-settings:nightlight");
        ControlCenterPopup.IsOpen = false;
    }

    private void CC_Display_Click(object sender, MouseButtonEventArgs e)
    {
        OpenWindowsSettings("ms-settings:display");
        ControlCenterPopup.IsOpen = false;
    }

    private void CC_TaskManager_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "taskmgr.exe",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"タスクマネージャーを開けませんでした。\nエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CC_Settings_Click(object sender, MouseButtonEventArgs e)
    {
        OpenWindowsSettings("ms-settings:");
        ControlCenterPopup.IsOpen = false;
    }

    // ─── ミュージックプレーヤー ──────────────────────
    private void CC_Music_Click(object sender, MouseButtonEventArgs e)
    {
        if (_musicPlayerWindow == null || !_musicPlayerWindow.IsLoaded)
        {
            _musicPlayerWindow = new MusicPlayerWindow(_musicSettings);
            _musicPlayerWindow.NowPlayingChanged += OnNowPlayingChanged;
            _musicPlayerWindow.OnPlayerHidden += (s, ev) =>
            {
                // 最小化時の処理（必要に応じて）
            };
            _musicPlayerWindow.Closed += (s, _) =>
            {
                OnNowPlayingChanged("", "", "");
                _musicPlayerWindow = null;
            };
        }

        // コントロールセンターPopupの位置を基準にフローティング表示位置を計算
        var ccButton = ControlCenterButton;
        double left = Left;  // メニューバーの左端をデフォルトに
        double top = 28;     // メニューバーの高さの下
        try
        {
            var pt = ccButton.PointToScreen(new System.Windows.Point(0, 0));
            var source = PresentationSource.FromVisual(ccButton);
            if (source?.CompositionTarget != null)
            {
                double dpiX = source.CompositionTarget.TransformToDevice.M11;
                double dpiY = source.CompositionTarget.TransformToDevice.M22;
                left = pt.X / dpiX - 200; // ボタンの左側にオフセット
                top = pt.Y / dpiY + 28;    // ボタンの下
            }
        }
        catch { }

        // Popupを閉じてからプレーヤーを独立ウィンドウとして表示
        ControlCenterPopup.IsOpen = false;

        if (_musicPlayerWindow.IsVisible)
        {
            _musicPlayerWindow.Hide();
        }
        else
        {
            _musicPlayerWindow.ShowAt(left, top);
        }
    }

    private void OnNowPlayingChanged(string service, string title, string thumbnailUrl)
    {
        Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrEmpty(title))
            {
                CC_NowPlayingPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                CC_NowPlayingPanel.Visibility = Visibility.Visible;
                CC_NowPlayingService.Text = service;
                CC_NowPlayingTitle.Text = title;
            }
        });
    }

    /// <summary>
    /// ミュージックサービス設定を更新（MainWindowから呼び出し）
    /// </summary>
    public void UpdateMusicSettings(MusicServiceSettings settings)
    {
        _musicSettings = settings;
        bool anyEnabled = settings.IsYouTubeEnabled
                       || settings.IsAmazonMusicEnabled
                       || settings.IsSpotifyEnabled;
        CC_MusicTile.Visibility = anyEnabled ? Visibility.Visible : Visibility.Collapsed;
        _musicPlayerWindow?.UpdateSettings(settings);
    }


    // ─── Quick AI パネル (Web) ─────────────────────────
    private void QuickAi_Click(object sender, MouseButtonEventArgs e)
    {
        var url = _quickAiService switch
        {
            "Gemini" => "https://gemini.google.com",
            "Claude" => "https://claude.ai",
            _ => "https://chatgpt.com"
        };

        if (_quickAiWindow == null || !_quickAiWindow.IsLoaded)
        {
            _quickAiWindow = new QuickAiPanelWindow(url, _quickAiService);
            _quickAiWindow.OnPanelHidden += (s, ev) =>
            {
                // 最小化時の処理
            };
            _quickAiWindow.Closed += (s, _) =>
            {
                _quickAiWindow = null;
            };
        }

        // ボタンの位置を基準にフローティング表示位置を計算
        var aiButton = QuickAiButton;
        double left = Left;
        double top = 28;
        try
        {
            var pt = aiButton.PointToScreen(new System.Windows.Point(0, 0));
            var source = PresentationSource.FromVisual(aiButton);
            if (source?.CompositionTarget != null)
            {
                double dpiX = source.CompositionTarget.TransformToDevice.M11;
                double dpiY = source.CompositionTarget.TransformToDevice.M22;
                left = pt.X / dpiX - 200;
                top = pt.Y / dpiY + 28;
            }
        }
        catch { }

        if (_quickAiWindow.IsVisible)
        {
            _quickAiWindow.Hide();
        }
        else
        {
            _quickAiWindow.UpdateService(url, _quickAiService);
            _quickAiWindow.ShowAt(left, top);
        }
    }


    /// <summary>
    /// Quick AI サービスを更新（MainWindowから呼び出し）
    /// </summary>
    public void UpdateQuickAiService(string serviceName)
    {
        _quickAiService = serviceName;
        var url = _quickAiService switch
        {
            "Gemini" => "https://gemini.google.com",
            "Claude" => "https://claude.ai",
            _ => "https://chatgpt.com"
        };
        _quickAiWindow?.UpdateService(url, _quickAiService);
    }

    // ─── ヘルパー ──────────────────────────────────────────
    private static void OpenWindowsSettings(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"設定を開けませんでした。\nエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─── オーディオデバイス切り替え ─────────────────────────────────
    private void ToggleAudioDevices_Click(object sender, MouseButtonEventArgs e)
    {
        if (AudioDevicesList.Visibility == Visibility.Visible)
        {
            AudioDevicesList.Visibility = Visibility.Collapsed;
            AudioDevicesToggleIcon.Text = "\xE70D"; // ChevronDown
        }
        else
        {
            AudioDevicesList.Visibility = Visibility.Visible;
            AudioDevicesToggleIcon.Text = "\xE70E"; // ChevronUp
            PopulateAudioDevicesList();
        }
    }

    private void PopulateAudioDevicesList()
    {
        AudioDevicesList.Children.Clear();
        var devices = VolumeHelper.GetAudioDevices();

        foreach (var device in devices)
        {
            if (device.IsDefault)
            {
                CurrentAudioDeviceName.Text = device.Name;
            }

            var itemPanel = new DockPanel { Margin = new Thickness(0, 4, 0, 4), Background = System.Windows.Media.Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            itemPanel.Tag = device.Id;
            itemPanel.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (s is FrameworkElement fe && fe.Tag is string id)
                {
                    VolumeHelper.SetDefaultAudioDevice(id);
                    PopulateAudioDevicesList(); // 選択後に再読み込みしてトグル更新
                    UpdateVolumeUI(); // ボリュームスライダーを新しいデバイスに合わせる
                }
            };

            var checkIcon = new TextBlock
            {
                Text = device.IsDefault ? "\xE73E" : "", // CheckMark
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 14,
                Margin = new Thickness(0, 0, 4, 0)
            };
            DockPanel.SetDock(checkIcon, Dock.Left);
            itemPanel.Children.Add(checkIcon);

            var nameText = new TextBlock
            {
                Text = device.Name,
                FontSize = 10,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    device.IsDefault ? System.Windows.Media.Color.FromRgb(255, 255, 255) : System.Windows.Media.Color.FromRgb(170, 170, 170)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            itemPanel.Children.Add(nameText);

            AudioDevicesList.Children.Add(itemPanel);
        }
    }

    // ─── 通知センター ──────────────────────────────────────
    private async void ClearAllNotifications_Click(object sender, MouseButtonEventArgs e)
    {
        await NotificationHelper.ClearAllNotificationsAsync();
        await LoadNotificationsAsync();
    }

    private void OpenNotificationCenter_Click(object sender, MouseButtonEventArgs e)
    {
        CalendarPopup.IsOpen = false;
        NativeMethods.OpenNotificationCenter();
    }

    // ─── 一時保存トレイ (Popup方式) ──────────────────────────────
    private void TempStorageTray_Click(object sender, MouseButtonEventArgs e)
    {
        TempStoragePopup.IsOpen = !TempStoragePopup.IsOpen;
        if (TempStoragePopup.IsOpen)
        {
            UpdateTempStoragePreview();
        }
    }

    private void TempStorageSaveFromClipboard_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (System.Windows.Clipboard.ContainsFileDropList())
            {
                var files = System.Windows.Clipboard.GetFileDropList();
                string[] fileArray = new string[files.Count];
                files.CopyTo(fileArray, 0);
                _tempStorageData = fileArray;
                UpdateTempStoragePreview();
            }
            else if (System.Windows.Clipboard.ContainsImage())
            {
                _tempStorageData = System.Windows.Clipboard.GetImage();
                UpdateTempStoragePreview();
            }
            else if (System.Windows.Clipboard.ContainsText())
            {
                _tempStorageData = System.Windows.Clipboard.GetText();
                UpdateTempStoragePreview();
            }
        }
        catch { }
    }

    private void TempStorageCopyToClipboard_Click(object sender, MouseButtonEventArgs e)
    {
        if (_tempStorageData == null) return;
        try
        {
            if (_tempStorageData is string[] files)
            {
                var dataObj = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, files);
                System.Windows.Clipboard.SetDataObject(dataObj, true);
            }
            else if (_tempStorageData is string text)
            {
                System.Windows.Clipboard.SetText(text);
            }
            else if (_tempStorageData is System.Windows.Interop.InteropBitmap interopBitmap)
            {
                System.Windows.Clipboard.SetImage(interopBitmap);
            }
            else if (_tempStorageData is System.Windows.Media.Imaging.BitmapSource bitmap)
            {
                System.Windows.Clipboard.SetImage(bitmap);
            }
            
            TempStoragePopup.IsOpen = false;
        }
        catch { }
    }

    private void TempStorageClear_Click(object sender, MouseButtonEventArgs e)
    {
        _tempStorageData = null;
        UpdateTempStoragePreview();
    }

    private void UpdateTempStoragePreview()
    {
        TempStorageEmptyText.Visibility = Visibility.Collapsed;
        TempStorageTextPreview.Visibility = Visibility.Collapsed;
        TempStorageImagePreview.Visibility = Visibility.Collapsed;
        TempStorageFileList.Visibility = Visibility.Collapsed;

        if (_tempStorageData == null)
        {
            TempStorageEmptyText.Visibility = Visibility.Visible;
            TempStorageIcon.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
            return;
        }

        TempStorageIcon.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));

        if (_tempStorageData is string[] files)
        {
            TempStorageFileList.Visibility = Visibility.Visible;
            TempStorageFileList.ItemsSource = files.Select(f => System.IO.Path.GetFileName(f)).ToList();
        }
        else if (_tempStorageData is string text)
        {
            TempStorageTextPreview.Visibility = Visibility.Visible;
            TempStorageTextPreview.Text = text;
        }
        else if (_tempStorageData is System.Windows.Media.ImageSource image)
        {
            TempStorageImagePreview.Visibility = Visibility.Visible;
            TempStorageImagePreview.Source = image;
        }
    }

    // ─── クリーンアップ ──────────────────────────────
    public void Cleanup()
    {
        _topmostTimer?.Stop();
        _clockTimer?.Stop();
        _activeWindowTimer?.Stop();
        _statusTimer?.Stop();
        _hwndSource?.RemoveHook(WndProc);
        
        // メモリリーク防止のためグローバルイベントから登録解除
        App.PluginHost.OnMenuItemAdded -= InjectMenuItem;
        App.PluginHost.OnTrayPopupViewAdded -= InjectTrayPopupView;

        UnregisterAsAppBar();
        try { _musicPlayerWindow?.Close(); } catch { }
        try { _quickAiWindow?.Close(); } catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        Cleanup();
        base.OnClosed(e);
    }
}
