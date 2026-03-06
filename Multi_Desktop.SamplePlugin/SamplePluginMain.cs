using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Multi_Desktop.PluginApi;

namespace Multi_Desktop.SamplePlugin
{
    public class SamplePluginMain : IPlugin
    {
        public string Name => "Sample Plugin";
        public string Description => "A demonstration of the Multi_Desktop Plugin API";
        public string Version => "1.0.0";
        public string Author => "AI Assistant";

        public void Initialize(IPluginHost host)
        {
            host.AddMenuItem("サンプルプラグイン", () =>
            {
                MessageBox.Show("プラグインからメニューがクリックされました！", "Sample Plugin");
            });

            host.InvokeOnUIThread(() =>
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(50, 255, 100, 100)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                
                var panel = new StackPanel();
                panel.Children.Add(new TextBlock
                {
                    Text = "🍣 サンプルプラグイン",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                panel.Children.Add(new TextBlock
                {
                    Text = "これはトレイに埋め込まれたUIです。",
                    FontSize = 10,
                    Foreground = Brushes.White
                });
                
                border.Child = panel;
                host.AddTrayPopupView(border);
            });
        }

        public void Shutdown()
        {
            // Optional cleanup
        }
    }
}
