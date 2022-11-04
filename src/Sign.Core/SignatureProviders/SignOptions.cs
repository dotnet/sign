using System.Security.Cryptography;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Sign.Core
{
    internal sealed class SignOptions
    {
        internal string? Name { get; }
        internal string? Description { get; }
        internal Uri? DescriptionUrl { get; }
        internal Matcher? Matcher { get; }
        internal Matcher? AntiMatcher { get; }
        internal HashAlgorithmName FileHashAlgorithm { get; } = HashAlgorithmName.SHA256;
        internal HashAlgorithmName TimestampHashAlgorithm { get; } = HashAlgorithmName.SHA256;
        internal Uri? TimestampService { get; }

        internal SignOptions(
            string? name,
            string? description,
            Uri? descriptionUrl,
            HashAlgorithmName fileHashAlgorithm,
            HashAlgorithmName timestampHashAlgorithm,
            Uri? timestampService,
            Matcher? matcher,
            Matcher? antiMatcher)
        {
            Name = name;
            Description = description;
            DescriptionUrl = descriptionUrl;
            FileHashAlgorithm = fileHashAlgorithm;
            TimestampHashAlgorithm = timestampHashAlgorithm;
            TimestampService = timestampService;
            Matcher = matcher;
            AntiMatcher = antiMatcher;
        }

        internal SignOptions(HashAlgorithmName fileHashAlgorithm)
            : this(name: null, description: null, descriptionUrl: null, fileHashAlgorithm,
                  HashAlgorithmName.SHA256, timestampService: null, matcher: null, antiMatcher: null)
        {
        }
    }
}