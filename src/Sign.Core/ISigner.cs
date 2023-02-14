// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Azure.Core;

namespace Sign.Core
{
    internal interface ISigner
    {
        Task<int> SignAsync(
            IReadOnlyList<FileInfo> inputFiles,
            string? outputFile,
            FileInfo? fileList,
            DirectoryInfo baseDirectory,
            string? applicationName,
            string? publisherName,
            string? description,
            Uri? descriptionUrl,
            Uri timestampUrl,
            int maxConcurrency,
            HashAlgorithmName fileHashAlgorithm,
            HashAlgorithmName timestampHashAlgorithm,
            TokenCredential tokenCredential,
            Uri keyVaultUrl,
            string certificateName);
    }
}