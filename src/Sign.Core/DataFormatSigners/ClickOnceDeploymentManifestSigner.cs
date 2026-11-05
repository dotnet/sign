// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    // Dormant deployment pipeline, composed by a later dispatch layer.
    internal sealed class ClickOnceDeploymentManifestSigner
    {
        private readonly IServiceProvider _services;
        private readonly IClickOnceManifestReader _reader;
        private readonly ClickOnceDeploymentPublishLayoutResolver _resolver;
        private readonly ClickOnceNestedSigningHandler _nested;
        private readonly AsyncLocal<ClickOnceDeploymentSigningContext?> _context = new();

        internal ClickOnceDeploymentManifestSigner(
            IServiceProvider services,
            IClickOnceManifestReader? reader = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            _services = services;
            _reader = reader ?? new ClickOnceManifestReader();
            _resolver = new(_reader, new ClickOncePayloadResolver());
            _nested = new(this, _reader, _services.GetRequiredService<IAggregatingDataFormatSigner>(),
                _services.GetRequiredService<ILogger<IDataFormatSigner>>());
        }

        internal bool CanSign(SigningFile input) =>
            input.File.Extension.Equals(".application", StringComparison.OrdinalIgnoreCase) ||
            input.File.Extension.Equals(".vsto", StringComparison.OrdinalIgnoreCase) ||
            (input.File.Extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase) && _nested.IsApplication(input));

        internal Task<SigningOperationArtifact> SignInputOwnerAsync(
            SigningFile input,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            ClickOnceApplicationSigningPolicy policy,
            bool updateManifest) =>
            input.File.Extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase)
                ? _nested.SignApplicationOwnerAsync(input, options, coordinator, policy, updateManifest)
                : SignOwnerAsync(input, options, coordinator, policy, updateManifest);

        internal async Task ExecuteAsync(
            SigningOperationPlan plan,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            ClickOnceApplicationSigningPolicy policy = ClickOnceApplicationSigningPolicy.SignPayloads,
            bool updateManifest = true)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(coordinator);
            if (!Enum.IsDefined(policy))
            {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }
            using IDisposable scheduling = coordinator.UseScheduling(1);
            ClickOnceDeploymentSigningContext? preparedContext;
            using (IDisposable preflight = await coordinator.AcquireSchedulingLeaseAsync())
            {
                if (updateManifest)
                {
                    plan = new SigningOperationPlan(BindInput(plan.Source), plan.Output);
                }
                preparedContext = updateManifest
                    ? PrepareContext(new[] { plan }, Array.Empty<SigningFile>(), options, policy)
                    : null;
            }
            using ClickOnceDeploymentSigningContext? prepared = preparedContext;
            ClickOnceDeploymentSigningContext? previous = _context.Value;
            _context.Value = prepared;
            try
            {
                SigningOperationResult result = await coordinator.WaitAsync(coordinator.ExecuteArtifactAsync(
                    plan.Source.SourceIdentity,
                    () => SignOwnerAsync(plan.Source, options, coordinator, policy, updateManifest)));
                using IDisposable publication = await coordinator.AcquireSchedulingLeaseAsync();
                Publish(result, plan, updateManifest);
            }
            finally
            {
                _context.Value = previous;
            }
        }

        internal async Task ExecuteApplicationAsync(
            SigningOperationPlan plan,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            ClickOnceApplicationSigningPolicy policy = ClickOnceApplicationSigningPolicy.SignPayloads,
            bool updateManifest = true,
            ClickOnceApplicationSigningContext? context = null)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(coordinator);
            if (!Enum.IsDefined(policy))
            {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }
            using IDisposable scheduling = coordinator.UseScheduling(1);
            ClickOnceDeploymentSigningContext? preparedContext;
            using (IDisposable preflight = await coordinator.AcquireSchedulingLeaseAsync())
            {
                if (updateManifest)
                {
                    plan = new SigningOperationPlan(BindInput(plan.Source), plan.Output);
                }
                preparedContext = updateManifest && policy == ClickOnceApplicationSigningPolicy.SignPayloads
                    ? PrepareContext(Array.Empty<SigningOperationPlan>(), new[] { plan.Source }, options, policy)
                    : null;
            }
            using ClickOnceDeploymentSigningContext? prepared = preparedContext;
            ClickOnceDeploymentSigningContext? previous = _context.Value;
            _context.Value = prepared;
            try
            {
                SigningOperationResult result = await coordinator.WaitAsync(coordinator.ExecuteArtifactAsync(
                    plan.Source.SourceIdentity,
                    () => _nested.SignApplicationOwnerAsync(plan.Source, options, coordinator, policy, updateManifest, context)));
                using IDisposable publication = await coordinator.AcquireSchedulingLeaseAsync();
                Publish(result, plan, updateManifest);
            }
            finally
            {
                _context.Value = previous;
            }
        }

        internal async Task ExecuteAsync(
            IReadOnlyList<SigningOperationPlan> plans,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            int maxConcurrency,
            ClickOnceApplicationSigningPolicy policy = ClickOnceApplicationSigningPolicy.SignPayloads,
            bool updateManifest = true,
            bool insideOperation = false)
        {
            ArgumentNullException.ThrowIfNull(plans);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(coordinator);
            if (maxConcurrency < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
            }
            if (!Enum.IsDefined(policy))
            {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }
            using IDisposable? scheduling = insideOperation ? null : coordinator.UseScheduling(maxConcurrency);
            SigningOperationPlan[] applications;
            SigningOperationPlan[] deployments;
            HashSet<SigningSourceIdentity> supportedOrdinary;
            ClickOnceDeploymentSigningContext? preparedContext;
            using (IDisposable? preflight = insideOperation ? null : await coordinator.AcquireSchedulingLeaseAsync())
            {
                plans = plans.Select(plan => updateManifest && (IsDeployment(plan) || IsApplication(plan))
                    ? new SigningOperationPlan(BindInput(plan.Source), plan.Output)
                    : plan).ToArray();
                applications = plans.Where(IsApplication).ToArray();
                deployments = plans.Where(IsDeployment).ToArray();
                IAggregatingDataFormatSigner aggregate = _services.GetRequiredService<IAggregatingDataFormatSigner>();
                supportedOrdinary = plans.Where(plan => !IsDeployment(plan) && !IsApplication(plan) &&
                    aggregate.HasSigningWork(plan.Source.File, options))
                    .Select(plan => plan.Source.SourceIdentity).ToHashSet();
                preparedContext = updateManifest
                    ? PrepareContext(deployments,
                        applications.Select(plan => plan.Source).ToArray(),
                        options, policy)
                    : null;
            }
            using ClickOnceDeploymentSigningContext? prepared = preparedContext;
            ParallelOptions parallel = new() { MaxDegreeOfParallelism = maxConcurrency };
            ClickOnceDeploymentSigningContext? previous = _context.Value;
            _context.Value = prepared;
            try
            {
                SigningOperationExecutor ordinary = new(
                    _services.GetRequiredService<IAggregatingDataFormatSigner>(),
                    _services.GetRequiredService<IDirectoryService>(),
                    _services.GetRequiredService<ILogger<ISigner>>(), coordinator);
                SigningOperationPlan[] ordinaryPlans = plans.Where(plan =>
                    !IsDeployment(plan) && !IsApplication(plan)).ToArray();
                Dictionary<SigningOperationPlan, SigningOperationResult> ordinaryResults = new();
                await RunPhaseAsync(ordinaryPlans, async plan =>
                {
                    if (supportedOrdinary.Contains(plan.Source.SourceIdentity))
                    {
                        SigningOperationResult result = await ordinary.ExecuteSigningResultAsync(plan, options);
                        lock (ordinaryResults)
                        {
                            ordinaryResults.Add(plan, result);
                        }
                    }
                });
                using (IDisposable? ordinaryPublication = insideOperation ? null : await coordinator.AcquireSchedulingLeaseAsync())
                {
                    foreach (SigningOperationPlan plan in ordinaryPlans)
                    {
                        if (ordinaryResults.TryGetValue(plan, out SigningOperationResult? result))
                        {
                            result.Materialize(plan.Output);
                        }
                        else
                        {
                            SigningOperationExecutor.CopyWithoutSigning(plan);
                        }
                    }
                }
                ClickOnceApplicationSigningContext context = new(plans
                    .Where(plan => IsDeployment(plan) || IsApplication(plan) ||
                        supportedOrdinary.Contains(plan.Source.SourceIdentity))
                    .Select(plan => plan.Source.SourceIdentity));
                TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource deploymentReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<SigningOperationResult>[] applicationTasks = applications.Select(plan =>
                    coordinator.ExecuteArtifactAsync(plan.Source.SourceIdentity, async () =>
                    {
                        await ready.Task;
                        return await _nested.SignApplicationOwnerAsync(
                            plan.Source, options, coordinator, policy, updateManifest, context);
                    })).ToArray();
                // Register every explicit prerequisite before starting graph work.
                // Owners release their scheduling permits during actual prerequisite
                // waits, so a required owner cannot remain queued behind its waiter.
                Task<SigningOperationResult>[] RegisterDeployments() => deployments.Select(plan =>
                    coordinator.ExecuteArtifactAsync(plan.Source.SourceIdentity, async () =>
                    {
                        await deploymentReady.Task;
                        return await SignOwnerAsync(plan.Source, options, coordinator, policy, updateManifest);
                    })).ToArray();
                Task<SigningOperationResult>[] deploymentTasks = updateManifest
                    ? RegisterDeployments()
                    : Array.Empty<Task<SigningOperationResult>>();
                ready.SetResult();
                if (updateManifest)
                {
                    deploymentReady.SetResult();
                    await coordinator.WaitAllForReadyOwnerAsync(applicationTasks.Concat(deploymentTasks));
                }
                SigningOperationResult[] applicationResults =
                    await coordinator.WaitAllForReadyOwnerAsync(applicationTasks);
                if (!updateManifest)
                {
                    deploymentTasks = RegisterDeployments();
                    deploymentReady.SetResult();
                }
                SigningOperationResult[] results = await coordinator.WaitAllForReadyOwnerAsync(deploymentTasks);
                using IDisposable? publication = insideOperation ? null : await coordinator.AcquireSchedulingLeaseAsync();
                for (int index = 0; index < applications.Length; ++index)
                {
                    if (updateManifest && policy == ClickOnceApplicationSigningPolicy.SignPayloads &&
                        deployments.Concat(applications).Any(owner => _nested.GetOutputs(owner.Source.SourceIdentity)
                            .Any(output => output.Source.SourceIdentity.Equals(applications[index].Source.SourceIdentity) &&
                                string.Equals(output.Source.File.FullName, applications[index].Source.File.FullName,
                                    StringComparison.Ordinal) &&
                                ReferenceEquals(output.Result, applicationResults[index]) &&
                                FileInfoComparer.Instance.Equals(
                                    new FileInfo(Path.Combine(owner.Output.DirectoryName!, output.RelativePath)),
                                    applications[index].Output))))
                    {
                        continue;
                    }
                    Publish(applicationResults[index], applications[index], updateManifest);
                }
                // Shared application/payload outputs must not be replaced while
                // another deployment still reads them or publishes the same path.
                for (int index = 0; index < deployments.Length; ++index)
                {
                    Publish(results[index], deployments[index], updateManifest);
                }
            }
            finally
            {
                _context.Value = previous;
            }

            async Task RunPhaseAsync(IEnumerable<SigningOperationPlan> phase, Func<SigningOperationPlan, Task> execute)
            {
                if (insideOperation)
                {
                    // A nested batch shares one owner permit. Its ready waits
                    // must not overlap; child owners retain scheduler parallelism.
                    foreach (SigningOperationPlan plan in phase)
                    {
                        await execute(plan);
                    }
                }
                else
                {
                    await Parallel.ForEachAsync(phase, parallel, (plan, _) => new ValueTask(execute(plan)));
                }
            }

            bool IsApplication(SigningOperationPlan plan)
            {
                FileInfo file = plan.Source.File;
                if (!file.Extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                return _nested.IsApplication(plan.Source);
            }
            static bool IsDeployment(SigningOperationPlan plan) =>
                plan.Source.File.Extension.Equals(".application", StringComparison.OrdinalIgnoreCase) ||
                plan.Source.File.Extension.Equals(".vsto", StringComparison.OrdinalIgnoreCase);
        }

        internal async Task<SigningOperationArtifact> SignOwnerAsync(
            SigningFile input,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            ClickOnceApplicationSigningPolicy policy = ClickOnceApplicationSigningPolicy.SignPayloads,
            bool updateManifest = true)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(coordinator);
            if (!Enum.IsDefined(policy))
            {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }
            try
            {
                if (!updateManifest)
                {
                    return await SignWithoutUpdatingAsync(input.File, options);
                }
                return await SignDeploymentAsync(input, options, coordinator, policy);
            }
            catch (SigningException exception)
            {
                _services.GetRequiredService<ILogger<IDataFormatSigner>>()
                    .LogError(exception, "{ManifestMessage}", exception.Message);
                throw;
            }
            catch (ClickOncePublishLayoutResolutionException exception)
            {
                ILogger<IDataFormatSigner> logger = _services.GetRequiredService<ILogger<IDataFormatSigner>>();
                LogDiagnostics(logger, exception.Diagnostics);
                logger.LogError(exception, "{ManifestMessage}", exception.Message);
                throw;
            }
        }

        internal ClickOnceApplicationManifestSigner CreateApplicationSigner() =>
            new(
                new ClickOnceApplicationPublishLayoutResolver(_reader, new ClickOncePayloadResolver()),
                new ClickOncePublishLayoutStager(_services.GetRequiredService<IDirectoryService>()),
                _services.GetRequiredService<IAggregatingDataFormatSigner>(),
                _services.GetRequiredService<IFileMatcher>(),
                _services.GetRequiredService<IManifestSigner>(),
                _services.GetRequiredService<ICertificateProvider>(),
                _services.GetRequiredService<ISignatureAlgorithmProvider>(),
                _services.GetRequiredService<ILogger<IDataFormatSigner>>(), _nested);

        internal ClickOnceDeploymentSigningContext.ApplicationContext? GetApplicationContext(
            SigningSourceIdentity identity) => _context.Value?.GetApplication(identity);

        private ClickOnceDeploymentSigningContext PrepareContext(
            IReadOnlyList<SigningOperationPlan> deployments,
            IReadOnlyList<SigningFile> applications,
            SignOptions options,
            ClickOnceApplicationSigningPolicy policy)
        {
            try
            {
                return ClickOnceDeploymentSigningContext.Create(deployments, applications,
                    _resolver, new(_reader, new ClickOncePayloadResolver()),
                    policy == ClickOnceApplicationSigningPolicy.SignPayloads,
                    (file, directory) => IsSelected(file, directory, options), _nested.IsApplication);
            }
            catch (SigningException exception)
            {
                _services.GetRequiredService<ILogger<IDataFormatSigner>>()
                    .LogError(exception, "{ManifestMessage}", exception.Message);
                throw;
            }
            catch (ClickOncePublishLayoutResolutionException exception)
            {
                ILogger<IDataFormatSigner> logger = _services.GetRequiredService<ILogger<IDataFormatSigner>>();
                LogDiagnostics(logger, exception.Diagnostics);
                logger.LogError(exception, "{ManifestMessage}", exception.Message);
                throw;
            }
        }

        private static void Publish(SigningOperationResult result, SigningOperationPlan plan, bool updateManifest)
        {
            if (updateManifest && ClickOnceApplicationManifestSigner.PublishesBesideInput(
                plan.Source.File.Directory!, plan.Output.Directory!))
            {
                result.Materialize(plan.Output, file =>
                    ClickOnceApplicationManifestSigner.BindExistingPublicationDestination(file, plan.Output.Directory!));
            }

            else
            {
                result.Materialize(plan.Output);
            }
        }

        private static SigningFile BindInput(SigningFile input) =>
            input.File.Exists
                ? input.GetSibling(ClickOnceApplicationManifestSigner.BindOriginalSource(
                    input.File, input.File.Directory!), input.File.Directory!)
                : input;

        private async Task<SigningOperationArtifact> SignWithoutUpdatingAsync(
            FileInfo source, SignOptions options)
        {
            using (IDeployManifest manifest = _resolver.ReadDeployManifest(source))
            {
                LogDiagnostics(_services.GetRequiredService<ILogger<IDataFormatSigner>>(), manifest.Diagnostics);
                TemporaryDirectory directory = new(_services.GetRequiredService<IDirectoryService>());
                try
                {
                    FileInfo staged = source.CopyTo(Path.Combine(directory.Directory.FullName, source.Name));
                    staged.IsReadOnly = false;
                    await SignManifestAsync(staged, options);
                    return SigningOperationArtifact.OwnedSingle(staged, directory);
                }
                catch (Exception exception)
                {
                    DisposeAfterFailure(directory, exception);
                    throw;
                }
            }
        }

        private async Task<SigningOperationArtifact> SignDeploymentAsync(
            SigningFile input,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            ClickOnceApplicationSigningPolicy policy)
        {
            bool signDependencies = policy == ClickOnceApplicationSigningPolicy.SignPayloads;
            ResolvedClickOncePublishLayout layout = _context.Value?.GetDeployment(input.SourceIdentity) ??
                _resolver.Resolve(input.File, resolvePayloads: signDependencies);
            using IDeployManifest deployment = layout.Deployment!.Manifest;
            using IApplicationManifest application = layout.Application.Manifest;
            ILogger<IDataFormatSigner> logger = _services.GetRequiredService<ILogger<IDataFormatSigner>>();
            LogDiagnostics(logger, layout.Diagnostics);
            SigningFile applicationInput = input.GetSibling(
                ClickOnceApplicationManifestSigner.BindOriginalSource(layout.Application.Source, input.File.Directory!),
                input.File.Directory!);
            Task<SigningOperationResult>? applicationTask;
            if (signDependencies)
            {
                ResolvedClickOncePublishLayout applicationLayout = new(
                    deployment: null, layout.Application,
                    Array.Empty<ResolvedClickOnceAdjacentExecutable>(), layout.Diagnostics);
                applicationTask = coordinator.ExecuteArtifactAsync(
                    applicationInput.SourceIdentity,
                    () => _nested.SignApplicationOwnerAsync(
                        applicationInput, options, coordinator,
                        policy, layout: applicationLayout, layoutOrigin: input));
            }
            else
            {
                coordinator.TryGetResult(applicationInput.SourceIdentity, out applicationTask);
            }
            SigningOperationResult? applicationResult = applicationTask is null
                ? null
                : await coordinator.WaitForReadyOwnerAsync(applicationTask);
            ClickOncePublishLayoutStager stager = new(_services.GetRequiredService<IDirectoryService>());
            ClickOnceStagingSession session = stager.Stage(layout, payload =>
                ClickOnceApplicationManifestSigner.BindOriginalSource(payload.Source, input.File.Directory!));
            try
            {
                FileInfo stagedDeployment = session.DeploymentManifest!.Destination;
                FileInfo stagedApplication = session.ApplicationManifest.Destination;
                stagedDeployment.IsReadOnly = false;
                if (signDependencies)
                {
                    stagedApplication.IsReadOnly = false;
                }
                List<FileInfo> published = new() { stagedDeployment };
                List<ClickOnceGraphOutput> graphOutputs = new();
                if (applicationResult is not null)
                {
                    SigningOperationResult result = applicationResult;
                    if (signDependencies)
                    {
                        List<ClickOncePayloadSigningOutput> outputs = new();
                        Dictionary<string, bool> mappingRoles = new(StringComparer.OrdinalIgnoreCase);
                        foreach (ClickOnceGraphOutput child in _nested.GetOutputs(applicationInput.SourceIdentity))
                        {
                            ClickOnceStagedFile? direct = session.Payloads.Values.FirstOrDefault(file =>
                                string.Equals(file.Source.FullName, child.Source.File.FullName, StringComparison.Ordinal));
                            FileInfo destination = direct is null
                                ? new(Path.Combine(stagedApplication.DirectoryName!, child.RelativePath))
                                : new(Path.Combine(session.Directory.FullName,
                                    Path.GetRelativePath(input.File.DirectoryName!, child.Source.File.FullName)));
                            if (child.PublicationOrigin is not null)
                            {
                                ClickOnceStagedFile? origin = session.Payloads.Values.FirstOrDefault(file =>
                                    string.Equals(file.Source.FullName, child.PublicationOrigin.File.FullName, StringComparison.Ordinal) &&
                                    input.GetSibling(file.Source, input.File.Directory!).SourceIdentity.Equals(
                                        child.PublicationOrigin.SourceIdentity));
                                if (origin is null || child.OriginRelativePath is null)
                                {
                                    throw new InvalidOperationException("A nested output has no validated publication origin.");
                                }
                                FileInfo primaryDestination = new(Path.Combine(session.Directory.FullName,
                                    Path.GetRelativePath(input.File.DirectoryName!, origin.Source.FullName)));
                                destination = new(Path.Combine(primaryDestination.DirectoryName!, child.OriginRelativePath));
                            }
                            ClickOnceStagedFile? adjacent = session.AdjacentExecutables.Values.FirstOrDefault(file =>
                                FileInfoComparer.Instance.Equals(file.Destination, destination) &&
                                string.Equals(ClickOnceApplicationManifestSigner.BindOriginalSource(
                                    file.Source, input.File.Directory!).FullName, child.Source.File.FullName, StringComparison.Ordinal));
                            if (adjacent is not null &&
                                IsSelected(child.Source.File, input.File.Directory!, options) &&
                                _services.GetRequiredService<IAggregatingDataFormatSigner>().HasSigningWork(adjacent.Destination, options) &&
                                coordinator.TryGetResult(child.Source.SourceIdentity, out Task<SigningOperationResult>? adjacentResult) &&
                                adjacentResult!.IsCompletedSuccessfully &&
                                ReferenceEquals(await coordinator.WaitForReadyOwnerAsync(adjacentResult), child.Result))
                            {
                                continue;
                            }
                            if (mappingRoles.TryGetValue(destination.FullName, out bool mapped) && mapped != child.IsMapped)
                            {
                                throw new InvalidOperationException("Deployment outputs contain conflicting mapping roles.");
                            }
                            mappingRoles[destination.FullName] = child.IsMapped;
                            outputs.Add(new(new SigningOperationPlan(child.Source, destination), child.Result));
                        }
                        ClickOncePayloadSigningOutput[] extra = outputs.Where(output =>
                            !session.Payloads.Values.Any(file =>
                                FileInfoComparer.Instance.Equals(file.Destination, output.Plan.Output) &&
                                string.Equals(file.Source.FullName, output.Plan.Source.File.FullName, StringComparison.Ordinal)))
                            .ToArray();
                        stager.ValidateAdditionalOutputs(layout, session, extra.Select(output => output.Plan).ToArray());
                        Dictionary<string, ClickOncePayloadSigningOutput> destinations = new(StringComparer.OrdinalIgnoreCase);
                        foreach (ClickOncePayloadSigningOutput output in outputs)
                        {
                            if (destinations.TryGetValue(output.Plan.Output.FullName, out ClickOncePayloadSigningOutput? existing))
                            {
                                if (!existing.Plan.Source.SourceIdentity.Equals(output.Plan.Source.SourceIdentity) ||
                                    !ReferenceEquals(existing.Result, output.Result))
                                {
                                    throw new InvalidOperationException("Deployment outputs contain conflicting coordinated results.");
                                }
                                continue;
                            }
                            destinations.Add(output.Plan.Output.FullName, output);
                        }
                        result.MaterializePrimary(stagedApplication);
                        foreach (ClickOncePayloadSigningOutput output in destinations.Values)
                        {
                            output.Result.MaterializePrimary(output.Plan.Output);
                            published.Add(output.Plan.Output);
                            graphOutputs.Add(new(output.Plan.Source,
                                Path.GetRelativePath(session.Directory.FullName, output.Plan.Output.FullName), output.Result,
                                mappingRoles[output.Plan.Output.FullName]));
                        }
                        published.Add(stagedApplication);
                        graphOutputs.Add(new(applicationInput,
                            Path.GetRelativePath(session.Directory.FullName, stagedApplication.FullName), result));
                    }
                    else
                    {
                        result.MaterializePrimary(stagedApplication);
                    }
                }

                using (FileStream stream = stagedApplication.OpenRead())
                {
                    if (!_reader.TryReadApplicationManifest(stream, out IApplicationManifest? signedApplication))
                    {
                        throw new SigningException(string.Format(CultureInfo.CurrentCulture,
                            Resources.ClickOnceDeploymentManifestReferencedApplicationWrongType,
                            stagedApplication.FullName, input.File.FullName));
                    }
                    using (signedApplication)
                    {
                        deployment.EntryPoint!.AssemblyIdentity = signedApplication.AssemblyIdentity;
                    }
                }
                deployment.EntryPoint!.ResolvedPath = stagedApplication.FullName;
                using X509Certificate2 certificate =
                    await _services.GetRequiredService<ICertificateProvider>().GetCertificateAsync();
                using RSA rsa = await _services.GetRequiredService<ISignatureAlgorithmProvider>().GetRsaAsync();
                deployment.ApplySigningOptions(options, certificate.SubjectName.Name.Replace("\"", ""));
                int previousCount = deployment.Diagnostics.Count;
                bool hasErrors = false;
                try
                {
                    deployment.UpdateFileInfo();
                }
                finally
                {
                    hasErrors = LogDiagnostics(logger, deployment.Diagnostics.Skip(previousCount));
                }
                if (hasErrors)
                {
                    throw new SigningException(string.Join(Environment.NewLine,
                        deployment.Diagnostics.Skip(previousCount)
                            .Where(diagnostic => diagnostic.Type == OutputMessageType.Error)
                            .Select(diagnostic => diagnostic.Text)));
                }
                deployment.Write(stagedDeployment);
                _services.GetRequiredService<IManifestSigner>().Sign(stagedDeployment, certificate, rsa, options);

                if (signDependencies)
                {
                    IAggregatingDataFormatSigner signer = _services.GetRequiredService<IAggregatingDataFormatSigner>();
                    foreach (KeyValuePair<ResolvedClickOnceAdjacentExecutable, ClickOnceStagedFile> pair in session.AdjacentExecutables)
                    {
                        FileInfo adjacentSource = ClickOnceApplicationManifestSigner.BindOriginalSource(
                            pair.Key.Source, input.File.Directory!);
                        if (IsSelected(adjacentSource, input.File.Directory!, options) &&
                            signer.HasSigningWork(pair.Value.Destination, options))
                        {
                            SigningFile adjacent = input.GetSibling(adjacentSource, input.File.Directory!);
                            await signer.SignAsync(
                                new[] { adjacent.WithFile(pair.Value.Destination) },
                                options, coordinator);
                            published.Add(pair.Value.Destination);
                            if (!coordinator.TryGetResult(adjacent.SourceIdentity, out Task<SigningOperationResult>? adjacentTask))
                            {
                                throw new InvalidOperationException("A selected adjacent executable has no coordinated result.");
                            }
                            graphOutputs.Add(new(adjacent,
                                Path.GetRelativePath(session.Directory.FullName, pair.Value.Destination.FullName),
                                await coordinator.WaitForReadyOwnerAsync(adjacentTask!)));
                        }
                    }
                }
                _nested.SetOutputs(input.SourceIdentity, graphOutputs);
                return SigningOperationArtifact.OwnedLayout(
                    stagedDeployment, session.Directory, published, session);
            }
            catch (Exception exception)
            {
                DisposeAfterFailure(session, exception);
                throw;
            }
        }

        private bool IsSelected(FileInfo file, DirectoryInfo sourceDirectory, SignOptions options)
        {
            IFileMatcher matcher = _services.GetRequiredService<IFileMatcher>();
            DirectoryInfoWrapper directory = new(sourceDirectory);
            return (options.Matcher is null ||
                    matcher.EnumerateMatches(directory, options.Matcher).Contains(file, FileInfoComparer.Instance)) &&
                (options.AntiMatcher is null ||
                    !matcher.EnumerateMatches(directory, options.AntiMatcher).Contains(file, FileInfoComparer.Instance));
        }

        private async Task SignManifestAsync(FileInfo file, SignOptions options)
        {
            using X509Certificate2 certificate = await _services.GetRequiredService<ICertificateProvider>().GetCertificateAsync();
            using RSA rsa = await _services.GetRequiredService<ISignatureAlgorithmProvider>().GetRsaAsync();
            _services.GetRequiredService<IManifestSigner>().Sign(file, certificate, rsa, options);
        }

        private static bool LogDiagnostics(ILogger logger, IEnumerable<ClickOnceManifestDiagnostic> diagnostics)
        {
            bool hasErrors = false;
            foreach (ClickOnceManifestDiagnostic diagnostic in diagnostics)
            {
                bool isError = diagnostic.Type == OutputMessageType.Error;
                logger.Log(isError ? LogLevel.Error : LogLevel.Warning, "{ManifestMessage}", diagnostic.Text);
                hasErrors |= isError;
            }
            return hasErrors;
        }

        private static void DisposeAfterFailure(IDisposable resource, Exception exception)
        {
            try
            {
                resource.Dispose();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(exception, cleanupException);
            }
        }
    }
}
