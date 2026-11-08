# Sign CLI

[<img align="right" src="https://xunit.net/images/dotnet-fdn-logo.png" width="100" />](https://www.dotnetfoundation.org/)

This project aims to make it easier to integrate secure code signing into a CI pipeline by using cloud-based hardware security module(HSM)-protected keys. This project is part of the [.NET Foundation](https://www.dotnetfoundation.org/) and operates under their [code of conduct](https://www.dotnetfoundation.org/code-of-conduct). It is licensed under [MIT](https://opensource.org/licenses/MIT) (an OSI approved license).

You can find the latest version of Sign CLI on [NuGet.org](https://www.nuget.org/packages/sign).

## Prerequisites

- An up-to-date x64-based version of Windows currently in [mainstream support](https://learn.microsoft.com/lifecycle/products/)
- [.NET 8 SDK or later](https://dotnet.microsoft.com/download)
- [Microsoft Visual C++ 14 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe)
- For ClickOnce and VSTO signing: a recent, ideally the latest, [.NET Framework](https://dotnet.microsoft.com/download/dotnet-framework)

The Sign CLI package includes the MSBuild manifest-utility assemblies used for ClickOnce version 2; a separate Visual Studio or MSBuild installation is not required. These packaged assemblies are required at run time: if they are removed, version-2 operations fail rather than falling back to another MSBuild installation or cache.

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

## Install

To install Sign CLI in the current directory, open a command prompt and execute:

```
dotnet tool install --tool-path . --prerelease sign
```

To run Sign CLI, execute `sign` from the same directory.

## Design

Given an initial file path or glob pattern, this tool recursively searches directories and containers to find signable files and containers.  For each signable artifact, the tool uses an implementation of [`System.Security.Cryptography.RSA`](https://learn.microsoft.com/dotnet/api/system.security.cryptography.rsa?view=net-8.0) that delegates the signing operation to Azure Key Vault.  The tool computes a digest (or hash) of the to-be-signed content and submits the digest --- not the original content --- to Azure Key Vault for digest signing.  The returned raw signature value is then incorporated in whatever signature format is appropriate for the file type.  Signable content is not sent to Azure Key Vault.

While the current version is limited to RSA and Azure Key Vault, it is desirable to support ECDSA and other cloud providers in the future.

## Supported File Types

- `.msi`, `.msp`, `.msm`, `.cab`, `.dll`, `.exe`, `.appx`, `.appxbundle`, `.msix`, `.msixbundle`, `.sys`, `.vxd`, `.ps1`, `.psm1`, and any portable executable (PE) file (via [AzureSignTool](https://github.com/vcsjones/AzureSignTool))
- `.vsix`
- ClickOnce `.application` and VSTO `.vsto` deployment manifests; opt-in version 2 also supports explicit ClickOnce application `.manifest` inputs. Unrelated side-by-side/fusion `.manifest` files are not ClickOnce inputs. See below.
- `.nupkg`

## ClickOnce

Omitting `--clickonce-signing-version` selects **version 1**, the legacy algorithm with recursive directory discovery and Mage-based metadata orchestration. Explicit `--clickonce-signing-version 1` keeps that behavior. Neither choice emits a migration or deprecation warning during the initial rollout. Specify the option at most once; repeated occurrences are invalid even when the values match.

**Version 2 is opt-in** with `--clickonce-signing-version 2`. It follows the deployment manifest's referenced application version, signs selected supported payloads, refreshes and signs the application manifest, then refreshes and signs the deployment manifest. It uses manifest APIs rather than the legacy Mage orchestration; other signing formats are unchanged. Independently glob-matched files in other versions still follow their normal signing path unless excluded.

For full signing, keep the complete referenced publish layout available and pass the deployment manifest. For version-2 re-signing, use `--no-sign-clickonce-deps` to update and sign only specified ClickOnce manifests, or `--no-update-clickonce-manifest` to sign them as-is without needing referenced files. These options are mutually exclusive, require explicit version 2, and do not disable ordinary signing of user-matched non-ClickOnce inputs. None of the three options has a short alias.

The legacy deployment-only re-signing workflow, where referenced files are absent, remains available in version 1. Version 2 fails by default if the referenced application manifest is missing; use its sign-as-is option only when existing metadata is intentionally retained. Callers must maintain consistent payload/application/deployment references and use the same certificate for related manifests. Within one invocation, overlapping explicit and manifest-selected work is coordinated so each source signing operation has one owner and all waiters reuse its result or failure. That coordination is not persisted across commands, and signing is not transactional.

See the [ClickOnce signing guide](docs/clickonce-signing.md) for complete options, filtering, `.deploy` mapping, VSTO archival-copy responsibilities, and practical examples.

## Best Practices

* Create a [ServicePrincipal with minimum permissions](https://learn.microsoft.com/azure/active-directory/develop/howto-create-service-principal-portal). Note that you do not need to assign any subscription-level roles to this identity. Only access to Key Vault is required.
* Follow [Best practices for using Azure Key Vault](https://learn.microsoft.com/azure/key-vault/general/best-practices). The Premium SKU is required for code signing certificates to meet key storage requirements.
   * If using Azure role-based access control (RBAC), [configure your signing account to have these roles](https://learn.microsoft.com/azure/key-vault/general/rbac-guide?tabs=azure-portal):
     - Key Vault Reader
     - Key Vault Crypto User
   * If using Azure Key Vault access policies, [configure an access policy](https://learn.microsoft.com/azure/key-vault/general/assign-access-policy?tabs=azure-portal) for your signing account to have minimal permissions:
     - Key permissions
       - Cryptographic Operations
         - Sign
       - Key Management Operations
         - Get  _(Note:  this is only for the public key not the private key.)_
     - Certificate permissions
       - Certificate Management Operations
         - Get
* Isolate signing operations in a separate leg of your build pipeline.
* Ensure that this CLI and all input and output files are in a directory under your control.
* Execute this CLI as a standard user.  Elevation is not required.
* Use [OIDC authentication from your GitHub Action to Azure](https://learn.microsoft.com/azure/developer/github/connect-from-azure?tabs=azure-portal%2Cwindows#use-the-azure-login-action-with-openid-connect).

## Sample Workflows

* [Azure DevOps Pipelines](./docs/azdo-build-and-sign.yml)
* [GitHub Actions](./docs/gh-build-and-sign.yml)

Code signing is a complex process that may involve multiple signing formats and artifact types. Some artifacts are containers that contain other signable file types. For example, NuGet Packages (`.nupkg`) frequently contain `.dll` files. The signing tool will sign all files inside-out, starting with the most nested files and then the outer files, ensuring everything is signed in the correct order.

Signing `.exe`/`.dll` files, and other Authenticode file types is only possible on Windows at this time. The recommended solution is to build on one agent and sign on another using jobs or stages where the signing steps run on Windows. Running code signing on a separate stage to ensure secrets aren't exposed to the build stage.

### Build Variables

The following information is needed for the signing build:

* `Tenant Id` Azure AD tenant
* `Client Id` / `Application Id` ServicePrincipal identifier
* `Key Vault Url` Url to Key Vault. Must be a Premium Sku for EV code signing certificates and all certificates issued after June 2023
* `Certificate Id` Id of the certificate in Key Vault.
* `Client Secret` for Azure DevOps Pipelines
* `Subscription Id` for GitHub Actions

## Creating a code signing certificate in Azure Key Vault

Code signing certificates must use the `RSA-HSM` key type to ensure the private keys are stored in a FIPS 140-2 compliant manner. While you can import a certificate from a PFX file, if available, the most secure option is to create a new Certificate Signing Request to provide to your certificate authority, and then merge in the public certificate they issue. Detailed steps are available [here](https://learn.microsoft.com/answers/questions/732422/ev-code-signing-with-azure-keyvault-and-azure-pipe).

## Migrating from the legacy code signing service

If you've been using the legacy code signing service, using `SignClient.exe` to upload files for signing, you can use your existing certificate and Key Vault with this new tool. You will need to create a new ServicePrincipal and assign it permissions as described above.

## FAQ

### What signature algorithms are supported?

At this time, only RSA PKCS #1 v1.5 is supported.

ECDSA is not supported.  Not only do some signature providers not support ECDSA, [the Microsoft Trusted Root Program does not support ECDSA code signing.](https://learn.microsoft.com/security/trusted-root/program-requirements#b-signature-requirements)

> **Please Note**: Signatures using elliptical curve cryptography (ECC), such as ECDSA, aren't supported in Windows and newer Windows security features. Users utilizing these algorithms and certificates will face various errors and potential security risks. The Microsoft Trusted Root Program recommends that ECC/ECDSA certificates shouldn't be issued to subscribers due to this known incompatibility and risk.

## Useful Links

* [Sign CLI Primer](docs/primer.md) (for maintainers: cryptography vocabulary, signature-format details)
* [Issue Triage Policy](triage-policy.md)
