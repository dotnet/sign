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