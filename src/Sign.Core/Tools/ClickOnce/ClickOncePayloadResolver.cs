// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;

namespace Sign.Core
{
    internal sealed class ClickOncePayloadResolver
    {
        private const string ClrPlatformAssemblyName = "Microsoft.Windows.CommonLanguageRuntime";
        private const string DeploySuffix = ".deploy";

        private readonly Func<FileInfo, bool> _fileExists;

        internal ClickOncePayloadResolver()
            : this(ClickOnceFileSystem.IsFile)
        {
        }

        internal ClickOncePayloadResolver(Func<FileInfo, bool> fileExists)
        {
            ArgumentNullException.ThrowIfNull(fileExists, nameof(fileExists));

            _fileExists = fileExists;
        }

        internal IReadOnlyList<ResolvedClickOncePayload> ResolveForDeployment(
            FileInfo applicationManifestFile,
            IApplicationManifest applicationManifest,
            DirectoryInfo deploymentDirectory,
            bool mapFileExtensions,
            ICollection<ClickOnceManifestDiagnostic> diagnostics)
        {
            ArgumentNullException.ThrowIfNull(applicationManifestFile, nameof(applicationManifestFile));
            ArgumentNullException.ThrowIfNull(applicationManifest, nameof(applicationManifest));
            ArgumentNullException.ThrowIfNull(deploymentDirectory, nameof(deploymentDirectory));
            ArgumentNullException.ThrowIfNull(diagnostics, nameof(diagnostics));

            DirectoryInfo applicationDirectory = applicationManifestFile.Directory!;
            DirectoryInfo[] searchDirectories = PathsEqual(applicationDirectory.FullName, deploymentDirectory.FullName)
                ? new[] { applicationDirectory }
                : new[] { applicationDirectory, deploymentDirectory };

            return Resolve(
                applicationManifestFile,
                applicationManifest,
                searchDirectories,
                mapFileExtensions ? PayloadLookupKind.Mapped : PayloadLookupKind.Unmapped,
                diagnostics);
        }

        internal IReadOnlyList<ResolvedClickOncePayload> ResolveForExplicitApplication(
            FileInfo applicationManifestFile,
            IApplicationManifest applicationManifest,
            ICollection<ClickOnceManifestDiagnostic> diagnostics)
        {
            ArgumentNullException.ThrowIfNull(applicationManifestFile, nameof(applicationManifestFile));
            ArgumentNullException.ThrowIfNull(applicationManifest, nameof(applicationManifest));
            ArgumentNullException.ThrowIfNull(diagnostics, nameof(diagnostics));

            return Resolve(
                applicationManifestFile,
                applicationManifest,
                new[] { applicationManifestFile.Directory! },
                PayloadLookupKind.UnmappedThenMapped,
                diagnostics);
        }

        private IReadOnlyList<ResolvedClickOncePayload> Resolve(
            FileInfo applicationManifestFile,
            IApplicationManifest applicationManifest,
            DirectoryInfo[] searchDirectories,
            PayloadLookupKind lookupKind,
            ICollection<ClickOnceManifestDiagnostic> diagnostics)
        {
            List<BaseReference> references = GetPhysicalReferences(applicationManifest);

            AddDiagnostics(applicationManifest, diagnostics);

            foreach (BaseReference reference in references)
            {
                ValidateTargetPath(
                    applicationManifestFile,
                    reference.TargetPath,
                    searchDirectories,
                    diagnostics);
            }

            List<ResolvedClickOncePayload> payloads = new(references.Count);

            foreach (BaseReference reference in references)
            {
                string? targetPath = reference.TargetPath;

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    throw new ClickOncePublishLayoutResolutionException(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            Resources.ClickOnceApplicationManifestMissingTargetPath,
                            applicationManifestFile.FullName),
                        diagnostics);
                }

                FileInfo source;

