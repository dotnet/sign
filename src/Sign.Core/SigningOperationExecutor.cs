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
        private readonly SigningOperationCoordinator _coordinator;
        private readonly IAggregatingDataFormatSigner _signer;

        internal SigningOperationExecutor(
            IAggregatingDataFormatSigner signer,
            IDirectoryService directoryService,
            ILogger<ISigner> logger,
            SigningOperationCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(signer, nameof(signer));
            ArgumentNullException.ThrowIfNull(
                directoryService,
                nameof(directoryService));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));
            ArgumentNullException.ThrowIfNull(
                coordinator,
                nameof(coordinator));

            _signer = signer;
            _directoryService = directoryService;
            _logger = logger;
            _coordinator = coordinator;
        }

        internal async Task ExecuteAsync(
            SigningOperationPlan plan,
            SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(plan, nameof(plan));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            if (!_signer.HasSigningWork(plan.Source.File, options))
            {
                CopyWithoutSigning(plan);

                return;
            }

            SigningOperationResult result =
                await _coordinator.ExecuteArtifactAsync(
                    plan.Source.SourceIdentity,
                    () => ExecuteOwnerAsync(plan, options));

            result.Materialize(plan.Output);
        }

        private static void CopyWithoutSigning(SigningOperationPlan plan)
        {
            FileInfo source = plan.Source.File;
            FileInfo output = plan.Output;

            if (string.Equals(
                Path.GetFullPath(source.FullName),
                Path.GetFullPath(output.FullName),
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            output.Directory!.Create();
            source.CopyTo(output.FullName, overwrite: true);
        }

        internal SigningOperationStage Stage(
            SigningOperationPlan plan,
            SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(plan, nameof(plan));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            TemporaryDirectory temporaryDirectory =
                new(_directoryService);

            try
            {
                FileInfo source = plan.Source.File;
                string stagedPath = Path.Combine(
                    temporaryDirectory.Directory.FullName,
                    _signer.IsOriginalFileNameRequired(source)
                        ? source.Name
                        : Path.GetRandomFileName());

                if (_signer.CanSign(source))
                {
                    stagedPath = Path.ChangeExtension(
                        stagedPath,
                        source.Extension);
                }

                bool hasStagedDependencies = false;

                if (source.Length > 0)
                {
                    source.CopyTo(stagedPath, overwrite: true);
                    _signer.StageSigningDependencies(
                        source,
                        temporaryDirectory.Directory,
                        options);
                    hasStagedDependencies = temporaryDirectory.Directory
                        .EnumerateFileSystemInfos()
                        .Any(
                            entry => !string.Equals(
                                entry.FullName,
                                stagedPath,
                                StringComparison.OrdinalIgnoreCase));
                }

                return new SigningOperationStage(
                    temporaryDirectory,
                    plan.Source,
                    new SigningFile(
                        new FileInfo(stagedPath),
                        plan.Source.SourceIdentity),
                    hasStagedDependencies);
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

            await _signer.SignOwnerAsync(
                stage.Input,
                options,
                _coordinator);
        }

        private async Task<SigningOperationArtifact> ExecuteOwnerAsync(
            SigningOperationPlan plan,
            SignOptions options)
        {
            SigningOperationStage stage = Stage(plan, options);

            try
            {
                await SignAsync(stage, options);

                // The whole staging directory is published only when the
                // signer staged dependencies (ClickOnce); otherwise only the
                // input is.
                return stage.HasStagedDependencies
                    ? SigningOperationArtifact.OwnedLayout(
                        stage.Input.File,
                        stage.Input.File.Directory!,
                        stage)
                    : SigningOperationArtifact.OwnedSingle(
                        stage.Input.File,
                        stage);
            }
            catch
            {
                stage.Dispose();

                throw;
            }
        }
    }
}
