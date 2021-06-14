using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Orts.Common.Native
{
    public static partial class NativeMethods
    {

        public static IntPtr CreateCompatibleDC(IntPtr hdc)
        { return CreateCompatibleDCNative(hdc); }
        [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleDC", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr CreateCompatibleDCNative(IntPtr hdc);

        public static IntPtr SelectObject(IntPtr hdc, IntPtr hObject)
        { return SelectObjectNative(hdc, hObject); }
        [DllImport("gdi32.dll", EntryPoint = "SelectObject", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr SelectObjectNative(IntPtr hdc, IntPtr hObject);

        public static bool DeleteDC(IntPtr hdc)
        { return DeleteDCNative(hdc); }
        [DllImport("gdi32.dll", EntryPoint = "DeleteDC", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool DeleteDCNative(IntPtr hdc);

        [Flags]
#pragma warning disable CA1028 // Enum Storage should be Int32
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
        public enum GgiFlags : uint
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
#pragma warning restore CA1028 // Enum Storage should be Int32
        {
            None = 0,
            MarkNonexistingGlyphs = 1,
        }

        public static uint GetGlyphIndices(IntPtr hdc, string text, int textLength, [Out] short[] indices, GgiFlags flags)
        { return GetGlyphIndicesNative(hdc, text, textLength, indices, flags); }
        [DllImport("gdi32.dll", EntryPoint = "GetGlyphIndices", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint GetGlyphIndicesNative(IntPtr hdc, string text, int textLength, [Out] short[] indices, GgiFlags flags);

        public static bool GetCharABCWidthsFloat(IntPtr hdc, uint firstChar, uint lastChar, out NativeStructs.AbcFloatWidth abcFloatWidths)
        { return GetCharABCWidthsFloatNative(hdc, firstChar, lastChar, out abcFloatWidths); }
        [DllImport("gdi32.dll", EntryPoint = "GetCharABCWidthsFloat", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool GetCharABCWidthsFloatNative(IntPtr hdc, uint firstChar, uint lastChar, out NativeStructs.AbcFloatWidth abcFloatWidths);

    }

#pragma warning disable CA1815 // Override equals and operator equals on value types
#pragma warning disable CA1034 // Nested types should not be visible
    public static partial class NativeStructs
    {
        [DebuggerDisplay("{First} + {Second} = {Amount}")]
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct KerningPair
        {
            public char First;
            public char Second;
            public int Amount;
        }

        [DebuggerDisplay("{A} + {B} + {C}")]
        [StructLayout(LayoutKind.Sequential)]
        public struct AbcFloatWidth
        {
            public float A;
            public float B;
            public float C;
        }
    }
#pragma warning restore CA1034 // Nested types should not be visible
#pragma warning restore CA1815 // Override equals and operator equals on value types

}
