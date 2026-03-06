using System.Windows;
using Multi_Desktop.Services;

namespace Multi_Desktop;

/// <summary>
/// App.xaml のコードビハインド
/// </summary>
public partial class App : System.Windows.Application
{
    public static MainPluginHost PluginHost { get; } = new MainPluginHost();
    public static PluginManager PluginManager { get; } = new PluginManager(PluginHost);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        PluginManager.LoadPlugins();

        // UIPI（ユーザーインターフェイス特権の分離）をバイパスして
        // 管理者権限でもD&Dできるようにする
        Helpers.NativeMethods.AllowDragAndDropFromLowerPrivilege();

        // 未処理例外のハンドリング — タスクバーを必ず復元
        DispatcherUnhandledException += (s, args) =>
        {
            // 安全策: タスクバーを復元
            try { Helpers.NativeMethods.ShowTaskbar(); } catch { }

            System.Windows.MessageBox.Show(
                $"予期しないエラーが発生しました:\n\n{args.Exception.Message}",
                "Multi Desktop — エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        // プロセス終了時の安全策
        AppDomain.CurrentDomain.ProcessExit += (s, args) =>
        {
            try { Helpers.NativeMethods.ShowTaskbar(); } catch { }
        };

        // 未処理の非同期例外
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try { Helpers.NativeMethods.ShowTaskbar(); } catch { }
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PluginManager.ShutdownPlugins();
        // アプリ終了時にタスクバーを必ず復元
        try { Helpers.NativeMethods.ShowTaskbar(); } catch { }
        // COM オブジェクトを解放
        try { Helpers.VolumeHelper.Cleanup(); } catch { }
        base.OnExit(e);
    }
}
