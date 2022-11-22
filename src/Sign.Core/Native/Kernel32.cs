// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sign.Core
{
#pragma warning disable IDE1006 // Naming Styles
    internal static partial class Kernel32
    {
        [DllImport("kernel32.dll", SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDllDirectoryW(
            [MarshalAs(UnmanagedType.LPWStr)] string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LoadLibraryW(
            [MarshalAs(UnmanagedType.LPWStr)] string path);

        [DllImport("kernel32.dll", SetLastError = true, PreserveSig = true)]
        public static extern IntPtr CreateActCtxW(ref ACTCTX pActCtx);

        [DllImport("kernel32.dll", SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ActivateActCtx(IntPtr hActCtx, out IntPtr lpCookie);

        [DllImport("kernel32.dll", SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeactivateActCtx(int dwFlags, IntPtr lpCookie);

        [DllImport("kernel32.dll", PreserveSig = true)]
        public static extern void ReleaseActCtx(IntPtr hActCtx);

        [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
        public struct ACTCTX
        {
            public int cbSize;
            public ActivationContextFlags dwFlags;
            public string lpSource;
            public ushort wProcessorArchitecture;
            public ushort wLangId;
            public string lpAssemblyDirectory;
            public string lpResourceName;
            public string lpApplicationName;
            public IntPtr hModule;
        }

        [Flags]
        public enum ActivationContextFlags : uint
        {
            ACTCTX_FLAG_RESOURCE_NAME_VALID = 0x008,
            ACTCTX_FLAG_APPLICATION_NAME_VALID = 0x020
        }

        public class ActivationContext : IDisposable
        {
            readonly IntPtr INVALID_HANDLE_VALUE = new(-1);
            IntPtr activationContext = new(-1);
            IntPtr activationContextCookie;

            public ActivationContext(FileInfo manifestFile)
            {
                ArgumentNullException.ThrowIfNull(manifestFile, nameof(manifestFile));

                var requestedActivationContext = new ACTCTX
                {
                    cbSize = Marshal.SizeOf<ACTCTX>(),
                    lpSource = manifestFile.FullName
                };

                activationContext = CreateActCtxW(ref requestedActivationContext);
                if (activationContext != INVALID_HANDLE_VALUE)
                {
                    if (!ActivateActCtx(activationContext, out activationContextCookie))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                else
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }

            public void Dispose()
            {
                if (activationContextCookie != IntPtr.Zero)
                {
                    if (!DeactivateActCtx(dwFlags: 0, activationContextCookie))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    activationContextCookie = IntPtr.Zero;
                }

                if (activationContext != INVALID_HANDLE_VALUE)
                {
                    ReleaseActCtx(activationContext);
                    activationContext = INVALID_HANDLE_VALUE;
                }
            }
        }
    }
#pragma warning restore IDE1006 // Naming Styles
}