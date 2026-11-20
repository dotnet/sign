// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sign.TestInfrastructure
{
    internal static class HardLink
    {
        internal static void Create(string path, string existingPath)
        {
            if (!CreateHardLinkW(path, existingPath, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            EntryPoint = "CreateHardLinkW",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLinkW(
            string fileName,
            string existingFileName,
            IntPtr securityAttributes);
    }
}
