using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SignService.Utils
{
    static class Kernel32
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, LoadLibraryFlags dwFlags);

        [Flags]
        public enum LoadLibraryFlags
        {
            LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100,
            LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400
        }

        [DllImport("Kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateActCtx(ref ACTCTX pActCtx);

        [DllImport("Kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ActivateActCtx(IntPtr hActCtx, out IntPtr lpCookie);

        [DllImport("Kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeactivateActCtx(int dwFlags, IntPtr lpCookie);

        [DllImport("Kernel32.dll", SetLastError = true)]
        public static extern void ReleaseActCtx(IntPtr hActCtx);

        [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
        public struct ACTCTX
        {
            public int cbSize;
            public ActivationContextFlags dwFlags;
            public string lpSource;
            public ushort wProcessorArchitecture;
            public Int16 wLangId;
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
            IntPtr actCtx = new IntPtr(-1);
            IntPtr cookie;

            public ActivationContext(string assemblyName)
            {

                var ctx = new ACTCTX();
                ctx.cbSize = Marshal.SizeOf(ctx);
                ctx.dwFlags = ActivationContextFlags.ACTCTX_FLAG_APPLICATION_NAME_VALID |
                              ActivationContextFlags.ACTCTX_FLAG_RESOURCE_NAME_VALID;
                ctx.lpResourceName = "1";
                ctx.lpSource = assemblyName;


                actCtx = CreateActCtx(ref ctx);

                if (actCtx != INVALID_HANDLE_VALUE)
                {
                    if (!ActivateActCtx(actCtx, out cookie))
                    {
                        var err = Marshal.GetLastWin32Error();
                        
                    }
                }
                else
                {
                    var err = Marshal.GetLastWin32Error();

                }
            }

            public void Dispose()
            {
                if (cookie != IntPtr.Zero)
                {
                    if (!DeactivateActCtx(0, cookie))
                    {
                        var err = Marshal.GetLastWin32Error();
                    }

                    cookie = IntPtr.Zero;
                }

                if (actCtx != INVALID_HANDLE_VALUE)
                {
                    ReleaseActCtx(actCtx);
                    actCtx = INVALID_HANDLE_VALUE;
                }
            
            }
        }
    }


}
