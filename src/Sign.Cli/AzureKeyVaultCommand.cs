using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
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

        internal Option<string> CertificateOption { get; } = new(new[] { "-kvc", "--azure-key-vault-certificate" }, "Name of the certificate in Azure Key Vault.");
        internal Option<string?> ClientIdOption { get; } = new(new[] { "-kvi", "--azure-key-vault-client-id" }, "Client ID to authenticate to Azure Key Vault.");
        internal Option<string?> ClientSecretOption { get; } = new(new[] { "-kvs", "--azure-key-vault-client-secret" }, "Client secret to authenticate to Azure Key Vault.");
        internal Argument<string?> FileArgument { get; } = new("file(s)", "File to sign.");
        internal Option<bool> ManagedIdentityOption { get; } = new(new[] { "-kvm", "--azure-key-vault-managed-identity" }, getDefaultValue: () => false, "Managed identity to authenticate to Azure Key Vault.");
        internal Option<string?> TenantIdOption { get; } = new(new[] { "-kvt", "--azure-key-vault-tenant-id" }, "Tenant ID to authenticate to Azure Key Vault.");
        internal Option<Uri> UrlOption { get; } = new(new[] { "-kvu", "--azure-key-vault-url" }, "URL to an Azure Key Vault.");

        internal AzureKeyVaultCommand(CodeCommand codeCommand)
            : base("azure-key-vault", "Use Azure Key Vault.")
        {
            ArgumentNullException.ThrowIfNull(codeCommand, nameof(codeCommand));

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
                string? name = context.ParseResult.GetValueForOption(_codeCommand.NameOption);
                string? description = context.ParseResult.GetValueForOption(_codeCommand.DescriptionOption);
                Uri? descriptionUrl = context.ParseResult.GetValueForOption(_codeCommand.DescriptionUrlOption);
                string? fileListFilePath = context.ParseResult.GetValueForOption(_codeCommand.FileListOption);
                HashAlgorithmName fileHashAlgorithmName = context.ParseResult.GetValueForOption(_codeCommand.FileDigestOption);
                HashAlgorithmName timestampHashAlgorithmName = context.ParseResult.GetValueForOption(_codeCommand.TimestampDigestOption);
                Uri? timestampUrl = context.ParseResult.GetValueForOption(_codeCommand.TimestampUrlOption);
                LogLevel verbosity = context.ParseResult.GetValueForOption(_codeCommand.VerbosityOption);
                string? output = context.ParseResult.GetValueForOption(_codeCommand.OutputOption);
                int maxConcurrency = context.ParseResult.GetValueForOption(_codeCommand.MaxConcurrencyOption);

                string? fileArgument = context.ParseResult.GetValueForArgument(FileArgument);

                if (string.IsNullOrEmpty(fileArgument))
                {
                    context.Console.Error.WriteLine("A file or glob is required.");
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
                    context.Console.Error.WriteLine("--base-directory parameter must be rooted if specified");
                    context.ExitCode = ExitCode.InvalidOptions;
                    return;
                }

                Core.ServiceProvider serviceProvider = Core.ServiceProvider.CreateDefault();

                List<FileInfo> inputFiles;

                // If we're going to glob, we can't be fully rooted currently (fix me later)

                bool isGlob = fileArgument.Contains('*');

                if (isGlob)
                {
                    if (Path.IsPathRooted(fileArgument))
                    {
                        context.Console.Error.WriteLine("The file path cannot be rooted when using a glob. Use a path relative to the working directory.");
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
                        context.Console.Error.WriteLine("If not using managed identity, all of the options are required: --azure-key-vault-tenant-id, --azure-key-vault-client-id, --azure-key-vault-secret.");
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
                    name,
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
    }
}