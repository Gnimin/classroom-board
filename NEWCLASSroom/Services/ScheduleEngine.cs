using System;
using System.Collections.Generic;
using System.Linq;
using NEWCLASSroom.Models;

namespace NEWCLASSroom.Services
{
    public enum BoardState
    {
        /// <summary>还没有任何作息时间。</summary>
        NoSchedule,
        /// <summary>第一节课之前（含早读前）。</summary>
        BeforeSchool,
        /// <summary>正在上课。</summary>
        InLesson,
        /// <summary>课间。</summary>
        InBreak,
        /// <summary>午休。</summary>
        InLunch,
        /// <summary>当天的安排已全部结束。</summary>
        AfterSchool
    }

    /// <summary>今日课表里的一行。</summary>
    public class TimetableRow
    {
        public Period Period { get; set; }
        public string Subject { get; set; }
        public string Teacher { get; set; }
        public bool IsLesson { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsPast { get; set; }
        public string TimeText { get; set; }

        /// <summary>新建一行课表。</summary>
        public TimetableRow()
        {
            Subject = "";
            Teacher = "";
        }
    }

    /// <summary>某一时刻班牌需要展示的全部内容。</summary>
    public class BoardSnapshot
    {
        public BoardState State { get; set; }
        public Period CurrentPeriod { get; set; }
        public CourseEntry CurrentCourse { get; set; }
        public Period NextPeriod { get; set; }
        public CourseEntry NextCourse { get; set; }

        /// <summary>距下课或距上课的剩余时间。</summary>
        public TimeSpan Remaining { get; set; }
        public double Progress { get; set; }
        public bool ShowProgress { get; set; }

        public List<TimetableRow> Rows { get; set; }
        public List<DutyMember> DutyMembers { get; set; }

        /// <summary>状态角标文字，例如“正在上”。</summary>
        public string BadgeText { get; set; }
        /// <summary>主标题，例如“数学”或倒计时。</summary>
        public string HeadlineText { get; set; }
        /// <summary>副标题，例如“张老师 · 第3节 · 08:00 - 08:40”。</summary>
        public string SubText { get; set; }
        /// <summary>倒计时行，例如“距下课 12:34”。</summary>
        public string CountdownText { get; set; }

        /// <summary>新建一份空的显示快照。</summary>
        public BoardSnapshot()
        {
            Rows = new List<TimetableRow>();
            DutyMembers = new List<DutyMember>();
            BadgeText = "";
            HeadlineText = "";
            SubText = "";
            CountdownText = "";
        }
    }

    /// <summary>按作息时间和课表算出当前该显示什么。</summary>
    public class ScheduleEngine
    {
        private readonly AppData _data;

        /// <summary>绑定到一份课表数据上。</summary>
        public ScheduleEngine(AppData data)
        {
            _data = data;
        }

        /// <summary>把 1 到 7 转成周一到周日的简称。</summary>
        public static string DayName(int day)
        {
            switch (day)
            {
                case 1: return "周一";
                case 2: return "周二";
                case 3: return "周三";
                case 4: return "周四";
                case 5: return "周五";
                case 6: return "周六";
                case 7: return "周日";
                default: return "";
            }
        }

        /// <summary>把 1 到 7 转成星期一到星期日的全称。</summary>
        public static string FullDayName(int day)
        {
            switch (day)
            {
                case 1: return "星期一";
                case 2: return "星期二";
                case 3: return "星期三";
                case 4: return "星期四";
                case 5: return "星期五";
                case 6: return "星期六";
                case 7: return "星期日";
                default: return "";
            }
        }

        /// <summary>把剩余时间格式化成 05:05 或 01:05:05。</summary>
        public static string FormatCountdown(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            int totalHours = (int)span.TotalHours;
            if (span.TotalHours >= 1)
                return string.Format("{0:00}:{1:00}:{2:00}", totalHours, span.Minutes, span.Seconds);
            return string.Format("{0:00}:{1:00}", span.Minutes, span.Seconds);
        }

        /// <summary>把时长写成几小时几分钟这样的中文。</summary>
        public static string FormatChineseDuration(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            int hours = (int)span.TotalHours;
            int minutes = span.Minutes;
            List<string> parts = new List<string>();
            if (hours > 0) parts.Add(hours + " 小时");
            if (minutes > 0) parts.Add(minutes + " 分");
            if (parts.Count == 0) parts.Add(span.Seconds + " 秒");
            return string.Join(" ", parts);
        }

        /// <summary>取按开始时间排好序的节次列表。</summary>
        public List<Period> SortedPeriods()
        {
            return _data.Periods
                .OrderBy(p => p.StartTime)
                .ThenBy(p => p.EndTime)
                .ToList();
        }

        /// <summary>查某天某一节上什么课。</summary>
        public CourseEntry FindCourse(int day, string periodId)
        {
            return _data.Courses.FirstOrDefault(c => c.Day == day && c.PeriodId == periodId);
        }

        /// <summary>取某一天的值日安排。</summary>
        public List<DutyMember> DutyFor(DateTime date)
        {
            int day = (int)date.DayOfWeek;
            if (day == 0) day = 7;
            DutyDay dd = _data.Duty.FirstOrDefault(d => d.Day == day);
            if (dd == null) return new List<DutyMember>();
            return dd.Members.Where(m => !string.IsNullOrWhiteSpace(m.Name)).ToList();
        }

