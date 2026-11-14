using System;
using System.Text;
using System.Runtime.InteropServices;

namespace ForcedUpscaling
{
    // Small wrapper around another process's memory, used by the per-game fixes.
    // The app itself runs as 64-bit; the games are either 32-bit (ImageBase) or 64-bit (ImageBase64).
    class ProcessMemory : IDisposable
    {
        const uint QueryInfo = 0x0400, VmRead = 0x0010, VmWrite = 0x0020, VmOperation = 0x0008, SuspendResume = 0x0800;
        const uint QueryLimited = 0x1000;

        readonly IntPtr handle;
        readonly int pid;

        ProcessMemory(IntPtr handle, int pid) { this.handle = handle; this.pid = pid; }

        public int Pid { get { return pid; } }

        public static ProcessMemory Open(int pid)
        {
            IntPtr h = OpenProcess(QueryInfo | VmRead | VmWrite | VmOperation | SuspendResume, false, pid);
            return h == IntPtr.Zero ? null : new ProcessMemory(h, pid);
        }

        // Image base of a 64-bit process: ImageBaseAddress in its PEB.
        public long ImageBase64()
        {
            long[] info = new long[6];   // PROCESS_BASIC_INFORMATION
            int returned;
            if (NtQueryInformationProcessBasic(handle, 0, info, info.Length * 8, out returned) != 0 || info[1] == 0) return 0;
            byte[] b = new byte[8];
            return Read(info[1] + 0x10, b) ? BitConverter.ToInt64(b, 0) : 0;
        }

        // Size of the client area of the game's main window, 0x0 while it has none yet.
        public static bool ClientSize(int pid, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(pid))
                {
                    p.Refresh();
                    RECT r;
                    if (p.MainWindowHandle == IntPtr.Zero || !GetClientRect(p.MainWindowHandle, out r)) return false;
                    width = r.Right; height = r.Bottom;
                    return width > 0 && height > 0;
                }
            }
            catch (ArgumentException) { return false; }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out RECT rect);
        [DllImport("ntdll.dll", EntryPoint = "NtQueryInformationProcess")]
        static extern int NtQueryInformationProcessBasic(IntPtr process, int cls, long[] info, int size, out int returned);

        public void Dispose() { CloseHandle(handle); }

        public bool HasExited { get { return WaitForSingleObject(handle, 0) == 0; } }

        public void Suspend() { NtSuspendProcess(handle); }
        public void Resume() { NtResumeProcess(handle); }

        // Image base of a 32-bit process, read from its 32-bit PEB. 0 if it is not one.
        public long ImageBase()
        {
            IntPtr peb32;
            int returned;
            if (NtQueryInformationProcess(handle, 26, out peb32, IntPtr.Size, out returned) != 0 || peb32 == IntPtr.Zero) return 0;
            byte[] b = new byte[4];
            if (!Read(peb32.ToInt64() + 8, b)) return 0;
            return BitConverter.ToUInt32(b, 0);
        }

        public bool Read(long address, byte[] buffer)
        {
            IntPtr read;
            return ReadProcessMemory(handle, new IntPtr(address), buffer, buffer.Length, out read) && (int)read == buffer.Length;
        }

        public int ReadWord(long address)
        {
            byte[] b = new byte[2];
            return Read(address, b) ? BitConverter.ToUInt16(b, 0) : -1;
        }

        // A small block of executable memory inside the process, for values and tiny
        // code stubs the fixes bring along. 0 if the allocation fails.
        public long Allocate(int size)
        {
            return VirtualAllocEx(handle, IntPtr.Zero, (UIntPtr)(uint)size, 0x3000, 0x40).ToInt64();
        }

        public int ReadInt(long address)
        {
            byte[] b = new byte[4];
            return Read(address, b) ? BitConverter.ToInt32(b, 0) : -1;
        }

        public bool Matches(long address, byte[] expected)
        {
            byte[] b = new byte[expected.Length];
            if (!Read(address, b)) return false;
            for (int i = 0; i < b.Length; i++) if (b[i] != expected[i]) return false;
            return true;
        }

        // A page that is already writable is left alone: the copy protection flips code
        // pages between writable and executable while it unpacks, and putting back a
        // stale protection at the wrong moment leaves the game with non-executable code.
        public void Write(long address, byte[] data)
        {
            IntPtr target = new IntPtr(address), done;
            UIntPtr size = new UIntPtr((uint)data.Length);

            MemInfo info;
            bool writable = VirtualQueryEx(handle, target, out info, (UIntPtr)Marshal.SizeOf(typeof(MemInfo))) != UIntPtr.Zero
                && (info.Protect & 0xCC) != 0;   // READWRITE, WRITECOPY, EXECUTE_READWRITE, EXECUTE_WRITECOPY

            if (writable)
            {
                WriteProcessMemory(handle, target, data, data.Length, out done);
            }
            else
            {
                uint old;
                VirtualProtectEx(handle, target, size, 0x40, out old);
                WriteProcessMemory(handle, target, data, data.Length, out done);
                VirtualProtectEx(handle, target, size, old, out old);
            }
            FlushInstructionCache(handle, target, size);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MemInfo
        {
            public IntPtr BaseAddress, AllocationBase;
            public uint AllocationProtect;
            public ushort PartitionId;
            public IntPtr RegionSize;
            public uint State, Protect, Type;
        }

        public static string ImagePath(int pid)
        {
            IntPtr process = OpenProcess(QueryLimited, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int len = sb.Capacity;
                return QueryFullProcessImageNameW(process, 0, sb, ref len) ? sb.ToString() : null;
            }
            finally { CloseHandle(process); }
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr p, IntPtr a, byte[] b, int n, out IntPtr read);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(IntPtr p, IntPtr a, byte[] b, int n, out IntPtr written);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualProtectEx(IntPtr p, IntPtr a, UIntPtr n, uint protect, out uint old);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr a, UIntPtr n, uint type, uint protect);
        [DllImport("kernel32.dll")] static extern UIntPtr VirtualQueryEx(IntPtr p, IntPtr a, out MemInfo info, UIntPtr length);
        [DllImport("kernel32.dll")] static extern bool FlushInstructionCache(IntPtr p, IntPtr a, UIntPtr n);
        [DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr process);
        [DllImport("ntdll.dll")] static extern int NtResumeProcess(IntPtr process);
        [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr process, int cls, out IntPtr info, int size, out int returned);
    }
}
