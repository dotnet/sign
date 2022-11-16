// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Sign.Cli
{
    internal sealed class CodeCommand : Command
    {
        internal Option<DirectoryInfo> BaseDirectoryOption { get; } = new(new[] { "-b", "--base-directory" }, ParseBaseDirectoryOption, description: "Base directory for files to override the working directory.");
        internal Option<string> DescriptionOption { get; } = new(new[] { "-d", "--description" }, "Description of the signing certificate.");
        internal Option<Uri> DescriptionUrlOption { get; } = new(new[] { "-u", "--description-url" }, "Description URL of the signing certificate.");
        internal Option<HashAlgorithmName> FileDigestOption { get; } = new(new[] { "-fd", "--file-digest" }, ParseHashAlgorithmName, description: "Digest algorithm to hash the file with. Allowed values are sha256, sha384, and sha512.");
        internal Option<string?> FileListOption = new(new[] { "-fl", "--file-list" }, "Path to file containing paths of files to sign within an archive.");
        internal Option<int> MaxConcurrencyOption { get; } = new(new[] { "-m", "--max-concurrency" }, ParseMaxConcurrencyOption, description: "Maximum concurrency (default is 4)");
        internal Option<string?> OutputOption { get; } = new(new[] { "-o", "--output" }, "Output file or directory. If omitted, overwrites input file.");
        internal Option<string?> PublisherNameOption { get; } = new(new[] { "-pn", "--publisher-name" }, "Publisher name (ClickOnce).");
        internal Option<HashAlgorithmName> TimestampDigestOption { get; } = new(new[] { "-td", "--timestamp-digest" }, ParseHashAlgorithmName, description: "Used with the -t switch to request a digest algorithm used by the RFC 3161 timestamp server. Allowed values are sha256, sha384, and sha512.");
        internal Option<Uri> TimestampUrlOption { get; } = new(new[] { "-t", "--timestamp-url" }, "RFC 3161 timestamp server URL. If this option is not specified, the signed file will not be timestamped.");
        internal Option<LogLevel> VerbosityOption { get; } = new(new[] { "-v", "--verbosity" }, () => LogLevel.Warning, "Sets the verbosity level of the command. Allowed values are q[uiet], m[inimal], n[ormal], d[etailed], and diag[nostic].");

        internal CodeCommand()
            : base("code", "Sign binaries and containers.")
        {
            DescriptionOption.IsRequired = true;
            DescriptionUrlOption.IsRequired = true;

            MaxConcurrencyOption.SetDefaultValue(4);
            FileDigestOption.SetDefaultValue(HashAlgorithmName.SHA256);
            TimestampDigestOption.SetDefaultValue(HashAlgorithmName.SHA256);
            BaseDirectoryOption.SetDefaultValue(new DirectoryInfo(Environment.CurrentDirectory));

            // Global options are available on the adding command and all subcommands.
            // Order here is significant as it represents the order in which options are
            // displayed in help.
            AddGlobalOption(DescriptionOption);
            AddGlobalOption(DescriptionUrlOption);
            AddGlobalOption(BaseDirectoryOption);
            AddGlobalOption(OutputOption);
            AddGlobalOption(PublisherNameOption);
            AddGlobalOption(FileListOption);
            AddGlobalOption(FileDigestOption);
            AddGlobalOption(TimestampUrlOption);
            AddGlobalOption(TimestampDigestOption);
            AddGlobalOption(MaxConcurrencyOption);
            AddGlobalOption(VerbosityOption);
        }

        private static DirectoryInfo ParseBaseDirectoryOption(ArgumentResult result)
        {
            const string ErrorMessage = "A fully rooted directory path is required.";

            if (result.Tokens.Count != 1 ||
                string.IsNullOrWhiteSpace(result.Tokens.Single().Value))
            {
                result.ErrorMessage = ErrorMessage;

                return new DirectoryInfo(Environment.CurrentDirectory);
            }

            string value = result.Tokens.Single().Value;

            if (Path.IsPathRooted(value))
            {
                return new DirectoryInfo(value);
            }

            result.ErrorMessage = ErrorMessage;

            return new DirectoryInfo(Environment.CurrentDirectory);
        }

        private static int ParseMaxConcurrencyOption(ArgumentResult result)
        {
            if (result.Tokens.Count != 1 ||
                !int.TryParse(result.Tokens.Single().Value, out int value) ||
                value < 1)
            {
                result.ErrorMessage = "A number value greater than or equal to 1 is required.";

                return default;
            }

            return value;
        }

        private static HashAlgorithmName ParseHashAlgorithmName(ArgumentResult result)
        {
            if (result.Tokens.Count == 0)
            {
                return HashAlgorithmName.SHA256;
            }

            string token = result.Tokens.Single().Value.ToLowerInvariant();

            switch (token)
            {
                case "sha256":
                    return HashAlgorithmName.SHA256;

                case "sha384":
                    return HashAlgorithmName.SHA384;

                case "sha512":
                    return HashAlgorithmName.SHA512;

                default:
                    result.ErrorMessage = "Unsupported hash algorithm.  Valid values are sha256, sha384, and sha512.";

                    return HashAlgorithmName.SHA256;
            }
        }
    }
}