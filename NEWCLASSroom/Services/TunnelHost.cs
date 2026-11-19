using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace NEWCLASSroom.Services
{
    /// <summary>
    /// 外网访问靠一条隧道进程（cloudflared / frpc 等）实现。
    /// 这里不绑定具体厂商，只负责把配置好的命令跑起来、并从输出里抓出公网地址。
    /// 隧道进程被放进一个作业对象，班牌退出（包括崩溃）时会被系统一并结束，
    /// 不会留下占着隧道名的孤儿进程。
    /// </summary>
    public class TunnelHost : IDisposable
    {
        private Process _process;
        private readonly List<string> _log = new List<string>();
        private static readonly Regex UrlPattern = new Regex(@"https?://[A-Za-z0-9\-\._~:/?#\[\]@!$&'()*+,;=%]+", RegexOptions.Compiled);

        #region 作业对象（保证子进程不残留）

        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        private const int JobObjectExtendedLimitInformation = 9;

        private static IntPtr _jobHandle;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string name);

        [DllImport("kernel32.dll")]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint infoLength);

        [DllImport("kernel32.dll")]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        /// <summary>创建带关闭即杀标志的作业对象。</summary>
        private static void EnsureJobObject()
        {
            if (_jobHandle != IntPtr.Zero) return;

            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return;

            JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, buffer, false);
                SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)length);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            _jobHandle = job;
        }

        /// <summary>把隧道进程加入作业，让它随班牌一起结束。</summary>
        private static void BindToJob(Process process)
        {
            try
            {
                EnsureJobObject();
                if (_jobHandle == IntPtr.Zero) return;
                AssignProcessToJobObject(_jobHandle, process.Handle);
            }
            catch { }
        }

        #endregion

        public string PublicUrl { get; private set; }
        public string LastError { get; private set; }

        /// <summary>状态变化（启动、输出、退出）。</summary>
        public event EventHandler Changed;

        public bool IsRunning
        {
            get { return _process != null && !_process.HasExited; }
        }

        public List<string> Log
        {
            get { lock (_log) { return new List<string>(_log); } }
        }

        /// <summary>展开命令模板并启动隧道进程。</summary>
        public void Start(string commandTemplate, int port)
        {
            Stop();
            PublicUrl = null;
            LastError = null;

            if (string.IsNullOrWhiteSpace(commandTemplate))
            {
                LastError = "还没有配置外网隧道命令";
                RaiseChanged();
                return;
            }

            string command = ExpandTemplate(commandTemplate, port);
            List<string> parts = SplitCommandLine(command);
            if (parts.Count == 0)
            {
                LastError = "命令为空";
                RaiseChanged();
                return;
            }

            // 写的是绝对路径却找不到文件，直接说清楚，别等到报“系统找不到指定的文件”
            if ((parts[0].IndexOf('\\') >= 0 || parts[0].IndexOf('/') >= 0) && !File.Exists(parts[0]))
            {
                LastError = "找不到隧道客户端：" + parts[0] +
                            "。如果用的是便携版，请确认 frp 文件夹和班牌程序放在同一个目录下。";
                RaiseChanged();
                return;
            }

            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = parts[0];
                for (int i = 1; i < parts.Count; i++) info.Arguments += (i == 1 ? "" : " ") + Quote(parts[i]);
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.StandardOutputEncoding = Encoding.UTF8;
                info.StandardErrorEncoding = Encoding.UTF8;

                // 以可执行文件所在目录为工作目录，配置里的相对路径（证书等）才能正确解析
                try
                {
                    string full = Path.GetFullPath(parts[0]);
                    string dir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) info.WorkingDirectory = dir;
                }
                catch { }

                _process = new Process();
                _process.StartInfo = info;
                _process.OutputDataReceived += OnData;
                _process.ErrorDataReceived += OnData;
                _process.EnableRaisingEvents = true;
                _process.Exited += (s, e) =>
                {
                    Append("隧道进程已退出");
                    RaiseChanged();
                };
                _process.Start();
                BindToJob(_process);
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                Append("已启动：" + command);
                RaiseChanged();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _process = null;
                RaiseChanged();
            }
        }

        /// <summary>把命令里的 {app} 和 {port} 换成真实值。{app} 是软件所在目录，换机器不用改配置。</summary>
        public static string ExpandTemplate(string template, int port)
        {
            if (template == null) return "";
            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string result = template.Replace("{app}", appDir);
            result = result.Replace("{APP}", appDir);
            result = result.Replace("{port}", port.ToString());
            result = result.Replace("{PORT}", port.ToString());
            return result;
        }

        /// <summary>参数里有空格就补上引号。</summary>
        private static string Quote(string value)
        {
            if (value.IndexOf(' ') >= 0) return "\"" + value + "\"";
            return value;
        }

        /// <summary>读取隧道输出，抓公网地址并识别常见错误。</summary>
        private void OnData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            Append(e.Data);

            string line = e.Data;

            // 隧道名被另一个客户端占用，是最常见也最容易看不懂的失败
            if (line.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                LastError = "这条隧道已经被别的客户端占用了。请先退出 LoLi 客户端，" +
                            "或者在任务管理器里结束残留的 frpc.exe 进程，再点一次启动。";
                RaiseChanged();
                return;
            }

            if (line.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (line.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 line.IndexOf("错误", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                LastError = "隧道 token 校验失败，配置文件里的 token 可能抄错了，请重新复制一次。";
                RaiseChanged();
                return;
            }

            Match match = UrlPattern.Match(line);
            if (match.Success)
            {
                string url = match.Value.TrimEnd('.', ',', ')');
                if (url != PublicUrl)
                {
                    PublicUrl = url;
                    RaiseChanged();
                }
            }
        }

        /// <summary>往日志里追加一行。</summary>
        private void Append(string line)
        {
            lock (_log)
            {
                _log.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
                if (_log.Count > 200) _log.RemoveRange(0, _log.Count - 200);
            }
        }

        /// <summary>通知界面刷新隧道状态。</summary>
        private void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>结束隧道进程。</summary>
        public void Stop()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(2000);
                }
            }
            catch { }
            finally
            {
                _process = null;
            }
        }

        /// <summary>释放隧道进程。</summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>按 Windows 命令行习惯切分，支持双引号包裹的路径。</summary>
        public static List<string> SplitCommandLine(string command)
        {
            List<string> result = new List<string>();
            StringBuilder current = new StringBuilder();
            bool inQuotes = false;
            foreach (char ch in command)
            {
                if (ch == '"') { inQuotes = !inQuotes; continue; }
                if (!inQuotes && char.IsWhiteSpace(ch))
                {
                    if (current.Length > 0) { result.Add(current.ToString()); current.Length = 0; }
                    continue;
                }
                current.Append(ch);
            }
            if (current.Length > 0) result.Add(current.ToString());
            return result;
        }
    }
}
