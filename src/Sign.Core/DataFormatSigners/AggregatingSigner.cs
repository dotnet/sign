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

        public async Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));

            await SignAsync(
                files.Select(SigningFile.Capture),
                options);
        }

        public async Task SignAsync(
            IEnumerable<SigningFile> files,
            SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            List<SigningFile> signingFiles = files.ToList();

            if (options.RecurseContainers)
            {
                await SignContainerContentsAsync(
                    signingFiles,
                    options);
            }

            // split by code sign service and fallback to default

            var grouped = (from signer in _signers
                           from file in signingFiles
                           where signer.CanSign(file.File)
                           group file.File by signer into groups
                           select groups).ToList();

            // get all files and exclude existing; 

            // This is to catch PE files that don't have the correct extension set
            HashSet<FileInfo> explicitlyAssignedFiles = grouped
                .SelectMany(group => group)
                .ToHashSet(FileInfoComparer.Instance);
            IGrouping<IDataFormatSigner, FileInfo>? defaultFiles = signingFiles
                                    .Select(file => file.File)
                                    .Where(file => !explicitlyAssignedFiles.Contains(file))
                                    .Distinct(FileInfoComparer.Instance)
                                    .Where(_fileMetadataService.IsPortableExecutable)
                                    .Select(f => new { _defaultSigner.Signer, f })
                                    .GroupBy(a => a.Signer, k => k.f)
                                    .SingleOrDefault(); // one group here

            if (defaultFiles != null)
            {
                grouped.Add(defaultFiles);
            }

            await Task.WhenAll(grouped.Select(g => g.Key.SignAsync(g.ToList(), options)));
        }

        private async Task SignContainerContentsAsync(
            IReadOnlyList<SigningFile> files,
            SignOptions options)
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
                    await SignAsync(allFiles, options);

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
                    await SignAsync(allFiles, options);
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
                    await SignAsync(allFiles, options);

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

        public void CopySigningResults(
            FileInfo stagedFile,
            DirectoryInfo outputDirectory,
            SignOptions options)
        {
            foreach (IDataFormatSigner signer in _signers)
            {
                if (signer.CanSign(stagedFile))
                {
                    signer.CopySigningResults(
                        stagedFile,
                        outputDirectory,
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
