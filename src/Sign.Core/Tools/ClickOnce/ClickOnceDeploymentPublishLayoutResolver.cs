// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ClickOnceDeploymentPublishLayoutResolver
    {
        private readonly IClickOnceManifestReader _manifestReader;
        private readonly ClickOncePayloadResolver _payloadResolver;
        private readonly Func<FileInfo, bool> _fileExists;

        internal ClickOnceDeploymentPublishLayoutResolver(
            IClickOnceManifestReader manifestReader,
            ClickOncePayloadResolver payloadResolver)
            : this(manifestReader, payloadResolver, ClickOnceFileSystem.IsFile)
        {
        }

        internal ClickOnceDeploymentPublishLayoutResolver(
            IClickOnceManifestReader manifestReader,
            ClickOncePayloadResolver payloadResolver,
            Func<FileInfo, bool> fileExists)
        {
            ArgumentNullException.ThrowIfNull(manifestReader, nameof(manifestReader));
            ArgumentNullException.ThrowIfNull(payloadResolver, nameof(payloadResolver));
            ArgumentNullException.ThrowIfNull(fileExists, nameof(fileExists));

            _manifestReader = manifestReader;
            _payloadResolver = payloadResolver;
            _fileExists = fileExists;
        }

        internal ResolvedClickOncePublishLayout Resolve(FileInfo deploymentManifestFile)
        {
            ArgumentNullException.ThrowIfNull(deploymentManifestFile, nameof(deploymentManifestFile));

            IDeployManifest deploymentManifest = ReadDeployManifest(deploymentManifestFile);
            AssemblyReference? entryPoint = deploymentManifest.EntryPoint;
            List<ClickOnceManifestDiagnostic> diagnostics = GetDiagnostics(deploymentManifest);

            if ((entryPoint is null &&
                    deploymentManifest.AssemblyReferences.Count == 0 &&
                    deploymentManifest.FileReferences.Count == 0) ||
                (entryPoint is not null &&
                    string.IsNullOrWhiteSpace(entryPoint.TargetPath)))
            {
                throw new ClickOncePublishLayoutResolutionException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceDeploymentManifestMissingEntryPoint,
                        deploymentManifestFile.FullName),
                    diagnostics);
            }

            entryPoint = ValidateReferences(
                deploymentManifestFile,
                deploymentManifest,
                entryPoint);
            ValidateTargetPath(
                deploymentManifestFile,
                entryPoint,
                diagnostics);
            deploymentManifest.ReadOnly = false;

            try
            {
                ResolveFilesWithTargetOnlyEntryPoint(
                    deploymentManifest,
                    entryPoint,
                    new[] { deploymentManifestFile.Directory! });
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                ArgumentException)
            {
                List<ClickOnceManifestDiagnostic> resolutionDiagnostics = GetDiagnostics(deploymentManifest);

                throw CreateResolutionException(
                    deploymentManifestFile,
                    resolutionDiagnostics,
                    exception);
            }

            diagnostics = GetDiagnostics(deploymentManifest);

            FileInfo applicationManifestFile = GetApplicationManifestFile(
                deploymentManifestFile,
                entryPoint,
                diagnostics);
            IApplicationManifest applicationManifest = ReadApplicationManifest(
                deploymentManifestFile,
                applicationManifestFile,
                diagnostics);
            applicationManifest.ReadOnly = false;

            IReadOnlyList<ResolvedClickOncePayload> payloads = _payloadResolver.ResolveForDeployment(
                applicationManifestFile,
                applicationManifest,
                deploymentManifestFile.Directory!,
                deploymentManifest.MapFileExtensions,
                diagnostics);

            IReadOnlyList<ResolvedClickOnceAdjacentExecutable> adjacentExecutables =
                ResolveAdjacentExecutables(
                    deploymentManifestFile,
                    deploymentManifestFile.Directory!,
                    payloads,
                    diagnostics);

            return new ResolvedClickOncePublishLayout(
                new ResolvedClickOnceDeployment(
                    deploymentManifestFile,
                    deploymentManifest,
                    entryPoint),
                new ResolvedClickOnceApplication(
                    applicationManifestFile,
                    applicationManifest,
                    payloads),
                adjacentExecutables,
                diagnostics);
        }

        private IDeployManifest ReadDeployManifest(FileInfo deploymentManifestFile)
        {
            try
            {
                using FileStream stream = deploymentManifestFile.OpenRead();

                if (_manifestReader.TryReadDeployManifest(
                    stream,
                    out IDeployManifest? deploymentManifest))
                {
                    return deploymentManifest;
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                System.Xml.XmlException)
            {
                throw new ClickOncePublishLayoutResolutionException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceDeploymentManifestReadFailed,
                        deploymentManifestFile.FullName),
                    innerException: exception);
            }

            throw new ClickOncePublishLayoutResolutionException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceDeploymentManifestWrongType,
                    deploymentManifestFile.FullName));
        }

        private static AssemblyReference ValidateReferences(
            FileInfo deploymentManifestFile,
            IDeployManifest deploymentManifest,
            AssemblyReference? entryPoint)
        {
            // Mirrors System.Deployment's deployment-role checks:
            // Ex_DepApplicationDependencyRequired, Ex_DepFileNotAllowed, and
            // Ex_DepAppRefPrereqOrOptionalOrResourceFallback. Fail before
            // ManifestUtilities or mage can reorder or rewrite invalid references.
            if (deploymentManifest.AssemblyReferences.Count != 1 ||
                deploymentManifest.FileReferences.Count != 0 ||
                entryPoint is null ||
                entryPoint.IsPrerequisite ||
                entryPoint.IsOptional ||
                deploymentManifest.HasUnsupportedResourceFallback ||
                !ReferenceEquals(
                    entryPoint,
                    deploymentManifest.AssemblyReferences[0]))
            {
                throw new ClickOncePublishLayoutResolutionException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceDeploymentManifestUnsupportedReferences,
                        deploymentManifestFile.FullName),
                    deploymentManifest.Diagnostics);
            }

            return entryPoint;
        }

        private static void ValidateTargetPath(
            FileInfo deploymentManifestFile,
            AssemblyReference entryPoint,
            IReadOnlyCollection<ClickOnceManifestDiagnostic> diagnostics)
        {
            if (Path.IsPathRooted(entryPoint.TargetPath))
            {
                throw new ClickOncePublishLayoutResolutionException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceDeploymentManifestInvalidTargetPath,
                        deploymentManifestFile.FullName,
                        entryPoint.TargetPath),
                    diagnostics);
            }
        }

        private IApplicationManifest ReadApplicationManifest(
            FileInfo deploymentManifestFile,
            FileInfo applicationManifestFile,
            IReadOnlyCollection<ClickOnceManifestDiagnostic> diagnostics)
        {
            try
            {
                using FileStream stream = applicationManifestFile.OpenRead();

                if (_manifestReader.TryReadApplicationManifest(
                    stream,
                    out IApplicationManifest? applicationManifest))
                {
                    return applicationManifest;
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                System.Xml.XmlException)
            {
                throw new ClickOncePublishLayoutResolutionException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceDeploymentManifestReferencedApplicationReadFailed,
                        applicationManifestFile.FullName,
                        deploymentManifestFile.FullName),
                    diagnostics,
                    exception);
            }

            throw new ClickOncePublishLayoutResolutionException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceDeploymentManifestReferencedApplicationWrongType,
                    applicationManifestFile.FullName,
                    deploymentManifestFile.FullName),
                diagnostics);
        }

        private FileInfo GetApplicationManifestFile(
            FileInfo deploymentManifestFile,
            AssemblyReference entryPoint,
            IReadOnlyCollection<ClickOnceManifestDiagnostic> diagnostics)
        {
            string? resolvedPath = entryPoint.ResolvedPath;

            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                string expectedPath = Path.Combine(
                    deploymentManifestFile.DirectoryName!,
                    entryPoint.TargetPath);

                throw new ClickOncePublishLayoutResolutionException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceDeploymentManifestUnresolvedApplicationManifest,
                        deploymentManifestFile.FullName,
                        expectedPath),
                    diagnostics);
            }

            FileInfo applicationManifestFile = new(resolvedPath);

            try
            {
                if (!_fileExists(applicationManifestFile))
                {
                    throw new ClickOncePublishLayoutResolutionException(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            Resources.ClickOnceDeploymentManifestApplicationManifestNotFound,
                            deploymentManifestFile.FullName,
                            applicationManifestFile.FullName),
                        diagnostics);
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                ArgumentException or
                NotSupportedException)
            {
                throw new ClickOncePublishLayoutResolutionException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceDeploymentManifestReferencedApplicationReadFailed,
                        applicationManifestFile.FullName,
                        deploymentManifestFile.FullName),
                    diagnostics,
                    exception);
            }

            return applicationManifestFile;
        }

        private static void ResolveFilesWithTargetOnlyEntryPoint(
            IDeployManifest deploymentManifest,
            AssemblyReference? entryPoint,
            IReadOnlyList<DirectoryInfo> searchDirectories)
        {
            if (entryPoint is null)
            {
                deploymentManifest.ResolveFiles(searchDirectories);

                return;
            }

            string? sourcePath = entryPoint.SourcePath;
            AssemblyIdentity? assemblyIdentity = entryPoint.AssemblyIdentity;

            try
            {
                // ManifestUtilities otherwise prioritizes SourcePath and assembly identity over TargetPath.
                entryPoint.SourcePath = null;
                entryPoint.AssemblyIdentity = null;
                deploymentManifest.ResolveFiles(searchDirectories);
            }
            finally
            {
                entryPoint.SourcePath = sourcePath;
                entryPoint.AssemblyIdentity = assemblyIdentity;
            }
        }

        private IReadOnlyList<ResolvedClickOnceAdjacentExecutable> ResolveAdjacentExecutables(
            FileInfo deploymentManifestFile,
            DirectoryInfo deploymentDirectory,
            IReadOnlyList<ResolvedClickOncePayload> payloads,
            IReadOnlyCollection<ClickOnceManifestDiagnostic> diagnostics)
        {
            HashSet<string> payloadPaths = payloads
                .Select(payload => Path.GetFullPath(payload.Source.FullName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<ResolvedClickOnceAdjacentExecutable> adjacentExecutables = new();
            FileInfo setup = new(
                Path.Combine(
                    deploymentDirectory.FullName,
                    ResolvedClickOnceAdjacentExecutable.SetupFileName));

            if (IsAdjacentFile(
                setup,
                deploymentManifestFile,
                Resources.ClickOnceDeploymentManifestSetupProbeFailed,
                diagnostics))
            {
                adjacentExecutables.Add(
                    new ResolvedClickOnceAdjacentExecutable(
                        setup,
                        ClickOnceAdjacentExecutableKind.Setup));
            }

            FileInfo launcher = new(
                Path.Combine(
                    deploymentDirectory.FullName,
                    ResolvedClickOnceAdjacentExecutable.LauncherFileName));

            if (!payloadPaths.Contains(Path.GetFullPath(launcher.FullName)) &&
                IsAdjacentFile(
                    launcher,
                    deploymentManifestFile,
                    Resources.ClickOnceDeploymentManifestLauncherProbeFailed,
                    diagnostics))
            {
                adjacentExecutables.Add(
                    new ResolvedClickOnceAdjacentExecutable(
                        launcher,
                        ClickOnceAdjacentExecutableKind.Launcher));
            }

            return adjacentExecutables;
        }

        private bool IsAdjacentFile(
            FileInfo candidate,
            FileInfo deploymentManifestFile,
            string failureMessageFormat,
            IReadOnlyCollection<ClickOnceManifestDiagnostic> diagnostics)
        {
            try
            {
                return _fileExists(candidate);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                throw new ClickOncePublishLayoutResolutionException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        failureMessageFormat,
                        candidate.FullName,
                        deploymentManifestFile.FullName),
                    diagnostics,
                    exception);
            }
        }

        private static ClickOncePublishLayoutResolutionException CreateResolutionException(
            FileInfo deploymentManifestFile,
            IEnumerable<ClickOnceManifestDiagnostic> diagnostics,
            Exception innerException)
        {
            return new ClickOncePublishLayoutResolutionException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ClickOnceDeploymentManifestResolveFailed,
                    deploymentManifestFile.FullName),
                diagnostics,
                innerException);
        }

        private static List<ClickOnceManifestDiagnostic> GetDiagnostics(IClickOnceManifest manifest)
        {
            return manifest.Diagnostics.ToList();
        }
    }
}
