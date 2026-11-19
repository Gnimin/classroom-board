using System;
using System.Collections.Generic;
using System.Windows;
using NEWCLASSroom.Models;
using NEWCLASSroom.Services;

namespace NEWCLASSroom
{
    /// <summary>全局状态中枢：数据、引擎、服务都在这里串起来。</summary>
    public class AppState : IBoardHost
    {
        private static AppState _instance;

        public static AppState Instance
        {
            get { return _instance; }
        }

        public AppData Data { get; private set; }
        public StorageService Storage { get; private set; }
        public ScheduleEngine Engine { get; private set; }
        public MessageService Messages { get; private set; }
        public SpeechService Speech { get; private set; }
        public WebServer Server { get; private set; }
        public TunnelHost Tunnel { get; private set; }

        /// <summary>数据被修改，界面需要刷新。</summary>
        public event EventHandler DataChanged;

        /// <summary>设置项变化，可能需要重开窗口或重启服务。</summary>
        public event EventHandler SettingsChanged;

        public bool ServerStarted { get { return Server != null && Server.IsRunning; } }

        /// <summary>班牌当前是否正占据桌面。隐藏状态下才会启用“无操作全屏”。</summary>
        public bool BoardVisible { get; set; }

        /// <summary>初始化各服务和数据，并把自己登记成全局单例。</summary>
        public AppState()
        {
            Storage = new StorageService();
            Data = Storage.Load();
            Engine = new ScheduleEngine(Data);
            Messages = new MessageService();
            Speech = new SpeechService();
            Server = new WebServer(this);
            Tunnel = new TunnelHost();
            BoardVisible = true;

            _instance = this;
        }

        /// <summary>启动手机端 HTTP 服务，必要时拉起外网隧道。</summary>
        public void StartServices()
        {
            try
            {
                Server.Start(Data.Settings.Port);
            }
            catch { }

            if (Data.Settings.TunnelAutoStart && !string.IsNullOrWhiteSpace(Data.Settings.TunnelCommand))
            {
                Tunnel.Start(Data.Settings.TunnelCommand, Data.Settings.Port);
            }
        }

        /// <summary>停止 HTTP 服务和隧道进程。</summary>
        public void StopServices()
        {
            try { Server.Stop(); } catch { }
            try { Tunnel.Stop(); } catch { }
        }

        /// <summary>按当前时间算出班牌要显示的整份快照。</summary>
        public BoardSnapshot CurrentSnapshot()
        {
            return Engine.Build(DateTime.Now);
        }

        /// <summary>立刻落盘，并通知界面重画。</summary>
        public void PersistNow()
        {
            Storage.SaveNow(Data);
            // 手机端改完数据后，班牌要立刻重画
            EventHandler handler = DataChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>请求保存，短时间内的多次修改会合并成一次写盘。</summary>
        public void Save()
        {
            Storage.Save(Data);
        }

        /// <summary>数据改过之后调用，通知界面重画并落盘。</summary>
        public void NotifyDataChanged()
        {
            Save();
            EventHandler handler = DataChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>设置变了：通知外部做相应的窗口调整。</summary>
        public void NotifySettingsChanged()
        {
            Save();
            EventHandler handler = SettingsChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>从任意线程推一条消息进入弹窗队列。</summary>
        public void PushMessage(BannerMessage message)
        {
            if (message == null) return;
            Application app = Application.Current;
            if (app == null)
            {
                Messages.Enqueue(message);
                return;
            }
            app.Dispatcher.BeginInvoke(new Action(() => Messages.Enqueue(message)));
        }

        /// <summary>取最近发过的消息历史。</summary>
        public List<BannerMessage> RecentMessages()
        {
            return Messages.History;
        }

        /// <summary>返回这台电脑的名字，给手机端辨认用。</summary>
        public string DeviceName()
        {
            try
            {
                return Environment.MachineName;
            }
            catch
            {
                return "classroom";
            }
        }

        public string LanUrl
        {
            get
            {
                return "http://" + WebServer.LocalAddress() + ":" + Data.Settings.Port + "/";
            }
        }

        /// <summary>隧道客户端自动报出来的公网地址，没有就用手动填的那个。</summary>
        public string PublicUrl
        {
            get
            {
                if (Tunnel != null && !string.IsNullOrWhiteSpace(Tunnel.PublicUrl))
                    return Tunnel.PublicUrl.Trim();

                string manual = Data.Settings.PublicUrl;
                return string.IsNullOrWhiteSpace(manual) ? null : manual.Trim();
            }
        }

        /// <summary>手机端该用哪个地址（优先公网），没有就返回局域网地址。</summary>
        public string PreferredUrl
        {
            get
            {
                string pub = PublicUrl;
                if (!string.IsNullOrWhiteSpace(pub)) return pub;
                return LanUrl;
            }
        }

        /// <summary>改了端口之后重启 HTTP 服务。</summary>
        public void RestartServer()
        {
            try { Server.Stop(); } catch { }
            try { Server.Start(Data.Settings.Port); } catch { }
        }

        /// <summary>释放语音、HTTP 和隧道等资源。</summary>
        public void Dispose()
        {
            try { Speech.Dispose(); } catch { }
            try { Server.Dispose(); } catch { }
            try { Tunnel.Dispose(); } catch { }
        }
    }
}
