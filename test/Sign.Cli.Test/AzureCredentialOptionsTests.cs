﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Azure.Identity;
using Moq;
using Sign.Core;

namespace Sign.Cli.Test
{
    public class AzureCredentialOptionsTests
    {
        private readonly AzureCredentialOptions _options;
        private readonly AzureKeyVaultCommand _command;
        private readonly Parser _parser;

        public AzureCredentialOptionsTests()
        {
            _command = new(new CodeCommand(), Mock.Of<IServiceProviderFactory>());
            _parser = new CommandLineBuilder(_command).Build();
            _options = _command.AzureCredentialOptions;
        }

        [Fact]
        public void CredentialTypeOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _options.CredentialTypeOption.Arity);
        }

        [Fact]
        public void CredentialTypeOption_Always_IsNotRequired()
        {
            Assert.False(_options.CredentialTypeOption.IsRequired);
        }

        [Fact]
        public void TenantIdOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _options.TenantIdOption.Arity);
        }

        [Fact]
        public void TenantIdOption_Always_IsNotRequired()
        {
            Assert.False(_options.TenantIdOption.IsRequired);
        }

        [Fact]
        public void ObsoleteManagedIdentityOption_Always_HasArityOfZeroOrOne()
        {
            Assert.Equal(ArgumentArity.ZeroOrOne, _options.ObsoleteManagedIdentityOption.Arity);
        }

        [Fact]
        public void ObsoleteManagedIdentityOption_Always_IsNotRequired()
        {
            Assert.False(_options.ObsoleteManagedIdentityOption.IsRequired);
        }

        [Fact]
        public void ObsoleteManagedIdentityOption_Always_IsHidden()
        {
            Assert.True(_options.ObsoleteManagedIdentityOption.IsHidden);
        }

        [Fact]
        public void ObsoleteTenantIdOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _options.ObsoleteTenantIdOption.Arity);
        }

        [Fact]
        public void ObsoleteTenantIdOption_Always_IsNotRequired()
        {
            Assert.False(_options.ObsoleteTenantIdOption.IsRequired);
        }

        [Fact]
        public void ObsoleteTenantIdOption_Always_IsHidden()
        {
            Assert.True(_options.ObsoleteTenantIdOption.IsHidden);
        }

        [Fact]
        public void ObsoleteClientIdOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _options.ObsoleteClientIdOption.Arity);
        }

        [Fact]
        public void ObsoleteClientIdOption_Always_IsNotRequired()
        {
            Assert.False(_options.ObsoleteClientIdOption.IsRequired);
        }

        [Fact]
        public void ObsoleteClientIdOption_Always_IsHidden()
        {
            Assert.True(_options.ObsoleteClientIdOption.IsHidden);
        }

        [Fact]
        public void ObsoleteClientSecretOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _options.ObsoleteClientSecretOption.Arity);
        }

        [Fact]
        public void ObsoleteClientSecretOption_Always_IsNotRequired()
        {
            Assert.False(_options.ObsoleteClientSecretOption.IsRequired);
        }

        [Fact]
        public void ObsoleteClientSecretOption_Always_IsHidden()
        {
            Assert.True(_options.ObsoleteClientSecretOption.IsHidden);
        }

        [Fact]
        public void AddOptionsToCommand_Always_AddsAllOptionsToCommand()
        {
            var command = new Command("test");

            _options.AddOptionsToCommand(command);

            Assert.Contains(_options.CredentialTypeOption, command.Options);
            Assert.Contains(_options.ObsoleteManagedIdentityOption, command.Options);
            Assert.Contains(_options.ObsoleteTenantIdOption, command.Options);
            Assert.Contains(_options.ObsoleteClientIdOption, command.Options);
            Assert.Contains(_options.ObsoleteClientSecretOption, command.Options);
        }

        [Fact]
        public void CreateDefaultAzureCredentialOptions_WhenTenantIsSpecified_TenantIdIsSet()
        {
            ParseResult result = _parser.Parse(@"azure-key-vault -kvu https://keyvault.test -kvc a -ati b c");

            DefaultAzureCredentialOptions credentialOptions = _options.CreateDefaultAzureCredentialOptions(result);

            Assert.Equal("b", credentialOptions.TenantId);
        }

        [Fact]
        public void CreateDefaultAzureCredentialOptions_WhenNoOptionsAreSpecified_ExcludeOptionsHaveTheCorrectDefaultValues()
        {
            ParseResult result = _parser.Parse("azure-key-vault -kvu https://keyvault.test -kvc a b");

            DefaultAzureCredentialOptions credentialOptions = _options.CreateDefaultAzureCredentialOptions(result);

            Assert.True(credentialOptions.ExcludeInteractiveBrowserCredential);
            Assert.True(credentialOptions.ExcludeSharedTokenCacheCredential);
            Assert.True(credentialOptions.ExcludeVisualStudioCodeCredential);
            Assert.False(credentialOptions.ExcludeAzureCliCredential);
            Assert.False(credentialOptions.ExcludeAzureDeveloperCliCredential);
            Assert.False(credentialOptions.ExcludeAzurePowerShellCredential);
            Assert.False(credentialOptions.ExcludeEnvironmentCredential);
            Assert.False(credentialOptions.ExcludeManagedIdentityCredential);
            Assert.False(credentialOptions.ExcludeVisualStudioCredential);
            Assert.False(credentialOptions.ExcludeWorkloadIdentityCredential);
        }

        [Fact]
        public void CreateDefaultAzureCredentialOptions_WhenEnvironmentCredentialTypeIsSpecified_ExcludeOptionsHaveTheCorrectValues()
        {
            ParseResult result = _parser.Parse(@"azure-key-vault -kvu https://keyvault.test -kvc a -act environment b");

            DefaultAzureCredentialOptions credentialOptions = _options.CreateDefaultAzureCredentialOptions(result);

            Assert.True(credentialOptions.ExcludeAzureCliCredential);
            Assert.True(credentialOptions.ExcludeAzureDeveloperCliCredential);
            Assert.True(credentialOptions.ExcludeAzurePowerShellCredential);
            Assert.False(credentialOptions.ExcludeEnvironmentCredential);
            Assert.True(credentialOptions.ExcludeInteractiveBrowserCredential);
            Assert.True(credentialOptions.ExcludeManagedIdentityCredential);
            Assert.True(credentialOptions.ExcludeSharedTokenCacheCredential);
            Assert.True(credentialOptions.ExcludeVisualStudioCodeCredential);
            Assert.True(credentialOptions.ExcludeVisualStudioCredential);
            Assert.True(credentialOptions.ExcludeWorkloadIdentityCredential);
        }

        [Fact]
        public void CreateDefaultAzureCredentialOptions_WhenAzureCliCredentialTypeIsSpecified_ExcludeOptionsHaveTheCorrectValues()
        {
            ParseResult result = _parser.Parse(@"azure-key-vault -kvu https://keyvault.test -kvc a -act azure-cli b");

            DefaultAzureCredentialOptions credentialOptions = _options.CreateDefaultAzureCredentialOptions(result);

            Assert.False(credentialOptions.ExcludeAzureCliCredential);
            Assert.True(credentialOptions.ExcludeAzureDeveloperCliCredential);
            Assert.True(credentialOptions.ExcludeAzurePowerShellCredential);
            Assert.True(credentialOptions.ExcludeEnvironmentCredential);
            Assert.True(credentialOptions.ExcludeInteractiveBrowserCredential);
            Assert.True(credentialOptions.ExcludeManagedIdentityCredential);
            Assert.True(credentialOptions.ExcludeSharedTokenCacheCredential);
            Assert.True(credentialOptions.ExcludeVisualStudioCodeCredential);
            Assert.True(credentialOptions.ExcludeVisualStudioCredential);
            Assert.True(credentialOptions.ExcludeWorkloadIdentityCredential);
        }
    }
}
