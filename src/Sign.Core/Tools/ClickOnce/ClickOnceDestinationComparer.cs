// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Sign.Core
{
    internal sealed class ClickOnceDestinationComparer : IEqualityComparer<string>
    {
        internal static ClickOnceDestinationComparer Instance { get; } = new();

        public bool Equals(string? left, string? right) =>
            left is null || right is null
                ? left == right
                : string.Equals(Key(left), Key(right), StringComparison.Ordinal);

        public int GetHashCode(string path) => StringComparer.Ordinal.GetHashCode(Key(path));

        private static string Key(string path)
        {
            string fullPath = Path.GetFullPath(path);
            fullPath = fullPath.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)
                ? @"\\" + fullPath[8..]
                : fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) ? fullPath[4..] : fullPath;
            string root = Path.GetPathRoot(fullPath)!;
            string current = root;
            string key = root.ToUpperInvariant();
            bool sensitive = false;
            foreach (string segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries))
            {
                using SafeFileHandle handle = CreateFile(current, 0x80,
                    FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error is not (2 or 3))
                    {
                        throw new IOException($"Cannot inspect ClickOnce destination directory '{current}'.",
                            new Win32Exception(error));
                    }
                }
                else if (GetFileInformationByHandleEx(handle, 23, out uint flags, sizeof(uint)))
                {
                    sensitive = (flags & 1) != 0;
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error is not (1 or 87))
                    {
                        throw new IOException($"Cannot inspect ClickOnce destination directory '{current}'.",
                            new Win32Exception(error));
                    }
                    sensitive = false;
                }
                key = Path.Combine(key, sensitive ? segment : segment.ToUpperInvariant());
                current = Path.Combine(current, segment);
            }
            return key;
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string path, uint access, FileShare share, IntPtr security, uint disposition,
            uint flags, IntPtr template);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle handle, int informationClass, out uint information, uint size);
    }
}
