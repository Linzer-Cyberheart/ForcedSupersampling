using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ForcedUpscaling
{
    // Reads and sets the Windows scale (100%, 125%...) for a monitor.
    //
    // There is no public API for this. The Settings app and tools like SetDPI
    // use the DisplayConfig "device info" requests -3 (get) and -4 (set), so
    // that is what we call too. Values are steps relative to the scale Windows
    // recommends for the current resolution: 0 is the recommended one, the
    // minimum is always 100%.
    static class DpiScale
    {
        const int GetSourceName = 1;
        const int GetDpi = -3;
        const int SetDpi = -4;

        [DllImport("user32.dll")]
        static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);

        [DllImport("user32.dll")]
        static extern int QueryDisplayConfig(uint flags, ref uint paths, byte[] pathInfo, ref uint modes, byte[] modeInfo, IntPtr topology);

        [DllImport("user32.dll")]
        static extern int DisplayConfigGetDeviceInfo(byte[] packet);

        [DllImport("user32.dll")]
        static extern int DisplayConfigSetDeviceInfo(byte[] packet);

        const uint QueryOnlyActivePaths = 2;
        const int PathSize = 72;
        const int ModeSize = 64;

        static byte[] Header(int type, int size, byte[] adapter, uint id)
        {
            byte[] p = new byte[size];
            BitConverter.GetBytes(type).CopyTo(p, 0);
            BitConverter.GetBytes(size).CopyTo(p, 4);
            adapter.CopyTo(p, 8);
            BitConverter.GetBytes(id).CopyTo(p, 16);
            return p;
        }

        // finds the adapter/source pair behind a name like \\.\DISPLAY1
        static bool FindSource(string gdiName, out byte[] adapter, out uint sourceId)
        {
            adapter = new byte[8];
            sourceId = 0;

            uint pathCount, modeCount;
            GetDisplayConfigBufferSizes(QueryOnlyActivePaths, out pathCount, out modeCount);
            byte[] paths = new byte[pathCount * PathSize];
            byte[] modes = new byte[modeCount * ModeSize];
            if (QueryDisplayConfig(QueryOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                return false;

            for (int i = 0; i < pathCount; i++)
            {
                int offset = i * PathSize;
                byte[] luid = new byte[8];
                Array.Copy(paths, offset, luid, 0, 8);
                uint id = BitConverter.ToUInt32(paths, offset + 8);

                byte[] packet = Header(GetSourceName, 84, luid, id);
                if (DisplayConfigGetDeviceInfo(packet) != 0) continue;

                string name = Encoding.Unicode.GetString(packet, 20, 64).TrimEnd('\0');
                if (string.Equals(name, gdiName, StringComparison.OrdinalIgnoreCase))
                {
                    adapter = luid;
                    sourceId = id;
                    return true;
                }
            }
            return false;
        }

        public static bool TryGet(string gdiName, out int min, out int current, out int max)
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

        public static bool Set(string gdiName, int relative)
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
