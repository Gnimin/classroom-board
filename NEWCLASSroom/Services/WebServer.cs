using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NEWCLASSroom.Models;

namespace NEWCLASSroom.Services
{
    /// <summary>班牌对外暴露的能力，Web 服务通过它读写数据，不直接碰 UI。</summary>
    public interface IBoardHost
    {
        AppData Data { get; }
        void PersistNow();
        BoardSnapshot CurrentSnapshot();
        void PushMessage(BannerMessage message);
        List<BannerMessage> RecentMessages();
        string DeviceName();
    }

    /// <summary>
    /// 自己写的极简 HTTP 服务器。用 TcpListener 而不是 HttpListener，
    /// 这样普通用户权限就能监听局域网端口，不需要 netsh urlacl 或管理员权限。
    /// </summary>
    public class WebServer : IDisposable
    {
        private readonly IBoardHost _host;
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private string _token;

        public int Port { get; private set; }
        public bool IsRunning { get { return _running; } }
        public string LastError { get; private set; }

        /// <summary>绑定到提供数据的班牌宿主上。</summary>
        public WebServer(IBoardHost host)
        {
            _host = host;
            _token = Guid.NewGuid().ToString("N");
        }

        /// <summary>在指定端口开始监听。</summary>
        public void Start(int port)
        {
            Stop();
            Port = port;
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                _running = true;
                _acceptThread = new Thread(AcceptLoop);
                _acceptThread.IsBackground = true;
                _acceptThread.Start();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _running = false;
            }
        }

