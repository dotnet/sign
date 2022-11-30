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

* Isolate signing operations in a separate leg in your build pipeline.
* Ensure that this CLI and all files to be signed are in a directory under your control.
* Configure Azure Key Vault access with the following permissions:
  - Key permissions
    - Key Management Operations
      - Get
      - List
    - Cryptographic Operations
      - Verify
      - Sign
  - Secret permissions
    - Secret Management Operations
      - Get
      - List
  - Certificate permissions
    - Certificate Management Operations
      - Get
      - List
* Follow [Best practices for using Azure Key Vault](https://learn.microsoft.com/en-us/azure/key-vault/general/best-practices)
