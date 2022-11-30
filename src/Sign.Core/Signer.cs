// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Diagnostics;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class Signer : ISigner
    {
        private readonly IServiceProvider _serviceProvider;

        // Dependency injection requires a public constructor.
        public Signer(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider, nameof(serviceProvider));

            _serviceProvider = serviceProvider;
        }

        public async Task<int> SignAsync(
            IReadOnlyList<FileInfo> inputFiles,
            string? outputFile,
            FileInfo? fileList,
            DirectoryInfo baseDirectory,
            string? publisherName,
            string? description,
            Uri? descriptionUrl,
            Uri? timestampUrl,
            int maxConcurrency,
            HashAlgorithmName fileHashAlgorithm,
            HashAlgorithmName timestampHashAlgorithm,
            TokenCredential tokenCredential,
            Uri keyVaultUrl,
            string certificateName)
        {
            IAggregatingSignatureProvider signatureProvider = _serviceProvider.GetRequiredService<IAggregatingSignatureProvider>();
            ILogger<Signer> logger = _serviceProvider.GetRequiredService<ILogger<Signer>>();
            IDirectoryService directoryService = _serviceProvider.GetRequiredService<IDirectoryService>();
            ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = maxConcurrency };

            Matcher? matcher = null;
            Matcher? antiMatcher = null;

            if (fileList is not null)
            {
                IFileListReader fileListReader = _serviceProvider.GetRequiredService<IFileListReader>();

                using (FileStream stream = fileList.OpenRead())
                using (StreamReader reader = new(stream))
                {
                    fileListReader.Read(reader, out matcher, out antiMatcher);
                }
            }

            IKeyVaultService keyVaultService = _serviceProvider.GetRequiredService<IKeyVaultService>();

            keyVaultService.Initialize(keyVaultUrl, tokenCredential, certificateName);

            SignOptions signOptions = new(
                publisherName,
                description,
                descriptionUrl,
                fileHashAlgorithm,
                timestampHashAlgorithm,
                timestampUrl,
                matcher,
                antiMatcher);

            try
            {
                using (X509Certificate2 certificate = await keyVaultService.GetCertificateAsync())
                {
                    ICertificateVerifier certificateVerifier = _serviceProvider.GetRequiredService<ICertificateVerifier>();

                    certificateVerifier.Verify(certificate);
                }

                await Parallel.ForEachAsync(inputFiles, parallelOptions, async (input, token) =>
                {
                    FileInfo output;

                    Stopwatch sw = Stopwatch.StartNew();

                    // Special case if there's only one input file and the output has a value, treat it as a file
                    if (inputFiles.Count == 1 && !string.IsNullOrWhiteSpace(outputFile))
                    {
                        // See if it has a file extension and if not, treat as a directory and use the input file name
                        if (Path.HasExtension(outputFile))
                        {
                            output = new FileInfo(ExpandFilePath(baseDirectory, outputFile));
                        }
                        else
                        {
                            output = new FileInfo(Path.Combine(ExpandFilePath(baseDirectory, outputFile), inputFiles[0].Name));
                        }
                    }
                    else
                    {
                        // if the output is specified, treat it as a directory, if not, overwrite the current file
                        if (!string.IsNullOrWhiteSpace(outputFile))
                        {
                            output = new FileInfo(input.FullName);
                        }
                        else
                        {
                            var relative = Path.GetRelativePath(baseDirectory.FullName, input.FullName);

                            var basePath = Path.IsPathRooted(outputFile) ?
                                           outputFile :
                                           $"{baseDirectory}{Path.DirectorySeparatorChar}{outputFile}";

                            var fullOutput = Path.Combine(basePath, relative);

                            output = new FileInfo(fullOutput);
                        }
                    }

                    //Ensure the output directory exists
                    Directory.CreateDirectory(output.DirectoryName!);

                    //Do action

                    // HttpResponseMessage response;

                    logger.LogInformation($"Submitting '{input.FullName}' for signing.");

                    // this might have two files, one containing the file list
                    // The first will be the package and the second is the filter
                    using (TemporaryDirectory temporaryDirectory = new(directoryService))
                    {
                        var inputFileName = Path.Combine(temporaryDirectory.Directory.FullName, Path.GetRandomFileName());
                        // However check its extension as it might be important (e.g. zip, bundle, etc)
                        if (signatureProvider.CanSign(input))
                        {
                            // Keep the input extenstion as it has significance.
                            inputFileName = Path.ChangeExtension(inputFileName, input.Extension);
                        }

                        logger.LogInformation("SignAsync called for {source}. Using {inputFileName} locally.", input.FullName, inputFileName);

                        if (input.Length > 0)
                        {
                            input.CopyTo(inputFileName, overwrite: true);
                        }

                        FileInfo fi = new(inputFileName);

                        await signatureProvider.SignAsync(new[] { fi }, signOptions);

                        fi.CopyTo(output.FullName, overwrite: true);
                    }

                    logger.LogInformation("Successfully signed '{filePath}' in {millseconds} ms", output.FullName, sw.ElapsedMilliseconds);
                });

            }
            catch (AuthenticationException e)
            {
                logger.LogError(e, e.Message);
                return ExitCode.Failed;
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
                return ExitCode.Failed;
            }

            return ExitCode.Success;
        }

        private static string ExpandFilePath(DirectoryInfo baseDirectory, string file)
        {
            if (!Path.IsPathRooted(file))
            {
                return Path.Combine(baseDirectory.FullName, file);
            }

            return file;
        }
    }
}