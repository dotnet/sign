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

                IReadOnlyList<SigningOperationPlan> plans =
                    SigningOperationPlanner.Create(
                        inputFiles,
                        outputFile,
                        baseDirectory);

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
    }
}
