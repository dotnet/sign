// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using System.Text;
using System.Xml;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed class ClickOnceFileGraphResolverTests : IDisposable
    {
        private const string ApplicationDirectory = @"Application Files\App_1_0_0_0";
        private const string ApplicationManifestFileName = "App.exe.manifest";
        private const string ClrPlatformAssemblyName = "Microsoft.Windows.CommonLanguageRuntime";
        private const string DependencyManifestFileName = "dependency.manifest";
        private const string DeploySuffix = ".deploy";
        private const string DeploymentManifestFileName = "App.application";
        private const string FileContents = "payload";
        private const string LauncherFileName = "Launcher.exe";
        private const string ManifestVersion = "1.0.0.0";
        private const string MissingTargetPathMessage = "without a target path";
        private const string OptionalPayloadFileName = "optional.txt";
        private const string PayloadFileName = "payload.dll";
        private const string ProcessorArchitecture = "msil";
        private const string SharedPayloadFileName = "shared.dll";
        private const string SetupFileName = "setup.exe";
        private const string VstoManifestFileName = "App.vsto";
        private const string WarningMessageName = "GenerateManifest.ResolveFailedInReadWriteMode";
        private const string WarningOneTargetPath = "warning-one.txt";
        private const string WarningTwoTargetPath = "warning-two.txt";

        private readonly DirectoryService _directoryService;
        private readonly ClickOnceApplicationManifestFileGraphResolver _applicationResolver;
        private readonly ClickOnceDeployManifestFileGraphResolver _deploymentResolver;
        private readonly ClickOncePayloadFileResolver _payloadResolver;

        public ClickOnceFileGraphResolverTests()
        {
            _directoryService = new(
                Substitute.For<ILogger<IDirectoryService>>());

            ClickOnceManifestReader manifestReader = new();
            _payloadResolver = new ClickOncePayloadFileResolver();

            _applicationResolver = new ClickOnceApplicationManifestFileGraphResolver(
                manifestReader,
                _payloadResolver);
            _deploymentResolver = new ClickOnceDeployManifestFileGraphResolver(
                manifestReader,
                _payloadResolver);
        }

        public void Dispose()
        {
            _directoryService.Dispose();
        }

        [Fact]
        public void DeploymentResolver_WhenMultipleVersionsAndManifestsExist_UsesReferencedApplicationManifest()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string CurrentApplicationDirectory = @"Application Files\App_2_0_0_0";
            const string CurrentPayloadFileName = "current.dll";
            const string OldPayloadFileName = "old.dll";

            FileInfo currentPayload = CreateFile(
                root,
                $@"{CurrentApplicationDirectory}\{CurrentPayloadFileName}");
            ApplicationManifest currentApplication = CreateApplicationManifest();
            AddFileReference(currentApplication, CurrentPayloadFileName);
            FileInfo currentManifest = WriteManifest(
                root,
                $@"{CurrentApplicationDirectory}\{ApplicationManifestFileName}",
                currentApplication);

            ApplicationManifest oldApplication = CreateApplicationManifest();
            AddFileReference(oldApplication, OldPayloadFileName);
            CreateFile(root, $@"{ApplicationDirectory}\{OldPayloadFileName}");
            WriteManifest(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                oldApplication);
            CreateFusionManifest(
                root,
                $@"{CurrentApplicationDirectory}\{DependencyManifestFileName}");

            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                Path.GetRelativePath(root.FullName, currentManifest.FullName));

            ClickOnceFileGraph graph = _deploymentResolver.Resolve(deploymentManifest);

            Assert.Equal(currentManifest.FullName, graph.ApplicationManifest.Source.FullName);
            Assert.False(graph.DeployManifest!.ReadOnly);
            Assert.False(graph.ApplicationManifestModel.ReadOnly);
            Assert.Equal(
                currentManifest.FullName,
                graph.DeployManifest.EntryPoint!.ResolvedPath);
            ClickOnceFileGraphEntry payload = Assert.Single(graph.Payloads);
            Assert.Equal(currentPayload.FullName, payload.Source.FullName);
            Assert.Equal(CurrentPayloadFileName, payload.TargetPath);
        }

        [Fact]
        public void DeploymentResolver_WhenSameIdentityApplicationManifestExistsAtRoot_UsesTargetPath()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            string applicationManifestPath = $@"{ApplicationDirectory}\{ApplicationManifestFileName}";

            FileInfo expectedPayload = CreateFile(root, $@"{ApplicationDirectory}\target.dll");
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, expectedPayload.Name);
            FileInfo expectedApplicationManifest = WriteManifest(
                root,
                applicationManifestPath,
                application);

            FileInfo decoyPayload = CreateFile(root, "decoy.dll");
            ApplicationManifest decoyApplication = CreateApplicationManifest();
            AddFileReference(decoyApplication, decoyPayload.Name);
            WriteManifest(
                root,
                $"{application.AssemblyIdentity.Name}.manifest",
                decoyApplication);

            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                applicationManifestPath,
                applicationManifestIdentity: application.AssemblyIdentity);

            ClickOnceFileGraph graph = _deploymentResolver.Resolve(deploymentManifest);

            Assert.Equal(expectedApplicationManifest.FullName, graph.ApplicationManifest.Source.FullName);
            Assert.Equal(expectedPayload.FullName, Assert.Single(graph.Payloads).Source.FullName);
            Assert.Equal(application.AssemblyIdentity.Name, graph.DeployManifest!.EntryPoint!.AssemblyIdentity.Name);
            Assert.Equal(application.AssemblyIdentity.Version, graph.DeployManifest.EntryPoint.AssemblyIdentity.Version);
            Assert.Equal(
                application.AssemblyIdentity.ProcessorArchitecture,
                graph.DeployManifest.EntryPoint.AssemblyIdentity.ProcessorArchitecture);
        }

        [Fact]
        public void DeploymentResolver_WhenSameIdentityFusionManifestExistsAtRoot_UsesTargetPath()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            string applicationManifestPath = $@"{ApplicationDirectory}\{ApplicationManifestFileName}";

            FileInfo expectedPayload = CreateFile(root, $@"{ApplicationDirectory}\target.dll");
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, expectedPayload.Name);
            FileInfo expectedApplicationManifest = WriteManifest(
                root,
                applicationManifestPath,
                application);
            CreateFusionManifest(
                root,
                $"{application.AssemblyIdentity.Name}.manifest",
                application.AssemblyIdentity.Name,
                application.AssemblyIdentity.Version);

            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                applicationManifestPath,
                applicationManifestIdentity: application.AssemblyIdentity);

            ClickOnceFileGraph graph = _deploymentResolver.Resolve(deploymentManifest);

            Assert.Equal(expectedApplicationManifest.FullName, graph.ApplicationManifest.Source.FullName);
            Assert.Equal(expectedPayload.FullName, Assert.Single(graph.Payloads).Source.FullName);
            Assert.Equal(application.AssemblyIdentity.Name, graph.DeployManifest!.EntryPoint!.AssemblyIdentity.Name);
            Assert.Equal(application.AssemblyIdentity.Version, graph.DeployManifest.EntryPoint.AssemblyIdentity.Version);
            Assert.Equal(
                application.AssemblyIdentity.ProcessorArchitecture,
                graph.DeployManifest.EntryPoint.AssemblyIdentity.ProcessorArchitecture);
        }

        [Fact]
        public void DeploymentResolver_WhenVstoDeploymentManifestIsValid_ResolvesFileGraph()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;

            FileInfo payload = CreateFile(root, $@"{ApplicationDirectory}\{PayloadFileName}");
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadFileName);
            FileInfo applicationManifest = WriteManifest(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                VstoManifestFileName,
                Path.GetRelativePath(root.FullName, applicationManifest.FullName));

            ClickOnceFileGraph graph = _deploymentResolver.Resolve(deploymentManifest);

            Assert.NotNull(graph.DeploymentManifest);
            Assert.Equal(deploymentManifest.FullName, graph.DeploymentManifest.Source.FullName);
            Assert.Equal(applicationManifest.FullName, graph.ApplicationManifest.Source.FullName);
            Assert.Equal(payload.FullName, Assert.Single(graph.Payloads).Source.FullName);
        }

        [Fact]
        public void DeploymentResolver_WhenFileExtensionsAreMapped_ResolvesApplicationManifestSeparately()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            string applicationManifestPath = $@"{ApplicationDirectory}\{ApplicationManifestFileName}";

            FileInfo mappedPayload = CreateFile(
                root,
                $@"{ApplicationDirectory}\{PayloadFileName}{DeploySuffix}");
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadFileName);
            FileInfo applicationManifest = WriteManifest(
                root,
                applicationManifestPath,
                application);
            FileInfo mappedApplicationManifest = WriteManifest(
                root,
                $"{applicationManifestPath}{DeploySuffix}",
                CreateApplicationManifest());
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                applicationManifestPath,
                mapFileExtensions: true);

            ClickOnceFileGraph graph = _deploymentResolver.Resolve(deploymentManifest);

            Assert.Equal(applicationManifest.FullName, graph.ApplicationManifest.Source.FullName);
            Assert.NotEqual(mappedApplicationManifest.FullName, graph.ApplicationManifest.Source.FullName);
            ClickOnceFileGraphEntry payload = Assert.Single(graph.Payloads);
            Assert.Equal(mappedPayload.FullName, payload.Source.FullName);
            Assert.Equal(DeploySuffix, payload.MappingAddedSuffix);
        }

        [Fact]
        public void DeploymentResolver_WhenOnlyMappedApplicationManifestExists_DoesNotUseIt()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            string applicationManifestPath = $@"{ApplicationDirectory}\{ApplicationManifestFileName}";

            FileInfo mappedApplicationManifest = WriteManifest(
                root,
                $"{applicationManifestPath}{DeploySuffix}",
                CreateApplicationManifest());
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                applicationManifestPath,
                mapFileExtensions: true);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _deploymentResolver.Resolve(deploymentManifest));

            Assert.Contains(
                Path.Combine(root.FullName, applicationManifestPath),
                exception.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain(mappedApplicationManifest.FullName, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void DeploymentResolver_WhenPayloadExistsInBothDirectories_PrefersApplicationDirectory()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string PayloadTargetPath = SharedPayloadFileName;

            FileInfo expectedPayload = CreateFile(root, $@"{ApplicationDirectory}\{PayloadTargetPath}");
            CreateFile(root, PayloadTargetPath);

            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadTargetPath);
            FileInfo applicationManifest = WriteManifest(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                Path.GetRelativePath(root.FullName, applicationManifest.FullName));

            ClickOnceFileGraph graph = _deploymentResolver.Resolve(deploymentManifest);

            Assert.Equal(expectedPayload.FullName, Assert.Single(graph.Payloads).Source.FullName);
        }

        [Fact]
        public void DeploymentResolver_WhenPayloadIsMissingFromApplicationDirectory_UsesDeploymentDirectory()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string PayloadTargetPath = SharedPayloadFileName;

            FileInfo expectedPayload = CreateFile(root, PayloadTargetPath);
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadTargetPath);
            FileInfo applicationManifest = WriteManifest(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                Path.GetRelativePath(root.FullName, applicationManifest.FullName));

            ClickOnceFileGraph graph = _deploymentResolver.Resolve(deploymentManifest);

            Assert.Equal(expectedPayload.FullName, Assert.Single(graph.Payloads).Source.FullName);
            ClickOnceManifestDiagnostic diagnostic = Assert.Single(graph.Diagnostics);
            Assert.Equal(OutputMessageType.Error, diagnostic.Type);
            Assert.Contains(PayloadTargetPath, diagnostic.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void DeploymentResolver_WhenApplicationDirectoryPayloadProbeFails_DoesNotUseDeploymentDirectory()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            string inaccessiblePayloadPath = Path.Combine(
                root.FullName,
                ApplicationDirectory,
                SharedPayloadFileName);

            CreateFile(root, SharedPayloadFileName);
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, SharedPayloadFileName);
            FileInfo applicationManifest = WriteManifest(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                Path.GetRelativePath(root.FullName, applicationManifest.FullName));
            UnauthorizedAccessException expectedException = new();
            ClickOncePayloadFileResolver payloadResolver = new(
                file => file.FullName.Equals(inaccessiblePayloadPath, StringComparison.OrdinalIgnoreCase)
                    ? throw expectedException
                    : file.Exists);
            ClickOnceDeployManifestFileGraphResolver deploymentResolver = new(
                new ClickOnceManifestReader(),
                payloadResolver);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => deploymentResolver.Resolve(deploymentManifest));

            Assert.Same(expectedException, exception.InnerException);
            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceApplicationManifestPayloadProbeFailed,
                    applicationManifest.FullName,
                    SharedPayloadFileName,
                    inaccessiblePayloadPath),
                exception.Message);
            Assert.Contains(
                exception.Diagnostics,
                diagnostic => diagnostic.Text.Contains(SharedPayloadFileName, StringComparison.Ordinal));
        }

        [Fact]
        public void DeploymentResolver_WhenApplicationDirectoryPayloadProbeHasIoFailure_DoesNotUseDeploymentDirectory()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            string failedPayloadPath = Path.Combine(
                root.FullName,
                ApplicationDirectory,
                SharedPayloadFileName);

            CreateFile(root, SharedPayloadFileName);
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, SharedPayloadFileName);
            FileInfo applicationManifest = WriteManifest(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                Path.GetRelativePath(root.FullName, applicationManifest.FullName));
            IOException expectedException = new();
            ClickOncePayloadFileResolver payloadResolver = new(
                file => file.FullName.Equals(failedPayloadPath, StringComparison.OrdinalIgnoreCase)
                    ? throw expectedException
                    : file.Exists);
            ClickOnceDeployManifestFileGraphResolver deploymentResolver = new(
                new ClickOnceManifestReader(),
                payloadResolver);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => deploymentResolver.Resolve(deploymentManifest));

            Assert.Same(expectedException, exception.InnerException);
            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceApplicationManifestPayloadProbeFailed,
                    applicationManifest.FullName,
                    SharedPayloadFileName,
                    failedPayloadPath),
                exception.Message);
            Assert.Contains(
                exception.Diagnostics,
                diagnostic => diagnostic.Text.Contains(SharedPayloadFileName, StringComparison.Ordinal));
        }

        [Fact]
        public void DeploymentResolver_WhenMappedApplicationDirectoryPayloadProbeFails_DoesNotUseDeploymentDirectory()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            string failedPayloadPath = Path.Combine(
                root.FullName,
                ApplicationDirectory,
                $"{SharedPayloadFileName}{DeploySuffix}");

            CreateFile(root, $"{SharedPayloadFileName}{DeploySuffix}");
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, SharedPayloadFileName);
            FileInfo applicationManifest = WriteManifest(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                Path.GetRelativePath(root.FullName, applicationManifest.FullName),
                mapFileExtensions: true);
            UnauthorizedAccessException expectedException = new();
            ClickOncePayloadFileResolver payloadResolver = new(
                file => file.FullName.Equals(failedPayloadPath, StringComparison.OrdinalIgnoreCase)
                    ? throw expectedException
                    : file.Exists);
            ClickOnceDeployManifestFileGraphResolver deploymentResolver = new(
                new ClickOnceManifestReader(),
                payloadResolver);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => deploymentResolver.Resolve(deploymentManifest));

            Assert.Same(expectedException, exception.InnerException);
            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceApplicationManifestPayloadProbeFailed,
                    applicationManifest.FullName,
                    SharedPayloadFileName,
                    failedPayloadPath),
                exception.Message);
            Assert.Contains(
                exception.Diagnostics,
                diagnostic => diagnostic.Text.Contains(SharedPayloadFileName, StringComparison.Ordinal));
        }

        [Fact]
        public void DeploymentResolver_WhenFileExtensionsAreMapped_RecordsMappingAddedSuffix()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string PayloadTargetPath = PayloadFileName;

            FileInfo expectedPayload = CreateFile(root, $@"{ApplicationDirectory}\{PayloadTargetPath}{DeploySuffix}");
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadTargetPath);
            FileInfo applicationManifest = WriteManifest(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                Path.GetRelativePath(root.FullName, applicationManifest.FullName),
                mapFileExtensions: true);

            ClickOnceFileGraph graph = _deploymentResolver.Resolve(deploymentManifest);

            ClickOnceFileGraphEntry payload = Assert.Single(graph.Payloads);
            Assert.Equal(expectedPayload.FullName, payload.Source.FullName);
            Assert.Equal(PayloadTargetPath, payload.TargetPath);
            Assert.Equal(DeploySuffix, payload.MappingAddedSuffix);
            Assert.Equal($"{PayloadTargetPath}{DeploySuffix}", payload.Source.Name);
            Assert.Equal(FileContents, File.ReadAllText(expectedPayload.FullName));
        }

        [Fact]
        public void DeploymentResolver_WhenMappedPayloadExistsInBothDirectories_PrefersApplicationDirectory()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string PayloadTargetPath = SharedPayloadFileName;

            FileInfo expectedPayload = CreateFile(
                root,
                $@"{ApplicationDirectory}\{PayloadTargetPath}{DeploySuffix}");
            CreateFile(root, $"{PayloadTargetPath}{DeploySuffix}");
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadTargetPath);
            FileInfo applicationManifest = WriteManifest(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                Path.GetRelativePath(root.FullName, applicationManifest.FullName),
                mapFileExtensions: true);

            ClickOnceFileGraph graph = _deploymentResolver.Resolve(deploymentManifest);

            Assert.Equal(expectedPayload.FullName, Assert.Single(graph.Payloads).Source.FullName);
        }

        [Fact]
        public void DeploymentResolver_WhenFileExtensionsAreMapped_DoesNotUseExactTarget()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string PayloadTargetPath = PayloadFileName;

            CreateFile(root, $@"{ApplicationDirectory}\{PayloadTargetPath}");
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadTargetPath);
            FileInfo applicationManifest = WriteManifest(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                Path.GetRelativePath(root.FullName, applicationManifest.FullName),
                mapFileExtensions: true);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _deploymentResolver.Resolve(deploymentManifest));

            Assert.Contains(PayloadTargetPath, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void DeploymentResolver_WhenFileExtensionsAreNotMapped_DoesNotUseMappedTarget()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string PayloadTargetPath = PayloadFileName;

            CreateFile(root, $@"{ApplicationDirectory}\{PayloadTargetPath}{DeploySuffix}");
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadTargetPath);
            FileInfo applicationManifest = WriteManifest(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                Path.GetRelativePath(root.FullName, applicationManifest.FullName));

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _deploymentResolver.Resolve(deploymentManifest));

            Assert.Contains(PayloadTargetPath, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void DeploymentResolver_WhenMappedTargetAlreadyEndsInDeploy_AddsExactlyOneSuffix()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string PayloadTargetPath = "payload.deploy";

            FileInfo expectedPayload = CreateFile(
                root,
                $@"{ApplicationDirectory}\{PayloadTargetPath}{DeploySuffix}");
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadTargetPath);
            FileInfo applicationManifest = WriteManifest(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                Path.GetRelativePath(root.FullName, applicationManifest.FullName),
                mapFileExtensions: true);

            ClickOnceFileGraph graph = _deploymentResolver.Resolve(deploymentManifest);

            ClickOnceFileGraphEntry payload = Assert.Single(graph.Payloads);
            Assert.Equal(expectedPayload.FullName, payload.Source.FullName);
            Assert.Equal(DeploySuffix, payload.MappingAddedSuffix);
        }

        [Fact]
        public void DeploymentResolver_ClassifiesReferencedAndAdjacentExecutables()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string NestedLauncherPath = $@"nested\{LauncherFileName}";
            const string NestedSetupPath = @"nested\setup.exe";

            FileInfo referencedLauncher = CreateFile(root, $@"{ApplicationDirectory}\{LauncherFileName}");
            FileInfo adjacentLauncher = CreateFile(root, LauncherFileName);
            FileInfo setup = CreateFile(root, SetupFileName);
            CreateFile(root, NestedLauncherPath);
            CreateFile(root, NestedSetupPath);

            ApplicationManifest application = CreateApplicationManifest();
            AddAssemblyReference(application, LauncherFileName, isEntryPoint: true);
            FileInfo applicationManifest = WriteManifest(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                Path.GetRelativePath(root.FullName, applicationManifest.FullName));

            ClickOnceFileGraph graph = _deploymentResolver.Resolve(deploymentManifest);

            ClickOnceFileGraphEntry payload = Assert.Single(graph.Payloads);
            Assert.Equal(referencedLauncher.FullName, payload.Source.FullName);
            Assert.Equal(ClickOnceFileGraphEntryKind.Payload, payload.Kind);

            Assert.Contains(
                graph.AdjacentExecutables,
                entry =>
                    entry.Source.FullName == setup.FullName &&
                    entry.Kind == ClickOnceFileGraphEntryKind.Setup);
            Assert.Contains(
                graph.AdjacentExecutables,
                entry =>
                    entry.Source.FullName == adjacentLauncher.FullName &&
                    entry.Kind == ClickOnceFileGraphEntryKind.Launcher);
            Assert.Equal(expected: 2, actual: graph.AdjacentExecutables.Count);
        }

        [Fact]
        public void DeploymentResolver_WhenRootLauncherIsReferenced_DoesNotClassifyItAsAdjacent()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo launcher = CreateFile(root, LauncherFileName);
            ApplicationManifest application = CreateApplicationManifest();
            AddAssemblyReference(application, LauncherFileName, isEntryPoint: true);
            FileInfo applicationManifest = WriteManifest(
                root,
                ApplicationManifestFileName,
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                applicationManifest.Name);
            List<string> adjacentProbeCandidates = new();
            ClickOnceDeployManifestFileGraphResolver resolver = new(
                new ClickOnceManifestReader(),
                _payloadResolver,
                file =>
                {
                    adjacentProbeCandidates.Add(file.FullName);

                    return ClickOnceFileSystem.IsFile(file);
                });

            ClickOnceFileGraph graph = resolver.Resolve(deploymentManifest);

            Assert.Equal(launcher.FullName, Assert.Single(graph.Payloads).Source.FullName);
            Assert.Empty(graph.AdjacentExecutables);
            Assert.DoesNotContain(
                launcher.FullName,
                adjacentProbeCandidates,
                StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DeploymentResolver_WhenSetupProbeFails_PreservesDiagnosticsAndThrows(
            bool isUnauthorizedAccess)
        {
            AssertAdjacentExecutableProbeFailure(
                SetupFileName,
                Resources.ClickOnceDeploymentManifestSetupProbeFailed,
                isUnauthorizedAccess);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DeploymentResolver_WhenLauncherProbeFails_PreservesDiagnosticsAndThrows(
            bool isUnauthorizedAccess)
        {
            AssertAdjacentExecutableProbeFailure(
                LauncherFileName,
                Resources.ClickOnceDeploymentManifestLauncherProbeFailed,
                isUnauthorizedAccess);
        }

        private void AssertAdjacentExecutableProbeFailure(
            string candidateFileName,
            string failureMessageFormat,
            bool isUnauthorizedAccess)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo deploymentManifestFile = CreateFile(root, DeploymentManifestFileName);
            FileInfo applicationManifestFile = CreateFile(root, ApplicationManifestFileName);
            FileInfo candidate = new(Path.Combine(root.FullName, candidateFileName));
            ClickOnceManifestDiagnostic expectedDiagnostic =
                CreateWarningDiagnostic(WarningOneTargetPath);
            AssemblyReference entryPoint = new()
            {
                ResolvedPath = applicationManifestFile.FullName,
                TargetPath = applicationManifestFile.Name
            };
            IDeployManifest deploymentManifest = Substitute.For<IDeployManifest>();

            ConfigureDeploymentManifestReferences(
                deploymentManifest,
                entryPoint);
            deploymentManifest.Diagnostics.Returns(new[] { expectedDiagnostic });

            ApplicationManifest applicationModel = CreateApplicationManifest();
            IApplicationManifest applicationManifest = Substitute.For<IApplicationManifest>();

            applicationManifest.AssemblyReferences.Returns(applicationModel.AssemblyReferences);
            applicationManifest.EntryPoint.Returns(applicationModel.EntryPoint);
            applicationManifest.FileReferences.Returns(applicationModel.FileReferences);
            applicationManifest.Diagnostics.Returns(
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
            manifestReader.TryReadApplicationManifest(
                    Arg.Any<Stream>(),
                    out Arg.Any<IApplicationManifest?>())
                .Returns(callInfo =>
                {
                    callInfo[1] = applicationManifest;
                    return true;
                });

            Exception expectedException = isUnauthorizedAccess
                ? new UnauthorizedAccessException()
                : new IOException();
            ClickOnceDeployManifestFileGraphResolver resolver = new(
                manifestReader,
                _payloadResolver,
                file =>
                {
                    if (file.FullName == candidate.FullName)
                    {
                        throw expectedException;
                    }

                    return ClickOnceFileSystem.IsFile(file);
                });

            ClickOnceFileGraphResolutionException exception =
                Assert.Throws<ClickOnceFileGraphResolutionException>(
                    () => resolver.Resolve(deploymentManifestFile));

            Assert.Same(expectedException, exception.InnerException);
            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    failureMessageFormat,
                    candidate.FullName,
                    deploymentManifestFile.FullName),
                exception.Message);
            Assert.Same(expectedDiagnostic, Assert.Single(exception.Diagnostics));
        }

        [Theory]
        [InlineData(DeploymentManifestFileName)]
        [InlineData(VstoManifestFileName)]
        public void DeploymentResolver_WhenDeploymentManifestIsWrongType_Throws(string fileName)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo deploymentManifest = WriteManifest(
                root,
                fileName,
                CreateApplicationManifest());

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _deploymentResolver.Resolve(deploymentManifest));

            Assert.Contains(deploymentManifest.FullName, exception.Message, StringComparison.Ordinal);
            const string ExpectedMessage = "not a deployment manifest";

            Assert.Contains(ExpectedMessage, exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(DeploymentManifestFileName)]
        [InlineData(VstoManifestFileName)]
        public void DeploymentResolver_WhenDeploymentManifestIsMalformed_Throws(string fileName)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo deploymentManifest = CreateFile(root, fileName);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _deploymentResolver.Resolve(deploymentManifest));

            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceDeploymentManifestReadFailed,
                    deploymentManifest.FullName),
                exception.Message);
            Assert.IsType<XmlException>(exception.InnerException);
        }

        [Fact]
        public void DeploymentResolver_WhenReferencedApplicationManifestIsWrongType_Throws()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;

            FileInfo wrongTypeManifest = CreateFusionManifest(root, ApplicationManifestFileName);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                wrongTypeManifest.Name);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _deploymentResolver.Resolve(deploymentManifest));

            Assert.Contains(wrongTypeManifest.FullName, exception.Message, StringComparison.Ordinal);
            const string ExpectedMessage = "not an application manifest";

            Assert.Contains(ExpectedMessage, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void DeploymentResolver_WhenReferencedApplicationManifestIsMalformed_Throws()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;

            FileInfo malformedManifest = CreateFile(root, ApplicationManifestFileName);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                malformedManifest.Name);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _deploymentResolver.Resolve(deploymentManifest));

            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceDeploymentManifestReferencedApplicationReadFailed,
                    malformedManifest.FullName,
                    deploymentManifest.FullName),
                exception.Message);
            Assert.IsType<XmlException>(exception.InnerException);
        }

        [Fact]
        public void DeploymentResolver_WhenReferencedApplicationManifestIsMissing_ThrowsWithExpectedPath()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string MissingManifest = ApplicationDirectory + @"\" + ApplicationManifestFileName;

            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                MissingManifest);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _deploymentResolver.Resolve(deploymentManifest));

            Assert.Contains(
                Path.Combine(root.FullName, MissingManifest),
                exception.Message,
                StringComparison.Ordinal);
            Assert.Contains(
                exception.Diagnostics,
                diagnostic => diagnostic.Type == OutputMessageType.Error);
        }

        [Theory]
        [InlineData(ApplicationManifestFileName)]
        [InlineData(@"missing\App.exe.manifest")]
        public void DeploymentResolver_WhenResolvedApplicationManifestIsMissing_ThrowsNotFound(
            string applicationManifestPath)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            string resolvedPath = Path.Combine(root.FullName, applicationManifestPath);
            ClickOnceFileGraphResolutionException exception = ResolveWithApplicationManifestProbe(
                root,
                resolvedPath,
                ClickOnceFileSystem.IsFile);

            Assert.Contains(resolvedPath, exception.Message, StringComparison.Ordinal);
            Assert.Contains("file does not exist", exception.Message, StringComparison.Ordinal);
            Assert.Null(exception.InnerException);
        }

        [Fact]
        public void DeploymentResolver_WhenResolvedApplicationManifestIsDirectory_ThrowsNotFound()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            string resolvedPath = Path.Combine(root.FullName, ApplicationManifestFileName);
            Directory.CreateDirectory(resolvedPath);

            ClickOnceFileGraphResolutionException exception = ResolveWithApplicationManifestProbe(
                root,
                resolvedPath,
                ClickOnceFileSystem.IsFile);

            Assert.Contains(resolvedPath, exception.Message, StringComparison.Ordinal);
            Assert.Contains("file does not exist", exception.Message, StringComparison.Ordinal);
            Assert.Null(exception.InnerException);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DeploymentResolver_WhenApplicationManifestProbeFails_ThrowsReadFailure(
            bool unauthorized)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            string resolvedPath = Path.Combine(root.FullName, ApplicationManifestFileName);
            Exception expectedException = unauthorized
                ? new UnauthorizedAccessException()
                : new IOException();

            ClickOnceFileGraphResolutionException exception = ResolveWithApplicationManifestProbe(
                root,
                resolvedPath,
                _ => throw expectedException);

            Assert.Contains(resolvedPath, exception.Message, StringComparison.Ordinal);
            Assert.Contains("Failed to read application manifest", exception.Message, StringComparison.Ordinal);
            Assert.Same(expectedException, exception.InnerException);
            ClickOnceManifestDiagnostic diagnostic = Assert.Single(exception.Diagnostics);
            Assert.Equal(OutputMessageType.Warning, diagnostic.Type);
            Assert.Contains(WarningOneTargetPath, diagnostic.Text, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(false, null)]
        [InlineData(true, null)]
        [InlineData(true, "")]
        [InlineData(true, " ")]
        public void DeploymentResolver_WhenEntryPointIsMissingOrHasNoTargetPath_Throws(
            bool hasEntryPoint,
            string? targetPath)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            FileInfo deploymentManifestFile = CreateFile(
                temporaryDirectory.Directory,
                DeploymentManifestFileName);
            ClickOnceManifestDiagnostic expectedDiagnostic = CreateWarningDiagnostic(WarningOneTargetPath);
            IDeployManifest deploymentManifest = Substitute.For<IDeployManifest>();
            AssemblyReference? entryPoint = hasEntryPoint
                ? new AssemblyReference() { TargetPath = targetPath }
                : null;

            ConfigureDeploymentManifestReferences(
                deploymentManifest,
                entryPoint);
            deploymentManifest.Diagnostics.Returns(new[] { expectedDiagnostic });

            IClickOnceManifestReader manifestReader = Substitute.For<IClickOnceManifestReader>();

            manifestReader.TryReadDeployManifest(
                    Arg.Any<Stream>(),
                    out Arg.Any<IDeployManifest?>())
                .Returns(callInfo =>
                {
                    callInfo[1] = deploymentManifest;
                    return true;
                });

            ClickOnceDeployManifestFileGraphResolver resolver = new(
                manifestReader,
                _payloadResolver);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => resolver.Resolve(deploymentManifestFile));

            Assert.Contains(deploymentManifestFile.FullName, exception.Message, StringComparison.Ordinal);
            Assert.Contains("does not identify an application manifest", exception.Message, StringComparison.Ordinal);
            ClickOnceManifestDiagnostic diagnostic = Assert.Single(exception.Diagnostics);
            Assert.Equal(OutputMessageType.Warning, diagnostic.Type);
            Assert.Contains(WarningOneTargetPath, diagnostic.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void DeploymentResolver_WhenEntryPointIsUnresolved_DoesNotUseTargetPathAsFallback()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;

            FileInfo deploymentManifestFile = CreateFile(root, DeploymentManifestFileName);
            FileInfo applicationManifestFile = CreateFile(root, ApplicationManifestFileName);
            AssemblyReference entryPoint = new()
            {
                TargetPath = applicationManifestFile.Name
            };
            IDeployManifest deploymentManifest =
                Substitute.For<IDeployManifest>();

            ConfigureDeploymentManifestReferences(
                deploymentManifest,
                entryPoint);
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

            ClickOnceDeployManifestFileGraphResolver resolver = new(
                manifestReader,
                _payloadResolver);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => resolver.Resolve(deploymentManifestFile));

            Assert.Contains(applicationManifestFile.FullName, exception.Message, StringComparison.Ordinal);
            deploymentManifest.Received(1).ResolveFiles(
                Arg.Is<IReadOnlyList<DirectoryInfo>>(directories =>
                    directories != null &&
                    directories
                        .Select(directory => directory.FullName)
                        .SequenceEqual(
                            new[] { root.FullName },
                            StringComparer.OrdinalIgnoreCase)));
        }

        [Fact]
        public void DeploymentResolver_WhenRequiredPayloadIsMissing_ThrowsWithTargetPath()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string PayloadTargetPath = @"missing\payload.dll";

            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadTargetPath);
            FileInfo applicationManifest = WriteManifest(
                root,
                ApplicationManifestFileName,
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                applicationManifest.Name);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _deploymentResolver.Resolve(deploymentManifest));

            Assert.Contains(PayloadTargetPath, exception.Message, StringComparison.Ordinal);
            Assert.Contains(
                exception.Diagnostics,
                diagnostic =>
                    diagnostic.Type == OutputMessageType.Error &&
                    diagnostic.Text.Contains(PayloadTargetPath, StringComparison.Ordinal));
        }

        [Fact]
        public void DeploymentResolver_WhenApplicationResolutionProducesWarning_PreservesDiagnosticAndSucceeds()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;

            FileInfo deploymentManifestFile = CreateFile(root, DeploymentManifestFileName);
            FileInfo applicationManifestFile = CreateFile(root, ApplicationManifestFileName);
            FileInfo payload = CreateFile(root, PayloadFileName);

            AssemblyReference deploymentEntryPoint = new()
            {
                ResolvedPath = applicationManifestFile.FullName,
                TargetPath = applicationManifestFile.Name
            };
            IDeployManifest deploymentManifest =
                Substitute.For<IDeployManifest>();

            ConfigureDeploymentManifestReferences(
                deploymentManifest,
                deploymentEntryPoint);
            deploymentManifest.MapFileExtensions.Returns(false);
            deploymentManifest.Diagnostics.Returns(
                Array.Empty<ClickOnceManifestDiagnostic>());

            ApplicationManifest applicationModel = CreateApplicationManifest();
            AddFileReference(applicationModel, payload.Name);
            List<ClickOnceManifestDiagnostic> applicationDiagnostics = new();
            IApplicationManifest applicationManifest =
                Substitute.For<IApplicationManifest>();

            applicationManifest.AssemblyReferences.Returns(
                applicationModel.AssemblyReferences);
            applicationManifest.EntryPoint.Returns(
                applicationModel.EntryPoint);
            applicationManifest.FileReferences.Returns(
                applicationModel.FileReferences);
            applicationManifest.Diagnostics.Returns(applicationDiagnostics);
            applicationManifest
                .When(manifest => manifest.ResolveFiles(
                    Arg.Any<IReadOnlyList<DirectoryInfo>>()))
                .Do(_ =>
                {
                    applicationDiagnostics.Add(CreateWarningDiagnostic(WarningOneTargetPath));
                    applicationDiagnostics.Add(CreateWarningDiagnostic(WarningTwoTargetPath));
                });

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
            manifestReader.TryReadApplicationManifest(
                    Arg.Any<Stream>(),
                    out Arg.Any<IApplicationManifest?>())
                .Returns(callInfo =>
                {
                    callInfo[1] = applicationManifest;
                    return true;
                });

            ClickOnceDeployManifestFileGraphResolver resolver = new(
                manifestReader,
                _payloadResolver);

            ClickOnceFileGraph graph = resolver.Resolve(deploymentManifestFile);

            Assert.Equal(payload.FullName, Assert.Single(graph.Payloads).Source.FullName);
            Assert.Collection(
                graph.Diagnostics,
                diagnostic =>
                {
                    Assert.Equal(OutputMessageType.Warning, diagnostic.Type);
                    Assert.Contains(WarningOneTargetPath, diagnostic.Text, StringComparison.Ordinal);
                },
                diagnostic =>
                {
                    Assert.Equal(OutputMessageType.Warning, diagnostic.Type);
                    Assert.Contains(WarningTwoTargetPath, diagnostic.Text, StringComparison.Ordinal);
                });
        }

        [Fact]
        public void DeploymentResolver_WhenApplicationResolutionRetries_PreservesEachDiagnosticOnceInOrder()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;

            FileInfo deploymentManifestFile = CreateFile(root, DeploymentManifestFileName);
            FileInfo applicationManifestFile = CreateFile(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}");
            FileInfo payload = CreateFile(root, PayloadFileName);

            AssemblyReference deploymentEntryPoint = new()
            {
                ResolvedPath = applicationManifestFile.FullName,
                TargetPath = Path.GetRelativePath(root.FullName, applicationManifestFile.FullName)
            };
            IDeployManifest deploymentManifest = Substitute.For<IDeployManifest>();

            ConfigureDeploymentManifestReferences(
                deploymentManifest,
                deploymentEntryPoint);
            deploymentManifest.MapFileExtensions.Returns(false);
            deploymentManifest.Diagnostics.Returns(
                Array.Empty<ClickOnceManifestDiagnostic>());

            ApplicationManifest applicationModel = CreateApplicationManifest();
            AddFileReference(applicationModel, payload.Name);
            List<ClickOnceManifestDiagnostic> applicationDiagnostics = new();
            IApplicationManifest applicationManifest = Substitute.For<IApplicationManifest>();
            int resolutionAttempt = 0;

            applicationManifest.AssemblyReferences.Returns(applicationModel.AssemblyReferences);
            applicationManifest.EntryPoint.Returns(applicationModel.EntryPoint);
            applicationManifest.FileReferences.Returns(applicationModel.FileReferences);
            applicationManifest.Diagnostics.Returns(applicationDiagnostics);
            applicationManifest
                .When(manifest => manifest.ResolveFiles(
                    Arg.Any<IReadOnlyList<DirectoryInfo>>()))
                .Do(_ =>
                {
                    ++resolutionAttempt;
                    applicationDiagnostics.Add(
                        CreateWarningDiagnostic(
                            resolutionAttempt == 1
                                ? WarningOneTargetPath
                                : WarningTwoTargetPath));
                });

            IClickOnceManifestReader manifestReader = Substitute.For<IClickOnceManifestReader>();

            manifestReader.TryReadDeployManifest(
                    Arg.Any<Stream>(),
                    out Arg.Any<IDeployManifest?>())
                .Returns(callInfo =>
                {
                    callInfo[1] = deploymentManifest;
                    return true;
                });
            manifestReader.TryReadApplicationManifest(
                    Arg.Any<Stream>(),
                    out Arg.Any<IApplicationManifest?>())
                .Returns(callInfo =>
                {
                    callInfo[1] = applicationManifest;
                    return true;
                });

            ClickOnceDeployManifestFileGraphResolver resolver = new(
                manifestReader,
                _payloadResolver);

            ClickOnceFileGraph graph = resolver.Resolve(deploymentManifestFile);

            Assert.Equal(expected: 2, actual: resolutionAttempt);
            Assert.Equal(payload.FullName, Assert.Single(graph.Payloads).Source.FullName);
            Assert.Collection(
                graph.Diagnostics,
                diagnostic =>
                {
                    Assert.Equal(OutputMessageType.Warning, diagnostic.Type);
                    Assert.Contains(WarningOneTargetPath, diagnostic.Text, StringComparison.Ordinal);
                },
                diagnostic =>
                {
                    Assert.Equal(OutputMessageType.Warning, diagnostic.Type);
                    Assert.Contains(WarningTwoTargetPath, diagnostic.Text, StringComparison.Ordinal);
                });
        }

        [Fact]
        public void ApplicationResolver_WhenTargetExists_UsesTargetWithoutMappingSuffix()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string PayloadTargetPath = @"content\payload.dll";

            FileInfo expectedPayload = CreateFile(root, PayloadTargetPath);
            CreateFile(root, $"{PayloadTargetPath}{DeploySuffix}");
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadTargetPath);
            FileInfo applicationManifest = WriteManifest(
                root,
                ApplicationManifestFileName,
                application);

            Assert.True(_applicationResolver.TryResolve(applicationManifest, out ClickOnceFileGraph? graph));

            Assert.NotNull(graph);
            ClickOnceFileGraphEntry payload = Assert.Single(graph.Payloads);
            Assert.Equal(expectedPayload.FullName, payload.Source.FullName);
            Assert.Null(payload.MappingAddedSuffix);
        }

        [Fact]
        public void ApplicationResolver_WhenOnlyMappedTargetExists_RecordsOneMappingAddedSuffix()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string PayloadTargetPath = "payload.deploy";

            FileInfo expectedPayload = CreateFile(root, $"{PayloadTargetPath}{DeploySuffix}");
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadTargetPath);
            FileInfo applicationManifest = WriteManifest(
                root,
                ApplicationManifestFileName,
                application);

            Assert.True(_applicationResolver.TryResolve(applicationManifest, out ClickOnceFileGraph? graph));

            Assert.NotNull(graph);
            ClickOnceFileGraphEntry payload = Assert.Single(graph.Payloads);
            Assert.Equal(expectedPayload.FullName, payload.Source.FullName);
            Assert.Equal(PayloadTargetPath, payload.TargetPath);
            Assert.Equal(DeploySuffix, payload.MappingAddedSuffix);
        }

        [Fact]
        public void ApplicationResolver_WhenExactPayloadProbeFails_DoesNotUseMappedTarget()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            string inaccessiblePayloadPath = Path.Combine(root.FullName, PayloadFileName);

            CreateFile(root, $"{PayloadFileName}{DeploySuffix}");
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadFileName);
            FileInfo applicationManifest = WriteManifest(
                root,
                ApplicationManifestFileName,
                application);
            UnauthorizedAccessException expectedException = new();
            ClickOncePayloadFileResolver payloadResolver = new(
                file => file.FullName.Equals(inaccessiblePayloadPath, StringComparison.OrdinalIgnoreCase)
                    ? throw expectedException
                    : file.Exists);
            ClickOnceApplicationManifestFileGraphResolver applicationResolver = new(
                new ClickOnceManifestReader(),
                payloadResolver);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => applicationResolver.TryResolve(applicationManifest, out _));

            Assert.Same(expectedException, exception.InnerException);
            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceApplicationManifestPayloadProbeFailed,
                    applicationManifest.FullName,
                    PayloadFileName,
                    inaccessiblePayloadPath),
                exception.Message);
            Assert.Contains(
                exception.Diagnostics,
                diagnostic => diagnostic.Text.Contains(PayloadFileName, StringComparison.Ordinal));
        }

        [Fact]
        public void ApplicationResolver_WhenDirectoryOccupiesExactPayloadPath_UsesMappedTarget()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;

            Directory.CreateDirectory(Path.Combine(root.FullName, PayloadFileName));
            FileInfo expectedPayload = CreateFile(root, $"{PayloadFileName}{DeploySuffix}");
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadFileName);
            FileInfo applicationManifest = WriteManifest(
                root,
                ApplicationManifestFileName,
                application);

            Assert.True(_applicationResolver.TryResolve(applicationManifest, out ClickOnceFileGraph? graph));

            Assert.NotNull(graph);
            ClickOnceFileGraphEntry payload = Assert.Single(graph.Payloads);
            Assert.Equal(expectedPayload.FullName, payload.Source.FullName);
            Assert.Equal(DeploySuffix, payload.MappingAddedSuffix);
        }

        [Fact]
        public void ApplicationResolver_WhenOnlyDoubleMappedTargetExists_DoesNotInventAdditionalSuffix()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string DoubleMappedPayloadPath = $"{PayloadFileName}{DeploySuffix}{DeploySuffix}";

            CreateFile(root, DoubleMappedPayloadPath);
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadFileName);
            FileInfo applicationManifest = WriteManifest(
                root,
                ApplicationManifestFileName,
                application);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _applicationResolver.TryResolve(applicationManifest, out _));

            Assert.Contains(PayloadFileName, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ApplicationResolver_DoesNotUseDeploymentDirectoryFallback()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string PayloadTargetPath = "shared.dll";

            CreateFile(root, PayloadTargetPath);
            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadTargetPath);
            FileInfo applicationManifest = WriteManifest(
                root,
                $@"{ApplicationDirectory}\{ApplicationManifestFileName}",
                application);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _applicationResolver.TryResolve(applicationManifest, out _));

            Assert.Contains(PayloadTargetPath, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ApplicationResolver_WhenManifestIsNotClickOnce_ReturnsFalse()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            FileInfo fusionManifest = CreateFusionManifest(
                temporaryDirectory.Directory,
                DependencyManifestFileName);

            bool result = _applicationResolver.TryResolve(
                fusionManifest,
                out ClickOnceFileGraph? graph);

            Assert.False(result);
            Assert.Null(graph);
        }

        [Fact]
        public void ApplicationResolver_WhenOptionalPayloadIsMissing_ThrowsWithDiagnostic()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string PayloadTargetPath = OptionalPayloadFileName;

            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadTargetPath, isOptional: true);
            FileInfo applicationManifest = WriteManifest(
                root,
                ApplicationManifestFileName,
                application);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _applicationResolver.TryResolve(applicationManifest, out _));

            Assert.Contains(PayloadTargetPath, exception.Message, StringComparison.Ordinal);
            Assert.Contains(
                exception.Diagnostics,
                diagnostic =>
                    diagnostic.Type == OutputMessageType.Error &&
                    diagnostic.Text.Contains(PayloadTargetPath, StringComparison.Ordinal));
        }

        [Fact]
        public void DeploymentResolver_WhenOptionalPayloadHasNoTargetPath_Throws()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;

            ApplicationManifest application = CreateApplicationManifest();
            application.FileReferences.Add(new FileReference() { IsOptional = true });
            FileInfo applicationManifest = WriteManifest(
                root,
                ApplicationManifestFileName,
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                applicationManifest.Name);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _deploymentResolver.Resolve(deploymentManifest));

            Assert.Contains(MissingTargetPathMessage, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void DeploymentResolver_WhenOptionalPayloadIsMissing_ThrowsWithDiagnostic()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            const string PayloadTargetPath = OptionalPayloadFileName;

            ApplicationManifest application = CreateApplicationManifest();
            AddFileReference(application, PayloadTargetPath, isOptional: true);
            FileInfo applicationManifest = WriteManifest(
                root,
                ApplicationManifestFileName,
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                applicationManifest.Name);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _deploymentResolver.Resolve(deploymentManifest));

            Assert.Contains(PayloadTargetPath, exception.Message, StringComparison.Ordinal);
            Assert.Contains(
                exception.Diagnostics,
                diagnostic =>
                    diagnostic.Type == OutputMessageType.Error &&
                    diagnostic.Text.Contains(PayloadTargetPath, StringComparison.Ordinal));
        }

        [Fact]
        public void DeploymentResolver_WhenRequiredPayloadHasNoTargetPath_Throws()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;

            ApplicationManifest application = CreateApplicationManifest();
            application.FileReferences.Add(new FileReference());
            FileInfo applicationManifest = WriteManifest(
                root,
                ApplicationManifestFileName,
                application);
            FileInfo deploymentManifest = WriteDeploymentManifest(
                root,
                DeploymentManifestFileName,
                applicationManifest.Name);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _deploymentResolver.Resolve(deploymentManifest));

            Assert.Contains(MissingTargetPathMessage, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void PayloadResolver_WhenOptionalReferenceHasNoTargetPath_Throws()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            FileInfo applicationManifestFile = CreateFile(
                temporaryDirectory.Directory,
                ApplicationManifestFileName);
            ApplicationManifest applicationManifest = CreateApplicationManifest();
            applicationManifest.FileReferences.Add(new FileReference() { IsOptional = true });

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _payloadResolver.ResolveForExplicitApplication(
                    applicationManifestFile,
                    new ApplicationManifestAdapter(applicationManifest),
                    new List<ClickOnceManifestDiagnostic>()));

            Assert.Contains(MissingTargetPathMessage, exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PayloadResolver_WhenOptionalPhysicalReferenceProbeFails_Throws(bool isAssembly)
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            FileInfo applicationManifestFile = CreateFile(
                temporaryDirectory.Directory,
                ApplicationManifestFileName);
            ApplicationManifest applicationManifest = CreateApplicationManifest();

            if (isAssembly)
            {
                AddAssemblyReference(applicationManifest, OptionalPayloadFileName, isOptional: true);
            }
            else
            {
                AddFileReference(applicationManifest, OptionalPayloadFileName, isOptional: true);
            }

            UnauthorizedAccessException expectedException = new();
            ClickOncePayloadFileResolver payloadResolver = new(_ => throw expectedException);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => payloadResolver.ResolveForExplicitApplication(
                    applicationManifestFile,
                    new ApplicationManifestAdapter(applicationManifest),
                    new List<ClickOnceManifestDiagnostic>()));

            Assert.Same(expectedException, exception.InnerException);
            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceApplicationManifestPayloadProbeFailed,
                    applicationManifestFile.FullName,
                    OptionalPayloadFileName,
                    Path.Combine(applicationManifestFile.DirectoryName!, OptionalPayloadFileName)),
                exception.Message);
        }

        [Fact]
        public void PayloadResolver_WhenRequiredFileProbeFails_PreservesExistingDiagnostics()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            FileInfo applicationManifestFile = CreateFile(
                temporaryDirectory.Directory,
                ApplicationManifestFileName);
            ApplicationManifest applicationManifest = CreateApplicationManifest();
            AddFileReference(applicationManifest, WarningOneTargetPath);
            applicationManifest.ResolveFiles(
                new[] { temporaryDirectory.Directory.FullName });
            ClickOnceManifestDiagnostic existingDiagnostic = Assert.Single(
                new ApplicationManifestAdapter(applicationManifest).Diagnostics);
            applicationManifest.FileReferences.Clear();
            AddFileReference(applicationManifest, PayloadFileName);
            IOException expectedException = new();
            ClickOncePayloadFileResolver payloadResolver = new(_ => throw expectedException);
            List<ClickOnceManifestDiagnostic> diagnostics = new();

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => payloadResolver.ResolveForExplicitApplication(
                    applicationManifestFile,
                    new ApplicationManifestAdapter(applicationManifest),
                    diagnostics));

            Assert.Same(expectedException, exception.InnerException);
            Assert.Equal(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceApplicationManifestPayloadProbeFailed,
                    applicationManifestFile.FullName,
                    PayloadFileName,
                    Path.Combine(applicationManifestFile.DirectoryName!, PayloadFileName)),
                exception.Message);
            Assert.Contains(
                exception.Diagnostics,
                diagnostic =>
                    diagnostic.Name == existingDiagnostic.Name &&
                    diagnostic.Text == existingDiagnostic.Text &&
                    diagnostic.Type == existingDiagnostic.Type);
        }

        [Fact]
        public void PayloadResolver_WhenRequiredFileProbeHasPathTooLongFailure_ReportsInvalidTargetPath()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            FileInfo applicationManifestFile = CreateFile(
                temporaryDirectory.Directory,
                ApplicationManifestFileName);
            ApplicationManifest applicationManifest = CreateApplicationManifest();
            AddFileReference(applicationManifest, PayloadFileName);
            PathTooLongException expectedException = new();
            ClickOncePayloadFileResolver payloadResolver = new(_ => throw expectedException);

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => payloadResolver.ResolveForExplicitApplication(
                    applicationManifestFile,
                    new ApplicationManifestAdapter(applicationManifest),
                    new List<ClickOnceManifestDiagnostic>()));

            Assert.Same(expectedException, exception.InnerException);
            Assert.Contains(applicationManifestFile.FullName, exception.Message, StringComparison.Ordinal);
            Assert.Contains(PayloadFileName, exception.Message, StringComparison.Ordinal);
            Assert.Contains("invalid target path", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void PayloadResolver_IncludesOnlyPhysicalReferencesAndPreservesReferenceIdentity()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo applicationManifestFile = CreateFile(root, ApplicationManifestFileName);
            ApplicationManifest applicationManifest = CreateApplicationManifest();

            const string AssemblyTargetPath = "library.dll";
            const string ContentTargetPath = "content.txt";
            const string EntryPointTargetPath = "entry.exe";
            const string PrerequisiteTargetPath = "prerequisite.dll";
            const string VirtualTargetPath = "virtual.dll";

            AssemblyReference assemblyReference = new() { TargetPath = AssemblyTargetPath };
            AssemblyReference prerequisiteReference = new()
            {
                IsPrerequisite = true,
                TargetPath = PrerequisiteTargetPath
            };
            AssemblyReference virtualReference = new()
            {
                AssemblyIdentity = new AssemblyIdentity(ClrPlatformAssemblyName, ManifestVersion),
                TargetPath = VirtualTargetPath
            };
            AssemblyReference entryPoint = new() { TargetPath = EntryPointTargetPath };
            FileReference fileReference = new() { TargetPath = ContentTargetPath };

            applicationManifest.AssemblyReferences.Add(assemblyReference);
            applicationManifest.AssemblyReferences.Add(prerequisiteReference);
            applicationManifest.AssemblyReferences.Add(virtualReference);
            applicationManifest.EntryPoint = entryPoint;
            applicationManifest.FileReferences.Add(fileReference);

            CreateFile(root, assemblyReference.TargetPath);
            CreateFile(root, prerequisiteReference.TargetPath);
            CreateFile(root, virtualReference.TargetPath);
            CreateFile(root, entryPoint.TargetPath);
            CreateFile(root, fileReference.TargetPath);

            IReadOnlyList<ClickOnceFileGraphEntry> payloads = _payloadResolver.ResolveForExplicitApplication(
                applicationManifestFile,
                new ApplicationManifestAdapter(applicationManifest),
                new List<ClickOnceManifestDiagnostic>());

            Assert.Equal(expected: 3, actual: payloads.Count);
            Assert.Contains(payloads, payload => ReferenceEquals(payload.ManifestReference, assemblyReference));
            Assert.Contains(payloads, payload => ReferenceEquals(payload.ManifestReference, entryPoint));
            Assert.Contains(payloads, payload => ReferenceEquals(payload.ManifestReference, fileReference));
            Assert.DoesNotContain(payloads, payload => ReferenceEquals(payload.ManifestReference, prerequisiteReference));
            Assert.DoesNotContain(payloads, payload => ReferenceEquals(payload.ManifestReference, virtualReference));
            Assert.Null(prerequisiteReference.ResolvedPath);
            Assert.Null(virtualReference.ResolvedPath);
        }

        [Fact]
        public void PayloadResolver_WhenSourcePathCompetesWithTargetPath_UsesTargetPath()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo applicationManifestFile = CreateFile(root, ApplicationManifestFileName);
            FileInfo expectedPayload = CreateFile(root, PayloadFileName);
            FileInfo sourcePathPayload = CreateFile(root, "source-path.dll");
            ApplicationManifest applicationManifest = CreateApplicationManifest();
            AssemblyReference reference = new()
            {
                SourcePath = sourcePathPayload.Name,
                TargetPath = expectedPayload.Name
            };
            applicationManifest.AssemblyReferences.Add(reference);

            IReadOnlyList<ClickOnceFileGraphEntry> payloads = _payloadResolver.ResolveForExplicitApplication(
                applicationManifestFile,
                new ApplicationManifestAdapter(applicationManifest),
                new List<ClickOnceManifestDiagnostic>());

            ClickOnceFileGraphEntry payload = Assert.Single(payloads);
            Assert.Equal(expectedPayload.FullName, payload.Source.FullName);
            Assert.Equal(expectedPayload.FullName, reference.ResolvedPath);
            Assert.Equal(sourcePathPayload.Name, reference.SourcePath);
            Assert.Same(reference, payload.ManifestReference);
        }

        [Fact]
        public void PayloadResolver_WhenAssemblyIdentityCompetesWithTargetPath_UsesTargetPath()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo applicationManifestFile = CreateFile(root, ApplicationManifestFileName);
            FileInfo expectedPayload = CreateFile(root, PayloadFileName);
            FileInfo identityPayload = CreateFusionManifest(root, "SideBySide.manifest");

            ApplicationManifest applicationManifest = CreateApplicationManifest();
            AssemblyIdentity identity = AssemblyIdentity.FromManifest(identityPayload.FullName);
            AssemblyReference reference = new()
            {
                AssemblyIdentity = identity,
                TargetPath = expectedPayload.Name
            };
            applicationManifest.AssemblyReferences.Add(reference);
            applicationManifest.ResolveFiles(new[] { root.FullName });
            Assert.Equal(identityPayload.FullName, reference.ResolvedPath);

            IReadOnlyList<ClickOnceFileGraphEntry> payloads = _payloadResolver.ResolveForExplicitApplication(
                applicationManifestFile,
                new ApplicationManifestAdapter(applicationManifest),
                new List<ClickOnceManifestDiagnostic>());

            ClickOnceFileGraphEntry payload = Assert.Single(payloads);
            Assert.Equal(expectedPayload.FullName, payload.Source.FullName);
            Assert.Equal(expectedPayload.FullName, reference.ResolvedPath);
            Assert.Same(identity, reference.AssemblyIdentity);
            Assert.Same(reference, payload.ManifestReference);
        }

        [Fact]
        public void PayloadResolver_DuringDiagnosticResolution_SuppressesAndRestoresCompetingHints()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo applicationManifestFile = CreateFile(root, ApplicationManifestFileName);
            FileInfo expectedPayload = CreateFile(root, PayloadFileName);
            const string SourcePath = "source-path.dll";
            AssemblyIdentity identity = new("SideBySide", ManifestVersion);
            AssemblyReference reference = new()
            {
                AssemblyIdentity = identity,
                SourcePath = SourcePath,
                TargetPath = expectedPayload.Name
            };
            ApplicationManifest applicationModel = CreateApplicationManifest();
            applicationModel.AssemblyReferences.Add(reference);
            IApplicationManifest applicationManifest = Substitute.For<IApplicationManifest>();

            applicationManifest.AssemblyReferences.Returns(applicationModel.AssemblyReferences);
            applicationManifest.EntryPoint.Returns(applicationModel.EntryPoint);
            applicationManifest.FileReferences.Returns(applicationModel.FileReferences);
            applicationManifest.Diagnostics.Returns(
                Array.Empty<ClickOnceManifestDiagnostic>());
            applicationManifest
                .When(manifest => manifest.ResolveFiles(
                    Arg.Any<IReadOnlyList<DirectoryInfo>>()))
                .Do(_ =>
                {
                    Assert.Null(reference.SourcePath);
                    Assert.Null(reference.AssemblyIdentity);
                    reference.ResolvedPath = "diagnostic-path.dll";
                });

            IReadOnlyList<ClickOnceFileGraphEntry> payloads = _payloadResolver.ResolveForExplicitApplication(
                applicationManifestFile,
                applicationManifest,
                new List<ClickOnceManifestDiagnostic>());

            Assert.Equal(expectedPayload.FullName, Assert.Single(payloads).Source.FullName);
            Assert.Equal(SourcePath, reference.SourcePath);
            Assert.Same(identity, reference.AssemblyIdentity);
            Assert.Equal(expectedPayload.FullName, reference.ResolvedPath);
        }

        [Fact]
        public void PayloadResolver_WhenResolvedPathCompetesWithTargetPath_UsesTargetPath()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;
            FileInfo applicationManifestFile = CreateFile(root, ApplicationManifestFileName);
            FileInfo expectedPayload = CreateFile(root, PayloadFileName);
            FileInfo resolvedPathPayload = CreateFile(root, "resolved-path.dll");
            ApplicationManifest applicationModel = CreateApplicationManifest();
            AssemblyReference reference = new()
            {
                ResolvedPath = resolvedPathPayload.FullName,
                TargetPath = expectedPayload.Name
            };
            applicationModel.AssemblyReferences.Add(reference);
            IApplicationManifest applicationManifest = Substitute.For<IApplicationManifest>();
            applicationManifest.AssemblyReferences.Returns(applicationModel.AssemblyReferences);
            applicationManifest.EntryPoint.Returns(applicationModel.EntryPoint);
            applicationManifest.FileReferences.Returns(applicationModel.FileReferences);
            applicationManifest.Diagnostics.Returns(
                Array.Empty<ClickOnceManifestDiagnostic>());

            IReadOnlyList<ClickOnceFileGraphEntry> payloads = _payloadResolver.ResolveForExplicitApplication(
                applicationManifestFile,
                applicationManifest,
                new List<ClickOnceManifestDiagnostic>());

            ClickOnceFileGraphEntry payload = Assert.Single(payloads);
            Assert.Equal(expectedPayload.FullName, payload.Source.FullName);
            Assert.Equal(expectedPayload.FullName, reference.ResolvedPath);
            Assert.Same(reference, payload.ManifestReference);
        }

        [Fact]
        public void PayloadResolver_WhenTargetPathIsInvalid_ThrowsLocalizedResolutionException()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            FileInfo applicationManifestFile = CreateFile(
                temporaryDirectory.Directory,
                ApplicationManifestFileName);
            ApplicationManifest applicationManifest = CreateApplicationManifest();
            const string InvalidTargetPath = "invalid\0path";

            applicationManifest.FileReferences.Add(new FileReference() { TargetPath = InvalidTargetPath });

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _payloadResolver.ResolveForExplicitApplication(
                    applicationManifestFile,
                    new ApplicationManifestAdapter(applicationManifest),
                    new List<ClickOnceManifestDiagnostic>()));

            const string ExpectedMessage = "invalid target path";

            Assert.Contains(ExpectedMessage, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void PayloadResolver_WhenRequiredReferenceHasNoTargetPath_Throws()
        {
            using TemporaryDirectory temporaryDirectory = new(_directoryService);
            DirectoryInfo root = temporaryDirectory.Directory;

            FileInfo applicationManifestFile = CreateFile(root, ApplicationManifestFileName);
            ApplicationManifest applicationManifest = CreateApplicationManifest();
            applicationManifest.FileReferences.Add(new FileReference());

            ClickOnceFileGraphResolutionException exception = Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => _payloadResolver.ResolveForExplicitApplication(
                    applicationManifestFile,
                    new ApplicationManifestAdapter(applicationManifest),
                    new List<ClickOnceManifestDiagnostic>()));

            Assert.Contains(MissingTargetPathMessage, exception.Message, StringComparison.Ordinal);
        }

        private static ApplicationManifest CreateApplicationManifest()
        {
            ApplicationManifest manifest = new();
            manifest.AssemblyIdentity.Name = "TestApplication";
            manifest.AssemblyIdentity.Version = ManifestVersion;
            manifest.AssemblyIdentity.ProcessorArchitecture = ProcessorArchitecture;

            return manifest;
        }

        private static void AddAssemblyReference(
            ApplicationManifest manifest,
            string targetPath,
            bool isEntryPoint = false,
            bool isOptional = false)
        {
            AssemblyReference reference = new()
            {
                IsOptional = isOptional,
                TargetPath = targetPath
            };

            manifest.AssemblyReferences.Add(reference);

            if (isEntryPoint)
            {
                manifest.EntryPoint = reference;
            }
        }

        private static void AddFileReference(
            ApplicationManifest manifest,
            string targetPath,
            bool isOptional = false)
        {
            manifest.FileReferences.Add(
                new FileReference()
                {
                    IsOptional = isOptional,
                    TargetPath = targetPath
                });
        }

        private static FileInfo WriteDeploymentManifest(
            DirectoryInfo root,
            string relativePath,
            string applicationManifestTargetPath,
            bool mapFileExtensions = false,
            AssemblyIdentity? applicationManifestIdentity = null)
        {
            DeployManifest manifest = new()
            {
                MapFileExtensions = mapFileExtensions
            };
            manifest.AssemblyIdentity.Name = "TestDeployment";
            manifest.AssemblyIdentity.Version = ManifestVersion;
            manifest.AssemblyIdentity.ProcessorArchitecture = ProcessorArchitecture;

            AssemblyReference entryPoint = new(applicationManifestTargetPath);

            if (applicationManifestIdentity is not null)
            {
                entryPoint.AssemblyIdentity = new AssemblyIdentity(applicationManifestIdentity);
            }

            manifest.AssemblyReferences.Add(entryPoint);
            manifest.EntryPoint = entryPoint;

            return WriteManifest(root, relativePath, manifest);
        }

        private static FileInfo WriteManifest(
            DirectoryInfo root,
            string relativePath,
            Manifest manifest)
        {
            FileInfo file = new(Path.Combine(root.FullName, relativePath));
            file.Directory!.Create();

            using (FileStream stream = file.Create())
            {
                ManifestWriter.WriteManifest(manifest, stream);
            }

            file.Refresh();

            return file;
        }

        private static FileInfo CreateFile(
            DirectoryInfo root,
            string relativePath)
        {
            FileInfo file = new(Path.Combine(root.FullName, relativePath));
            file.Directory!.Create();
            File.WriteAllText(path: file.FullName, contents: FileContents);
            file.Refresh();

            return file;
        }

        private static FileInfo CreateFusionManifest(
            DirectoryInfo root,
            string relativePath,
            string identityName = "SideBySide",
            string identityVersion = ManifestVersion)
        {
            string xml = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
                  <assemblyIdentity name="{identityName}" version="{identityVersion}" processorArchitecture="msil" type="win32" />
                </assembly>
                """;
            FileInfo file = new(Path.Combine(root.FullName, relativePath));
            file.Directory!.Create();
            File.WriteAllText(file.FullName, xml, Encoding.UTF8);
            file.Refresh();

            return file;
        }

        private static ClickOnceManifestDiagnostic CreateWarningDiagnostic(
            string targetPath)
        {
            return new ClickOnceManifestDiagnostic(
                WarningMessageName,
                targetPath,
                OutputMessageType.Warning);
        }

        private static void ConfigureDeploymentManifestReferences(
            IDeployManifest deploymentManifest,
            AssemblyReference? entryPoint)
        {
            DeployManifest model = new();

            if (entryPoint is not null)
            {
                model.AssemblyReferences.Add(entryPoint);
                model.EntryPoint = entryPoint;
            }

            deploymentManifest.AssemblyReferences.Returns(
                model.AssemblyReferences);
            deploymentManifest.EntryPoint.Returns(entryPoint);
            deploymentManifest.FileReferences.Returns(
                model.FileReferences);
        }

        private static ClickOnceFileGraphResolutionException ResolveWithApplicationManifestProbe(
            DirectoryInfo root,
            string resolvedPath,
            Func<FileInfo, bool> fileExists)
        {
            FileInfo deploymentManifestFile = CreateFile(root, DeploymentManifestFileName);
            AssemblyReference entryPoint = new()
            {
                ResolvedPath = resolvedPath,
                TargetPath = ApplicationManifestFileName
            };
            IDeployManifest deploymentManifest = Substitute.For<IDeployManifest>();

            ConfigureDeploymentManifestReferences(
                deploymentManifest,
                entryPoint);
            deploymentManifest.Diagnostics.Returns(
                new[] { CreateWarningDiagnostic(WarningOneTargetPath) });

            IClickOnceManifestReader manifestReader = Substitute.For<IClickOnceManifestReader>();

            manifestReader.TryReadDeployManifest(
                    Arg.Any<Stream>(),
                    out Arg.Any<IDeployManifest?>())
                .Returns(callInfo =>
                {
                    callInfo[1] = deploymentManifest;
                    return true;
                });

            ClickOnceDeployManifestFileGraphResolver resolver = new(
                manifestReader,
                new ClickOncePayloadFileResolver(),
                fileExists);

            return Assert.Throws<ClickOnceFileGraphResolutionException>(
                () => resolver.Resolve(deploymentManifestFile));
        }
    }
}
