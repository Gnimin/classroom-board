using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using NEWCLASSroom.Models;

namespace NEWCLASSroom.Services
{
    /// <summary>把整份数据存成一个 JSON 文件，带自动备份和防抖保存。</summary>
    public class StorageService
    {
        private readonly object _lock = new object();
        private readonly System.Windows.Threading.DispatcherTimer _debounce;
        private AppData _pending;

        public string DataDirectory { get; private set; }
        public string DataFilePath { get; private set; }
        public string BackupDirectory { get; private set; }
        public string LastError { get; private set; }

        /// <summary>确定数据目录，并准备防抖保存的定时器。</summary>
        public StorageService()
        {
            DataDirectory = ResolveDataDirectory();
            DataFilePath = Path.Combine(DataDirectory, "classboard.json");
            BackupDirectory = Path.Combine(DataDirectory, "backup");

            _debounce = new System.Windows.Threading.DispatcherTimer();
            _debounce.Interval = TimeSpan.FromMilliseconds(800);
            _debounce.Tick += (s, e) =>
            {
                _debounce.Stop();
                AppData pending;
                lock (_lock) { pending = _pending; _pending = null; }
                if (pending != null) WriteToDisk(pending);
            };
        }

        /// <summary>优先用程序目录下的 data，不可写就退回用户目录。</summary>
        private static string ResolveDataDirectory()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dir = Path.Combine(baseDir, "data");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return dir;
            }
            catch
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NEWCLASSroom");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>读取数据文件，坏了就用默认数据并备份原文件。</summary>
        public AppData Load()
        {
            try
            {
                if (!File.Exists(DataFilePath))
                {
                    AppData fresh = AppData.CreateDefault();
                    WriteToDisk(fresh);
                    return fresh;
                }

                string json = File.ReadAllText(DataFilePath, Encoding.UTF8);
                AppData data = JsonConvert.DeserializeObject<AppData>(json);
                if (data == null) throw new InvalidDataException("配置文件内容为空");
                Normalize(data);
                return data;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                TryBackupCorruptFile();
                return AppData.CreateDefault();
            }
        }

        /// <summary>把读不出来的旧文件另存一份，避免直接丢数据。</summary>
        private void TryBackupCorruptFile()
        {
            try
            {
                if (!File.Exists(DataFilePath)) return;
                if (!Directory.Exists(BackupDirectory)) Directory.CreateDirectory(BackupDirectory);
                string name = "corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
                File.Copy(DataFilePath, Path.Combine(BackupDirectory, name), true);
            }
            catch { }
        }

        /// <summary>补齐旧版本文件里可能缺失的字段，避免空引用。</summary>
        public static void Normalize(AppData data)
        {
            if (data.Class == null) data.Class = new ClassInfo();
            if (data.Periods == null) data.Periods = new List<Period>();
            if (data.Courses == null) data.Courses = new List<CourseEntry>();
            if (data.Duty == null) data.Duty = new List<DutyDay>();
            if (data.Settings == null) data.Settings = new AppSettings();

            foreach (Period p in data.Periods)
            {
                if (string.IsNullOrWhiteSpace(p.Id)) p.Id = Guid.NewGuid().ToString("N").Substring(0, 8);
                if (p.Name == null) p.Name = "";
                if (p.Start == null) p.Start = "08:00";
                if (p.End == null) p.End = "08:40";
            }
            foreach (CourseEntry c in data.Courses)
            {
                if (c.Subject == null) c.Subject = "";
                if (c.Teacher == null) c.Teacher = "";
            }
            foreach (DutyDay d in data.Duty)
            {
                if (d.Members == null) d.Members = new List<DutyMember>();
                foreach (DutyMember m in d.Members)
                {
                    if (m.Name == null) m.Name = "";
                    if (m.Task == null) m.Task = "";
                }
            }
            if (data.Settings.Pin == null) data.Settings.Pin = "";
            if (data.Settings.PublicUrl == null) data.Settings.PublicUrl = "";
            if (data.Settings.TunnelCommand == null) data.Settings.TunnelCommand = "";
            string displayMode = data.Settings.DisplayMode;
            if (displayMode != "Wallpaper" && displayMode != "RightDocked")
                data.Settings.DisplayMode = "BottomDocked";
            if (data.Settings.Theme != "Light") data.Settings.Theme = "Dark";
            if (data.Settings.Port <= 0 || data.Settings.Port > 65535) data.Settings.Port = 8420;
            if (data.Settings.IdleMinutes < 1) data.Settings.IdleMinutes = 5;
            if (data.Settings.MessageDurationSeconds < 3) data.Settings.MessageDurationSeconds = 15;
        }

        /// <summary>请求保存，短时间内的多次请求会合并成一次写盘。</summary>
        public void Save(AppData data)
        {
            lock (_lock) { _pending = data; }
            _debounce.Stop();
            _debounce.Start();
        }

        /// <summary>立刻写盘，不等防抖。</summary>
        public void SaveNow(AppData data)
        {
            _debounce.Stop();
            lock (_lock) { _pending = null; }
            WriteToDisk(data);
        }

        /// <summary>先写临时文件再替换，避免写一半断电损坏。</summary>
        private void WriteToDisk(AppData data)
        {
            try
            {
                if (!Directory.Exists(DataDirectory)) Directory.CreateDirectory(DataDirectory);
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                string tmp = DataFilePath + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(DataFilePath)) File.Delete(DataFilePath);
                File.Move(tmp, DataFilePath);
                RotateBackups();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        /// <summary>每天留一份备份。</summary>
        private void RotateBackups()
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(DataDirectory);
                FileInfo[] files = dir.GetFiles("classboard.*.json")
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToArray();
                if (files.Length == 0)
                {
                    string stamp = DateTime.Now.ToString("yyyyMMdd");
                    string target = Path.Combine(DataDirectory, "classboard." + stamp + ".json");
                    if (!File.Exists(target)) File.Copy(DataFilePath, target, false);
                }
                else if (files[0].LastWriteTime.Date != DateTime.Today)
                {
                    string stamp = DateTime.Now.ToString("yyyyMMdd");
                    string target = Path.Combine(DataDirectory, "classboard." + stamp + ".json");
                    if (!File.Exists(target)) File.Copy(DataFilePath, target, false);
                }
            }
            catch { }
        }
    }
}
