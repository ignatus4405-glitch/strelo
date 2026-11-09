using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using StreloReminder.Models;

namespace StreloReminder.Services
{
    public delegate List<ReminderItem> LanSyncMergeCallback(List<ReminderItem> remotePhoneReminders, string phoneIpAddress);
    public delegate List<ReminderItem> LanGetRemindersCallback();

    /// <summary>
    /// Сервис локальной синхронизации по домашней/офисной сети Wi-Fi (LAN) без участия внешних серверов.
    /// Включает:
    /// 1) UDP-ответчик автообнаружения ПК в сети (порт 45891) для мгновенного поиска компьютера с телефона.
    /// 2) Встроенный легковесный HTTP/JSON сервер на базе TcpListener (порт 45892), не требующий прав администратора.
    /// </summary>
    public class LanSyncService : IDisposable
    {
        public const int DiscoveryUdpPort = 45891;
        public const int DefaultHttpPort = 45892;
        public const string DiscoveryRequestMagic = "STRELO_DISCOVER_V1";
        public const string DiscoveryResponsePrefix = "STRELO_PC_V1|";

        private readonly LanSyncMergeCallback _onMergeCallback;
        private readonly LanGetRemindersCallback _onGetRemindersCallback;

        private bool _isRunning;
        private int _httpPort = DefaultHttpPort;
        private UdpClient _udpListener;
        private TcpListener _tcpListener;
        private string _lastConnectedPhoneIp = string.Empty;
        private DateTime _lastSyncTime = DateTime.MinValue;

        public LanSyncService(LanSyncMergeCallback onMergeCallback, LanGetRemindersCallback onGetRemindersCallback)
        {
            _onMergeCallback = onMergeCallback;
            _onGetRemindersCallback = onGetRemindersCallback;
        }

        public bool IsRunning
        {
            get { return _isRunning; }
        }

        public int HttpPort
        {
            get { return _httpPort; }
        }

        public string LastConnectedPhoneIp
        {
            get { return _lastConnectedPhoneIp; }
        }

        public DateTime LastSyncTime
        {
            get { return _lastSyncTime; }
        }

