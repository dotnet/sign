// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core
{
    internal sealed class AggregatingSigner : IAggregatingDataFormatSigner
    {
        private readonly IContainerProvider _containerProvider;
        private readonly IDefaultDataFormatSigner _defaultSigner;
        private readonly IFileMetadataService _fileMetadataService;
        private readonly IMatcherFactory _matcherFactory;
        private readonly IEnumerable<IDataFormatSigner> _signers;

        // Dependency injection requires a public constructor.
        public AggregatingSigner(
            IEnumerable<IDataFormatSigner> signers,
            IDefaultDataFormatSigner defaultSigner,
            IContainerProvider containerProvider,
            IFileMetadataService fileMetadataService,
            IMatcherFactory matcherFactory)
        {
            ArgumentNullException.ThrowIfNull(signers, nameof(signers));
            ArgumentNullException.ThrowIfNull(defaultSigner, nameof(defaultSigner));
            ArgumentNullException.ThrowIfNull(containerProvider, nameof(containerProvider));
            ArgumentNullException.ThrowIfNull(fileMetadataService, nameof(fileMetadataService));
            ArgumentNullException.ThrowIfNull(matcherFactory, nameof(matcherFactory));

            _signers = signers;
            _defaultSigner = defaultSigner;
            _containerProvider = containerProvider;
            _fileMetadataService = fileMetadataService;
            _matcherFactory = matcherFactory;
        }

        public bool CanSign(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            foreach (IDataFormatSigner signer in _signers)
            {
                if (signer.CanSign(file))
                {
                    return true;
                }
            }

            string extension = file.Extension.ToLowerInvariant();

            return extension switch
            {
                // archives
                ".zip" or ".appxupload" or ".msixupload" => true,
                _ => false
            };
        }

        public bool HasSigningWork(FileInfo file, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            return (options.RecurseContainers && IsContainer(file)) ||
                GetSigners(file).Count > 0;
        }

        public async Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            await SignUncoordinatedAsync(
                files.Select(SigningFile.Capture),
                options);
        }

        public async Task SignAsync(
            IEnumerable<SigningFile> files,
            SignOptions options,
            SigningOperationCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            ArgumentNullException.ThrowIfNull(
                coordinator,
                nameof(coordinator));

            List<SigningFile> signingFiles = files.ToList();
            List<SigningFile> containerFiles = signingFiles
                .Where(file => IsContainer(file.File))
                .ToList();
            HashSet<SigningSourceIdentity> containerIdentities =
                containerFiles
                    .Select(file => file.SourceIdentity)
                    .ToHashSet();
            List<SigningFile> sequentialFiles = signingFiles
                .Where(
                    file =>
                        !containerIdentities.Contains(
                            file.SourceIdentity) &&
                        IsSequentialCoordinationRequired(file.File))
                .ToList();
            HashSet<SigningSourceIdentity> sequentialIdentities =
                sequentialFiles
                    .Select(file => file.SourceIdentity)
                    .ToHashSet();

            await Task.WhenAll(
                containerFiles.Select(
                    file => SignCoordinatedAsync(
                        file,
                        options,
                        coordinator)));

            // Ordinary files precede sequential (ClickOnce) files because
            // ClickOnce manifests hash the payload bytes present when they
            // are signed.
            await SignCoordinatedBatchAsync(
                signingFiles
                    .Where(
                        file =>
                            !containerIdentities.Contains(
                                file.SourceIdentity) &&
                            !sequentialIdentities.Contains(
                                file.SourceIdentity) &&
                            HasSigningWork(file.File, options))
                    .ToList(),
                options,
                coordinator);

            foreach (SigningFile file in sequentialFiles)
            {
                await SignCoordinatedAsync(
                    file,
                    options,
                    coordinator);
            }
        }

        public async Task SignOwnerAsync(
            SigningFile file,
            SignOptions options,
            SigningOperationCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            ArgumentNullException.ThrowIfNull(
                coordinator,
                nameof(coordinator));

            await SignOwnerCoreAsync(file, options, coordinator);
        }

        private async Task SignOwnerCoreAsync(
            SigningFile file,
            SignOptions options,
            SigningOperationCoordinator? coordinator)
        {
            if (options.RecurseContainers)
            {
                await SignContainerContentsAsync(
                    new[] { file },
                    options,
                    coordinator);
            }

            await SignCurrentFileAsync(file, options, coordinator);
        }

        private async Task SignCurrentFileAsync(
            SigningFile file,
            SignOptions options,
            SigningOperationCoordinator? coordinator)
        {
            List<IDataFormatSigner> signers = GetSigners(file.File);

            await Task.WhenAll(
                signers.Select(
                    signer => coordinator is not null
                        ? signer.SignAsync(
                            file,
                            options,
                            coordinator)
                        : signer.SignAsync(
                            new[] { file.File },
                            options)));
        }

        private List<IDataFormatSigner> GetSigners(FileInfo file)
        {
            List<IDataFormatSigner> signers = _signers
                .Where(signer => signer.CanSign(file))
                .ToList();

            if (signers.Count == 0 &&
                _fileMetadataService.IsPortableExecutable(file))
            {
                signers.Add(_defaultSigner.Signer);
            }

            return signers;
        }

        private async Task SignCoordinatedAsync(
            SigningFile file,
            SignOptions options,
            SigningOperationCoordinator coordinator)
        {
            SigningOperationResult result =
                await coordinator.ExecuteArtifactAsync(
                    file.SourceIdentity,
                    async () =>
                    {
                        await SignOwnerCoreAsync(
                            file,
                            options,
                            coordinator);

                        return SigningOperationArtifact.Single(
                            file.File);
                    });

            result.Materialize(file.File);
        }

        // Claims every file, signs the claimed files with one call per
        // signer as the uncoordinated path does, then materializes every
        // result, including those of files claimed elsewhere. This relies on
        // the coordinator invoking a claimed operation before
        // ExecuteArtifactAsync returns. The batch waits on no other
        // operation before it completes, so it cannot join a wait cycle.
        private async Task SignCoordinatedBatchAsync(
            IReadOnlyList<SigningFile> files,
            SignOptions options,
            SigningOperationCoordinator coordinator)
        {
            if (files.Count == 0)
            {
                return;
            }

            TaskCompletionSource batch = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            List<SigningFile> ownedFiles = new();
            List<Task<SigningOperationResult>> results = new();
            SigningFile? claimingFile = null;

            try
            {
                foreach (SigningFile file in files)
                {
                    claimingFile = file;
                    results.Add(
                        coordinator.ExecuteArtifactAsync(
                            file.SourceIdentity,
                            () => AwaitBatchAsync(file)));
                    claimingFile = null;
                }

                await SignWithSignersAsync(ownedFiles, options);
                batch.SetResult();
            }
            catch (Exception exception)
            {
                // Owned operations share the batch's failure.
                batch.TrySetException(exception);

                throw;
            }

            for (int i = 0; i < files.Count; ++i)
            {
                SigningOperationResult result = await results[i];

                result.Materialize(files[i].File);
            }

            async Task<SigningOperationArtifact> AwaitBatchAsync(
                SigningFile file)
            {
                if (!ReferenceEquals(claimingFile, file))
                {
                    throw new InvalidOperationException(
                        message:
                            "A batched signing operation started after " +
                            "its claim.");
                }

                ownedFiles.Add(file);

                await batch.Task.ConfigureAwait(
                    continueOnCapturedContext: false);

                return SigningOperationArtifact.Single(file.File);
            }
        }

        private bool IsContainer(FileInfo file)
        {
            return _containerProvider.IsZipContainer(file) ||
                _containerProvider.IsNuGetContainer(file) ||
                _containerProvider.IsAppxContainer(file) ||
                _containerProvider.IsAppxBundleContainer(file);
        }

        public bool IsSequentialCoordinationRequired(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return _signers.Any(
                signer =>
                    signer.RequiresSequentialCoordination &&
                    signer.CanSign(file));
        }

        public bool IsOriginalFileNameRequired(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return _signers.Any(
                signer =>
                    signer.RequiresOriginalFileName &&
                    signer.CanSign(file));
        }

        private async Task SignContainerContentsAsync(
            IReadOnlyList<SigningFile> files,
            SignOptions options,
            SigningOperationCoordinator? coordinator)
        {
            // See if any of them are archives
            List<SigningFile> archives = (from file in files
                                       where _containerProvider.IsZipContainer(file.File) || _containerProvider.IsNuGetContainer(file.File)
                                       select file).ToList();

            // expand the archives and sign recursively first
            List<OpenedContainer> containers = new();

            try
            {
                foreach (SigningFile archive in archives)
                {
                    IContainer container =
                        _containerProvider.GetContainer(archive.File)!;

                    await container.OpenAsync();

                    containers.Add(
                        new OpenedContainer(
                            container,
                            archive.SourceIdentity));
                }

                // See if there's any files in the expanded zip that we need to sign
                List<SigningFile> allFiles = containers
                    .SelectMany(
                        container => GetFiles(
                            container,
                            options))
                    .ToList();

                if (allFiles.Count > 0)
                {
                    // Send the files from the archives through the aggregator to sign
                    await SignRecursiveAsync(
                        allFiles,
                        options,
                        coordinator);

                    // After signing the contents, save the zip
                    // For NuPkg, this step removes the signature too, but that's ok as it'll get signed below
                    await Parallel.ForEachAsync(
                        containers,
                        (container, cancellationToken) =>
                            container.Container.SaveAsync());
                }
            }
            finally
            {
                containers.ForEach(
                    container => container.Container.Dispose());
                containers.Clear();
            }

            // See if there's any appx's in here, process them recursively first to sign the inner files
            List<SigningFile> appxs = (from file in files
                                    where _containerProvider.IsAppxContainer(file.File)
                                    select file).ToList();

            // See if there's any appxbundles here, process them recursively first
            // expand the archives and sign recursively first
            // This will also update the publisher information to get it ready for signing
            try
            {
                foreach (SigningFile appx in appxs)
                {
                    IContainer container =
                        _containerProvider.GetContainer(appx.File)!;

                    await container.OpenAsync();

                    containers.Add(
                        new OpenedContainer(
                            container,
                            appx.SourceIdentity));
                }

                // See if there's any files in the expanded zip that we need to sign
                List<SigningFile> allFiles = containers
                    .SelectMany(
                        container => GetFiles(
                            container,
                            options))
                    .ToList();

                if (allFiles.Count > 0)
                {
                    // Send the files from the archives through the aggregator to sign
                    await SignRecursiveAsync(
                        allFiles,
                        options,
                        coordinator);
                }

                // Save the appx with the updated publisher info
                await Parallel.ForEachAsync(
                    containers,
                    (container, cancellationToken) =>
                        container.Container.SaveAsync());
            }
            finally
            {
                containers.ForEach(
                    container => container.Container.Dispose());
                containers.Clear();
            }

            List<SigningFile> bundles = (from file in files
                                      where _containerProvider.IsAppxBundleContainer(file.File)
                                      select file).ToList();

            try
            {
                foreach (SigningFile bundle in bundles)
                {
                    IContainer container =
                        _containerProvider.GetContainer(bundle.File)!;

                    await container.OpenAsync();

                    containers.Add(
                        new OpenedContainer(
                            container,
                            bundle.SourceIdentity));
                }

                Matcher appxBundleFileMatcher = _matcherFactory.Create();

                appxBundleFileMatcher.AddInclude("**/*.appx");
                appxBundleFileMatcher.AddInclude("**/*.msix");

                // See if there's any files in the expanded zip that we need to sign
                List<SigningFile> allFiles = containers
                    .SelectMany(
                        container => container.Container.GetFiles(
                            appxBundleFileMatcher,
                            container.SourceIdentity))
                    .ToList();

                if (allFiles.Count > 0)
                {
                    // Send the files from the archives through the aggregator to sign
                    await SignRecursiveAsync(
                        allFiles,
                        options,
                        coordinator);

                    // After signing the contents, save the zip
                    await Parallel.ForEachAsync(
                        containers,
                        (container, cancellationToken) =>
                            container.Container.SaveAsync());
                }
            }
            finally
            {
                containers.ForEach(
                    container => container.Container.Dispose());
                containers.Clear();
            }
        }

        private Task SignRecursiveAsync(
            IEnumerable<SigningFile> files,
            SignOptions options,
            SigningOperationCoordinator? coordinator)
        {
            return coordinator is null
                ? SignUncoordinatedAsync(files, options)
                : SignAsync(files, options, coordinator);
        }

        private async Task SignUncoordinatedAsync(
            IEnumerable<SigningFile> files,
            SignOptions options)
        {
            List<SigningFile> signingFiles = files.ToList();

            if (options.RecurseContainers)
            {
                await SignContainerContentsAsync(
                    signingFiles,
                    options,
                    coordinator: null);
            }

            await SignWithSignersAsync(signingFiles, options);
        }

        private async Task SignWithSignersAsync(
            IReadOnlyList<SigningFile> signingFiles,
            SignOptions options)
        {
            List<IGrouping<IDataFormatSigner, FileInfo>> grouped =
                (from signer in _signers
                 from file in signingFiles
                 where signer.CanSign(file.File)
                 group file.File by signer).ToList();
            HashSet<FileInfo> explicitlyAssignedFiles = grouped
                .SelectMany(group => group)
                .ToHashSet(FileInfoComparer.Instance);
            IGrouping<IDataFormatSigner, FileInfo>? defaultFiles =
                signingFiles
                    .Select(file => file.File)
                    .Where(
                        file =>
                            !explicitlyAssignedFiles.Contains(file))
                    .Distinct(FileInfoComparer.Instance)
                    .Where(_fileMetadataService.IsPortableExecutable)
                    .Select(file => new
                    {
                        _defaultSigner.Signer,
                        File = file
                    })
                    .GroupBy(item => item.Signer, item => item.File)
                    .SingleOrDefault();

            if (defaultFiles is not null)
            {
                grouped.Add(defaultFiles);
            }

            await Task.WhenAll(
                grouped.Select(
                    group =>
                        group.Key.SignAsync(
                            group.ToList(),
                            options)));
        }


        public void StageSigningDependencies(
            FileInfo source,
            DirectoryInfo stagingDirectory,
            SignOptions options)
        {
            foreach (IDataFormatSigner signer in _signers)
            {
                if (signer.CanSign(source))
                {
                    signer.StageSigningDependencies(
                        source,
                        stagingDirectory,
                        options);
                }
            }
        }

        private static IEnumerable<SigningFile> GetFiles(
            OpenedContainer container,
            SignOptions options)
        {
            IEnumerable<SigningFile> files;

            if (options.Matcher is null)
            {
                // If not filtered, default to all
                files = container.Container.GetFiles(
                    container.SourceIdentity);
            }
            else
            {
                files = container.Container.GetFiles(
                    options.Matcher,
                    container.SourceIdentity);
            }

            if (options.AntiMatcher is not null)
            {
                HashSet<SigningSourceIdentity> antiFiles =
                    container.Container
                        .GetFiles(
                            options.AntiMatcher,
                            container.SourceIdentity)
                        .Select(file => file.SourceIdentity)
                        .ToHashSet();

                files = files
                    .Where(
                        file => !antiFiles.Contains(
                            file.SourceIdentity))
                    .ToList();
            }

            return files;
        }

        private sealed class OpenedContainer
        {
            internal OpenedContainer(
                IContainer container,
                SigningSourceIdentity sourceIdentity)
            {
                Container = container;
                SourceIdentity = sourceIdentity;
            }

            internal IContainer Container { get; }
            internal SigningSourceIdentity SourceIdentity { get; }
        }
    }
}
