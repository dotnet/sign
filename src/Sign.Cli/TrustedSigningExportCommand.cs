// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using Azure.Core;
using Sign.Core;
using Sign.SignatureProviders.TrustedSigning;

namespace Sign.Cli
{
    internal sealed class TrustedSigningExportCommand : TrustedSigningCommand
    {
        internal TrustedSigningExportCommand(ExportCommand exportCommand, IServiceProviderFactory serviceProviderFactory)
        {
            ArgumentNullException.ThrowIfNull(exportCommand, nameof(exportCommand));
            ArgumentNullException.ThrowIfNull(serviceProviderFactory, nameof(serviceProviderFactory));

            this.SetHandler(async context =>
            {
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

                await exportCommand.HandleAsync(context, serviceProviderFactory, trustedSigningServiceProvider);
            });
        }
    }
}
