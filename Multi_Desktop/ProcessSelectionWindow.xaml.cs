using System;
using System.Linq;
using System.Windows;
using Multi_Desktop.Models;
using Multi_Desktop.Services;

namespace Multi_Desktop;

/// <summary>
/// 実行中のプロセス一覧からアプリを選択するダイアログ
/// </summary>
public partial class ProcessSelectionWindow : Window
{
    public string? SelectedExePath { get; private set; }

    public ProcessSelectionWindow()
    {
        InitializeComponent();
        Loaded += ProcessSelectionWindow_Loaded;
    }

    private void ProcessSelectionWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var apps = RunningAppService.GetVisibleWindows();
        
        // 実行ファイルパスが存在するアプリのみリストに表示
        var validApps = apps.Where(a => !string.IsNullOrEmpty(a.ExePath)).ToList();
        ProcessList.ItemsSource = validApps;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessList.SelectedItem is DockAppItem selectedItem)
        {
            SelectedExePath = selectedItem.ExePath;
            DialogResult = true;
            Close();
        }
    }
}
