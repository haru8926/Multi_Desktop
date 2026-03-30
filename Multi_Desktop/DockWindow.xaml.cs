using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Multi_Desktop.Models;
using Multi_Desktop.Services;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfPoint = System.Windows.Point;
using WpfImage = System.Windows.Controls.Image;
using WpfToolTip = System.Windows.Controls.ToolTip;

namespace Multi_Desktop;

/// <summary>
/// MacOS風 Dock ウィンドウのコードビハインド
/// </summary>
public partial class DockWindow : Window
{
    private readonly RunningAppService _runningAppService;
    private readonly TaskbarSettings _settings;
    private bool _isAutoHidden;
    private string _aiApiKey = "";
    private AiOperationWindow? _aiOperationWindow;
    private readonly Duration _animDuration = new(TimeSpan.FromMilliseconds(200));
    private readonly Duration _magnifyDuration = new(TimeSpan.FromMilliseconds(120));

    /// <summary>プレースホルダーアイコンのキャッシュ（毎回の RenderTargetBitmap 生成を防止）</summary>
    private static readonly Lazy<System.Windows.Media.ImageSource> _placeholderIcon = new(() =>
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRoundedRectangle(
                new SolidColorBrush(WpfColor.FromRgb(70, 70, 100)),
                null, new Rect(0, 0, 48, 48), 8, 8);
            ctx.DrawText(
                new FormattedText("?",
                    System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 24, WpfBrushes.White, 96),
                new WpfPoint(16, 8));
        }
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(48, 48, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    });

    // 画面下部ホットゾーン検出用
    private System.Windows.Threading.DispatcherTimer? _hotZoneTimer;

    /// <summary>設定ボタンが押されたときに呼び出されるイベント</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>ピン留めアプリが変更されたときに呼び出されるイベント</summary>
    public event Action<List<string>>? PinnedAppsChanged;

    public DockWindow(TaskbarSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _runningAppService = new RunningAppService(settings.PinnedApps);

        Loaded += DockWindow_Loaded;
        MouseLeave += DockWindow_MouseLeave;
        MouseEnter += DockWindow_MouseEnter;
    }

    // ─── 初期化 ──────────────────────────────────────
    private void DockWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 設定ボタンのサイズをアイコンサイズに合わせる
        ApplySettingsButtonSize();

        // 設定ボタンイベント
        SetupSettingsButton();

        PositionDock();
        _runningAppService.ItemsUpdated += (_, _) =>
            Dispatcher.Invoke(() => { RebuildDockIcons(); UpdateDockWidth(GetCurrentLogicalScreenBounds()); });

        // ピン留め変更時の完全リロード
        _runningAppService.DockReloadRequested += (_, _) =>
            Dispatcher.Invoke(() => { RebuildDockIcons(); UpdateDockWidth(GetCurrentLogicalScreenBounds()); });

        _runningAppService.Start();

        // カスタムDockモードでは常に自動非表示
        // 初回は一度表示してから数秒後に非表示にする
        StartHotZoneDetection();
        StartInitialAutoHide();
    }

    /// <summary>設定ボタンのサイズをDockアイコンサイズに合わせる</summary>
    private void ApplySettingsButtonSize()
    {
        var size = _settings.DockIconSize;
        SettingsButtonContainer.Width = size + 4;
        SettingsButtonBorder.Width = size;
        SettingsButtonBorder.Height = size;
        SettingsButtonIcon.FontSize = size * 0.42;
        SettingsSeparator.Height = size * 0.55;

        // AIボタンも同様にサイズ合わせする
        AiButtonContainer.Width = size + 4;
        AiButtonBorder.Width = size;
        AiButtonBorder.Height = size;
        AiSeparator.Height = size * 0.55;
        YoutubeButtonContainer.Width = size + 4;
        YoutubeButtonBorder.Width = size;
        YoutubeButtonBorder.Height = size;
        YoutubeButtonIcon.FontSize = size * 0.42;
        YoutubeSeparator.Height = size * 0.55;
    }

    /// <summary>設定とAIボタンのイベントおよびホバーアニメーションを設定</summary>
    private void SetupSettingsButton()
    {
        SettingsButtonContainer.MouseLeftButtonUp += (_, _) =>
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        };

        // ホバーでアイコン拡大
        SettingsButtonContainer.MouseEnter += (_, _) => AnimateScale(SettingsButtonBorder, 1.2, _magnifyDuration);
        SettingsButtonContainer.MouseLeave += (_, _) => AnimateScale(SettingsButtonBorder, 1.0, _magnifyDuration);

        // AIボタン
        AiButtonContainer.MouseLeftButtonUp += AiButton_Click;
        AiButtonContainer.MouseEnter += (_, _) => AnimateScale(AiButtonBorder, 1.2, _magnifyDuration);
        AiButtonContainer.MouseLeave += (_, _) => AnimateScale(AiButtonBorder, 1.0, _magnifyDuration);
        YoutubeButtonContainer.MouseLeftButtonUp += YoutubeButton_Click;
        YoutubeButtonContainer.MouseEnter += (_, _) => AnimateScale(YoutubeButtonBorder, 1.2, _magnifyDuration);
        YoutubeButtonContainer.MouseLeave += (_, _) => AnimateScale(YoutubeButtonBorder, 1.0, _magnifyDuration);
    }
    private void YoutubeButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (YoutubeTvWindowManager.IsYoutubeModeActive)
        {
            // 既に起動している場合は選択肢(ContextMenu)を表示する
            var contextMenu = new System.Windows.Controls.ContextMenu();

            var menuFull = new System.Windows.Controls.MenuItem { Header = "全画面モード" };
            menuFull.Click += (s, ev) => YoutubeTvWindowManager.ChangeMode(YoutubeMode.FullScreen);

            var menuBg = new System.Windows.Controls.MenuItem { Header = "背景モード" };
            menuBg.Click += (s, ev) => YoutubeTvWindowManager.ChangeMode(YoutubeMode.Background);

            var menuClose = new System.Windows.Controls.MenuItem { Header = "終了" };
            menuClose.Click += (s, ev) => YoutubeTvWindowManager.CloseAllWindows();

            contextMenu.Items.Add(menuFull);
            contextMenu.Items.Add(menuBg);
            contextMenu.Items.Add(new System.Windows.Controls.Separator());
            contextMenu.Items.Add(menuClose);

            // ボタンのすぐ下にメニューを表示
            contextMenu.PlacementTarget = sender as System.Windows.UIElement;
            contextMenu.IsOpen = true;
        }
        else
        {
            // 起動していなければ通常通り全画面で起動
            YoutubeTvWindowManager.ShowAllWindows();
        }
    }
    private void AiButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_aiOperationWindow == null || !_aiOperationWindow.IsLoaded)
        {
            _aiOperationWindow = new AiOperationWindow(_aiApiKey);
            _aiOperationWindow.Closed += (s, ev) => _aiOperationWindow = null;
        }

        if (_aiOperationWindow.IsVisible)
        {
            _aiOperationWindow.Hide();
        }
        else
        {
            // 画面の右下に配置 (右端から20px, Dockの少し上)
            var p = GetCurrentLogicalScreenBounds();
            var left = p.Right - _aiOperationWindow.Width - 20;
            var top = p.Bottom - this.Height - _aiOperationWindow.Height - 10;
            _aiOperationWindow.ShowAt(left, top);
        }
    }

    public void UpdateAiApiKey(string key)
    {
        _aiApiKey = key;
        _aiOperationWindow?.UpdateApiKey(key);
    }

    /// <summary>現在のマウス位置があるディスプレイの論理座標を取得する</summary>
    private Rect GetCurrentLogicalScreenBounds()
    {
        var p = System.Windows.Forms.Cursor.Position;
        return Helpers.NativeMethods.GetLogicalScreenBounds(new System.Windows.Point(p.X, p.Y));
    }

    private Rect _currentDockBounds; // 表示をトリガーされたモニタの境界を保持

    /// <summary>Dockを現在画面の下部中央に配置（初期表示用）</summary>
    private void PositionDock()
    {
        var bounds = GetCurrentLogicalScreenBounds();
        _currentDockBounds = bounds;
        SizeToContent = SizeToContent.Width;
        UpdateDockWidth(bounds);
        Top = bounds.Bottom - Height;
    }

    /// <summary>アプリ数に応じてDockの横幅を動的に調整し、対象モニタの中央へ配置</summary>
    private void UpdateDockWidth(Rect bounds)
    {
        var screenW = bounds.Width;
        var margin = 40.0;
        var maxW = screenW - margin * 2;

        DockBackground.MaxWidth = maxW;
        MaxWidth = screenW;

        Dispatcher.InvokeAsync(() =>
        {
            UpdateLayout();
            var actualW = ActualWidth > 0 ? ActualWidth : Width;
            if (actualW > maxW) actualW = maxW;
            Left = bounds.Left + (screenW - actualW) / 2;
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    /// <summary>初回表示後3秒で自動非表示</summary>
    private void StartInitialAutoHide()
    {
        var initTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        initTimer.Tick += (_, _) =>
        {
            initTimer.Stop();
            AutoHideDock();
        };
        initTimer.Start();
    }

    // ─── アイコン表示構築 ────────────────────────────
    private void RebuildDockIcons()
    {
        DockIconPanel.Children.Clear();

        bool addedPinned = false;
        bool hasRunningNonPinned = false;

        foreach (var item in _runningAppService.DockItems)
        {
            if (item.IsPinned)
            {
                addedPinned = true;
                DockIconPanel.Children.Add(CreateDockIcon(item));
            }
        }

        foreach (var item in _runningAppService.DockItems)
        {
            if (!item.IsPinned && item.IsRunning)
            {
                if (addedPinned && !hasRunningNonPinned)
                {
                    DockIconPanel.Children.Add(CreateSeparator());
                    hasRunningNonPinned = true;
                }
                DockIconPanel.Children.Add(CreateDockIcon(item));
            }
        }
    }

    /// <summary>Dockアイコン要素を作成</summary>
    private UIElement CreateDockIcon(DockAppItem item)
    {
        var size = _settings.DockIconSize;

        var image = new WpfImage
        {
            Source = item.Icon ?? _placeholderIcon.Value,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            RenderTransformOrigin = new WpfPoint(0.5, 0.5)
        };

        var container = new Grid
        {
            Width = size + 8,
            Margin = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Tag = item
        };

        var iconBorder = new Border
        {
            Child = image,
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Background = WpfBrushes.Transparent,
            RenderTransformOrigin = new WpfPoint(0.5, 1.0),
            RenderTransform = new ScaleTransform(1, 1),
            VerticalAlignment = VerticalAlignment.Bottom
        };

        container.Children.Add(iconBorder);

        // 実行中インジケータードット
        if (item.IsRunning)
        {
            var dot = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(WpfColor.FromRgb(220, 220, 240)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -3),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 4,
                    ShadowDepth = 0,
                    Color = Colors.White,
                    Opacity = 0.7
                }
            };
            container.Children.Add(dot);
        }

        // ツールチップ (プレビュー風)
        var toolTipContent = new StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical, MaxWidth = 350 };
        
        var headerPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0,0,0,4) };
        headerPanel.Children.Add(new WpfImage 
        { 
            Source = item.Icon, 
            Width = 24, Height = 24, 
            Margin = new Thickness(0,0,12,0),
            VerticalAlignment = VerticalAlignment.Center
        });
        headerPanel.Children.Add(new TextBlock 
        { 
            Text = item.Name, 
            Foreground = WpfBrushes.White,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });
        toolTipContent.Children.Add(headerPanel);

        var previewImage = new WpfImage
        {
            Stretch = Stretch.Uniform,
            MaxHeight = 150,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0)
        };
        toolTipContent.Children.Add(previewImage);

        var toolTip = new WpfToolTip
        {
            Content = toolTipContent,
            Style = (Style)FindResource("ModernPreviewToolTip"),
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
            HasDropShadow = false
        };
        ToolTipService.SetToolTip(container, toolTip);
        ToolTipService.SetInitialShowDelay(container, 400);

        if (item.IsRunning && item.WindowHandle != IntPtr.Zero)
        {
            toolTip.Opened += async (_, _) =>
            {
                var handle = item.WindowHandle;
                // UIスレッドをブロックしないよう別スレッドでキャプチャ
                var snapshot = await Task.Run(() => Helpers.NativeMethods.GetWindowSnapshot(handle));
                if (snapshot != null)
                {
                    previewImage.Source = snapshot;
                    previewImage.Visibility = Visibility.Visible;
                }
            };
            toolTip.Closed += (_, _) =>
            {
                previewImage.Source = null;
                previewImage.Visibility = Visibility.Collapsed;
            };
        }

        // 拡大アニメーション（魚眼効果）
        container.MouseEnter += (_, _) =>
        {
            var scale = _settings.MagnificationFactor;
            AnimateScale(iconBorder, scale, _magnifyDuration);
            ApplyNeighborMagnification(container);
        };

        container.MouseLeave += (_, _) =>
        {
            AnimateScale(iconBorder, 1.0, _magnifyDuration);
            ResetAllMagnification();
        };

        // クリック: アプリ起動/前面表示/選択
        container.MouseLeftButtonUp += (_, e) =>
        {
            if (item.IsRunning && item.Windows.Count > 1)
            {
                var menu = new ContextMenu { Style = (Style)FindResource("ModernContextMenu") };

                var titleItem = new MenuItem 
                { 
                    Header = "ウィンドウを選択", 
                    Style = (Style)FindResource("ModernMenuItem"),
                    IsEnabled = false,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(200, 200, 220))
                };
                menu.Items.Add(titleItem);
                menu.Items.Add(new Separator { Background = new SolidColorBrush(WpfColor.FromArgb(40, 255, 255, 255)), Margin = new Thickness(6, 2, 6, 2) });

                foreach (var win in item.Windows)
                {
                    var winItem = new MenuItem
                    {
                        Header = string.IsNullOrWhiteSpace(win.Title) ? "名称未設定ウィンドウ" : win.Title,
                        Style = (Style)FindResource("ModernMenuItem")
                    };
                    winItem.Click += (s, args) =>
                    {
                        Helpers.NativeMethods.BringWindowToFront(win.Handle);
                    };
                    menu.Items.Add(winItem);
                }

                menu.PlacementTarget = container;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                menu.IsOpen = true;
                e.Handled = true;
            }
            else if (item.IsRunning && item.Windows.Count == 1)
            {
                Helpers.NativeMethods.BringWindowToFront(item.Windows[0].Handle);
            }
            else if (item.IsRunning && item.WindowHandle != IntPtr.Zero)
            {
                // Fallback
                Helpers.NativeMethods.BringWindowToFront(item.WindowHandle);
            }
            else if (!string.IsNullOrEmpty(item.ExePath) && System.IO.File.Exists(item.ExePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = item.ExePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"アプリケーションを起動できませんでした。\nエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        };

        // 右クリックメニュー
        container.MouseRightButtonUp += (_, e) =>
        {
            ShowDockContextMenu(item, container);
            e.Handled = true;
        };
        return container;
    }

    /// <summary>右クリックメニューを表示</summary>
    private void ShowDockContextMenu(DockAppItem item, FrameworkElement targetElement)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("ModernContextMenu") };

        // アプリ名/ショートカット表示
        var titleItem = new MenuItem 
        { 
            Header = item.Name, 
            Style = (Style)FindResource("ModernMenuItem"),
            IsEnabled = false,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(200, 200, 220))
        };
        menu.Items.Add(titleItem);

        menu.Items.Add(new Separator { Background = new SolidColorBrush(WpfColor.FromArgb(40, 255, 255, 255)), Margin = new Thickness(6,2,6,2) });

        if (!item.IsPinned)
        {
            var pinItem = new MenuItem { Header = "📌 タスクバーにピン留めする", Style = (Style)FindResource("ModernMenuItem") };
            pinItem.Click += (_, _) => { PinApp(item); };
            menu.Items.Add(pinItem);
        }
        else
        {
            var unpinItem = new MenuItem { Header = "📌 ピン留めを外す", Style = (Style)FindResource("ModernMenuItem") };
            unpinItem.Click += (_, _) => { UnpinApp(item); };
            menu.Items.Add(unpinItem);
        }

        if (item.IsRunning)
        {
            menu.Items.Add(new Separator { Background = new SolidColorBrush(WpfColor.FromArgb(40, 255, 255, 255)), Margin = new Thickness(6,2,6,2) });

            var closeItem = new MenuItem { Header = "❌ すべてのウィンドウを閉じる", Style = (Style)FindResource("ModernMenuItem") };
            closeItem.Click += (_, _) => { CloseAppWindows(item); };
            menu.Items.Add(closeItem);

            var killItem = new MenuItem { Header = "🚫 すべてのタスクを終了", Style = (Style)FindResource("ModernMenuItem") };
            killItem.Click += (_, _) => { KillAppProcesses(item); };
            menu.Items.Add(killItem);
        }
        else
        {
            menu.Items.Add(new Separator { Background = new SolidColorBrush(WpfColor.FromArgb(40, 255, 255, 255)), Margin = new Thickness(6,2,6,2) });

            var openItem = new MenuItem { Header = "🚀 開く", Style = (Style)FindResource("ModernMenuItem") };
            openItem.Click += (_, _) => 
            {
                if (!string.IsNullOrEmpty(item.ExePath) && System.IO.File.Exists(item.ExePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = item.ExePath, UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"新しいインスタンスを起動できませんでした。\nエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };
            menu.Items.Add(openItem);
        }

        menu.PlacementTarget = targetElement;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    private void PinApp(DockAppItem item)
    {
        if (!string.IsNullOrEmpty(item.ExePath) && !_settings.PinnedApps.Contains(item.ExePath))
        {
            _settings.PinnedApps.Add(item.ExePath);
            _runningAppService.UpdatePinnedApps(_settings.PinnedApps);
            PinnedAppsChanged?.Invoke(_settings.PinnedApps);
        }
    }

    private void UnpinApp(DockAppItem item)
    {
        if (!string.IsNullOrEmpty(item.ExePath) && _settings.PinnedApps.Contains(item.ExePath))
        {
            _settings.PinnedApps.Remove(item.ExePath);
            _runningAppService.UpdatePinnedApps(_settings.PinnedApps);
            PinnedAppsChanged?.Invoke(_settings.PinnedApps);
        }
    }

    private void CloseAppWindows(DockAppItem item)
    {
        try
        {
            if (item.Windows.Count > 0)
            {
                foreach (var win in item.Windows)
                {
                    Helpers.NativeMethods.PostMessage(win.Handle, 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero);
                }
            }
            else if (item.WindowHandle != IntPtr.Zero)
            {
                Helpers.NativeMethods.PostMessage(item.WindowHandle, 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch { }
    }

    private void KillAppProcesses(DockAppItem item)
    {
        try
        {
            if (!string.IsNullOrEmpty(item.ExePath))
            {
                var exeName = System.IO.Path.GetFileNameWithoutExtension(item.ExePath);
                var procs = Process.GetProcessesByName(exeName);
                foreach (var p in procs)
                {
                    try { p.Kill(); } catch { }
                    finally { p.Dispose(); }
                }
            }
        }
        catch { }
    }

    /// <summary>区切り線を作成</summary>
    private UIElement CreateSeparator()
    {
        return new Border
        {
            Width = 2,
            Height = _settings.DockIconSize * 0.6,
            Background = new SolidColorBrush(WpfColor.FromArgb(80, 200, 200, 220)),
            CornerRadius = new CornerRadius(1),
            Margin = new Thickness(6, 0, 6, 8),
            VerticalAlignment = VerticalAlignment.Bottom
        };
    }

    // CreatePlaceholderIcon は static readonly _placeholderIcon に置き換え済み

    // ─── 拡大アニメーション ──────────────────────────
    private void AnimateScale(Border border, double targetScale, Duration duration)
    {
        if (border.RenderTransform is not ScaleTransform transform)
        {
            transform = new ScaleTransform(1, 1);
            border.RenderTransform = transform;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animX = new DoubleAnimation(targetScale, duration) { EasingFunction = easing };
        var animY = new DoubleAnimation(targetScale, duration) { EasingFunction = easing };

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
    }

    /// <summary>隣接アイコンの段階的拡大（魚眼効果）</summary>
    private void ApplyNeighborMagnification(Grid hoveredContainer)
    {
        var idx = -1;
        var iconBorders = new List<(int index, Border border)>();

        for (int i = 0; i < DockIconPanel.Children.Count; i++)
        {
            if (DockIconPanel.Children[i] == hoveredContainer)
                idx = i;

            if (DockIconPanel.Children[i] is Grid g &&
                g.Children.Count > 0 &&
                g.Children[0] is Border b)
            {
                iconBorders.Add((i, b));
            }
        }

        if (idx < 0) return;

        foreach (var (i, border) in iconBorders)
        {
            if (DockIconPanel.Children[i] == hoveredContainer) continue;

            var distance = Math.Abs(i - idx);
            double neighborScale = distance switch
            {
                1 => 1.0 + (_settings.MagnificationFactor - 1.0) * 0.5,
                2 => 1.0 + (_settings.MagnificationFactor - 1.0) * 0.2,
                _ => 1.0
            };

            AnimateScale(border, neighborScale, _magnifyDuration);
        }
    }

    /// <summary>全アイコンのスケールをリセット</summary>
    private void ResetAllMagnification()
    {
        for (int i = 0; i < DockIconPanel.Children.Count; i++)
        {
            if (DockIconPanel.Children[i] is Grid g &&
                g.Children.Count > 0 &&
                g.Children[0] is Border b)
            {
                AnimateScale(b, 1.0, _magnifyDuration);
            }
        }
    }

    // ─── 自動非表示（タイマーベース：クールダウン付き） ──────
    private bool _mouseIsOverDock;
    private DateTime _lastShowTime = DateTime.MinValue;
    private int _outsideTickCount;                   // Dock外にいるTick数をカウント
    private const int ShowCooldownMs = 600;          // 表示後の非表示禁止期間
    private const int HideAfterTicks = 5;            // 連続でDock外にいるTick数で非表示（80ms × 5 = 400ms）

    private void DockWindow_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // タイマーベースで処理するため何もしない
    }

    private void DockWindow_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // タイマーベースで処理するため何もしない
    }

    private void AutoHideDock()
    {
        if (_isAutoHidden) return;

        var bounds = _currentDockBounds; // 現在Dockが存在するディスプレイ情報を基準にする
        var mousePos = GetLogicalMousePosition();
        if (IsMouseOverDockArea(mousePos, bounds))
        {
            _mouseIsOverDock = true;
            _outsideTickCount = 0;
            return;
        }

        _isAutoHidden = true;

        var anim = new DoubleAnimation
        {
            To = bounds.Bottom + 10,
            Duration = _animDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        BeginAnimation(TopProperty, anim);
    }

    private void AutoShowDock(Rect bounds)
    {
        if (!_isAutoHidden) return;
        _isAutoHidden = false;
        _lastShowTime = DateTime.Now;
        _mouseIsOverDock = true;
        _outsideTickCount = 0;
        _currentDockBounds = bounds; // 表示中の基準モニタ境界をロックする

        UpdateDockWidth(bounds);

        var targetTop = bounds.Bottom - Height;
        var anim = new DoubleAnimation
        {
            To = targetTop,
            Duration = _animDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(TopProperty, anim);
    }

    /// <summary>DPIスケールを考慮した論理マウス座標を取得する</summary>
    private System.Windows.Point GetLogicalMousePosition()
    {
        var p = System.Windows.Forms.Cursor.Position;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            return source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(p.X, p.Y));
        }
        return new System.Windows.Point(p.X, p.Y);
    }

    /// <summary>マウスがDock領域内にあるか判定（余裕を持たせた広い範囲）</summary>
    private bool IsMouseOverDockArea(System.Windows.Point mousePos, Rect bounds)
    {
        var dockLeft = Left - 50;
        var dockTop = bounds.Bottom - Height - 60;
        var dockRight = Left + ActualWidth + 50;
        var dockBottom = bounds.Bottom + 10;

        return mousePos.X >= dockLeft && mousePos.X <= dockRight
            && mousePos.Y >= dockTop && mousePos.Y <= dockBottom;
    }

    private void StartHotZoneDetection()
    {
        _hotZoneTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150) // 初期は非表示状態の間隔
        };
        _hotZoneTimer.Tick += (_, _) =>
        {
            bool isFullScreen = Helpers.NativeMethods.IsForegroundFullScreen();

            // ★ Youtubeモード実行中は、フルスクリーンであっても強制的にDockを隠さない ★
            if (YoutubeTvWindowManager.IsYoutubeModeActive)
            {
                isFullScreen = false;
            }

            this.Visibility = isFullScreen ? Visibility.Collapsed : Visibility.Visible;
            if (!isFullScreen)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    Helpers.NativeMethods.EnforceTopmost(hwnd);
                }
            }

            var mousePos = GetLogicalMousePosition();
            var bounds = GetCurrentLogicalScreenBounds();

            if (_isAutoHidden)
            {
                // 非表示中は常にマウスがあるモニタ基準で判定
                if (_hotZoneTimer!.Interval.TotalMilliseconds < 140)
                    _hotZoneTimer.Interval = TimeSpan.FromMilliseconds(150);

                if (mousePos.Y >= bounds.Bottom - 5)
                {
                    AutoShowDock(bounds);
                }
            }
            else
            {
                // 表示中はターゲットとしていたモニタ基準（1番ディスプレイなどへの座標引っ張られ防止）
                bounds = _currentDockBounds;
                // 表示中はポーリング間隔を短く（80ms）
                if (_hotZoneTimer!.Interval.TotalMilliseconds > 100)
                    _hotZoneTimer.Interval = TimeSpan.FromMilliseconds(80);

                if ((DateTime.Now - _lastShowTime).TotalMilliseconds < ShowCooldownMs)
                {
                    _outsideTickCount = 0;
                    return;
                }

                if (IsMouseOverDockArea(mousePos, bounds))
                {
                    _mouseIsOverDock = true;
                    _outsideTickCount = 0;
                }
                else
                {
                    _mouseIsOverDock = false;
                    _outsideTickCount++;

                    if (_outsideTickCount >= HideAfterTicks)
                    {
                        _outsideTickCount = 0;
                        AutoHideDock();
                    }
                }
            }
        };
        _hotZoneTimer.Start();
    }

    // ─── クリーンアップ ──────────────────────────────
    public void Cleanup()
    {
        _hotZoneTimer?.Stop();
        _runningAppService.Stop();
        _runningAppService.Dispose();
    }

    protected override void OnClosed(EventArgs e)
    {
        Cleanup();
        base.OnClosed(e);
    }
}
