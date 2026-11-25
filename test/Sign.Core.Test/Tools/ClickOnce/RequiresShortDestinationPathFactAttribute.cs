// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Runtime.InteropServices;
using System.Text;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    internal sealed class RequiresShortDestinationPathFactAttribute : FactAttribute
    {
        private static readonly Lazy<bool> _supported = new(IsSupported);

        public override string? Skip
        {
            get => _supported.Value ? null : "An actual 8.3 directory alias is unavailable.";
            set { }
        }

        internal static string GetShortPath(string path)
        {
            StringBuilder buffer = new(32768);
            uint length = GetShortPathName(path, buffer, (uint)buffer.Capacity);
            if (length == 0 || length >= buffer.Capacity)
            {
                throw new IOException("Cannot obtain the test directory's short path.",
                    new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            }
            return buffer.ToString();
        }

        private static bool IsSupported()
        {
            try
            {
                using TestDirectory directory = new();
                DirectoryInfo parent = Directory.CreateDirectory(
                    Path.Combine(directory.FullPath, "LongDestinationParent"));
                return !string.Equals(parent.FullName, GetShortPath(parent.FullName),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    System.ComponentModel.Win32Exception or PlatformNotSupportedException)
            {
                return false;
            }
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW",
            CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetShortPathName(string path, StringBuilder buffer, uint capacity);
    }
}
