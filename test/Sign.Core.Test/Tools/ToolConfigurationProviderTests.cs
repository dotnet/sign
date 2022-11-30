// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class ToolConfigurationProviderTests
    {
        private readonly ToolConfigurationProvider _provider = new();
        private readonly DirectoryInfo _rootDirectory = new(Path.GetDirectoryName(Environment.ProcessPath)!);

        [Fact]
        public void Mage_Always_ReturnsFile()
        {
            string expectedFilePath = Path.Combine(_rootDirectory.FullName, "tools", "SDK", "x86", "mage.exe");

            Assert.Equal(expectedFilePath, _provider.Mage.FullName);
        }

        [Fact]
        public void MakeAppx_Always_ReturnsFile()
        {
            string expectedFilePath = Path.Combine(_rootDirectory.FullName, "tools", "SDK", "x64", "makeappx.exe");

            Assert.Equal(expectedFilePath, _provider.MakeAppx.FullName);
        }

        [Fact]
        public void SignToolManifest_Always_ReturnsFile()
        {
            string expectedFilePath = Path.Combine(_rootDirectory.FullName, "tools", "SDK", "x64", "SignTool.exe.manifest");

            Assert.Equal(expectedFilePath, _provider.SignToolManifest.FullName);
        }
    }
}