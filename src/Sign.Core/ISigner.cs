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
            string? publisherName,
            string? description,
            Uri? descriptionUrl,
            Uri? timestampUrl,
            int maxConcurrency,
            HashAlgorithmName fileHashAlgorithm,
            HashAlgorithmName timestampHashAlgorithm,
            TokenCredential tokenCredential,
            Uri keyVaultUrl,
            string certificateName);
    }
}