// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class ClickOnceSigner : RetryingSigner, IDataFormatSigner
    {
        private readonly Lazy<IAggregatingDataFormatSigner> _aggregatingSigner;
        private readonly ICertificateProvider _certificateProvider;
        private readonly ISignatureAlgorithmProvider _signatureAlgorithmProvider;
        private readonly IMageCli _mageCli;
        private readonly IManifestSigner _manifestSigner;
        private readonly ParallelOptions _parallelOptions = new() { MaxDegreeOfParallelism = 4 };
        private readonly IFileMatcher _fileMatcher;
        private readonly IXmlDocumentLoader _xmlDocumentLoader;

        // Dependency injection requires a public constructor.
        public ClickOnceSigner(
            ISignatureAlgorithmProvider signatureAlgorithmProvider,
            ICertificateProvider certificateProvider,
            IServiceProvider serviceProvider,
            IMageCli mageCli,
            IManifestSigner manifestSigner,
            ILogger<IDataFormatSigner> logger,
            IFileMatcher fileMatcher,
            IXmlDocumentLoader xmlDocumentLoader)
            : base(logger)
        {
            ArgumentNullException.ThrowIfNull(signatureAlgorithmProvider, nameof(signatureAlgorithmProvider));
            ArgumentNullException.ThrowIfNull(certificateProvider, nameof(certificateProvider));
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));
            ArgumentNullException.ThrowIfNull(mageCli, nameof(mageCli));
            ArgumentNullException.ThrowIfNull(manifestSigner, nameof(manifestSigner));
            ArgumentNullException.ThrowIfNull(fileMatcher, nameof(fileMatcher));
            ArgumentNullException.ThrowIfNull(xmlDocumentLoader, nameof(xmlDocumentLoader));

            _signatureAlgorithmProvider = signatureAlgorithmProvider;
            _certificateProvider = certificateProvider;
            _mageCli = mageCli;
            _manifestSigner = manifestSigner;
            _fileMatcher = fileMatcher;
            _xmlDocumentLoader = xmlDocumentLoader;

            // Need to delay this as it'd create a dependency loop if directly in the ctor
            _aggregatingSigner = new Lazy<IAggregatingDataFormatSigner>(() => serviceProvider.GetService<IAggregatingDataFormatSigner>()!);
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
                    await _aggregatingSigner.Value.SignAsync(filesToSign!, options);

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
                    FileInfo? applicationManifestFile = null;
                    if (TryGetApplicationManifestFileName(file, out string? fileName))
                    {
                        applicationManifestFile = filteredFiles.SingleOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                    }

                    string fileArgs = $@"-update ""{applicationManifestFile}"" {args}";

                    if (applicationManifestFile is not null && !await SignAsync(fileArgs, applicationManifestFile, rsaPrivateKey, certificate, options))
                    {
                        string message = string.Format(CultureInfo.CurrentCulture, Resources.SigningFailed, applicationManifestFile.FullName);

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
                        if (applicationManifestFile is not null)
                        {
                            fileArgs += $@" -appm ""{applicationManifestFile.FullName}""";
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

        /// <summary>
        /// Try and find the application manifest (.manifest) file from a ClickOnce application manifest (.application / .vsto
        /// There might not be one, if the user is attempting to only re-sign the deployment manifest without touching other files.
        /// This is necessary because there might be multiple *.manifest files present, e.g. if a DLL that's part of the ClickOnce
        /// package ships its own assembly manifest which isn't a ClickOnce application manifest.
        /// </summary>
        /// <param name="deploymentManifest">A <see cref="FileInfo"/> representing a deployment manifest file.</param>
        /// <param name="applicationManifestFileName">A <see cref="string?"/> representing a manifest file name or <c>null</c> if one isn't found.</param>
        /// <returns><c>true</c> if the application manifest file name was found; otherwise, <c>false</c>.</returns>
        /// <remarks>This is non-private only for unit testing.</remarks>
        internal bool TryGetApplicationManifestFileName(FileInfo deploymentManifest, [NotNullWhen(true)] out string? applicationManifestFileName)
        {
            applicationManifestFileName = null;

            XmlDocument xmlDoc = _xmlDocumentLoader.Load(deploymentManifest);

            // there should only be a single result here, if the file is a valid clickonce manifest.
            XmlNodeList dependentAssemblies = xmlDoc.GetElementsByTagName("dependentAssembly");
            if (dependentAssemblies.Count != 1)
            {
                Logger.LogDebug(Resources.ApplicationManifestNotFound);
                return false;
            }

            XmlNode? node = dependentAssemblies.Item(0);
            if (node is null || node.Attributes is null)
            {
                Logger.LogDebug(Resources.ApplicationManifestNotFound);
                return false;
            }

            XmlAttribute? codebaseAttribute = node.Attributes["codebase"];
            if (codebaseAttribute is null || string.IsNullOrEmpty(codebaseAttribute.Value))
            {
                Logger.LogDebug(Resources.ApplicationManifestNotFound);
                return false;
            }

            // The codebase attribute can be a relative file path (e.g. Application Files\MyApp_1_0_0_0\MyApp.dll.manifest) or
            // a URI (e.g. https://my.cdn.com/clickonce/MyApp/ApplicationFiles/MyApp_1_0_0_0/MyApp.dll.manifest) so we need to
            // handle both cases and extract just the file name part.
            //
            // We only try and parse absolute URI's, because a relative URI can just be treated like a file path for our purposes.
            if (Uri.TryCreate(codebaseAttribute.Value, UriKind.Absolute, out Uri? uri))
            {
                applicationManifestFileName = Path.GetFileName(uri.LocalPath); // works for http(s) and file:// uris
            }
            else
            {
                applicationManifestFileName = Path.GetFileName(codebaseAttribute.Value);
            }

            return !string.IsNullOrEmpty(applicationManifestFileName);
        }
    }
}
