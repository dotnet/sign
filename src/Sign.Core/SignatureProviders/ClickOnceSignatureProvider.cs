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
    internal sealed class ClickOnceSignatureProvider : RetryingSignatureProvider, ISignatureProvider
    {
        private readonly Lazy<IAggregatingSignatureProvider> _aggregatingSignatureProvider;
        private readonly ICertificateProvider _certificateProvider;
        private readonly ISignatureAlgorithmProvider _signatureAlgorithmProvider;
        private readonly IMageCli _mageCli;
        private readonly IManifestSigner _manifestSigner;
        private readonly ParallelOptions _parallelOptions = new() { MaxDegreeOfParallelism = 4 };
        private readonly IFileMatcher _fileMatcher;

        // Dependency injection requires a public constructor.
        public ClickOnceSignatureProvider(
            ISignatureAlgorithmProvider signatureAlgorithmProvider,
            ICertificateProvider certificateProvider,
            IServiceProvider serviceProvider,
            IMageCli mageCli,
            IManifestSigner manifestSigner,
            ILogger<ISignatureProvider> logger,
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
            _aggregatingSignatureProvider = new Lazy<IAggregatingSignatureProvider>(() => serviceProvider.GetService<IAggregatingSignatureProvider>()!);
        }

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
                // This outer loop is for a deployment manifest file (.application/.vsto).
                await Parallel.ForEachAsync(files, _parallelOptions, async (file, state) =>
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

                    DirectoryInfo clickOnceDirectory = file.Directory!;

                    // get the files, _including_ the SignOptions, so that we only actually try to sign the files specified.
                    // this is useful if e.g. you don't want to sign third-party assemblies that your application depends on
                    // but you do still want to sign your own assemblies.
                    List<FileInfo> filteredFiles = GetFiles(clickOnceDirectory, options).ToList();
                    List<FileInfo> deployFilesToSign = filteredFiles
                        .Where(f => ".deploy".Equals(f.Extension, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    List<FileInfo> contentFiles = new();

                    RemoveDeployExtension(deployFilesToSign, contentFiles);

                    List<FileInfo> filesToSign = contentFiles.ToList(); // copy it since we may add setup.exe
                    IEnumerable<FileInfo> setupExe = filteredFiles.Where(f => ".exe".Equals(f.Extension, StringComparison.OrdinalIgnoreCase));
                    filesToSign.AddRange(setupExe);

                    // sign the inner files
                    await _aggregatingSignatureProvider.Value.SignAsync(filesToSign!, options);

                    // rename the rest of the deploy files since signing the manifest will need them.
                    // this uses the overload of GetFiles() that ignores file matching options because we
                    // require all files to be named correctly in order to generate valid manifests.
                    List<FileInfo> filesExceptFiltered = GetFiles(clickOnceDirectory).Except(filteredFiles, FileInfoComparer.Instance).ToList();
                    List<FileInfo> deployFiles = filesExceptFiltered
                        .Where(f => ".deploy".Equals(f.Extension, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    RemoveDeployExtension(deployFiles, contentFiles);

                    // at this point contentFiles has all deploy files renamed

                    // Inner files are now signed
                    // now look for the manifest file and sign that if we have one

                    FileInfo? manifestFile = filteredFiles.SingleOrDefault(f => ".manifest".Equals(f.Extension, StringComparison.OrdinalIgnoreCase));

                    string fileArgs = $@"-update ""{manifestFile}"" {args}";

                    if (manifestFile is not null && !await SignAsync(fileArgs, manifestFile, rsaPrivateKey, certificate, options))
                    {
                        string message = string.Format(CultureInfo.CurrentCulture, Resources.SigningFailed, manifestFile.FullName);

                        throw new Exception(message);
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
                    List<FileInfo> deploymentManifestFiles = filteredFiles
                        .Where(f => ".vsto".Equals(f.Extension, StringComparison.OrdinalIgnoreCase) ||
                                    ".application".Equals(f.Extension, StringComparison.OrdinalIgnoreCase))
                        .Select(f => new { file = f, f.FullName.Length })
                        .OrderByDescending(f => f.Length)
                        .Select(f => f.file)
                        .ToList();

                    foreach (FileInfo deploymentManifestFile in deploymentManifestFiles)
                    {
                        fileArgs = $@"-update ""{deploymentManifestFile.FullName}"" {args} {publisherParam}";
                        if (manifestFile is not null)
                        {
                            fileArgs += $@" -appm ""{manifestFile.FullName}""";
                        }
                        if (options.DescriptionUrl is not null)
                        {
                            fileArgs += $@" -SupportURL {options.DescriptionUrl.AbsoluteUri}";
                        }

                        if (!await SignAsync(fileArgs, deploymentManifestFile, rsaPrivateKey, certificate, options))
                        {
                            string message = string.Format(CultureInfo.CurrentCulture, Resources.SigningFailed, deploymentManifestFile.FullName);

                            throw new Exception(message);
                        }
                    }

                    // restore the .deploy files
                    foreach (FileInfo contentFile in contentFiles)
                    {
                        File.Move(contentFile.FullName, $"{contentFile.FullName}.deploy");
                    }
                });
            }
        }

        private static void RemoveDeployExtension(List<FileInfo> deployFilesToSign, List<FileInfo> contentFiles)
        {
            foreach (FileInfo deployFileToSign in deployFilesToSign)
            {
                // Rename to file without .deploy extension
                // For example:
                //      *  MyApp.dll.deploy => MyApp.dll
                //      *  MyApp.exe.deploy => MyApp.exe
                string contentFilePath = Path.Combine(
                    deployFileToSign.DirectoryName!,
                    Path.GetFileNameWithoutExtension(deployFileToSign.Name));
                FileInfo contentFile = new(contentFilePath);

                File.Move(deployFileToSign.FullName, contentFile.FullName);

                contentFiles.Add(contentFile);
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


        private IEnumerable<FileInfo> GetFiles(DirectoryInfo clickOnceRoot)
        {
            return clickOnceRoot.EnumerateFiles("*", SearchOption.AllDirectories);
        }

        private IEnumerable<FileInfo> GetFiles(DirectoryInfo clickOnceRoot, SignOptions options)
        {
            IEnumerable<FileInfo> files;

            if (options.Matcher is null)
            {
                // If not filtered, default to all
                files = GetFiles(clickOnceRoot);
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
            return files;
        }

        public void CopySigningDependencies(FileInfo deploymentManifestFile, DirectoryInfo destination, SignOptions signOptions)
        {
            // copy _all_ files, ignoring matching options, because we need them to be available to generate
            // valid manifests.
            foreach (FileInfo file in GetFiles(deploymentManifestFile.Directory!))
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