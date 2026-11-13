using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ForcedUpscaling
{
    class VideoMode
    {
        public int Width;
        public int Height;
        public int Hz;
    }

    // Wrapper around the classic display settings calls. Nothing in here is
    // written to the registry, so a reboot always puts the monitor back.
    static class Display
    {
        const int CurrentSettings = -1;
        const int RegistrySettings = -2;
        const int AttachedFlag = 0x1;
        const int PrimaryFlag = 0x4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DisplayDevice
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
            public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevicesW(string device, uint index, ref DisplayDevice dd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettingsW(string device, int mode, ref DevMode dm);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int ChangeDisplaySettingsExW(string device, ref DevMode dm, IntPtr hwnd, uint flags, IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
        static extern int ResetDisplaySettings(string device, IntPtr dm, IntPtr hwnd, uint flags, IntPtr param);

        static DevMode NewDevMode()
        {
            DevMode dm = new DevMode();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DevMode));
            return dm;
        }

        static VideoMode Convert(DevMode dm)
        {
            VideoMode m = new VideoMode();
            m.Width = dm.dmPelsWidth;
            m.Height = dm.dmPelsHeight;
            m.Hz = dm.dmDisplayFrequency;
            return m;
        }

        public static string Primary()
        {
            for (uint i = 0; i < 16; i++)
            {
                DisplayDevice dd = new DisplayDevice();
                dd.cb = Marshal.SizeOf(typeof(DisplayDevice));
                if (!EnumDisplayDevicesW(null, i, ref dd, 0)) break;
                bool attached = (dd.StateFlags & AttachedFlag) != 0;
                bool primary = (dd.StateFlags & PrimaryFlag) != 0;
                if (attached && primary) return dd.DeviceName;
            }
            return null;
        }

        // what the monitor is doing right now
        public static VideoMode Current(string device)
        {
            DevMode dm = NewDevMode();
            EnumDisplaySettingsW(device, CurrentSettings, ref dm);
            return Convert(dm);
        }

        // what Windows has stored as the normal mode (our changes never touch this)
        public static VideoMode Saved(string device)
        {
            DevMode dm = NewDevMode();
            EnumDisplaySettingsW(device, RegistrySettings, ref dm);
            return Convert(dm);
        }

        // Modes the driver offers that are bigger than the saved one and have the
        // same aspect ratio. These are the DSR resolutions. Biggest first.
        public static List<VideoMode> Larger(string device, VideoMode saved)
        {
            SortedDictionary<long, VideoMode> found = new SortedDictionary<long, VideoMode>();
            double ratio = (double)saved.Width / saved.Height;
            for (int i = 0; ; i++)
            {
                DevMode dm = NewDevMode();
                if (!EnumDisplaySettingsW(device, i, ref dm)) break;
                if (dm.dmBitsPerPel != 32) continue;
                if (dm.dmPelsWidth <= saved.Width) continue;
                if (Math.Abs((double)dm.dmPelsWidth / dm.dmPelsHeight - ratio) > 0.01) continue;
                VideoMode m = Convert(dm);
                found[(long)m.Width * 100000 + m.Height] = m;
            }
            List<VideoMode> list = new List<VideoMode>(found.Values);
            list.Reverse();
            return list;
        }

        // Returns 0 on success. No CDS_UPDATEREGISTRY, so it is temporary.
        public static int Apply(string device, int width, int height, int hz)
        {
            for (int i = 0; ; i++)
            {
                DevMode dm = NewDevMode();
                if (!EnumDisplaySettingsW(device, i, ref dm)) break;
                bool match = dm.dmPelsWidth == width && dm.dmPelsHeight == height
                    && dm.dmDisplayFrequency == hz && dm.dmBitsPerPel == 32;
                if (match) return ChangeDisplaySettingsExW(device, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
            }
            return -100;
        }

        public static int Reset()
        {
            return ResetDisplaySettings(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        }
    }
}
