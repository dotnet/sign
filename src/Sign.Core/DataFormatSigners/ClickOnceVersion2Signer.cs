// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;

namespace Sign.Core
{
    internal sealed class ClickOnceVersion2Signer : IDataFormatSigner
    {
        private readonly ClickOnceSigningConfiguration _configuration;
        private readonly IServiceProvider _services;
        private readonly Lazy<ClickOnceDeploymentManifestSigner> _pipeline;

        internal ClickOnceVersion2Signer(ClickOnceSigningConfiguration configuration, IServiceProvider services)
        {
            _configuration = configuration;
            _services = services;
            _pipeline = new(() => new ClickOnceDeploymentManifestSigner(services));
        }

        public bool CanSign(FileInfo file) => CanSign(SigningFile.Capture(file));

        internal bool CanSign(SigningFile file) => _pipeline.Value.CanSign(file);

        public bool RequiresOwnStaging => true;
        public bool RequiresOriginalFileName => true;

        public Task<SigningOperationArtifact> SignOwnerArtifactAsync(
            SigningFile file, SignOptions options, SigningOperationCoordinator coordinator) =>
            _pipeline.Value.SignInputOwnerAsync(file, options, coordinator,
                _configuration.ApplicationPolicy, _configuration.UpdateManifest);

        internal Task ExecuteAsync(
            IReadOnlyList<SigningOperationPlan> plans,
            SignOptions options,
            SigningOperationCoordinator coordinator,
            bool insideOperation = false) =>
            _pipeline.Value.ExecuteAsync(plans, options, coordinator, _configuration.MaxConcurrency,
                _configuration.ApplicationPolicy, _configuration.UpdateManifest, insideOperation);

        public async Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            await using SigningOperationCoordinator coordinator = new(_services.GetRequiredService<IDirectoryService>());
            await ExecuteAsync(files.Select(file =>
                new SigningOperationPlan(SigningFile.Capture(file), file)).ToArray(), options, coordinator);
        }

        public async Task SignAsync(SigningFile file, SignOptions options, SigningOperationCoordinator coordinator)
        {
            SigningOperationResult result = await coordinator.WaitForReadyOwnerAsync(
                coordinator.ExecuteArtifactAsync(file.SourceIdentity,
                    () => SignOwnerArtifactAsync(file, options, coordinator)));
            result.Materialize(file.File);
        }
    }
}
