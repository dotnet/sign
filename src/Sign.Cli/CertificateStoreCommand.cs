// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Security.Cryptography;
using Sign.Core;
using Sign.SignatureProviders.CertificateStore;

namespace Sign.Cli
{
    internal sealed class CertificateStoreCommand : Command
    {
        private readonly CodeCommand _codeCommand;

        internal Option<string> CertificateFingerprintOption { get; } = new(["-cfp", "--certificate-fingerprint"], CertificateStoreResources.CertificateFingerprintOptionDescription);
        internal Option<HashAlgorithmName> CertificateFingerprintAlgorithmOption { get; } = new(["-cfpa", "--certificate-fingerprint-algorithm"], HashAlgorithmParser.ParseHashAlgorithmName, description: CertificateStoreResources.CertificateFingerprintAlgorithmOptionDescription);
        internal Option<string?> CertificateFileOption { get; } = new(["-cf", "--certificate-file"], CertificateStoreResources.CertificateFileOptionDescription);
        internal Option<string?> CertificatePasswordOption { get; } = new(["-p", "--password"], CertificateStoreResources.CertificatePasswordOptionDescription);
        internal Option<string?> CryptoServiceProviderOption { get; } = new(["-csp", "--crypto-service-provider"], CertificateStoreResources.CspOptionDescription);
        internal Option<string?> PrivateKeyContainerOption { get; } = new(["-k", "--key-container"], CertificateStoreResources.KeyContainerOptionDescription);
        internal Option<bool> UseMachineKeyContainerOption { get; } = new(["-km", "--use-machine-key-container"], getDefaultValue: () => false, description: CertificateStoreResources.UseMachineKeyContainerOptionDescription);

        internal Argument<string?> FileArgument { get; } = new("file(s)", Resources.FilesArgumentDescription);

        internal CertificateStoreCommand(CodeCommand codeCommand, IServiceProviderFactory serviceProviderFactory)
            : base("certificate-store", Resources.CertificateStoreCommandDescription)
        {
            ArgumentNullException.ThrowIfNull(codeCommand, nameof(codeCommand));
            ArgumentNullException.ThrowIfNull(serviceProviderFactory, nameof(serviceProviderFactory));

            _codeCommand = codeCommand;

            CertificateFingerprintOption.IsRequired = true;

            CertificateFingerprintAlgorithmOption.SetDefaultValue(HashAlgorithmName.SHA256);

            AddOption(CertificateFingerprintOption);
            AddOption(CertificateFingerprintAlgorithmOption);
            AddOption(CertificateFileOption);
            AddOption(CertificatePasswordOption);
            AddOption(CryptoServiceProviderOption);
            AddOption(PrivateKeyContainerOption);
            AddOption(UseMachineKeyContainerOption);
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

                // Some of the options are required and that is why we can safely use
                // the null-forgiving operator (!) to simplify the code.
                string certificateFingerprint = context.ParseResult.GetValueForOption(CertificateFingerprintOption)!;
                HashAlgorithmName certificateFingerprintAlgorithm = context.ParseResult.GetValueForOption(CertificateFingerprintAlgorithmOption);
                string? certificatePath = context.ParseResult.GetValueForOption(CertificateFileOption);
                string? certificatePassword = context.ParseResult.GetValueForOption(CertificatePasswordOption);
                string? cryptoServiceProvider = context.ParseResult.GetValueForOption(CryptoServiceProviderOption);
                string? privateKeyContainer = context.ParseResult.GetValueForOption(PrivateKeyContainerOption);
                bool useMachineKeyContainer = context.ParseResult.GetValueForOption(UseMachineKeyContainerOption);

                // Certificate Fingerprint is required in case the provided certificate container contains multiple certificates.
                if (string.IsNullOrEmpty(certificateFingerprint))
                {
                    context.Console.Error.WriteFormattedLine(
                        Resources.InvalidCertificateFingerprintValue,
                        CertificateFingerprintOption);
                    context.ExitCode = ExitCode.NoInputsFound;

                    return;
                }

                // CSP requires a private key container to function.
                if (string.IsNullOrEmpty(cryptoServiceProvider) != string.IsNullOrEmpty(privateKeyContainer))
                {
                    if (string.IsNullOrEmpty(privateKeyContainer))
                    {
                        context.Console.Error.WriteLine(CertificateStoreResources.MissingPrivateKeyContainerError);
                        context.ExitCode = ExitCode.InvalidOptions;
                        return;
                    }
                    else
                    {
                        context.Console.Error.WriteLine(CertificateStoreResources.MissingCspError);
                        context.ExitCode = ExitCode.InvalidOptions;
                        return;
                    }
                }

                CertificateStoreServiceProvider certificateStoreServiceProvider = new(
                    certificateFingerprint,
                    certificateFingerprintAlgorithm,
                    cryptoServiceProvider,
                    privateKeyContainer,
                    certificatePath,
                    certificatePassword,
                    useMachineKeyContainer);

                await _codeCommand.HandleAsync(context, serviceProviderFactory, certificateStoreServiceProvider, fileArgument);
            });
        }
    }
}
