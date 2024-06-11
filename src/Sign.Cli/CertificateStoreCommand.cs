// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;
using Sign.Core;
using Sign.SignatureProviders.CertificateStore;

namespace Sign.Cli
{
    internal sealed class CertificateStoreCommand : Command
    {
        private readonly CodeCommand _codeCommand;

        internal Option<string> CertificateFingerprintOption { get; } = new(["-cfp", "--certificate-fingerprint"], CertificateStoreResources.CertificateFingerprintOptionDescription);
        internal Option<HashAlgorithmName> CertificateFingerprintAlgorithmOption { get; } = new([ "-cfpa", "--certificate-fingerprint-algorithm" ], HashAlgorithmParser.ParseHashAlgorithmName, description: CertificateStoreResources.CertificateFingerprintAlgorithmOptionDescription);
        internal Option<string?> CertificateFileOption { get; } = new(["-cf", "--certificate-file"], CertificateStoreResources.CertificateFileOptionDescription);
        internal Option<string?> CertificatePasswordOption { get; } = new(["-p", "--password"], CertificateStoreResources.CertificatePasswordOptionDescription);
        internal Option<string?> CryptoServiceProviderOption { get; } = new(["-csp", "--crypto-service-provider"], CertificateStoreResources.CspOptionDescription);
        internal Option<string?> PrivateKeyContainerOption { get; } = new(["-k", "--key-container"], CertificateStoreResources.KeyContainerOptionDescription);
        internal Option<bool> UseMachineKeyContainerOption { get; } = new(["-km", "--use-machine-key-container"], getDefaultValue: () => false, description: CertificateStoreResources.UseMachineKeyContainerOptionDescription);

        internal Argument<string?> FileArgument { get; } = new("file(s)", Resources.FilesArgumentDescription);

        internal CertificateStoreCommand(CodeCommand codeCommand, IServiceProviderFactory serviceProviderFactory)
            : base("certificate-store", Resources.CertificateStoreCommandDescription)
        {
            ArgumentNullException.ThrowIfNull(codeCommand, nameof(codeCommand));
            ArgumentNullException.ThrowIfNull(serviceProviderFactory, nameof(serviceProviderFactory));

            _codeCommand = codeCommand;

            CertificateFingerprintOption.IsRequired = true;

            CertificateFingerprintAlgorithmOption.SetDefaultValue(HashAlgorithmName.SHA256);

            AddOption(CertificateFingerprintOption);
            AddOption(CertificateFingerprintAlgorithmOption);
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

                string? certificateFingerprint = context.ParseResult.GetValueForOption(CertificateFingerprintOption);
                HashAlgorithmName certificateFingerprintAlgorithm = context.ParseResult.GetValueForOption(CertificateFingerprintAlgorithmOption);
                string? certificatePath = context.ParseResult.GetValueForOption(CertificateFileOption);
                string? certificatePassword = context.ParseResult.GetValueForOption(CertificatePasswordOption);
                string? cryptoServiceProvider = context.ParseResult.GetValueForOption(CryptoServiceProviderOption);
                string? privateKeyContainer = context.ParseResult.GetValueForOption(PrivateKeyContainerOption);
                bool useMachineKeyContainer = context.ParseResult.GetValueForOption(UseMachineKeyContainerOption);

                string? fileArgument = context.ParseResult.GetValueForArgument(FileArgument);

                if (string.IsNullOrEmpty(fileArgument))
                {
                    context.Console.Error.WriteLine(Resources.MissingFileValue);
                    context.ExitCode = ExitCode.InvalidOptions;
                    return;
                }

                // Certificate Fingerprint is required in case the provided certificate container contains multiple certificates.
                if (string.IsNullOrEmpty(certificateFingerprint))
                {
                    context.Console.Error.WriteFormattedLine(
                        Resources.InvalidCertificateFingerprintValue,
                        CertificateFingerprintOption);
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
                    context.Console.Error.WriteFormattedLine(
                        Resources.InvalidBaseDirectoryValue,
                        _codeCommand.BaseDirectoryOption);
                    context.ExitCode = ExitCode.InvalidOptions;
                    return;
                }

                IServiceProvider serviceProvider = serviceProviderFactory.Create(
                    verbosity,
                    addServices: (IServiceCollection services) =>
                    {
                        CertificateStoreServiceProvider certificateStoreServiceProvider = new(
                            certificateFingerprint,
                            certificateFingerprintAlgorithm,
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
                    context.Console.Error.WriteLine(Resources.NoFilesToSign);
                    context.ExitCode = ExitCode.NoInputsFound;
                    return;
                }

                if (inputFiles.Any(file => !file.Exists))
                {
                    context.Console.Error.WriteFormattedLine(
                        Resources.SomeFilesDoNotExist,
                        _codeCommand.BaseDirectoryOption);

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
    }
}