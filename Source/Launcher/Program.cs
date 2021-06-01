using System;
using System.Diagnostics;
using System.IO;

[assembly: CLSCompliant(true)]
namespace Orts.Launcher
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        private static void Main()
        {
            Process.Start(Path.Combine(Directory.GetCurrentDirectory(), "netcoreapp3.1", "Menu.exe"));
        }
    }
}