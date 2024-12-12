// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;

using Azure.Core;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;

using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

                // Construct the URI for the certificate and the key from user parameters. We'll validate those with the SDK
                var certUri = new Uri($"{url.Scheme}://{url.Authority}/certificates/{certificateId}");

                if (!KeyVaultCertificateIdentifier.TryCreate(certUri, out var certId))
                {
                    context.Console.Error.WriteLine(AzureKeyVaultResources.InvalidKeyVaultUrl);
                    context.ExitCode = ExitCode.InvalidOptions;
                    return;
                }

                // The key uri is similar and the key name matches the certificate name
                var keyUri = new Uri($"{url.Scheme}://{url.Authority}/keys/{certificateId}");

                if (!KeyVaultKeyIdentifier.TryCreate(certUri, out var keyId))
                {
                    context.Console.Error.WriteLine(AzureKeyVaultResources.InvalidKeyVaultUrl);
                    context.ExitCode = ExitCode.InvalidOptions;
                    return;
                }

                serviceProviderFactory.AddServices(services =>
                {
                    services.AddAzureClients(builder =>
                    {
                        builder.AddCertificateClient(certId.VaultUri);
                        builder.AddKeyClient(keyId.VaultUri);
                        builder.UseCredential(credential);
                        builder.ConfigureDefaults(options => options.Retry.Mode = RetryMode.Exponential);
                    });

                    services.AddSingleton<KeyVaultService>(serviceProvider =>
                    {
                        return new KeyVaultService(
                                serviceProvider.GetRequiredService<CertificateClient>(),
                                serviceProvider.GetRequiredService<CryptographyClient>(),
                                certId.Name,
                                serviceProvider.GetRequiredService<ILogger<KeyVaultService>>());
                    });
                });


                KeyVaultServiceProvider keyVaultServiceProvider = new();
                await codeCommand.HandleAsync(context, serviceProviderFactory, keyVaultServiceProvider, fileArgument);
            });
        }
    }
}
