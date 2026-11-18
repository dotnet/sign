// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Runtime.InteropServices;

namespace Sign.Core
{
    internal static class WindowsArchitecture
    {
        internal static string GetSdkDirectoryName(Architecture architecture)
        {
            return architecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException()
            };
        }

        internal static string GetFrameworkDirectoryName(Architecture architecture)
        {
            return architecture switch
            {
                Architecture.X64 => "Framework64",
                Architecture.Arm64 => "FrameworkArm64",
                _ => throw new PlatformNotSupportedException()
            };
        }
    }
}
