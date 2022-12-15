// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core
{
    internal sealed class AppRootDirectoryLocator : IAppRootDirectoryLocator
    {
        private static readonly Lazy<DirectoryInfo> LazyDirectory = new(GetAppRootDirectory);

        public DirectoryInfo Directory => LazyDirectory.Value;

        // Dependency injection requires a public constructor.
        public AppRootDirectoryLocator()
        {
        }

        private static DirectoryInfo GetAppRootDirectory()
        {
            string filePath = typeof(AppRootDirectoryLocator).Assembly.Location;
            FileInfo file = new(filePath);

            return file.Directory!;
        }
    }
}