// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class SigningOperationExecutor
    {
        private readonly IDirectoryService _directoryService;
        private readonly ILogger<ISigner> _logger;
        private readonly IAggregatingDataFormatSigner _signer;

        internal SigningOperationExecutor(
            IAggregatingDataFormatSigner signer,
            IDirectoryService directoryService,
            ILogger<ISigner> logger)
        {
            ArgumentNullException.ThrowIfNull(signer, nameof(signer));
            ArgumentNullException.ThrowIfNull(
                directoryService,
                nameof(directoryService));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _signer = signer;
            _directoryService = directoryService;
            _logger = logger;
        }

        internal async Task ExecuteAsync(
            SigningOperationPlan plan,
            SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(plan, nameof(plan));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            using SigningOperationStage stage = Stage(plan, options);

            await SignAsync(stage, options);

            Publish(stage, plan, options);
        }

        internal SigningOperationStage Stage(
            SigningOperationPlan plan,
            SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(plan, nameof(plan));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            plan.Output.Directory!.Create();

            TemporaryDirectory temporaryDirectory =
                new(_directoryService);

            try
            {
                FileInfo source = plan.Source.File;
                string stagedPath = Path.Combine(
                    temporaryDirectory.Directory.FullName,
                    Path.GetRandomFileName());

                if (_signer.CanSign(source))
                {
                    stagedPath = Path.ChangeExtension(
                        stagedPath,
                        source.Extension);
                }

                if (source.Length > 0)
                {
                    source.CopyTo(stagedPath, overwrite: true);
                    _signer.StageSigningDependencies(
                        source,
                        temporaryDirectory.Directory,
                        options);
                }

                return new SigningOperationStage(
                    temporaryDirectory,
                    plan.Source,
                    new SigningFile(
                        new FileInfo(stagedPath),
                        plan.Source.SourceIdentity));
            }
            catch
            {
                temporaryDirectory.Dispose();

                throw;
            }
        }

        internal async Task SignAsync(
            SigningOperationStage stage,
            SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(stage, nameof(stage));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            _logger.LogInformation(
                Resources.SignAsyncCalled,
                stage.Source.File.FullName,
                stage.Input.File.FullName);

            await _signer.SignAsync(
                new[] { stage.Input },
                options);
        }

        internal void Publish(
            SigningOperationStage stage,
            SigningOperationPlan plan,
            SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(stage, nameof(stage));
            ArgumentNullException.ThrowIfNull(plan, nameof(plan));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            FileInfo output = plan.Output;

            _signer.CopySigningResults(
                stage.Input.File,
                output.Directory!,
                options);
            stage.Input.File.CopyTo(
                output.FullName,
                overwrite: true);
        }
    }
}
