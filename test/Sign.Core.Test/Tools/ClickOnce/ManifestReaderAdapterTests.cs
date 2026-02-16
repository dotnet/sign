// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;
using Moq;

namespace Sign.Core.Test
{
    [Collection(SigningTestsCollection.Name)]
    public class ManifestReaderAdapterTests : IDisposable
    {
        private readonly DirectoryService _directoryService;
        private readonly TemporaryDirectory _temporaryDirectory;

        public ManifestReaderAdapterTests()
        {
            _directoryService = new DirectoryService(Mock.Of<ILogger<IDirectoryService>>());
            _temporaryDirectory = new TemporaryDirectory(_directoryService);
        }

        public void Dispose()
        {
            _temporaryDirectory.Dispose();
            _directoryService.Dispose();
        }

        [Fact]
        public async Task TryReadDeployManifest_WhenSuccessful_SetsReadOnlyToFalse()
        {
            TestClickOnceApp testApp = TestClickOnceApp.Create(_temporaryDirectory.Directory);

            ManifestReaderAdapter adapter = new();

            await using FileStream stream = testApp.DeploymentManifestFile.OpenRead();

            bool result = adapter.TryReadDeployManifest(stream, out IDeployManifest? deployManifest);

            Assert.True(result);
            Assert.NotNull(deployManifest);
            Assert.False(deployManifest!.ReadOnly);
        }

        [Fact]
        public async Task TryReadApplicationManifest_WhenSuccessful_SetsReadOnlyToFalse()
        {
            TestClickOnceApp testApp = TestClickOnceApp.Create(_temporaryDirectory.Directory);

            ManifestReaderAdapter adapter = new();

            await using FileStream stream = testApp.ApplicationManifestFile.OpenRead();

            bool result = adapter.TryReadApplicationManifest(stream, out IApplicationManifest? applicationManifest);

            Assert.True(result);
            Assert.NotNull(applicationManifest);
            Assert.False(applicationManifest!.ReadOnly);
        }
    }
}
