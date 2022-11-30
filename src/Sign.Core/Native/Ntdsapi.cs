// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Runtime.InteropServices;

namespace Sign.Core
{
    internal static class Ntdsapi
    {
        [method: DllImport("ntdsapi.dll", EntryPoint = "DsGetRdnW", ExactSpelling = true, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Error)]
        internal static extern int DsGetRdnW(
            [param: In, Out, MarshalAs(UnmanagedType.SysInt)] ref IntPtr ppDN,
            [param: In, Out, MarshalAs(UnmanagedType.U4)] ref uint pcDN,
            [param: Out, MarshalAs(UnmanagedType.SysInt)] out IntPtr ppKey,
            [param: Out, MarshalAs(UnmanagedType.U4)] out uint pcKey,
            [param: Out, MarshalAs(UnmanagedType.SysInt)] out IntPtr ppVal,
            [param: Out, MarshalAs(UnmanagedType.U4)] out uint pcVal);
    }
}