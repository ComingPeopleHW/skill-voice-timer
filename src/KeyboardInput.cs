using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SkillVoiceTimer
{
    // Only the HID keyboard usage is registered. Ordinary game key messages still go to the foreground window.
    internal static class KeyboardInput
    {
        internal const int WmInput = 0x00FF;
        private const uint InputSink = 0x100;
        private const uint Remove = 1;
        private const uint ReadInput = 0x10000003;

        [StructLayout(LayoutKind.Sequential)]
        private struct Device { public ushort Page, Usage; public uint Flags; public IntPtr Window; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Header { public uint Type, Size; public IntPtr Device, WParam; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardPacket
        {
            public Header Header;
            public ushort MakeCode, Flags, Reserved, VirtualKey;
            public uint Message, ExtraInformation;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(ref Device device, uint count, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr input, uint command, out KeyboardPacket packet,
            ref uint size, uint headerSize);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        public static bool IsDown(Keys key) { return GetAsyncKeyState((int)key) < 0; }

        internal static Keys PhysicalKey(KeyboardPacket packet)
        {
            var key = (Keys)packet.VirtualKey;
            bool extended = (packet.Flags & 2) != 0;
            switch (key)
            {
                case Keys.ShiftKey: return packet.MakeCode == 0x36 ? Keys.RShiftKey : Keys.LShiftKey;
                case Keys.ControlKey: return extended ? Keys.RControlKey : Keys.LControlKey;
                case Keys.Menu: return extended ? Keys.RMenu : Keys.LMenu;
                default: return key;
            }
        }

        private static readonly uint HeaderSize = (uint)Marshal.SizeOf(typeof(Header));
        private static readonly uint PacketSize = (uint)Marshal.SizeOf(typeof(KeyboardPacket));

        public static void Register(IntPtr window, bool enabled)
        {
            var device = new Device { Page = 1, Usage = 6, Flags = enabled ? InputSink : Remove,
                Window = enabled ? window : IntPtr.Zero };
            if (!RegisterRawInputDevices(ref device, 1, (uint)Marshal.SizeOf(typeof(Device))))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public static bool Read(IntPtr input, out Keys key, out bool released)
        {
            uint size = PacketSize;
            KeyboardPacket packet;
            var bytes = GetRawInputData(input, ReadInput, out packet, ref size, HeaderSize);
            key = Keys.None;
            released = false;
            if (bytes != PacketSize || packet.Header.Type != 1 || packet.VirtualKey >= 255 ||
                packet.MakeCode == 255) return false;
            key = PhysicalKey(packet);
            released = (packet.Flags & 1) != 0;
            return true;
        }
    }
}
