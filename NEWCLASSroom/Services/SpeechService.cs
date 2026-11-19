using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Synthesis;
using NEWCLASSroom.Models;

namespace NEWCLASSroom.Services
{
    /// <summary>用系统自带的 SAPI 语音朗读，Windows 中文系统一般已带中文语音。</summary>
    public class SpeechService : IDisposable
    {
        private SpeechSynthesizer _synth;
        private bool _available;

        public string LastError { get; private set; }
        public bool IsAvailable { get { return _available; } }

        /// <summary>初始化系统语音引擎。</summary>
        public SpeechService()
        {
            try
            {
                _synth = new SpeechSynthesizer();
                _synth.SetOutputToDefaultAudioDevice();
                _available = true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _available = false;
            }
        }

        /// <summary>列出系统里装好的语音包。</summary>
        public static List<VoiceInfo> InstalledVoices()
        {
            List<VoiceInfo> list = new List<VoiceInfo>();
            try
            {
                using (SpeechSynthesizer s = new SpeechSynthesizer())
                {
                    foreach (InstalledVoice v in s.GetInstalledVoices())
                    {
                        if (v.Enabled) list.Add(v.VoiceInfo);
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>优先挑中文语音，没有就退回到系统默认。</summary>
        public static string PickChineseVoice(IEnumerable<VoiceInfo> voices, string preferred)
        {
            List<VoiceInfo> all = voices.ToList();
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                VoiceInfo exact = all.FirstOrDefault(v => v.Name == preferred);
                if (exact != null) return exact.Name;
            }
            VoiceInfo zh = all.FirstOrDefault(v => v.Culture != null &&
                (v.Culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
                 v.Culture.ThreeLetterWindowsLanguageName == "CHS"));
            if (zh != null) return zh.Name;
            return all.Count > 0 ? all[0].Name : "";
        }

        /// <summary>朗读一段文字，会打断上一条。</summary>
        public void Speak(string text, AppSettings settings)
        {
            if (!_available || _synth == null) return;
            if (settings == null || !settings.SpeechEnabled) return;
            if (string.IsNullOrWhiteSpace(text)) return;

            try
            {
                _synth.SpeakAsyncCancelAll();

                List<VoiceInfo> voices = InstalledVoices();
                string voiceName = PickChineseVoice(voices, settings.SpeechVoice);
                if (!string.IsNullOrWhiteSpace(voiceName))
                {
                    try { _synth.SelectVoice(voiceName); }
                    catch { }
                }

                _synth.Rate = Math.Max(-10, Math.Min(10, settings.SpeechRate));
                _synth.Volume = Math.Max(0, Math.Min(100, settings.SpeechVolume));
                _synth.SpeakAsync(text);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        /// <summary>停止正在朗读的内容。</summary>
        public void Stop()
        {
            try { if (_synth != null) _synth.SpeakAsyncCancelAll(); }
            catch { }
        }

        /// <summary>释放语音引擎。</summary>
        public void Dispose()
        {
            try
            {
                if (_synth != null)
                {
                    _synth.SpeakAsyncCancelAll();
                    _synth.Dispose();
                    _synth = null;
                }
            }
            catch { }
        }
    }
}
