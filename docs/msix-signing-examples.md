# MSIX bundle signing examples

The following PowerShell examples use Sign CLI `0.9.1-beta.26475.3` on Windows, installed in the current directory as described in the [installation instructions](../README.md#install). Replace the placeholder values with your own configuration and sign in with Azure CLI (`az login`) using an identity that has access to your signing service.

## Sign an MSIX bundle with Azure Key Vault

Use an existing code signing certificate in Azure Key Vault. Configure the signing identity's permissions as described in [Best Practices](../README.md#best-practices).

```powershell
.\sign.exe code azure-key-vault `
  "MyApp.msixbundle" `
  --base-directory "C:\build\out" `
  --azure-key-vault-url "https://<vault-name>.vault.azure.net/" `
  --azure-key-vault-certificate "<certificate-name>" `
  --azure-credential-type azure-cli
```

`--azure-key-vault-certificate` is the certificate name in the vault, not a local certificate file.

## Sign an MSIX bundle with Artifact Signing

Use an existing Artifact Signing account and certificate profile. The signing identity needs the certificate profile signer role; see [Artifact Signing integration](artifact-signing-integration.md) for setup and authentication details.

```powershell
.\sign.exe code artifact-signing `
  "MyApp.msixbundle" `
  --base-directory "C:\build\out" `
  --artifact-signing-endpoint "https://<region>.codesigning.azure.net/" `
  --artifact-signing-account "<account-name>" `
  --artifact-signing-certificate-profile "<profile-name>" `
  --azure-credential-type azure-cli
```

Use the endpoint for the region containing your Artifact Signing account and profile.

## Select files and discover options

Both commands select `C:\build\out\MyApp.msixbundle`. To select all MSIX bundles below that directory, replace `"MyApp.msixbundle"` with the quoted relative glob `"**/*.msixbundle"`. See [File List Filtering and Globbing](file-globbing.md) for more selection options.

Signing updates the selected files in place unless `--output` is specified. Containers are processed from the inside out: their signable contents are signed before the enclosing container. MSIX and other Authenticode signing require Windows.

These examples explicitly use your Azure CLI login. If you omit `--azure-credential-type`, Sign CLI uses `DefaultAzureCredential`. For CI, prefer workload identity or managed identity; see [Best Practices](../README.md#best-practices) and [Sample Workflows](../README.md#sample-workflows).

To view the options supported by your installed version:

```powershell
.\sign.exe code azure-key-vault --help
.\sign.exe code artifact-signing --help
```
