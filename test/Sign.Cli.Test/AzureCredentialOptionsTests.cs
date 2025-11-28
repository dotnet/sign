// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using Azure.Core;
using Azure.Identity;
using Moq;
using Sign.Core;

namespace Sign.Cli.Test
{
    public class AzureCredentialOptionsTests
    {
        private readonly AzureCredentialOptions _options;
        private readonly AzureKeyVaultCommand _command;
        private readonly RootCommand _rootCommand;

        public AzureCredentialOptionsTests()
        {
            CodeCommand codeCommand = new();
            _command = new(codeCommand, Mock.Of<IServiceProviderFactory>());
            _rootCommand = new RootCommand();
            _rootCommand.Subcommands.Add(codeCommand);
            codeCommand.Subcommands.Add(_command);
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
            Assert.False(_options.CredentialTypeOption.Required);
        }

        [Fact]
        public void CredentialTypeOption_Always_HasCorrectCompletions()
        {
            // Verify valid values are accepted
            ParseResult result1 = _rootCommand.Parse("code azure-key-vault --azure-key-vault-url https://test --azure-key-vault-certificate cert --azure-credential-type azure-cli test.dll");
            Assert.Empty(result1.Errors);

            ParseResult result2 = _rootCommand.Parse("code azure-key-vault --azure-key-vault-url https://test --azure-key-vault-certificate cert --azure-credential-type azure-powershell test.dll");
            Assert.Empty(result2.Errors);

            ParseResult result3 = _rootCommand.Parse("code azure-key-vault --azure-key-vault-url https://test --azure-key-vault-certificate cert --azure-credential-type managed-identity test.dll");
            Assert.Empty(result3.Errors);

            ParseResult result4 = _rootCommand.Parse("code azure-key-vault --azure-key-vault-url https://test --azure-key-vault-certificate cert --azure-credential-type workload-identity test.dll");
            Assert.Empty(result4.Errors);

            // Verify invalid values are rejected
            ParseResult result5 = _rootCommand.Parse("code azure-key-vault --azure-key-vault-url https://test --azure-key-vault-certificate cert --azure-credential-type invalid test.dll");
            Assert.NotEmpty(result5.Errors);
        }

