// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class NuGetSignTool : Tool, INuGetSignTool
    {
        // Dependency injection requires a public constructor.
        public NuGetSignTool(ILogger<INuGetSignTool> logger)
            : base(logger)
        {
        }

        public async Task<bool> SignAsync(FileInfo packageFile, RSA rsa, X509Certificate2 certificate, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(packageFile, nameof(packageFile));
            ArgumentNullException.ThrowIfNull(rsa, nameof(rsa));
            ArgumentNullException.ThrowIfNull(certificate, nameof(certificate));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            Logger.LogInformation(Resources.SigningFile, packageFile.FullName);

            NuGetPackageSigner signer = new(Logger);

            var result = false;

            try
            {
                NuGet.Common.HashAlgorithmName fileHashAlgorithm = FromCryptographyName(options.FileHashAlgorithm);
                NuGet.Common.HashAlgorithmName timestampHashAlgorithm = FromCryptographyName(options.TimestampHashAlgorithm);

                result = await signer.SignAsync(
                    packageFile.FullName,
                    packageFile.FullName,
                    options.TimestampService,
                    NuGet.Packaging.Signing.SignatureType.Author,
                    fileHashAlgorithm,
                    timestampHashAlgorithm,
                    certificate,
                    rsa,
                    overwrite: true);
            }
            catch (Exception e)
            {
                Logger.LogError(e, e.Message);
            }

            return result;
        }

        private static NuGet.Common.HashAlgorithmName FromCryptographyName(HashAlgorithmName hashAlgorithmName)
        {
            if (hashAlgorithmName == HashAlgorithmName.SHA256)
            {
                return NuGet.Common.HashAlgorithmName.SHA256;
            }

            if (hashAlgorithmName == HashAlgorithmName.SHA384)
            {
                return NuGet.Common.HashAlgorithmName.SHA384;
            }

            if (hashAlgorithmName == HashAlgorithmName.SHA512)
            {
                return NuGet.Common.HashAlgorithmName.SHA512;
            }

            throw new ArgumentException(nameof(hashAlgorithmName));
        }
    }
}
