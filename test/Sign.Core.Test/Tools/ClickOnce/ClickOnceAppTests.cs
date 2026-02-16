// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    [Collection(SigningTestsCollection.Name)]
    public class ClickOnceAppTests : IDisposable
    {
        private readonly DirectoryService _directoryService;
        private readonly TemporaryDirectory _temporaryDirectory;

        public ClickOnceAppTests()
        {
            MsBuildLocatorHelper.EnsureInitialized();
            _directoryService = new DirectoryService(Mock.Of<ILogger<IDirectoryService>>());
            _temporaryDirectory = new TemporaryDirectory(_directoryService);
        }

        public void Dispose()
        {
            _temporaryDirectory.Dispose();
            _directoryService.Dispose();
        }

        [Fact]
        public void TryReadFromDeploymentManifest_WhenDeploymentManifestFileIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => ClickOnceApp.TryReadFromDeploymentManifest(
                    deploymentManifestFile: null!,
                    Mock.Of<ILogger>(),
                    new ManifestReaderAdapter(),
                    out IClickOnceApp? clickOnceApp));

            Assert.Equal("deploymentManifestFile", exception.ParamName);
        }

        [Fact]
        public void TryReadFromDeploymentManifest_WhenLoggerIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => ClickOnceApp.TryReadFromDeploymentManifest(
                    new FileInfo("app.application"),
                    logger: null!,
                    new ManifestReaderAdapter(),
                    out IClickOnceApp? clickOnceApp));

            Assert.Equal("logger", exception.ParamName);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TryReadFromDeploymentManifest_WhenMappedFileExtensionsIsTrueOrFalse_ReturnsTrue(bool mapFileExtensions)
        {
            TestClickOnceApp testApp = TestClickOnceApp.Create(
                _temporaryDirectory.Directory,
                mapFileExtensions: mapFileExtensions);

            bool actualResult = ClickOnceApp.TryReadFromDeploymentManifest(
                testApp.DeploymentManifestFile,
                Mock.Of<ILogger>(),
                new ManifestReaderAdapter(),
                out IClickOnceApp? clickOnceApp);

            Assert.True(actualResult);
            Assert.NotNull(clickOnceApp);
            Assert.NotNull(clickOnceApp.DeploymentManifestFile);
            Assert.NotNull(clickOnceApp.DeploymentManifest);
            Assert.NotNull(clickOnceApp.ApplicationManifestFile);
            Assert.NotNull(clickOnceApp.ApplicationManifest);
            Assert.False(clickOnceApp.DeploymentManifest.ReadOnly);
            Assert.False(clickOnceApp.ApplicationManifest!.ReadOnly);

            List<FileInfo> payloadFiles = clickOnceApp.GetPayloadFiles().ToList();
            string expectedFileName = $"{testApp.Name}.exe";

            if (mapFileExtensions)
            {
                expectedFileName += ".deploy";
            }

            Assert.Collection(payloadFiles, new[]
            {
                (FileInfo payloadFile) => Assert.Equal(expectedFileName, payloadFile.Name)
            });
        }

        [Fact]
        public void TryReadFromDeploymentManifest_WhenApplicationIsInSubdirectory_ReturnsTrue()
        {
            TestClickOnceApp testApp = TestClickOnceApp.Create(
                _temporaryDirectory.Directory,
                applicationRelativeDirectoryPath: @"Application Files\App_1_0_0_0");

            bool actualResult = ClickOnceApp.TryReadFromDeploymentManifest(
                testApp.DeploymentManifestFile,
                Mock.Of<ILogger>(),
                new ManifestReaderAdapter(),
                out IClickOnceApp? clickOnceApp);

            Assert.True(actualResult);
            Assert.NotNull(clickOnceApp);
            Assert.NotNull(clickOnceApp.DeploymentManifestFile);
            Assert.NotNull(clickOnceApp.DeploymentManifest);
            Assert.NotNull(clickOnceApp.ApplicationManifestFile);
            Assert.NotNull(clickOnceApp.ApplicationManifest);
            Assert.False(clickOnceApp.DeploymentManifest.ReadOnly);
            Assert.False(clickOnceApp.ApplicationManifest!.ReadOnly);

            List<FileInfo> payloadFiles = clickOnceApp.GetPayloadFiles().ToList();
            string expectedFileName = $"{testApp.Name}.exe.deploy";

            Assert.Collection(payloadFiles, new[]
            {
                (FileInfo payloadFile) => Assert.Equal(expectedFileName, payloadFile.Name)
            });
        }

        [Fact]
        public void TryReadFromDeploymentManifest_WhenApplicationManifestFileIsMissing_ReturnsTrue()
        {
            TestClickOnceApp testApp = TestClickOnceApp.Create(_temporaryDirectory.Directory);

            testApp.ApplicationManifestFile.Delete();

            bool actualResult = ClickOnceApp.TryReadFromDeploymentManifest(
                testApp.DeploymentManifestFile,
                Mock.Of<ILogger>(),
                new ManifestReaderAdapter(),
                out IClickOnceApp? clickOnceApp);

            Assert.True(actualResult);
            Assert.NotNull(clickOnceApp);
            Assert.NotNull(clickOnceApp.DeploymentManifestFile);
            Assert.NotNull(clickOnceApp.DeploymentManifest);
            Assert.Null(clickOnceApp.ApplicationManifestFile);
            Assert.Null(clickOnceApp.ApplicationManifest);
            Assert.False(clickOnceApp.DeploymentManifest.ReadOnly);
        }

        [Fact]
        public void TryReadManifest_WhenApplicationManifestLoaded_IsWritable()
        {
            TestClickOnceApp testApp = TestClickOnceApp.Create(_temporaryDirectory.Directory);

            bool result = ClickOnceApp.TryReadManifest(
                testApp.ApplicationManifestFile,
                Mock.Of<ILogger>(),
                out ApplicationManifest? manifest,
                new ManifestReaderAdapter());

            Assert.True(result);
            Assert.NotNull(manifest);
            Assert.False(manifest!.ReadOnly);
        }
    }
}
