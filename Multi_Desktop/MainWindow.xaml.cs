using System.Linq;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Multi_Desktop.Models;
using Multi_Desktop.Services;
using NHotkey;
using NHotkey.Wpf;
using Forms = System.Windows.Forms;

namespace Multi_Desktop;

/// <summary>
/// MainWindow のコードビハインド
/// </summary>
public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly DesktopSwitchService _switchService = new();
    private readonly TaskbarService _taskbarService = new();
    private AppSettings _settings = new();
    private bool _isSwitching;
    private bool _isReallyClosing;

    // システムトレイ
    private Forms.NotifyIcon? _notifyIcon;

    // MacOS Dock
    private DockWindow? _dockWindow;

    // macOS風メニューバー（各ディスプレイごと）
    private readonly List<MenuBarWindow> _menuBarWindows = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            // 誤って最小化された場合はすぐに元に戻す
            WindowState = WindowState.Normal;
        }
    }

    // ─── 初期化 ──────────────────────────────────────
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 設定を読み込み
        _settings = await _settingsService.LoadAsync();

        // データフォルダ自動作成
        DesktopSwitchService.EnsureDataFoldersExist(_settings);

        // 現在のモードを検出
        var detectedMode = DesktopSwitchService.DetectCurrentMode(_settings);
        if (detectedMode.HasValue)
        {
            _settings.CurrentMode = detectedMode.Value;
        }

        UpdateModeDisplay();

        // システムトレイアイコンを初期化
        InitializeNotifyIcon();

        // 引数に --minimized があればトレイに最小化
        if (Environment.GetCommandLineArgs().Contains("--minimized"))
        {
            MinimizeToTray();
        }

        // グローバルホットキー登録 (Ctrl+Shift+S)
        try
        {
            HotkeyManager.Current.AddOrReplace("ToggleDesktop",
                Key.S, ModifierKeys.Control | ModifierKeys.Shift, OnHotkeyPressed);
            StatusText.Text = "準備完了 — ホットキー登録済み";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"ホットキー登録失敗: {ex.Message}";
        }

        // 現在のモードに応じてDockを表示
        ApplyDockForCurrentMode();

        // AI アシスタント設定をメニューバーに反映
        foreach (var mb in _menuBarWindows)
        {
            mb.UpdateAiApiKey(_settings.GeminiApiKey);
            mb.UpdateQuickAiService(_settings.QuickAiService);
        }
    }

    // ─── Dock管理 ─────────────────────────────────────
    /// <summary>現在のモードに応じてDockの表示/非表示を切り替える</summary>
    private void ApplyDockForCurrentMode()
    {
        var taskbarSettings = _settings.GetTaskbarSettings(_settings.CurrentMode);

        if (taskbarSettings.Style == TaskbarStyle.MacOSDock)
        {
            ShowDock(taskbarSettings);
        }
        else
        {
            HideDock();
        }
    }

    /// <summary>MacOS風Dockを表示（Windowsタスクバーを非表示にする）</summary>
    private void ShowDock(TaskbarSettings settings)
    {
        // 既存のDockがあれば閉じる
        if (_dockWindow != null)
        {
            _dockWindow.Cleanup();
            _dockWindow.Close();
            _dockWindow = null;
        }

        // 既存のメニューバーをすべて閉じる
        CloseAllMenuBars();

        // Windowsタスクバーを非表示
        _taskbarService.HideTaskbar();

        // Dockウィンドウを生成・表示
        _dockWindow = new DockWindow(settings);
        _dockWindow.SettingsRequested += (_, _) =>
        {
            // MainWindowを表示して設定画面を開く
            ShowFromTray();
            SettingsButton_Click(this, new RoutedEventArgs());
        };
        _dockWindow.PinnedAppsChanged += async (pinned) =>
        {
            var targetSettings = _settings.GetTaskbarSettings(_settings.CurrentMode);
            var copyList = new List<string>(pinned);
            targetSettings.PinnedApps.Clear();
            targetSettings.PinnedApps.AddRange(copyList);
            await _settingsService.SaveAsync(_settings);
        };
        _dockWindow.Show();

        // 全ディスプレイにmacOS風メニューバーを生成・表示
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var menuBar = new MenuBarWindow(screen);
            menuBar.Show();
            _menuBarWindows.Add(menuBar);
        }

        // ミュージックサービス設定を反映
        foreach (var mb in _menuBarWindows)
            mb.UpdateMusicSettings(_settings.MusicSettings);

        // AI アシスタント設定をメニューバーに反映
        foreach (var mb in _menuBarWindows)
        {
            mb.UpdateAiApiKey(_settings.GeminiApiKey);
            mb.UpdateQuickAiService(_settings.QuickAiService);
        }
    }

    /// <summary>Dockを非表示にし、Windowsタスクバーを復元する</summary>
    private void HideDock()
    {
        if (_dockWindow != null)
        {
            _dockWindow.Cleanup();
            _dockWindow.Close();
            _dockWindow = null;
        }

        // メニューバーをすべて閉じる
        CloseAllMenuBars();

        // Windowsタスクバーを復元
        _taskbarService.ShowTaskbar();
    }

    /// <summary>全メニューバーウィンドウをクリーンアップして閉じる</summary>
    private void CloseAllMenuBars()
    {
        foreach (var menuBar in _menuBarWindows)
        {
            menuBar.Cleanup();
            menuBar.Close();
        }
        _menuBarWindows.Clear();
    }

    // ─── システムトレイ ──────────────────────────────
    private void InitializeNotifyIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Multi Desktop Switcher",
            Icon = SystemIcons.Application,
            Visible = false
        };

        // ダブルクリックでウィンドウ表示
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();

        // 右クリックメニュー
        var contextMenu = new Forms.ContextMenuStrip();

        var showItem = new Forms.ToolStripMenuItem("ウィンドウを表示");
        showItem.Click += (_, _) => ShowFromTray();
        contextMenu.Items.Add(showItem);

        var toggleItem = new Forms.ToolStripMenuItem("モード切り替え (Ctrl+Shift+S)");
        toggleItem.Click += async (_, _) => await ToggleModeAsync();
        contextMenu.Items.Add(toggleItem);

        contextMenu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("終了");
        exitItem.Click += (_, _) =>
        {
            _isReallyClosing = true;
            Close();
        };
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (_notifyIcon != null) _notifyIcon.Visible = false;
    }

    private void MinimizeToTray()
    {
        Hide();
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = true;
            _notifyIcon.ShowBalloonTip(2000, "Multi Desktop",
                "バックグラウンドで動作中です。\nCtrl+Shift+S でモード切替できます。",
                Forms.ToolTipIcon.Info);
        }
    }

    // ─── ウィンドウ閉じ処理 ──────────────────────────
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isReallyClosing)
        {
            // ×ボタン → トレイに最小化
            e.Cancel = true;
            MinimizeToTray();
            return;
        }

        // 本当に閉じる場合：リソース解放
        try { HotkeyManager.Current.Remove("ToggleDesktop"); } catch { }

        // Dockを閉じてタスクバーを必ず復元
        HideDock();
        _taskbarService.Dispose();

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }

    // ─── ホットキー ──────────────────────────────────
    private async void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
    {
        await ToggleModeAsync();
        e.Handled = true;
    }

    // ─── ボタンイベント ──────────────────────────────
    private async void SwitchToMainButton_Click(object sender, RoutedEventArgs e)
    {
        await SwitchToModeAsync(DesktopMode.Main);
    }

    private async void SwitchToSubButton_Click(object sender, RoutedEventArgs e)
    {
        await SwitchToModeAsync(DesktopMode.Sub);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_settings);
        if (settingsWindow.ShowDialog() == true)
        {
            _settings = settingsWindow.UpdatedSettings;
            UpdateModeDisplay();

            // Dock状態を再適用（設定変更が反映される）
            ApplyDockForCurrentMode();

            // ミュージックサービス設定をメニューバーに反映
            foreach (var mb in _menuBarWindows)
                mb.UpdateMusicSettings(_settings.MusicSettings);

            // AI アシスタント設定をメニューバーに反映
            foreach (var mb in _menuBarWindows)
            {
                mb.UpdateAiApiKey(_settings.GeminiApiKey);
                mb.UpdateQuickAiService(_settings.QuickAiService);
            }
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        // 確認なしで完全にアプリを終了する
        _isReallyClosing = true;
        Close();
    }

    // ─── 切り替えロジック ────────────────────────────
    private async Task ToggleModeAsync()
    {
        var nextMode = AppSettings.ToggleMode(_settings.CurrentMode);
        await SwitchToModeAsync(nextMode);
    }

    private async Task SwitchToModeAsync(DesktopMode mode)
    {
        if (_isSwitching) return;

        _isSwitching = true;
        SetButtonsEnabled(false);
        StatusText.Text = "切り替え中...";

        var result = await _switchService.SwitchToAsync(mode, _settings);

        _settings.CurrentMode = mode;
        await _settingsService.SaveAsync(_settings);

        // Dock状態を切り替え後のモードに適用
        ApplyDockForCurrentMode();

        StatusText.Text = result;
        UpdateModeDisplay();
        UpdateTrayTooltip();

        SetButtonsEnabled(true);
        _isSwitching = false;
    }

    // ─── UI更新 ──────────────────────────────────────
    private void UpdateModeDisplay()
    {
        if (_settings.CurrentMode == DesktopMode.Main)
        {
            ModeText.Text = "MAIN";
            ModeText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 180, 255));
            ModeDescription.Text = _settings.MainFolderPath;
            ModeIndicatorBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 32, 62));
        }
        else
        {
            ModeText.Text = "SUB";
            ModeText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 120, 255));
            ModeDescription.Text = _settings.SubFolderPath;
            ModeIndicatorBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 20, 62));
        }

        // タスクバー状態の表示
        var tbSettings = _settings.GetTaskbarSettings(_settings.CurrentMode);
        var tbStatus = tbSettings.Style == TaskbarStyle.MacOSDock ? "🍎 Dock" : "📌 標準";
        ModeDescription.Text += $"  |  タスクバー: {tbStatus}";
    }

    private void UpdateTrayTooltip()
    {
        if (_notifyIcon != null)
        {
            var modeName = _settings.CurrentMode == DesktopMode.Main ? "Main" : "Sub";
            _notifyIcon.Text = $"Multi Desktop — {modeName} モード";
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        SwitchToMainButton.IsEnabled = enabled;
        SwitchToSubButton.IsEnabled = enabled;
        SettingsButton.IsEnabled = enabled;
    }
}