        /// <summary>根据当前时间算出班牌该显示的全部内容。</summary>
        public BoardSnapshot Build(DateTime now)
        {
            BoardSnapshot snap = new BoardSnapshot();
            List<Period> periods = SortedPeriods();

            if (periods.Count == 0)
            {
                snap.State = BoardState.NoSchedule;
                snap.BadgeText = "尚未设置";
                snap.HeadlineText = "作息时间表为空";
                snap.SubText = "请在设置里添加节次，或用手机端上传";
                snap.DutyMembers = DutyFor(now);
                return snap;
            }

            int today = (int)now.DayOfWeek;
            if (today == 0) today = 7;
            TimeSpan t = now.TimeOfDay;

            Period current = periods.FirstOrDefault(p => t >= p.StartTime && t < p.EndTime);
            Period next = periods.FirstOrDefault(p => p.StartTime > t);

            snap.CurrentPeriod = current;
            if (current != null)
            {
                snap.CurrentCourse = FindCourse(today, current.Id);
                snap.Remaining = current.EndTime - t;
                if (current.EndTime > current.StartTime)
                {
                    double total = (current.EndTime - current.StartTime).TotalSeconds;
                    double done = (t - current.StartTime).TotalSeconds;
                    snap.Progress = Math.Max(0, Math.Min(1, done / total));
                }
                snap.ShowProgress = true;
            }
            else if (next != null)
            {
                snap.NextPeriod = next;
                snap.NextCourse = FindCourse(today, next.Id);
                snap.Remaining = next.StartTime - t;
            }

            if (current != null)
            {
                switch (current.Kind)
                {
                    case PeriodKind.Lesson:
                        snap.State = BoardState.InLesson;
                        snap.BadgeText = "正在上";
                        snap.HeadlineText = snap.CurrentCourse != null && !string.IsNullOrWhiteSpace(snap.CurrentCourse.Subject)
                            ? snap.CurrentCourse.Subject
                            : current.Name;
                        break;
                    case PeriodKind.Lunch:
                        snap.State = BoardState.InLunch;
                        snap.BadgeText = "午休中";
                        snap.HeadlineText = "午休";
                        break;
                    default:
                        snap.State = BoardState.InBreak;
                        snap.BadgeText = current.Kind == PeriodKind.Break ? "课间" : "进行中";
                        snap.HeadlineText = current.Name;
                        break;
                }
                snap.SubText = BuildPeriodSubText(current, snap.CurrentCourse);
                snap.CountdownText = "距" + EndWord(current) + " " + FormatCountdown(snap.Remaining);
            }
            else if (next != null)
            {
                bool isFirst = periods.IndexOf(next) == 0;
                snap.State = isFirst ? BoardState.BeforeSchool : BoardState.InBreak;
                snap.BadgeText = isFirst ? "距离上学" : "下一节";
                snap.HeadlineText = BuildNextHeadline(next, snap.NextCourse);
                snap.SubText = BuildPeriodSubText(next, snap.NextCourse);
                snap.CountdownText = "距" + StartWord(next) + "还有 " + FormatCountdown(snap.Remaining);
            }
            else
            {
                snap.State = BoardState.AfterSchool;
                snap.BadgeText = "今日结束";
                snap.HeadlineText = "放学啦";
                snap.SubText = "今天的课程已经全部结束";
                snap.CountdownText = "";
            }

            snap.Rows = BuildRows(periods, today, current, t);
            snap.DutyMembers = DutyFor(now);
            return snap;
        }

        /// <summary>按节次类型选下课或结束这样的措辞。</summary>
        private string EndWord(Period p)
        {
            switch (p.Kind)
            {
                case PeriodKind.Lesson: return "下课";
                case PeriodKind.Lunch: return "午休结束";
                default: return "结束";
            }
        }

        /// <summary>按节次类型选上课或开始这样的措辞。</summary>
        private string StartWord(Period p)
        {
            switch (p.Kind)
            {
                case PeriodKind.Lesson: return "上课";
                case PeriodKind.Lunch: return "午休";
                default: return "开始";
            }
        }

        /// <summary>课间时显示下一节要上什么。</summary>
        private string BuildNextHeadline(Period p, CourseEntry course)
        {
            if (p.Kind == PeriodKind.Lesson)
                return course != null && !string.IsNullOrWhiteSpace(course.Subject) ? course.Subject : p.Name;
            return p.Name;
        }

        /// <summary>拼出老师、节次和时间这行副标题。</summary>
        private string BuildPeriodSubText(Period p, CourseEntry course)
        {
            List<string> parts = new List<string>();
            if (course != null && !string.IsNullOrWhiteSpace(course.Teacher))
                parts.Add(course.Teacher);
            parts.Add(p.Name);
            parts.Add(p.RangeText);
            return string.Join(" · ", parts);
        }

        /// <summary>生成今日课表的每一行数据。</summary>
        private List<TimetableRow> BuildRows(List<Period> periods, int today, Period current, TimeSpan now)
        {
            List<TimetableRow> rows = new List<TimetableRow>();
            foreach (Period p in periods)
            {
                if (p.Kind == PeriodKind.Break) continue;
                CourseEntry c = FindCourse(today, p.Id);
                TimetableRow row = new TimetableRow();
                row.Period = p;
                row.TimeText = p.RangeText;
                row.IsLesson = p.Kind == PeriodKind.Lesson;
                if (c != null)
                {
                    row.Subject = c.Subject;
                    row.Teacher = c.Teacher;
                }
                if (string.IsNullOrWhiteSpace(row.Subject))
                    row.Subject = p.Kind == PeriodKind.Lesson ? "—" : p.Name;

                row.IsCurrent = current != null && current.Id == p.Id;
                row.IsPast = !row.IsCurrent && now >= p.EndTime;
                rows.Add(row);
            }
            return rows;
        }
    }
}
