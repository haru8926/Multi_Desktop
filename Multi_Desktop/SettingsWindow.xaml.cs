using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Multi_Desktop.Models;
using Multi_Desktop.Services;

namespace Multi_Desktop;

/// <summary>
/// 設定画面のコードビハインド
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService = new();
    public AppSettings UpdatedSettings { get; private set; }

    // ピン留めアプリの一時リスト
    private readonly List<string> _mainPinnedApps;
    private readonly List<string> _subPinnedApps;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        UpdatedSettings = settings;

        // 現在の設定をUIに反映
        MainFolderTextBox.Text = settings.MainFolderPath;
        MainWallpaperTextBox.Text = settings.MainWallpaperPath;
        SubFolderTextBox.Text = settings.SubFolderPath;
        SubWallpaperTextBox.Text = settings.SubWallpaperPath;
        StartupToggle.IsChecked = settings.IsStartupEnabled;

        // ミュージックサービス設定をUIに反映
        YouTubeToggle.IsChecked = settings.MusicSettings.IsYouTubeEnabled;
        AmazonMusicToggle.IsChecked = settings.MusicSettings.IsAmazonMusicEnabled;
        SpotifyToggle.IsChecked = settings.MusicSettings.IsSpotifyEnabled;
        UpdateMusicLoginButtons();

        // タスクバー設定をUIに反映
        _mainPinnedApps = new List<string>(settings.MainTaskbarSettings.PinnedApps);
        _subPinnedApps = new List<string>(settings.SubTaskbarSettings.PinnedApps);

        LoadTaskbarSettings(settings.MainTaskbarSettings,
            MainDockToggle, MainDockSettingsPanel,
            MainIconSizeSlider, MainIconSizeLabel,
            MainMagSlider, MainMagLabel,
            MainPinnedAppsList, _mainPinnedApps);

        LoadTaskbarSettings(settings.SubTaskbarSettings,
            SubDockToggle, SubDockSettingsPanel,
            SubIconSizeSlider, SubIconSizeLabel,
            SubMagSlider, SubMagLabel,
            SubPinnedAppsList, _subPinnedApps);

        // AI アシスタント設定をUIに反映
        GeminiApiKeyBox.Password = settings.GeminiApiKey ?? string.Empty;
        
        foreach (ComboBoxItem item in QuickAiServiceComboBox.Items)
        {
            if (item.Content.ToString() == settings.QuickAiService)
            {
                QuickAiServiceComboBox.SelectedItem = item;
                break;
            }
        }
    }

    // ─── タスクバー設定のUI反映 ──────────────────────
    private void LoadTaskbarSettings(TaskbarSettings ts,
        System.Windows.Controls.CheckBox dockToggle, StackPanel settingsPanel,
        Slider iconSizeSlider, System.Windows.Controls.TextBlock iconSizeLabel,
        Slider magSlider, System.Windows.Controls.TextBlock magLabel,
        System.Windows.Controls.ListBox pinnedList, List<string> pinnedApps)
    {
        dockToggle.IsChecked = ts.Style == TaskbarStyle.MacOSDock;
        settingsPanel.Visibility = ts.Style == TaskbarStyle.MacOSDock
            ? Visibility.Visible : Visibility.Collapsed;

        iconSizeSlider.Value = ts.DockIconSize;
        iconSizeLabel.Text = $"{ts.DockIconSize}px";

        magSlider.Value = ts.MagnificationFactor * 10;
        magLabel.Text = $"{ts.MagnificationFactor:F1}x";

        RefreshPinnedAppsList(pinnedList, pinnedApps);
    }

    private void RefreshPinnedAppsList(System.Windows.Controls.ListBox listBox, List<string> pinnedApps)
    {
        listBox.Items.Clear();
        foreach (var path in pinnedApps)
        {
            listBox.Items.Add(Path.GetFileName(path));
        }
    }

    // ─── Dockトグルイベント ──────────────────────────
    private void MainDockToggle_Changed(object sender, RoutedEventArgs e)
    {
        MainDockSettingsPanel.Visibility =
            MainDockToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SubDockToggle_Changed(object sender, RoutedEventArgs e)
    {
        SubDockSettingsPanel.Visibility =
            SubDockToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ─── スライダーイベント ──────────────────────────
    private void MainIconSizeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainIconSizeLabel != null)
            MainIconSizeLabel.Text = $"{(int)MainIconSizeSlider.Value}px";
    }

    private void MainMagSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainMagLabel != null)
            MainMagLabel.Text = $"{MainMagSlider.Value / 10.0:F1}x";
    }

    private void SubIconSizeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SubIconSizeLabel != null)
            SubIconSizeLabel.Text = $"{(int)SubIconSizeSlider.Value}px";
    }

    private void SubMagSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SubMagLabel != null)
            SubMagLabel.Text = $"{SubMagSlider.Value / 10.0:F1}x";
    }

    // ─── ピン留めアプリ管理 ──────────────────────────
    private void AddMainPinnedApp_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseExe();
        if (path != null && !_mainPinnedApps.Contains(path))
        {
            _mainPinnedApps.Add(path);
            RefreshPinnedAppsList(MainPinnedAppsList, _mainPinnedApps);
        }
    }

    private void AddMainPinnedAppFromProcess_Click(object sender, RoutedEventArgs e)
    {
        var window = new ProcessSelectionWindow { Owner = this };
        if (window.ShowDialog() == true && !string.IsNullOrEmpty(window.SelectedExePath))
        {
            if (!_mainPinnedApps.Contains(window.SelectedExePath))
            {
                _mainPinnedApps.Add(window.SelectedExePath);
                RefreshPinnedAppsList(MainPinnedAppsList, _mainPinnedApps);
            }
        }
    }

    private void RemoveMainPinnedApp_Click(object sender, RoutedEventArgs e)
    {
        if (MainPinnedAppsList.SelectedIndex >= 0 &&
            MainPinnedAppsList.SelectedIndex < _mainPinnedApps.Count)
        {
            _mainPinnedApps.RemoveAt(MainPinnedAppsList.SelectedIndex);
            RefreshPinnedAppsList(MainPinnedAppsList, _mainPinnedApps);
        }
    }

    private void AddSubPinnedApp_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseExe();
        if (path != null && !_subPinnedApps.Contains(path))
        {
            _subPinnedApps.Add(path);
            RefreshPinnedAppsList(SubPinnedAppsList, _subPinnedApps);
        }
    }

    private void AddSubPinnedAppFromProcess_Click(object sender, RoutedEventArgs e)
    {
        var window = new ProcessSelectionWindow { Owner = this };
        if (window.ShowDialog() == true && !string.IsNullOrEmpty(window.SelectedExePath))
        {
            if (!_subPinnedApps.Contains(window.SelectedExePath))
            {
                _subPinnedApps.Add(window.SelectedExePath);
                RefreshPinnedAppsList(SubPinnedAppsList, _subPinnedApps);
            }
        }
    }

    private void RemoveSubPinnedApp_Click(object sender, RoutedEventArgs e)
    {
        if (SubPinnedAppsList.SelectedIndex >= 0 &&
            SubPinnedAppsList.SelectedIndex < _subPinnedApps.Count)
        {
            _subPinnedApps.RemoveAt(SubPinnedAppsList.SelectedIndex);
            RefreshPinnedAppsList(SubPinnedAppsList, _subPinnedApps);
        }
    }

    // ─── フォルダ選択 ────────────────────────────────
    private void BrowseMainFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseFolder(MainFolderTextBox.Text);
        if (path != null) MainFolderTextBox.Text = path;
    }

    private void BrowseSubFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseFolder(SubFolderTextBox.Text);
        if (path != null) SubFolderTextBox.Text = path;
    }

    // ─── 壁紙選択 ───────────────────────────────────
    private void BrowseMainWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseImage(MainWallpaperTextBox.Text);
        if (path != null) MainWallpaperTextBox.Text = path;
    }

    private void BrowseSubWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseImage(SubWallpaperTextBox.Text);
        if (path != null) SubWallpaperTextBox.Text = path;
    }

    // ─── ミュージックサービス ─────────────────────────
    private void MusicToggle_Changed(object sender, RoutedEventArgs e)
    {
        UpdateMusicLoginButtons();
    }

    private void UpdateMusicLoginButtons()
    {
        YouTubeLoginBtn.Visibility = YouTubeToggle.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
        AmazonLoginBtn.Visibility = AmazonMusicToggle.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
        SpotifyLoginBtn.Visibility = SpotifyToggle.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void YouTubeLogin_Click(object sender, RoutedEventArgs e)
    {
        new MusicLoginWindow("YouTube", "https://accounts.google.com/ServiceLogin?service=youtube")
            { Owner = this }.ShowDialog();
    }

    private void AmazonLogin_Click(object sender, RoutedEventArgs e)
    {
        new MusicLoginWindow("Amazon Music", "https://music.amazon.co.jp")
            { Owner = this }.ShowDialog();
    }

    private void SpotifyLogin_Click(object sender, RoutedEventArgs e)
    {
        new MusicLoginWindow("Spotify", "https://accounts.spotify.com/login")
            { Owner = this }.ShowDialog();
    }

    // ─── 保存/キャンセル ────────────────────────────
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        UpdatedSettings.MainFolderPath = MainFolderTextBox.Text.Trim();
        UpdatedSettings.SubFolderPath = SubFolderTextBox.Text.Trim();
        UpdatedSettings.MainWallpaperPath = MainWallpaperTextBox.Text.Trim();
        UpdatedSettings.SubWallpaperPath = SubWallpaperTextBox.Text.Trim();
        UpdatedSettings.IsStartupEnabled = StartupToggle.IsChecked == true;

        // ミュージックサービス設定を保存
        UpdatedSettings.MusicSettings.IsYouTubeEnabled = YouTubeToggle.IsChecked == true;
        UpdatedSettings.MusicSettings.IsAmazonMusicEnabled = AmazonMusicToggle.IsChecked == true;
        UpdatedSettings.MusicSettings.IsSpotifyEnabled = SpotifyToggle.IsChecked == true;

        // AI アシスタント設定を保存
        UpdatedSettings.GeminiApiKey = GeminiApiKeyBox.Password;
        if (QuickAiServiceComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            UpdatedSettings.QuickAiService = selectedItem.Content.ToString() ?? "ChatGPT";
        }

        // タスクバー設定を保存
        SaveTaskbarSettings(UpdatedSettings.MainTaskbarSettings,
            MainDockToggle, MainIconSizeSlider, MainMagSlider,
            _mainPinnedApps);

        SaveTaskbarSettings(UpdatedSettings.SubTaskbarSettings,
            SubDockToggle, SubIconSizeSlider, SubMagSlider,
            _subPinnedApps);

        await _settingsService.SaveAsync(UpdatedSettings);

        // スタートアップ設定を適用
        StartupService.SetStartup(UpdatedSettings.IsStartupEnabled);

        // データフォルダを自動作成
        DesktopSwitchService.EnsureDataFoldersExist(UpdatedSettings);

        DialogResult = true;
        Close();
    }

    private void SaveTaskbarSettings(TaskbarSettings ts,
        System.Windows.Controls.CheckBox dockToggle,
        Slider iconSizeSlider, Slider magSlider,
        List<string> pinnedApps)
    {
        ts.Style = dockToggle.IsChecked == true
            ? TaskbarStyle.MacOSDock : TaskbarStyle.Normal;
        ts.DockIconSize = (int)iconSizeSlider.Value;
        ts.MagnificationFactor = magSlider.Value / 10.0;
        ts.AutoHide = true; // カスタムDockモードでは常に自動非表示
        ts.PinnedApps = new List<string>(pinnedApps);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ─── ダイアログヘルパー ──────────────────────────
    private static string? BrowseFolder(string currentPath)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "フォルダを選択",
            InitialDirectory = Directory.Exists(currentPath) ? currentPath : ""
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static string? BrowseImage(string currentPath)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "壁紙画像を選択",
            Filter = "画像ファイル|*.jpg;*.jpeg;*.png;*.bmp;*.gif|すべてのファイル|*.*",
            InitialDirectory = string.IsNullOrWhiteSpace(currentPath) ? "" :
                Path.GetDirectoryName(currentPath) ?? ""
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string? BrowseExe()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "アプリケーションを選択",
            Filter = "実行ファイル|*.exe;*.lnk|すべてのファイル|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
