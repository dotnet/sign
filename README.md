# Sign CLI

[<img align="right" src="https://xunit.net/images/dotnet-fdn-logo.png" width="100" />](https://www.dotnetfoundation.org/)

This project aims to make it easier to integrate secure code signing into a CI pipeline by using cloud-based hardware security module(HSM)-protected keys. This project is part of the [.NET Foundation](https://www.dotnetfoundation.org/) and operates under their [code of conduct](https://www.dotnetfoundation.org/code-of-conduct). It is licensed under [MIT](https://opensource.org/licenses/MIT) (an OSI approved license).

## Prerequisites

- An up-to-date x64-based version of Windows currently in [mainstream support](https://learn.microsoft.com/lifecycle/products/)
- [.NET 8 SDK or later](https://dotnet.microsoft.com/download)
- [Microsoft Visual C++ 14 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe)

## Install

To install Sign CLI in the current directory, open a command prompt and execute:

```
dotnet tool install --tool-path . --prerelease sign
```

To run Sign CLI, execute `sign` from the same directory.

## Design

Given an initial file path or glob pattern, this tool recursively searches directories and containers to find signable files and containers.  For each signable artifact, the tool uses an implementation of [`System.Security.Cryptography.RSA`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsa?view=net-7.0) that delegates the signing operation to Azure Key Vault.  The tool computes a digest (or hash) of the to-be-signed content and submits the digest --- not the original content --- to Azure Key Vault for digest signing.  The returned raw signature value is then incorporated in whatever signature format is appropriate for the file type.  Signable content is not sent to Azure Key Vault.

While the current version is limited to RSA and Azure Key Vault, it is desirable to support ECDSA and other cloud providers in the future.

## Supported File Types

- `.msi`, `.msp`, `.msm`, `.cab`, `.dll`, `.exe`, `.appx`, `.appxbundle`, `.msix`, `.msixbundle`, `.sys`, `.vxd`, `.ps1`, `.psm1`, and any portable executable (PE) file (via [AzureSignTool](https://github.com/vcsjones/AzureSignTool))
- `.vsix` via [OpenOpcSignTool](https://github.com/vcsjones/OpenOpcSignTool)
- ClickOnce `.application` and `.vsto` (via `Mage`). Notes below.
- `.nupkg` via [NuGetKeyVaultSignTool](https://github.com/novotnyllc/NuGetKeyVaultSignTool)

## ClickOnce
There are a couple of possibilities for signing ClickOnce packages.

Generally you will want to sign an entire package and all its contents i.e. the deployment manifest (`.application` or `.vsto`),
application manifest (`.exe.manifest` or `.dll.manifest`) and the underlying `.exe` and `.dll` files themselves.
To do this, ensure that the entire contents of the package are available (i.e. the whole `publish` folder from your build) and pass
the deployment manifest as the file to sign - the rest of the files will be detected and signed in the proper order automatically.

You can also re-sign just the deployment manifest in case you want to e.g. change the Deployment URL but leave the rest of the contents the
same. To do this, pass the deployment manifest as the file to sign as in the case above, but just don't have the rest of the files
present on-disk alongside it. This tool will detect that they're missing and just update the signature on the deployment manifest.
Note that this is strictly for re-signing an already-signed deployment manifest - you cannot have a signed deployment manifest that
points to an un-signed application manifest. You must also take care to sign all manifests with the same certificate otherwise the application
will not install.

You should also use the `filter` parameter with the file list to sign, something like this:
```
**/ProjectAddIn1.*
**/setup.exe
```

## Best Practices

* Create a [ServicePrincipal with minimum permissions](https://learn.microsoft.com/en-us/azure/active-directory/develop/howto-create-service-principal-portal). Note that you do not need to assign any subscription-level roles to this identity. Only access to Key Vault is required.
* Follow [Best practices for using Azure Key Vault](https://learn.microsoft.com/en-us/azure/key-vault/general/best-practices). The Premium SKU is required for code signing certificates to meet key storage requirements.
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
* Ensure that this CLI and all files to be signed are in a directory under your control.
* Execute this CLI as a standard user.  Elevation is not required.
* Use [OIDC authentication from your GitHub Action to Azure](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure?tabs=azure-portal%2Cwindows#use-the-azure-login-action-with-openid-connect).

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

Code signing certificates must use the `RSA-HSM` key type to ensure the private keys are stored in a FIPS 140-2 compliant manner. While you can import a certificate from a PFX file, if available, the most secure option is to create a new Certificate Signing Request to provide to your certificate authority, and then merge in the public certificate they issue. Detailed steps are available [here](https://learn.microsoft.com/en-us/answers/questions/732422/ev-code-signing-with-azure-keyvault-and-azure-pipe).

## Migrating from the legacy code signing service

If you've been using the legacy code signing service, using `SignClient.exe` to upload files for signing, you can use your existing certificate and Key Vault with this new tool. You will need to create a new ServicePrincipal and assign it permissions as described above.

## FAQ

### What signature algorithms are supported?

At this time, only RSA PKCS #1 v1.5 is supported.

ECDSA is not supported.  Not only do some signature providers not support ECDSA, [the Microsoft Trusted Root Program does not support ECDSA code signing.](https://learn.microsoft.com/security/trusted-root/program-requirements#b-signature-requirements)

> **Please Note**: Signatures using elliptical curve cryptography (ECC), such as ECDSA, aren't supported in Windows and newer Windows security features. Users utilizing these algorithms and certificates will face various errors and potential security risks. The Microsoft Trusted Root Program recommends that ECC/ECDSA certificates shouldn't be issued to subscribers due to this known incompatibility and risk.

## Useful Links

* [Issue Triage Policy](triage-policy.md)
