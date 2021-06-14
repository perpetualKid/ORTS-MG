using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Orts.Common.Native
{
    public partial class NativeMethods
    {
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
        /// <summary>
        /// Lock or relase a window for updating.
        /// </summary>
        public static int LockWindowUpdate(IntPtr hwnd)
        { return LockWindowUpdateNative(hwnd); }
        [DllImport("user32", EntryPoint = "LockWindowUpdate", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
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
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int MapVirtualKeyNative(int code, MapVirtualKeyType type);

        public static int GetKeyNameText(int scanCode, [Out] StringBuilder name, int length)
        { return GetKeyNameTextNative(scanCode, name, length); }
        [DllImport("user32.dll", EntryPoint = "GetKeyNameText", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int GetKeyNameTextNative(int scanCode, [Out] StringBuilder name, int length);

        public static IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref CharFormat2 lParam)
        { return SendMessageNative(hwnd, msg, wParam, ref lParam); }
        [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr SendMessageNative(IntPtr hwnd, int msg, IntPtr wParam, ref CharFormat2 lParam);

        [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
#pragma warning disable CA1034 // Nested types should not be visible
#pragma warning disable CA1815 // Override equals and operator equals on value types
        public struct CharFormat2
#pragma warning restore CA1815 // Override equals and operator equals on value types
#pragma warning restore CA1034 // Nested types should not be visible
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

        public static IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam)
        { return SendMessageNative(hwnd, msg, wParam, lParam); }
        [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr SendMessageNative(IntPtr hWnd, int msg, IntPtr wParam, [MarshalAs(UnmanagedType.LPWStr)] string lParam);

        public delegate IntPtr KeyboardProcedure(int nCode, IntPtr wParam, IntPtr lParam);

        public static IntPtr SetWindowsHookEx(int idHook, KeyboardProcedure lpfn, IntPtr hMod, uint dwThreadId)
        { return SetWindowsHookExNative(idHook, lpfn, hMod, dwThreadId); }
        [DllImport("user32.dll", EntryPoint = "SetWindowsHookEx", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr SetWindowsHookExNative(int idHook, KeyboardProcedure lpfn, IntPtr hMod, uint dwThreadId);

        public static bool UnhookWindowsHookEx(IntPtr hhk)
        { return UnhookWindowsHookExNative(hhk); }
        [DllImport("user32.dll", EntryPoint = "UnhookWindowsHookEx", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool UnhookWindowsHookExNative(IntPtr hhk);

        public static IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam)
        { return CallNextHookExNative(hhk, nCode, wParam, lParam); }
        [DllImport("user32.dll", EntryPoint = "CallNextHookEx", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr CallNextHookExNative(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
    }
}
