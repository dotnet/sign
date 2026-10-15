// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Runtime.InteropServices;

namespace Sign.Core.Test
{
    public class ToolConfigurationProviderTests
    {
        private static readonly AppRootDirectoryLocator DirectoryLocator = new();
        private readonly ToolConfigurationProvider _provider = new(DirectoryLocator);
        private readonly DirectoryInfo _rootDirectory = DirectoryLocator.Directory!;

        [Fact]
        public void Constructor_WhenAppRootDirectoryLocatorIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ToolConfigurationProvider(appRootDirectoryLocator: null!));

            Assert.Equal("appRootDirectoryLocator", exception.ParamName);
        }

        [Fact]
        public void Mage_Always_ReturnsFile()
        {
            string expectedFilePath = Path.Combine(_rootDirectory.FullName, "tools", "SDK", "x86", "mage.exe");

            Assert.Equal(expectedFilePath, _provider.Mage.FullName);
        }

        [Fact]
        public void MakeAppx_Always_ReturnsFile()
        {
            string expectedFilePath = Path.Combine(_rootDirectory.FullName, "tools", "SDK", WindowsArchitecture.GetSdkDirectoryName(RuntimeInformation.ProcessArchitecture), "makeappx.exe");

            Assert.Equal(expectedFilePath, _provider.MakeAppx.FullName);
        }

        [Fact]
        public void SignToolManifest_Always_ReturnsFile()
        {
            string expectedFilePath = Path.Combine(_rootDirectory.FullName, "tools", "SDK", WindowsArchitecture.GetSdkDirectoryName(RuntimeInformation.ProcessArchitecture), "SignTool.exe.manifest");

            Assert.Equal(expectedFilePath, _provider.SignToolManifest.FullName);
        }

        [Theory]
        [InlineData(Architecture.X64, "x64")]
        [InlineData(Architecture.Arm64, "arm64")]
        public void Constructor_SelectsNativeToolsForProcessArchitecture(Architecture architecture, string directoryName)
        {
            ToolConfigurationProvider provider = new(DirectoryLocator, architecture);

            Assert.Equal(Path.Combine(_rootDirectory.FullName, "tools", "SDK", directoryName, "makeappx.exe"), provider.MakeAppx.FullName);
            Assert.Equal(Path.Combine(_rootDirectory.FullName, "tools", "SDK", directoryName, "SignTool.exe.manifest"), provider.SignToolManifest.FullName);
            Assert.Equal(Path.Combine(_rootDirectory.FullName, "tools", "SDK", "x86", "mage.exe"), provider.Mage.FullName);
            Assert.True(provider.MakeAppx.Exists);
            Assert.True(provider.SignToolManifest.Exists);
        }
    }
}
