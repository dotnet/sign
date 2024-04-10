// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using NuGetKeyVaultSignTool;

namespace Sign.Core
{
    internal sealed class NuGetSignTool : Tool, INuGetSignTool
    {
        // Dependency injection requires a public constructor.
        public NuGetSignTool(ILogger<INuGetSignTool> logger)
            : base(logger)
        {
        }

        public async Task<bool> SignAsync(FileInfo file, RSA rsaPrivateKey, X509Certificate2 certificate, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            Logger.LogInformation(Resources.SigningFile, file.FullName);

            SignCommand signCommand = new(Logger);

            var result = false;

            try
            {
                NuGet.Common.HashAlgorithmName fileHashAlgorithm = FromCryptographyName(options.FileHashAlgorithm);
                NuGet.Common.HashAlgorithmName timestampHashAlgorithm = FromCryptographyName(options.TimestampHashAlgorithm);

                result = await signCommand.SignAsync(
                    file.FullName,
                    file.FullName,
                    options.TimestampService?.AbsoluteUri,
                    v3ServiceIndex: null,
                    packageOwners: null,
                    NuGet.Packaging.Signing.SignatureType.Author,
                    fileHashAlgorithm,
                    timestampHashAlgorithm,
                    overwrite: true,
                    certificate,
                    rsaPrivateKey);
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