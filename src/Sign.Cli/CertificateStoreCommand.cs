// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Security.Cryptography;
using Sign.Core;
using Sign.SignatureProviders.CertificateStore;

namespace Sign.Cli
{
    internal sealed class CertificateStoreCommand : Command
    {
        internal Option<string?> CertificateFingerprintOption { get; } = new(["--certificate-fingerprint", "-cfp"], ParseCertificateFingerprint, description: CertificateStoreResources.CertificateFingerprintOptionDescription);
        internal Option<string?> CertificateFileOption { get; } = new(["--certificate-file", "-cf"], CertificateStoreResources.CertificateFileOptionDescription);
        internal Option<string?> CertificatePasswordOption { get; } = new(["--password", "-p"], CertificateStoreResources.CertificatePasswordOptionDescription);
        internal Option<string?> CryptoServiceProviderOption { get; } = new(["--crypto-service-provider", "-csp"], CertificateStoreResources.CspOptionDescription);
        internal Option<string?> PrivateKeyContainerOption { get; } = new(["--key-container", "-k"], CertificateStoreResources.KeyContainerOptionDescription);
        internal Option<bool> UseMachineKeyContainerOption { get; } = new(["--use-machine-key-container", "-km"], getDefaultValue: () => false, description: CertificateStoreResources.UseMachineKeyContainerOptionDescription);

        internal Argument<string?> FileArgument { get; } = new("file(s)", Resources.FilesArgumentDescription);

        internal CertificateStoreCommand(CodeCommand codeCommand, IServiceProviderFactory serviceProviderFactory)
            : base("certificate-store", Resources.CertificateStoreCommandDescription)
        {
            ArgumentNullException.ThrowIfNull(codeCommand, nameof(codeCommand));
            ArgumentNullException.ThrowIfNull(serviceProviderFactory, nameof(serviceProviderFactory));

            CertificateFingerprintOption.IsRequired = true;

            AddOption(CertificateFingerprintOption);
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
                string? certificatePath = context.ParseResult.GetValueForOption(CertificateFileOption);
                string? certificatePassword = context.ParseResult.GetValueForOption(CertificatePasswordOption);
                string? cryptoServiceProvider = context.ParseResult.GetValueForOption(CryptoServiceProviderOption);
                string? privateKeyContainer = context.ParseResult.GetValueForOption(PrivateKeyContainerOption);
                bool useMachineKeyContainer = context.ParseResult.GetValueForOption(UseMachineKeyContainerOption);

                // Certificate fingerprint is required in case the provided certificate container contains multiple certificates.
                if (string.IsNullOrEmpty(certificateFingerprint))
                {
                    context.Console.Error.WriteFormattedLine(
                        Resources.InvalidCertificateFingerprintValue,
                        CertificateFingerprintOption);
                    context.ExitCode = ExitCode.InvalidOptions;

                    return;
                }

                if (!TryDeduceHashAlgorithm(certificateFingerprint, out HashAlgorithmName certificateFingerprintAlgorithm))
                {
                    context.Console.Error.WriteFormattedLine(
                        Resources.InvalidCertificateFingerprintValue,
                        CertificateFingerprintOption);
                    context.ExitCode = ExitCode.InvalidOptions;

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

                await codeCommand.HandleAsync(context, serviceProviderFactory, certificateStoreServiceProvider, fileArgument);
            });
        }

        private static string? ParseCertificateFingerprint(ArgumentResult result)
        {
            string? token = null;

            if (result.Tokens.Count == 1)
            {
                token = result.Tokens[0].Value;

                if (!HexHelpers.IsHex(token))
                {
                    result.ErrorMessage = FormatMessage(
                        Resources.InvalidCertificateFingerprintValue,
                        result.Argument);
                }
                else if (!TryDeduceHashAlgorithm(token, out HashAlgorithmName hashAlgorithmName))
                {
                    result.ErrorMessage = FormatMessage(
                        Resources.InvalidCertificateFingerprintValue,
                        result.Argument);
                }
            }
            else
            {
                result.ErrorMessage = FormatMessage(
                    Resources.InvalidCertificateFingerprintValue,
                    result.Argument);
            }

            return token;
        }

        private static string FormatMessage(string format, Argument argument)
        {
            return string.Format(CultureInfo.CurrentCulture, format, $"--{argument.Name}");
        }

        private static bool TryDeduceHashAlgorithm(
            string certificateFingerprint,
            out HashAlgorithmName hashAlgorithmName)
        {
            hashAlgorithmName = HashAlgorithmName.SHA256;

            if (string.IsNullOrEmpty(certificateFingerprint))
            {
                return false;
            }

            // One hexadecimal character is 4 bits.
            switch (certificateFingerprint.Length)
            {
                case 64: // 64 characters * 4 bits/character = 256 bits
                    hashAlgorithmName = HashAlgorithmName.SHA256;
                    return true;

                case 96: // 96 characters * 4 bits/character = 384 bits
                    hashAlgorithmName = HashAlgorithmName.SHA384;
                    return true;

                case 128: // 128 characters * 4 bits/character = 512 bits
                    hashAlgorithmName = HashAlgorithmName.SHA512;
                    return true;

                default:
                    return false;
            }
        }
    }
}
