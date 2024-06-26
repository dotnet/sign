// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using Sign.Core;

namespace Sign.Cli
{
    internal abstract class TrustedSigningCommand : Command
    {
        internal Option<Uri> EndpointOption { get; } = new(["--trusted-signing-endpoint", "-tse"], TrustedSigningResources.EndpointOptionDescription);
        internal Option<string> AccountOption { get; } = new(["--trusted-signing-account", "-tsa"], TrustedSigningResources.AccountOptionDescription);
        internal Option<string> CertificateProfileOption { get; } = new(["--trusted-signing-certificate-profile", "-tscp"], TrustedSigningResources.CertificateProfileOptionDescription);
        internal AzureCredentialOptions AzureCredentialOptions { get; } = new();

        protected TrustedSigningCommand()
            : base("trusted-signing", TrustedSigningResources.CommandDescription)
        {
            EndpointOption.IsRequired = true;
            AccountOption.IsRequired = true;
            CertificateProfileOption.IsRequired = true;

            AddOption(EndpointOption);
            AddOption(AccountOption);
            AddOption(CertificateProfileOption);
            AzureCredentialOptions.AddOptionsToCommand(this);
        }

    }
}
