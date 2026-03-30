using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Multi_Desktop.Helpers;
using Image = System.Windows.Controls.Image;

namespace Multi_Desktop
{
    /// <summary>
    /// 背景モード用：全仮想デスクトップを覆う1つのウィンドウ。
    /// WorkerW の子として配置され、各モニター位置に YouTube の映像を表示する。
    /// メインWebView2は画面外に送られるが、CapturePreviewAsync でレンダリング内容を取得し、
    /// 各モニター用の Image コントロールに描画する。
    /// </summary>
    public partial class YoutubeTvBackgroundWindow : Window
    {
        private WebView2? _webView;
        private DispatcherTimer? _captureTimer;
        private bool _isCapturing;
        private readonly List<Image> _monitorImages = new();

        /// <summary>
        /// ぼかしモード: true=ぼかしあり背景、false=ぼかしなし背景
        /// </summary>
        public bool UseBlur { get; set; }

        public YoutubeTvBackgroundWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 各モニターに合わせた Image コントロールを Canvas 上に配置し、キャプチャを開始する。
        /// </summary>
        /// <param name="webView">キャプチャ元の WebView2</param>
        /// <param name="captureIntervalMs">キャプチャ間隔（ミリ秒）</param>
        public void StartMirroring(WebView2 webView, int captureIntervalMs = 33)
        {
            _webView = webView;

            // 仮想スクリーン全体のサイズを計算
            var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;

            // 各モニターに Image を配置
            MonitorCanvas.Children.Clear();
            _monitorImages.Clear();

            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var img = new Image
                {
                    Stretch = Stretch.UniformToFill
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.LowQuality);

                // Canvas 内での位置は virtualScreen 左上からの相対座標
                Canvas.SetLeft(img, screen.Bounds.X - virtualScreen.X);
                Canvas.SetTop(img, screen.Bounds.Y - virtualScreen.Y);
                img.Width = screen.Bounds.Width;
                img.Height = screen.Bounds.Height;

                // クリッピング: 各モニター領域で切り取る
                img.Clip = new RectangleGeometry(new Rect(0, 0, screen.Bounds.Width, screen.Bounds.Height));

                MonitorCanvas.Children.Add(img);
                _monitorImages.Add(img);
            }

            // キャプチャタイマー開始
            _captureTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(captureIntervalMs)
            };
            _captureTimer.Tick += CaptureTimer_Tick;
            _captureTimer.Start();
        }

        /// <summary>
        /// WebView2 の CapturePreviewAsync で映像を取得し、全モニターの Image に反映する
        /// </summary>
        private async void CaptureTimer_Tick(object? sender, EventArgs e)
        {
            if (_isCapturing || _webView?.CoreWebView2 == null) return;
            _isCapturing = true;

            try
            {
                using var ms = new MemoryStream();
                await _webView.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png, ms);

                ms.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                // 全モニターの Image を同じビットマップで更新
                foreach (var img in _monitorImages)
                {
                    img.Source = bitmap;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Background capture error: {ex.Message}");
            }
            finally
            {
                _isCapturing = false;
            }
        }

        /// <summary>
        /// キャプチャを停止しリソースを解放する
        /// </summary>
        public void StopMirroring()
        {
            if (_captureTimer != null)
            {
                _captureTimer.Stop();
                _captureTimer.Tick -= CaptureTimer_Tick;
                _captureTimer = null;
            }
            _webView = null;
            _monitorImages.Clear();
            MonitorCanvas.Children.Clear();
        }

        /// <summary>
        /// ウィンドウを WorkerW の子として仮想デスクトップ全体に配置する
        /// </summary>
        public void PlaceInBackground()
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero) return;

            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            NativeMethods.SetWindowToBackground(hwnd, vs.X, vs.Y, vs.Width, vs.Height);
        }

        /// <summary>
        /// WorkerW から復帰する
        /// </summary>
        public void RestoreFromBackground()
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.RestoreWindowFromBackground(hwnd);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            StopMirroring();
            base.OnClosed(e);
        }
    }
}
