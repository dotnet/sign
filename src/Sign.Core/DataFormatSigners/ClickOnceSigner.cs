// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class ClickOnceSigner :
        RetryingSigner,
        IDataFormatSigner
    {
        private readonly Lazy<IAggregatingDataFormatSigner> _aggregatingSigner;
        private readonly ICertificateProvider _certificateProvider;
        private readonly ISignatureAlgorithmProvider _signatureAlgorithmProvider;
        private readonly IMageCli _mageCli;
        private readonly IManifestSigner _manifestSigner;
        private readonly ParallelOptions _parallelOptions = new() { MaxDegreeOfParallelism = 4 };
        private readonly IFileMatcher _fileMatcher;

        // Dependency injection requires a public constructor.
        public ClickOnceSigner(
            ISignatureAlgorithmProvider signatureAlgorithmProvider,
            ICertificateProvider certificateProvider,
            IServiceProvider serviceProvider,
            IMageCli mageCli,
            IManifestSigner manifestSigner,
            ILogger<IDataFormatSigner> logger,
            IFileMatcher fileMatcher)
            : base(logger)
        {
            ArgumentNullException.ThrowIfNull(signatureAlgorithmProvider, nameof(signatureAlgorithmProvider));
            ArgumentNullException.ThrowIfNull(certificateProvider, nameof(certificateProvider));
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));
            ArgumentNullException.ThrowIfNull(mageCli, nameof(mageCli));
            ArgumentNullException.ThrowIfNull(manifestSigner, nameof(manifestSigner));
            ArgumentNullException.ThrowIfNull(fileMatcher, nameof(fileMatcher));

            _signatureAlgorithmProvider = signatureAlgorithmProvider;
            _certificateProvider = certificateProvider;
            _mageCli = mageCli;
            _manifestSigner = manifestSigner;
            _fileMatcher = fileMatcher;

            // Need to delay this as it'd create a dependency loop if directly in the ctor
            _aggregatingSigner = new Lazy<IAggregatingDataFormatSigner>(() => serviceProvider.GetService<IAggregatingDataFormatSigner>()!);
        }

        public bool RequiresSequentialCoordination => true;

        // StageSigningDependencies copies the source directory around the
        // staged manifest, so a renamed manifest would leave two copies.
        public bool RequiresOriginalFileName => true;

        public bool CanSign(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return file.Extension.ToLowerInvariant() switch
            {
                ".vsto" or ".application" => true,
                _ => false
            };
        }

        public async Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            await SignAsync(
                files.Select(SigningFile.Capture),
                options,
                coordinator: null);
        }

        public Task SignAsync(
            SigningFile file,
            SignOptions options,
            SigningOperationCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            ArgumentNullException.ThrowIfNull(
                coordinator,
                nameof(coordinator));

            return SignAsync(
                new[] { file },
                options,
                coordinator);
        }

        private async Task SignAsync(
            IEnumerable<SigningFile> files,
            SignOptions options,
            SigningOperationCoordinator? coordinator)
        {
            List<SigningFile> signingFiles = files.ToList();

            Logger.LogInformation(
                Resources.ClickOnceSignatureProviderSigning,
                signingFiles.Count);

            var args = "-a sha256RSA";
            if (!string.IsNullOrWhiteSpace(options.ApplicationName))
            {
                args += $@" -n ""{options.ApplicationName}""";
            }

            Uri? timeStampUrl = options.TimestampService;

            using (X509Certificate2 certificate = await _certificateProvider.GetCertificateAsync())
            using (RSA rsaPrivateKey = await _signatureAlgorithmProvider.GetRsaAsync())
            {
                // This outer loop is for a deployment manifest file (.application/.vsto).
                await Parallel.ForEachAsync(
                    signingFiles,
                    _parallelOptions,
                    async (file, state) =>
                {
                    // We need to be explicit about the order these files are signed in. The data files must be signed first
                    // Then the .manifest file
                    // Then the nested clickonce/vsto file
                    // finally the top-level clickonce/vsto file
                    // It's possible that there might not actually be a .manifest file or any data files if the user just
                    // wants to re-sign an existing deployment manifest because e.g. the update URL has changed but nothing
                    // else has. In that case we don't need to touch the other files and we can just sign the deployment manifest.

                    // Look for the data files first - these are .deploy files
                    // we need to rename them, sign, then restore the name

                    DirectoryInfo clickOnceDirectory =
                        file.File.Directory!;

                    // get the files, _including_ the SignOptions, so that we only actually try to sign the files specified.
                    // this is useful if e.g. you don't want to sign third-party assemblies that your application depends on
                    // but you do still want to sign your own assemblies.
                    List<SigningFile> filteredFiles =
                        GetFiles(file, clickOnceDirectory, options)
                            .ToList();
                    List<SigningFile> deployFilesToSign = filteredFiles
                        .Where(
                            f => ".deploy".Equals(
                                f.File.Extension,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    List<SigningFile> contentFiles = new();

                    RemoveDeployExtension(deployFilesToSign, contentFiles);

                    List<SigningFile> filesToSign =
                        contentFiles.ToList();
                    IEnumerable<SigningFile> setupExe = filteredFiles
                        .Where(
                            f => ".exe".Equals(
                                f.File.Extension,
                                StringComparison.OrdinalIgnoreCase));
                    filesToSign.AddRange(setupExe);

                    // sign the inner files
                    if (coordinator is null)
                    {
                        await _aggregatingSigner.Value.SignAsync(
                            filesToSign.Select(
                                signingFile => signingFile.File),
                            options);
                    }
                    else
                    {
                        await _aggregatingSigner.Value.SignAsync(
                            filesToSign,
                            options,
                            coordinator);
                    }

                    // rename the rest of the deploy files since signing the manifest will need them.
                    // this uses the overload of GetFiles() that ignores file matching options because we
                    // require all files to be named correctly in order to generate valid manifests.
                    HashSet<SigningSourceIdentity> filteredIdentities =
                        filteredFiles
                            .Select(
                                signingFile =>
                                    signingFile.SourceIdentity)
                            .ToHashSet();
                    List<SigningFile> filesExceptFiltered =
                        GetFiles(file, clickOnceDirectory)
                            .Where(
                                signingFile =>
                                    !filteredIdentities.Contains(
                                        signingFile.SourceIdentity))
                            .ToList();
                    List<SigningFile> deployFiles = filesExceptFiltered
                        .Where(
                            f => ".deploy".Equals(
                                f.File.Extension,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    RemoveDeployExtension(deployFiles, contentFiles);

                    // at this point contentFiles has all deploy files renamed

                    // Inner files are now signed
                    // now look for the manifest file and sign that if we have one

                    SigningFile? manifestFile = filteredFiles
                        .SingleOrDefault(
                            f => ".manifest".Equals(
                                f.File.Extension,
                                StringComparison.OrdinalIgnoreCase));

                    string fileArgs =
                        $@"-update ""{manifestFile?.File}"" {args}";

                    if (manifestFile is not null)
                    {
                        await SignDiscoveredFileAsync(
                            manifestFile,
                            coordinator,
                            includeSiblings: false,
                            async () =>
                            {
                                if (!await SignAsync(
                                    fileArgs,
                                    manifestFile.File,
                                    rsaPrivateKey,
                                    certificate,
                                    options))
                                {
                                    throw CreateSigningException(
                                        manifestFile.File);
                                }
                            });
                    }

                    string publisherParam = string.Empty;

                    if (string.IsNullOrEmpty(options.PublisherName))
                    {
                        string publisherName = certificate.SubjectName.Name;

                        // get the DN. it may be quoted
                        publisherParam = $@"-pub ""{publisherName.Replace("\"", "")}""";
                    }
                    else
                    {
                        publisherParam = $"-pub \"{options.PublisherName}\"";
                    }

                    // Now sign deployment manifest files (.application/.vsto).
                    // Order by desending length to put the inner one first
                    List<SigningFile> deploymentManifestFiles =
                        filteredFiles
                        .Where(f => CanSign(f.File))
                        .Select(
                            f => new
                            {
                                file = f,
                                f.File.FullName.Length
                            })
                        .OrderByDescending(f => f.Length)
                        .Select(f => f.file)
                        .ToList();
                    TaskCompletionSource layoutReady = new(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    List<Task<SigningOperationResult>>
                        pendingDiscoveredResults = new();
                    List<(SigningOperationResult Result, FileInfo Destination)>
                        completedDiscoveredResults = new();

                    try
                    {
                        foreach (
                            SigningFile deploymentManifestFile
                            in deploymentManifestFiles)
                        {
                            fileArgs =
                                $@"-update ""{deploymentManifestFile.File.FullName}"" {args} {publisherParam}";
                            if (manifestFile is not null)
                            {
                                fileArgs +=
                                    $@" -appm ""{manifestFile.File.FullName}""";
                            }
                            if (options.DescriptionUrl is not null)
                            {
                                fileArgs +=
                                    $@" -SupportURL {options.DescriptionUrl.AbsoluteUri}";
                            }

                            async Task SignDeploymentManifestAsync()
                            {
                                if (!await SignAsync(
                                    fileArgs,
                                    deploymentManifestFile.File,
                                    rsaPrivateKey,
                                    certificate,
                                    options))
                                {
                                    throw CreateSigningException(
                                        deploymentManifestFile.File);
                                }
                            }

                            if (coordinator is null ||
                                deploymentManifestFile
                                    .SourceIdentity.Equals(
                                        file.SourceIdentity))
                            {
                                await SignDeploymentManifestAsync();
                            }
                            else
                            {
                                TaskCompletionSource signed = new(
                                    TaskCreationOptions
                                        .RunContinuationsAsynchronously);
                                Task<SigningOperationResult> resultTask =
                                    coordinator.ExecuteArtifactAsync(
                                        deploymentManifestFile
                                            .SourceIdentity,
                                        async () =>
                                        {
                                            try
                                            {
                                                await
                                                    SignDeploymentManifestAsync();
                                                signed.TrySetResult();
                                                await layoutReady.Task;

                                                return
                                                    SigningOperationArtifact
                                                        .Layout(
                                                            deploymentManifestFile
                                                                .File,
                                                            deploymentManifestFile
                                                                .File
                                                                .Directory!);
                                            }
                                            catch (Exception exception)
                                            {
                                                signed.TrySetException(
                                                    exception);

                                                throw;
                                            }
                                        });
                                Task firstCompletion =
                                    await Task.WhenAny(
                                        resultTask,
                                        signed.Task);

                                if (ReferenceEquals(
                                    firstCompletion,
                                    resultTask))
                                {
                                    completedDiscoveredResults.Add(
                                        (await resultTask,
                                            deploymentManifestFile.File));
                                }
                                else
                                {
                                    await signed.Task;
                                    pendingDiscoveredResults.Add(resultTask);
                                }
                            }
                        }

                        // restore the .deploy files
                        foreach (SigningFile contentFile in contentFiles)
                        {
                            File.Move(
                                contentFile.File.FullName,
                                $"{contentFile.File.FullName}.deploy");
                        }

                        // Layouts signed elsewhere contain .deploy names, so
                        // they are copied only after the names are restored.
                        foreach (
                            (SigningOperationResult result,
                                FileInfo destination)
                            in completedDiscoveredResults)
                        {
                            result.Materialize(destination);
                        }

                        layoutReady.TrySetResult();

                        // This flow signed these manifests in this layout,
                        // which is unchanged since their snapshots were
                        // taken, so copying the snapshots back is redundant.
                        // Awaiting surfaces snapshot failures before the
                        // layout is published or deleted.
                        foreach (
                            Task<SigningOperationResult> resultTask
                            in pendingDiscoveredResults)
                        {
                            await resultTask;
                        }
                    }
                    catch (Exception exception)
                    {
                        layoutReady.TrySetException(exception);

                        throw;
                    }
                });
            }
        }

        private static void RemoveDeployExtension(
            List<SigningFile> deployFilesToSign,
            List<SigningFile> contentFiles)
        {
            foreach (SigningFile deployFileToSign in deployFilesToSign)
            {
                // Rename to file without .deploy extension
                // For example:
                //      *  MyApp.dll.deploy => MyApp.dll
                //      *  MyApp.exe.deploy => MyApp.exe
                string contentFilePath = Path.Combine(
                    deployFileToSign.File.DirectoryName!,
                    Path.GetFileNameWithoutExtension(
                        deployFileToSign.File.Name));
                FileInfo contentFile = new(contentFilePath);

                File.Move(
                    deployFileToSign.File.FullName,
                    contentFile.FullName);

                contentFiles.Add(
                    deployFileToSign.WithFile(contentFile));
            }
        }

        private static SigningException CreateSigningException(
            FileInfo file)
        {
            string message = string.Format(
                CultureInfo.CurrentCulture,
                Resources.SigningFailed,
                file.FullName);

            return new SigningException(message);
        }

        private static async Task SignDiscoveredFileAsync(
            SigningFile file,
            SigningOperationCoordinator? coordinator,
            bool includeSiblings,
            Func<Task> operation)
        {
            if (coordinator is null)
            {
                await operation();

                return;
            }

            SigningOperationResult result =
                await coordinator.ExecuteArtifactAsync(
                    file.SourceIdentity,
                    async () =>
                    {
                        await operation();

                        return includeSiblings
                            ? SigningOperationArtifact.Layout(
                                file.File,
                                file.File.Directory!)
                            : SigningOperationArtifact.Single(
                                file.File);
                    });

            result.Materialize(file.File);
        }

        protected override async Task<bool> SignCoreAsync(string? args, FileInfo file, RSA rsaPrivateKey, X509Certificate2 certificate, SignOptions options)
        {
            int exitCode = await _mageCli.RunAsync(args);

            if (exitCode == 0)
            {
                // Now add the signature
                _manifestSigner.Sign(file, certificate, rsaPrivateKey, options);

                return true;
            }

            Logger.LogError(Resources.SigningFailedWithError, exitCode);

            return false;
        }


        private static IEnumerable<SigningFile> GetFiles(
            SigningFile source,
            DirectoryInfo clickOnceRoot)
        {
            return clickOnceRoot
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Select(
                    file => GetSigningFile(
                        source,
                        clickOnceRoot,
                        file));
        }

        private IEnumerable<SigningFile> GetFiles(
            SigningFile source,
            DirectoryInfo clickOnceRoot,
            SignOptions options)
        {
            IEnumerable<FileInfo> files;

            if (options.Matcher is null)
            {
                // If not filtered, default to all
                return GetFiles(source, clickOnceRoot);
            }
            else
            {
                files = _fileMatcher.EnumerateMatches(new DirectoryInfoWrapper(clickOnceRoot), options.Matcher);
            }

            if (options.AntiMatcher is not null)
            {
                IEnumerable<FileInfo> antiFiles = _fileMatcher.EnumerateMatches(new DirectoryInfoWrapper(clickOnceRoot), options.AntiMatcher);

                files = files.Except(antiFiles, FileInfoComparer.Instance).ToList();
            }
            return files.Select(
                file => GetSigningFile(
                    source,
                    clickOnceRoot,
                    file));
        }

        private static SigningFile GetSigningFile(
            SigningFile source,
            DirectoryInfo sourceDirectory,
            FileInfo file)
        {
            return FileInfoComparer.Instance.Equals(
                source.File,
                file)
                ? source
                : source.GetSibling(file, sourceDirectory);
        }

        public void StageSigningDependencies(
            FileInfo deploymentManifestFile,
            DirectoryInfo stagingDirectory,
            SignOptions signOptions)
        {
            CopyDependencies(
                deploymentManifestFile,
                stagingDirectory);
        }

        private void CopyDependencies(
            FileInfo deploymentManifestFile,
            DirectoryInfo destination)
        {
            // copy _all_ files, ignoring matching options, because we need them to be available to generate
            // valid manifests.
            foreach (
                FileInfo file
                in deploymentManifestFile.Directory!.EnumerateFiles(
                    "*",
                    SearchOption.AllDirectories))
            {
                // don't copy the file itself because that's already taken care of (and we don't want a duplicate copy with the 'real' name)
                // lying around since it'll get copied back and overwrite the signed one.
                if (file.FullName != deploymentManifestFile.FullName)
                {
                    string relativeDestPath = Path.GetRelativePath(deploymentManifestFile.Directory!.FullName, file.FullName);
                    string fullDestPath = Path.Combine(destination.FullName, relativeDestPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullDestPath!)!);
                    file.CopyTo(fullDestPath, overwrite: true);
                }
            }
        }
    }
}
