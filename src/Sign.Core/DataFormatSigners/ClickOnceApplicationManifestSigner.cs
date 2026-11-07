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

        internal ClickOnceApplicationManifestSigner(
            ClickOnceApplicationPublishLayoutResolver resolver,
            ClickOncePublishLayoutStager stager,
            IAggregatingDataFormatSigner payloadSigner,
            IFileMatcher fileMatcher,
            IManifestSigner manifestSigner,
            ICertificateProvider certificateProvider,
            ISignatureAlgorithmProvider signatureAlgorithmProvider,
            ILogger<IDataFormatSigner> logger,
            IClickOncePayloadSigningHandler? payloadHandler = null)
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
        }

        internal async Task ExecuteAsync(
            SigningOperationPlan plan,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            ClickOnceApplicationSigningPolicy policy,
            bool updateManifest = true,
            ClickOnceApplicationSigningContext? context = null)
        {
            ArgumentNullException.ThrowIfNull(plan, nameof(plan));
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            ArgumentNullException.ThrowIfNull(coordinator, nameof(coordinator));
            if (!Enum.IsDefined(policy))
            {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }

            SigningOperationResult result =
                await coordinator.WaitAsync(coordinator.ExecuteArtifactAsync(
                    plan.Source.SourceIdentity,
                    () => SignOwnerAsync(
                        plan.Source,
                        options,
                        coordinator,
                        policy,
                        updateManifest,
                        context)));

            if (updateManifest && ClickOncePathSpelling.IsSameDirectory(plan.Source.File.Directory!, plan.Output.Directory!))
            {
                result.Materialize(plan.Output,
                    file => BindExistingPublicationDestination(file, plan.Output.Directory!));
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
            ClickOnceApplicationSigningPolicy policy,
            bool updateManifest = true,
            ClickOnceApplicationSigningContext? context = null)
        {
            ArgumentNullException.ThrowIfNull(input, nameof(input));
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            ArgumentNullException.ThrowIfNull(coordinator, nameof(coordinator));
            if (!Enum.IsDefined(policy))
            {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }

            if (!updateManifest)
            {
                return await SignAsIsOwnerAsync(input, options);
            }

            if (!_resolver.TryResolve(input.File, out ResolvedClickOncePublishLayout? layout))
            {
                throw CreateFailure(
                    Resources.ClickOnceApplicationManifestWrongType,
                    input.File);
            }

            return await SignOwnerAsync(input, options, coordinator, policy, layout, context: context);
        }

        internal async Task<SigningOperationArtifact> SignOwnerAsync(
            SigningFile input,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            ClickOnceApplicationSigningPolicy policy,
            ResolvedClickOncePublishLayout layout,
            SigningFile? layoutOrigin = null,
            ClickOnceApplicationSigningContext? context = null)
        {
            ArgumentNullException.ThrowIfNull(input, nameof(input));
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            ArgumentNullException.ThrowIfNull(coordinator, nameof(coordinator));
            ArgumentNullException.ThrowIfNull(layout, nameof(layout));
            if (!Enum.IsDefined(policy))
            {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }
            if (layout.Deployment is not null)
            {
                throw new ArgumentException("Application composition requires an application-only layout.", nameof(layout));
            }

            using IApplicationManifest manifest = layout.Application.Manifest;
            manifest.ReadOnly = false;
            SigningFile origin = layoutOrigin ?? input;
            DirectoryInfo sourceRoot = origin.File.Directory!;
            ClickOnceStagingSession session = _stager.Stage(layout, payload =>
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

            try
            {
                StagedPayload[] payloads = session.Payloads.Select(pair =>
                    new StagedPayload(
                        new ClickOncePayloadSigningRequest(
                            origin.GetSibling(pair.Value.Source, sourceRoot),
                            sourceRoot,
                            session.Directory,
                            pair.Value))).ToArray();
                foreach (FileInfo file in payloads
                    .Select(payload => payload.File.Destination)
                    .Append(session.ApplicationManifest.Destination)
                    .Distinct(FileInfoComparer.Instance))
                {
                    file.IsReadOnly = false;
                }

                StagedPayload[] selected;
                List<ClickOncePayloadSigningOutput> additionalOutputs = new();
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
                        ? SelectPayloads(origin, payloads, options)
                        : Array.Empty<StagedPayload>();
                    if (policy == ClickOnceApplicationSigningPolicy.UpdateManifestOnly && context is not null)
                    {
                        foreach (StagedPayload payload in payloads.Where(payload =>
                            context.IsExplicitPayload(payload.Source.SourceIdentity)))
                        {
                            if (!coordinator.TryGetResult(payload.Source.SourceIdentity,
                                out Task<SigningOperationResult>? prerequisite))
                            {
                                throw new InvalidOperationException(
                                    $"The explicit payload operation for '{payload.Source.File.FullName}' was not registered before application signing.");
                            }

                            SigningOperationResult result = await coordinator.WaitAsync(prerequisite!);
                            result.MaterializePrimary(payload.UpdateFile);
                        }
                    }
                    if (selected.Length > 0)
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
                        Dictionary<string, (FileInfo File, SigningOperationResult Result)> materializations =
                            new(StringComparer.OrdinalIgnoreCase);
                        foreach ((StagedPayload payload, ClickOncePayloadSigningResult result) in results)
                        {
                            AddMaterialization(payload.UpdateFile, result.Primary);
                        }
                        foreach (ClickOncePayloadSigningOutput output in additionalOutputs)
                        {
                            AddMaterialization(output.Plan.Output, output.Result);
                        }
                        foreach ((FileInfo file, SigningOperationResult result) in materializations.Values)
                        {
                            result.MaterializePrimary(file);
                        }

                        void AddMaterialization(FileInfo file, SigningOperationResult result)
                        {
                            if (materializations.TryGetValue(file.FullName, out (FileInfo File, SigningOperationResult Result) existing))
                            {
                                if (!ReferenceEquals(existing.Result, result))
                                {
                                    throw new InvalidOperationException(
                                        "The payload handler returned different coordinated results for one destination.");
                                }
                                return;
                            }
                            materializations[file.FullName] = (file, result);
                        }
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

                if (!string.IsNullOrWhiteSpace(options.ApplicationName))
                {
                    manifest.AssemblyIdentity.Name = options.ApplicationName;
                }
                manifest.Write(session.ApplicationManifest.Destination);
                await SignManifestAsync(session.ApplicationManifest.Destination, options);

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

        private async Task<SigningOperationArtifact> SignAsIsOwnerAsync(
            SigningFile input,
            SignOptions options)
        {
            TemporaryDirectory directory = new(_stager.DirectoryService);
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
                        source.Position = 0;
                        using FileStream destination = copy.Create();
                        source.CopyTo(destination);
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

                copy.IsReadOnly = false;
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

        private static FileInfo BindExistingPublicationDestination(FileInfo destination, DirectoryInfo sourceRoot)
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

        private static FileInfo BindOriginalSource(FileInfo source, DirectoryInfo sourceRoot)
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

        private sealed record StagedPayload(ClickOncePayloadSigningRequest Request)
        {
            internal SigningFile Source => Request.Source;
            internal ClickOnceStagedFile File => Request.StagedFile;
            internal bool Handled { get; init; }
            internal FileInfo UpdateFile => File.FileInfoUpdateFile ?? File.Destination;
        }
    }
}
