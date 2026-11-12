// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Sign.Core
{
    internal sealed class ClickOnceStagingDirectory : ITemporaryDirectory
    {
        private const uint FileReadAttributes = 0x80;
        private const uint FileWriteAttributes = 0x100;
        private const uint OpenExisting = 3;
        private readonly TemporaryDirectory _directory;
        private readonly List<SafeFileHandle> _readOnlyCopies = new();

        internal ClickOnceStagingDirectory(IDirectoryService directoryService) =>
            _directory = new(directoryService);

        public DirectoryInfo Directory => _directory.Directory;

        internal FileInfo Copy(FileInfo source, FileInfo destination)
        {
            FileInfo copy = source.CopyTo(destination.FullName, overwrite: false);
            if (copy.IsReadOnly)
            {
                // Retain the copied object, not a path which a publisher could
                // later replace with an alias of a user-owned file.
                SafeFileHandle handle = CreateFile(copy.FullName,
                    FileReadAttributes | FileWriteAttributes, FileShare.ReadWrite | FileShare.Delete,
                    IntPtr.Zero, OpenExisting, flagsAndAttributes: 0, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    throw new IOException("Cannot retain a ClickOnce staging copy for cleanup.",
                        new Win32Exception(error));
                }
                _readOnlyCopies.Add(handle);
            }
            return copy;
        }

        public void Dispose()
        {
            try
            {
                foreach (SafeFileHandle handle in _readOnlyCopies)
                {
                    FileAttributes attributes = File.GetAttributes(handle);
                    if ((attributes & FileAttributes.ReadOnly) == 0)
                    {
                        continue;
                    }
                    if (!GetFileInformationByHandle(handle, out FileInformation information))
                    {
                        throw new IOException("Cannot inspect a ClickOnce staging copy before cleanup.",
                            new Win32Exception(Marshal.GetLastWin32Error()));
                    }
                    if (information.NumberOfLinks != 1 ||
                        (attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new IOException("Cannot clear read-only attributes on an aliased ClickOnce staging copy.");
                    }
                    File.SetAttributes(handle, attributes & ~FileAttributes.ReadOnly);
                }
            }
            finally
            {
                foreach (SafeFileHandle handle in _readOnlyCopies)
                {
                    handle.Dispose();
                }
                _readOnlyCopies.Clear();
            }
            _directory.Dispose();
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle handle, out FileInformation information);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string path, uint desiredAccess, FileShare shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [StructLayout(LayoutKind.Sequential)]
        private struct FileInformation
        {
            internal uint Attributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            internal uint VolumeSerialNumber;
            internal uint FileSizeHigh;
            internal uint FileSizeLow;
            internal uint NumberOfLinks;
            internal uint FileIndexHigh;
            internal uint FileIndexLow;
        }
    }
}
