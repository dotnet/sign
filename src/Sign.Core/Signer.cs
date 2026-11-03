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
            SigningOperationExecutor operationExecutor =
                new(signer, directoryService, _logger);
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
                        baseDirectory);

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

                await Parallel.ForEachAsync(plans, parallelOptions, async (plan, token) =>
                {
                    Stopwatch sw = Stopwatch.StartNew();

                    _logger.LogInformation(
                        Resources.SubmittingFileForSigning,
                        plan.Source.File.FullName);

                    await operationExecutor.ExecuteAsync(
                        plan,
                        signOptions);

                    _logger.LogInformation(Resources.SigningSucceededWithTimeElapsed, sw.ElapsedMilliseconds);
                });
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
    }
}