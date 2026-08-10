// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Sign.Core.Test
{
    internal sealed class ContainerProviderStub : IContainerProvider
    {
        private readonly ContainerProvider _containerProvider;

        internal IEnumerable<ContainerSpy> Containers { get; set; } = Enumerable.Empty<ContainerSpy>();

        internal ContainerProviderStub()
        {
            _containerProvider = new ContainerProvider(
                Substitute.For<ICertificateProvider>(),
                Substitute.For<IDirectoryService>(),
                Substitute.For<IFileMatcher>(),
                Substitute.For<IMakeAppxCli>(),
                Substitute.For<ILogger<IContainerProvider>>());
        }

        public bool IsAppxBundleContainer(FileInfo file)
        {
            return _containerProvider.IsAppxBundleContainer(file);
        }

        public bool IsAppxContainer(FileInfo file)
        {
            return _containerProvider.IsAppxContainer(file);
        }

        public bool IsNuGetContainer(FileInfo file)
        {
            return _containerProvider.IsNuGetContainer(file);
        }

        public bool IsZipContainer(FileInfo file)
        {
            return _containerProvider.IsZipContainer(file);
        }

        public IContainer? GetContainer(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            foreach (ContainerSpy container in Containers)
            {
                if (FileInfoComparer.Instance.Equals(container.File, file))
                {
                    return container;
                }
            }

            return null;
        }
    }
}