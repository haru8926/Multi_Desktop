using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Multi_Desktop.PluginApi;

namespace Multi_Desktop.FFmpegPlugin
{
    public class FFmpegPluginMain : IPlugin
    {
        public string Name => "メディア変換 (FFmpeg)";
        public string Description => "動画・音声ファイルの多彩な変換や圧縮を行います。";
        public string Version => "2.0.0";
        public string Author => "AI Assistant";

        private IPluginHost? _host;
        private string _pluginDir = string.Empty;
        private string _ffmpegPath = string.Empty;
        
        // UI Elements
        private TextBlock _statusText = new();
        private Border _dropArea = new();
        private WrapPanel _actionPanel = new();
        private string _currentFilePath = string.Empty;

        private enum ConvertAction
        {
            ToMp3,
            ShrinkHigh,
            ShrinkLow,
            ToWebm,
            SpeedUp,
            SlowDown,
            ToGif,
            RemoveAudio
        }

        public void Initialize(IPluginHost host)
        {
            _host = host;
            _pluginDir = System.IO.Path.GetDirectoryName(GetType().Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            _ffmpegPath = System.IO.Path.Combine(_pluginDir, "ffmpeg.exe");

            host.InvokeOnUIThread(() =>
            {
                var container = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 40)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16)
                };

                var mainStack = new StackPanel();

                // Header
                var headerPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
                var titleText = new TextBlock
                {
                    Text = "🎞 高機能メディア変換機",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerPanel.Children.Add(titleText);
                mainStack.Children.Add(headerPanel);

                // Status
                _statusText.Text = "準備中...";
                _statusText.FontSize = 12;
                _statusText.Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
                _statusText.Margin = new Thickness(0, 0, 0, 12);
                _statusText.TextWrapping = TextWrapping.Wrap;
                mainStack.Children.Add(_statusText);

                // Drop Area
                _dropArea.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
                _dropArea.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
                _dropArea.BorderThickness = new Thickness(2);
                _dropArea.CornerRadius = new CornerRadius(8);
                _dropArea.Height = 100;
                _dropArea.AllowDrop = true;
                _dropArea.Cursor = System.Windows.Input.Cursors.Hand;
                
                var dropLabel = new TextBlock
                {
                    Text = "ここに動画(MP4等)をドロップ\nまたはクリックして選択",
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 12
                };
                _dropArea.Child = dropLabel;
                
                // Drag & Drop events
                _dropArea.Drop += OnFileDrop;
                _dropArea.DragEnter += (s, e) =>
                {
                    if (e.Data.GetDataPresent(DataFormats.FileDrop))
                        e.Effects = DragDropEffects.Copy;
                    else
                        e.Effects = DragDropEffects.None;
                    e.Handled = true;
                };

                // Click event for file dialog
                _dropArea.MouseLeftButtonUp += OnDropAreaClick;

                mainStack.Children.Add(_dropArea);

                // Actions panel
                _actionPanel.Visibility = Visibility.Collapsed;
                _actionPanel.Margin = new Thickness(0, 16, 0, 0);
                _actionPanel.Orientation = Orientation.Horizontal;

                AddActionButton("MP3抽出", "#aa33aa", ConvertAction.ToMp3);
                AddActionButton("高画質圧縮", "#33aacc", ConvertAction.ShrinkHigh);
                AddActionButton("低画質圧縮", "#2288aa", ConvertAction.ShrinkLow);
                AddActionButton("WebMに変換", "#cc5533", ConvertAction.ToWebm);
                AddActionButton("2倍速化", "#eebb33", ConvertAction.SpeedUp);
                AddActionButton("0.5倍スロー", "#55cc55", ConvertAction.SlowDown);
                AddActionButton("GIF作成(10s)", "#ff3366", ConvertAction.ToGif);
                AddActionButton("音声ミュート", "#777777", ConvertAction.RemoveAudio);

                mainStack.Children.Add(_actionPanel);
                container.Child = mainStack;

                // Create a standalone window to host this UI
                var pluginWindow = new Window
                {
                    Title = "FFmpeg Plugin (Pro)",
                    Width = 380,
                    Height = 440,
                    WindowStyle = WindowStyle.ToolWindow,
                    Background = new SolidColorBrush(Color.FromArgb(255, 20, 20, 28)),
                    Content = container,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                pluginWindow.Closing += (sender, e) =>
                {
                    e.Cancel = true;
                    pluginWindow.Hide();
                };

                host.AddMenuItem("FFmpeg 変換(高機能)", () =>
                {
                    pluginWindow.Show();
                    pluginWindow.Activate();
                });
            });

            // 非同期でFFmpegの準備
            Task.Run(EnsureFFmpegAsync);
        }

        private void AddActionButton(string text, string hexColor, ConvertAction action)
        {
            var b = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Width = 160
            };
            b.Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            b.MouseLeftButtonUp += (s, e) => ExecuteFFmpeg(action);
            _actionPanel.Children.Add(b);
        }

