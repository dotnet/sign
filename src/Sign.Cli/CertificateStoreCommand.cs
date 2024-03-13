// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
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
    internal sealed class CertificateStoreCommand : Command
    {
        private readonly CodeCommand _codeCommand;

        internal Option<string> Sha1ThumbprintOption { get; } = new(new[] { "-s", "--sha1" }, CertificateStoreResources.Sha1ThumbprintOptionDescription);
        internal Option<string?> CertificateFileOption { get; } = new(new[] { "-cf", "--certificate-file" }, CertificateStoreResources.CertificateFileOptionDescription);
        internal Option<string?> CertificatePasswordOption { get; } = new(new[] { "-p", "--password" }, CertificateStoreResources.CertificatePasswordOptionDescription);
        internal Option<string?> CryptoServiceProviderOption { get; } = new(new[] { "-csp", "--crypto-service-provider" }, CertificateStoreResources.CspOptionDescription);
        internal Option<string?> PrivateKeyContainerOption { get; } = new(new[] { "-k", "--key-container" }, CertificateStoreResources.KeyContainerOptionDescription);
        internal Option<bool> UseMachineKeyContainerOption { get; } = new(new[] { "-km", "--use-machine-key-container" }, getDefaultValue: () => false, description: CertificateStoreResources.UseMachineKeyContainerOptionDescription);

        internal Argument<string?> FileArgument { get; } = new("file(s)", AzureKeyVaultResources.FilesArgumentDescription);

        internal CertificateStoreCommand(CodeCommand codeCommand, IServiceProviderFactory serviceProviderFactory)
            : base("certificate-store", Resources.CertificateStoreCommandDescription)
        {
            ArgumentNullException.ThrowIfNull(codeCommand, nameof(codeCommand));
            ArgumentNullException.ThrowIfNull(serviceProviderFactory, nameof(serviceProviderFactory));

            _codeCommand = codeCommand;

            Sha1ThumbprintOption.IsRequired = true;

            AddOption(Sha1ThumbprintOption);
            AddOption(CertificateFileOption);
            AddOption(CertificatePasswordOption);
            AddOption(CryptoServiceProviderOption);
            AddOption(PrivateKeyContainerOption);
            AddOption(UseMachineKeyContainerOption);
            AddArgument(FileArgument);

            this.SetHandler(async (InvocationContext context) =>
            {
                DirectoryInfo baseDirectory = context.ParseResult.GetValueForOption(_codeCommand.BaseDirectoryOption)!;
                string? applicationName = context.ParseResult.GetValueForOption(_codeCommand.ApplicationNameOption);
                string? publisherName = context.ParseResult.GetValueForOption(_codeCommand.PublisherNameOption);
                string? description = context.ParseResult.GetValueForOption(_codeCommand.DescriptionOption);
                // This option is required.  If its value fails to parse we won't have reached here,
                // and after successful parsing its value will never be null.
                // Non-null is already guaranteed; the null-forgiving operator (!) just simplifies code.
                Uri descriptionUrl = context.ParseResult.GetValueForOption(_codeCommand.DescriptionUrlOption)!;
                string? fileListFilePath = context.ParseResult.GetValueForOption(_codeCommand.FileListOption);
                HashAlgorithmName fileHashAlgorithmName = context.ParseResult.GetValueForOption(_codeCommand.FileDigestOption);
                HashAlgorithmName timestampHashAlgorithmName = context.ParseResult.GetValueForOption(_codeCommand.TimestampDigestOption);
                // This option is optional but has a default value.  If its value fails to parse we won't have
                // reached here, and after successful parsing its value will never be null.
                // Non-null is already guaranteed; the null-forgiving operator (!) just simplifies code.
                Uri timestampUrl = context.ParseResult.GetValueForOption(_codeCommand.TimestampUrlOption)!;
                LogLevel verbosity = context.ParseResult.GetValueForOption(_codeCommand.VerbosityOption);
                string? output = context.ParseResult.GetValueForOption(_codeCommand.OutputOption);
                int maxConcurrency = context.ParseResult.GetValueForOption(_codeCommand.MaxConcurrencyOption);

                string? sha1Thumbprint = context.ParseResult.GetValueForOption(Sha1ThumbprintOption);
                string? certificatePath = context.ParseResult.GetValueForOption(CertificateFileOption);
                string? certificatePassword = context.ParseResult.GetValueForOption(CertificatePasswordOption);
                string? cryptoServiceProvider = context.ParseResult.GetValueForOption(CryptoServiceProviderOption);
                string? privateKeyContainer = context.ParseResult.GetValueForOption(PrivateKeyContainerOption);
                bool useMachineKeyContainer = context.ParseResult.GetValueForOption(UseMachineKeyContainerOption);

                string? fileArgument = context.ParseResult.GetValueForArgument(FileArgument);

                if (string.IsNullOrEmpty(fileArgument))
                {
                    context.Console.Error.WriteLine(AzureKeyVaultResources.MissingFileValue);
                    context.ExitCode = ExitCode.InvalidOptions;
                    return;
                }

                // SHA-1 Thumbprint is required in case the provided certificate container contains multiple certificates.
                if (string.IsNullOrEmpty(sha1Thumbprint))
                {
                    context.Console.Error.WriteLine(
                        FormatMessage(Resources.InvalidSha1ThumbprintValue, Sha1ThumbprintOption));
                    context.ExitCode = ExitCode.NoInputsFound;

                    return;
                }

                // CSP requires a private key container to function.
                if (string.IsNullOrEmpty(cryptoServiceProvider) != string.IsNullOrEmpty(privateKeyContainer))
                {
                    if (string.IsNullOrEmpty(privateKeyContainer))
                    {
                        context.Console.Error.WriteLine(CertificateStoreResources.MissingPrivateKeyContainerError);
                        context.ExitCode = ExitCode.InvalidOptions;
                        return;
                    }
                    else
                    {
                        context.Console.Error.WriteLine(CertificateStoreResources.MissingCspError);
                        context.ExitCode = ExitCode.InvalidOptions;
                        return;
                    }
                }

                // Make sure this is rooted
                if (!Path.IsPathRooted(baseDirectory.FullName))
                {
                    context.Console.Error.WriteLine(
                        FormatMessage(
                            AzureKeyVaultResources.InvalidBaseDirectoryValue,
                            _codeCommand.BaseDirectoryOption));
                    context.ExitCode = ExitCode.InvalidOptions;
                    return;
                }

                IServiceProvider serviceProvider = serviceProviderFactory.Create(
                    verbosity,
                    addServices: (IServiceCollection services) =>
                    {
                        CertificateStoreServiceProvider certificateStoreServiceProvider = new(
                            sha1Thumbprint,
                            cryptoServiceProvider,
                            privateKeyContainer,
                            certificatePath,
                            certificatePassword,
                            useMachineKeyContainer);

                        services.AddSingleton<ISignatureAlgorithmProvider>(
                            (IServiceProvider serviceProvider) => certificateStoreServiceProvider.GetSignatureAlgorithmProvider(serviceProvider));
                        services.AddSingleton<ICertificateProvider>(
                            (IServiceProvider serviceProvider) => certificateStoreServiceProvider.GetCertificateProvider(serviceProvider));
                    });

                List<FileInfo> inputFiles;

                // If we're going to glob, we can't be fully rooted currently (fix me later)

                bool isGlob = fileArgument.Contains('*');

                if (isGlob)
                {
                    if (Path.IsPathRooted(fileArgument))
                    {
                        context.Console.Error.WriteLine(AzureKeyVaultResources.InvalidFileValue);
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
                    inputFiles = new List<FileInfo>()
                    {
                        new FileInfo(ExpandFilePath(baseDirectory, fileArgument))
                    };
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
                    context.Console.Error.WriteLine(AzureKeyVaultResources.NoFilesToSign);
                    context.ExitCode = ExitCode.NoInputsFound;
                    return;
                }

                if (inputFiles.Any(file => !file.Exists))
                {
                    context.Console.Error.WriteLine(
                        FormatMessage(
                            AzureKeyVaultResources.SomeFilesDoNotExist,
                            _codeCommand.BaseDirectoryOption));

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
            });
        }

        private static string ExpandFilePath(DirectoryInfo baseDirectory, string file)
        {
            if (Path.IsPathRooted(file))
            {
                return file;
            }

            return Path.Combine(baseDirectory.FullName, file);
        }

        private static string FormatMessage(string format, params IdentifierSymbol[] symbols)
        {
            string[] formattedSymbols = symbols
                .Select(symbol => $"--{symbol.Name}")
                .ToArray();

            return string.Format(CultureInfo.CurrentCulture, format, formattedSymbols);
        }
    }
}