// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Sign.Core
{
    internal sealed class NuGetPackageSigner
    {
        private readonly ILogger _logger;

        public NuGetPackageSigner(ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _logger = logger;
        }

        public async Task<bool> SignAsync(
            string packagePath,
            string outputPath,
            Uri timestampUrl,
            SignatureType signatureType,
            HashAlgorithmName signatureHashAlgorithm,
            HashAlgorithmName timestampHashAlgorithm,
            X509Certificate2 signingCertificate,
            System.Security.Cryptography.RSA rsa,
            bool overwrite,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(packagePath, nameof(packagePath));
            ArgumentException.ThrowIfNullOrEmpty(outputPath, nameof(outputPath));
            ArgumentNullException.ThrowIfNull(timestampUrl, nameof(timestampUrl));
            ArgumentNullException.ThrowIfNull(signingCertificate, nameof(signingCertificate));
            ArgumentNullException.ThrowIfNull(rsa, nameof(rsa));

            bool inPlaceSigning = String.Equals(packagePath, outputPath);
            bool usingWildCards = packagePath.Contains('*') || packagePath.Contains('?');
            IEnumerable<string> packageFilePaths = LocalFolderUtility.ResolvePackageFromPath(packagePath);
            NuGetSignatureProvider signatureProvider = new(rsa, new Rfc3161TimestampProvider(timestampUrl));
            SignPackageRequest? request = null;

            if (signatureType == SignatureType.Author)
            {
                request = new AuthorSignPackageRequest(signingCertificate, signatureHashAlgorithm, timestampHashAlgorithm);
            }
            else
            {
                throw new NotSupportedException(nameof(signatureType));
            }

            foreach (string packageFilePath in packageFilePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string packageFileName = Path.GetFileName(packageFilePath);

                _logger.LogInformation($"{nameof(SignAsync)} [{packageFilePath}]: Begin signing {packageFileName}");

                string? originalPackageCopyPath = null;

                try
                {
                    originalPackageCopyPath = CopyPackage(packageFilePath);
                    string signedPackagePath = outputPath;

                    if (inPlaceSigning)
                    {
                        signedPackagePath = packageFilePath;
                    }
                    else if (usingWildCards)
                    {
                        string? pathName = Path.GetDirectoryName(outputPath + Path.DirectorySeparatorChar);

                        if (!string.IsNullOrEmpty(pathName) && !Directory.Exists(pathName))
                        {
                            Directory.CreateDirectory(pathName);
                        }

                        signedPackagePath = pathName + Path.DirectorySeparatorChar + packageFileName;
                    }

                    using (SigningOptions options = SigningOptions.CreateFromFilePaths(
                        originalPackageCopyPath,
                        signedPackagePath,
                        overwrite,
                        signatureProvider,
                        new NuGetLogger(_logger, packageFilePath)))
                    {
                        await SigningUtility.SignAsync(options, request, cancellationToken);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, e.Message);
                    return false;
                }
                finally
                {
                    if (!string.IsNullOrEmpty(originalPackageCopyPath))
                    {
                        try
                        {
                            FileUtility.Delete(originalPackageCopyPath);
                        }
                        catch
                        {
                        }
                    }

                    _logger.LogInformation($"{nameof(SignAsync)} [{packageFilePath}]: End signing {packageFileName}");
                }
            }

            return true;
        }

        private static string CopyPackage(string sourceFilePath)
        {
            string destFilePath = Path.GetTempFileName();

            File.Copy(sourceFilePath, destFilePath, overwrite: true);

            return destFilePath;
        }

        private static void OverwritePackage(string sourceFilePath, string destFilePath)
        {
            File.Copy(sourceFilePath, destFilePath, overwrite: true);
        }
    }
}