        private async Task EnsureFFmpegAsync()
        {
            if (File.Exists(_ffmpegPath))
            {
                UpdateStatus("FFmpeg 準備完了。ドロップできます。");
                return;
            }

            try
            {
                UpdateStatus("FFmpegをダウンロードしています... (初回のみ数分かかります)");

                string zipPath = System.IO.Path.Combine(_pluginDir, "ffmpeg.zip");
                string downloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(downloadUrl);
                    response.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                UpdateStatus("展開中...");

                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.FullName.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.ExtractToFile(_ffmpegPath, true);
                            break;
                        }
                    }
                }

                if (File.Exists(zipPath)) File.Delete(zipPath);

                UpdateStatus("FFmpeg 準備完了。ドロップできます。");
            }
            catch (Exception ex)
            {
                UpdateStatus($"FFmpegの準備に失敗しました: {ex.Message}");
            }
        }

        private void OnDropAreaClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!File.Exists(_ffmpegPath))
            {
                MessageBox.Show("FFmpegのダウンロードが完了するまでお待ちください。");
                return;
            }

            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "変換する動画ファイルを選択",
                Filter = "動画ファイル|*.mp4;*.mkv;*.webm;*.mov;*.avi|すべてのファイル|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ProcessSelectedFile(openFileDialog.FileName);
            }
        }

        private void ProcessSelectedFile(string filePath)
        {
            _currentFilePath = filePath;
            var ext = System.IO.Path.GetExtension(_currentFilePath).ToLower();

            if (ext == ".mp4" || ext == ".mkv" || ext == ".webm" || ext == ".mov" || ext == ".avi")
            {
                if (_dropArea.Child is TextBlock dropLabel)
                {
                    dropLabel.Text = System.IO.Path.GetFileName(_currentFilePath);
                }
                _actionPanel.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show("未対応のファイル形式です。動画ファイルを選択してください。");
            }
        }

        private void OnFileDrop(object sender, DragEventArgs e)
        {
            if (!File.Exists(_ffmpegPath))
            {
                MessageBox.Show("FFmpegのダウンロードが完了するまでお待ちください。");
                return;
            }

            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                ProcessSelectedFile(files[0]);
            }
        }

        private async void ExecuteFFmpeg(ConvertAction action)
        {
            if (string.IsNullOrEmpty(_currentFilePath) || !File.Exists(_currentFilePath)) return;

            string outDir = System.IO.Path.GetDirectoryName(_currentFilePath) ?? string.Empty;
            string baseName = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath);

            string outFile = "";
            string args = "";

            switch (action)
            {
                case ConvertAction.ToMp3:
                    outFile = System.IO.Path.Combine(outDir, $"{baseName}_audio.mp3");
                    args = $"-i \"{_currentFilePath}\" -vn -ar 44100 -ac 2 -b:a 192k -y \"{outFile}\"";
                    break;
                case ConvertAction.ShrinkHigh:
                    outFile = System.IO.Path.Combine(outDir, $"{baseName}_shrinkHigh.mp4");
                    args = $"-i \"{_currentFilePath}\" -vcodec libx264 -crf 23 -preset fast -acodec aac -b:a 128k -y \"{outFile}\"";
                    break;
                case ConvertAction.ShrinkLow:
                    outFile = System.IO.Path.Combine(outDir, $"{baseName}_shrinkLow.mp4");
                    args = $"-i \"{_currentFilePath}\" -vcodec libx264 -crf 28 -preset veryfast -acodec aac -b:a 96k -y \"{outFile}\"";
                    break;
                case ConvertAction.ToWebm:
                    outFile = System.IO.Path.Combine(outDir, $"{baseName}.webm");
                    args = $"-i \"{_currentFilePath}\" -c:v libvpx-vp9 -b:v 1M -c:a libopus -y \"{outFile}\"";
                    break;
                case ConvertAction.SpeedUp:
                    outFile = System.IO.Path.Combine(outDir, $"{baseName}_2x.mp4");
                    args = $"-i \"{_currentFilePath}\" -filter:v \"setpts=0.5*PTS\" -filter:a \"atempo=2.0\" -y \"{outFile}\"";
                    break;
                case ConvertAction.SlowDown:
                    outFile = System.IO.Path.Combine(outDir, $"{baseName}_0.5x.mp4");
                    args = $"-i \"{_currentFilePath}\" -filter:v \"setpts=2.0*PTS\" -filter:a \"atempo=0.5\" -y \"{outFile}\"";
                    break;
                case ConvertAction.ToGif:
                    outFile = System.IO.Path.Combine(outDir, $"{baseName}.gif");
                    // 最初の10秒間をGIFにする、FPS 10、幅最大480
                    args = $"-t 10 -i \"{_currentFilePath}\" -vf \"fps=10,scale=480:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" -loop 0 -y \"{outFile}\"";
                    break;
                case ConvertAction.RemoveAudio:
                    outFile = System.IO.Path.Combine(outDir, $"{baseName}_muted.mp4");
                    args = $"-i \"{_currentFilePath}\" -vcodec copy -an -y \"{outFile}\"";
                    break;
            }

            UpdateStatus("変換中...");
            _actionPanel.IsEnabled = false;

            try
            {
                await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = _ffmpegPath,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit();
                });

                UpdateStatus("変換完了！\n保存先: " + System.IO.Path.GetFileName(outFile));
                MessageBox.Show($"変換が完了しました。\n{outFile}", "FFmpeg 変換", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UpdateStatus("エラー発生！");
                MessageBox.Show($"変換中にエラーが発生しました。\n{ex.Message}", "FFmpeg エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _actionPanel.IsEnabled = true;
            }
        }

        private void UpdateStatus(string message)
        {
            _host?.InvokeOnUIThread(() =>
            {
                _statusText.Text = message;
            });
        }

        public void Shutdown()
        {
            // optional cleanup
        }
    }
}
