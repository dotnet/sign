## About

Sign CLI is a .NET tool that provides digital signing for .NET assemblies, packages, and other files.

The tool signs files inside-out, starting with the most nested files and then the outer files, ensuring everything is signed in the correct order.

## Prerequisites

- An up-to-date x64-based version of Windows currently in [mainstream support](https://learn.microsoft.com/lifecycle/products/)
- [.NET 8 SDK or later](https://dotnet.microsoft.com/download)
- [Microsoft Visual C++ 14 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe)
- For ClickOnce and VSTO signing:  A recent --- ideally, the latest --- version of [.NET Framework](https://dotnet.microsoft.com/download/dotnet-framework)

Sign CLI targets .NET 8 and .NET 10 and supports .NET 9 through runtime roll-forward.
**Support for .NET 8 and .NET 9 will be removed shortly after [their end of support on November 10, 2026](https://dotnet.microsoft.com/platform/support/policy/dotnet-core).**
Use the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to prepare for this change.

## Certificate file compatibility

For `certificate-store --certificate-file`, the .NET 10 build supports X.509 certificates, PKCS#12 (PFX), and PKCS#7 files.
Windows serialized certificates/stores and Authenticode files are no longer supported as certificate-file inputs; re-export their certificates in one of the supported formats.

The .NET 10 build of Sign CLI does not support RC4-encrypted PFX files supplied through `certificate-store --certificate-file`.
Re-export the existing certificate and private key to an AES-encrypted PFX before using this build; no new certificate or signing key is required.

The .NET 10 build also rejects raw base64-encoded PKCS#7 files with trailing NUL (`\0`) characters, which the .NET 8 build accepts.
Remove the trailing NUL from existing files. Exporters using [`CryptBinaryToString`](https://learn.microsoft.com/windows/win32/api/wincrypt/nf-wincrypt-cryptbinarytostringw) should write only the returned character count, which excludes the terminator, rather than the entire allocated buffer.

## Usage

- For help with...
    - Azure Key Vault:  `sign code azure-key-vault --help`
    - Artifact Signing:  `sign code artifact-signing --help`
    - local signing:  `sign code certificate-store --help`
- Version information:  `sign --version`

See the [GitHub repository](https://github.com/dotnet/sign) for additional information and samples.

## License

This package is released as open source under the [MIT license](https://licenses.nuget.org/MIT).

## Feedback

Bug reports, feedback, and contributions are welcome at the [GitHub repository](https://github.com/dotnet/sign).

Happy signing! 🚀