        /// <summary>停止监听并断开连接。</summary>
        public void Stop()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); }
            catch { }
            _listener = null;
            _acceptThread = null;
        }

        /// <summary>停止服务并释放监听器。</summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>后台线程不断接受新连接。</summary>
        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch
                {
                    if (!_running) return;
                    continue;
                }

                TcpClient captured = client;
                ThreadPool.QueueUserWorkItem(state => HandleClient(captured));
            }
        }

        /// <summary>解析一个 HTTP 请求并交给路由处理。</summary>
        private void HandleClient(TcpClient client)
        {
            try
            {
                client.ReceiveTimeout = 8000;
                client.SendTimeout = 8000;
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] buffer = new byte[8192];
                    MemoryStream received = new MemoryStream();
                    int headerEnd = -1;

                    while (true)
                    {
                        int read = stream.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        received.Write(buffer, 0, read);
                        byte[] data = received.ToArray();
                        headerEnd = IndexOfHeaderEnd(data);
                        if (headerEnd >= 0) break;
                        if (received.Length > 512 * 1024) break;
                    }

                    if (headerEnd < 0) return;
                    byte[] all = received.ToArray();
                    string headerText = Encoding.UTF8.GetString(all, 0, headerEnd);
                    string[] lines = headerText.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                    if (lines.Length == 0) return;

                    string[] requestLine = lines[0].Split(' ');
                    if (requestLine.Length < 2) return;
                    string method = requestLine[0].ToUpperInvariant();
                    string rawPath = requestLine[1];
                    string path = rawPath;
                    string query = "";
                    int qIndex = rawPath.IndexOf('?');
                    if (qIndex >= 0)
                    {
                        path = rawPath.Substring(0, qIndex);
                        query = rawPath.Substring(qIndex + 1);
                    }

                    Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int colon = lines[i].IndexOf(':');
                        if (colon > 0)
                        {
                            headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
                        }
                    }

                    int contentLength = 0;
                    string clValue;
                    if (headers.TryGetValue("Content-Length", out clValue)) int.TryParse(clValue, out contentLength);
                    if (contentLength > 4 * 1024 * 1024) contentLength = 0;

                    byte[] body = new byte[0];
                    int bodyStart = headerEnd + 4;
                    int available = all.Length - bodyStart;
                    if (contentLength > 0)
                    {
                        MemoryStream bodyStream = new MemoryStream();
                        if (available > 0) bodyStream.Write(all, bodyStart, Math.Min(available, contentLength));
                        while (bodyStream.Length < contentLength)
                        {
                            int read = stream.Read(buffer, 0, Math.Min(buffer.Length, (int)(contentLength - bodyStream.Length)));
                            if (read <= 0) break;
                            bodyStream.Write(buffer, 0, read);
                        }
                        body = bodyStream.ToArray();
                    }

                    HandleRequest(stream, method, path, query, headers, body);
                }
            }
            catch
            {
                // 单个连接出错不影响服务
            }
        }

        /// <summary>找出请求头结束的位置。</summary>
        private static int IndexOfHeaderEnd(byte[] data)
        {
            for (int i = 0; i + 3 < data.Length; i++)
            {
                if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10)
                    return i;
            }
            return -1;
        }

        private void HandleRequest(NetworkStream stream, string method, string path, string query,
            Dictionary<string, string> headers, byte[] body)
        {
            try
            {
                if (path == "/" || path == "/index.html")
                {
                    WriteText(stream, 200, "text/html; charset=utf-8", WebAssets.IndexHtml());
                    return;
                }
                if (path == "/favicon.ico")
                {
                    WriteText(stream, 204, "text/plain", "");
                    return;
                }
                if (path == "/api/ping")
                {
                    WriteJson(stream, 200, new JObject { { "ok", true }, { "name", _host.DeviceName() } });
                    return;
                }
                if (path == "/api/login" && method == "POST")
                {
                    HandleLogin(stream, body);
                    return;
                }

                string token;
                headers.TryGetValue("X-Token", out token);
                if (!string.Equals(token, _token, StringComparison.Ordinal))
                {
                    WriteJson(stream, 401, new JObject { { "ok", false }, { "error", "unauthorized" } });
                    return;
                }

                if (path == "/api/state" && method == "GET") { HandleState(stream); return; }
                if (path == "/api/config" && method == "GET") { HandleGetConfig(stream); return; }
                if (path == "/api/config" && method == "POST") { HandlePostConfig(stream, body); return; }
                if (path == "/api/message" && method == "POST") { HandlePostMessage(stream, body); return; }
                if (path == "/api/messages" && method == "GET") { HandleHistory(stream); return; }

                WriteJson(stream, 404, new JObject { { "ok", false }, { "error", "not found" } });
            }
            catch (Exception ex)
            {
                try
                {
                    WriteJson(stream, 500, new JObject { { "ok", false }, { "error", ex.Message } });
                }
                catch { }
            }
        }

        /// <summary>校验口令并返回访问令牌。</summary>
        private void HandleLogin(NetworkStream stream, byte[] body)
        {
            string pin = "";
            try
            {
                JObject json = ParseBody(body);
                if (json != null) pin = (string)json["pin"] ?? "";
            }
            catch { }

            string expected = (_host.Data.Settings.Pin ?? "").Trim();
            if (expected.Length > 0 && pin.Trim() != expected)
            {
                WriteJson(stream, 200, new JObject { { "ok", false }, { "error", "口令不正确" } });
                return;
            }

            WriteJson(stream, 200, new JObject
            {
                { "ok", true },
                { "token", _token },
                { "className", _host.Data.Class.ClassName ?? "" },
                { "schoolName", _host.Data.Class.SchoolName ?? "" }
            });
        }

        /// <summary>把班牌当前的显示内容返回给手机端。</summary>
        private void HandleState(NetworkStream stream)
        {
            BoardSnapshot snap = _host.CurrentSnapshot();
            AppData data = _host.Data;

            JArray rows = new JArray();
            foreach (TimetableRow row in snap.Rows)
            {
                JObject item = new JObject();
                item["name"] = row.Period.Name;
                item["time"] = row.TimeText;
                item["subject"] = row.Subject;
                item["teacher"] = row.Teacher;
                item["lesson"] = row.IsLesson;
                item["current"] = row.IsCurrent;
                item["past"] = row.IsPast;
                rows.Add(item);
            }

            JArray duty = new JArray();
            foreach (DutyMember m in snap.DutyMembers)
            {
                duty.Add(new JObject { { "name", m.Name }, { "task", m.Task } });
            }

            JObject result = new JObject();
            result["ok"] = true;
            result["className"] = data.Class.ClassName ?? "";
            result["schoolName"] = data.Class.SchoolName ?? "";
            result["badge"] = snap.BadgeText;
            result["headline"] = snap.HeadlineText;
            result["sub"] = snap.SubText;
            result["countdown"] = snap.CountdownText;
            result["rows"] = rows;
            result["duty"] = duty;
            result["serverTime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            WriteJson(stream, 200, result);
        }

        /// <summary>返回课表、值日、时间表等全部配置。</summary>
        private void HandleGetConfig(NetworkStream stream)
        {
            AppData data = _host.Data;
            JObject result = new JObject();
            result["ok"] = true;
            result["classInfo"] = JObject.FromObject(data.Class);
            result["periods"] = JArray.FromObject(data.Periods
                .OrderBy(p => p.StartTime)
                .Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    start = p.Start,
                    end = p.End,
                    kind = p.Kind.ToString()
                })
                .ToList());
            result["courses"] = JArray.FromObject(data.Courses);
            result["duty"] = JArray.FromObject(data.Duty);
            result["settings"] = new JObject
            {
                { "messageDurationSeconds", data.Settings.MessageDurationSeconds },
                { "speechEnabled", data.Settings.SpeechEnabled },
                { "pin", data.Settings.Pin ?? "" },
                { "port", data.Settings.Port }
            };
            WriteJson(stream, 200, result);
        }

        /// <summary>接收手机端提交的配置改动并落盘。</summary>
        private void HandlePostConfig(NetworkStream stream, byte[] body)
        {
            JObject json = ParseBody(body);
            if (json == null)
            {
                WriteJson(stream, 400, new JObject { { "ok", false }, { "error", "内容为空" } });
                return;
            }

            AppData data = _host.Data;

            if (json["classInfo"] != null)
            {
                ClassInfo info = json["classInfo"].ToObject<ClassInfo>();
                if (info != null)
                {
                    data.Class.SchoolName = info.SchoolName ?? data.Class.SchoolName;
                    data.Class.ClassName = info.ClassName ?? data.Class.ClassName;
                    data.Class.Motto = info.Motto ?? "";
                }
            }

            if (json["periods"] != null)
            {
                List<Period> periods = json["periods"].ToObject<List<Period>>();
                if (periods != null)
                {
                    foreach (Period p in periods)
                    {
                        if (string.IsNullOrWhiteSpace(p.Id)) p.Id = Guid.NewGuid().ToString("N").Substring(0, 8);
                    }
                    data.Periods = periods;
                }
            }

            if (json["courses"] != null)
            {
                List<CourseEntry> courses = json["courses"].ToObject<List<CourseEntry>>();
                if (courses != null) data.Courses = courses;
            }

            if (json["duty"] != null)
            {
                List<DutyDay> duty = json["duty"].ToObject<List<DutyDay>>();
                if (duty != null) data.Duty = duty;
            }

            if (json["settings"] != null)
            {
                AppSettings incoming = json["settings"].ToObject<AppSettings>();
                if (incoming != null)
                {
                    data.Settings.SpeechEnabled = incoming.SpeechEnabled;
                    data.Settings.MessageDurationSeconds = Math.Max(3, incoming.MessageDurationSeconds);
                    if (!string.IsNullOrWhiteSpace(incoming.Pin)) data.Settings.Pin = incoming.Pin;
                }
            }

            _host.PersistNow();
            WriteJson(stream, 200, new JObject { { "ok", true } });
        }

        /// <summary>接收手机端发来的消息并推给班牌。</summary>
        private void HandlePostMessage(NetworkStream stream, byte[] body)
        {
            JObject json = ParseBody(body);
            if (json == null)
            {
                WriteJson(stream, 400, new JObject { { "ok", false }, { "error", "内容为空" } });
                return;
            }

            string text = ((string)json["text"] ?? "").Trim();
            if (text.Length == 0)
            {
                WriteJson(stream, 400, new JObject { { "ok", false }, { "error", "消息不能为空" } });
                return;
            }
            if (text.Length > 200) text = text.Substring(0, 200);

            BannerMessage msg = new BannerMessage();
            msg.Text = text;
            msg.Urgent = json["urgent"] != null && (bool)json["urgent"];
            msg.DurationSeconds = json["duration"] != null ? (int)json["duration"] : 0;
            msg.Source = "手机";
            _host.PushMessage(msg);

            WriteJson(stream, 200, new JObject { { "ok", true }, { "id", msg.Id } });
        }

        /// <summary>返回最近发过的消息列表。</summary>
        private void HandleHistory(NetworkStream stream)
        {
            JArray list = new JArray();
            foreach (BannerMessage m in _host.RecentMessages())
            {
                list.Add(new JObject
                {
                    { "text", m.Text },
                    { "time", m.CreatedAt.ToString("MM-dd HH:mm") },
                    { "urgent", m.Urgent },
                    { "source", m.Source }
                });
            }
            WriteJson(stream, 200, new JObject { { "ok", true }, { "items", list } });
        }

        /// <summary>把请求体解析成 JSON。</summary>
        private static JObject ParseBody(byte[] body)
        {
            if (body == null || body.Length == 0) return null;
            string text = Encoding.UTF8.GetString(body).Trim();
            if (text.Length == 0) return null;
            return JObject.Parse(text);
        }

        /// <summary>回一个 JSON 响应。</summary>
        private static void WriteJson(NetworkStream stream, int status, JObject payload)
        {
            WriteText(stream, status, "application/json; charset=utf-8", payload.ToString(Formatting.None));
        }

        /// <summary>回一个文本或网页响应。</summary>
        private static void WriteText(NetworkStream stream, int status, string contentType, string content)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content ?? "");
            StringBuilder sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(StatusText(status)).Append("\r\n");
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(bytes.Length).Append("\r\n");
            sb.Append("Cache-Control: no-store\r\n");
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            sb.Append("Connection: close\r\n\r\n");
            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(head, 0, head.Length);
            if (bytes.Length > 0) stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        /// <summary>把状态码翻译成英文短句。</summary>
        private static string StatusText(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 404: return "Not Found";
                default: return "Internal Server Error";
            }
        }

        /// <summary>给出局域网内可访问的地址，尽量挑有线/无线网卡而不是虚拟网卡。</summary>
        public static string LocalAddress()
        {
            try
            {
                List<IPAddress> candidates = new List<IPAddress>();
                foreach (NetworkInterfaceInfo info in NetworkInterfaceInfo.Enumerate())
                {
                    if (info.IsLoopback) continue;
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    candidates.Add(info.Address);
                }
                if (candidates.Count == 0) return "127.0.0.1";
                IPAddress chosen = candidates
                    .OrderBy(a =>
                    {
                        string s = a.ToString();
                        if (s.StartsWith("192.168.")) return 0;
                        if (s.StartsWith("10.")) return 1;
                        if (s.StartsWith("172.")) return 1;
                        if (s.StartsWith("169.254.")) return 5;
                        return 3;
                    })
                    .First();
                return chosen.ToString();
            }
            catch
            {
                return "127.0.0.1";
            }
        }
    }

    internal class NetworkInterfaceInfo
    {
        public IPAddress Address { get; set; }
        public bool IsLoopback { get; set; }

        /// <summary>列出所有网卡上的地址。</summary>
        public static List<NetworkInterfaceInfo> Enumerate()
        {
            List<NetworkInterfaceInfo> list = new List<NetworkInterfaceInfo>();
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface nic in
                    System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    foreach (System.Net.NetworkInformation.UnicastIPAddressInformation addr in
                        nic.GetIPProperties().UnicastAddresses)
                    {
                        list.Add(new NetworkInterfaceInfo
                        {
                            Address = addr.Address,
                            IsLoopback = IPAddress.IsLoopback(addr.Address)
                        });
                    }
                }
            }
            catch { }
            return list;
        }
    }
}
