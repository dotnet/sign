// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal enum ClickOnceApplicationSigningPolicy
    {
        SignPayloads,
        UpdateManifestOnly
    }

    // Deliberately not an IDataFormatSigner or a registered CLI service.
    internal sealed class ClickOnceApplicationManifestSigner
    {
        private readonly ClickOnceApplicationPublishLayoutResolver _resolver;
        private readonly ClickOncePublishLayoutStager _stager;
        private readonly IAggregatingDataFormatSigner _payloadSigner;
        private readonly IFileMatcher _fileMatcher;
        private readonly IManifestSigner _manifestSigner;
        private readonly ICertificateProvider _certificateProvider;
        private readonly ISignatureAlgorithmProvider _signatureAlgorithmProvider;
        private readonly ILogger<IDataFormatSigner> _logger;

        internal ClickOnceApplicationManifestSigner(
            ClickOnceApplicationPublishLayoutResolver resolver,
            ClickOncePublishLayoutStager stager,
            IAggregatingDataFormatSigner payloadSigner,
            IFileMatcher fileMatcher,
            IManifestSigner manifestSigner,
            ICertificateProvider certificateProvider,
            ISignatureAlgorithmProvider signatureAlgorithmProvider,
            ILogger<IDataFormatSigner> logger)
        {
            ArgumentNullException.ThrowIfNull(resolver, nameof(resolver));
            ArgumentNullException.ThrowIfNull(stager, nameof(stager));
            ArgumentNullException.ThrowIfNull(payloadSigner, nameof(payloadSigner));
            ArgumentNullException.ThrowIfNull(fileMatcher, nameof(fileMatcher));
            ArgumentNullException.ThrowIfNull(manifestSigner, nameof(manifestSigner));
            ArgumentNullException.ThrowIfNull(certificateProvider, nameof(certificateProvider));
            ArgumentNullException.ThrowIfNull(signatureAlgorithmProvider, nameof(signatureAlgorithmProvider));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _resolver = resolver;
            _stager = stager;
            _payloadSigner = payloadSigner;
            _fileMatcher = fileMatcher;
            _manifestSigner = manifestSigner;
            _certificateProvider = certificateProvider;
            _signatureAlgorithmProvider = signatureAlgorithmProvider;
            _logger = logger;
        }

        internal async Task ExecuteAsync(
            SigningOperationPlan plan,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            ClickOnceApplicationSigningPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(plan, nameof(plan));
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            ArgumentNullException.ThrowIfNull(coordinator, nameof(coordinator));
            if (!Enum.IsDefined(policy))
            {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }

            SigningOperationResult result =
                await coordinator.ExecuteArtifactAsync(
                    plan.Source.SourceIdentity,
                    () => SignOwnerAsync(
                        plan.Source,
                        options,
                        coordinator,
                        policy));

            result.Materialize(plan.Output);
        }

        internal async Task<SigningOperationArtifact> SignOwnerAsync(
            SigningFile input,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            ClickOnceApplicationSigningPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(input, nameof(input));
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            ArgumentNullException.ThrowIfNull(coordinator, nameof(coordinator));
            if (!Enum.IsDefined(policy))
            {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }

            if (!_resolver.TryResolve(input.File, out ResolvedClickOncePublishLayout? layout))
            {
                throw CreateFailure(
                    Resources.ClickOnceApplicationManifestWrongType,
                    input.File);
            }

            using IApplicationManifest manifest = layout.Application.Manifest;
            ClickOnceStagingSession session = _stager.Stage(layout);

            try
            {
                StagedPayload[] payloads = session.Payloads.Select(pair =>
                    new StagedPayload(
                        input.GetSibling(pair.Key.Source, input.File.Directory!),
                        pair.Value)).ToArray();
                foreach (FileInfo file in payloads
                    .Select(payload => payload.File.Destination)
                    .Append(session.ApplicationManifest.Destination)
                    .Distinct(FileInfoComparer.Instance))
                {
                    file.IsReadOnly = false;
                }

                StagedPayload[] selected;
                IDisposable suffixScope = session.BeginManifestFileInfoUpdate();
                try
                {
                    foreach (StagedPayload payload in payloads)
                    {
                        BaseReference reference = payload.File.ManifestReference!;
                        reference.SourcePath = payload.UpdateFile.FullName;
                        reference.ResolvedPath = null;
                    }

                    RunManifestOperation(
                        manifest, input.File,
                        Resources.ClickOnceApplicationManifestResolveFailed,
                        () => manifest.ResolveFiles(
                            new[] { session.ApplicationManifest.Destination.Directory! }));
                    ValidateReferences(input.File, session, payloads);

                    selected = policy == ClickOnceApplicationSigningPolicy.SignPayloads
                        ? SelectPayloads(input, payloads, options)
                        : Array.Empty<StagedPayload>();
                    if (selected.Length > 0)
                    {
                        await _payloadSigner.SignAsync(
                            selected.Select(payload =>
                                payload.Source.WithFile(payload.UpdateFile)),
                            options,
                            coordinator);
                    }

                    // Signing and materialization can change files or reference
                    // state. Never refresh hashes using an unchecked result.
                    ValidateReferences(input.File, session, payloads);
                    manifest.SynchronizeAssemblyIdentities();
                    ValidateReferences(input.File, session, payloads);
                    RunManifestOperation(
                        manifest, input.File,
                        Resources.ClickOnceApplicationManifestUpdateFailed,
                        manifest.UpdateFileInfo);
                }
                catch (Exception exception)
                {
                    DisposeAfterFailure(suffixScope, exception);
                    throw;
                }
                suffixScope.Dispose();

                manifest.Write(session.ApplicationManifest.Destination);
                using X509Certificate2 certificate =
                    await _certificateProvider.GetCertificateAsync();
                using RSA rsa = await _signatureAlgorithmProvider.GetRsaAsync();
                _manifestSigner.Sign(
                    session.ApplicationManifest.Destination,
                    certificate,
                    rsa,
                    options);

                return SigningOperationArtifact.OwnedLayout(
                    session.ApplicationManifest.Destination,
                    session.Directory,
                    selected.Select(payload => payload.File.Destination)
                        .Append(session.ApplicationManifest.Destination),
                    session);
            }
            catch (Exception exception)
            {
                DisposeAfterFailure(session, exception);
                throw;
            }
        }

        private StagedPayload[] SelectPayloads(
            SigningFile input,
            IReadOnlyList<StagedPayload> payloads,
            SignOptions options)
        {
            DirectoryInfoWrapper directory = new(input.File.Directory!);
            HashSet<FileInfo>? included = options.Matcher is null
                ? null
                : _fileMatcher.EnumerateMatches(directory, options.Matcher)
                    .ToHashSet(FileInfoComparer.Instance);
            HashSet<FileInfo>? excluded = options.AntiMatcher is null
                ? null
                : _fileMatcher.EnumerateMatches(directory, options.AntiMatcher)
                    .ToHashSet(FileInfoComparer.Instance);

            return payloads.Where(payload =>
                (included is null || included.Contains(payload.Source.File)) &&
                (excluded is null || !excluded.Contains(payload.Source.File)) &&
                _payloadSigner.HasSigningWork(payload.UpdateFile, options))
                .ToArray();
        }

        private void RunManifestOperation(
            IApplicationManifest manifest,
            FileInfo source,
            string failureMessage,
            Action operation)
        {
            int previousCount = manifest.Diagnostics.Count;
            bool hasErrors = false;
            try
            {
                operation();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                ArgumentException or System.Xml.XmlException)
            {
                throw new SigningException(
                    string.Format(CultureInfo.CurrentCulture, failureMessage, source.FullName),
                    exception);
            }
            finally
            {
                foreach (ClickOnceManifestDiagnostic diagnostic in
                    manifest.Diagnostics.Skip(previousCount))
                {
                    bool isError = diagnostic.Type == OutputMessageType.Error;
                    _logger.Log(
                        isError ? LogLevel.Error : LogLevel.Warning,
                        "{ManifestMessage}",
                        diagnostic.Text);
                    hasErrors |= isError;
                }
            }

            if (hasErrors)
            {
                throw CreateFailure(failureMessage, source);
            }
        }

        private static void ValidateReferences(
            FileInfo source,
            ClickOnceStagingSession session,
            IReadOnlyList<StagedPayload> payloads)
        {
            string root = PathContainment.GetFullDirectoryPath(session.Directory.FullName);
            foreach (StagedPayload payload in payloads)
            {
                string? resolvedPath = payload.File.ManifestReference!.ResolvedPath;
                string expectedPath = payload.UpdateFile.FullName;
                if (string.IsNullOrWhiteSpace(resolvedPath) ||
                    !string.Equals(resolvedPath, expectedPath, StringComparison.OrdinalIgnoreCase) ||
                    !PathContainment.IsContained(root, resolvedPath) ||
                    !File.Exists(expectedPath) ||
                    (payload.File.FileInfoUpdateFile is not null &&
                        File.Exists(payload.File.Destination.FullName)))
                {
                    throw new SigningException(string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ClickOnceApplicationManifestReferenceMismatch,
                        source.FullName,
                        payload.File.TargetPath,
                        expectedPath));
                }
            }
        }

        private static SigningException CreateFailure(string message, FileInfo source)
        {
            return new SigningException(string.Format(
                CultureInfo.CurrentCulture, message, source.FullName));
        }

        private static void DisposeAfterFailure(IDisposable resource, Exception primaryException)
        {
            try
            {
                resource.Dispose();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(primaryException, cleanupException);
            }
        }

        private sealed record StagedPayload(
            SigningFile Source,
            ClickOnceStagedFile File)
        {
            internal FileInfo UpdateFile => File.FileInfoUpdateFile ?? File.Destination;
        }
    }
}