                try
                {
                    if (TryResolve(
                        applicationManifestFile,
                        targetPath,
                        searchDirectories,
                        lookupKind,
                        diagnostics,
                        out source))
                    {
                        reference.ResolvedPath = source.FullName;

                        payloads.Add(
                            new ResolvedClickOncePayload(
                                source,
                                reference));

                        continue;
                    }
                }
                catch (Exception exception) when (
                    exception is ArgumentException or
                    NotSupportedException or
                    PathTooLongException)
                {
                    throw new ClickOncePublishLayoutResolutionException(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            Resources.ClickOnceApplicationManifestInvalidTargetPath,
                            applicationManifestFile.FullName,
                            targetPath),
                        diagnostics,
                        exception);
                }
                throw new ClickOncePublishLayoutResolutionException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceApplicationManifestRequiredFileNotFound,
                        applicationManifestFile.FullName,
                        targetPath),
                    diagnostics);
            }

            return payloads;
        }

        private static List<BaseReference> GetPhysicalReferences(IApplicationManifest applicationManifest)
        {
            List<BaseReference> references = applicationManifest.AssemblyReferences
                .Cast<AssemblyReference>()
                .Where(IsPhysical)
                .Cast<BaseReference>()
                .ToList();

            if (applicationManifest.EntryPoint is not null &&
                IsPhysical(applicationManifest.EntryPoint) &&
                !references.Any(reference => ReferenceEquals(reference, applicationManifest.EntryPoint)))
            {
                references.Add(applicationManifest.EntryPoint);
            }

            references.AddRange(applicationManifest.FileReferences.Cast<FileReference>());

            return references;
        }

        private static bool IsPhysical(AssemblyReference reference)
        {
            return !reference.IsPrerequisite &&
                !string.Equals(
                    reference.AssemblyIdentity?.Name,
                    ClrPlatformAssemblyName,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateTargetPath(
            FileInfo applicationManifestFile,
            string? targetPath,
            IEnumerable<DirectoryInfo> searchDirectories,
            ICollection<ClickOnceManifestDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            if (Path.IsPathRooted(targetPath))
            {
                throw new ClickOncePublishLayoutResolutionException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceApplicationManifestInvalidTargetPath,
                        applicationManifestFile.FullName,
                        targetPath),
                    diagnostics);
            }

            try
            {
                foreach (DirectoryInfo directory in searchDirectories)
                {
                    _ = Path.GetFullPath(Path.Combine(directory.FullName, targetPath));
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                PathTooLongException)
            {
                throw new ClickOncePublishLayoutResolutionException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceApplicationManifestInvalidTargetPath,
                        applicationManifestFile.FullName,
                        targetPath),
                    diagnostics,
                    exception);
            }
        }

        private bool TryResolve(
            FileInfo applicationManifestFile,
            string targetPath,
            IEnumerable<DirectoryInfo> searchDirectories,
            PayloadLookupKind lookupKind,
            ICollection<ClickOnceManifestDiagnostic> diagnostics,
            out FileInfo source)
        {
            foreach (DirectoryInfo directory in searchDirectories)
            {
                if (lookupKind is PayloadLookupKind.Unmapped or PayloadLookupKind.UnmappedThenMapped)
                {
                    FileInfo unmappedSource = new(Path.Combine(directory.FullName, targetPath));

                    if (IsFile(
                        applicationManifestFile,
                        targetPath,
                        unmappedSource,
                        diagnostics))
                    {
                        source = unmappedSource;

                        return true;
                    }
                }

                if (lookupKind is PayloadLookupKind.Mapped or PayloadLookupKind.UnmappedThenMapped)
                {
                    FileInfo mappedSource = new(Path.Combine(directory.FullName, $"{targetPath}{DeploySuffix}"));

                    if (IsFile(
                        applicationManifestFile,
                        targetPath,
                        mappedSource,
                        diagnostics))
                    {
                        source = mappedSource;

                        return true;
                    }
                }
            }
            source = null!;

            return false;
        }

        private bool IsFile(
            FileInfo applicationManifestFile,
            string targetPath,
            FileInfo sourceCandidate,
            ICollection<ClickOnceManifestDiagnostic> diagnostics)
        {
            try
            {
                return _fileExists(sourceCandidate);
            }
            catch (PathTooLongException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                throw new ClickOncePublishLayoutResolutionException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceApplicationManifestPayloadProbeFailed,
                        applicationManifestFile.FullName,
                        targetPath,
                        sourceCandidate.FullName),
                    diagnostics,
                    exception);
            }
        }

        private static void AddDiagnostics(
            IClickOnceManifest manifest,
            ICollection<ClickOnceManifestDiagnostic> diagnostics)
        {
            foreach (ClickOnceManifestDiagnostic diagnostic in manifest.Diagnostics)
            {
                diagnostics.Add(diagnostic);
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private enum PayloadLookupKind
        {
            Unmapped,
            Mapped,
            UnmappedThenMapped
        }
    }
}
