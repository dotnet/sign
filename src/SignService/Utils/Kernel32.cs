using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SignService.Utils
{
    static class Kernel32
    {
        [DllImport("kernel32.dll", SetLastError = true, PreserveSig = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDllDirectoryW(
            [MarshalAs(UnmanagedType.LPWStr)] string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true, PreserveSig = true)]
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
            IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
            IntPtr _activationContext = new IntPtr(-1);
            IntPtr _activationContextCookie;

            public ActivationContext(string assemblyName)
            {
                var requestedActivationContext = new ACTCTX
                {
                    cbSize = Marshal.SizeOf<ACTCTX>(),
                    lpSource = assemblyName
                };

                _activationContext = CreateActCtxW(ref requestedActivationContext);
                if (_activationContext != INVALID_HANDLE_VALUE)
                {
                    if (!ActivateActCtx(_activationContext, out _activationContextCookie))
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
                if (_activationContextCookie != IntPtr.Zero)
                {
                    if (!DeactivateActCtx(dwFlags: 0, _activationContextCookie))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    _activationContextCookie = IntPtr.Zero;
                }

                if (_activationContext != INVALID_HANDLE_VALUE)
                {
                    ReleaseActCtx(_activationContext);
                    _activationContext = INVALID_HANDLE_VALUE;
                }
            }
        }
    }
}
