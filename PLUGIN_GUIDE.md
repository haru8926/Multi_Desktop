# Multi_Desktop プラグイン開発ガイド

このガイドでは、`Multi_Desktop` アプリケーション用の拡張機能（プラグイン）を開発する方法を説明します。
プラグインシステムは .NET 8 WPF 上で動作し、各プラグインは独立した `AssemblyLoadContext` 内で読み込まれるため、独自の依存関係（NuGet パッケージや .dll ファイル）を安全に利用できます。

## 1. プロジェクトの作成

新しい WPF クラスライブラリ プロジェクトを作成します。

```bash
dotnet new classlib -n MyAwesomePlugin -f net8.0-windows
```

`.csproj` ファイルを開き、`<UseWPF>true</UseWPF>` を必ず有効にします。

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- PluginApi を参照する -->
    <ProjectReference Include="..\Multi_Desktop.PluginApi\Multi_Desktop.PluginApi.csproj">
      <Private>false</Private>
    </ProjectReference>
  </ItemGroup>
</Project>
```
※ `Private=false` を設定することで、ビルド出力に `PluginApi.dll` が含まれないようにします（ホストアプリ側に既存のため）。

## 2. IPlugin の実装

プラグインのメインクラスを作成し、`IPlugin` インターフェースを実装します。

```csharp
using System;
using System.Windows.Controls;
using System.Windows.Media;
using Multi_Desktop.PluginApi;

namespace MyAwesomePlugin
{
    public class PluginMain : IPlugin
    {
        public string Name => "Sample Plugin";
        public string Description => "A simple example plugin.";
        public string Version => "1.0.0";
        public string Author => "Developer";

        public void Initialize(IPluginHost host)
        {
            // 1. メニューバーにボタンを追加する
            host.AddMenuItem("Hello", () =>
            {
                System.Windows.MessageBox.Show("Hello from Plugin!");
            });

            // 2. コントロールセンター（トレイ）に独自のUI部品を追加する
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(80, 255, 100, 100)),
                CornerRadius = new System.Windows.CornerRadius(8),
                Padding = new System.Windows.Thickness(10),
                Margin = new System.Windows.Thickness(0, 0, 0, 8)
            };
            
            var text = new TextBlock
            {
                Text = "プラグインからのUIです！",
                Foreground = Brushes.White,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            
            border.Child = text;
            
            host.AddTrayPopupView(border);
        }

        public void Shutdown()
        {
            // クリーンアップ処理
        }
    }
}
```

## 3. 依存関係について (ffmpg 等)

サードパーティ製のライブラリ（NuGet 等）を利用する場合、プラグインは独自の `AssemblyLoadContext` 内にロードされるため、通常通りプロジェクトでインストールして使用できます。
ただし、ビルドされた `.dll` はすべて**「プラグイン用の独立したフォルダ」内にまとめる**必要があります。

## 4. デプロイと利用

1. プロジェクトをビルドします（`dotnet build -c Release`）。
2. `bin\Release\net8.0-windows...` にあるすべての `.dll`（および依存ファイル）を1つのフォルダにまとめます。
   例: `Plugins\MyAwesomePlugin\`
3. このフォルダを `Multi_Desktop.exe` と同じディレクトリにある `Plugins` フォルダの中に配置します。
4. アプリケーションを起動すると、自動的にプラグインが検出され、初期化されます！

> [!NOTE]
> プラグイン内で致命的な例外（クラッシュ）が発生した場合、初期化ロジックは `try-catch` で保護されているため `Multi_Desktop` 本体が巻き込まれて落ちることは防がれます。ただし、UIスレッド等で投げられた未処理例外に関しては安全のために自身のコード内で `try-catch` を行うよう推奨します。
