using System.Diagnostics;
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

        var source = PresentationSource.FromVisual(this);
        var dpiScale = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
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
        var physicalRect = new Rect(_targetScreen.Bounds.X, _targetScreen.Bounds.Y,
            _targetScreen.Bounds.Width, _targetScreen.Bounds.Height);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            var transform = source.CompositionTarget.TransformFromDevice;
            return new Rect(
                transform.Transform(physicalRect.TopLeft),
                transform.Transform(physicalRect.BottomRight));
        }
        return physicalRect;
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
            if (NativeMethods.IsForegroundFullScreen()) return;

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
                catch { }
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
        // 必要な初期化処理
    }

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
        catch { }
        ControlCenterPopup.IsOpen = false;
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

    // ─── ヘルパー ──────────────────────────────────────────
    private static void OpenWindowsSettings(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
        }
        catch { }
    }

    // ─── 通知センター ──────────────────────────────────────
    private async void ClearAllNotifications_Click(object sender, MouseButtonEventArgs e)
    {
        NotificationHelper.ClearAllNotifications();
        await LoadNotificationsAsync();
    }

    private void OpenNotificationCenter_Click(object sender, MouseButtonEventArgs e)
    {
        CalendarPopup.IsOpen = false;
        NativeMethods.OpenNotificationCenter();
    }

    // ─── クリーンアップ ──────────────────────────────
    public void Cleanup()
    {
        _topmostTimer?.Stop();
        _clockTimer?.Stop();
        _activeWindowTimer?.Stop();
        _statusTimer?.Stop();
        _hwndSource?.RemoveHook(WndProc);
        UnregisterAsAppBar();
        try { _musicPlayerWindow?.Close(); } catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        Cleanup();
        base.OnClosed(e);
    }
}
