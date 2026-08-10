// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Security.Cryptography;
using Sign.Core;
using Sign.SignatureProviders.CertificateStore;

namespace Sign.Cli
{
    internal sealed class CertificateStoreCommand : Command
    {
        internal Option<string?> CertificateFingerprintOption { get; }
        internal Option<string?> CertificateFileOption { get; }
        internal Option<string?> CertificatePasswordOption { get; }
        internal Option<string?> CryptoServiceProviderOption { get; }
        internal Option<string?> PrivateKeyContainerOption { get; }
        internal Option<bool> UseMachineKeyContainerOption { get; }
        internal Option<bool> InteractiveOption { get; }

        internal Argument<List<string>?> FilesArgument { get; }

        internal CertificateStoreCommand(CodeCommand codeCommand, IServiceProviderFactory serviceProviderFactory)
            : base("certificate-store", Resources.CertificateStoreCommandDescription)
        {
            ArgumentNullException.ThrowIfNull(codeCommand, nameof(codeCommand));
            ArgumentNullException.ThrowIfNull(serviceProviderFactory, nameof(serviceProviderFactory));

            CertificateFingerprintOption = new Option<string?>("--certificate-fingerprint", "-cfp")
            {
                CustomParser = ParseCertificateFingerprint,
                Description = CertificateStoreResources.CertificateFingerprintOptionDescription,
                Required = true
            };
            CertificateFileOption = new Option<string?>("--certificate-file", "-cf")
            {
                Description = CertificateStoreResources.CertificateFileOptionDescription
            };
            CertificatePasswordOption = new Option<string?>("--password", "-p")
            {
                Description = CertificateStoreResources.CertificatePasswordOptionDescription
            };
            CryptoServiceProviderOption = new Option<string?>("--crypto-service-provider", "-csp")
            {
                Description = CertificateStoreResources.CspOptionDescription
            };
            PrivateKeyContainerOption = new Option<string?>("--key-container", "-k")
            {
                Description = CertificateStoreResources.KeyContainerOptionDescription
            };
            UseMachineKeyContainerOption = new Option<bool>("--use-machine-key-container", "-km")
            {
                DefaultValueFactory = _ => false,
                Description = CertificateStoreResources.UseMachineKeyContainerOptionDescription
            };
            InteractiveOption = new Option<bool>("--interactive", "-i")
            {
                DefaultValueFactory = _ => false,
                Description = CertificateStoreResources.InteractiveDescription
            };
            FilesArgument = new Argument<List<string>?>("file(s)")
            {
                Description = Resources.FilesArgumentDescription,
                Arity = ArgumentArity.OneOrMore
            };

            Options.Add(CertificateFingerprintOption);
            Options.Add(CertificateFileOption);
            Options.Add(CertificatePasswordOption);
            Options.Add(CryptoServiceProviderOption);
            Options.Add(PrivateKeyContainerOption);
            Options.Add(UseMachineKeyContainerOption);
            Options.Add(InteractiveOption);
            Arguments.Add(FilesArgument);

            SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
            {
                List<string>? filesArgument = parseResult.GetValue(FilesArgument);

                if (filesArgument is not { Count: > 0 })
                {
                    Console.Error.WriteLine(Resources.MissingFileValue);

                    return Task.FromResult(ExitCode.InvalidOptions);
                }

                // Some of the options are required and that is why we can safely use
                // the null-forgiving operator (!) to simplify the code.
                string certificateFingerprint = parseResult.GetValue(CertificateFingerprintOption)!;
                string? certificatePath = parseResult.GetValue(CertificateFileOption);
                string? certificatePassword = parseResult.GetValue(CertificatePasswordOption);
                string? cryptoServiceProvider = parseResult.GetValue(CryptoServiceProviderOption);
                string? privateKeyContainer = parseResult.GetValue(PrivateKeyContainerOption);
                bool useMachineKeyContainer = parseResult.GetValue(UseMachineKeyContainerOption);
                bool isInteractive = parseResult.GetValue(InteractiveOption);

                // Certificate fingerprint is required in case the provided certificate container contains multiple certificates.
                if (string.IsNullOrEmpty(certificateFingerprint))
                {
                    Console.Error.WriteFormattedLine(
                        Resources.InvalidCertificateFingerprintValue,
                        CertificateFingerprintOption);

                    return Task.FromResult(ExitCode.InvalidOptions);
                }

                if (!TryDeduceHashAlgorithm(certificateFingerprint, out HashAlgorithmName certificateFingerprintAlgorithm))
                {
                    Console.Error.WriteFormattedLine(
                        Resources.InvalidCertificateFingerprintValue,
                        CertificateFingerprintOption);

                    return Task.FromResult(ExitCode.InvalidOptions);
                }

                // CSP requires a private key container to function.
                if (string.IsNullOrEmpty(cryptoServiceProvider) != string.IsNullOrEmpty(privateKeyContainer))
                {
                    if (string.IsNullOrEmpty(privateKeyContainer))
                    {
                        Console.Error.WriteLine(CertificateStoreResources.MissingPrivateKeyContainerError);

                        return Task.FromResult(ExitCode.InvalidOptions);
                    }
                    else
                    {
                        Console.Error.WriteLine(CertificateStoreResources.MissingCspError);

                        return Task.FromResult(ExitCode.InvalidOptions);
                    }
                }

                CertificateStoreServiceProvider certificateStoreServiceProvider = new(
                    certificateFingerprint,
                    certificateFingerprintAlgorithm,
                    cryptoServiceProvider,
                    privateKeyContainer,
                    certificatePath,
                    certificatePassword,
                    useMachineKeyContainer,
                    isInteractive);

                return codeCommand.HandleAsync(parseResult, serviceProviderFactory, certificateStoreServiceProvider, filesArgument);
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
                    result.AddError(FormatMessage(
                        Resources.InvalidCertificateFingerprintValue,
                        result.Argument));
                }
                else if (!TryDeduceHashAlgorithm(token, out HashAlgorithmName hashAlgorithmName))
                {
                    result.AddError(FormatMessage(
                        Resources.InvalidCertificateFingerprintValue,
                        result.Argument));
                }
            }
            else
            {
                result.AddError(FormatMessage(
                    Resources.InvalidCertificateFingerprintValue,
                    result.Argument));
            }

            return token;
        }

        private static string FormatMessage(string format, Argument argument)
        {
            return string.Format(CultureInfo.CurrentCulture, format, argument.Name);
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
