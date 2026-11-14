using System;
using System.Diagnostics;
using System.Threading;

namespace ForcedUpscaling
{
    // One setting a game offers in the window. The choices are Loc keys.
    class GameOption
    {
        public readonly string Key;
        public readonly string LabelKey;
        public readonly string[] ChoiceKeys;
        public int Default;   // the choice used until the user picks one

        public GameOption(string key, string labelKey, params string[] choiceKeys)
        {
            Key = key;
            LabelKey = labelKey;
            ChoiceKeys = choiceKeys;
        }
    }

    // A game that needs more than a bigger desktop. The profile knows how to
    // recognize the executable and what to change in the running process.
    abstract class GameProfile
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string ProcessName { get; }
        public abstract long ExeSize { get; }
        public abstract GameOption[] Options { get; }

        // Runs on its own thread right after the game starts, with the mode on.
        public abstract void Apply(ProcessMemory process, int width, int height, Settings store);

        public string SettingKey(GameOption option) { return "game." + Id + "." + option.Key; }

        protected int Choice(Settings store, GameOption option)
        {
            int n = store.GetInt(SettingKey(option), option.Default);
            return n < 0 || n >= option.ChoiceKeys.Length ? option.Default : n;
        }
    }

    static class Games
    {
        public static readonly GameProfile[] All = { new Dw8Empires(), new Dw8XtremeLegends(), new Z2Chaos() };
    }

    // Onechanbara Z2: Chaos (Steam 446390, 64-bit, DirectX 11). The window already follows the
    // desktop size, but the game builds its scene in an internal size taken from its own list of
    // eight sizes, and the last one is 1920x1080 (what the menu shows). Everything is then
    // stretched to the window, which is where the jagged edges and the blur come from. That last
    // size is rewritten to the screen size, and so are the eight constants that build the effect
    // layers at 1920x1080. The code is not packed and the image base is fixed, so it is done at once.
    class Z2Chaos : GameProfile
    {
        const int OldWidth = 1920, OldHeight = 1080;
        const int WindowWaitMs = 60000;

        // mov r9d, imm32 / mov r8d, imm32 / mov edx, imm32 with the size in the last four bytes.
        class Site
        {
            public readonly int Rva;
            public readonly byte[] Head;
            public readonly bool IsWidth;
            public Site(int rva, bool isWidth, params byte[] head) { Rva = rva; IsWidth = isWidth; Head = head; }
        }

        // The game keeps a table of nine sizes and builds one set of buffers for each; the setting picks
        // one. The last entry is 1920x1080 (mov dword [rsp+20h], imm32 in the function that fills the
        // table). The two functions that report the size of the chosen entry have the same last item
        // (mov dword [rsp+44h], imm32), and those follow along.
        static readonly Site[] TableSites = {
            new Site(0x4187AA, true, 0xC7, 0x44, 0x24, 0x20), new Site(0x418802, false, 0xC7, 0x44, 0x24, 0x20),
            new Site(0x191EA5, true, 0xC7, 0x44, 0x24, 0x44), new Site(0x191DA5, false, 0xC7, 0x44, 0x24, 0x44) };

        static readonly Site[] Sites = {
            new Site(0x206111, false, 0x41, 0xB9), new Site(0x206117, true, 0x41, 0xB8),
            new Site(0x20698D, false, 0x41, 0xB9), new Site(0x206993, true, 0x41, 0xB8),
            new Site(0x206DF6, false, 0x41, 0xB9), new Site(0x206DFC, true, 0x41, 0xB8),
            new Site(0x206E31, true, 0xBA),        new Site(0x206E36, false, 0x41, 0xB8) };

        static readonly GameOption Layers = new GameOption("layers", "layers", "layers_game", "layers_full") { Default = 1 };
        static readonly GameOption[] options = { Layers };

        public override string Id { get { return "z2"; } }
        public override string Name { get { return "Onechanbara Z2: Chaos"; } }
        public override string ProcessName { get { return "OnechanbaraZ2"; } }
        public override long ExeSize { get { return 7965696; } }
        public override GameOption[] Options { get { return options; } }

        public override void Apply(ProcessMemory process, int width, int height, Settings store)
        {
            if (Choice(store, Layers) != 1) return;   // on unless the user switched it to the game's own
            long imageBase = process.ImageBase64();
            if (imageBase == 0) return;

            // The internal size is read while the game starts, so the list goes in first of all.
            bool tableOk = true;
            foreach (Site s in TableSites)
                if (!Matches(process, imageBase, s, s.IsWidth ? OldWidth : OldHeight)) tableOk = false;
            if (tableOk && width > OldWidth)
                foreach (Site s in TableSites)
                    process.Write(imageBase + s.Rva + s.Head.Length, BitConverter.GetBytes(s.IsWidth ? width : height));

            // The window tells what size the game picked for the screen; the effect layers follow it.
            int w = 0, h = 0;
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < WindowWaitMs)
            {
                if (process.HasExited) return;
                if (ProcessMemory.ClientSize(process.Pid, out w, out h)) break;
                Thread.Sleep(100);
            }
            if (w <= 0 || (w == OldWidth && h == OldHeight)) return;

            foreach (Site s in Sites)
                if (!Matches(process, imageBase, s, s.IsWidth ? OldWidth : OldHeight)) return;
            foreach (Site s in Sites)
                process.Write(imageBase + s.Rva + s.Head.Length, BitConverter.GetBytes(s.IsWidth ? w : h));
        }

        static bool Matches(ProcessMemory process, long imageBase, Site site, int value)
        {
            byte[] expected = new byte[site.Head.Length + 4];
            Array.Copy(site.Head, expected, site.Head.Length);
            Array.Copy(BitConverter.GetBytes(value), 0, expected, site.Head.Length, 4);
            return process.Matches(imageBase + site.Rva, expected);
        }
    }

    // A 32-bit constant inside the switches that turn a list position into a size.
    // Head holds the opcode bytes in front of it (mov reg, imm32 or mov [esp+0C], imm32).
    class SizeSite
    {
        public readonly int Rva;
        public readonly byte[] Head;
        public SizeSite(int rva, params byte[] head) { Rva = rva; Head = head; }
    }

    // The engine behind Dynasty Warriors 8 Empires and Xtreme Legends. Both only list resolutions
    // up to 1920x1200 and build their screen sizes from constants in the code, so the list and
    // those constants are rewritten in memory right after the game starts. Nothing on disk is
    // touched, which keeps the Steam integrity check happy.
    abstract class Dw8Engine : GameProfile
    {
        const int Slot = 9;                                      // 1920x1080, what Config.exe picks by default
        const int OldWidth = 1920, OldHeight = 1080;
        protected const int UnpackWaitMs = 20000;                // time the Steam stub gets to unpack the code

        protected abstract int WidthsRva { get; }                // 16-bit lists in .rdata
        protected abstract int HeightsRva { get; }
        protected abstract SizeSite[] WidthSites { get; }
        protected abstract SizeSite[] HeightSites { get; }

        public override string ProcessName { get { return "Launch"; } }

        public override void Apply(ProcessMemory process, int width, int height, Settings store)
        {
            // Right after the start the loader may not have filled in the image base yet.
            long imageBase = 0;
            Stopwatch start = Stopwatch.StartNew();
            while ((imageBase = process.ImageBase()) == 0 && start.ElapsedMilliseconds < 3000)
            {
                if (process.HasExited) return;
                Thread.Sleep(2);
            }
            if (imageBase == 0) return;

            // The lists are plain data. Hold the game while they change so it
            // cannot read the old ones first.
            process.Suspend();
            try
            {
                if (process.ReadWord(imageBase + WidthsRva + Slot * 2) != OldWidth ||
                    process.ReadWord(imageBase + HeightsRva + Slot * 2) != OldHeight) return;
                process.Write(imageBase + WidthsRva + Slot * 2, BitConverter.GetBytes((ushort)width));
                process.Write(imageBase + HeightsRva + Slot * 2, BitConverter.GetBytes((ushort)height));
            }
            finally { process.Resume(); }

            // The code only becomes readable after the protection has unpacked it. The
            // sizes go in as soon as it does, before the game gets to read them.
            Stopwatch clock = Stopwatch.StartNew();
            bool sized = false;
            while (clock.ElapsedMilliseconds < UnpackWaitMs)
            {
                if (process.HasExited) return;
                if (AllSites(process, imageBase, OldWidth, OldHeight))
                {
                    foreach (SizeSite s in WidthSites) process.Write(imageBase + s.Rva + s.Head.Length, BitConverter.GetBytes(width));
                    foreach (SizeSite s in HeightSites) process.Write(imageBase + s.Rva + s.Head.Length, BitConverter.GetBytes(height));
                    sized = true;
                    break;
                }
                Thread.SpinWait(200);
            }
            if (sized) AfterSized(process, imageBase, clock, store);
        }

        // What a game adds once the sizes are in. The clock keeps counting from the start.
        protected virtual void AfterSized(ProcessMemory process, long imageBase, Stopwatch clock, Settings store) { }

        bool AllSites(ProcessMemory process, long imageBase, int width, int height)
        {
            foreach (SizeSite s in WidthSites) if (!SiteIs(process, imageBase, s, width)) return false;
            foreach (SizeSite s in HeightSites) if (!SiteIs(process, imageBase, s, height)) return false;
            return true;
        }

        static bool SiteIs(ProcessMemory process, long imageBase, SizeSite site, int value)
        {
            byte[] expected = new byte[site.Head.Length + 4];
            Array.Copy(site.Head, expected, site.Head.Length);
            Array.Copy(BitConverter.GetBytes(value), 0, expected, site.Head.Length, 4);
            return process.Matches(imageBase + site.Rva, expected);
        }
    }

    // Dynasty Warriors 8 Empires (Steam 322520).
    class Dw8Empires : Dw8Engine
    {
        static readonly SizeSite[] widthSites = {
            new SizeSite(0x5DF0, 0xB8), new SizeSite(0x64AE, 0xB8), new SizeSite(0x7F1784, 0xB8), new SizeSite(0x7FED1C, 0xBE),
            new SizeSite(0x804243, 0xB8), new SizeSite(0x804321, 0xB8), new SizeSite(0x804BE7, 0xB8), new SizeSite(0x804D03, 0xB8),
            new SizeSite(0x804DD4, 0xB8), new SizeSite(0x8052B6, 0xBD), new SizeSite(0x8052F0, 0xB8),
            new SizeSite(0x357977, 0xC7, 0x44, 0x24, 0x0C) };
        static readonly SizeSite[] heightSites = {
            new SizeSite(0x5DB0, 0xBA), new SizeSite(0x61A0, 0xB8), new SizeSite(0x7F1726, 0xB8), new SizeSite(0x7F78F0, 0xB8) };

        // Depth of field. The camera code feeds the blur shader a "fAperture" value and the
        // blur grows with it. Lowering the post effect level does not switch the blur off:
        // below level 0 the camera stops setting the shader values and the whole picture
        // blurs. So the level stays alone and the aperture itself is scaled instead.
        // Pass 0 (level 0) computes it as distance * 0.01 / 4.6: the 0.01 is read from
        // memory by an fmul that is pointed at our own value. Pass 1 loads it from the
        // camera data with one fld: that load detours through a stub that multiplies it.
        const int ApertureScaleRva = 0x356941;                    // fmul qword [0.01]
        const int ApertureScaleConstantRva = 0x89D188;            // where that 0.01 lives
        const int ApertureLoadRva = 0x356ECE;                     // fld dword [ebx+150h]
        const int ApertureAfterRva = 0x356ED4;
        static readonly byte[] ApertureLoad = { 0xD9, 0x83, 0x50, 0x01, 0x00, 0x00 };
        static readonly double[] ApertureScales = { 1, 0.5, 0 };   // normal (the game's own), weak, off

        static readonly GameOption Dof = new GameOption("dof", "dof", "dof_normal", "dof_weak", "dof_off");
        static readonly GameOption[] options = { Dof };

        public override string Id { get { return "dw8e"; } }
        public override string Name { get { return "Dynasty Warriors 8 Empires"; } }
        public override long ExeSize { get { return 10862464; } }
        public override GameOption[] Options { get { return options; } }
        protected override int WidthsRva { get { return 0x84BA3C; } }
        protected override int HeightsRva { get { return 0x84BA54; } }        protected override SizeSite[] WidthSites { get { return widthSites; } }
        protected override SizeSite[] HeightSites { get { return heightSites; } }

        protected override void AfterSized(ProcessMemory process, long imageBase, Stopwatch clock, Settings store)
        {
            double scale = ApertureScales[Choice(store, Dof)];
            if (scale == 1) return;

            // The blur is only computed once the game renders, so this can wait until the
            // unpacker has also fixed up the addresses inside those instructions.
            while (clock.ElapsedMilliseconds < UnpackWaitMs)
            {
                if (process.HasExited) return;
                if (ApertureReady(process, imageBase))
                {
                    ScaleAperture(process, imageBase, scale);
                    return;
                }
                Thread.Sleep(5);
            }
        }

        static void ScaleAperture(ProcessMemory process, long imageBase, double scale)
        {
            long block = process.Allocate(64);
            if (block == 0) return;

            // block: +0 the pass 0 multiplier (a double), +8 the pass 1 factor (a float),
            // +16 the stub: the load that was replaced, fmul [factor], jump back.
            byte[] data = new byte[64];
            Array.Copy(BitConverter.GetBytes(0.01 * scale), 0, data, 0, 8);
            Array.Copy(BitConverter.GetBytes((float)scale), 0, data, 8, 4);
            Array.Copy(ApertureLoad, 0, data, 16, ApertureLoad.Length);
            data[22] = 0xD8; data[23] = 0x0D;
            Array.Copy(BitConverter.GetBytes((uint)(block + 8)), 0, data, 24, 4);
            data[28] = 0xE9;
            Array.Copy(BitConverter.GetBytes((int)(imageBase + ApertureAfterRva - (block + 33))), 0, data, 29, 4);
            process.Write(block, data);

            byte[] detour = new byte[ApertureLoad.Length];
            detour[0] = 0xE9;
            Array.Copy(BitConverter.GetBytes((int)(block + 16 - (imageBase + ApertureLoadRva + 5))), 0, detour, 1, 4);
            detour[5] = 0x90;

            // Both edits land together, with the game held for those few microseconds.
            process.Suspend();
            try
            {
                process.Write(imageBase + ApertureScaleRva + 2, BitConverter.GetBytes((uint)block));
                process.Write(imageBase + ApertureLoadRva, detour);
            }
            finally { process.Resume(); }
        }

        // fmul qword [absolute address of the 0.01], which moves with the image base,
        // and the untouched load in the other pass.
        static bool ApertureReady(ProcessMemory process, long imageBase)
        {
            byte[] expected = new byte[6];
            expected[0] = 0xDC; expected[1] = 0x0D;
            Array.Copy(BitConverter.GetBytes((uint)(imageBase + ApertureScaleConstantRva)), 0, expected, 2, 4);
            return process.Matches(imageBase + ApertureScaleRva, expected)
                && process.Matches(imageBase + ApertureLoadRva, ApertureLoad);
        }
    }

    // Dynasty Warriors 8: Xtreme Legends Complete Edition (Steam 278080, version 1.0.4.0).
    // Same engine and the same layout as Empires, only the addresses differ. The game has its
    // own depth of field option, so there is nothing to add in the window yet.
    class Dw8XtremeLegends : Dw8Engine
    {
        static readonly SizeSite[] widthSites = {
            new SizeSite(0x30F0, 0xB8), new SizeSite(0x375E, 0xB8), new SizeSite(0x5B9644, 0xB8), new SizeSite(0x5C4B6D, 0xBE),
            new SizeSite(0x5CC033, 0xB8), new SizeSite(0x5CC111, 0xB8), new SizeSite(0x5CC947, 0xB8), new SizeSite(0x5CCA63, 0xB8),
            new SizeSite(0x5CCB34, 0xB8), new SizeSite(0x5CCDC6, 0xBD), new SizeSite(0x5CCE00, 0xB8),
            new SizeSite(0x585E9D, 0xC7, 0x44, 0x24, 0x0C) };
        static readonly SizeSite[] heightSites = {
            new SizeSite(0x30B0, 0xBA), new SizeSite(0x31C0, 0xB8), new SizeSite(0x5B95E6, 0xB8), new SizeSite(0x5BFC50, 0xB8) };
        static readonly GameOption[] options = { };

        public override string Id { get { return "dw8xl"; } }
        public override string Name { get { return "Dynasty Warriors 8: Xtreme Legends"; } }
        public override long ExeSize { get { return 7320880; } }
        public override GameOption[] Options { get { return options; } }
        protected override int WidthsRva { get { return 0x600170; } }
        protected override int HeightsRva { get { return 0x600188; } }        protected override SizeSite[] WidthSites { get { return widthSites; } }
        protected override SizeSite[] HeightSites { get { return heightSites; } }
    }
}