        public void Start(int port)
        {
            if (_isRunning)
                return;

            _httpPort = port > 0 ? port : DefaultHttpPort;
            _isRunning = true;

            try
            {
                _udpListener = new UdpClient(DiscoveryUdpPort);
                _udpListener.EnableBroadcast = true;
                Thread udpThread = new Thread(RunUdpDiscoveryLoop);
                udpThread.IsBackground = true;
                udpThread.Start();
            }
            catch
            {
            }

            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, _httpPort);
                _tcpListener.Start();
                Thread tcpThread = new Thread(RunTcpHttpServerLoop);
                tcpThread.IsBackground = true;
                tcpThread.Start();
            }
            catch
            {
                _isRunning = false;
            }
        }

        public void Stop()
        {
            _isRunning = false;
            try
            {
                if (_udpListener != null)
                {
                    _udpListener.Close();
                    _udpListener = null;
                }
            }
            catch
            {
            }

            try
            {
                if (_tcpListener != null)
                {
                    _tcpListener.Stop();
                    _tcpListener = null;
                }
            }
            catch
            {
            }
        }

        private void RunUdpDiscoveryLoop()
        {
            while (_isRunning && _udpListener != null)
            {
                try
                {
                    IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udpListener.Receive(ref remoteEp);
                    if (data == null || data.Length == 0)
                        continue;

                    string message = Encoding.UTF8.GetString(data).Trim();
                    if (message.StartsWith(DiscoveryRequestMagic, StringComparison.OrdinalIgnoreCase))
                    {
                        string localIp = GetPrimaryLanIp();
                        string pcName = SanitizeName(Environment.MachineName);
                        string reply = DiscoveryResponsePrefix + pcName + "|" + _httpPort + "|" + localIp;
                        byte[] replyBytes = Encoding.UTF8.GetBytes(reply);
                        _udpListener.Send(replyBytes, replyBytes.Length, remoteEp);
                    }
                }
                catch
                {
                    if (!_isRunning)
                        break;
                    Thread.Sleep(200);
                }
            }
        }

        private void RunTcpHttpServerLoop()
        {
            while (_isRunning && _tcpListener != null)
            {
                try
                {
                    TcpClient client = _tcpListener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate { HandleTcpHttpClient(client); });
                }
                catch
                {
                    if (!_isRunning)
                        break;
                    Thread.Sleep(200);
                }
            }
        }

        private void HandleTcpHttpClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 10000;
                    client.SendTimeout = 10000;

                    string remoteIp = string.Empty;
                    IPEndPoint remoteEp = client.Client.RemoteEndPoint as IPEndPoint;
                    if (remoteEp != null)
                    {
                        remoteIp = remoteEp.Address.ToString();
                    }

                    using (NetworkStream stream = client.GetStream())
                    {
                        string method;
                        string path;
                        string body;
                        if (!ReadHttpRequest(stream, out method, out path, out body))
                        {
                            return;
                        }

                        if (path.StartsWith("/strelo/ping", StringComparison.OrdinalIgnoreCase) || path == "/")
                        {
                            string pingJson = string.Format(
                                "{{\"status\":\"ok\",\"pcName\":\"{0}\",\"ip\":\"{1}\",\"port\":{2},\"version\":\"{3}\"}}",
                                SanitizeName(Environment.MachineName),
                                GetPrimaryLanIp(),
                                _httpPort,
                                UpdateService.CurrentVersion);
                            WriteHttpResponse(stream, "200 OK", "application/json; charset=utf-8", pingJson);
                        }
                        else if (path.StartsWith("/strelo/reminders", StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                        {
                            List<ReminderItem> current = _onGetRemindersCallback != null
                                ? _onGetRemindersCallback()
                                : new List<ReminderItem>();
                            string json = StorageService.SerializeRemindersToJson(current);
                            WriteHttpResponse(stream, "200 OK", "application/json; charset=utf-8", json);
                        }
                        else if (path.StartsWith("/strelo/sync", StringComparison.OrdinalIgnoreCase) &&
                                 (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase)))
                        {
                            List<ReminderItem> phoneReminders = StorageService.DeserializeRemindersFromJson(body);
                            List<ReminderItem> merged = _onMergeCallback != null
                                ? _onMergeCallback(phoneReminders, remoteIp)
                                : phoneReminders;

                            _lastConnectedPhoneIp = remoteIp;
                            _lastSyncTime = DateTime.Now;

                            string mergedJson = StorageService.SerializeRemindersToJson(merged);
                            WriteHttpResponse(stream, "200 OK", "application/json; charset=utf-8", mergedJson);
                        }
                        else
                        {
                            WriteHttpResponse(stream, "404 Not Found", "text/plain; charset=utf-8", "Not Found");
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private static bool ReadHttpRequest(NetworkStream stream, out string method, out string path, out string body)
        {
            method = "GET";
            path = "/";
            body = string.Empty;

            List<byte> headerBytes = new List<byte>(1024);
            byte[] single = new byte[1];
            int matchState = 0;

            while (headerBytes.Count < 16384)
            {
                int read = stream.Read(single, 0, 1);
                if (read <= 0)
                    break;
                byte b = single[0];
                headerBytes.Add(b);

                if ((matchState == 0 || matchState == 2) && b == (byte)'\r')
                    matchState++;
                else if ((matchState == 1 || matchState == 3) && b == (byte)'\n')
                    matchState++;
                else
                    matchState = 0;

                if (matchState == 4)
                    break;
            }

            if (headerBytes.Count == 0)
                return false;

            string headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            string[] lines = headerText.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                return false;

            string[] firstLineParts = lines[0].Split(' ');
            if (firstLineParts.Length >= 2)
            {
                method = firstLineParts[0].Trim();
                path = firstLineParts[1].Trim();
            }

            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    string val = line.Substring("Content-Length:".Length).Trim();
                    int.TryParse(val, out contentLength);
                }
            }

            if (contentLength > 0 && contentLength <= 10 * 1024 * 1024)
            {
                byte[] bodyBytes = new byte[contentLength];
                int offset = 0;
                while (offset < contentLength)
                {
                    int r = stream.Read(bodyBytes, offset, contentLength - offset);
                    if (r <= 0)
                        break;
                    offset += r;
                }
                body = Encoding.UTF8.GetString(bodyBytes, 0, offset);
            }

            return true;
        }

        private static void WriteHttpResponse(NetworkStream stream, string status, string contentType, string body)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
            StringBuilder sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
            sb.Append("Connection: close\r\n");
            sb.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (bodyBytes.Length > 0)
            {
                stream.Write(bodyBytes, 0, bodyBytes.Length);
            }
            stream.Flush();
        }

        public static string GetPrimaryLanIp()
        {
            List<string> ips = GetLocalLanIpAddresses();
            if (ips.Count > 0)
                return ips[0];
            return "127.0.0.1";
        }

        public static List<string> GetLocalLanIpAddresses()
        {
            List<string> preferred = new List<string>();
            List<string> fallback = new List<string>();

            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < interfaces.Length; i++)
                {
                    NetworkInterface ni = interfaces[i];
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;

                    IPInterfaceProperties props = ni.GetIPProperties();
                    UnicastIPAddressInformationCollection unicast = props.UnicastAddresses;
                    for (int j = 0; j < unicast.Count; j++)
                    {
                        IPAddress addr = unicast[j].Address;
                        if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr))
                        {
                            string ip = addr.ToString();
                            if (ip.StartsWith("169.254."))
                                continue;

                            if (ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172."))
                            {
                                if (!preferred.Contains(ip))
                                    preferred.Add(ip);
                            }
                            else
                            {
                                if (!fallback.Contains(ip))
                                    fallback.Add(ip);
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            preferred.AddRange(fallback);
            return preferred;
        }

        private static string SanitizeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "Windows-PC";
            return raw.Replace("|", "-").Replace("\"", "").Trim();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
