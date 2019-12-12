using System;
using System.Runtime.InteropServices;

namespace Orts.Common.Native
{
    public partial class NativeMethods
    {
        /// <summary>
        /// Lock or relase a window for updating.
        /// </summary>
        public static int LockWindowUpdate(IntPtr hwnd)
        { return LockWindowUpdateNative(hwnd); }
        [DllImport("user32", EntryPoint = "LockWindowUpdate", SetLastError = true)]
        private static extern int LockWindowUpdateNative(IntPtr hwnd);

        public enum MapVirtualKeyType
        {
            VirtualToCharacter = 2,
            VirtualToScan = 0,
            VirtualToScanEx = 4,
            ScanToVirtual = 1,
            ScanToVirtualEx = 3,
        }

        public static int MapVirtualKey(int code, MapVirtualKeyType type)
        { return MapVirtualKeyNative(code, type); }
        [DllImport("user32.dll", EntryPoint = "MapVirtualKey", SetLastError = true)]
        private static extern int MapVirtualKeyNative(int code, MapVirtualKeyType type);

        public static int GetKeyNameText(int scanCode, [Out] string name, int length)
        { return GetKeyNameTextNative(scanCode, name, length); }
        [DllImport("user32.dll", EntryPoint = "GetKeyNameText", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetKeyNameTextNative(int scanCode, [Out] string name, int length);

        public static IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref CharFormat2 lParam)
        { return SendMessageNative(hwnd, msg, wParam, ref lParam); }
        [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageNative(IntPtr hwnd, int msg, IntPtr wParam, ref CharFormat2 lParam);

        [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public struct CharFormat2
        {
            public int Size;
            public int Mask;
            public int Effects;
            public int Height;
            public int Offset;
            public int TextColor;
            public byte CharSet;
            public byte PitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string FaceName;
            public short Weight;
            public short Spacing;
            public int BackColor;
            public int Lcid;
            public int Reserved;
            public short Style;
            public short Kerning;
            public byte UnderlineType;
            public byte Animation;
            public byte RevAuthor;
            public byte Reserved1;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NativeMessage
        {
            public IntPtr Handle;
            public uint Msg;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public System.Drawing.Point P;
        }

        public static IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam)
        { return SendMessageNative(hwnd, msg, wParam, lParam); }
        [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageNative(IntPtr hWnd, int msg, IntPtr wParam, [MarshalAs(UnmanagedType.LPWStr)]string lParam);

        public static bool PeekMessage(out NativeMessage message, IntPtr hwnd, uint filterMin, uint filterMax, uint flags)
        { return PeekMessageNative(out message, hwnd, filterMin, filterMax, flags); }
        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("User32.dll", EntryPoint = "PeekMessage", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool PeekMessageNative(out NativeMessage message, IntPtr hWnd, uint filterMin, uint filterMax, uint flags);
    }
}
