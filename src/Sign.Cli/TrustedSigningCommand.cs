// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Azure.Core;
using Sign.Core;
using Sign.SignatureProviders.TrustedSigning;

namespace Sign.Cli
{
    internal sealed class TrustedSigningCommand : Command
    {
        internal Option<Uri> EndpointOption { get; } = new(["--trusted-signing-endpoint", "-tse"], TrustedSigningResources.EndpointOptionDescription);
        internal Option<string> AccountOption { get; } = new(["--trusted-signing-account", "-tsa"], TrustedSigningResources.AccountOptionDescription);
        internal Option<string> CertificateProfileOption { get; } = new(["--trusted-signing-certificate-profile", "-tscp"], TrustedSigningResources.CertificateProfileOptionDescription);
        internal AzureCredentialOptions AzureCredentialOptions { get; } = new();

        internal Argument<string?> FileArgument { get; } = new("file(s)", Resources.FilesArgumentDescription);

        internal TrustedSigningCommand(CodeCommand codeCommand, IServiceProviderFactory serviceProviderFactory)
            : base("trusted-signing", TrustedSigningResources.CommandDescription)
        {
            ArgumentNullException.ThrowIfNull(codeCommand, nameof(codeCommand));
            ArgumentNullException.ThrowIfNull(serviceProviderFactory, nameof(serviceProviderFactory));

            EndpointOption.IsRequired = true;
            AccountOption.IsRequired = true;
            CertificateProfileOption.IsRequired = true;

            AddOption(EndpointOption);
            AddOption(AccountOption);
            AddOption(CertificateProfileOption);
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

                TokenCredential? credential = AzureCredentialOptions.CreateTokenCredential(context);
                if (credential is null)
                {
                    return;
                }

                // Some of the options are required and that is why we can safely use
                // the null-forgiving operator (!) to simplify the code.
                Uri endpointUrl = context.ParseResult.GetValueForOption(EndpointOption)!;
                string accountName = context.ParseResult.GetValueForOption(AccountOption)!;
                string certificateProfileName = context.ParseResult.GetValueForOption(CertificateProfileOption)!;

                TrustedSigningServiceProvider trustedSigningServiceProvider = new(credential, endpointUrl, accountName, certificateProfileName);

                await codeCommand.HandleAsync(context, serviceProviderFactory, trustedSigningServiceProvider, fileArgument);
            });
        }
    }
}
