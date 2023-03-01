# Sign CLI

[<img align="right" src="https://xunit.net/images/dotnet-fdn-logo.png" width="100" />](https://www.dotnetfoundation.org/)

This project aims to make it easier to integrate secure code signing into a CI pipeline by using cloud-based hardware security module(HSM)-protected keys. This project is part of the [.NET Foundation](https://www.dotnetfoundation.org/) and operates under their [code of conduct](https://www.dotnetfoundation.org/code-of-conduct). It is licensed under [MIT](https://opensource.org/licenses/MIT) (an OSI approved license).

## Design

Given an initial file path or glob pattern, this tool recursively searches directories and containers to find signable files and containers.  For each signable artifact, the tool uses an implementation of [`System.Security.Cryptography.RSA`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsa?view=net-7.0) that delegates the signing operation to Azure Key Vault.  The tool computes a digest (or hash) of the to-be-signed content and submits the digest --- not the original content --- to Azure Key Vault for digest signing.  The returned raw signature value is then incorporated in whatever signature format is appropriate for the file type.  Signable content is not sent to Azure Key Vault.

While the current version is limited to RSA and Azure Key Vault, it is desirable to support ECDSA and other cloud providers in the future.

## Supported File Types

- `.msi`, `.msp`, `.msm`, `.cab`, `.dll`, `.exe`, `.appx`, `.appxbundle`, `.msix`, `.msixbundle`, `.sys`, `.vxd`, `.ps1`, `.psm1`, and any portable executable (PE) file (via [AzureSignTool](https://github.com/vcsjones/AzureSignTool))
- `.vsix` via [OpenOpcSignTool](https://github.com/vcsjones/OpenOpcSignTool)
- ClickOnce `.application` and `.vsto` (via `Mage`). Special instructions below.
- `.nupkg` via [NuGetKeyVaultSignTool](https://github.com/novotnyllc/NuGetKeyVaultSignTool)

## ClickOnce
ClickOnce files can be signed with this tool, but it requires an extra step -- you must zip up the `publish` directory containing the `setup.exe`, `foo.application` or `foo.vsto` files along with the `Application Files` directory. The `Application Files` must only have a single subdirectory (version you want to sign). Zip these and then rename the extension to `.clickonce` before submitting to the tool. Once done, you can extract the signed files wherever you'd like for publication. If the `name` parameter is supplied, it's used in the `Mage` name to update the `Product` in the manifests. If the `descriptionUrl` parameter is supplied, it's used as the `supportUrl` in the manifests.

You should also use the `filter` parameter with the file list to sign, something like this:
```
**/ProjectAddIn1.*
**/setup.exe
```

## Best Practices

* Create a [ServicePrincipal with minimum permissions](https://learn.microsoft.com/en-us/azure/active-directory/develop/howto-create-service-principal-portal). Note that you do not need to assign any subscription-level roles to this identity. Only access to Key Vault is required.
* Follow [Best practices for using Azure Key Vault](https://learn.microsoft.com/en-us/azure/key-vault/general/best-practices). The Premium SKU is required for code signing certificates to meet key storage requirements.
* [Configure an Azure Key Vault access policy](https://learn.microsoft.com/en-us/azure/key-vault/general/assign-access-policy?tabs=azure-portal) for your signing account to have minimal permissions:
  - Key permissions
    - Cryptographic Operations
      - Sign
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