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
using Sign.SignatureProviders.ArtifactSigning;

namespace Sign.Cli
{
    internal sealed class TrustedSigningCommand : Command
    {
        internal Option<Uri> EndpointOption { get; }
        internal Option<string> AccountOption { get; }
        internal Option<string?> CertificateOutputOption { get; }
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
                CustomParser = CodeCommand.ParseHttpsUrl,
                Description = TrustedSigningResources.EndpointOptionDescription,
                Required = true
            };
            AccountOption = new Option<string>("--trusted-signing-account", "-tsa")
            {
                Description = TrustedSigningResources.AccountOptionDescription,
                Required = true
            };
            CertificateOutputOption = new Option<string?>("--certificate-output", "-co")
            {
                Description = Resources.CertificateOutputOptionDescription
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
            Options.Add(CertificateOutputOption);
            Options.Add(CertificateProfileOption);
            AzureCredentialOptions.AddOptionsToCommand(this);

            Arguments.Add(FilesArgument);

            SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
            {
                Console.Out.WriteLine(Resources.TrustedSigningCommandObsolete);

                List<string>? filesArgument = parseResult.GetValue(FilesArgument);

                if (filesArgument is not { Count: > 0 })
                {
                    Console.Error.WriteLine(Resources.MissingFileValue);

                    return ExitCode.InvalidOptions;
                }

                TokenCredential? credential = AzureCredentialOptions.CreateTokenCredential(parseResult);

                if (credential is null)
                {
                    return ExitCode.Failed;
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

                    services.AddSingleton<ArtifactSigningService>(serviceProvider =>
                    {
                        return new ArtifactSigningService(
                            serviceProvider.GetRequiredService<CertificateProfileClient>(),
                            accountName,
                            certificateProfileName,
                            serviceProvider.GetRequiredService<ILogger<ArtifactSigningService>>());
                    });
                });

                ArtifactSigningServiceProvider trustedSigningServiceProvider = new();

                cancellationToken.ThrowIfCancellationRequested();

                string? certificateOutput = parseResult.GetValue(CertificateOutputOption);
                FileInfo? certificateOutputFile = null;

                if (!string.IsNullOrEmpty(certificateOutput))
                {
                    DirectoryInfo baseDirectory = parseResult.GetValue(codeCommand.BaseDirectoryOption)!;
                    certificateOutputFile = new FileInfo(CodeCommand.ExpandFilePath(baseDirectory, certificateOutput));

                    if (certificateOutputFile.Exists)
                    {
                        throw new IOException($"The certificate output file already exists: '{certificateOutputFile.FullName}'.");
                    }
                }

                int exitCode = await codeCommand.HandleAsync(
                    parseResult,
                    serviceProviderFactory,
                    trustedSigningServiceProvider,
                    filesArgument,
                    cancellationToken);

                if (exitCode == ExitCode.Success && certificateOutputFile is not null)
                {
                    await CodeCommand.ExportCertificateAsync(
                        trustedSigningServiceProvider.CertificateProvider,
                        certificateOutputFile);
                }

                return exitCode;
            });
        }
    }
}
