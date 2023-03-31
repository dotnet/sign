// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Sign.Cli
{
    internal sealed class CodeCommand : Command
    {
        internal Option<string?> ApplicationNameOption { get; } = new(new[] { "-an", "--application-name" }, Resources.ApplicationNameOptionDescription);
        internal Option<DirectoryInfo> BaseDirectoryOption { get; } = new(new[] { "-b", "--base-directory" }, ParseBaseDirectoryOption, description: Resources.BaseDirectoryOptionDescription);
        internal Option<string> DescriptionOption { get; } = new(new[] { "-d", "--description" }, Resources.DescriptionOptionDescription);
        internal Option<Uri?> DescriptionUrlOption { get; } = new(new[] { "-u", "--description-url" }, ParseUrl, description: Resources.DescriptionUrlOptionDescription);
        internal Option<HashAlgorithmName> FileDigestOption { get; } = new(new[] { "-fd", "--file-digest" }, ParseHashAlgorithmName, description: Resources.FileDigestOptionDescription);
        internal Option<string?> FileListOption = new(new[] { "-fl", "--file-list" }, Resources.FileListOptionDescription);
        internal Option<int> MaxConcurrencyOption { get; } = new(new[] { "-m", "--max-concurrency" }, ParseMaxConcurrencyOption, description: Resources.MaxConcurrencyOptionDescription);
        internal Option<string?> OutputOption { get; } = new(new[] { "-o", "--output" }, Resources.OutputOptionDescription);
        internal Option<string?> PublisherNameOption { get; } = new(new[] { "-pn", "--publisher-name" }, Resources.PublisherNameOptionDescription);
        internal Option<HashAlgorithmName> TimestampDigestOption { get; } = new(new[] { "-td", "--timestamp-digest" }, ParseHashAlgorithmName, description: Resources.TimestampDigestOptionDescription);
        internal Option<Uri?> TimestampUrlOption { get; } = new(new[] { "-t", "--timestamp-url" }, ParseUrl, description: Resources.TimestampUrlOptionDescription);
        internal Option<LogLevel> VerbosityOption { get; } = new(new[] { "-v", "--verbosity" }, () => LogLevel.Warning, Resources.VerbosityOptionDescription);

        internal CodeCommand()
            : base("code", Resources.CodeCommandDescription)
        {
            DescriptionOption.IsRequired = true;
            DescriptionUrlOption.IsRequired = true;

            MaxConcurrencyOption.SetDefaultValue(4);
            FileDigestOption.SetDefaultValue(HashAlgorithmName.SHA256);
            TimestampDigestOption.SetDefaultValue(HashAlgorithmName.SHA256);
            TimestampUrlOption.SetDefaultValue(new Uri("http://timestamp.acs.microsoft.com"));
            BaseDirectoryOption.SetDefaultValue(new DirectoryInfo(Environment.CurrentDirectory));

            // Global options are available on the adding command and all subcommands.
            // Order here is significant as it represents the order in which options are
            // displayed in help.
            AddGlobalOption(ApplicationNameOption);
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
            if (result.Tokens.Count != 1 ||
                string.IsNullOrWhiteSpace(result.Tokens.Single().Value))
            {
                result.ErrorMessage = FormatMessage(Resources.InvalidBaseDirectoryValue, result.Argument);

                return new DirectoryInfo(Environment.CurrentDirectory);
            }

            string value = result.Tokens.Single().Value;

            if (Path.IsPathRooted(value))
            {
                return new DirectoryInfo(value);
            }

            result.ErrorMessage = FormatMessage(Resources.InvalidBaseDirectoryValue, result.Argument);

            return new DirectoryInfo(Environment.CurrentDirectory);
        }

        private static int ParseMaxConcurrencyOption(ArgumentResult result)
        {
            if (result.Tokens.Count != 1 ||
                !int.TryParse(result.Tokens.Single().Value, out int value) ||
                value < 1)
            {
                result.ErrorMessage = FormatMessage(Resources.InvalidMaxConcurrencyValue, result.Argument);

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
                    result.ErrorMessage = FormatMessage(Resources.InvalidDigestValue, result.Argument);

                    return HashAlgorithmName.SHA256;
            }
        }

        private static Uri? ParseUrl(ArgumentResult result)
        {
            if (result.Tokens.Count != 1 ||
                !Uri.TryCreate(result.Tokens[0].Value, UriKind.Absolute, out Uri? uri) ||
                !(string.Equals(Uri.UriSchemeHttp, uri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Uri.UriSchemeHttps, uri.Scheme, StringComparison.OrdinalIgnoreCase)))
            {
                result.ErrorMessage = FormatMessage(Resources.InvalidUrlValue, result.Argument);

                return null;
            }

            return uri;
        }

        private static string FormatMessage(string format, Argument argument)
        {
            return string.Format(CultureInfo.CurrentCulture, format, $"--{argument.Name}");
        }
    }
}