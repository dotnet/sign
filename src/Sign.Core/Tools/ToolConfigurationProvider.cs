namespace Sign.Core
{
    internal sealed class ToolConfigurationProvider : IToolConfigurationProvider
    {
        private readonly DirectoryInfo _rootDirectory;

        public FileInfo Mage { get; }
        public FileInfo MakeAppx { get; }
        public FileInfo SignToolManifest { get; }

        // Dependency injection requires a public constructor.
        public ToolConfigurationProvider()
        {
            _rootDirectory = new DirectoryInfo(Path.GetDirectoryName(Environment.ProcessPath)!);

            DirectoryInfo sdkDirectory = new(Path.Combine(_rootDirectory.FullName, "tools", "SDK"));

            Mage = new FileInfo(Path.Combine(sdkDirectory.FullName, "x86", "mage.exe"));
            MakeAppx = new FileInfo(Path.Combine(sdkDirectory.FullName, "x64", "makeappx.exe"));
            SignToolManifest = new FileInfo(Path.Combine(sdkDirectory.FullName, "x64", "SignTool.exe.manifest"));
        }
    }
}