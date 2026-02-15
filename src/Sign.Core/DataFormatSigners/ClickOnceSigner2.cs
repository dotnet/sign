// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class ClickOnceSigner2 : RetryingSigner, IDataFormatSigner
    {
        private const string DeployExtension = ".deploy";
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

        private readonly Lazy<IAggregatingDataFormatSigner> _aggregatingSigner;
        private readonly ICertificateProvider _certificateProvider;
        private readonly ISignatureAlgorithmProvider _signatureAlgorithmProvider;
        private readonly IMageCli _mageCli;
        private readonly IManifestSigner _manifestSigner;
        private readonly IFileMatcher _fileMatcher;
        private readonly IClickOnceAppFactory _clickOnceAppFactory;
        private readonly IManifestReader _manifestReader;

        // Dependency injection requires a public constructor.
        public ClickOnceSigner2(
            ISignatureAlgorithmProvider signatureAlgorithmProvider,
            ICertificateProvider certificateProvider,
            IServiceProvider serviceProvider,
            IMageCli mageCli,
            IManifestSigner manifestSigner,
            IFileMatcher fileMatcher,
            IClickOnceAppFactory clickOnceAppFactory,
            IManifestReader manifestReader,
            ILogger<IDataFormatSigner> logger)
            : base(logger)
        {
            ArgumentNullException.ThrowIfNull(signatureAlgorithmProvider, nameof(signatureAlgorithmProvider));
            ArgumentNullException.ThrowIfNull(certificateProvider, nameof(certificateProvider));
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));
            ArgumentNullException.ThrowIfNull(mageCli, nameof(mageCli));
            ArgumentNullException.ThrowIfNull(manifestSigner, nameof(manifestSigner));
            ArgumentNullException.ThrowIfNull(fileMatcher, nameof(fileMatcher));
            ArgumentNullException.ThrowIfNull(clickOnceAppFactory, nameof(clickOnceAppFactory));
            ArgumentNullException.ThrowIfNull(manifestReader, nameof(manifestReader));

            _signatureAlgorithmProvider = signatureAlgorithmProvider;
            _certificateProvider = certificateProvider;
            _mageCli = mageCli;
            _manifestSigner = manifestSigner;
            _fileMatcher = fileMatcher;
            _clickOnceAppFactory = clickOnceAppFactory;
            _manifestReader = manifestReader;

            // Need to delay this as it'd create a dependency loop if directly in the ctor
            _aggregatingSigner = new Lazy<IAggregatingDataFormatSigner>(() => serviceProvider.GetService<IAggregatingDataFormatSigner>()!);
        }

        public bool CanSign(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return file.Extension.ToLowerInvariant() switch
            {
                ".vsto" or ".application" => true,
                ".manifest" => IsClickOnceApplicationManifest(file),
                _ => false
            };
        }

        private bool IsClickOnceApplicationManifest(FileInfo file)
        {
            try
            {
                // Use high-confidence check to ensure this is a ClickOnce application manifest
                // and not a side-by-side (Win32) manifest which also uses .manifest extension
                using FileStream stream = file.OpenRead();
                return _manifestReader.TryReadApplicationManifest(stream, out _);
            }
            catch
            {
                // If we can't read the file or it's malformed, it's not a valid ClickOnce manifest
                return false;
            }
        }

        public async Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            Logger.LogInformation(Resources.ClickOnceSignatureProviderSigning, files.Count());

            var args = "-a sha256RSA";
            if (!string.IsNullOrWhiteSpace(options.ApplicationName))
            {
                args += $@" -n ""{options.ApplicationName}""";
            }

            Uri? timeStampUrl = options.TimestampService;

            using (X509Certificate2 certificate = await _certificateProvider.GetCertificateAsync())
            using (RSA rsaPrivateKey = await _signatureAlgorithmProvider.GetRsaAsync())
            {
                foreach (FileInfo file in files)
                {
                    // Try to read as deployment manifest first
                    if (_clickOnceAppFactory.TryReadFromDeploymentManifest(file, Logger, out IClickOnceApp? clickOnceApp))
                    {
                        await SignDeploymentManifestAsync(clickOnceApp, args, rsaPrivateKey, certificate, options);

                        continue;
                    }

                    IApplicationManifest? applicationManifest = null;

                    // Close the stream before calling SignStandaloneApplicationManifestAsync
                    // so that mage.exe can open the file for writing with -update.
                    await using (FileStream stream = file.OpenRead())
                    {
                        _manifestReader.TryReadApplicationManifest(stream, out applicationManifest);
                    }

                    if (applicationManifest is null)
                    {
                        Logger.Log(LogLevel.Trace, "{filePath} is not a ClickOnce manifest.", file.FullName);
                    }
                    else
                    {
                        await SignStandaloneApplicationManifestAsync(file, applicationManifest, args, rsaPrivateKey, certificate, options);
                    }
                }
            }
        }

        private async Task SignDeploymentManifestAsync(
            IClickOnceApp clickOnceApp,
            string args,
            RSA rsaPrivateKey,
            X509Certificate2 certificate,
            SignOptions options)
        {
            // When --no-update-clickonce-manifest is specified, skip all discovery,
            // metadata updates, and dependency signing. Only the explicitly specified
            // deployment manifest will be signed.
            if (!options.NoUpdateClickOnceManifest)
            {
                ResolveApplicationManifest(clickOnceApp, Logger);

                if (clickOnceApp.ApplicationManifest is null)
                {
                    string targetPath = clickOnceApp.DeploymentManifest.EntryPoint?.TargetPath ?? string.Empty;
                    string message = string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.ApplicationManifestNotFoundWithSuggestion,
                        targetPath);

                    throw new SigningException(message);
                }

                // get the files, _including_ the SignOptions, so that we only actually try to sign the files specified.
                // this is useful if e.g. you don't want to sign third-party assemblies that your application depends on
                // but you do still want to sign your own assemblies.
                List<FileInfo> filteredFiles = GetFiles(clickOnceApp, options).ToList();
                List<FileInfo> deployFiles = new();

                // Exclude the deployment and application manifest files from the files to sign,
                // because they need to be signed after the inner files are signed.
                List<FileInfo> filesToSign = filteredFiles
                    .Where(file => !FileInfoComparer.Instance.Equals(file, clickOnceApp.ApplicationManifestFile)
                        && !FileInfoComparer.Instance.Equals(file, clickOnceApp.DeploymentManifestFile))
                    .ToList();

                RemoveDeployExtension(clickOnceApp, filteredFiles, ref deployFiles);

                // sign the inner files (unless --no-sign-clickonce-deps is specified)
                if (!options.NoSignClickOnceDeps)
                {
                    await _aggregatingSigner.Value.SignAsync(filesToSign, options);
                }

                // rename the rest of the deploy files since signing the manifest will need them.
                // this uses the overload of GetFiles() that ignores file matching options because we
                // require all files to be named correctly in order to generate valid manifests.
                List<FileInfo> filesExceptFiltered = GetFiles(clickOnceApp).Except(filteredFiles, FileInfoComparer.Instance).ToList();

                RemoveDeployExtension(clickOnceApp, filesExceptFiltered, ref deployFiles);

                // at this point contentFiles has all deploy files renamed

                // Update application manifest file info (hashes, sizes, identities) after payload signing.
                // UpdateFileInfo() hashes files at their ResolvedPath, which does not include .deploy;
                // the suffixes must be absent when hashes are computed.
                if (clickOnceApp.ApplicationManifest is not null)
                {
                    clickOnceApp.ApplicationManifest.UpdateFileInfo();
                }

                if (clickOnceApp.ApplicationManifest is not null)
                {
                    ResolveApplicationManifest(clickOnceApp, Logger);
                }

                // Restore .deploy extensions after updating manifest metadata
                RestoreDeployExtension(clickOnceApp, deployFiles);

                // Inner files are now signed
                // now look for the manifest file and sign that if we have one
                // (unless --no-sign-clickonce-deps is specified, in which case only the
                // explicitly provided deployment manifest is signed)

                if (!options.NoSignClickOnceDeps &&
                    clickOnceApp.ApplicationManifestFile is not null &&
                    filteredFiles.Any(file => FileInfoComparer.Instance.Equals(file, clickOnceApp.ApplicationManifestFile)))
                {
                    if (options.SignedFileTracker.HasSigned(clickOnceApp.ApplicationManifestFile!))
                    {
                        Logger.LogTrace("Skipping application manifest '{FilePath}' - already signed.", clickOnceApp.ApplicationManifestFile!.FullName);
                    }
                    else
                    {
                        string filePath = clickOnceApp.ApplicationManifestFile!.FullName;

                        string appFileArgs = $@"-update ""{filePath}"" {args}";

                        if (!await SignAsync(appFileArgs, clickOnceApp.ApplicationManifestFile, rsaPrivateKey, certificate, options))
                        {
                            string message = string.Format(CultureInfo.CurrentCulture, Resources.SigningFailed, filePath);

                            throw new SigningException(message);
                        }

                        options.SignedFileTracker.MarkAsSigned(clickOnceApp.ApplicationManifestFile);
                    }
                }

                // Update deployment manifest metadata to reflect the current state of referenced files.
                // The deployment manifest only references the application manifest (which does
                // not have a .deploy extension), so no stripping is needed here.
                clickOnceApp.DeploymentManifest!.ResolveFiles([clickOnceApp.DeploymentManifestFile!.DirectoryName!]);
                clickOnceApp.DeploymentManifest.UpdateFileInfo();
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

            string fileArgs = $@"-update ""{clickOnceApp.DeploymentManifestFile!.FullName}"" {args} {publisherParam}";

            if (clickOnceApp.ApplicationManifestFile is not null)
            {
                fileArgs += $@" -appm ""{clickOnceApp.ApplicationManifestFile.FullName}""";
            }

            if (options.DescriptionUrl is not null)
            {
                fileArgs += $@" -SupportURL {options.DescriptionUrl.AbsoluteUri}";
            }

            // Check if deployment manifest has already been signed
            if (options.SignedFileTracker.HasSigned(clickOnceApp.DeploymentManifestFile))
            {
                Logger.LogTrace("Skipping deployment manifest '{FilePath}' - already signed.", clickOnceApp.DeploymentManifestFile.FullName);
            }
            else
            {
                if (!await SignAsync(fileArgs, clickOnceApp.DeploymentManifestFile, rsaPrivateKey, certificate, options))
                {
                    string message = string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.SigningFailed,
                        clickOnceApp.DeploymentManifestFile.FullName);

                    throw new SigningException(message);
                }

                // Mark deployment manifest as signed
                options.SignedFileTracker.MarkAsSigned(clickOnceApp.DeploymentManifestFile);
            }
        }

        private async Task SignStandaloneApplicationManifestAsync(
            FileInfo applicationManifestFile,
            IApplicationManifest applicationManifest,
            string args,
            RSA rsaPrivateKey,
            X509Certificate2 certificate,
            SignOptions options)
        {
            // This is the standalone application manifest signing path (no deployment manifest)
            if (!applicationManifestFile.Exists)
            {
                string message = string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.ApplicationManifestNotFound);

                throw new SigningException(message);
            }

            // Check if already signed
            if (options.SignedFileTracker.HasSigned(applicationManifestFile))
            {
                Logger.LogTrace("Skipping application manifest '{FilePath}' - already signed.", applicationManifestFile.FullName);
                return;
            }

            string applicationManifestDirectoryPath = applicationManifestFile.Directory!.FullName;

            // When --no-update-clickonce-manifest is specified, skip all discovery,
            // metadata updates, and dependency signing. Only the explicitly specified
            // application manifest will be signed.
            if (!options.NoUpdateClickOnceManifest)
            {
                applicationManifest.ResolveFiles([applicationManifestDirectoryPath]);

                if (ClickOnceApp.LogOutputMessages(applicationManifest, Logger))
                {
                    Logger.LogWarning("Application manifest reported errors after resolving files. See preceding log entries for details.");
                }

                applicationManifest.OutputMessages.Clear();

                // Get payload files to sign
                List<FileInfo> payloadFiles = GetPayloadFilesFromManifest(applicationManifest, applicationManifestDirectoryPath, mapFileExtensions: false);

                // Apply file matching if specified
                List<FileInfo> allFiles = new() { applicationManifestFile };
                allFiles.AddRange(payloadFiles);

                List<FileInfo> filteredFiles;
                if (options.Matcher is not null)
                {
                    InMemoryDirectoryInfo wrapper = new(
                        applicationManifestFile.DirectoryName!,
                        allFiles.Select(file => file.FullName));

                    filteredFiles = _fileMatcher.EnumerateMatches(wrapper, options.Matcher).ToList();
                }
                else
                {
                    filteredFiles = allFiles;
                }

                if (options.AntiMatcher is not null)
                {
                    InMemoryDirectoryInfo wrapper = new(
                        applicationManifestFile.DirectoryName!,
                        allFiles.Select(file => file.FullName));
                    IEnumerable<FileInfo> antiFiles = _fileMatcher.EnumerateMatches(wrapper, options.AntiMatcher);

                    filteredFiles = filteredFiles.Except(antiFiles, FileInfoComparer.Instance).ToList();
                }

                // Exclude the application manifest itself from the files to sign
                // because it needs to be signed after the payload files are signed
                List<FileInfo> filesToSign = filteredFiles
                    .Where(file => !FileInfoComparer.Instance.Equals(file, applicationManifestFile))
                    .ToList();

                // Sign payload files (unless --no-sign-clickonce-deps is specified)
                if (!options.NoSignClickOnceDeps && filesToSign.Any())
                {
                    await _aggregatingSigner.Value.SignAsync(filesToSign, options);
                }

                // Update application manifest file info (hashes, sizes, identities) after payload signing
                applicationManifest.UpdateFileInfo();
            }

            // Sign the application manifest
            string filePath = applicationManifestFile.FullName;
            string fileArgs = $@"-update ""{filePath}"" {args}";

            if (!await SignAsync(fileArgs, applicationManifestFile, rsaPrivateKey, certificate, options))
            {
                string message = string.Format(CultureInfo.CurrentCulture, Resources.SigningFailed, filePath);
                throw new SigningException(message);
            }

            options.SignedFileTracker.MarkAsSigned(applicationManifestFile);
        }

        private static List<FileInfo> GetPayloadFilesFromManifest(
            IApplicationManifest applicationManifest,
            string applicationManifestDirectoryPath,
            bool mapFileExtensions)
        {
            List<FileInfo> payloadFiles = new();

            foreach (Microsoft.Build.Tasks.Deployment.ManifestUtilities.BaseReference baseReference in applicationManifest.AssemblyReferences)
            {
                AddPayloadFile(baseReference, applicationManifestDirectoryPath, mapFileExtensions, DeployExtension, payloadFiles);
            }

            foreach (Microsoft.Build.Tasks.Deployment.ManifestUtilities.BaseReference baseReference in applicationManifest.FileReferences)
            {
                AddPayloadFile(baseReference, applicationManifestDirectoryPath, mapFileExtensions, DeployExtension, payloadFiles);
            }

            return payloadFiles;
        }

        private static void AddPayloadFile(
            Microsoft.Build.Tasks.Deployment.ManifestUtilities.BaseReference baseReference,
            string applicationManifestDirectoryPath,
            bool mapFileExtensions,
            string DeployExtension,
            List<FileInfo> payloadFiles)
        {
            string? sourcePath = null;

            // When mapFileExtensions is true, prefer constructing from TargetPath to get .deploy files
            if (mapFileExtensions && !string.IsNullOrEmpty(applicationManifestDirectoryPath) && !string.IsNullOrEmpty(baseReference.TargetPath))
            {
                string relativeFilePath = baseReference.TargetPath.Replace('/', Path.DirectorySeparatorChar);

                if (!relativeFilePath.EndsWith(DeployExtension, StringComparison.OrdinalIgnoreCase))
                {
                    relativeFilePath += DeployExtension;
                }

                string candidatePath = Path.Combine(applicationManifestDirectoryPath, relativeFilePath);

                if (File.Exists(candidatePath))
                {
                    sourcePath = candidatePath;
                }
            }

            // If we haven't found via TargetPath, try ResolvedPath
            if (string.IsNullOrEmpty(sourcePath) && !string.IsNullOrEmpty(baseReference.ResolvedPath) && File.Exists(baseReference.ResolvedPath))
            {
                // When mapFileExtensions is true, prefer ResolvedPath if it has .deploy suffix
                if (mapFileExtensions)
                {
                    if (baseReference.ResolvedPath.EndsWith(DeployExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        sourcePath = baseReference.ResolvedPath;
                    }
                    else
                    {
                        // Try adding .deploy to ResolvedPath
                        string candidateWithDeploy = baseReference.ResolvedPath + DeployExtension;
                        if (File.Exists(candidateWithDeploy))
                        {
                            sourcePath = candidateWithDeploy;
                        }
                        else
                        {
                            // Fall back to ResolvedPath even without .deploy
                            sourcePath = baseReference.ResolvedPath;
                        }
                    }
                }
                else
                {
                    sourcePath = baseReference.ResolvedPath;
                }
            }

            // Final fallback: construct from TargetPath without .deploy check
            if (string.IsNullOrEmpty(sourcePath) && !string.IsNullOrEmpty(applicationManifestDirectoryPath) && !string.IsNullOrEmpty(baseReference.TargetPath))
            {
                string relativeFilePath = baseReference.TargetPath.Replace('/', Path.DirectorySeparatorChar);
                string candidatePath = Path.Combine(applicationManifestDirectoryPath, relativeFilePath);

                if (File.Exists(candidatePath))
                {
                    sourcePath = candidatePath;
                }
            }

            if (!string.IsNullOrEmpty(sourcePath))
            {
                payloadFiles.Add(new FileInfo(sourcePath));
            }
        }

        private static void RemoveDeployExtension(IClickOnceApp clickOnceApp, List<FileInfo> files, ref List<FileInfo> deployFiles)
        {
            if (clickOnceApp.DeploymentManifest.MapFileExtensions)
            {
                foreach (FileInfo file in files)
                {
                    if (DeployExtension.Equals(file.Extension, StringComparison.OrdinalIgnoreCase))
                    {
                        // Rename to file without .deploy extension
                        // For example:
                        //      *  MyApp.dll.deploy => MyApp.dll
                        //      *  MyApp.exe.deploy => MyApp.exe
                        string newFilePath = Path.Combine(
                            file.DirectoryName!,
                            Path.GetFileNameWithoutExtension(file.Name));
                        FileInfo deployFile = new(newFilePath);

                        file.MoveTo(deployFile.FullName);

                        deployFiles.Add(deployFile);
                    }
                }
            }
        }

        private void RestoreDeployExtension(IClickOnceApp clickOnceApp, List<FileInfo> deployFiles)
        {
            if (clickOnceApp.DeploymentManifest.MapFileExtensions && deployFiles is not null)
            {
                // If the manifest maps file extensions, we need to rename the files
                // that are not already renamed.
                foreach (FileInfo deployFileToSign in deployFiles)
                {
                    // Rename to file with .deploy extension
                    // For example:
                    //      *  MyApp.dll => MyApp.dll.deploy
                    //      *  MyApp.exe => MyApp.exe.deploy
                    string payloadFilePath = $"{deployFileToSign.FullName}.deploy";
                    FileInfo payloadFile = new(payloadFilePath);

                    deployFileToSign.MoveTo(payloadFile.FullName);
                }
            }
        }

        /// <summary>
        /// Temporarily strips .deploy extensions from files in the given directories
        /// so that manifest resolution can locate them by their original names.
        /// Returns a list of (strippedPath, originalPath) tuples for restoration.
        /// If files are already stripped by an outer caller, this is a no-op.
        /// </summary>
        private static List<(string Stripped, string Original)> StripDeployExtensionFromDirectories(IReadOnlyList<string> directories)
        {
            List<(string Stripped, string Original)> renamedFiles = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            foreach (string directory in directories)
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (string deployFilePath in Directory.EnumerateFiles(directory, "*" + DeployExtension, SearchOption.AllDirectories))
                {
                    if (!seen.Add(deployFilePath))
                    {
                        continue;
                    }

                    string strippedPath = deployFilePath[..^DeployExtension.Length];

                    if (!File.Exists(strippedPath))
                    {
                        File.Move(deployFilePath, strippedPath);
                        renamedFiles.Add((strippedPath, deployFilePath));
                    }
                }
            }

            return renamedFiles;
        }

        private static void RestoreDeployExtensionToFiles(List<(string Stripped, string Original)> renamedFiles)
        {
            foreach ((string stripped, string original) in renamedFiles)
            {
                if (File.Exists(stripped) && !File.Exists(original))
                {
                    File.Move(stripped, original);
                }
            }
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

        private static IEnumerable<FileInfo> GetFiles(IClickOnceApp clickOnceApp)
        {
            yield return clickOnceApp.DeploymentManifestFile;

            List<FileInfo> bootstrapperFiles = clickOnceApp.DeploymentManifestFile.Directory!.GetFiles("*.exe")
                    .Where(file => string.Equals(file.Name, "setup.exe", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(file.Name, "Launcher.exe", StringComparison.OrdinalIgnoreCase))
                    .ToList();

            foreach (FileInfo bootstrapperFile in bootstrapperFiles)
            {
                yield return bootstrapperFile;
            }

            if (clickOnceApp.ApplicationManifestFile is not null)
            {
                yield return clickOnceApp.ApplicationManifestFile;

                foreach (FileInfo file in clickOnceApp.GetPayloadFiles())
                {
                    yield return file;
                }
            }
        }

        private IEnumerable<FileInfo> GetFiles(IClickOnceApp clickOnceApp, SignOptions options)
        {
            IEnumerable<FileInfo> allFiles = GetFiles(clickOnceApp);
            IEnumerable<FileInfo> files;

            if (options.Matcher is null)
            {
                files = allFiles;
            }
            else
            {
                InMemoryDirectoryInfo wrapper = new(
                    clickOnceApp.DeploymentManifestFile.DirectoryName!,
                    allFiles.Select(file => file.FullName));

                files = _fileMatcher.EnumerateMatches(wrapper, options.Matcher);
            }

            if (options.AntiMatcher is not null)
            {
                InMemoryDirectoryInfo wrapper = new(
                   clickOnceApp.DeploymentManifestFile.DirectoryName!,
                   allFiles.Select(file => file.FullName));
                IEnumerable<FileInfo> antiFiles = _fileMatcher.EnumerateMatches(wrapper, options.AntiMatcher);

                files = files.Except(antiFiles, FileInfoComparer.Instance).ToList();
            }

            return files;
        }

        public void CopySigningDependencies(FileInfo deploymentManifestFile, DirectoryInfo destination, SignOptions signOptions)
        {
            ArgumentNullException.ThrowIfNull(deploymentManifestFile, nameof(deploymentManifestFile));
            ArgumentNullException.ThrowIfNull(destination, nameof(destination));
            ArgumentNullException.ThrowIfNull(signOptions, nameof(signOptions));

            if (!_clickOnceAppFactory.TryReadFromDeploymentManifest(deploymentManifestFile, Logger, out IClickOnceApp? clickOnceApp))
            {
                Logger.Log(LogLevel.Trace, "{filePath} is not a ClickOnce deployment manifest.", deploymentManifestFile.FullName);

                return;
            }

            HashSet<string> stagedFiles = new(PathComparer);

            if (clickOnceApp.ApplicationManifest is not null)
            {
                ResolveApplicationManifest(clickOnceApp, Logger);

                CopyApplicationManifestFiles(clickOnceApp, destination, stagedFiles);
                CopyPayloadFiles(clickOnceApp, destination, stagedFiles);
            }

            CopyBootstrapperFiles(clickOnceApp, destination, stagedFiles);
        }

        private static void ResolveApplicationManifest(IClickOnceApp clickOnceApp, ILogger logger)
        {
            if (clickOnceApp.ApplicationManifest is null)
            {
                return;
            }

            if (clickOnceApp.ApplicationManifest.AssemblyReferences.Count == 0 &&
                clickOnceApp.ApplicationManifest.FileReferences.Count == 0)
            {
                return;
            }

            List<string> searchPaths = new(2);

            if (clickOnceApp.ApplicationManifestFile?.DirectoryName is string applicationDirectory)
            {
                searchPaths.Add(applicationDirectory);
            }

            if (clickOnceApp.DeploymentManifestFile.DirectoryName is string deploymentDirectory &&
                (searchPaths.Count == 0 || !string.Equals(searchPaths[0], deploymentDirectory, StringComparison.OrdinalIgnoreCase)))
            {
                searchPaths.Add(deploymentDirectory);
            }

            if (searchPaths.Count == 0)
            {
                return;
            }

            // When MapFileExtensions is true, payload files on disk have .deploy suffixes
            // but the manifest references them without the suffix. Temporarily strip .deploy
            // so that ResolveFiles() can locate them. If files are already stripped by an
            // outer caller, this is a no-op.
            List<(string Stripped, string Original)> renamedFiles = clickOnceApp.DeploymentManifest.MapFileExtensions
                ? StripDeployExtensionFromDirectories(searchPaths)
                : [];

            try
            {
                clickOnceApp.ApplicationManifest.ResolveFiles(searchPaths.ToArray());

                bool hasErrors = ClickOnceApp.LogOutputMessages(clickOnceApp.ApplicationManifest, logger);
                clickOnceApp.ApplicationManifest.OutputMessages.Clear();

                if (hasErrors)
                {
                    // ResolveFiles() commonly produces non-fatal error-level OutputMessages
                    // (e.g., assembly metadata mismatches, optional references). Log a warning
                    // rather than failing, since signing can typically proceed successfully.
                    logger.LogWarning("Application manifest reported errors after resolving files. See preceding log entries for details.");
                }
            }
            finally
            {
                RestoreDeployExtensionToFiles(renamedFiles);
            }
        }

        private static void CopyApplicationManifestFiles(IClickOnceApp clickOnceApp, DirectoryInfo destination, HashSet<string> stagedFiles)
        {
            if (clickOnceApp.ApplicationManifestFile is null)
            {
                return;
            }

            string relativePath = Path.GetRelativePath(
                clickOnceApp.DeploymentManifestFile.DirectoryName!,
                clickOnceApp.ApplicationManifestFile.FullName);

            CopyFileToDestination(clickOnceApp.ApplicationManifestFile.FullName, relativePath, destination, stagedFiles);
        }

        private static void CopyPayloadFiles(IClickOnceApp clickOnceApp, DirectoryInfo destination, HashSet<string> stagedFiles)
        {
            if (clickOnceApp.ApplicationManifest is null)
            {
                return;
            }

            string? applicationDirectory = clickOnceApp.ApplicationManifestFile?.DirectoryName;
            string? deploymentDirectory = clickOnceApp.DeploymentManifestFile.DirectoryName;

            if (string.IsNullOrEmpty(applicationDirectory) || string.IsNullOrEmpty(deploymentDirectory))
            {
                return;
            }

            foreach (FileInfo payloadFile in clickOnceApp.GetPayloadFiles())
            {
                string relativeFromDeployment = Path.GetRelativePath(deploymentDirectory, payloadFile.FullName);

                if (!string.IsNullOrEmpty(relativeFromDeployment) &&
                    !relativeFromDeployment.StartsWith("..", StringComparison.Ordinal))
                {
                    CopyFileToDestination(payloadFile.FullName, relativeFromDeployment, destination, stagedFiles);
                }
            }
        }

        private static void CopyBootstrapperFiles(IClickOnceApp clickOnceApp, DirectoryInfo destination, HashSet<string> stagedFiles)
        {
            DirectoryInfo? deploymentDirectory = clickOnceApp.DeploymentManifestFile.Directory;

            if (deploymentDirectory is null)
            {
                return;
            }

            foreach (FileInfo bootstrapper in deploymentDirectory.GetFiles("*.exe"))
            {
                if (!string.Equals(bootstrapper.Name, "setup.exe", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(bootstrapper.Name, "Launcher.exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relativePath = Path.GetRelativePath(deploymentDirectory.FullName, bootstrapper.FullName);

                CopyFileToDestination(bootstrapper.FullName, relativePath, destination, stagedFiles);
            }
        }

        private static void CopyFileToDestination(string sourcePath, string relativePath, DirectoryInfo destination, HashSet<string> stagedFiles)
        {
            string normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);

            string fullDestinationPath = Path.Combine(destination.FullName, normalizedRelativePath);

            if (!stagedFiles.Add(fullDestinationPath))
            {
                return;
            }

            string? directoryPath = Path.GetDirectoryName(fullDestinationPath);

            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            File.Copy(sourcePath, fullDestinationPath, overwrite: true);
        }

    }
}
