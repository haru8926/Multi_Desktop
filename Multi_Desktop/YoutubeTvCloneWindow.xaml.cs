using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Multi_Desktop
{
    /// <summary>
    /// キャプチャ方式の切り替え
    /// </summary>
    public enum CloneCaptureMode
    {
        /// <summary>全画面モード: PrintWindow API で高速キャプチャ</summary>
        PrintWindow,
        /// <summary>背景モード: WebView2 の CapturePreviewAsync で確実にキャプチャ</summary>
        WebViewCapture
    }

    public partial class YoutubeTvCloneWindow : Window
    {
        private readonly Window _mainWindow;
        private readonly WebView2 _webView;
        private CancellationTokenSource? _cts;
        private WriteableBitmap? _writeableBitmap;

        /// <summary>
        /// このクローンウィンドウが配置されるべきスクリーン情報（物理ピクセル）
        /// </summary>
        public System.Windows.Forms.Screen? TargetScreen { get; set; }

        /// <summary>
        /// キャプチャのフレーム間隔（ミリ秒）
        /// </summary>
        public int CaptureIntervalMs { get; set; } = 16; // ~60fps 目標

        /// <summary>
        /// キャプチャ方式（全画面: PrintWindow / 背景: WebViewCapture）
        /// </summary>
        public CloneCaptureMode CaptureMode { get; set; } = CloneCaptureMode.PrintWindow;

        public YoutubeTvCloneWindow(Window mainWindow, WebView2 webView)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _webView = webView;
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            StartCapture();
        }

        /// <summary>
        /// キャプチャループを開始する
        /// </summary>
        public void StartCapture()
        {
            StopCapture();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            if (CaptureMode == CloneCaptureMode.PrintWindow)
            {
                // PrintWindow はバックグラウンドスレッドで実行
                Task.Run(() => PrintWindowCaptureLoop(token), token);
            }
            else
            {
                // WebView CapturePreview は UIスレッドの DispatcherTimer で実行
                StartWebViewCaptureLoop(token);
            }
        }

        /// <summary>
        /// キャプチャを停止する
        /// </summary>
        public void StopCapture()
        {
            _cts?.Cancel();
            _cts = null;
        }

        #region PrintWindow キャプチャ（全画面モード用 - 高速）

        /// <summary>
        /// バックグラウンドスレッドで PrintWindow API を使った高速キャプチャを繰り返す。
        /// </summary>
        private async Task PrintWindowCaptureLoop(CancellationToken token)
        {
            IntPtr hdcSrc = IntPtr.Zero;
            IntPtr hdcDest = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr hOld = IntPtr.Zero;
            int bufWidth = 0, bufHeight = 0;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var sw = Stopwatch.StartNew();

                    try
                    {
                        IntPtr hwnd = IntPtr.Zero;
                        Dispatcher.Invoke(() =>
                        {
                            var helper = new WindowInteropHelper(_mainWindow);
                            hwnd = helper.Handle;
                        }, DispatcherPriority.Send, token);

                        if (hwnd == IntPtr.Zero || token.IsCancellationRequested)
                        {
                            await Task.Delay(100, token);
                            continue;
                        }

                        if (!GetWindowRect(hwnd, out var rect))
                        {
                            await Task.Delay(100, token);
                            continue;
                        }

                        int width = rect.Right - rect.Left;
                        int height = rect.Bottom - rect.Top;
                        if (width <= 0 || height <= 0)
                        {
                            await Task.Delay(100, token);
                            continue;
                        }

                        // GDIバッファのサイズが変わったら再作成
                        if (width != bufWidth || height != bufHeight)
                        {
                            if (hOld != IntPtr.Zero && hdcDest != IntPtr.Zero)
                                SelectObject(hdcDest, hOld);
                            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                            if (hdcDest != IntPtr.Zero) DeleteDC(hdcDest);
                            if (hdcSrc != IntPtr.Zero) ReleaseDC(hwnd, hdcSrc);

                            hdcSrc = GetWindowDC(hwnd);
                            hdcDest = CreateCompatibleDC(hdcSrc);
                            hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
                            hOld = SelectObject(hdcDest, hBitmap);
                            bufWidth = width;
                            bufHeight = height;
                        }

                        bool success = PrintWindowApi(hwnd, hdcDest, PW_RENDERFULLCONTENT);
                        if (!success)
                        {
                            await Task.Delay(50, token);
                            continue;
                        }

                        var bmi = new BITMAPINFO
                        {
                            biSize = 40,
                            biWidth = width,
                            biHeight = -height,
                            biPlanes = 1,
                            biBitCount = 32,
                            biCompression = 0
                        };

                        int stride = width * 4;
                        byte[] pixelData = new byte[stride * height];

                        int lines = GetDIBits(hdcDest, hBitmap, 0, (uint)height, pixelData, ref bmi, 0);
                        if (lines == 0)
                        {
                            await Task.Delay(50, token);
                            continue;
                        }

                        int w = width, h = height;
                        byte[] data = pixelData;
                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                if (_writeableBitmap == null ||
                                    _writeableBitmap.PixelWidth != w ||
                                    _writeableBitmap.PixelHeight != h)
                                {
                                    _writeableBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                                    cloneImage.Source = _writeableBitmap;
                                }

                                _writeableBitmap.WritePixels(
                                    new Int32Rect(0, 0, w, h),
                                    data, w * 4, 0);
                            }
                            catch { }
                        }, DispatcherPriority.Render, token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"PrintWindow CaptureLoop error: {ex.Message}");
                    }

                    sw.Stop();
                    int sleepMs = CaptureIntervalMs - (int)sw.ElapsedMilliseconds;
                    if (sleepMs > 0)
                    {
                        try { await Task.Delay(sleepMs, token); }
                        catch (OperationCanceledException) { break; }
                    }
                }
            }
            finally
            {
                if (hOld != IntPtr.Zero && hdcDest != IntPtr.Zero) SelectObject(hdcDest, hOld);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                if (hdcDest != IntPtr.Zero) DeleteDC(hdcDest);
            }
        }

        #endregion

        #region WebView2 CapturePreview キャプチャ（背景モード用 - 確実）

        /// <summary>
        /// WebView2 の CapturePreviewAsync を使ったキャプチャループ。
        /// 背景モードでウィンドウが WorkerW の子になっていても確実に動作する。
        /// </summary>
        private void StartWebViewCaptureLoop(CancellationToken token)
        {
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(CaptureIntervalMs)
            };

            bool isCapturing = false;

            timer.Tick += async (s, e) =>
            {
                if (token.IsCancellationRequested)
                {
                    timer.Stop();
                    return;
                }

                if (isCapturing) return;
                isCapturing = true;

                try
                {
                    if (_webView?.CoreWebView2 == null)
                    {
                        isCapturing = false;
                        return;
                    }

                    using var ms = new MemoryStream();

                    // WebView2 自身のレンダリング内容を PNG でキャプチャ
                    await _webView.CoreWebView2.CapturePreviewAsync(
                        CoreWebView2CapturePreviewImageFormat.Png, ms);

                    if (token.IsCancellationRequested) return;

                    ms.Position = 0;

                    // PNG → BitmapImage にデコード
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    cloneImage.Source = bitmap;
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WebView CapturePreview error: {ex.Message}");
                }
                finally
                {
                    isCapturing = false;
                }
            };

            // キャンセル時にタイマーを停止
            token.Register(() =>
            {
                Dispatcher.Invoke(() => timer.Stop());
            });

            timer.Start();
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            StopCapture();
            cloneImage.Source = null;
            _writeableBitmap = null;
            base.OnClosed(e);
        }

        #region Win32 P/Invoke

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll", EntryPoint = "PrintWindow")]
        private static extern bool PrintWindowApi(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
            [Out] byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

        #endregion
    }
}