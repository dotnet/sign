// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.Security.KeyVault.Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.Core;

namespace Sign.Cli.Test
{
    public class AzureKeyVaultCommandTests
    {
        private const string CertificateVersion = "0123456789abcdef0123456789abcdef";
        private readonly AzureKeyVaultCommand _command = new(new CodeCommand(), Substitute.For<IServiceProviderFactory>());

        [Fact]
        public void Constructor_WhenCodeCommandIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AzureKeyVaultCommand(codeCommand: null!, Substitute.For<IServiceProviderFactory>()));

            Assert.Equal("codeCommand", exception.ParamName);
        }

        [Fact]
        public void Constructor_WhenServiceProviderFactoryIsNull_Throws()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new AzureKeyVaultCommand(new CodeCommand(), serviceProviderFactory: null!));

            Assert.Equal("serviceProviderFactory", exception.ParamName);
        }

        [Fact]
        public void CertificateOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.CertificateOption.Arity);
        }

        [Fact]
        public void CertificateOption_Always_IsRequired()
        {
            Assert.True(_command.CertificateOption.Required);
        }

        [Fact]
        public void CertificateVersionOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.CertificateVersionOption.Arity);
        }

        [Fact]
        public void CertificateVersionOption_Always_IsOptional()
        {
            Assert.False(_command.CertificateVersionOption.Required);
        }

        [Fact]
        public void UrlOption_Always_HasArityOfExactlyOne()
        {
            Assert.Equal(ArgumentArity.ExactlyOne, _command.UrlOption.Arity);
        }

        [Fact]
        public void UrlOption_Always_IsRequired()
        {
            Assert.True(_command.UrlOption.Required);
        }

        public class ParserTests
        {
            private readonly AzureKeyVaultCommand _command;
            private readonly RootCommand _rootCommand;
            private readonly IServiceProviderFactory _serviceProviderFactory = Substitute.For<IServiceProviderFactory>();

            public ParserTests()
            {
                CodeCommand codeCommand = new();
                _command = new(codeCommand, _serviceProviderFactory);
                _rootCommand = new RootCommand();
                _rootCommand.Subcommands.Add(codeCommand);
                codeCommand.Subcommands.Add(_command);
            }

            [Theory]
            [InlineData("code azure-key-vault")]
            [InlineData("code azure-key-vault a")]
            [InlineData("code azure-key-vault -kvu")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test a")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt b")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi c")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi c -kvs")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi c -kvs d")]
            public void Command_WhenRequiredArgumentOrOptionsAreMissing_HasError(string command)
            {
                ParseResult result = _rootCommand.Parse(command);

                Assert.NotEmpty(result.Errors);
            }

            [Theory]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a b")]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a -kvt b -kvi c -kvs d e")]
            [InlineData($"code azure-key-vault -kvu https://keyvault.test -kvc a -kvcv {CertificateVersion} c")]
            [InlineData($"code azure-key-vault -kvu https://keyvault.test -kvc a --azure-key-vault-certificate-version {CertificateVersion} c")]
            public void Command_WhenRequiredArgumentsArePresent_HasNoError(string command)
            {
                ParseResult result = _rootCommand.Parse(command);

                Assert.Empty(result.Errors);
            }

            [Theory]
            [InlineData("-kvcv")]
            [InlineData("--azure-key-vault-certificate-version")]
            public void Command_WhenCertificateVersionIsSpecified_ParsesCorrectly(string option)
            {
                ParseResult result = _rootCommand.Parse($"code azure-key-vault -kvu https://keyvault.test -kvc a {option} {CertificateVersion} c");

                Assert.Empty(result.Errors);
                Assert.Equal(CertificateVersion, result.GetValue(_command.CertificateVersionOption));
            }

            [Fact]
            public async Task Command_WhenCertificateVersionIsSpecified_ConfiguresServiceWithVersion()
            {
                Action<IServiceCollection>? addServices = null;
                IServiceProvider emptyServiceProvider = new ServiceCollection().BuildServiceProvider();

                _serviceProviderFactory
                    .When(factory => factory.AddServices(Arg.Any<Action<IServiceCollection>>()))
                    .Do(call => addServices = call.Arg<Action<IServiceCollection>>());
                _serviceProviderFactory
                    .Create(
                        Arg.Any<LogLevel>(),
                        Arg.Any<ILoggerProvider?>(),
                        Arg.Any<Action<IServiceCollection>?>())
                    .Returns(emptyServiceProvider);

                int exitCode = await _rootCommand
                    .Parse($"code azure-key-vault -kvu https://keyvault.test -kvc a -kvcv {CertificateVersion} missing-file")
                    .InvokeAsync();

                Assert.Equal(ExitCode.NoInputsFound, exitCode);
                Assert.NotNull(addServices);

                Uri keyId = new("https://keyvault.test/keys/a/key-version");
                CertificateClient certificateClient = Substitute.For<CertificateClient>();
                certificateClient.VaultUri.Returns(new Uri("https://keyvault.test/"));
                using RSA rsa = RSA.Create();
                CertificateRequest certificateRequest = new("CN=test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using X509Certificate2 selfIssuedCertificate = certificateRequest.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddDays(1));
                KeyVaultCertificate certificate = CertificateModelFactory.KeyVaultCertificate(
                    new CertificateProperties("a"),
                    keyId: keyId,
                    cer: selfIssuedCertificate.Export(X509ContentType.Cert));
                Response<KeyVaultCertificate> response = Response.FromValue(certificate, Substitute.For<Response>());
                certificateClient
                    .GetCertificateVersionAsync("a", CertificateVersion, Arg.Any<CancellationToken>())
                    .Returns(response);

                ServiceCollection services = new();
                services.AddLogging();
                addServices(services);
                services.AddSingleton(certificateClient);

                using Microsoft.Extensions.DependencyInjection.ServiceProvider serviceProvider = services.BuildServiceProvider();
                Type serviceType = Type.GetType(
                    "Sign.SignatureProviders.KeyVault.KeyVaultService, Sign.SignatureProviders.KeyVault",
                    throwOnError: true)!;
                ICertificateProvider service = (ICertificateProvider)serviceProvider.GetRequiredService(serviceType);
                using X509Certificate2 result = await service.GetCertificateAsync(CancellationToken.None);

                await certificateClient.Received(1)
                    .GetCertificateVersionAsync("a", CertificateVersion, Arg.Any<CancellationToken>());
            }

            [Theory]
            [InlineData("")]
            [InlineData(" ")]
            [InlineData("0123456789abcdef0123456789abcde")]
            [InlineData("0123456789abcdef0123456789abcdef0")]
            public void Command_WhenCertificateVersionLengthIsInvalid_HasError(string certificateVersion)
            {
                ParseResult result = _rootCommand.Parse(
                    $"code azure-key-vault -kvu https://keyvault.test -kvc a -kvcv \"{certificateVersion}\" c");

                Assert.Contains(result.Errors, error => error.Message == AzureKeyVaultResources.InvalidCertificateVersionValue);
            }

            [Theory]
            [InlineData("code azure-key-vault -kvu \"\" -kvc a b")]
            [InlineData("code azure-key-vault -kvu //keyvault.test -kvc a b")]
            [InlineData("code azure-key-vault -kvu /path -kvc a b")]
            [InlineData("code azure-key-vault -kvu file:///file.bin -kvc a b")]
            [InlineData("code azure-key-vault -kvu http://keyvault.test -kvc a b")]
            [InlineData("code azure-key-vault -kvu ftp://keyvault.test -kvc a b")]
            public void Command_WhenUrlIsInvalid_HasError(string command)
            {
                ParseResult result = _rootCommand.Parse(command);

                Assert.NotEmpty(result.Errors);
                Assert.Contains(result.Errors, error => error.Message.Contains("URL"));
            }

            [Theory]
            [InlineData("code azure-key-vault -kvu https://keyvault.test -kvc a b", "https://keyvault.test/")]
            [InlineData("code azure-key-vault -kvu https://my-vault.vault.azure.test -kvc cert b", "https://my-vault.vault.azure.test/")]
            [InlineData("code azure-key-vault -kvu HTTPS://KEYVAULT.TEST -kvc a b", "https://keyvault.test/")]
            public void Command_WhenUrlIsValidHttps_ParsesCorrectly(string command, string expectedUrl)
            {
                ParseResult result = _rootCommand.Parse(command);

                Assert.Empty(result.Errors);
                Uri? actualUrl = result.GetValue(_command.UrlOption);
                Assert.NotNull(actualUrl);
                Assert.Equal(expectedUrl, actualUrl.AbsoluteUri);
            }
        }
    }
}
