// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sign.Core;

namespace Sign.Cli.Test
{
    public partial class SignCommandTests
    {
        public sealed class UseNewClickOnceSigningTests : IDisposable
        {
            private readonly DirectoryService _directoryService;
            private readonly SignCommand _signCommand;
            private readonly SignerSpy _signerSpy;
            private readonly TestServiceProviderFactory _serviceProviderFactory;
            private readonly TemporaryDirectory _temporaryDirectory;

            public UseNewClickOnceSigningTests()
            {
                ServiceCollection services = new();

                _signerSpy = new SignerSpy();

                services.AddSingleton<IMatcherFactory, MatcherFactory>();
                services.AddSingleton<IFileListReader, FileListReader>();
                services.AddSingleton<IFileMatcher, FileMatcher>();
                services.AddSingleton<ISigner>(_signerSpy);

                _serviceProviderFactory = new TestServiceProviderFactory(services.BuildServiceProvider());

                _signCommand = Program.CreateCommand(_serviceProviderFactory);

                _directoryService = new DirectoryService(Mock.Of<ILogger<IDirectoryService>>());
                _temporaryDirectory = new TemporaryDirectory(_directoryService);

                string filePath = Path.Combine(_temporaryDirectory.Directory.FullName, "a.dll");
                System.IO.File.WriteAllText(filePath, string.Empty);
            }

            public void Dispose()
            {
                _temporaryDirectory.Dispose();
                _directoryService.Dispose();
            }

            [Fact]
            public async Task Command_WhenUseNewClickOnceSigningIsSpecified_PassesTrueToServiceProviderFactory()
            {
                string commandText = $"code --description {Description} --description-url {DescriptionUrl} --timestamp-url {TimestampUrl} "
                    + $"--use-new-clickonce-signing "
                    + $"-b \"{_temporaryDirectory.Directory.FullName}\" azure-key-vault -kvu {KeyVaultUrl} -kvc {CertificateName} a.dll";

                await _signCommand.Parse(commandText).InvokeAsync();

                Assert.True(_serviceProviderFactory.UseNewClickOnceSigning);
            }

            [Fact]
            public async Task Command_WhenUseNewClickOnceSigningIsNotSpecified_PassesFalseToServiceProviderFactory()
            {
                string commandText = $"code --description {Description} --description-url {DescriptionUrl} --timestamp-url {TimestampUrl} "
                    + $"-b \"{_temporaryDirectory.Directory.FullName}\" azure-key-vault -kvu {KeyVaultUrl} -kvc {CertificateName} a.dll";

                await _signCommand.Parse(commandText).InvokeAsync();

                Assert.False(_serviceProviderFactory.UseNewClickOnceSigning);
            }

            [Fact]
            public async Task Command_WhenBothNoSignClickOnceDepsAndNoUpdateClickOnceManifest_ReturnsInvalidOptions()
            {
                string commandText = $"code --description {Description} --description-url {DescriptionUrl} --timestamp-url {TimestampUrl} "
                    + $"--use-new-clickonce-signing --no-sign-clickonce-deps --no-update-clickonce-manifest "
                    + $"-b \"{_temporaryDirectory.Directory.FullName}\" azure-key-vault -kvu {KeyVaultUrl} -kvc {CertificateName} a.dll";

                TextWriter originalError = Console.Error;
                try
                {
                    using StringWriter stringWriter = new();
                    Console.SetError(stringWriter);

                    int exitCode = await _signCommand.Parse(commandText).InvokeAsync();

                    Assert.Equal(1, exitCode);
                }
                finally
                {
                    Console.SetError(originalError);
                }
            }

            [Fact]
            public async Task Command_WhenBothNoSignClickOnceDepsAndNoUpdateClickOnceManifest_EmitsError()
            {
                string commandText = $"code --description {Description} --description-url {DescriptionUrl} --timestamp-url {TimestampUrl} "
                    + $"--use-new-clickonce-signing --no-sign-clickonce-deps --no-update-clickonce-manifest "
                    + $"-b \"{_temporaryDirectory.Directory.FullName}\" azure-key-vault -kvu {KeyVaultUrl} -kvc {CertificateName} a.dll";

                TextWriter originalError = Console.Error;
                try
                {
                    using StringWriter stringWriter = new();
                    Console.SetError(stringWriter);

                    await _signCommand.Parse(commandText).InvokeAsync();

                    string errorOutput = stringWriter.ToString();
                    Assert.Contains("--no-sign-clickonce-deps", errorOutput);
                    Assert.Contains("--no-update-clickonce-manifest", errorOutput);
                    Assert.Contains("cannot be combined", errorOutput);
                }
                finally
                {
                    Console.SetError(originalError);
                }
            }

            [Fact]
            public async Task Command_WhenOnlyNoSignClickOnceDeps_DoesNotEmitMutualExclusionError()
            {
                string commandText = $"code --description {Description} --description-url {DescriptionUrl} --timestamp-url {TimestampUrl} "
                    + $"--use-new-clickonce-signing --no-sign-clickonce-deps "
                    + $"-b \"{_temporaryDirectory.Directory.FullName}\" azure-key-vault -kvu {KeyVaultUrl} -kvc {CertificateName} a.dll";

                TextWriter originalError = Console.Error;
                try
                {
                    using StringWriter stringWriter = new();
                    Console.SetError(stringWriter);

                    int exitCode = await _signCommand.Parse(commandText).InvokeAsync();

                    string errorOutput = stringWriter.ToString();
                    Assert.Equal(0, exitCode);
                    Assert.DoesNotContain("cannot be combined", errorOutput);
                }
                finally
                {
                    Console.SetError(originalError);
                }
            }
        }
    }
}
