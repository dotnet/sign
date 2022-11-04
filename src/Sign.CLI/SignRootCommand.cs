using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
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
    internal sealed class SignRootCommand : RootCommand
    {
        private readonly Option<string> NameOption = new(new[] { "-n", "--name" }, "Name of project for tracking.");
        private readonly Option<string> DescriptionOption = new(new[] { "-d", "--description" }, "Description of the signing certificate.");
        private readonly Option<Uri> DescriptionUrlOption = new(new[] { "-u", "--description-url" }, "Description URL of the signing certificate.");
        private readonly Option<string> BaseDirectoryOption = new(new[] { "-b", "--base-directory" }, "Base directory for files to override the working directory.");
        private readonly Option<string> OutputOption = new(new[] { "-o", "--output" }, "Output file or directory. If omitted, overwrites input file.");
        private readonly Option<LogLevel> VerbosityOption = new(new[] { "-v", "--verbosity" }, () => LogLevel.Warning, "Sets the verbosity level of the command. Allowed values are q[uiet], m[inimal], n[ormal], d[etailed], and diag[nostic].");
        private readonly Option<string> FileListOption = new(new[] { "-fl", "--file-list" }, "Path to file containing paths of files to sign within an archive.");
        private readonly Option<HashAlgorithmName> FileDigestOption = new(new[] { "-fd", "--file-digest" }, ParseHashAlgorithmName, description: "Digest algorithm to hash the file with. Allowed values are sha256, sha384, and sha512.");
        private readonly Option<Uri> TimestampUrlOption = new(new[] { "-t", "--timestamp-url" }, "RFC 3161 timestamp server URL. If this option is not specified, the signed file will not be timestamped.");
        private readonly Option<HashAlgorithmName> TimestampDigestOption = new(new[] { "-td", "--timestamp-digest" }, ParseHashAlgorithmName, description: "Used with the -t switch to request a digest algorithm used by the RFC 3161 timestamp server. Allowed values are sha256, sha384, and sha512.");
        private readonly Option<int> MaxConcurrencyOption = new(new[] { "-m", "--max-concurrency" }, () => 4, "Maximum concurrency (default is 4)");
        private readonly Argument<string> FileArgument = new("file(s)", "File to sign.");

        internal SignRootCommand()
            : base(".NET Sign CLI")
        {
            Command codeCommand = new("code", "Sign binaries and containers.");

            FileDigestOption.SetDefaultValue(HashAlgorithmName.SHA256);
            TimestampDigestOption.SetDefaultValue(HashAlgorithmName.SHA256);

            codeCommand.AddOption(NameOption);
            codeCommand.AddOption(DescriptionOption);
            codeCommand.AddOption(DescriptionUrlOption);
            codeCommand.AddOption(VerbosityOption);
            codeCommand.AddOption(FileListOption);
            codeCommand.AddOption(FileDigestOption);
            codeCommand.AddOption(TimestampUrlOption);
            codeCommand.AddOption(TimestampDigestOption);
            codeCommand.AddOption(OutputOption);
            codeCommand.AddOption(BaseDirectoryOption);
            codeCommand.AddOption(MaxConcurrencyOption);

            AddAzureKeyVaultSubcommand(codeCommand);

            AddCommand(codeCommand);
        }

        private void AddAzureKeyVaultSubcommand(Command codeCommand)
        {
            Command subcommand = new("azure-key-vault", "Use Azure Key Vault.");

            Option<Uri> azureKeyVaultUrlOption = new(new[] { "-kvu", "--azure-key-vault-url" }, "URL to an Azure Key Vault.");
            Option<string> azureKeyVaultTenantIdOption = new(new[] { "-kvt", "--azure-key-vault-tenant-id" }, "Tenant ID to authenticate to Azure Key Vault.");
            Option<string> azureKeyVaultClientIdOption = new(new[] { "-kvi", "--azure-key-vault-client-id" }, "Client ID to authenticate to Azure Key Vault.");
            Option<string> azureKeyVaultClientSecretOption = new(new[] { "-kvs", "--azure-key-vault-client-secret" }, "Client secret to authenticate to Azure Key Vault.");
            Option<string> azureKeyVaultCertificateOption = new(new[] { "-kvc", "--azure-key-vault-certificate" }, "Name of the certificate in Azure Key Vault.");
            Option<string> azureKeyVaultManagedIdentityOption = new(new[] { "-kvm", "--azure-key-vault-managed-identity" }, "Managed identity to authenticate to Azure Key Vault.");

            foreach (Option option in codeCommand.Options)
            {
                subcommand.AddOption(option);
            }

            subcommand.AddOption(azureKeyVaultUrlOption);
            subcommand.AddOption(azureKeyVaultTenantIdOption);
            subcommand.AddOption(azureKeyVaultClientIdOption);
            subcommand.AddOption(azureKeyVaultClientSecretOption);
            subcommand.AddOption(azureKeyVaultCertificateOption);
            subcommand.AddOption(azureKeyVaultManagedIdentityOption);
            subcommand.Add(FileArgument);

            subcommand.SetHandler(async (InvocationContext context) =>
            {
                string? baseDirectory = context.ParseResult.GetValueForOption(BaseDirectoryOption);
                string? name = context.ParseResult.GetValueForOption(NameOption);
                string? description = context.ParseResult.GetValueForOption(DescriptionOption);
                Uri? descriptionUrl = context.ParseResult.GetValueForOption(DescriptionUrlOption);
                string? fileListFilePath = context.ParseResult.GetValueForOption(FileListOption);
                HashAlgorithmName fileHashAlgorithmName = context.ParseResult.GetValueForOption(FileDigestOption);
                HashAlgorithmName timestampHashAlgorithmName = context.ParseResult.GetValueForOption(TimestampDigestOption);
                Uri? timestampUrl = context.ParseResult.GetValueForOption(TimestampUrlOption);
                LogLevel verbosity = context.ParseResult.GetValueForOption(VerbosityOption);
                string? output = context.ParseResult.GetValueForOption(OutputOption);
                int maxConcurrency = context.ParseResult.GetValueForOption(MaxConcurrencyOption);

                string fileArgument = context.ParseResult.GetValueForArgument(FileArgument);

                Uri? azureKeyVaultUrl = context.ParseResult.GetValueForOption(azureKeyVaultUrlOption);
                string? azureKeyVaultTenantId = context.ParseResult.GetValueForOption(azureKeyVaultTenantIdOption);
                string? azureKeyVaultClientId = context.ParseResult.GetValueForOption(azureKeyVaultClientIdOption);
                string? azureKeyVaultClientSecret = context.ParseResult.GetValueForOption(azureKeyVaultClientSecretOption);
                string? azureKeyVaultCertificate = context.ParseResult.GetValueForOption(azureKeyVaultCertificateOption);
                string? azureKeyVaultManagedIdentity = context.ParseResult.GetValueForOption(azureKeyVaultManagedIdentityOption);

                if (string.IsNullOrEmpty(baseDirectory))
                {
                    baseDirectory = Environment.CurrentDirectory;
                }

                // Make sure this is rooted
                if (!Path.IsPathRooted(baseDirectory))
                {
                    context.Console.Error.WriteLine("--base-directory parameter must be rooted if specified");
                    context.ExitCode = ExitCode.InvalidOptions;
                    return;
                }


                List<FileInfo> inputFiles;

                // If we're going to glob, we can't be fully rooted currently (fix me later)

                var isGlob = fileArgument.Contains('*');

                if (isGlob)
                {
                    if (Path.IsPathRooted(fileArgument))
                    {
                        context.Console.Error.WriteLine("--input parameter cannot be rooted when using a glob. Use a path relative to the working directory");
                        context.ExitCode = ExitCode.InvalidOptions;

                        return;
                    }
                    Core.ServiceProvider serviceProviderForCli = Core.ServiceProvider.CreateForCli();

                    IFileListReader fileListReader = serviceProviderForCli.GetRequiredService<IFileListReader>();
                    IFileMatcher fileMatcher = serviceProviderForCli.GetRequiredService<IFileMatcher>();

                    using (MemoryStream stream = new(Encoding.UTF8.GetBytes(fileArgument)))
                    using (StreamReader reader = new(stream))
                    {
                        fileListReader.Read(reader, out Matcher? matcher, out Matcher? antiMatcher);

                        DirectoryInfoBase directory = new DirectoryInfoWrapper(new DirectoryInfo(baseDirectory));

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

                // var max concurrency
                //if (!int.TryParse(maxConcurrency.Value(), out var maxC) || maxC < 1)
                //{
                //    context.Console.WriteLine("--max-concurrency parameter is not valid");
                //    context.ExitCode = ExitCode.InvalidOptions;

                //    return;
                //}

                if (inputFiles.Count == 0)
                {
                    context.Console.Error.WriteLine("No inputs found to sign.");
                    context.ExitCode = ExitCode.NoInputsFound;
                    return;
                }

                if (inputFiles.Any(file => !file.Exists))
                {
                    context.Console.Error.WriteLine("Some files do not exist.  Try using a different --base-directory or a fully qualified file path.");
                    
                    foreach (FileInfo file in inputFiles.Where(file => !file.Exists))
                    {
                        context.Console.Error.WriteLine($"    {file.FullName}");
                    }

                    context.ExitCode = ExitCode.NoInputsFound;
                    return;
                }

                TokenCredential? credential = null;

                if (string.IsNullOrEmpty(azureKeyVaultManagedIdentity))
                {
                    credential = new ClientSecretCredential(azureKeyVaultTenantId!, azureKeyVaultClientId!, azureKeyVaultClientSecret!);
                }
                else
                {
                    credential = new DefaultAzureCredential();
                }

                Core.ServiceProvider serviceProvider = Core.ServiceProvider.CreateDefault();

                Signer signer = new(serviceProvider);

                context.ExitCode = await signer.SignAsync(
                    inputFiles,
                    output,
                    fileList,
                    baseDirectory,
                    name,
                    description,
                    descriptionUrl,
                    timestampUrl,
                    maxConcurrency,
                    fileHashAlgorithmName,
                    timestampHashAlgorithmName,
                    credential,
                    azureKeyVaultUrl!,
                    azureKeyVaultCertificate!);
            });

            codeCommand.AddCommand(subcommand);
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

        private static string ExpandFilePath(string baseDirectory, string file)
        {
            if (!Path.IsPathRooted(file))
            {
                return $"{baseDirectory}{Path.DirectorySeparatorChar}{file}";
            }

            return file;
        }

        internal void CustomizeHelp(HelpContext context)
        {

        }
    }
}