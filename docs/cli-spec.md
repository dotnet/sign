# Signing CLI specficiation

### Inputs

The parameters to the signing client are as follows:

`sign code AzureKeyVault [options] <files>`

Options:

- `-o | --output` - The output file. If omitted, overwrites input. Must be a directory if multiple files are specified.
- `-b | --base-directory` - Base directory for files to override the working directory.
- `-f | --force` - Overwrites a signature if it exists.
- `-m | --max-concurrency` - Maximum concurrency (default is 4)
- `-fl | --filelist` - Path to file containing paths of files to sign within an archive
- `-fd | --file-digest` - The digest algorithm to hash the file with.
- `-tr | --timestamp-rfc3161` - Specifies the RFC 3161 timestamp server's URL.
- `-d | --description` - Description
- `-u | --descriptionUrl` - Description Url
- `-td | --timestamp-digest` - Used with the -tr switch to request a digest algorithm used by the RFC 3161 timestamp server.
- `-kvu | --azure-key-vault-url` - The URL to an Azure Key Vault.
- `-kvt | --azure-key-vault-tenant-id` - The Tenant Id to authenticate to the Azure Key Vault..
- `-kvi | --azure-key-vault-client-id` - The Client ID to authenticate to the Azure Key Vault.
- `-kvs | --azure-key-vault-client-secret` - The Client Secret to authenticate to the Azure Key Vault.
- `-kvc | --azure-key-vault-certificate` - The name of the certificate in Azure Key Vault.
- `-kvm | --azure-key-vault-managed-identity` - Use a Managed Identity to access Azure Key Vault.

Signing an archive type (`.zip`, `.nupkg`, `.vsix`) will open up the archive and sign any supported file types. It is strongly recommended to use the `filelist` parameter to explicitly list the files inside the archive that should be signed. Signing is recursive; it will sign contents of any detectected `Zip`, `NuPkg` or `VSIX` files inside the uploaded one. After signing contents of the archive, the archive itself is signed if supported (currently `VSIX`).

### File list

When specified, only files that match patterns in the list will be signed. This is critical as only first party files should be signed. Files that are third party should not be signed.

The format for the file contains one file path/pattern per line and does support globs.

```
**/MyLibrary.*
**/MyOtherLibrary.dll
```

## ClickOnce
ClickOnce files can be signed with this tool, but it requires an extra step -- you must zip up the `publish` directory containing the `setup.exe`, `foo.application` or `foo.vsto` files along with the `Application Files` directory. The `Application Files` must only have a single subdirectory (version you want to sign). Zip these and then rename the extension to `.clickonce` before submitting to the tool. Once done, you can extract the signed files wherever you'd like for publication. If the `name` parameter is supplied, it's used in the `Mage` name to update the `Product` in the manifests. If the `descriptionUrl` parameter is supplied, it's used as the `supportUrl` in the manifests.  You should also use the `filter` parameter with the file list to sign, something like this:

```
**/ProjectAddIn1.*
**/setup.exe
```