// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class Signer : ISigner
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ISigner> _logger;

        // Dependency injection requires a public constructor.
        public Signer(IServiceProvider serviceProvider, ILogger<ISigner> logger)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task<int> SignAsync(
            IReadOnlyList<FileInfo> inputFiles,
            string? outputFile,
            FileInfo? fileList,
            bool recurseContainers,
            DirectoryInfo baseDirectory,
            string? applicationName,
            string? publisherName,
            string? description,
            Uri? descriptionUrl,
            Uri timestampUrl,
            int maxConcurrency,
            HashAlgorithmName fileHashAlgorithm,
            HashAlgorithmName timestampHashAlgorithm)
        {
            IAggregatingDataFormatSigner signer = _serviceProvider.GetRequiredService<IAggregatingDataFormatSigner>();
            IDirectoryService directoryService = _serviceProvider.GetRequiredService<IDirectoryService>();
            IReadOnlyList<SigningOperationGroupExecution> groupExecutions =
                Array.Empty<SigningOperationGroupExecution>();
            ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = maxConcurrency };

            Matcher? matcher = null;
            Matcher? antiMatcher = null;

            if (fileList is not null)
            {
                IFileListReader fileListReader = _serviceProvider.GetRequiredService<IFileListReader>();

                using (FileStream stream = fileList.OpenRead())
                using (StreamReader reader = new(stream))
                {
                    fileListReader.Read(reader, out matcher, out antiMatcher);
                }
            }

            ICertificateProvider certificateProvider = _serviceProvider.GetRequiredService<ICertificateProvider>();

            SignOptions signOptions = new(
                applicationName,
                publisherName,
                description,
                descriptionUrl,
                fileHashAlgorithm,
                timestampHashAlgorithm,
                timestampUrl,
                matcher,
                antiMatcher,
                recurseContainers);

            try
            {
                using (X509Certificate2 certificate = await certificateProvider.GetCertificateAsync())
                {
                    ICertificateVerifier certificateVerifier = _serviceProvider.GetRequiredService<ICertificateVerifier>();

                    certificateVerifier.Verify(certificate);
                }

                // Plan the outputs from all of the inputs first, so that filtering by the
                // file list below can never change where a remaining input is written.
                IReadOnlyList<SigningOperationPlan> plans =
                    SigningOperationPlanner.Create(
                        inputFiles,
                        outputFile,
                        baseDirectory)
                    .OrderBy(plan => signer.SigningOrder(plan.Source.File))
                    .ToArray();

                // Apply file-list filtering to standalone input files too,
                // consistent with how container and ClickOnce contents are filtered.
                // Only the given inputs are matched, in memory. The base directory is not scanned,
                // so unrelated (for example unreadable) folders are never touched.
                if (matcher is not null || antiMatcher is not null)
                {
                    IContainerProvider containerProvider = _serviceProvider.GetRequiredService<IContainerProvider>();

                    plans = plans
                        .Where(plan =>
                        {
                            // The file list applies to the contents of containers and ClickOnce
                            // deployments, so those inputs are not filtered at the top level.
                            if (IsContainerOrClickOnceDeployment(containerProvider, plan.Source.File))
                            {
                                return true;
                            }

                            // Match the path relative to the base directory, so that the result
                            // does not depend on how the base directory part is cased.
                            string[] candidate = [Path.GetRelativePath(baseDirectory.FullName, plan.Source.File.FullName)];

                            bool isIncluded = matcher is null
                                || matcher.Match(baseDirectory.FullName, candidate).HasMatches;
                            bool isExcluded = antiMatcher is not null
                                && antiMatcher.Match(baseDirectory.FullName, candidate).HasMatches;

                            return isIncluded && !isExcluded;
                        })
                        .ToList();
                }

                if (_serviceProvider.GetService<ClickOnceSigningConfiguration>() is { Version: 2 })
                {
                    SigningOperationCoordinator coordinator = new(directoryService);
                    try
                    {
                        await _serviceProvider.GetRequiredService<ClickOnceVersion2Signer>()
                            .ExecuteAsync(plans, signOptions, coordinator);
                    }
                    finally
                    {
                        await DisposeCoordinatorAsync(coordinator);
                    }
                    return ExitCode.Success;
                }
                IReadOnlyList<IReadOnlyList<SigningOperationPlan>>
                    workItems = CreateWorkItems(
                        plans,
                        signer.IsSequentialCoordinationRequired);
                IReadOnlyList<SigningOperationGroup>
                    operationGroups = SigningOperationScope.CreateGroups(
                        plans,
                        signer.IsSequentialCoordinationRequired);
                groupExecutions = operationGroups
                    .Select(
                        group => new SigningOperationGroupExecution(
                            group.Plans,
                            group.Scope,
                            signer,
                            directoryService,
                            _logger))
                    .ToList();
                IReadOnlyDictionary<
                    SigningOperationPlan,
                    SigningOperationGroupExecution> groupsByPlan =
                        groupExecutions
                            .SelectMany(
                                execution => execution.Plans.Select(
                                    plan => (plan, execution)))
                            .ToDictionary(
                                pair => pair.plan,
                                pair => pair.execution);

                async ValueTask ExecuteWorkItemAsync(
                    IReadOnlyList<SigningOperationPlan> workItem,
                    CancellationToken token)
                {
                    foreach (SigningOperationPlan plan in workItem)
                    {
                        Stopwatch sw = Stopwatch.StartNew();

                        _logger.LogInformation(
                            Resources.SubmittingFileForSigning,
                            plan.Source.File.FullName);

                        SigningOperationGroupExecution execution =
                            groupsByPlan[plan];

                        try
                        {
                            await execution.ExecuteAsync(
                                plan,
                                signOptions);
                        }
                        finally
                        {
                            await execution.CompletePlanAsync(
                                DisposeCoordinatorAsync);
                        }

                        _logger.LogInformation(
                            Resources.SigningSucceededWithTimeElapsed,
                            sw.ElapsedMilliseconds);
                    }
                }

                if (string.IsNullOrWhiteSpace(outputFile))
                {
                    // ClickOnce stages its layout from the source directory
                    // and hashes whatever bytes it finds there. When signing
                    // in place, every input that does not require sequential
                    // coordination must therefore finish signing before any
                    // input that does (ClickOnce) stages its layout. With an
                    // output location, signed results never reach the source
                    // directory, so work items are instead started in input
                    // order (with concurrency above 1 they overlap), except
                    // for inputs without signing work.
                    await Parallel.ForEachAsync(
                        workItems.Where(
                            workItem => !signer.IsSequentialCoordinationRequired(
                                workItem[0].Source.File)),
                        parallelOptions,
                        ExecuteWorkItemAsync);
                    await Parallel.ForEachAsync(
                        workItems.Where(
                            workItem => signer.IsSequentialCoordinationRequired(
                                workItem[0].Source.File)),
                        parallelOptions,
                        ExecuteWorkItemAsync);
                }
                else
                {
                    // An input without signing work is copied unchanged, but
                    // its output can be a file that another input signs,
                    // such as a ClickOnce application manifest. Copies are
                    // therefore written before any signed results.
                    ILookup<bool, IReadOnlyList<SigningOperationPlan>>
                        workItemsBySigningWork = workItems.ToLookup(
                            workItem => workItem.Any(
                                plan => signer.HasSigningWork(
                                    plan.Source.File,
                                    signOptions)));

                    await Parallel.ForEachAsync(
                        workItemsBySigningWork[false],
                        parallelOptions,
                        ExecuteWorkItemAsync);
                    await Parallel.ForEachAsync(
                        workItemsBySigningWork[true],
                        parallelOptions,
                        ExecuteWorkItemAsync);
                }
            }
            catch (AuthenticationException e)
            {
                _logger.LogError(e, e.Message);
                return ExitCode.Failed;
            }
            catch (SigningException)
            {
                _logger.LogError(Resources.SigningFailedAfterAllAttempts);
                return ExitCode.Failed;
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
                return ExitCode.Failed;
            }
            finally
            {
                foreach (
                    SigningOperationGroupExecution execution
                    in groupExecutions)
                {
                    await execution.DisposeAsync(
                        DisposeCoordinatorAsync);
                }
            }

            return ExitCode.Success;
        }

        private static bool IsContainerOrClickOnceDeployment(IContainerProvider containerProvider, FileInfo file)
        {
            return containerProvider.IsAppxBundleContainer(file)
                || containerProvider.IsAppxContainer(file)
                || containerProvider.IsNuGetContainer(file)
                || containerProvider.IsZipContainer(file)
                || ".vsto".Equals(file.Extension, StringComparison.OrdinalIgnoreCase)
                || ".application".Equals(file.Extension, StringComparison.OrdinalIgnoreCase);
        }

        private async Task DisposeCoordinatorAsync(
            SigningOperationCoordinator coordinator)
        {
            try
            {
                await coordinator.DisposeAsync();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    exception.Message);
            }
        }

        private sealed class SigningOperationGroupExecution
        {
            private readonly IDirectoryService _directoryService;
            private readonly object _gate = new();
            private readonly ILogger<ISigner> _logger;
            private readonly SigningOperationScope _scope;
            private readonly IAggregatingDataFormatSigner _signer;
            private SigningOperationCoordinator? _coordinator;
            private Task? _disposal;
            private SigningOperationExecutor? _executor;
            private int _remainingPlanCount;

            internal SigningOperationGroupExecution(
                IReadOnlyList<SigningOperationPlan> plans,
                SigningOperationScope scope,
                IAggregatingDataFormatSigner signer,
                IDirectoryService directoryService,
                ILogger<ISigner> logger)
            {
                Plans = plans;
                _scope = scope;
                _signer = signer;
                _directoryService = directoryService;
                _logger = logger;
                _remainingPlanCount = plans.Count;
            }

            internal IReadOnlyList<SigningOperationPlan> Plans { get; }

            internal async Task ExecuteAsync(
                SigningOperationPlan plan,
                SignOptions options)
            {
                ArgumentNullException.ThrowIfNull(plan, nameof(plan));
                ArgumentNullException.ThrowIfNull(options, nameof(options));

                if (!_signer.HasSigningWork(plan.Source.File, options))
                {
                    SigningOperationExecutor.CopyWithoutSigning(plan);

                    return;
                }

                await GetExecutor().ExecuteSigningAsync(plan, options);
            }

            private SigningOperationExecutor GetExecutor()
            {
                lock (_gate)
                {
                    if (_executor is null)
                    {
                        _coordinator = new SigningOperationCoordinator(
                            _directoryService,
                            _scope);
                        _executor = new SigningOperationExecutor(
                            _signer,
                            _directoryService,
                            _logger,
                            _coordinator);
                    }

                    return _executor;
                }
            }

            internal Task CompletePlanAsync(
                Func<SigningOperationCoordinator, Task> dispose)
            {
                return Interlocked.Decrement(
                    ref _remainingPlanCount) == 0
                        ? DisposeAsync(dispose)
                        : Task.CompletedTask;
            }

            internal Task DisposeAsync(
                Func<SigningOperationCoordinator, Task> dispose)
            {
                lock (_gate)
                {
                    if (_coordinator is null)
                    {
                        return Task.CompletedTask;
                    }

                    return _disposal ??= dispose(_coordinator);
                }
            }
        }

        // Work items are in input order; they are started in that order, but
        // with concurrency above 1 they overlap. Non-ClickOnce inputs that
        // publish to the same output form one sequential work item. Inputs
        // that require sequential coordination (ClickOnce) form one
        // sequential work item per outermost such input directory, positioned
        // at its first input.
        // Directories are compared by path text only. A ClickOnce operation
        // waits on the deployment manifests it discovers in its directory
        // tree, so running inputs whose trees overlap concurrently can form a
        // wait cycle.
        internal static IReadOnlyList<IReadOnlyList<SigningOperationPlan>>
            CreateWorkItems(
                IReadOnlyList<SigningOperationPlan> plans,
                Func<FileInfo, bool> requiresSequentialCoordination)
        {
            List<IReadOnlyList<SigningOperationPlan>> workItems = new();
            Dictionary<string, List<SigningOperationPlan>> outputGroups =
                new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<SigningOperationPlan>> sequentialGroups =
                new(StringComparer.OrdinalIgnoreCase);
            List<string> sequentialDirectories = plans
                .Where(
                    plan => requiresSequentialCoordination(plan.Source.File))
                .Select(
                    plan => PathContainment.GetFullDirectoryPath(
                        plan.Source.File.DirectoryName!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (SigningOperationPlan plan in plans)
            {
                if (!requiresSequentialCoordination(plan.Source.File))
                {
                    string output = Path.GetFullPath(
                        plan.Output.FullName);

                    if (!outputGroups.TryGetValue(
                        output,
                        out List<SigningOperationPlan>? outputGroup))
                    {
                        outputGroup = new List<SigningOperationPlan>();
                        outputGroups.Add(output, outputGroup);
                        workItems.Add(outputGroup);
                    }

                    outputGroup.Add(plan);

                    continue;
                }

                string directory = PathContainment.GetFullDirectoryPath(
                    plan.Source.File.DirectoryName!);
                string root = sequentialDirectories
                    .Where(
                        candidate => PathContainment.IsContained(
                            candidate,
                            directory))
                    .MinBy(candidate => candidate.Length)!;

                if (!sequentialGroups.TryGetValue(
                    root,
                    out List<SigningOperationPlan>? group))
                {
                    group = new List<SigningOperationPlan>();
                    sequentialGroups.Add(root, group);
                    workItems.Add(group);
                }

                group.Add(plan);
            }

            return workItems;
        }
    }
}