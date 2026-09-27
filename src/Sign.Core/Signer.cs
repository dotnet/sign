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
            SigningOperationCoordinator? coordinator = null;
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
                coordinator = new SigningOperationCoordinator(
                    directoryService);
                SigningOperationExecutor operationExecutor = new(
                    signer,
                    directoryService,
                    _logger,
                    coordinator);

                using (X509Certificate2 certificate = await certificateProvider.GetCertificateAsync())
                {
                    ICertificateVerifier certificateVerifier = _serviceProvider.GetRequiredService<ICertificateVerifier>();

                    certificateVerifier.Verify(certificate);
                }

                IReadOnlyList<SigningOperationPlan> plans =
                    SigningOperationPlanner.Create(
                        inputFiles,
                        outputFile,
                        baseDirectory);
                IReadOnlyList<IReadOnlyList<SigningOperationPlan>>
                    workItems = CreateWorkItems(
                        plans,
                        signer.IsSequentialCoordinationRequired);

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

                        await operationExecutor.ExecuteAsync(
                            plan,
                            signOptions);

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
                try
                {
                    if (coordinator is not null)
                    {
                        await coordinator.DisposeAsync();
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        exception.Message);
                }
            }

            return ExitCode.Success;
        }

        // Work items are in input order; they are started in that order, but
        // with concurrency above 1 they overlap. Inputs that require
        // sequential coordination (ClickOnce) form one sequential work item
        // per outermost such input directory, positioned at its first input.
        // Directories are compared by path text only. A ClickOnce operation
        // waits on the deployment manifests it discovers in its directory
        // tree, so running inputs whose trees overlap concurrently can form a
        // wait cycle.
        private static IReadOnlyList<IReadOnlyList<SigningOperationPlan>>
            CreateWorkItems(
                IReadOnlyList<SigningOperationPlan> plans,
                Func<FileInfo, bool> requiresSequentialCoordination)
        {
            List<IReadOnlyList<SigningOperationPlan>> workItems = new();
            Dictionary<string, List<SigningOperationPlan>> sequentialGroups =
                new(StringComparer.OrdinalIgnoreCase);
            List<string> sequentialDirectories = plans
                .Where(
                    plan => requiresSequentialCoordination(plan.Source.File))
                .Select(plan => GetDirectoryPath(plan.Source.File))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (SigningOperationPlan plan in plans)
            {
                if (!requiresSequentialCoordination(plan.Source.File))
                {
                    workItems.Add(new[] { plan });

                    continue;
                }

                string directory = GetDirectoryPath(plan.Source.File);
                string root = sequentialDirectories
                    .Where(
                        candidate => directory.StartsWith(
                            candidate,
                            StringComparison.OrdinalIgnoreCase))
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

        private static string GetDirectoryPath(FileInfo file)
        {
            return Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(file.DirectoryName!)) +
                Path.DirectorySeparatorChar;
        }
    }
}
