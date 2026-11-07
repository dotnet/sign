// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
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
            string fullPath = CanonicalizeDirectory(Path.GetFullPath(path));
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

        private static string CanonicalizeDirectory(string path)
        {
            DirectoryInfo? directory = new FileInfo(path).Directory;
            while (directory is not null)
            {
                using SafeFileHandle handle = CreateFile(directory.FullName, 0x80,
                    FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error is not (2 or 3))
                    {
                        throw new IOException($"Cannot inspect ClickOnce destination directory '{directory.FullName}'.",
                            new Win32Exception(error));
                    }
                    directory = directory.Parent;
                    continue;
                }
                StringBuilder buffer = new(512);
                uint length = Kernel32.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
                if (length >= buffer.Capacity)
                {
                    buffer = new StringBuilder(checked((int)length + 1));
                    length = Kernel32.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
                }
                if (length == 0 || length >= buffer.Capacity)
                {
                    throw new IOException($"Cannot resolve ClickOnce destination directory '{directory.FullName}'.",
                        new Win32Exception(Marshal.GetLastWin32Error()));
                }
                string canonical = buffer.ToString();
                canonical = canonical.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)
                    ? @"\\" + canonical[8..]
                    : canonical.StartsWith(@"\\?\", StringComparison.Ordinal) ? canonical[4..] : canonical;
                return Path.Combine(canonical, Path.GetRelativePath(directory.FullName, path));
            }
            return path;
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
