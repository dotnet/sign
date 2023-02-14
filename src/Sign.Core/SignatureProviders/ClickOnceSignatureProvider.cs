// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class ClickOnceSignatureProvider : RetryingSignatureProvider, ISignatureProvider
    {
        private readonly Lazy<IAggregatingSignatureProvider> _aggregatingSignatureProvider;
        private readonly IContainerProvider _containerProvider;
        private readonly IDirectoryService _directoryService;
        private readonly IKeyVaultService _keyVaultService;
        private readonly IMageCli _mageCli;
        private readonly IManifestSigner _manifestSigner;
        private readonly ParallelOptions _parallelOptions = new() { MaxDegreeOfParallelism = 4 };

        // Dependency injection requires a public constructor.
        public ClickOnceSignatureProvider(
            IKeyVaultService keyVaultService,
            IContainerProvider containerProvider,
            IServiceProvider serviceProvider,
            IDirectoryService directoryService,
            IMageCli mageCli,
            IManifestSigner manifestSigner,
            ILogger<ISignatureProvider> logger)
            : base(logger)
        {
            ArgumentNullException.ThrowIfNull(keyVaultService, nameof(keyVaultService));
            ArgumentNullException.ThrowIfNull(containerProvider, nameof(containerProvider));
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));
            ArgumentNullException.ThrowIfNull(directoryService, nameof(directoryService));
            ArgumentNullException.ThrowIfNull(mageCli, nameof(mageCli));
            ArgumentNullException.ThrowIfNull(manifestSigner, nameof(manifestSigner));

            _keyVaultService = keyVaultService;
            _containerProvider = containerProvider;
            _directoryService = directoryService;
            _mageCli = mageCli;
            _manifestSigner = manifestSigner;

            // Need to delay this as it'd create a dependency loop if directly in the ctor
            _aggregatingSignatureProvider = new Lazy<IAggregatingSignatureProvider>(() => serviceProvider.GetService<IAggregatingSignatureProvider>()!);
        }

        public bool CanSign(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return string.Equals(file.Extension, ".clickonce", StringComparison.OrdinalIgnoreCase);
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

            using (X509Certificate2 certificate = await _keyVaultService.GetCertificateAsync())
            using (RSA rsaPrivateKey = await _keyVaultService.GetRsaAsync())
            {
                // This outer loop is for a .clickonce file
                await Parallel.ForEachAsync(files, _parallelOptions, async (file, state) =>
                {
                    // We need to be explicit about the order these files are signed in. The data files must be signed first
                    // Then the .manifest file
                    // Then the nested clickonce/vsto file
                    // finally the top-level clickonce/vsto file

                    using (TemporaryDirectory temporaryDirectory = new(_directoryService))
                    using (IContainer zip = _containerProvider.GetContainer(file)!)
                    {
                        await zip.OpenAsync();

                        // Look for the data files first - these are .deploy files
                        // we need to rename them, sign, then restore the name

                        List<FileInfo> filteredFiles = GetFiles(zip, options).ToList();
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

                        // rename the rest of the deploy files since signing the manifest will need them
                        List<FileInfo> filesExceptFiltered = zip.GetFiles().Except(filteredFiles, FileInfoComparer.Instance).ToList();
                        List<FileInfo> deployFiles = filesExceptFiltered
                            .Where(f => ".deploy".Equals(f.Extension, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        RemoveDeployExtension(deployFiles, contentFiles);

                        // at this point contentFiles has all deploy files renamed

                        // Inner files are now signed
                        // now look for the manifest file and sign that

                        FileInfo manifestFile = filteredFiles.Single(f => ".manifest".Equals(f.Extension, StringComparison.OrdinalIgnoreCase));

                        string fileArgs = $@"-update ""{manifestFile}"" {args}";

                        if (!await SignAsync(fileArgs, manifestFile, rsaPrivateKey, certificate, options))
                        {
                            string message = string.Format(CultureInfo.CurrentCulture, Resources.SigningFailed, manifestFile.FullName);

                            throw new Exception(message);
                        }

                        string publisherParam = string.Empty;

                        if (string.IsNullOrEmpty(options.PublisherName))
                        {
                            // Read the publisher name from the manifest for use below
                            XDocument manifestDoc = XDocument.Load(manifestFile.FullName);
                            XNamespace ns = manifestDoc.Root!.GetDefaultNamespace();
                            XElement? publisherIdentity = manifestDoc.Root.Element(ns + "publisherIdentity");
                            string publisherName = publisherIdentity!.Attribute("name")!.Value;
                            Dictionary<string, List<string>> dict = DistinguishedNameParser.Parse(publisherName);
 
                            if (dict.TryGetValue("CN", out List<string>? cns))
                            {
                                // get the CN. it may be quoted
                                publisherParam = $@"-pub ""{string.Join("+", cns.Select(s => s.Replace("\"", "")))}"" ";
                            }
                        }
                        else
                        {
                            publisherParam = $"-pub \"{options.PublisherName}\" ";
                        }

                        // Now sign the inner vsto/clickonce file
                        // Order by desending length to put the inner one first
                        List<FileInfo> clickOnceFilesToSign = filteredFiles
                            .Where(f => ".vsto".Equals(f.Extension, StringComparison.OrdinalIgnoreCase) ||
                                        ".application".Equals(f.Extension, StringComparison.OrdinalIgnoreCase))
                            .Select(f => new { file = f, f.FullName.Length })
                            .OrderByDescending(f => f.Length)
                            .Select(f => f.file)
                            .ToList();

                        foreach (FileInfo fileToSign in clickOnceFilesToSign)
                        {
                            fileArgs = $@"-update ""{fileToSign.FullName}"" {args} -appm ""{manifestFile.FullName}"" {publisherParam}";
                            if (options.DescriptionUrl is not null)
                            {
                                fileArgs += $@" -SupportURL {options.DescriptionUrl.AbsoluteUri}";
                            }

                            if (!await SignAsync(fileArgs, fileToSign, rsaPrivateKey, certificate, options))
                            {
                                string message = string.Format(CultureInfo.CurrentCulture, Resources.SigningFailed, fileToSign.FullName);

                                throw new Exception(message);
                            }
                        }

                        // restore the .deploy files
                        foreach (FileInfo contentFile in contentFiles)
                        {
                            File.Move(contentFile.FullName, $"{contentFile.FullName}.deploy");
                        }

                        await zip.SaveAsync();
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

        private static IEnumerable<FileInfo> GetFiles(IContainer container, SignOptions options)
        {
            IEnumerable<FileInfo> files;

            if (options.Matcher is null)
            {
                // If not filtered, default to all
                files = container.GetFiles();
            }
            else
            {
                files = container.GetFiles(options.Matcher);
            }

            if (options.AntiMatcher is not null)
            {
                IEnumerable<FileInfo> antiFiles = container.GetFiles(options.AntiMatcher);

                files = files.Except(antiFiles, FileInfoComparer.Instance).ToList();
            }

            return files;
        }
    }
}