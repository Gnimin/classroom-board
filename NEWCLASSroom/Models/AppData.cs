using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace NEWCLASSroom.Models
{
    /// <summary>节次类型，决定班牌上如何呈现这一段时间。</summary>
    public enum PeriodKind
    {
        /// <summary>正式上课</summary>
        Lesson,
        /// <summary>课间休息</summary>
        Break,
        /// <summary>午休</summary>
        Lunch,
        /// <summary>其它（早读、自习、集会等）</summary>
        Other
    }

    /// <summary>学校作息时间表中的一个时间段。</summary>
    public class Period
    {
        public string Id { get; set; }
        public string Name { get; set; }
        /// <summary>HH:mm</summary>
        public string Start { get; set; }
        /// <summary>HH:mm</summary>
        public string End { get; set; }
        public PeriodKind Kind { get; set; }

        /// <summary>新建一个默认节次。</summary>
        public Period()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Name = "新节次";
            Start = "08:00";
            End = "08:40";
            Kind = PeriodKind.Lesson;
        }

        [JsonIgnore]
        public TimeSpan StartTime { get { return ParseTime(Start); } }

        [JsonIgnore]
        public TimeSpan EndTime { get { return ParseTime(End); } }

        [JsonIgnore]
        public string RangeText { get { return Start + " - " + End; } }

        /// <summary>把 08:00 这样的文本解析成时间。</summary>
        public static TimeSpan ParseTime(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return TimeSpan.Zero;
            TimeSpan ts;
            if (TimeSpan.TryParse(text.Trim(), out ts)) return ts;
            DateTime dt;
            if (DateTime.TryParse(text.Trim(), out dt)) return dt.TimeOfDay;
            return TimeSpan.Zero;
        }

        /// <summary>把时间格式化成 08:00 的样子。</summary>
        public static string FormatTime(TimeSpan ts)
        {
            return string.Format("{0:00}:{1:00}", (int)ts.TotalHours, ts.Minutes);
        }
    }

    /// <summary>某一天某一节上什么课。课表按星期固定，每周循环。</summary>
    public class CourseEntry
    {
        /// <summary>1=周一 ... 7=周日</summary>
        public int Day { get; set; }
        public string PeriodId { get; set; }
        public string Subject { get; set; }
        public string Teacher { get; set; }

        /// <summary>新建一条空的课程记录。</summary>
        public CourseEntry()
        {
            Subject = "";
            Teacher = "";
        }
    }

    public class DutyMember
    {
        public string Name { get; set; }
        public string Task { get; set; }

        /// <summary>新建一个空的值日成员。</summary>
        public DutyMember()
        {
            Name = "";
            Task = "";
        }
    }

    /// <summary>某一星期的值班安排。</summary>
    public class DutyDay
    {
        public int Day { get; set; }
        public List<DutyMember> Members { get; set; }

        /// <summary>新建某一天的值日安排。</summary>
        public DutyDay()
        {
            Members = new List<DutyMember>();
        }
    }

    public class ClassInfo
    {
        public string SchoolName { get; set; }
        public string ClassName { get; set; }
        public string Motto { get; set; }

        /// <summary>用默认的学校和班级名初始化。</summary>
        public ClassInfo()
        {
            SchoolName = "迎春中学";
            ClassName = "六年级(1)班";
            Motto = "";
        }
    }

    /// <summary>老师从手机或本机发来的一条消息。</summary>
    public class BannerMessage
    {
        public string Id { get; set; }
        public string Text { get; set; }
        /// <summary>紧急消息不自动消失，需要点“知道了”。</summary>
        public bool Urgent { get; set; }
        public DateTime CreatedAt { get; set; }
        /// <summary>停留秒数，0 表示用默认值。</summary>
        public int DurationSeconds { get; set; }
        public string Source { get; set; }

        /// <summary>新建一条待显示的消息。</summary>
        public BannerMessage()
        {
            Id = Guid.NewGuid().ToString("N");
            Text = "";
            CreatedAt = DateTime.Now;
            Source = "本机";
        }
    }

    public class AppSettings
    {
        /// <summary>BottomDocked = 置底窗口；Wallpaper = 挂到桌面 WorkerW 层。</summary>
        public string DisplayMode { get; set; }
        /// <summary>Dark = 深色；Light = 浅色。</summary>
        public string Theme { get; set; }
        public int MonitorIndex { get; set; }
        public bool ShowSeconds { get; set; }
        public bool ShowTimetable { get; set; }
        public bool ShowDuty { get; set; }
        public bool EnableIdleFullscreen { get; set; }
        public int IdleMinutes { get; set; }
        public bool AutoStart { get; set; }

        public int Port { get; set; }
        /// <summary>手机端访问口令，可为空表示不校验。</summary>
        public string Pin { get; set; }

        public bool SpeechEnabled { get; set; }
        public string SpeechVoice { get; set; }
        public int SpeechRate { get; set; }
        public int SpeechVolume { get; set; }
        public int MessageDurationSeconds { get; set; }

        /// <summary>隧道命令的默认值：指向软件目录下的 frp 文件夹，换台电脑也不用改。</summary>
        public static string DefaultTunnelCommand
        {
            get { return @"{app}\frp\frpc.exe -c {app}\frp\frpc.toml"; }
        }

        /// <summary>外网隧道命令。支持 {app}（软件目录）和 {port}（端口号）两个占位符。</summary>
        public string TunnelCommand { get; set; }
        public bool TunnelAutoStart { get; set; }

        /// <summary>隧道客户端不打印地址时，手动填写的公网地址。</summary>
        public string PublicUrl { get; set; }

        /// <summary>所有设置项的默认值。</summary>
        public AppSettings()
        {
            DisplayMode = "BottomDocked";
            Theme = "Dark";
            MonitorIndex = 0;
            ShowSeconds = true;
            ShowTimetable = true;
            ShowDuty = true;
            EnableIdleFullscreen = true;
            IdleMinutes = 5;
            AutoStart = false;

            Port = 8420;
            Pin = "1234";

            SpeechEnabled = true;
            SpeechVoice = "";
            SpeechRate = 0;
            SpeechVolume = 100;
            MessageDurationSeconds = 15;

            TunnelCommand = DefaultTunnelCommand;
            TunnelAutoStart = false;
            PublicUrl = "";
        }
    }

    /// <summary>整份数据，序列化为一个 JSON 文件。</summary>
    public class AppData
    {
        public int Version { get; set; }
        public ClassInfo Class { get; set; }
        public List<Period> Periods { get; set; }
        public List<CourseEntry> Courses { get; set; }
        public List<DutyDay> Duty { get; set; }
        public AppSettings Settings { get; set; }

        /// <summary>新建一份空数据。</summary>
        public AppData()
        {
            Version = 1;
            Class = new ClassInfo();
            Periods = new List<Period>();
            Courses = new List<CourseEntry>();
            Duty = new List<DutyDay>();
            Settings = new AppSettings();
        }

        /// <summary>首次运行时给一份能直接看到效果的示例数据。</summary>
        public static AppData CreateDefault()
        {
            AppData d = new AppData();
            d.Class.SchoolName = "迎春中学";
            d.Class.ClassName = "六年级(1)班";

            d.Periods.Add(new Period { Name = "早读", Start = "07:40", End = "08:00", Kind = PeriodKind.Other });
            d.Periods.Add(new Period { Name = "第1节", Start = "08:00", End = "08:40", Kind = PeriodKind.Lesson });
            d.Periods.Add(new Period { Name = "第2节", Start = "08:50", End = "09:30", Kind = PeriodKind.Lesson });
            d.Periods.Add(new Period { Name = "大课间", Start = "09:30", End = "10:00", Kind = PeriodKind.Break });
            d.Periods.Add(new Period { Name = "第3节", Start = "10:00", End = "10:40", Kind = PeriodKind.Lesson });
            d.Periods.Add(new Period { Name = "第4节", Start = "10:50", End = "11:30", Kind = PeriodKind.Lesson });
            d.Periods.Add(new Period { Name = "午休", Start = "11:30", End = "13:30", Kind = PeriodKind.Lunch });
            d.Periods.Add(new Period { Name = "第5节", Start = "13:30", End = "14:10", Kind = PeriodKind.Lesson });
            d.Periods.Add(new Period { Name = "第6节", Start = "14:20", End = "15:00", Kind = PeriodKind.Lesson });
            d.Periods.Add(new Period { Name = "第7节", Start = "15:10", End = "15:50", Kind = PeriodKind.Lesson });
            d.Periods.Add(new Period { Name = "课外活动", Start = "15:50", End = "16:30", Kind = PeriodKind.Other });

            string[] subjects = { "语文", "数学", "英语", "科学", "道德与法治", "体育", "美术", "音乐" };
            string[] teachers = { "王老师", "张老师", "李老师", "刘老师", "陈老师", "赵老师", "周老师", "吴老师" };
            List<Period> lessons = d.Periods.Where(p => p.Kind == PeriodKind.Lesson).ToList();
            for (int day = 1; day <= 5; day++)
            {
                for (int i = 0; i < lessons.Count; i++)
                {
                    int idx = (day * 3 + i) % 8;
                    d.Courses.Add(new CourseEntry
                    {
                        Day = day,
                        PeriodId = lessons[i].Id,
                        Subject = subjects[idx],
                        Teacher = teachers[idx]
                    });
                }
            }

            string[][] dutyNames =
            {
                new string[] { "陈思远", "林小雨", "周子涵" },
                new string[] { "王浩然", "李梦琪", "张一鸣" },
                new string[] { "刘子轩", "赵欣怡", "孙嘉豪" },
                new string[] { "吴梓萱", "郑俊杰", "黄雨欣" },
                new string[] { "徐子墨", "何嘉怡", "马天宇" }
            };
            string[] tasks = { "扫地", "擦黑板", "整理讲台" };
            for (int day = 1; day <= 5; day++)
            {
                DutyDay dd = new DutyDay { Day = day };
                for (int i = 0; i < dutyNames[day - 1].Length; i++)
                {
                    dd.Members.Add(new DutyMember { Name = dutyNames[day - 1][i], Task = tasks[i % tasks.Length] });
                }
                d.Duty.Add(dd);
            }

            return d;
        }
    }
}
