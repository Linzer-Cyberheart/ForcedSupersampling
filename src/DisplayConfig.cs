using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ForcedUpscaling
{
    class MonitorInfo
    {
        public string Device;   // GDI name, like \\.\DISPLAY1
        public string Name;     // what the monitor calls itself
        public bool Primary;
    }

    // Monitor names and the Windows scale (100%, 125%...) per monitor.
    //
    // There is no public API for the scale. The Settings app and tools like
    // SetDPI use the DisplayConfig "device info" requests -3 (get) and -4 (set),
    // so that is what we call too. Values are steps relative to the scale
    // Windows recommends for the current resolution: 0 is the recommended one,
    // the minimum is always 100%.
    static class DisplayConfig
    {
        const int GetSourceName = 1;
        const int GetTargetName = 2;
        const int GetDpi = -3;
        const int SetDpi = -4;

        const uint QueryOnlyActivePaths = 2;
        const int PathSize = 72;
        const int ModeSize = 64;

        [DllImport("user32.dll")]
        static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);

        [DllImport("user32.dll")]
        static extern int QueryDisplayConfig(uint flags, ref uint paths, byte[] pathInfo, ref uint modes, byte[] modeInfo, IntPtr topology);

        [DllImport("user32.dll")]
        static extern int DisplayConfigGetDeviceInfo(byte[] packet);

        [DllImport("user32.dll")]
        static extern int DisplayConfigSetDeviceInfo(byte[] packet);

        static byte[] Header(int type, int size, byte[] adapter, uint id)
        {
            byte[] p = new byte[size];
            BitConverter.GetBytes(type).CopyTo(p, 0);
            BitConverter.GetBytes(size).CopyTo(p, 4);
            adapter.CopyTo(p, 8);
            BitConverter.GetBytes(id).CopyTo(p, 16);
            return p;
        }

        static byte[] ActivePaths(out uint count)
        {
            uint modeCount;
            GetDisplayConfigBufferSizes(QueryOnlyActivePaths, out count, out modeCount);
            byte[] paths = new byte[count * PathSize];
            byte[] modes = new byte[modeCount * ModeSize];
            if (QueryDisplayConfig(QueryOnlyActivePaths, ref count, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                count = 0;
            return paths;
        }

        static string SourceName(byte[] adapter, uint sourceId)
        {
            byte[] packet = Header(GetSourceName, 84, adapter, sourceId);
            if (DisplayConfigGetDeviceInfo(packet) != 0) return null;
            return Encoding.Unicode.GetString(packet, 20, 64).TrimEnd('\0');
        }

        static string TargetName(byte[] adapter, uint targetId)
        {
            byte[] packet = Header(GetTargetName, 420, adapter, targetId);
            if (DisplayConfigGetDeviceInfo(packet) != 0) return "";
            return Encoding.Unicode.GetString(packet, 36, 128).TrimEnd('\0');
        }

        // active monitors, left to right as they sit on the desktop
        public static List<MonitorInfo> Monitors()
        {
            List<MonitorInfo> list = new List<MonitorInfo>();
            string primary = Display.Primary();

            uint count;
            byte[] paths = ActivePaths(out count);
            for (int i = 0; i < count; i++)
            {
                int offset = i * PathSize;

                byte[] sourceAdapter = new byte[8];
                Array.Copy(paths, offset, sourceAdapter, 0, 8);
                string device = SourceName(sourceAdapter, BitConverter.ToUInt32(paths, offset + 8));
                if (device == null || list.Exists(delegate(MonitorInfo m) { return m.Device == device; })) continue;

                byte[] targetAdapter = new byte[8];
                Array.Copy(paths, offset + 20, targetAdapter, 0, 8);
                string name = TargetName(targetAdapter, BitConverter.ToUInt32(paths, offset + 28));

                MonitorInfo info = new MonitorInfo();
                info.Device = device;
                info.Name = name.Length > 0 ? name : "Monitor";
                info.Primary = device == primary;
                list.Add(info);
            }

            list.Sort(delegate(MonitorInfo a, MonitorInfo b)
            {
                return Display.PositionX(a.Device).CompareTo(Display.PositionX(b.Device));
            });
            return list;
        }

        // finds the adapter/source pair behind a name like \\.\DISPLAY1
        static bool FindSource(string gdiName, out byte[] adapter, out uint sourceId)
        {
            adapter = new byte[8];
            sourceId = 0;

            uint count;
            byte[] paths = ActivePaths(out count);
            for (int i = 0; i < count; i++)
            {
                int offset = i * PathSize;
                byte[] luid = new byte[8];
                Array.Copy(paths, offset, luid, 0, 8);
                uint id = BitConverter.ToUInt32(paths, offset + 8);

                string name = SourceName(luid, id);
                if (string.Equals(name, gdiName, StringComparison.OrdinalIgnoreCase))
                {
                    adapter = luid;
                    sourceId = id;
                    return true;
                }
            }
            return false;
        }

        public static bool TryGetScale(string gdiName, out int min, out int current, out int max)
        {
            min = current = max = 0;
            byte[] adapter;
            uint id;
            if (!FindSource(gdiName, out adapter, out id)) return false;

            byte[] packet = Header(GetDpi, 32, adapter, id);
            if (DisplayConfigGetDeviceInfo(packet) != 0) return false;

            min = BitConverter.ToInt32(packet, 20);
            current = BitConverter.ToInt32(packet, 24);
            max = BitConverter.ToInt32(packet, 28);
            return true;
        }

        public static bool SetScale(string gdiName, int relative)
        {
            byte[] adapter;
            uint id;
            if (!FindSource(gdiName, out adapter, out id)) return false;

            byte[] packet = Header(SetDpi, 24, adapter, id);
            BitConverter.GetBytes(relative).CopyTo(packet, 20);
            return DisplayConfigSetDeviceInfo(packet) == 0;
        }
    }
}
