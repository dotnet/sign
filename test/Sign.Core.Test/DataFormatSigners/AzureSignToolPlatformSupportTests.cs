// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Sign.Core.Test
{
    public sealed class AzureSignToolPlatformSupportTests : IDisposable
    {
        private readonly DirectoryService _directoryService = new(Substitute.For<ILogger<IDirectoryService>>());

        [Theory]
        [InlineData(Architecture.X64)]
        [InlineData(Architecture.Arm64)]
        public void PortableExecutable_IsSupported(Architecture architecture)
        {
            AzureSignToolSigner.ValidatePlatformSupport(new[] { new FileInfo("file.dll") }, architecture);
        }

        [Fact]
        public void BusinessCentralApp_OnArm64_ReportsMissingSip()
        {
            using (TemporaryDirectory directory = new(_directoryService))
            {
                FileInfo file = new(Path.Combine(directory.Directory.FullName, "file.app"));
                File.WriteAllBytes(file.FullName, new byte[] { 0x4e, 0x41, 0x56, 0x58 });

                PlatformNotSupportedException exception = Assert.Throws<PlatformNotSupportedException>(() => AzureSignToolSigner.ValidatePlatformSupport(new[] { file }, Architecture.Arm64));

                Assert.Equal(Resources.DynamicsBusinessCentralArm64NotSupported, exception.Message);
                AzureSignToolSigner.ValidatePlatformSupport(new[] { file }, Architecture.X64);
            }
        }

        [Fact]
        public void OtherAppFile_OnArm64_DoesNotRequireBusinessCentralSip()
        {
            using (TemporaryDirectory directory = new(_directoryService))
            {
                FileInfo file = new(Path.Combine(directory.Directory.FullName, "file.app"));
                File.WriteAllText(file.FullName, "{}");

                AzureSignToolSigner.ValidatePlatformSupport(new[] { file }, Architecture.Arm64);
            }
        }

        public void Dispose()
        {
            _directoryService.Dispose();
        }
    }
}
