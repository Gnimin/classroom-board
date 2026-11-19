using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using NEWCLASSroom.Models;
using NEWCLASSroom.Services;

internal class FakeHost : IBoardHost
{
    public AppData Data { get; set; }
    public BannerMessage Last { get; private set; }
    public List<BannerMessage> Items = new List<BannerMessage>();

    public void PersistNow() { }

    public BoardSnapshot CurrentSnapshot()
    {
        return new ScheduleEngine(Data).Build(DateTime.Now);
    }

    public void PushMessage(BannerMessage message)
    {
        Last = message;
        Items.Insert(0, message);
    }

    public List<BannerMessage> RecentMessages() { return Items; }

    public string DeviceName() { return "smoke-board"; }
}

internal class SmokeTest
{
    private static int _pass;
    private static int _fail;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { _pass++; Console.WriteLine("  [OK]   " + name); }
        else { _fail++; Console.WriteLine("  [FAIL] " + name + (detail.Length > 0 ? "  -> " + detail : "")); }
    }

    private static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("== 课程判定 ==");
        TestSchedule();

        Console.WriteLine();
        Console.WriteLine("== 手机端接口 ==");
        TestWeb();

        Console.WriteLine();
        Console.WriteLine(string.Format("通过 {0} 项，失败 {1} 项", _pass, _fail));
        Environment.Exit(_fail == 0 ? 0 : 1);
    }

    private static void TestSchedule()
    {
        AppData data = AppData.CreateDefault();
        ScheduleEngine engine = new ScheduleEngine(data);
        DateTime day = new DateTime(2026, 11, 5);

        BoardSnapshot s1 = engine.Build(day.AddHours(8).AddMinutes(20));
        Check("08:20 正在上课", s1.State == BoardState.InLesson, s1.State.ToString());
        Check("08:20 标题是科目", s1.HeadlineText.Length > 0, s1.HeadlineText);
        Check("08:20 距下课约 20 分钟", Math.Abs(s1.Remaining.TotalMinutes - 20) < 1.5,
            s1.Remaining.ToString());
        Check("08:20 进度约 50%", Math.Abs(s1.Progress - 0.5) < 0.1, s1.Progress.ToString("P0"));

        BoardSnapshot s2 = engine.Build(day.AddHours(8).AddMinutes(45));
        Check("08:45 处于课间", s2.State == BoardState.InBreak, s2.State.ToString());
        Check("08:45 指向下一节", s2.NextPeriod != null && s2.NextPeriod.Name == "第2节",
            s2.NextPeriod != null ? s2.NextPeriod.Name : "null");
        Check("08:45 距上课约 5 分钟", Math.Abs(s2.Remaining.TotalMinutes - 5) < 1.5,
            s2.Remaining.ToString());

        BoardSnapshot s3 = engine.Build(day.AddHours(12));
        Check("12:00 处于午休", s3.State == BoardState.InLunch, s3.State.ToString());

        BoardSnapshot s4 = engine.Build(day.AddHours(7));
        Check("07:00 还未开始", s4.State == BoardState.BeforeSchool, s4.State.ToString());

        BoardSnapshot s5 = engine.Build(day.AddHours(18));
        Check("18:00 今日结束", s5.State == BoardState.AfterSchool, s5.State.ToString());

        Check("课表行数正确（去掉大课间）", s5.Rows.Count == 10, s5.Rows.Count.ToString());

        int lessons = 0;
        foreach (TimetableRow row in s5.Rows) if (row.IsLesson) lessons++;
        Check("正式课程 7 节", lessons == 7, lessons.ToString());

        List<DutyMember> duty = engine.DutyFor(day);
        Check("周四有值日安排", duty.Count == 3, duty.Count.ToString());

        string countdown = ScheduleEngine.FormatCountdown(TimeSpan.FromSeconds(305));
        Check("倒计时格式 05:05", countdown == "05:05", countdown);

        AppData empty = new AppData();
        BoardSnapshot s6 = new ScheduleEngine(empty).Build(day);
        Check("没有作息表时给出提示", s6.State == BoardState.NoSchedule, s6.State.ToString());
    }

    private static void TestWeb()
    {
        FakeHost host = new FakeHost();
        host.Data = AppData.CreateDefault();

        const int port = 18420;
        WebServer server = new WebServer(host);
        server.Start(port);
        Check("服务已启动", server.IsRunning, server.LastError ?? "");
        if (!server.IsRunning) return;

        Thread.Sleep(200);
        string baseUrl = "http://127.0.0.1:" + port;

        using (HttpClient client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromSeconds(5);

            string html = client.GetStringAsync(baseUrl + "/").Result;
            Check("首页返回页面", html.Contains("<html") && html.Contains("发送消息到班牌"),
                html.Length.ToString());

            string ping = client.GetStringAsync(baseUrl + "/api/ping").Result;
            Check("ping 返回设备名", ping.Contains("smoke-board"), ping);

            HttpRequestMessage unauth = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/state");
            HttpResponseMessage unauthResp = client.SendAsync(unauth).Result;
            Check("未登录访问被拒绝", (int)unauthResp.StatusCode == 401, ((int)unauthResp.StatusCode).ToString());

            StringContent loginBody = new StringContent("{\"pin\":\"1234\"}", Encoding.UTF8, "application/json");
            HttpResponseMessage loginResp = client.PostAsync(baseUrl + "/api/login", loginBody).Result;
            JObject login = JObject.Parse(loginResp.Content.ReadAsStringAsync().Result);
            Check("口令正确可以登录", (bool)login["ok"], login.ToString());
            string token = (string)login["token"];

            StringContent badBody = new StringContent("{\"pin\":\"0000\"}", Encoding.UTF8, "application/json");
            JObject bad = JObject.Parse(client.PostAsync(baseUrl + "/api/login", badBody).Result
                .Content.ReadAsStringAsync().Result);
            Check("口令错误被拒绝", !(bool)bad["ok"], bad.ToString());

            HttpRequestMessage stateReq = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/state");
            stateReq.Headers.Add("X-Token", token);
            JObject state = JObject.Parse(client.SendAsync(stateReq).Result.Content.ReadAsStringAsync().Result);
            Check("state 带班级名", (string)state["className"] == "六年级(1)班", state.ToString());
            Check("state 带课表行", ((JArray)state["rows"]).Count > 0, "");

            HttpRequestMessage msgReq = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/message");
            msgReq.Headers.Add("X-Token", token);
            msgReq.Content = new StringContent("{\"text\":\"李明同学来办公室\",\"urgent\":true}",
                Encoding.UTF8, "application/json");
            JObject msg = JObject.Parse(client.SendAsync(msgReq).Result.Content.ReadAsStringAsync().Result);
            Check("消息发送成功", (bool)msg["ok"], msg.ToString());
            Check("班牌收到消息内容", host.Last != null && host.Last.Text == "李明同学来办公室",
                host.Last == null ? "null" : host.Last.Text);
            Check("紧急标记正确", host.Last != null && host.Last.Urgent, "");

            HttpRequestMessage cfgReq = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/config");
            cfgReq.Headers.Add("X-Token", token);
            JObject cfg = JObject.Parse(client.SendAsync(cfgReq).Result.Content.ReadAsStringAsync().Result);
            Check("配置含时间表", ((JArray)cfg["periods"]).Count == 11, ((JArray)cfg["periods"]).Count.ToString());

            JObject newPeriods = new JObject();
            newPeriods["periods"] = new JArray
            {
                new JObject { { "id", "p1" }, { "name", "第1节" }, { "start", "08:00" }, { "end", "08:40" }, { "kind", "Lesson" } }
            };
            HttpRequestMessage postCfg = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/config");
            postCfg.Headers.Add("X-Token", token);
            postCfg.Content = new StringContent(newPeriods.ToString(), Encoding.UTF8, "application/json");
            JObject saved = JObject.Parse(client.SendAsync(postCfg).Result.Content.ReadAsStringAsync().Result);
            Check("时间表保存成功", (bool)saved["ok"], saved.ToString());
            Check("时间表已写入数据", host.Data.Periods.Count == 1, host.Data.Periods.Count.ToString());
        }

        server.Stop();
        Check("服务已停止", !server.IsRunning, "");
    }
}
