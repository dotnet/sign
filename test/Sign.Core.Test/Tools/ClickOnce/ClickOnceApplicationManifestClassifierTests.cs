// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging.Abstractions;

namespace Sign.Core.Test
{
    public sealed class ClickOnceApplicationManifestClassifierTests : IDisposable
    {
        private readonly DirectoryService _directories = new(NullLogger<IDirectoryService>.Instance);

        [Theory]
        [InlineData("missing")]
        [InlineData("malformed")]
        [InlineData("fusion")]
        [InlineData("locked")]
        public void InitialNonselectionIsCached(string kind)
        {
            using TemporaryDirectory directory = new(_directories);
            FileInfo file = ClickOnceResolutionTestUtilities.WriteApplicationManifest(directory.Directory, "App.manifest");
            SigningFile source = SigningFile.Capture(file);
            if (kind == "missing")
            {
                file.Delete();
            }
            else if (kind == "malformed")
            {
                File.WriteAllText(file.FullName, "<assembly>");
            }
            else if (kind == "fusion")
            {
                File.WriteAllText(file.FullName,
                    """<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0"><assemblyIdentity name="SideBySide" version="1.0.0.0" type="win32" /></assembly>""");
            }
            ClickOnceApplicationManifestClassifier classifier = new(new ClickOnceManifestReader(), NullLogger<IDataFormatSigner>.Instance);
            using (FileStream? locked = kind == "locked"
                ? new FileStream(file.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                : null)
            {
                Assert.False(classifier.IsApplication(source));
            }
            ClickOnceResolutionTestUtilities.WriteApplicationManifest(directory.Directory, file.Name);

            Assert.False(classifier.IsApplication(source));
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("malformed")]
        [InlineData("deployment")]
        [InlineData("valid-mutation")]
        [InlineData("locked")]
        public void PositiveClassificationSurvivesChangeButValidationFails(string kind)
        {
            using TemporaryDirectory directory = new(_directories);
            FileInfo file = ClickOnceResolutionTestUtilities.WriteApplicationManifest(directory.Directory, "App.manifest");
            SigningFile source = SigningFile.Capture(file);
            ClickOnceApplicationManifestClassifier classifier = new(new ClickOnceManifestReader(), NullLogger<IDataFormatSigner>.Instance);
            Assert.True(classifier.IsApplication(source));
            classifier.Validate(source);
            switch (kind)
            {
                case "missing":
                    file.Delete();
                    break;
                case "malformed":
                    File.WriteAllText(file.FullName, "<assembly>");
                    break;
                case "deployment":
                    ClickOnceResolutionTestUtilities.WriteDeploymentManifest(directory.Directory, file.Name, "Other.manifest");
                    break;
                case "valid-mutation":
                    File.AppendAllText(file.FullName, "\n<!--changed-->");
                    break;
            }
            using FileStream? locked = kind == "locked"
                ? new FileStream(file.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                : null;

            Assert.True(classifier.IsApplication(source));
            SigningException exception = Assert.Throws<SigningException>(() => classifier.Validate(source));
            Assert.Contains("changed or became unreadable after classification", exception.Message);
        }

        [Fact]
        public void CacheUsesCapturedContainerIdentityRatherThanStagingPath()
        {
            using TemporaryDirectory directory = new(_directories);
            FileInfo file = ClickOnceResolutionTestUtilities.WriteApplicationManifest(directory.Directory, "App.manifest");
            SigningSourceIdentity parent = SigningSourceIdentity.Capture(
                ClickOnceResolutionTestUtilities.CreateFile(directory.Directory, "bundle.zip"));
            SigningFile first = SigningFile.ContainerEntry(file, parent, "first/App.manifest");
            SigningFile second = SigningFile.ContainerEntry(file, parent, "second/App.manifest");
            ClickOnceApplicationManifestClassifier classifier = new(new ClickOnceManifestReader(), NullLogger<IDataFormatSigner>.Instance);
            Assert.True(classifier.IsApplication(first));
            File.WriteAllText(file.FullName, "<assembly>");

            Assert.True(classifier.IsApplication(first));
            Assert.False(classifier.IsApplication(second));
            Assert.Throws<SigningException>(() => classifier.Validate(first));
        }

        public void Dispose() => _directories.Dispose();
    }
}
