// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    internal sealed class AggregatingSignatureProviderTest
    {
        private readonly ContainerProviderStub _containerProvider;

        internal Dictionary<string, ContainerSpy> Containers { get; }
        internal IEnumerable<FileInfo> Files { get; }
        internal AggregatingSignatureProvider Provider { get; }
        internal SignatureProviderSpy SignatureProviderSpy { get; }

        /// <summary>
        /// Creates a test for testing <see cref="AggregatingSignatureProvider" />.
        /// </summary>
        /// <param name="paths">Zero or more relative file or directory paths.</param>
        /// <remarks>
        /// All paths:
        ///
        ///     * must use only forward slashes (/) to delimit files and directories
        ///
        ///         Examples:
        ///           ❌ directory\a.dll
        ///           ✔️ directory/a.dll
        ///
        ///     * must not begin with a forward slash (/)
        ///
        ///         Examples:
        ///           ❌ /a.dll
        ///           ❌ /directory/b.dll
        ///           ✔️ a.dll
        ///           ✔️ directory/b.dll
        /// 
        ///     * must not contain current (.) or parent (..) directory specifiers
        ///
        ///         Examples:
        ///           ❌ ./a.dll
        ///           ❌ ../b.dll
        ///           ❌ directory/../c.dll
        ///           ✔️ a.dll
        ///           ✔️ directory/c.dll
        ///
        ///     * must be a relative path relative to the same arbitrary root path
        ///
        ///         Examples:
        ///           ❌ C:\a.dll
        ///           ❌ //directory/b.dll
        ///           ✔️ a.dll
        ///           ✔️ directory/b.dll
        ///
        ///     * must represent a file or directory, but paths that represent a directory must have
        ///       a trailing forward slash (/) to distinguish them from a file
        ///
        ///         Examples:
        ///           ❌ a.dll/
        ///           ❌ directory
        ///           ❌ directory/subdirectory
        ///           ✔️ a.dll
        ///           ✔️ directory/
        ///           ✔️ directory/subdirectory/
        /// 
        ///     * should treat a container file as a file when the entire path represents a container file and
        ///       should treat a container file as a directory when the entire path represents a file or
        ///       directory within the container file
        ///
        ///         Examples:
        ///           ❌ container.zip/
        ///           ✔️ container.zip/a.dll
        ///           ✔️ container.zip/directory/b.dll
        ///           ✔️ container.zip/nestedcontainer.zip/directory/c.dll
        ///
        /// </remarks>
        internal AggregatingSignatureProviderTest(params string[] paths)
        {
            _containerProvider = new ContainerProviderStub();
            Containers = new Dictionary<string, ContainerSpy>(StringComparer.Ordinal);
            SignatureProviderSpy = new SignatureProviderSpy();

            HashSet<FileInfo> looseFiles = new(FileInfoComparer.Instance);
            AzureSignToolSignatureProvider azureSignToolSignatureProvider = new(
                Mock.Of<IToolConfigurationProvider>(),
                Mock.Of<IKeyVaultService>(),
                Mock.Of<ILogger<ISignatureProvider>>());
            FileMetadataServiceStub fileMetadataService = new();

            // This directory doesn't actually exist or even need to exist.
            // It is only used to construct rooted file paths in memory.
            DirectoryInfo rootDirectory = new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

            foreach (string path in paths)
            {
                int startIndex = 0;
                int lastEndIndex = startIndex;
                int endIndex;
                HashSet<FileInfo> files = looseFiles;
                string? relativePath;
                bool isDirectory = false;

                do
                {
                    endIndex = path.IndexOf('/', lastEndIndex);

                    if (endIndex == -1)
                    {
                        relativePath = path[0..(path.Length)];
                    }
                    else if (endIndex == path.Length - 1)
                    {
                        // It's a directory because it has a trailing slash.
                        relativePath = path[0..(path.Length)];
                        isDirectory = true;

                        endIndex = -1;
                    }
                    else
                    {
                        relativePath = path[0..endIndex];
                    }

                    string fullPath = new(Path.Combine(rootDirectory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                    FileInfo file = new(fullPath);

                    if (!isDirectory &&
                        (_containerProvider.IsAppxBundleContainer(file) ||
                        _containerProvider.IsAppxContainer(file) ||
                        _containerProvider.IsNuGetContainer(file) ||
                        _containerProvider.IsZipContainer(file)))
                    {
                        files.Add(file);

                        if (!Containers.TryGetValue(relativePath, out ContainerSpy? container))
                        {
                            container = Containers[relativePath] = new ContainerSpy(file);
                        }

                        files = container.Files;

                        if (endIndex != -1)
                        {
                            startIndex = lastEndIndex = endIndex + 1;
                        }
                    }
                    else if (endIndex == -1)
                    {
                        files.Add(file);

                        if (azureSignToolSignatureProvider.CanSign(file))
                        {
                            fileMetadataService.PortableExecutableFiles.Add(file);
                        }
                    }
                    else
                    {
                        lastEndIndex = endIndex + 1;
                    }
                } while (endIndex >= 0);
            }

            List<string> inMemoryFiles = looseFiles.Select(looseFile => looseFile.FullName).ToList();
            InMemoryDirectoryInfo inMemoryDirectoryInfo = new(rootDirectory.FullName, inMemoryFiles);
            FileMatcher fileMatcher = new();
            Matcher matcher = new();

            matcher.AddInclude("**/*");

            Files = fileMatcher.EnumerateMatches(inMemoryDirectoryInfo, matcher).ToList();
            Provider = new AggregatingSignatureProvider(
                new[] { SignatureProviderSpy },
                SignatureProviderSpy,
                _containerProvider,
                fileMetadataService,
                new MatcherFactory());
            _containerProvider.Containers = Containers.Values.ToList();
        }
    }
}