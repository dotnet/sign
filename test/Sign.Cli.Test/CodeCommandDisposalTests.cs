// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.Security.Cryptography;
using NSubstitute;
using Sign.Core;

namespace Sign.Cli.Test
{
    public class CodeCommandDisposalTests
    {
        [Fact]
        public async Task Command_WhenSigningFails_DisposesServiceProvider()
        {
            ISigner signer = Substitute.For<ISigner>();

            signer.SignAsync(
                Arg.Any<IReadOnlyList<FileInfo>>(),
                Arg.Any<string?>(),
                Arg.Any<FileInfo?>(),
                Arg.Any<bool>(),
                Arg.Any<DirectoryInfo>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Uri?>(),
                Arg.Any<Uri>(),
                Arg.Any<int>(),
                Arg.Any<HashAlgorithmName>(),
                Arg.Any<HashAlgorithmName>())
                .Returns(Task.FromResult(ExitCode.Failed));

            IServiceProvider underlyingProvider =
                Substitute.For<IServiceProvider, IDisposable>();

            underlyingProvider.GetService(typeof(ISigner)).Returns(signer);

            Sign.Core.ServiceProvider serviceProvider =
                new(underlyingProvider);
            TestServiceProviderFactory factory = new(serviceProvider);
            SignCommand command = Program.CreateCommand(factory);

            string inputFile = typeof(CodeCommandDisposalTests).Assembly.Location;
            ParseResult parseResult = command.Parse([
                "code", "azure-key-vault",
                "-kvu", "https://keyvault.test",
                "-kvc", "certificate",
                inputFile
            ]);

            Assert.Empty(parseResult.Errors);

            int exitCode = await parseResult.InvokeAsync();

            Assert.Equal(ExitCode.Failed, exitCode);
            ((IDisposable)underlyingProvider).Received(1).Dispose();
        }
    }
}
