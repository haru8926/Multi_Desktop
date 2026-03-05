using System.Collections.Generic;

namespace Multi_Desktop.Models;

/// <summary>
/// タスクバー（Dock）の詳細設定モデル
/// </summary>
public class TaskbarSettings
{
    /// <summary>タスクバーのスタイル</summary>
    public TaskbarStyle Style { get; set; } = TaskbarStyle.Normal;

    /// <summary>Dockアイコンの基本サイズ (px)</summary>
    public int DockIconSize { get; set; } = 56;

    /// <summary>ホバー時の拡大率 (1.0〜2.5)</summary>
    public double MagnificationFactor { get; set; } = 1.8;

    /// <summary>自動非表示</summary>
    public bool AutoHide { get; set; } = false;

    /// <summary>ピン留めされたアプリのEXEパスリスト</summary>
    public List<string> PinnedApps { get; set; } = new();
}
