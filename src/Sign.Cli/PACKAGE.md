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

The installed package includes the MSBuild manifest-utility assemblies used for ClickOnce version 2; a separate Visual Studio or MSBuild installation is not required. These packaged assemblies are required at run time: removing them causes version-2 operations to fail instead of falling back to another MSBuild installation or cache.

## Usage

- For help with...
    - Azure Key Vault:  `sign code azure-key-vault --help`
    - Artifact Signing:  `sign code artifact-signing --help`
    - local signing:  `sign code certificate-store --help`
- Version information:  `sign --version`

See the [GitHub repository](https://github.com/dotnet/sign) for additional information and samples.

## ClickOnce and VSTO

ClickOnce `.application` and VSTO `.vsto` deployment manifests are supported. Opt-in version 2 also supports explicitly specified valid ClickOnce application `.manifest` files; unrelated side-by-side/fusion manifests with the same extension are not treated as ClickOnce.

`--clickonce-signing-version 1|2` accepts only `1` or `2`. Omission selects version 1, preserving legacy recursive discovery and Mage-based metadata orchestration without a migration/deprecation warning during the initial rollout. Specify the option at most once; every repeated occurrence is invalid, even when the values match. Version 2 remains opt-in, follows manifest references, and uses manifest APIs instead of that legacy orchestration. Other signing formats are unchanged.

With `--clickonce-signing-version 2`, choose at most one dependency option:

- `--no-sign-clickonce-deps`: update and sign only specified ClickOnce manifests. Referenced files may be discovered for metadata refresh, but dependent manifests and payloads are not implicitly signed.
- `--no-update-clickonce-manifest`: sign specified ClickOnce manifests as-is, without dependency resolution or file-information updates. Referenced files need not be available; callers own consistency with related manifests.

Both dependency options are invalid with omitted version or explicit version 1. None of the three options has a short alias. Combining the dependency options reports: `The '--no-sign-clickonce-deps' and '--no-update-clickonce-manifest' options cannot be combined.`

Normal user-matched non-ClickOnce inputs still follow their ordinary signing path in either mode. Full version-2 signing requires the referenced application manifest and payloads, processes selected payloads before the application manifest and then the deployment manifest, and refreshes SHA-256 metadata. It does not support legacy runtimes requiring SHA-1 manifest hashes. Broad globs can independently select other application versions; exclude them when needed. Within one invocation, each coordinated source signing operation has one owner and all waiters reuse its result or failure. Coordination does not cross invocations, and signing is not transactional.

For re-signing across invocations, arrange payload/application/deployment order and use the same certificate for related manifests. When application and deployment manifests are both explicit inputs in one invocation, the application is processed first. Signing an application manifest as-is can invalidate deployment references. Version 2 preserves the VSTO-specific XML subtree in application manifests, but does not implicitly synchronize VSTO's archival deployment copy; copy the signed deployment manifest explicitly if parity is required.

See the [ClickOnce signing guide](https://github.com/dotnet/sign/blob/main/docs/clickonce-signing.md) for examples and migration guidance.

## License

This package is released as open source under the [MIT license](https://licenses.nuget.org/MIT).

## Feedback

Bug reports, feedback, and contributions are welcome at the [GitHub repository](https://github.com/dotnet/sign).

Happy signing! 🚀
