// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Core;
using Azure.Security.KeyVault.Certificates;
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
        internal Option<Uri> UrlOption { get; }
        internal Option<string> CertificateOption { get; }
        internal AzureCredentialOptions AzureCredentialOptions { get; } = new();

        internal Argument<List<string>?> FilesArgument { get; }

        internal AzureKeyVaultCommand(CodeCommand codeCommand, IServiceProviderFactory serviceProviderFactory)
            : base("azure-key-vault", AzureKeyVaultResources.CommandDescription)
        {
            ArgumentNullException.ThrowIfNull(codeCommand, nameof(codeCommand));
            ArgumentNullException.ThrowIfNull(serviceProviderFactory, nameof(serviceProviderFactory));

            UrlOption = new Option<Uri>("--azure-key-vault-url", "-kvu")
            {
                Description = AzureKeyVaultResources.UrlOptionDescription,
                Required = true,
                CustomParser = ParseUrl
            };
            CertificateOption = new Option<string>("--azure-key-vault-certificate", "-kvc")
            {
                Description = AzureKeyVaultResources.CertificateOptionDescription,
                Required = true
            };
            FilesArgument = new Argument<List<string>?>("file(s)")
            {
                Description = Resources.FilesArgumentDescription,
                Arity = ArgumentArity.OneOrMore
            };

            Options.Add(UrlOption);
            Options.Add(CertificateOption);
            AzureCredentialOptions.AddOptionsToCommand(this);

            Arguments.Add(FilesArgument);

            SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
            {
                List<string>? filesArgument = parseResult.GetValue(FilesArgument);

                if (filesArgument is not { Count: > 0 })
                {
                    Console.Error.WriteLine(Resources.MissingFileValue);

                    return Task.FromResult(ExitCode.InvalidOptions);
                }

                // this check exists as a courtesy to users who may have been signing .clickonce files via the old workaround.
                // at some point we should remove this check, probably once we hit v1.0
                if (filesArgument.Any(x => x.EndsWith(".clickonce", StringComparison.OrdinalIgnoreCase)))
                {
                    Console.Error.WriteLine(AzureKeyVaultResources.ClickOnceExtensionNotSupported);

                    return Task.FromResult(ExitCode.InvalidOptions);
                }

                TokenCredential? credential = AzureCredentialOptions.CreateTokenCredential(parseResult);
                if (credential is null)
                {
                    return Task.FromResult(ExitCode.Failed);
                }

                // Some of the options are required and that is why we can safely use
                // the null-forgiving operator (!) to simplify the code.
                Uri url = parseResult.GetValue(UrlOption)!;
                string certificateId = parseResult.GetValue(CertificateOption)!;

                // Construct the URI for the certificate and the key from user parameters. We'll validate those with the SDK
                var certUri = new Uri($"{url.Scheme}://{url.Authority}/certificates/{certificateId}");

                if (!KeyVaultCertificateIdentifier.TryCreate(certUri, out var certId))
                {
                    Console.Error.WriteLine(AzureKeyVaultResources.InvalidKeyVaultUrl);

                    return Task.FromResult(ExitCode.InvalidOptions);
                }

                // The key uri is similar and the key name matches the certificate name
                var keyUri = new Uri($"{url.Scheme}://{url.Authority}/keys/{certificateId}");

                serviceProviderFactory.AddServices(services =>
                {
                    services.AddAzureClients(builder =>
                    {
                        builder.AddCertificateClient(certId.VaultUri);
                        builder.AddCryptographyClient(keyUri);
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

                return codeCommand.HandleAsync(parseResult, serviceProviderFactory, keyVaultServiceProvider, filesArgument);
            });
        }

        private static Uri? ParseUrl(ArgumentResult result)
        {
            if (result.Tokens.Count != 1 ||
                !Uri.TryCreate(result.Tokens[0].Value, UriKind.Absolute, out Uri? uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(AzureKeyVaultResources.InvalidUrlValue);

                return null;
            }

            return uri;
        }
    }
}
