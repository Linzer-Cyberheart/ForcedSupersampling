using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ForcedUpscaling
{
    // Watches for the games that have a profile. While the mode is on, a game that
    // just started gets its fixes applied at the resolution picked in the window.
    class GameMods
    {
        readonly Engine engine;
        readonly Settings settings;
        readonly Dictionary<int, bool> seen = new Dictionary<int, bool>();
        readonly string[] names;
        volatile bool stopping;

        public GameMods(Engine engine, Settings settings)
        {
            this.engine = engine;
            this.settings = settings;

            List<string> list = new List<string>();
            foreach (GameProfile p in Games.All)
                if (!list.Contains(p.ProcessName)) list.Add(p.ProcessName);
            names = list.ToArray();
        }

        public void Start()
        {
            Thread watcher = new Thread(Watch);
            watcher.IsBackground = true;
            watcher.Start();
        }

        public void Stop()
        {
            stopping = true;
        }

        void Watch()
        {
            while (!stopping)
            {
                try { Look(); }
                catch (Exception ex) { Log.Write(ex); }
                Thread.Sleep(30);
            }
        }

        void Look()
        {
            List<int> alive = new List<int>();
            foreach (string name in names)
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    int id = p.Id;
                    p.Dispose();
                    alive.Add(id);
                    if (seen.ContainsKey(id)) continue;
                    seen[id] = true;
                    Consider(id);
                }
            }

            List<int> gone = new List<int>();
            foreach (int id in seen.Keys) if (!alive.Contains(id)) gone.Add(id);
            foreach (int id in gone) seen.Remove(id);
        }

        void Consider(int pid)
        {
            string path = ProcessMemory.ImagePath(pid);
            if (path == null) return;
            long size;
            try { size = new FileInfo(path).Length; }
            catch (IOException) { return; }

            GameProfile profile = null;
            foreach (GameProfile p in Games.All)
                if (p.ExeSize == size && string.Equals(Path.GetFileNameWithoutExtension(path), p.ProcessName, StringComparison.OrdinalIgnoreCase))
                    profile = p;
            if (profile == null) return;

            // only while the mode is on, and at the resolution picked in the app
            if (!engine.IsOn) return;
            int width = engine.Width, height = engine.Height;
            GameProfile chosen = profile;

            Thread job = new Thread(delegate()
            {
                try
                {
                    using (ProcessMemory process = ProcessMemory.Open(pid))
                    {
                        if (process != null) chosen.Apply(process, width, height, settings);
                    }
                }
                catch (Exception ex) { Log.Write(ex); }
            });
            job.IsBackground = true;
            job.Start();
        }
    }
}
