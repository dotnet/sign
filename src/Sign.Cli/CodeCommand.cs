// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;
using Sign.Core;

namespace Sign.Cli
{
    internal sealed class CodeCommand : Command
    {
        internal Option<string?> ApplicationNameOption { get; } = new(["--application-name", "-an"], Resources.ApplicationNameOptionDescription);
        internal Option<DirectoryInfo> BaseDirectoryOption { get; } = new(["--base-directory", "-b"], ParseBaseDirectoryOption, description: Resources.BaseDirectoryOptionDescription);
        internal Option<string> DescriptionOption { get; } = new(["--description", "-d"], Resources.DescriptionOptionDescription);
        internal Option<Uri?> DescriptionUrlOption { get; } = new(["--description-url", "-u"], ParseUrl, description: Resources.DescriptionUrlOptionDescription);
        internal Option<HashAlgorithmName> FileDigestOption { get; } = new(["--file-digest", "-fd"], HashAlgorithmParser.ParseHashAlgorithmName, description: Resources.FileDigestOptionDescription);
        internal Option<string?> FileListOption = new(["--file-list", "-fl"], Resources.FileListOptionDescription);
        internal Option<int> MaxConcurrencyOption { get; } = new(["--max-concurrency", "-m"], ParseMaxConcurrencyOption, description: Resources.MaxConcurrencyOptionDescription);
        internal Option<string?> OutputOption { get; } = new(["--output", "-o"], Resources.OutputOptionDescription);
        internal Option<string?> PublisherNameOption { get; } = new(["--publisher-name", "-pn"], Resources.PublisherNameOptionDescription);
        internal Option<HashAlgorithmName> TimestampDigestOption { get; } = new(["--timestamp-digest", "-td"], HashAlgorithmParser.ParseHashAlgorithmName, description: Resources.TimestampDigestOptionDescription);
        internal Option<Uri?> TimestampUrlOption { get; } = new(["--timestamp-url", "-t"], ParseUrl, description: Resources.TimestampUrlOptionDescription);
        internal Option<LogLevel> VerbosityOption { get; } = new(["--verbosity", "-v"], () => LogLevel.Warning, Resources.VerbosityOptionDescription);

        internal CodeCommand()
            : base("code", Resources.CodeCommandDescription)
        {
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

        internal async Task HandleAsync(InvocationContext context, IServiceProviderFactory serviceProviderFactory, ISignatureProvider signatureProvider, string fileArgument)
        {
            // Some of the options have a default value and that is why we can safely use
            // the null-forgiving operator (!) to simplify the code.
            DirectoryInfo baseDirectory = context.ParseResult.GetValueForOption(BaseDirectoryOption)!;
            string? applicationName = context.ParseResult.GetValueForOption(ApplicationNameOption);
            string? publisherName = context.ParseResult.GetValueForOption(PublisherNameOption);
            string? description = context.ParseResult.GetValueForOption(DescriptionOption);
            Uri? descriptionUrl = context.ParseResult.GetValueForOption(DescriptionUrlOption);
            string? fileListFilePath = context.ParseResult.GetValueForOption(FileListOption);
            HashAlgorithmName fileHashAlgorithmName = context.ParseResult.GetValueForOption(FileDigestOption);
            HashAlgorithmName timestampHashAlgorithmName = context.ParseResult.GetValueForOption(TimestampDigestOption);
            Uri timestampUrl = context.ParseResult.GetValueForOption(TimestampUrlOption)!;
            LogLevel verbosity = context.ParseResult.GetValueForOption(VerbosityOption);
            string? output = context.ParseResult.GetValueForOption(OutputOption);
            int maxConcurrency = context.ParseResult.GetValueForOption(MaxConcurrencyOption);

            // Make sure this is rooted
            if (!Path.IsPathRooted(baseDirectory.FullName))
            {
                context.Console.Error.WriteFormattedLine(
                    Resources.InvalidBaseDirectoryValue,
                    BaseDirectoryOption);
                context.ExitCode = ExitCode.InvalidOptions;
                return;
            }

            IServiceProvider serviceProvider = serviceProviderFactory.Create(
                verbosity,
                addServices: (IServiceCollection services) =>
                {
                    services.AddSingleton<ISignatureAlgorithmProvider>(
                        (IServiceProvider serviceProvider) => signatureProvider.GetSignatureAlgorithmProvider(serviceProvider));
                    services.AddSingleton<ICertificateProvider>(
                        (IServiceProvider serviceProvider) => signatureProvider.GetCertificateProvider(serviceProvider));
                });

            List<FileInfo> inputFiles;

            // If we're going to glob, we can't be fully rooted currently (fix me later)
            bool isGlob = fileArgument.Contains('*');

            if (isGlob)
            {
                if (Path.IsPathRooted(fileArgument))
                {
                    context.Console.Error.WriteLine(Resources.InvalidFileValue);
                    context.ExitCode = ExitCode.InvalidOptions;
                    return;
                }

                IFileListReader fileListReader = serviceProvider.GetRequiredService<IFileListReader>();
                IFileMatcher fileMatcher = serviceProvider.GetRequiredService<IFileMatcher>();

                using (MemoryStream stream = new(Encoding.UTF8.GetBytes(fileArgument)))
                using (StreamReader reader = new(stream))
                {
                    fileListReader.Read(reader, out Matcher? matcher, out Matcher? antiMatcher);

                    DirectoryInfoBase directory = new DirectoryInfoWrapper(baseDirectory);

                    IEnumerable<FileInfo> matches = fileMatcher.EnumerateMatches(directory, matcher);

                    if (antiMatcher is not null)
                    {
                        IEnumerable<FileInfo> antiMatches = fileMatcher.EnumerateMatches(directory, antiMatcher);
                        matches = matches.Except(antiMatches, FileInfoComparer.Instance);
                    }

                    inputFiles = matches.ToList();
                }
            }
            else
            {
                inputFiles = [new FileInfo(ExpandFilePath(baseDirectory, fileArgument))];
            }

            FileInfo? fileList = null;
            if (!string.IsNullOrEmpty(fileListFilePath))
            {
                if (Path.IsPathRooted(fileListFilePath))
                {
                    fileList = new FileInfo(fileListFilePath);
                }
                else
                {
                    fileList = new FileInfo(ExpandFilePath(baseDirectory, fileListFilePath));
                }
            }

            if (inputFiles.Count == 0)
            {
                context.Console.Error.WriteLine(Resources.NoFilesToSign);
                context.ExitCode = ExitCode.NoInputsFound;
                return;
            }

            if (inputFiles.Any(file => !file.Exists))
            {
                context.Console.Error.WriteFormattedLine(
                    Resources.SomeFilesDoNotExist,
                    BaseDirectoryOption);

                foreach (FileInfo file in inputFiles.Where(file => !file.Exists))
                {
                    context.Console.Error.WriteLine($"    {file.FullName}");
                }

                context.ExitCode = ExitCode.NoInputsFound;
                return;
            }

            ISigner signer = serviceProvider.GetRequiredService<ISigner>();

            context.ExitCode = await signer.SignAsync(
                inputFiles,
                output,
                fileList,
                baseDirectory,
                applicationName,
                publisherName,
                description,
                descriptionUrl,
                timestampUrl,
                maxConcurrency,
                fileHashAlgorithmName,
                timestampHashAlgorithmName);
        }

        private static string ExpandFilePath(DirectoryInfo baseDirectory, string file)
        {
            if (Path.IsPathRooted(file))
            {
                return file;
            }

            return Path.Combine(baseDirectory.FullName, file);
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
