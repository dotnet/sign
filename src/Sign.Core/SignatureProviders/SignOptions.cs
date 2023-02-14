// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Security.Cryptography;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core
{
    internal sealed class SignOptions
    {
        internal string? ApplicationName { get; }
        internal string? PublisherName { get; }
        internal string? Description { get; }
        internal Uri? DescriptionUrl { get; }
        internal Matcher? Matcher { get; }
        internal Matcher? AntiMatcher { get; }
        internal HashAlgorithmName FileHashAlgorithm { get; } = HashAlgorithmName.SHA256;
        internal HashAlgorithmName TimestampHashAlgorithm { get; } = HashAlgorithmName.SHA256;
        internal Uri TimestampService { get; }

        internal SignOptions(
            string? applicationName,
            string? publisherName,
            string? description,
            Uri? descriptionUrl,
            HashAlgorithmName fileHashAlgorithm,
            HashAlgorithmName timestampHashAlgorithm,
            Uri timestampService,
            Matcher? matcher,
            Matcher? antiMatcher)
        {
            ApplicationName = applicationName;
            PublisherName = publisherName;
            Description = description;
            DescriptionUrl = descriptionUrl;
            FileHashAlgorithm = fileHashAlgorithm;
            TimestampHashAlgorithm = timestampHashAlgorithm;
            TimestampService = timestampService;
            Matcher = matcher;
            AntiMatcher = antiMatcher;
        }

        internal SignOptions(HashAlgorithmName fileHashAlgorithm, Uri timestampService)
            : this(applicationName: null, publisherName: null, description: null, descriptionUrl: null, 
                  fileHashAlgorithm, HashAlgorithmName.SHA256, timestampService, matcher: null,
                  antiMatcher: null)
        {
        }
    }
}