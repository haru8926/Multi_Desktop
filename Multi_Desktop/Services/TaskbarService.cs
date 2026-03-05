namespace Multi_Desktop.Services;

/// <summary>
/// Windowsタスクバーの表示/非表示を制御するサービス
/// </summary>
public class TaskbarService : IDisposable
{
    private bool _isTaskbarHidden;

    /// <summary>タスクバーが非表示かどうか</summary>
    public bool IsTaskbarHidden => _isTaskbarHidden;

    /// <summary>Windowsタスクバーを非表示にする</summary>
    public void HideTaskbar()
    {
        if (_isTaskbarHidden) return;
        Helpers.NativeMethods.HideTaskbar();
        _isTaskbarHidden = true;
    }

    /// <summary>Windowsタスクバーを表示する</summary>
    public void ShowTaskbar()
    {
        if (!_isTaskbarHidden) return;
        Helpers.NativeMethods.ShowTaskbar();
        _isTaskbarHidden = false;
    }

    /// <summary>破棄時にタスクバーを必ず復元する（安全策）</summary>
    public void Dispose()
    {
        if (_isTaskbarHidden)
        {
            Helpers.NativeMethods.ShowTaskbar();
            _isTaskbarHidden = false;
        }
    }
}