        [Fact]
        public void ManagedIdentityClientIdOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _options.ManagedIdentityClientIdOption.Arity);
        }

        [Fact]
        public void ManagedIdentityClientIdOption_Always_IsNotRequired()
        {
            Assert.False(_options.ManagedIdentityClientIdOption.Required);
        }

        [Fact]
        public void ManagedIdentityResourceIdOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _options.ManagedIdentityResourceIdOption.Arity);
        }

        [Fact]
        public void ManagedIdentityResourceIdOption_Always_IsNotRequired()
        {
            Assert.False(_options.ManagedIdentityResourceIdOption.Required);
        }

        [Fact]
        public void ObsoleteManagedIdentityOption_Always_HasArityOfZeroOrOne()
        {
            Assert.Equal(ArgumentArity.ZeroOrOne, _options.ObsoleteManagedIdentityOption.Arity);
        }

        [Fact]
        public void ObsoleteManagedIdentityOption_Always_IsNotRequired()
        {
            Assert.False(_options.ObsoleteManagedIdentityOption.Required);
        }

        [Fact]
        public void ObsoleteManagedIdentityOption_Always_IsHidden()
        {
            Assert.True(_options.ObsoleteManagedIdentityOption.Hidden);
        }

        [Fact]
        public void ObsoleteTenantIdOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _options.ObsoleteTenantIdOption.Arity);
        }

        [Fact]
        public void ObsoleteTenantIdOption_Always_IsNotRequired()
        {
            Assert.False(_options.ObsoleteTenantIdOption.Required);
        }

        [Fact]
        public void ObsoleteTenantIdOption_Always_IsHidden()
        {
            Assert.True(_options.ObsoleteTenantIdOption.Hidden);
        }

        [Fact]
        public void ObsoleteClientIdOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _options.ObsoleteClientIdOption.Arity);
        }

        [Fact]
        public void ObsoleteClientIdOption_Always_IsNotRequired()
        {
            Assert.False(_options.ObsoleteClientIdOption.Required);
        }

        [Fact]
        public void ObsoleteClientIdOption_Always_IsHidden()
        {
            Assert.True(_options.ObsoleteClientIdOption.Hidden);
        }

        [Fact]
        public void ObsoleteClientSecretOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _options.ObsoleteClientSecretOption.Arity);
        }

        [Fact]
        public void ObsoleteClientSecretOption_Always_IsNotRequired()
        {
            Assert.False(_options.ObsoleteClientSecretOption.Required);
        }

        [Fact]
        public void ObsoleteClientSecretOption_Always_IsHidden()
        {
            Assert.True(_options.ObsoleteClientSecretOption.Hidden);
        }

        [Fact]
        public void AddOptionsToCommand_Always_AddsAllOptionsToCommand()
        {
            var command = new Command("test");

            _options.AddOptionsToCommand(command);

            Assert.Contains(_options.CredentialTypeOption, command.Options);
            Assert.Contains(_options.ManagedIdentityClientIdOption, command.Options);
            Assert.Contains(_options.ManagedIdentityResourceIdOption, command.Options);
            Assert.Contains(_options.ObsoleteManagedIdentityOption, command.Options);
            Assert.Contains(_options.ObsoleteTenantIdOption, command.Options);
            Assert.Contains(_options.ObsoleteClientIdOption, command.Options);
            Assert.Contains(_options.ObsoleteClientSecretOption, command.Options);
        }

        [Fact]
        public void CreateDefaultAzureCredentialOptions_WhenManagedIdentityClientIdIsSpecified_ManagedIdentityClientIdIsSet()
        {
            ParseResult result = _rootCommand.Parse(@"code azure-key-vault -kvu https://keyvault.test -kvc a -mici b c");

            DefaultAzureCredentialOptions credentialOptions = _options.CreateDefaultAzureCredentialOptions(result);

            Assert.Equal("b", credentialOptions.ManagedIdentityClientId);
        }

        [Fact]
        public void CreateDefaultAzureCredentialOptions_WhenManagedIdentityResourceIdIsSpecified_ManagedIdentityResourceIdIsSet()
        {
            ParseResult result = _rootCommand.Parse(@"code azure-key-vault -kvu https://keyvault.test -kvc a -miri b c");

            DefaultAzureCredentialOptions credentialOptions = _options.CreateDefaultAzureCredentialOptions(result);

            Assert.Equal("b", credentialOptions.ManagedIdentityResourceId);
        }

        [Fact]
        public void CreateDefaultAzureCredentialOptions_WhenNoOptionsAreSpecified_ExcludeOptionsHaveTheCorrectDefaultValues()
        {
            ParseResult result = _rootCommand.Parse("code azure-key-vault -kvu https://keyvault.test -kvc a b");

            DefaultAzureCredentialOptions credentialOptions = _options.CreateDefaultAzureCredentialOptions(result);

            Assert.True(credentialOptions.ExcludeInteractiveBrowserCredential);
            Assert.False(credentialOptions.ExcludeAzureCliCredential);
            Assert.False(credentialOptions.ExcludeAzureDeveloperCliCredential);
            Assert.False(credentialOptions.ExcludeAzurePowerShellCredential);
            Assert.False(credentialOptions.ExcludeEnvironmentCredential);
            Assert.False(credentialOptions.ExcludeManagedIdentityCredential);
            Assert.False(credentialOptions.ExcludeVisualStudioCredential);
            Assert.False(credentialOptions.ExcludeWorkloadIdentityCredential);
        }

        [Fact]
        public void CreateTokenCredential_WhenClientSecretOptionsAreSet_ReturnsClientSecretCredential()
        {
            ParseResult result = _rootCommand.Parse("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi c -kvs d e");

            TokenCredential? tokenCredential = _options.CreateTokenCredential(result);

            Assert.IsType<ClientSecretCredential>(tokenCredential);
        }

        [Fact]
        public void CreateTokenCredential_WhenCredentialTypeIsAzureCli_ReturnsAzureCliCredential()
        {
            ParseResult result = _rootCommand.Parse("code azure-key-vault -kvu https://keyvault.test -kvc a -act azure-cli b");

            TokenCredential? tokenCredential = _options.CreateTokenCredential(result);

            Assert.IsType<AzureCliCredential>(tokenCredential);
        }

        [Fact]
        public void CreateTokenCredential_WhenCredentialTypeIsAzurePowerShell_ReturnsAzurePowerShellCredential()
        {
            ParseResult result = _rootCommand.Parse("code azure-key-vault -kvu https://keyvault.test -kvc a -act azure-powershell b");

            TokenCredential? tokenCredential = _options.CreateTokenCredential(result);

            Assert.IsType<AzurePowerShellCredential>(tokenCredential);
        }

        [Fact]
        public void CreateTokenCredential_WhenCredentialTypeIsManagedIdentity_ReturnsManagedIdentityCredential()
        {
            ParseResult result = _rootCommand.Parse("code azure-key-vault -kvu https://keyvault.test -kvc a -act managed-identity b");

            TokenCredential? tokenCredential = _options.CreateTokenCredential(result);

            Assert.IsType<ManagedIdentityCredential>(tokenCredential);
        }

        [Fact]
        public void CreateTokenCredential_WhenCredentialTypeIsWorkloadIdentity_ReturnsWorkloadIdentityCredential()
        {
            ParseResult result = _rootCommand.Parse("code azure-key-vault -kvu https://keyvault.test -kvc a -act workload-identity b");

            TokenCredential? tokenCredential = _options.CreateTokenCredential(result);

            Assert.IsType<WorkloadIdentityCredential>(tokenCredential);
        }

        [Fact]
        public void CreateTokenCredential_WhenCredentialTypeIsNotSet_ReturnsDefaultAzureCredential()
        {
            ParseResult result = _rootCommand.Parse("code azure-key-vault -kvu https://keyvault.test -kvc a b");

            TokenCredential? tokenCredential = _options.CreateTokenCredential(result);

            Assert.IsType<DefaultAzureCredential>(tokenCredential);
        }
    }
}
