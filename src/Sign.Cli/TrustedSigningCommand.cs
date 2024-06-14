// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Azure.Core;
using Azure.Identity;
using Sign.Core;
using Sign.SignatureProviders.TrustedSigning;

namespace Sign.Cli
{
    internal sealed class TrustedSigningCommand : Command
    {
        internal Option<Uri> EndpointOption { get; } = new(["-tse", "--trusted-signing-endpoint"], TrustedSigningResources.EndpointOptionDescription);
        internal Option<string> AccountOption { get; } = new(["-tsa", "--trusted-signing-account"], TrustedSigningResources.AccountOptionDescription);
        internal Option<string> CertificateProfileOption { get; } = new(["-tsc", "--trusted-signing-certificate-profile"], TrustedSigningResources.CertificateProfileOptionDescription);
        internal Option<bool> ManagedIdentityOption { get; } = new(["-tsm", "--trusted-signing-managed-identity"], getDefaultValue: () => false, TrustedSigningResources.ManagedIdentityOptionDescription);
        internal Option<string?> TenantIdOption { get; } = new(["-tst", "--trusted-signing-tenant-id"], TrustedSigningResources.TenantIdOptionDescription);
        internal Option<string?> ClientIdOption { get; } = new(["-tsi", "--trusted-signing-client-id"], TrustedSigningResources.ClientIdOptionDescription);
        internal Option<string?> ClientSecretOption { get; } = new(["-tss", "--trusted-signing-client-secret"], TrustedSigningResources.ClientSecretOptionDescription);

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
            AddOption(ManagedIdentityOption);
            AddOption(TenantIdOption);
            AddOption(ClientIdOption);
            AddOption(ClientSecretOption);

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

                bool useManagedIdentity = context.ParseResult.GetValueForOption(ManagedIdentityOption);

                TokenCredential? credential = null;

                if (useManagedIdentity)
                {
                    credential = new DefaultAzureCredential();
                }
                else
                {
                    string? tenantId = context.ParseResult.GetValueForOption(TenantIdOption);
                    string? clientId = context.ParseResult.GetValueForOption(ClientIdOption);
                    string? clientSecret = context.ParseResult.GetValueForOption(ClientSecretOption);

                    if (string.IsNullOrEmpty(tenantId) ||
                        string.IsNullOrEmpty(clientId) ||
                        string.IsNullOrEmpty(clientSecret))
                    {
                        context.Console.Error.WriteFormattedLine(
                                    TrustedSigningResources.InvalidClientSecretCredential,
                                    TenantIdOption,
                                    ClientIdOption,
                                    ClientSecretOption);
                        context.ExitCode = ExitCode.NoInputsFound;
                        return;
                    }

                    credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
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
