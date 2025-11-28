// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
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
        internal Option<string?> ApplicationNameOption { get; }
        internal Option<DirectoryInfo> BaseDirectoryOption { get; }
        internal Option<string> DescriptionOption { get; }
        internal Option<Uri?> DescriptionUrlOption { get; }
        internal Option<HashAlgorithmName> FileDigestOption { get; }
        internal Option<string?> FileListOption { get; }
        internal Option<bool> RecurseContainersOption { get; }
        internal Option<int> MaxConcurrencyOption { get; }
        internal Option<string?> OutputOption { get; }
        internal Option<string?> PublisherNameOption { get; }
        internal Option<HashAlgorithmName> TimestampDigestOption { get; }
        internal Option<Uri?> TimestampUrlOption { get; }
        internal Option<LogLevel> VerbosityOption { get; }

        internal CodeCommand()
            : base("code", Resources.CodeCommandDescription)
        {
            ApplicationNameOption = new Option<string?>("--application-name", "-an")
            {
                Description = Resources.ApplicationNameOptionDescription,
                Recursive = true
            };
            BaseDirectoryOption = new Option<DirectoryInfo>("--base-directory", "-b")
            {
                CustomParser = ParseBaseDirectoryOption,
                DefaultValueFactory = _ => new DirectoryInfo(Environment.CurrentDirectory),
                Description = Resources.BaseDirectoryOptionDescription,
                Recursive = true
            };
            DescriptionOption = new Option<string>("--description", "-d")
            {
                Description = Resources.DescriptionOptionDescription,
                Recursive = true
            };
            DescriptionUrlOption = new Option<Uri?>("--description-url", "-u")
            {
                CustomParser = ParseUrl,
                Description = Resources.DescriptionUrlOptionDescription,
                Recursive = true
            };
            FileDigestOption = new Option<HashAlgorithmName>("--file-digest", "-fd")
            {
                CustomParser = HashAlgorithmParser.ParseHashAlgorithmName,
                DefaultValueFactory = _ => HashAlgorithmName.SHA256,
                Description = Resources.FileDigestOptionDescription,
                Recursive = true
            };
            FileListOption = new Option<string?>("--file-list", "-fl")
            {
                Description = Resources.FileListOptionDescription,
                Recursive = true
            };
            RecurseContainersOption = new Option<bool>("--recurse-containers", "-rc")
            {
                DefaultValueFactory = _ => true,
                Description = CertificateStoreResources.ContainersDescription,
                Recursive = true
            };
            MaxConcurrencyOption = new Option<int>("--max-concurrency", "-m")
            {
                CustomParser = ParseMaxConcurrencyOption,
                DefaultValueFactory = _ => 4,
                Description = Resources.MaxConcurrencyOptionDescription,
                Recursive = true
            };
            OutputOption = new Option<string?>("--output", "-o")
            {
                Description = Resources.OutputOptionDescription,
                Recursive = true
            };
            PublisherNameOption = new Option<string?>("--publisher-name", "-pn")
            {
                Description = Resources.PublisherNameOptionDescription,
                Recursive = true
            };
            TimestampDigestOption = new Option<HashAlgorithmName>("--timestamp-digest", "-td")
            {
                CustomParser = HashAlgorithmParser.ParseHashAlgorithmName,
                DefaultValueFactory = _ => HashAlgorithmName.SHA256,
                Description = Resources.TimestampDigestOptionDescription,
                Recursive = true
            };
            TimestampUrlOption = new Option<Uri?>("--timestamp-url", "-t")
            {
                CustomParser = ParseUrl,
                DefaultValueFactory = _ => new Uri("http://timestamp.acs.microsoft.com"),
                Description = Resources.TimestampUrlOptionDescription,
                Recursive = true
            };
            VerbosityOption = new Option<LogLevel>("--verbosity", "-v")
            {
                Description = Resources.VerbosityOptionDescription,
                Recursive = true
            };

            // These options are available on the adding command and all subcommands.
            // Order here is significant as it represents the order in which options are
            // displayed in help.
            Options.Add(ApplicationNameOption);
            Options.Add(DescriptionOption);
            Options.Add(DescriptionUrlOption);
            Options.Add(BaseDirectoryOption);
            Options.Add(OutputOption);
            Options.Add(PublisherNameOption);
            Options.Add(FileListOption);
            Options.Add(RecurseContainersOption);
            Options.Add(FileDigestOption);
            Options.Add(TimestampUrlOption);
            Options.Add(TimestampDigestOption);
            Options.Add(MaxConcurrencyOption);
            Options.Add(VerbosityOption);
        }

        internal async Task<int> HandleAsync(ParseResult parseResult, IServiceProviderFactory serviceProviderFactory, ISignatureProvider signatureProvider, IEnumerable<string> filesArgument)
        {
            // Some of the options have a default value and that is why we can safely use
            // the null-forgiving operator (!) to simplify the code.
            DirectoryInfo baseDirectory = parseResult.GetValue(BaseDirectoryOption)!;
            string? applicationName = parseResult.GetValue(ApplicationNameOption);
            string? publisherName = parseResult.GetValue(PublisherNameOption);
            string? description = parseResult.GetValue(DescriptionOption);
            Uri? descriptionUrl = parseResult.GetValue(DescriptionUrlOption);
            string? fileListFilePath = parseResult.GetValue(FileListOption);
            bool recurseContainers = parseResult.GetValue(RecurseContainersOption);
            HashAlgorithmName fileHashAlgorithmName = parseResult.GetValue(FileDigestOption);
            HashAlgorithmName timestampHashAlgorithmName = parseResult.GetValue(TimestampDigestOption);
            Uri timestampUrl = parseResult.GetValue(TimestampUrlOption)!;
            LogLevel verbosity = parseResult.GetValue(VerbosityOption);
            string? output = parseResult.GetValue(OutputOption);
            int maxConcurrency = parseResult.GetValue(MaxConcurrencyOption);

            // Make sure this is rooted
            if (!Path.IsPathRooted(baseDirectory.FullName))
            {
                Console.Error.WriteFormattedLine(
                    Resources.InvalidBaseDirectoryValue,
                    BaseDirectoryOption);

                return ExitCode.InvalidOptions;
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

            List<FileInfo> inputFiles = [];

            foreach (string fileArgument in filesArgument)
            {
                // If we're going to glob, we can't be fully rooted currently (fix me later)
                bool isGlob = fileArgument.Contains('*');

                if (isGlob)
                {
                    if (Path.IsPathRooted(fileArgument))
                    {
                        Console.Error.WriteLine(Resources.InvalidFileValue);
                        return ExitCode.InvalidOptions;
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

                        inputFiles.AddRange(matches);
                    }
                }
                else
                {
                    inputFiles.Add(new FileInfo(ExpandFilePath(baseDirectory, fileArgument)));
                }
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
                Console.Error.WriteLine(Resources.NoFilesToSign);

                return ExitCode.NoInputsFound;
            }

            if (inputFiles.Any(file => !file.Exists))
            {
                Console.Error.WriteFormattedLine(
                    Resources.SomeFilesDoNotExist,
                    BaseDirectoryOption);

                foreach (FileInfo file in inputFiles.Where(file => !file.Exists))
                {
                    Console.Error.WriteLine($"    {file.FullName}");
                }

                return ExitCode.NoInputsFound;
            }

            ISigner signer = serviceProvider.GetRequiredService<ISigner>();

            int exitCode = await signer.SignAsync(
                inputFiles,
                output,
                fileList,
                recurseContainers,
                baseDirectory,
                applicationName,
                publisherName,
                description,
                descriptionUrl,
                timestampUrl,
                maxConcurrency,
                fileHashAlgorithmName,
                timestampHashAlgorithmName);

            return exitCode;
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
                string.IsNullOrWhiteSpace(result.Tokens[0].Value))
            {
                result.AddError(FormatMessage(Resources.InvalidBaseDirectoryValue, result.Argument));

                return new DirectoryInfo(Environment.CurrentDirectory);
            }

            string value = result.Tokens[0].Value;

            if (Path.IsPathRooted(value))
            {
                return new DirectoryInfo(value);
            }

            result.AddError(FormatMessage(Resources.InvalidBaseDirectoryValue, result.Argument));

            return new DirectoryInfo(Environment.CurrentDirectory);
        }

        private static int ParseMaxConcurrencyOption(ArgumentResult result)
        {
            if (result.Tokens.Count != 1 ||
                !int.TryParse(result.Tokens[0].Value, out int value) ||
                value < 1)
            {
                result.AddError(FormatMessage(Resources.InvalidMaxConcurrencyValue, result.Argument));

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
                result.AddError(FormatMessage(Resources.InvalidUrlValue, result.Argument));

                return null;
            }

            return uri;
        }

        private static string FormatMessage(string format, Argument argument)
        {
            return string.Format(CultureInfo.CurrentCulture, format, argument.Name);
        }
    }
}
