using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Orts.Common.Native
{
    /// <summary>
    /// P/Invoke for Kernel32.dll
    /// </summary>
    public static partial class NativeMethods
    {
        /// <summary>
        /// Retrieves all the keys and values for the specified section of an initialization file.
        /// </summary>
        /// <param name="sectionName">The name of the section in the initialization file.</param>
        /// <param name="value">A pointer to a buffer that receives the key name and value pairs associated with the named section. The buffer is filled with one or more null-terminated strings; the last string is followed by a second null character.</param>
        /// <param name="size">The size of the buffer pointed to by the <paramref name="value"/> parameter, in characters. The maximum profile section size is 32,767 characters.</param>
        /// <param name="fileName">The name of the initialization file. If this parameter does not contain a full path to the file, the system searches for the file in the Windows directory.</param>
        /// <returns>The return value specifies the number of characters copied to the buffer, not including the terminating null character. If the buffer is not large enough to contain all the key name and value pairs associated with the named section, the return value is equal to <paramref name="size"/> minus two.</returns>
        public static int GetPrivateProfileSection(string sectionName, string value, int size, string fileName)
        { return GetPrivateProfileSectionNative(sectionName, value, size, fileName); }
        [DllImport("kernel32.dll", EntryPoint = "GetPrivateProfileSection", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetPrivateProfileSectionNative(string sectionName, string value, int size, string fileName);

        /// <summary>
        /// Retrieves a string from the specified section in an initialization file.
        /// </summary>
        /// <param name="sectionName">The name of the section containing the key name. If this parameter is <c>null</c>, the <see cref="GetPrivateProfileString"/> function copies all section names in the file to the supplied buffer.</param>
        /// <param name="keyName">The name of the key whose associated string is to be retrieved. If this parameter is <c>null</c>, all key names in the section specified by the <paramref name="sectionName"/> parameter are copied to the buffer specified by the <paramref name="value"/> parameter.</param>
        /// <param name="defaultValue">A default string. If the <paramref name="keyName"/> key cannot be found in the initialization file, <see cref="GetPrivateProfileString"/> copies the default string to the <paramref name="value"/> buffer. If this parameter is <c>null</c>, the default is an empty string, <c>""</c>.
        /// Avoid specifying a default string with trailing blank characters. The function inserts a <c>null</c> character in the <paramref name="value"/> buffer to strip any trailing blanks.</param>
        /// <param name="value">A pointer to the buffer that receives the retrieved string. </param>
        /// <param name="size">The size of the buffer pointed to by the <paramref name="value"/> parameter, in characters.</param>
        /// <param name="fileName">The name of the initialization file. If this parameter does not contain a full path to the file, the system searches for the file in the Windows directory.</param>
        /// <returns>The return value is the number of characters copied to the buffer, not including the terminating <c>null</c> character.
        /// If neither <paramref name="sectionName"/> nor <paramref name="keyName"/> is <c>null</c> and the supplied destination buffer is too small to hold the requested string, the string is truncated and followed by a <c>null</c> character, and the return value is equal to <paramref name="size"/> minus one.
        /// If either <paramref name="sectionName"/> or <paramref name="keyName"/> is <c>null</c> and the supplied destination buffer is too small to hold all the strings, the last string is truncated and followed by two <c>null</c> characters. In this case, the return value is equal to <paramref name="size"/> minus two.
        /// In the event the initialization file specified by <paramref name="fileName"/> is not found, or contains invalid values, this function will set errorno with a value of '0x2' (File Not Found). To retrieve extended error information, call GetLastError.</returns>
        public static int GetPrivateProfileString(string sectionName, string keyName, string defaultValue, string value, int size, string fileName)
        { return GetPrivateProfileStringNative(sectionName, keyName, defaultValue, value, size, fileName); }
        [DllImport("kernel32.dll", EntryPoint = "GetPrivateProfileString", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetPrivateProfileStringNative(string sectionName, string keyName, string defaultValue, string value, int size, string fileName);

        /// <summary>
        /// Retrieves a string from the specified section in an initialization file.
        /// </summary>
        /// <param name="sectionName">The name of the section containing the key name. If this parameter is <c>null</c>, the <see cref="GetPrivateProfileString"/> function copies all section names in the file to the supplied buffer.</param>
        /// <param name="keyName">The name of the key whose associated string is to be retrieved. If this parameter is <c>null</c>, all key names in the section specified by the <paramref name="sectionName"/> parameter are copied to the buffer specified by the <paramref name="value"/> parameter.</param>
        /// <param name="defaultValue">A default string. If the <paramref name="keyName"/> key cannot be found in the initialization file, <see cref="GetPrivateProfileString"/> copies the default string to the <paramref name="value"/> buffer. If this parameter is <c>null</c>, the default is an empty string, <c>""</c>.
        /// Avoid specifying a default string with trailing blank characters. The function inserts a <c>null</c> character in the <paramref name="value"/> buffer to strip any trailing blanks.</param>
        /// <param name="value">A pointer to the buffer that receives the retrieved string. </param>
        /// <param name="size">The size of the buffer pointed to by the <paramref name="value"/> parameter, in characters.</param>
        /// <param name="fileName">The name of the initialization file. If this parameter does not contain a full path to the file, the system searches for the file in the Windows directory.</param>
        /// <returns>The return value is the number of characters copied to the buffer, not including the terminating <c>null</c> character.
        /// If neither <paramref name="sectionName"/> nor <paramref name="keyName"/> is <c>null</c> and the supplied destination buffer is too small to hold the requested string, the string is truncated and followed by a <c>null</c> character, and the return value is equal to <paramref name="size"/> minus one.
        /// If either <paramref name="sectionName"/> or <paramref name="keyName"/> is <c>null</c> and the supplied destination buffer is too small to hold all the strings, the last string is truncated and followed by two <c>null</c> characters. In this case, the return value is equal to <paramref name="size"/> minus two.
        /// In the event the initialization file specified by <paramref name="fileName"/> is not found, or contains invalid values, this function will set errorno with a value of '0x2' (File Not Found). To retrieve extended error information, call GetLastError.</returns>
        public static int GetPrivateProfileString(string sectionName, string keyName, string defaultValue, StringBuilder value, int size, string fileName)
        { return GetPrivateProfileStringNative(sectionName, keyName, defaultValue, value, size, fileName); }
        [DllImport("kernel32.dll", EntryPoint = "GetPrivateProfileString", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetPrivateProfileStringNative(string sectionName, string keyName, string defaultValue, StringBuilder value, int size, string fileName);

        /// <summary>
        /// Copies a string into the specified section of an initialization file.
        /// </summary>
        /// <param name="sectionName">The name of the section to which the string will be copied. If the section does not exist, it is created. The name of the section is case-independent; the string can be any combination of uppercase and lowercase letters.</param>
        /// <param name="keyName">The name of the key to be associated with a string. If the key does not exist in the specified section, it is created. If this parameter is <c>null</c>, the entire section, including all entries within the section, is deleted.</param>
        /// <param name="value">A <c>null</c>-terminated string to be written to the file. If this parameter is <c>null</c>, the key pointed to by the lpKeyName parameter is deleted. </param>
        /// <param name="fileName">The name of the initialization file.
        /// If the file was created using Unicode characters, the function writes Unicode characters to the file. Otherwise, the function writes ANSI characters.</param>
        /// <returns>If the function successfully copies the string to the initialization file, the return value is nonzero.
        /// If the function fails, or if it flushes the cached version of the most recently accessed initialization file, the return value is zero. To get extended error information, call GetLastError.</returns>
        public static int WritePrivateProfileString(string sectionName, string keyName, string value, string fileName)
        { return WritePrivateProfileStringNative(sectionName, keyName, value, fileName); }
        [DllImport("kernel32.dll", EntryPoint = "WritePrivateProfileString", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int WritePrivateProfileStringNative(string sectionName, string keyName, string value, string fileName);

        public static bool GlobalMemoryStatusEx([In, Out] NativeStructs.MemoryStatusExtended buffer)
        { return GlobalMemoryStatusExNative(buffer); }
        [DllImport("kernel32.dll", EntryPoint = "GlobalMemoryStatusEx", SetLastError = true)]
        private static extern bool GlobalMemoryStatusExNative([In, Out] NativeStructs.MemoryStatusExtended buffer);

        public static bool SetDllDirectory(string pathName)
        { return SetDllDirectoryNative(pathName); }
        [DllImport("kernel32.dll", EntryPoint = "SetDllDirectory", CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectoryNative(string pathName);

        public static bool GlobalMemoryStatusEx([In, Out] NativeStructs.MEMORYSTATUSEX buffer)
        { return GlobalMemoryStatusExNative(buffer); }
        [DllImport("kernel32.dll", EntryPoint = "GlobalMemoryStatusEx", SetLastError = true)]
        private static extern bool GlobalMemoryStatusExNative([In, Out] NativeStructs.MEMORYSTATUSEX buffer);

        public static bool GetProcessIoCounters(IntPtr hProcess, out NativeStructs.IO_COUNTERS ioCounters)
        { return GetProcessIoCountersNative(hProcess, out ioCounters); }
        [DllImport("kernel32.dll", EntryPoint = "GetProcessIoCounters", SetLastError = true)]
        private static extern bool GetProcessIoCountersNative(IntPtr hProcess, out NativeStructs.IO_COUNTERS ioCounters);

    }

    public static partial class NativeStructs
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        };

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        public class MemoryStatusExtended
        {
            public uint Size;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        public class MEMORYSTATUSEX
        {
            public uint Size;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }
    }
}
