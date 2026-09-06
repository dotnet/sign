// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using System.Xml.Linq;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed class ClickOnceDeploymentReferenceValidationTests : IDisposable
    {
        private const string AssemblyV2Namespace =
            "urn:schemas-microsoft-com:asm.v2";
        private const string ApplicationManifestFileName =
            "App.exe.manifest";
        private const string DeploymentManifestFileName =
            "App.application";
        private const string PrerequisiteFileName =
            "prerequisite.dll";
        private const string VstoManifestFileName = "App.vsto";

        private readonly DirectoryService _directoryService;

        public ClickOnceDeploymentReferenceValidationTests()
        {
            _directoryService = new(
                Substitute.For<ILogger<IDirectoryService>>());
        }

        public void Dispose()
        {
            _directoryService.Dispose();
        }

        [Theory]
        [InlineData(DeploymentManifestFileName)]
        [InlineData(VstoManifestFileName)]
        public void DeploymentPublishLayoutResolver_WhenSerializedManifestHasOneApplicationDependencyAndNoFiles_Succeeds(
            string deploymentManifestFileName)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo applicationManifest =
                ClickOnceResolutionTestUtilities.WriteApplicationManifest(
                    root,
                    ApplicationManifestFileName);
            FileInfo deploymentManifest =
                ClickOnceResolutionTestUtilities.WriteDeploymentManifest(
                    root,
                    deploymentManifestFileName,
                    applicationManifest.Name);
            ClickOnceDeploymentPublishLayoutResolver resolver = new(
                new ClickOnceManifestReader(),
                new ClickOncePayloadResolver());

            ResolvedClickOncePublishLayout layout = resolver.Resolve(deploymentManifest);

            IDeployManifest deployManifest =
                Assert.IsAssignableFrom<IDeployManifest>(
                    layout.Deployment!.Manifest);
            Assert.Equal(
                deploymentManifest.FullName,
                layout.Deployment!.Source.FullName);
            Assert.Equal(
                applicationManifest.FullName,
                layout.Application.Source.FullName);
            Assert.Single(deployManifest.AssemblyReferences);
            Assert.Same(
                deployManifest.EntryPoint,
                layout.Deployment.ApplicationManifestReference);
            Assert.Empty(deployManifest.FileReferences);
            Assert.Empty(layout.Application.Payloads);
        }

        [Fact]
        public void DeploymentPublishLayoutResolver_WhenSerializedManifestHasExtraInstallDependency_RejectsBeforeResolution()
        {
            AssertSerializedManifestRejected(
                manifest =>
                    manifest.AssemblyReferences.Add(
                        new AssemblyReference("extra.dll")),
                assertParsedManifest: manifest =>
                    Assert.Equal(
                        expected: 2,
                        actual: manifest.AssemblyReferences.Count));
        }

        [Fact]
        public void DeploymentPublishLayoutResolver_WhenSerializedManifestHasFileReference_RejectsBeforeResolution()
        {
            AssertSerializedManifestRejected(
                manifest =>
                    manifest.FileReferences.Add(
                        new FileReference("extra.dat")),
                assertParsedManifest: manifest =>
                    Assert.Single(manifest.FileReferences));
        }

        [Fact]
        public void DeploymentPublishLayoutResolver_WhenSerializedManifestHasExtraPrerequisite_RejectsBeforeResolution()
        {
            AssertSerializedManifestRejected(
                manifest =>
                    manifest.AssemblyReferences.Add(
                        CreatePrerequisiteReference()),
                MakeExtraDependencyPrerequisite,
                assertParsedManifest: manifest =>
                    Assert.Contains(
                        manifest.AssemblyReferences.Cast<AssemblyReference>(),
                        reference => reference.IsPrerequisite));
        }

        [Fact]
        public void DeploymentPublishLayoutResolver_WhenSerializedManifestHasDuplicateDependency_RejectsBeforeResolution()
        {
            AssertSerializedManifestRejected(
                _ =>
                {
                },
                DuplicateDependency,
                manifest =>
                    Assert.Equal(
                        expected: 2,
                        actual: manifest.AssemblyReferences.Count));
        }

        [Fact]
        public void DeploymentPublishLayoutResolver_WhenSerializedManifestHasSolePrerequisite_RejectsBeforeResolution()
        {
            AssertSerializedManifestRejected(
                _ =>
                {
                },
                MakeSoleDependencyPrerequisite,
                manifest =>
                    Assert.True(
                        Assert.Single(
                            manifest.AssemblyReferences
                                .Cast<AssemblyReference>())
                            .IsPrerequisite));
        }

        [Fact]
        public void DeploymentPublishLayoutResolver_WhenEntryPointIsOptional_RejectsBeforeResolution()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            FileInfo deploymentManifestFile =
                ClickOnceResolutionTestUtilities.CreateFile(
                    temporaryDirectory.Directory,
                    DeploymentManifestFileName);
            DeployManifest model = CreateDeploymentManifest();
            AssemblyReference entryPoint = model.EntryPoint!;

            entryPoint.IsOptional = true;

            IDeployManifest deploymentManifest =
                Substitute.For<IDeployManifest>();

            deploymentManifest.AssemblyReferences.Returns(
                model.AssemblyReferences);
            deploymentManifest.EntryPoint.Returns(entryPoint);
            deploymentManifest.FileReferences.Returns(model.FileReferences);
            deploymentManifest.Diagnostics.Returns(
                Array.Empty<ClickOnceManifestDiagnostic>());

            IClickOnceManifestReader manifestReader =
                Substitute.For<IClickOnceManifestReader>();

            manifestReader.TryReadDeployManifest(
                    Arg.Any<Stream>(),
                    out Arg.Any<IDeployManifest?>())
                .Returns(callInfo =>
                {
                    callInfo[1] = deploymentManifest;
                    return true;
                });

            AssertUnsupportedReferences(
                deploymentManifestFile,
                () => new ClickOnceDeploymentPublishLayoutResolver(
                    manifestReader,
                    new ClickOncePayloadResolver())
                    .Resolve(deploymentManifestFile));
            deploymentManifest.DidNotReceive().ResolveFiles(
                Arg.Any<IReadOnlyList<DirectoryInfo>>());
        }

        [Fact]
        public void DeploymentPublishLayoutResolver_WhenReferencesAndEntryPointTargetAreUnsupported_ReportsReferencesFirst()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            FileInfo deploymentManifestFile =
                ClickOnceResolutionTestUtilities.CreateFile(
                    temporaryDirectory.Directory,
                    DeploymentManifestFileName);
            DeployManifest model = CreateDeploymentManifest();
            AssemblyReference entryPoint = model.EntryPoint!;

            entryPoint.TargetPath = Path.Combine(
                temporaryDirectory.Directory.FullName,
                ApplicationManifestFileName);
            model.AssemblyReferences.Add(
                new AssemblyReference("extra.dll"));

            IDeployManifest deploymentManifest =
                Substitute.For<IDeployManifest>();

            deploymentManifest.AssemblyReferences.Returns(
                model.AssemblyReferences);
            deploymentManifest.EntryPoint.Returns(entryPoint);
            deploymentManifest.FileReferences.Returns(model.FileReferences);
            deploymentManifest.Diagnostics.Returns(
                Array.Empty<ClickOnceManifestDiagnostic>());

            IClickOnceManifestReader manifestReader =
                Substitute.For<IClickOnceManifestReader>();

            manifestReader.TryReadDeployManifest(
                    Arg.Any<Stream>(),
                    out Arg.Any<IDeployManifest?>())
                .Returns(callInfo =>
                {
                    callInfo[1] = deploymentManifest;
                    return true;
                });

            AssertUnsupportedReferences(
                deploymentManifestFile,
                () => new ClickOnceDeploymentPublishLayoutResolver(
                    manifestReader,
                    new ClickOncePayloadResolver())
                    .Resolve(deploymentManifestFile));
            deploymentManifest.DidNotReceive().ResolveFiles(
                Arg.Any<IReadOnlyList<DirectoryInfo>>());
        }

        [Theory]
        [InlineData("resourceFallbackCulture", false)]
        [InlineData("resourceFallbackCultureInternal", false)]
        [InlineData("resourceFallbackCulture", true)]
        [InlineData("resourceFallbackCultureInternal", true)]
        public void DeploymentPublishLayoutResolver_WhenEntryPointHasResourceFallbackCulture_RejectsBeforeResolution(
            string attributeName,
            bool useQualifiedAttribute)
        {
            AssertSerializedManifestRejected(
                _ =>
                {
                },
                deploymentManifest =>
                    SetSoleDependentAssemblyResourceFallback(
                        deploymentManifest,
                        attributeName,
                        useQualifiedAttribute),
                manifest =>
                    Assert.True(
                        manifest.HasUnsupportedResourceFallback));
        }

        [Fact]
        public void DeploymentPublishLayoutResolver_WhenEntryPointIsNotTheSoleCollectionReference_RejectsBeforeResolution()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            FileInfo deploymentManifestFile =
                ClickOnceResolutionTestUtilities.CreateFile(
                    temporaryDirectory.Directory,
                    DeploymentManifestFileName);
            AssemblyReference collectionReference = new()
            {
                TargetPath = ApplicationManifestFileName
            };
            AssemblyReference entryPoint = new()
            {
                TargetPath = ApplicationManifestFileName
            };
            DeployManifest model = CreateDeploymentManifest();

            model.AssemblyReferences.Clear();
            model.AssemblyReferences.Add(collectionReference);
            model.EntryPoint = collectionReference;

            IDeployManifest deploymentManifest =
                Substitute.For<IDeployManifest>();
            bool resolveFilesCalled = false;

            deploymentManifest.AssemblyReferences.Returns(
                model.AssemblyReferences);
            deploymentManifest.EntryPoint.Returns(entryPoint);
            deploymentManifest.FileReferences.Returns(model.FileReferences);
            deploymentManifest.Diagnostics.Returns(
                Array.Empty<ClickOnceManifestDiagnostic>());
            deploymentManifest
                .When(manifest => manifest.ResolveFiles(
                    Arg.Any<IReadOnlyList<DirectoryInfo>>()))
                .Do(_ => resolveFilesCalled = true);

            IClickOnceManifestReader manifestReader =
                Substitute.For<IClickOnceManifestReader>();

            manifestReader.TryReadDeployManifest(
                    Arg.Any<Stream>(),
                    out Arg.Any<IDeployManifest?>())
                .Returns(callInfo =>
                {
                    callInfo[1] = deploymentManifest;
                    return true;
                });

            int fileProbeCount = 0;
            ClickOnceDeploymentPublishLayoutResolver resolver = new(
                manifestReader,
                new ClickOncePayloadResolver(),
                _ =>
                {
                    ++fileProbeCount;
                    return false;
                });

            AssertUnsupportedReferences(
                deploymentManifestFile,
                () => resolver.Resolve(deploymentManifestFile));
            Assert.False(resolveFilesCalled);
            Assert.Equal(expected: 0, actual: fileProbeCount);
        }

        private void AssertSerializedManifestRejected(
            Action<DeployManifest> mutateManifest,
            Action<FileInfo>? mutateSerializedManifest = null,
            Action<IDeployManifest>? assertParsedManifest = null)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo applicationManifest =
                ClickOnceResolutionTestUtilities.WriteApplicationManifest(
                    root,
                    ApplicationManifestFileName);
            DeployManifest model = CreateDeploymentManifest();

            model.EntryPoint!.TargetPath = applicationManifest.Name;
            mutateManifest(model);

            FileInfo deploymentManifestFile =
                ClickOnceResolutionTestUtilities.WriteManifest(
                    root,
                    DeploymentManifestFileName,
                    model);

            mutateSerializedManifest?.Invoke(deploymentManifestFile);

            ClickOnceManifestReader realManifestReader = new();
            IDeployManifest parsedManifest;

            using (FileStream stream = deploymentManifestFile.OpenRead())
            {
                Assert.True(
                    realManifestReader.TryReadDeployManifest(
                        stream,
                        out IDeployManifest? manifest));
                parsedManifest = Assert.IsAssignableFrom<IDeployManifest>(
                    manifest);
            }

            assertParsedManifest?.Invoke(parsedManifest);

            IDeployManifest recordingManifest =
                Substitute.For<IDeployManifest>();
            bool resolveFilesCalled = false;

            recordingManifest.AssemblyReferences.Returns(
                parsedManifest.AssemblyReferences);
            recordingManifest.EntryPoint.Returns(
                parsedManifest.EntryPoint);
            recordingManifest.FileReferences.Returns(
                parsedManifest.FileReferences);
            recordingManifest.HasUnsupportedResourceFallback.Returns(
                parsedManifest.HasUnsupportedResourceFallback);
            recordingManifest.Diagnostics.Returns(
                parsedManifest.Diagnostics);
            recordingManifest
                .When(manifest => manifest.ResolveFiles(
                    Arg.Any<IReadOnlyList<DirectoryInfo>>()))
                .Do(_ => resolveFilesCalled = true);

            int applicationReadCount = 0;
            IClickOnceManifestReader manifestReader =
                Substitute.For<IClickOnceManifestReader>();

            manifestReader.TryReadDeployManifest(
                    Arg.Any<Stream>(),
                    out Arg.Any<IDeployManifest?>())
                .Returns(callInfo =>
                {
                    callInfo[1] = recordingManifest;
                    return true;
                });
            manifestReader.TryReadApplicationManifest(
                    Arg.Any<Stream>(),
                    out Arg.Any<IApplicationManifest?>())
                .Returns(callInfo =>
                {
                    ++applicationReadCount;
                    callInfo[1] = null;
                    return false;
                });

            int fileProbeCount = 0;
            ClickOnceDeploymentPublishLayoutResolver resolver = new(
                manifestReader,
                new ClickOncePayloadResolver(),
                _ =>
                {
                    ++fileProbeCount;
                    return false;
                });

            AssertUnsupportedReferences(
                deploymentManifestFile,
                () => resolver.Resolve(deploymentManifestFile));
            Assert.False(resolveFilesCalled);
            Assert.Equal(expected: 0, actual: applicationReadCount);
            Assert.Equal(expected: 0, actual: fileProbeCount);
        }

        private static DeployManifest CreateDeploymentManifest()
        {
            DeployManifest manifest = new();

            manifest.AssemblyIdentity.Name = "TestDeployment";
            manifest.AssemblyIdentity.Version =
                ClickOnceResolutionTestUtilities.ManifestVersion;
            manifest.AssemblyIdentity.ProcessorArchitecture =
                ClickOnceResolutionTestUtilities.ProcessorArchitecture;

            AssemblyReference entryPoint = new(
                ApplicationManifestFileName);

            manifest.AssemblyReferences.Add(entryPoint);
            manifest.EntryPoint = entryPoint;

            return manifest;
        }

        private static AssemblyReference CreatePrerequisiteReference()
        {
            return new AssemblyReference()
            {
                AssemblyIdentity = new AssemblyIdentity(
                    "Prerequisite",
                    ClickOnceResolutionTestUtilities.ManifestVersion),
                IsPrerequisite = true,
                TargetPath = PrerequisiteFileName
            };
        }

        private static void DuplicateDependency(FileInfo deploymentManifest)
        {
            XDocument document = XDocument.Load(
                deploymentManifest.FullName);
            XElement dependency = Assert.Single(
                document.Descendants(),
                element => element.Name.LocalName == "dependency");

            dependency.AddAfterSelf(new XElement(dependency));
            document.Save(
                deploymentManifest.FullName,
                SaveOptions.DisableFormatting);
            deploymentManifest.Refresh();
        }

        private static void MakeSoleDependencyPrerequisite(
            FileInfo deploymentManifest)
        {
            SetSoleDependentAssemblyAttribute(
                deploymentManifest,
                "dependencyType",
                "preRequisite");
        }

        private static void SetSoleDependentAssemblyAttribute(
            FileInfo deploymentManifest,
            string attributeName,
            string attributeValue)
        {
            XDocument document = XDocument.Load(
                deploymentManifest.FullName);
            XElement dependentAssembly = Assert.Single(
                document.Descendants(),
                element =>
                    element.Name.LocalName == "dependentAssembly");

            dependentAssembly.SetAttributeValue(
                attributeName,
                attributeValue);
            document.Save(
                deploymentManifest.FullName,
                SaveOptions.DisableFormatting);
            deploymentManifest.Refresh();
        }

        private static void SetSoleDependentAssemblyResourceFallback(
            FileInfo deploymentManifest,
            string attributeName,
            bool useQualifiedAttribute)
        {
            XDocument document = XDocument.Load(
                deploymentManifest.FullName);
            XElement dependentAssembly = Assert.Single(
                document.Descendants(),
                element =>
                    element.Name.LocalName == "dependentAssembly");

            XName resourceFallbackAttribute = useQualifiedAttribute
                ? XName.Get(
                    attributeName,
                    AssemblyV2Namespace)
                : attributeName;

            dependentAssembly.SetAttributeValue(
                resourceFallbackAttribute,
                "en-US");

            document.Save(
                deploymentManifest.FullName,
                SaveOptions.DisableFormatting);
            deploymentManifest.Refresh();
        }

        private static void MakeExtraDependencyPrerequisite(
            FileInfo deploymentManifest)
        {
            XDocument document = XDocument.Load(
                deploymentManifest.FullName);
            XElement dependentAssembly = Assert.Single(
                document.Descendants(),
                element =>
                    element.Name.LocalName == "dependentAssembly" &&
                    string.Equals(
                        (string?)element.Attribute("codebase"),
                        PrerequisiteFileName,
                        StringComparison.Ordinal));

            dependentAssembly.SetAttributeValue(
                "dependencyType",
                "preRequisite");
            document.Save(
                deploymentManifest.FullName,
                SaveOptions.DisableFormatting);
            deploymentManifest.Refresh();
        }

        private static void AssertUnsupportedReferences(
            FileInfo deploymentManifestFile,
            Action resolve)
        {
            ClickOncePublishLayoutResolutionException exception =
                Assert.Throws<ClickOncePublishLayoutResolutionException>(
                    resolve);

            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceDeploymentManifestUnsupportedReferences,
                    deploymentManifestFile.FullName),
                exception.Message);
        }
    }
}
