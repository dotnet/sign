// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Azure.Core;
using Sign.Core;
using Sign.SignatureProviders.KeyVault;

namespace Sign.Cli
{
    internal sealed class AzureKeyVaultCommand : Command
    {
        internal Option<Uri> UrlOption { get; } = new(["--azure-key-vault-url", "-kvu"], AzureKeyVaultResources.UrlOptionDescription);
        internal Option<string> CertificateOption { get; } = new(["--azure-key-vault-certificate", "-kvc"], AzureKeyVaultResources.CertificateOptionDescription);
        internal AzureCredentialOptions AzureCredentialOptions { get; } = new();

        internal Argument<string?> FileArgument { get; } = new("file(s)", Resources.FilesArgumentDescription);

        internal AzureKeyVaultCommand(CodeCommand codeCommand, IServiceProviderFactory serviceProviderFactory)
            : base("azure-key-vault", AzureKeyVaultResources.CommandDescription)
        {
            ArgumentNullException.ThrowIfNull(codeCommand, nameof(codeCommand));
            ArgumentNullException.ThrowIfNull(serviceProviderFactory, nameof(serviceProviderFactory));

            CertificateOption.IsRequired = true;
            UrlOption.IsRequired = true;

            AddOption(UrlOption);
            AddOption(CertificateOption);
            AzureCredentialOptions.AddOptionsToCommand(this);

            AddArgument(FileArgument);

            this.SetHandler(async (InvocationContext context) =>
            {
                string? fileArgument = context.ParseResult.GetValueForArgument(FileArgument);

                if (string.IsNullOrEmpty(fileArgument))
                {
                    context.Console.Error.WriteLine(Resources.MissingFileValue);
                    context.ExitCode = ExitCode.InvalidOptions;
                    return;
                }

                // this check exists as a courtesy to users who may have been signing .clickonce files via the old workaround.
                // at some point we should remove this check, probably once we hit v1.0
                if (fileArgument.EndsWith(".clickonce", StringComparison.OrdinalIgnoreCase))
                {
                    context.Console.Error.WriteLine(AzureKeyVaultResources.ClickOnceExtensionNotSupported);
                    context.ExitCode = ExitCode.InvalidOptions;
                    return;
                }

                TokenCredential? credential = AzureCredentialOptions.CreateTokenCredential(context);
                if (credential is null)
                {
                    return;
                }

                // Some of the options are required and that is why we can safely use
                // the null-forgiving operator (!) to simplify the code.
                Uri url = context.ParseResult.GetValueForOption(UrlOption)!;
                string certificateId = context.ParseResult.GetValueForOption(CertificateOption)!;

                KeyVaultServiceProvider keyVaultServiceProvider = new(credential, url, certificateId);
                await codeCommand.HandleAsync(context, serviceProviderFactory, keyVaultServiceProvider, fileArgument);
            });
        }
    }
}
