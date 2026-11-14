using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using System.Windows.Forms;

namespace ForcedUpscaling
{
    // Plain key=value file in %APPDATA%\ForcedUpscaling
    // The app used to be called Forced Upscaling. The folder, the autostart entry and the
    // internal names keep the old spelling so nobody loses their settings after updating.
    class Settings
    {
        readonly string path;
        readonly Dictionary<string, string> values = new Dictionary<string, string>();
        readonly object gate = new object();

        public static string Folder
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(root, "ForcedUpscaling");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public Settings()
        {
            path = Path.Combine(Folder, "config.ini");
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }

        public string Get(string key, string fallback)
        {
            lock (gate)
            {
                string v;
                return values.TryGetValue(key, out v) ? v : fallback;
            }
        }

        public int GetInt(string key, int fallback)
        {
            int n;
            return int.TryParse(Get(key, ""), out n) ? n : fallback;
        }

        public void Set(string key, string value)
        {
            lock (gate)
            {
                values[key] = value;
                Save();
            }
        }

        public void Remove(string key)
        {
            lock (gate)
            {
                values.Remove(key);
                Save();
            }
        }

        void Save()
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> kv in values) lines.Add(kv.Key + "=" + kv.Value);
            try { File.WriteAllLines(path, lines.ToArray()); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "ForcedUpscaling";

        public static bool IsOn
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(ValueName) != null;
            }
        }

        // If the autostart entry points to a file that is gone, or to an older name of this
        // exe in the same folder (the app was renamed), point it at the running exe.
        public static void Refresh()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    string current = k == null ? null : k.GetValue(ValueName) as string;
                    if (string.IsNullOrEmpty(current)) return;
                    string registered = current.Trim();
                    if (registered.StartsWith("\"")) registered = registered.Substring(1, Math.Max(0, registered.IndexOf('"', 1) - 1));
                    else if (registered.EndsWith(" --background")) registered = registered.Substring(0, registered.Length - " --background".Length);

                    string running = Application.ExecutablePath;
                    if (string.Equals(registered, running, StringComparison.OrdinalIgnoreCase)) return;
                    bool gone = !File.Exists(registered);
                    bool sameFolder = string.Equals(Path.GetDirectoryName(registered), Path.GetDirectoryName(running), StringComparison.OrdinalIgnoreCase);
                    if (gone || sameFolder) k.SetValue(ValueName, "\"" + running + "\" --background");
                }
            }
            catch (Exception ex) { Log.Write(ex); }
        }

        public static void Set(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) return;
                if (on) k.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" --background");
                else k.DeleteValue(ValueName, false);
            }
        }
    }

    static class Log
    {
        public static void Write(Exception ex)
        {
            try
            {
                string file = Path.Combine(Settings.Folder, "error.log");
                File.AppendAllText(file, DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }
}
