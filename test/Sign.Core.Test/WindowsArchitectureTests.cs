// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace Sign.Core.Test
{
    public sealed class WindowsArchitectureTests
    {
        [Theory]
        [InlineData(Architecture.X64, "x64", "Framework64", Machine.Amd64)]
        [InlineData(Architecture.Arm64, "arm64", "FrameworkArm64", Machine.Arm64)]
        public void NativeDependencies_MatchArchitecture(Architecture architecture, string sdkDirectory, string frameworkDirectory, Machine machine)
        {
            Assert.Equal(sdkDirectory, WindowsArchitecture.GetSdkDirectoryName(architecture));
            Assert.Equal(frameworkDirectory, WindowsArchitecture.GetFrameworkDirectoryName(architecture));

            DirectoryInfo rootDirectory = new AppRootDirectoryLocator().Directory;

            foreach (string fileName in new[] { "wintrust.dll", "mssign32.dll", "appxsip.dll", "appxpackaging.dll", "opcservices.dll", "makeappx.exe", "makepri.exe" })
            {
                using (FileStream stream = File.OpenRead(Path.Combine(rootDirectory.FullName, "tools", "SDK", sdkDirectory, fileName)))
                using (PEReader reader = new(stream))
                {
                    Assert.Equal(machine, reader.PEHeaders.CoffHeader.Machine);
                }
            }

            string ini = File.ReadAllText(Path.Combine(rootDirectory.FullName, "tools", "SDK", sdkDirectory, "wintrust.dll.ini"));

            Assert.Equal(architecture == Architecture.X64, ini.Contains("NavSip.dll", StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [InlineData(Architecture.X86)]
        [InlineData(Architecture.Arm)]
        public void UnsupportedArchitecture_Throws(Architecture architecture)
        {
            Assert.Throws<PlatformNotSupportedException>(() => WindowsArchitecture.GetSdkDirectoryName(architecture));
            Assert.Throws<PlatformNotSupportedException>(() => WindowsArchitecture.GetFrameworkDirectoryName(architecture));
        }

        [Fact]
        public void ManagedAssembly_DoesNotRequireX64()
        {
            using (FileStream stream = File.OpenRead(typeof(AppInitializer).Assembly.Location))
            using (PEReader reader = new(stream))
            {
                Assert.Equal(Machine.I386, reader.PEHeaders.CoffHeader.Machine);
                Assert.NotNull(reader.PEHeaders.CorHeader);
                Assert.True(reader.PEHeaders.CorHeader.Flags.HasFlag(CorFlags.ILOnly));
                Assert.False(reader.PEHeaders.CorHeader.Flags.HasFlag(CorFlags.Requires32Bit));
            }
        }
    }
}
