// YoutubeTvCloneWindow.xaml.cs (新規作成)
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace Multi_Desktop
{
    public partial class YoutubeTvCloneWindow : Window
    {
        private WebView2 _mainWebView;

        public YoutubeTvCloneWindow(WebView2 mainWebView)
        {
            InitializeComponent();
            _mainWebView = mainWebView;

            // ウィンドウがロードされたら複製を開始する
            this.Loaded += YoutubeTvCloneWindow_Loaded;
        }

        private void YoutubeTvCloneWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // VisualBrushにメインのWebView2を設定
            // これにより、メインウィンドウの内容がリアルタイムで複製表示される
            visualBrush.Visual = _mainWebView;
        }
    }
}