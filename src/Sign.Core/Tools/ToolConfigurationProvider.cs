// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class ToolConfigurationProvider : IToolConfigurationProvider
    {
        private readonly DirectoryInfo _rootDirectory;

        public FileInfo Mage { get; }
        public FileInfo MakeAppx { get; }
        public FileInfo SignToolManifest { get; }

        // Dependency injection requires a public constructor.
        public ToolConfigurationProvider(IAppRootDirectoryLocator appRootDirectoryLocator)
        {
            ArgumentNullException.ThrowIfNull(appRootDirectoryLocator, nameof(appRootDirectoryLocator));

            _rootDirectory = appRootDirectoryLocator.Directory;

            DirectoryInfo sdkDirectory = new(Path.Combine(_rootDirectory.FullName, "tools", "SDK"));

            Mage = new FileInfo(Path.Combine(sdkDirectory.FullName, "x86", "mage.exe"));
            MakeAppx = new FileInfo(Path.Combine(sdkDirectory.FullName, "x64", "makeappx.exe"));
            SignToolManifest = new FileInfo(Path.Combine(sdkDirectory.FullName, "x64", "SignTool.exe.manifest"));
        }
    }
}