// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;
using Sign.Core;

namespace Sign.Cli
{
    internal sealed class AzureKeyVaultCommand : Command
    {
        private readonly CodeCommand _codeCommand;

        internal Option<string> CertificateOption { get; } = new(new[] { "-kvc", "--azure-key-vault-certificate" }, AzureKeyVaultResources.CertificateOptionDescription);
        internal Option<string?> ClientIdOption { get; } = new(new[] { "-kvi", "--azure-key-vault-client-id" }, AzureKeyVaultResources.ClientIdOptionDescription);
        internal Option<string?> ClientSecretOption { get; } = new(new[] { "-kvs", "--azure-key-vault-client-secret" }, AzureKeyVaultResources.ClientSecretOptionDescription);
        internal Argument<string?> FileArgument { get; } = new("file(s)", AzureKeyVaultResources.FilesArgumentDescription);
        internal Option<bool> ManagedIdentityOption { get; } = new(new[] { "-kvm", "--azure-key-vault-managed-identity" }, getDefaultValue: () => false, AzureKeyVaultResources.ManagedIdentityOptionDescription);
        internal Option<string?> TenantIdOption { get; } = new(new[] { "-kvt", "--azure-key-vault-tenant-id" }, AzureKeyVaultResources.TenantIdOptionDescription);
        internal Option<Uri> UrlOption { get; } = new(new[] { "-kvu", "--azure-key-vault-url" }, AzureKeyVaultResources.UrlOptionDescription);

        internal AzureKeyVaultCommand(CodeCommand codeCommand, IServiceProviderFactory serviceProviderFactory)
            : base("azure-key-vault", AzureKeyVaultResources.CommandDescription)
        {
            ArgumentNullException.ThrowIfNull(codeCommand, nameof(codeCommand));
            ArgumentNullException.ThrowIfNull(serviceProviderFactory, nameof(serviceProviderFactory));

            _codeCommand = codeCommand;

            CertificateOption.IsRequired = true;
            UrlOption.IsRequired = true;

            ManagedIdentityOption.SetDefaultValue(false);

            AddOption(UrlOption);
            AddOption(TenantIdOption);
            AddOption(ClientIdOption);
            AddOption(ClientSecretOption);
            AddOption(CertificateOption);
            AddOption(ManagedIdentityOption);

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

                string? fileArgument = context.ParseResult.GetValueForArgument(FileArgument);

                if (string.IsNullOrEmpty(fileArgument))
                {
                    context.Console.Error.WriteLine(AzureKeyVaultResources.MissingFileValue);
                    context.ExitCode = ExitCode.InvalidOptions;
                    return;
                }

                Uri? url = context.ParseResult.GetValueForOption(UrlOption);
                string? tenantId = context.ParseResult.GetValueForOption(TenantIdOption);
                string? clientId = context.ParseResult.GetValueForOption(ClientIdOption);
                string? secret = context.ParseResult.GetValueForOption(ClientSecretOption);
                string? certificateId = context.ParseResult.GetValueForOption(CertificateOption);
                bool useManagedIdentity = context.ParseResult.GetValueForOption(ManagedIdentityOption);

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

                IServiceProvider serviceProvider = serviceProviderFactory.Create(verbosity);

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

                TokenCredential? credential = null;

                if (useManagedIdentity)
                {
                    credential = new DefaultAzureCredential();
                }
                else
                {
                    if (string.IsNullOrEmpty(tenantId) ||
                        string.IsNullOrEmpty(clientId) ||
                        string.IsNullOrEmpty(secret))
                    {
                        context.Console.Error.WriteLine(
                            FormatMessage(
                                AzureKeyVaultResources.InvalidClientSecretCredential,
                                TenantIdOption,
                                ClientIdOption,
                                ClientSecretOption));
                        context.ExitCode = ExitCode.NoInputsFound;

                        return;
                    }

                    credential = new ClientSecretCredential(tenantId!, clientId!, secret!);
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
                    timestampHashAlgorithmName,
                    credential,
                    url!,
                    certificateId!);
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