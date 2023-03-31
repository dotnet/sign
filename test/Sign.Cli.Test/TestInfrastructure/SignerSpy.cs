// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Azure.Core;
using Sign.Core;

namespace Sign.Cli.Test
{
    internal sealed class SignerSpy : ISigner
    {
        internal IReadOnlyList<FileInfo>? InputFiles { get; private set; }
        internal string? OutputFile { get; private set; }
        internal FileInfo? FileList { get; private set; }
        internal DirectoryInfo? BaseDirectory { get; private set; }
        internal string? ApplicationName { get; private set; }
        internal string? PublisherName { get; private set; }
        internal string? Description { get; private set; }
        internal Uri? DescriptionUrl { get; private set; }
        internal Uri? TimestampUrl { get; private set; }
        internal int? MaxConcurrency { get; private set; }
        internal HashAlgorithmName? FileHashAlgorithm { get; private set; }
        internal HashAlgorithmName? TimestampHashAlgorithm { get; private set; }
        internal TokenCredential? TokenCredential { get; private set; }
        internal Uri? KeyVaultUrl { get; private set; }
        internal string? CertificateName { get; private set; }
        internal int ExitCode { get; }

        internal SignerSpy()
        {
            ExitCode = Core.ExitCode.Success;
        }

        public Task<int> SignAsync(
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
            string certificateName)
        {
            InputFiles = inputFiles;
            OutputFile = outputFile;
            FileList = fileList;
            BaseDirectory = baseDirectory;
            ApplicationName = applicationName;
            PublisherName = publisherName;
            Description = description;
            DescriptionUrl = descriptionUrl;
            TimestampUrl = timestampUrl;
            MaxConcurrency = maxConcurrency;
            FileHashAlgorithm = fileHashAlgorithm;
            TimestampHashAlgorithm = timestampHashAlgorithm;
            TokenCredential = tokenCredential;
            KeyVaultUrl = keyVaultUrl;
            CertificateName = certificateName;

            return Task.FromResult(ExitCode);
        }
    }
}