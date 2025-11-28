// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using Azure.CodeSigning;
using Azure.CodeSigning.Extensions;
using Azure.Core;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sign.Core;
using Sign.SignatureProviders.TrustedSigning;

namespace Sign.Cli
{
    internal sealed class TrustedSigningCommand : Command
    {
        internal Option<Uri> EndpointOption { get; }
        internal Option<string> AccountOption { get; }
        internal Option<string> CertificateProfileOption { get; }
        internal AzureCredentialOptions AzureCredentialOptions { get; } = new();

        internal Argument<List<string>?> FilesArgument { get; }

        internal TrustedSigningCommand(CodeCommand codeCommand, IServiceProviderFactory serviceProviderFactory)
            : base("trusted-signing", TrustedSigningResources.CommandDescription)
        {
            ArgumentNullException.ThrowIfNull(codeCommand, nameof(codeCommand));
            ArgumentNullException.ThrowIfNull(serviceProviderFactory, nameof(serviceProviderFactory));

            EndpointOption = new Option<Uri>("--trusted-signing-endpoint", "-tse")
            {
                Description = TrustedSigningResources.EndpointOptionDescription,
                Required = true
            };
            AccountOption = new Option<string>("--trusted-signing-account", "-tsa")
            {
                Description = TrustedSigningResources.AccountOptionDescription,
                Required = true
            };
            CertificateProfileOption = new Option<string>("--trusted-signing-certificate-profile", "-tscp")
            {
                Description = TrustedSigningResources.CertificateProfileOptionDescription,
                Required = true
            };
            FilesArgument = new Argument<List<string>?>("file(s)")
            {
                Description = Resources.FilesArgumentDescription,
                Arity = ArgumentArity.OneOrMore
            };

            Options.Add(EndpointOption);
            Options.Add(AccountOption);
            Options.Add(CertificateProfileOption);
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

                TokenCredential? credential = AzureCredentialOptions.CreateTokenCredential(parseResult);

                if (credential is null)
                {
                    return Task.FromResult(ExitCode.Failed);
                }

                // Some of the options are required and that is why we can safely use
                // the null-forgiving operator (!) to simplify the code.
                Uri endpointUrl = parseResult.GetValue(EndpointOption)!;
                string accountName = parseResult.GetValue(AccountOption)!;
                string certificateProfileName = parseResult.GetValue(CertificateProfileOption)!;

                serviceProviderFactory.AddServices(services =>
                {
                    services.AddAzureClients(builder =>
                    {
                        builder.AddCertificateProfileClient(endpointUrl);
                        builder.UseCredential(credential);
                        builder.ConfigureDefaults(options => options.Retry.Mode = RetryMode.Exponential);
                    });

                    services.AddSingleton<TrustedSigningService>(serviceProvider =>
                    {
                        return new TrustedSigningService(
                            serviceProvider.GetRequiredService<CertificateProfileClient>(),
                            accountName,
                            certificateProfileName,
                            serviceProvider.GetRequiredService<ILogger<TrustedSigningService>>());
                    });
                });

                TrustedSigningServiceProvider trustedSigningServiceProvider = new();

                return codeCommand.HandleAsync(parseResult, serviceProviderFactory, trustedSigningServiceProvider, filesArgument);
            });
        }
    }
}
