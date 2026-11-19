using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NEWCLASSroom.Services
{
    /// <summary>开机自启，写在当前用户的 Run 键里，不需要管理员权限。</summary>
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "NEWCLASSroom";

        /// <summary>查注册表判断是否已经设了开机自启。</summary>
        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    object value = key.GetValue(ValueName);
                    return value != null;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>写入或删除开机自启项。</summary>
        public static bool Set(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return false;
                    if (enabled)
                    {
                        string exe = Application.ExecutablePath;
                        key.SetValue(ValueName, "\"" + exe + "\"");
                    }
                    else
                    {
                        if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
