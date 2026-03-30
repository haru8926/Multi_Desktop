using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Multi_Desktop
{
    public static class YoutubeTvDialServer
    {
        private static UdpClient? _ssdpClient;
        private static TcpListener? _httpListener;
        private static CancellationTokenSource? _cts;
        private static readonly string Uuid = Guid.NewGuid().ToString();
        private static readonly int _httpPort = 56789;

        private static string GetLocalIpAddress()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint endPoint)
                    {
                        return endPoint.Address.ToString();
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public static void Start()
        {
            Stop();
            _cts = new CancellationTokenSource();

            string currentIp = GetLocalIpAddress();
            Debug.WriteLine("=========================================");
            Debug.WriteLine("[DIAL Server] 起動しました！");
            Debug.WriteLine($"[DIAL Server] 決定したPCのIPアドレスは {currentIp} です");
            Debug.WriteLine("=========================================");

            Task.Run(() => ListenSsdpAsync(_cts.Token));
            Task.Run(() => ListenHttpAsync(_cts.Token));
        }

        public static void Stop()
        {
            _cts?.Cancel();
            _ssdpClient?.Close();
            _httpListener?.Stop();
        }

        private static async Task ListenSsdpAsync(CancellationToken token)
        {
            try
            {
                _ssdpClient = new UdpClient();
                _ssdpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _ssdpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 1900));

                string localIpStr = GetLocalIpAddress();
                if (localIpStr != "127.0.0.1")
                {
                    _ssdpClient.JoinMulticastGroup(IPAddress.Parse("239.255.255.250"), IPAddress.Parse(localIpStr));
                }
                else
                {
                    _ssdpClient.JoinMulticastGroup(IPAddress.Parse("239.255.255.250"));
                }

                while (!token.IsCancellationRequested)
                {
                    var result = await _ssdpClient.ReceiveAsync();
                    var request = Encoding.UTF8.GetString(result.Buffer);

                    if (request.StartsWith("M-SEARCH") && request.Contains("urn:dial-multiscreen-org:service:dial:1"))
                    {
                        string localIp = GetLocalIpAddress();
                        Debug.WriteLine($"[DIAL SSDP] スマホ({result.RemoteEndPoint.Address})から探索リクエストを受信！");

                        string response = $"HTTP/1.1 200 OK\r\n" +
                                          $"CACHE-CONTROL: max-age=1800\r\n" +
                                          $"DATE: {DateTime.UtcNow:R}\r\n" +
                                          $"EXT:\r\n" +
                                          $"LOCATION: http://{localIp}:{_httpPort}/dd.xml\r\n" +
                                          $"SERVER: Windows/10 UPnP/1.1 MultiDesktop/1.0\r\n" +
                                          $"ST: urn:dial-multiscreen-org:service:dial:1\r\n" +
                                          $"USN: uuid:{Uuid}::urn:dial-multiscreen-org:service:dial:1\r\n" +
                                          $"BOOTID.UPNP.ORG: 1\r\n\r\n";

                        var responseBytes = Encoding.UTF8.GetBytes(response);
                        await _ssdpClient.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[DIAL SSDP Error] {ex.Message}"); }
        }

        private static async Task ListenHttpAsync(CancellationToken token)
        {
            try
            {
                _httpListener = new TcpListener(IPAddress.Any, _httpPort);
                _httpListener.Start();

                using (token.Register(() => _httpListener.Stop()))
                {
                    while (!token.IsCancellationRequested)
                    {
                        var client = await _httpListener.AcceptTcpClientAsync();
                        _ = Task.Run(() => HandleHttpRequestAsync(client, token));
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[DIAL HTTP Listener Error] {ex.Message}"); }
        }

        private static async Task HandleHttpRequestAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream))
                using (var writer = new StreamWriter(stream) { AutoFlush = true })
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) return;

                    var parts = line.Split(' ');
                    if (parts.Length < 2) return;

                    var method = parts[0];
                    var fullPath = parts[1];
                    var cleanPath = fullPath.Split('?')[0];

                    Debug.WriteLine($"[DIAL HTTP] スマホからリクエストを受信: {method} {fullPath}");

                    int contentLength = 0;
                    string headerLine;
                    while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync()))
                    {
                        if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(headerLine.Substring(15).Trim(), out contentLength);
                        }
                    }

                    string postBody = "";
                    if (contentLength > 0 && method == "POST")
                    {
                        char[] buffer = new char[contentLength];
                        await reader.ReadAsync(buffer, 0, contentLength);
                        postBody = new string(buffer);
                        Debug.WriteLine($"[DIAL HTTP] POSTデータ: {postBody}");
                    }

                    string localIp = GetLocalIpAddress();

                    string corsHeaders = "Access-Control-Allow-Origin: *\r\n" +
                                         "Access-Control-Allow-Methods: GET, POST, DELETE, OPTIONS\r\n" +
                                         "Access-Control-Allow-Headers: Content-Type, Authorization, X-Requested-With\r\n";

                    if (method == "OPTIONS")
                    {
                        await writer.WriteAsync($"HTTP/1.1 204 No Content\r\n{corsHeaders}Content-Length: 0\r\n\r\n");
                        return;
                    }

                    if (method == "GET" && cleanPath == "/dd.xml")
                    {
                        string body = $@"<?xml version=""1.0""?>
<root xmlns=""urn:schemas-upnp-org:device-1-0"">
  <specVersion><major>1</major><minor>0</minor></specVersion>
  <device>
    <deviceType>urn:dial-multiscreen-org:device:dial:1</deviceType>
    <friendlyName>MultiDesktopYoutube</friendlyName>
    <manufacturer>MultiDesktop</manufacturer>
    <modelName>MultiDesktop V1</modelName>
    <UDN>uuid:{Uuid}</UDN>
  </device>
</root>";
                        string headers = $"HTTP/1.1 200 OK\r\nContent-Type: application/xml\r\nApplication-URL: http://{localIp}:{_httpPort}/apps/\r\n{corsHeaders}Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n";
                        await writer.WriteAsync(headers + body);
                    }
                    else if (method == "GET" && cleanPath == "/apps/YouTube")
                    {
                        // ★修正：実際の起動状態をスマホに教える
                        string state = YoutubeTvWindowManager.IsYoutubeModeActive ? "running" : "stopped";
                        string body = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<service xmlns=""urn:dial-multiscreen-org:schemas:dial"">
  <name>YouTube</name>
  <options allowStop=""true""/>
  <state>{state}</state>
</service>";
                        string headers = $"HTTP/1.1 200 OK\r\nContent-Type: application/xml\r\n{corsHeaders}Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n";
                        await writer.WriteAsync(headers + body);
                    }
                    else if (method == "POST" && cleanPath == "/apps/YouTube")
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            // ★ 1. 通知を表示してユーザーにお知らせ！

                            // ★ 2. Youtubeモードが終了していても、自動で立ち上げる！
                            if (!YoutubeTvWindowManager.IsYoutubeModeActive)
                            {
                                YoutubeTvWindowManager.ShowAllWindows(); // 引数なしで呼ぶ（ペアリングしない）
                            }
                        });

                        // ★ 3. スマホ側には「接続成功したよ！」と嘘の返事(201 Created)をする
                        // これにより、スマホ側は一瞬「接続中」になるが、数秒後にタイムアウトして自然に切断される（自己解決）
                        string headers = $"HTTP/1.1 201 Created\r\n{corsHeaders}LOCATION: http://{localIp}:{_httpPort}/apps/YouTube/run\r\nContent-Length: 0\r\n\r\n";
                        await writer.WriteAsync(headers);
                    }
                    else if (method == "DELETE" && cleanPath.StartsWith("/apps/YouTube"))
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (YoutubeTvWindowManager.IsYoutubeModeActive) YoutubeTvWindowManager.CloseAllWindows();
                        });
                        string headers = $"HTTP/1.1 200 OK\r\n{corsHeaders}Content-Length: 0\r\n\r\n";
                        await writer.WriteAsync(headers);
                    }
                    else
                    {
                        await writer.WriteAsync($"HTTP/1.1 404 Not Found\r\n{corsHeaders}Content-Length: 0\r\n\r\n");
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[DIAL HTTP Request Error] {ex.Message}"); }
        }
    }
}