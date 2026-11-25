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
        private readonly IClickOncePayloadSigningHandler? _payloadHandler;
        private readonly Action<SigningFile>? _observeSource;

        internal ClickOnceApplicationManifestSigner(
            ClickOnceApplicationPublishLayoutResolver resolver,
            ClickOncePublishLayoutStager stager,
            IAggregatingDataFormatSigner payloadSigner,
            IFileMatcher fileMatcher,
            IManifestSigner manifestSigner,
            ICertificateProvider certificateProvider,
            ISignatureAlgorithmProvider signatureAlgorithmProvider,
            ILogger<IDataFormatSigner> logger,
            IClickOncePayloadSigningHandler? payloadHandler = null,
            Action<SigningFile>? observeSource = null)
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
            _payloadHandler = payloadHandler;
            _observeSource = observeSource;
        }

        internal async Task ExecuteAsync(
            SigningOperationPlan plan,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            ClickOnceApplicationSigningMode mode,
            ClickOnceApplicationSigningContext? context = null)
        {
            ArgumentNullException.ThrowIfNull(plan, nameof(plan));
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            ArgumentNullException.ThrowIfNull(coordinator, nameof(coordinator));
            ValidateMode(mode, context);

            SigningOperationResult result =
                await coordinator.WaitAsync(coordinator.ExecuteArtifactAsync(
                    plan.Source.SourceIdentity,
                    () => SignOwnerAsync(
                        plan.Source,
                        options,
                        coordinator,
                        mode,
                        context)));

            if (mode != ClickOnceApplicationSigningMode.SignAsIs &&
                ClickOncePathSpelling.IsSameDirectory(plan.Source.File.Directory!, plan.Output.Directory!))
            {
                result.Materialize(plan.Output,
                    file => BindExistingPublicationDestination(file, plan.Output.Directory!),
                    collisionPolicy: SigningOperationCollisionPolicy.Reject);
            }
            else
            {
                result.Materialize(plan.Output);
            }
        }

        internal async Task<SigningOperationArtifact> SignOwnerAsync(
            SigningFile input,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            ClickOnceApplicationSigningMode mode,
            ClickOnceApplicationSigningContext? context = null)
        {
            ArgumentNullException.ThrowIfNull(input, nameof(input));
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            ArgumentNullException.ThrowIfNull(coordinator, nameof(coordinator));
            ValidateMode(mode, context);

            if (mode == ClickOnceApplicationSigningMode.SignAsIs)
            {
                return await SignAsIsOwnerAsync(input, options);
            }

            if (!_resolver.TryResolve(input.File, out ResolvedClickOncePublishLayout? layout))
            {
                throw CreateFailure(
                    Resources.ClickOnceApplicationManifestWrongType,
                    input.File);
            }

            return await SignOwnerAsync(input, options, coordinator, mode, layout, context: context);
        }

        internal async Task<SigningOperationArtifact> SignOwnerAsync(
            SigningFile input,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            ClickOnceApplicationSigningMode mode,
            ResolvedClickOncePublishLayout layout,
            SigningFile? layoutOrigin = null,
            ClickOnceApplicationSigningContext? context = null)
        {
            ArgumentNullException.ThrowIfNull(input, nameof(input));
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            ArgumentNullException.ThrowIfNull(coordinator, nameof(coordinator));
            ArgumentNullException.ThrowIfNull(layout, nameof(layout));
            ValidateMode(mode, context);
            if (mode == ClickOnceApplicationSigningMode.SignAsIs)
            {
                throw new ArgumentException("A resolved application layout requires manifest updates.", nameof(mode));
            }
            if (layout.Deployment is not null)
            {
                throw new ArgumentException("Application composition requires an application-only layout.", nameof(layout));
            }

            using IApplicationManifest manifest = layout.Application.Manifest;
            manifest.ReadOnly = false;
            SigningFile origin = layoutOrigin ?? input;
            DirectoryInfo sourceRoot = origin.File.Directory!;
            ClickOnceStagingSession session = StageLayout(input, layout, sourceRoot);

            try
            {
                StagedPayload[] payloads = PreparePayloads(origin, sourceRoot, session);
                _observeSource?.Invoke(input);
                foreach (StagedPayload payload in payloads)
                {
                    _observeSource?.Invoke(payload.Source);
                }
                ClickOnceReadOnlyGuard.ValidateModification(input.File, session.ApplicationManifest.Destination);
                StagedPayload[] selected;
                List<ClickOncePayloadSigningOutput> additionalOutputs = new();
                IDisposable suffixScope = session.BeginManifestFileInfoUpdate();
                try
                {
                    ResolvePayloads(manifest, input.File, session, payloads);
                    selected = mode == ClickOnceApplicationSigningMode.Full
                        ? SelectPayloads(origin, payloads, options)
                        : Array.Empty<StagedPayload>();
                    foreach (StagedPayload payload in selected)
                    {
                        ClickOnceReadOnlyGuard.ValidateModification(payload.Source.File, payload.UpdateFile);
                    }
                    if (mode == ClickOnceApplicationSigningMode.UpdateManifestOnly)
                    {
                        await MaterializeExplicitPayloadsAsync(payloads, context!, coordinator);
                    }
                    if (selected.Length > 0)
                    {
                        await SignSelectedPayloadsAsync(layout, session, selected, additionalOutputs, options, coordinator);
                    }
                    RefreshManifestMetadata(manifest, input.File, session, payloads);
                }
                catch (Exception exception)
                {
                    DisposeAfterFailure(suffixScope, exception);
                    throw;
                }
                suffixScope.Dispose();

                await WriteAndSignManifestAsync(manifest, session.ApplicationManifest.Destination, options);

                return SigningOperationArtifact.OwnedLayout(
                    session.ApplicationManifest.Destination,
                    session.Directory,
                    selected.Select(payload => payload.File.Destination)
                        .Concat(additionalOutputs.Select(output => output.Plan.Output))
                        .Append(session.ApplicationManifest.Destination)
                        .DistinctBy(file => file.FullName, StringComparer.OrdinalIgnoreCase),
                    session);
            }
            catch (Exception exception)
            {
                DisposeAfterFailure(session, exception);
                throw;
            }
        }

        private ClickOnceStagingSession StageLayout(
            SigningFile input,
            ResolvedClickOncePublishLayout layout,
            DirectoryInfo sourceRoot)
        {
            return _stager.Stage(layout, payload =>
            {
                try
                {
                    return BindOriginalSource(payload.Source, sourceRoot);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    throw new ClickOncePublishLayoutResolutionException(
                        string.Format(CultureInfo.CurrentCulture,
                            Resources.ClickOnceApplicationManifestPayloadProbeFailed,
                            input.File.FullName, payload.TargetPath, payload.Source.FullName),
                        layout.Diagnostics, exception);
                }
            });
        }

        private static StagedPayload[] PreparePayloads(
            SigningFile origin,
            DirectoryInfo sourceRoot,
            ClickOnceStagingSession session)
        {
            StagedPayload[] payloads = session.Payloads.Select(pair =>
                new StagedPayload(
                    new ClickOncePayloadSigningRequest(
                        origin.GetSibling(pair.Value.Source, sourceRoot),
                        sourceRoot,
                        session.Directory,
                        pair.Value))).ToArray();
            return payloads;
        }

        private void ResolvePayloads(
            IApplicationManifest manifest,
            FileInfo source,
            ClickOnceStagingSession session,
            IReadOnlyList<StagedPayload> payloads)
        {
            foreach (StagedPayload payload in payloads)
            {
                BaseReference reference = payload.File.ManifestReference!;
                reference.SourcePath = payload.UpdateFile.FullName;
                reference.ResolvedPath = null;
            }
            RunManifestOperation(
                manifest, source,
                Resources.ClickOnceApplicationManifestResolveFailed,
                () => manifest.ResolveFiles(
                    new[] { session.ApplicationManifest.Destination.Directory! }));
            ValidateReferences(source, session, payloads);
        }

        private static async Task MaterializeExplicitPayloadsAsync(
            IReadOnlyList<StagedPayload> payloads,
            ClickOnceApplicationSigningContext context,
            SigningOperationCoordinator coordinator)
        {
            foreach (StagedPayload payload in payloads.Where(payload =>
                context.IsExplicitPayload(payload.Source.SourceIdentity)))
            {
                ClickOnceReadOnlyGuard.ValidateModification(payload.Source.File, payload.UpdateFile);
                if (!coordinator.TryGetResult(payload.Source.SourceIdentity,
                    out Task<SigningOperationResult>? prerequisite))
                {
                    throw new InvalidOperationException(
                        $"The explicit payload operation for '{payload.Source.File.FullName}' was not registered before application signing.");
                }
                SigningOperationResult result = await coordinator.WaitForReadyOwnerAsync(prerequisite!);
                result.MaterializePrimary(payload.UpdateFile);
            }
        }

        private async Task SignSelectedPayloadsAsync(
            ResolvedClickOncePublishLayout layout,
            ClickOnceStagingSession session,
            IReadOnlyList<StagedPayload> selected,
            List<ClickOncePayloadSigningOutput> additionalOutputs,
            SignOptions options,
            SigningOperationCoordinator coordinator)
        {
            StagedPayload[] ordinary = selected.Where(payload => !payload.Handled).ToArray();
            if (ordinary.Length > 0)
            {
                await _payloadSigner.SignAsync(
                    ordinary.Select(payload => payload.Source.WithFile(payload.UpdateFile)),
                    options,
                    coordinator);
            }

            List<(StagedPayload Payload, ClickOncePayloadSigningResult Result)> results = new();
            foreach (StagedPayload payload in selected.Where(payload => payload.Handled))
            {
                ClickOncePayloadSigningResult result =
                    await _payloadHandler!.SignAsync(payload.Request, options, coordinator);
                if (result is null)
                {
                    throw new InvalidOperationException("The payload handler returned no result.");
                }
                results.Add((payload, result));
                additionalOutputs.AddRange(result.AdditionalOutputs);
            }

            _stager.ValidateAdditionalOutputs(
                layout, session, additionalOutputs.Select(output => output.Plan).ToArray());
            Dictionary<string, (FileInfo File, SigningFile Source, SigningOperationResult Result)> materializations =
                new(StringComparer.OrdinalIgnoreCase);
            foreach ((StagedPayload payload, ClickOncePayloadSigningResult result) in results)
            {
                AddMaterialization(materializations, payload.UpdateFile, payload.Source, result.Primary);
            }
            foreach (ClickOncePayloadSigningOutput output in additionalOutputs)
            {
                AddMaterialization(materializations, output.Plan.Output, output.Plan.Source, output.Result);
            }
            foreach ((FileInfo file, SigningFile source, SigningOperationResult result) in materializations.Values)
            {
                ClickOnceReadOnlyGuard.ValidateModification(source.File, file);
                result.MaterializePrimary(file);
            }
        }

        private static void AddMaterialization(
            Dictionary<string, (FileInfo File, SigningFile Source, SigningOperationResult Result)> materializations,
            FileInfo file,
            SigningFile source,
            SigningOperationResult result)
        {
            if (materializations.TryGetValue(file.FullName,
                out (FileInfo File, SigningFile Source, SigningOperationResult Result) existing))
            {
                if (!ReferenceEquals(existing.Result, result))
                {
                    throw new InvalidOperationException(
                        "The payload handler returned different coordinated results for one destination.");
                }
                return;
            }
            materializations[file.FullName] = (file, source, result);
        }

        private void RefreshManifestMetadata(
            IApplicationManifest manifest,
            FileInfo source,
            ClickOnceStagingSession session,
            IReadOnlyList<StagedPayload> payloads)
        {
            // Signing and materialization can change files or reference
            // state. Never refresh hashes using an unchecked result.
            ValidateReferences(source, session, payloads);
            manifest.SynchronizeAssemblyIdentities();
            ValidateReferences(source, session, payloads);
            RunManifestOperation(
                manifest, source,
                Resources.ClickOnceApplicationManifestUpdateFailed,
                manifest.UpdateFileInfo);
        }

        private async Task WriteAndSignManifestAsync(
            IApplicationManifest manifest,
            FileInfo destination,
            SignOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.ApplicationName))
            {
                manifest.AssemblyIdentity.Name = options.ApplicationName;
            }
            manifest.Write(destination);
            await SignManifestAsync(destination, options);
        }

        private static void ValidateMode(
            ClickOnceApplicationSigningMode mode,
            ClickOnceApplicationSigningContext? context)
        {
            if (!Enum.IsDefined(mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            if (mode == ClickOnceApplicationSigningMode.UpdateManifestOnly)
            {
                ArgumentNullException.ThrowIfNull(context, nameof(context));
            }
        }

        private async Task<SigningOperationArtifact> SignAsIsOwnerAsync(
            SigningFile input,
            SignOptions options)
        {
            ClickOnceStagingDirectory directory = new(_stager.DirectoryService);
            try
            {
                FileInfo copy = new(Path.Combine(directory.Directory.FullName, input.File.Name));
                try
                {
                    using FileStream source = input.File.OpenRead();
                    if (!_resolver.ManifestReader.TryReadApplicationManifest(source, out IApplicationManifest? manifest))
                    {
                        throw CreateFailure(Resources.ClickOnceApplicationManifestWrongType, input.File);
                    }
                    using (manifest)
                    {
                        directory.Copy(input.File, copy);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or
                    InvalidOperationException or System.Xml.XmlException)
                {
                    throw new SigningException(
                        string.Format(CultureInfo.CurrentCulture,
                            Resources.ClickOnceApplicationManifestReadFailed, input.File.FullName),
                        exception);
                }

                ClickOnceReadOnlyGuard.ValidateModification(input.File, copy);
                await SignManifestAsync(copy, options);
                return SigningOperationArtifact.OwnedSingle(copy, directory);
            }
            catch (Exception exception)
            {
                DisposeAfterFailure(directory, exception);
                throw;
            }
        }

        private async Task SignManifestAsync(FileInfo file, SignOptions options)
        {
            using X509Certificate2 certificate = await _certificateProvider.GetCertificateAsync();
            using RSA rsa = await _signatureAlgorithmProvider.GetRsaAsync();
            _manifestSigner.Sign(file, certificate, rsa, options);
        }

        internal static bool PublishesBesideInput(DirectoryInfo sourceRoot, DirectoryInfo destinationRoot) =>
            ClickOncePathSpelling.IsSameDirectory(sourceRoot, destinationRoot);

        internal static FileInfo BindExistingPublicationDestination(FileInfo destination, DirectoryInfo sourceRoot)
        {
            try
            {
                _ = File.GetAttributes(destination.FullName);
            }
            catch (Exception exception) when (
                exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return destination;
            }

            FileInfo existing = BindOriginalSource(destination, sourceRoot);
            return new FileInfo(Path.Combine(destination.DirectoryName!, existing.Name));
        }

        internal static FileInfo BindOriginalSource(FileInfo source, DirectoryInfo sourceRoot)
        {
            string root = PathContainment.GetFullDirectoryPath(sourceRoot.FullName);
            if (!PathContainment.IsContained(root, source.FullName))
            {
                throw new ArgumentException("The original payload source is outside its source root.", nameof(source));
            }

            // Opening the declared path first prevents a case-insensitive
            // spelling lookup from broadening resolution in sensitive directories.
            using FileStream original = source.OpenRead();
            return (FileInfo)ClickOncePathSpelling.Bind(source, sourceRoot);
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
                (excluded is null || !excluded.Contains(payload.Source.File)))
                .Select(payload => payload with
                {
                    Handled = _payloadHandler?.CanSign(payload.Request, options) == true
                })
                .Where(payload => payload.Handled || _payloadSigner.HasSigningWork(payload.UpdateFile, options))
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
                    !string.Equals(resolvedPath, expectedPath, StringComparison.Ordinal) ||
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

        private sealed record StagedPayload(ClickOncePayloadSigningRequest Request)
        {
            internal SigningFile Source => Request.Source;
            internal ClickOnceStagedFile File => Request.StagedFile;
            internal bool Handled { get; init; }
            internal FileInfo UpdateFile => File.FileInfoUpdateFile ?? File.Destination;
        }
    }
}
