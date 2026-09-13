# Signing CLI specficiation

## Inputs

The parameters to the signing client are as follows:

```dotnetcli
sign code [command] [options] <files>
```

### Commands
- `azure-key-vault` - Use Azure Key Vault to acquire a private key for signing.
- `certificate-store` - Use a certificate and private key stored in a PFX file, under Windows Certificate Manager, or in a USB device with a Cryprographic Service Provider.

### Options:
Required:
- `-d | --description` - Description of the signing certificate.
- `-u | --descriptionUrl` - Description Url of the signing certificate.

Optional:
- `-o | --output` - The output file. If omitted, overwrites input. Must be a directory if multiple files are specified.
- `-b | --base-directory` - Base directory for files to override the working directory.
- `-f | --force` - Overwrites a signature if it exists.
- `-m | --max-concurrency` - Maximum concurrency (default is 4)
- `-fl | --filelist` - Path to file containing paths of files to sign within an archive
- `-fd | --file-digest` - The digest algorithm to hash the file with.
- `-tr | --timestamp-rfc3161` - Specifies the RFC 3161 timestamp server's URL.
- `-td | --timestamp-digest` - Used with the -tr switch to request a digest algorithm used by the RFC 3161 timestamp server.

Signing an archive type (`.zip`, `.nupkg`, `.vsix`) will open up the archive and sign any supported file types. It is strongly recommended to use the `filelist` parameter to explicitly list the files inside the archive that should be signed. Signing is recursive; it will sign contents of any detectected `Zip`, `NuPkg` or `VSIX` files inside the uploaded one. After signing contents of the archive, the archive itself is signed if supported (currently `VSIX`).

### File list

Although the tool recursively signs supported file types if within a container, the `-fl` option allows for specifying which files to sign. This is critical as only first party files should be signed. Files that are third party should not be signed.
The format for the file contains one file path/pattern per line and does support globs:
```
**/MyLibrary.*
**/MyOtherLibrary.dll
```


## Sign Using Azure Key Vault
Sign requires a vault URL and certificate name as a minimum to sign, although additional options are available.

```dotnetcli
sign code azure-key-vault [options] <files>
```

### Options:
Required:
- `-kvu | --azure-key-vault-url` - The URL to an Azure Key Vault.
- `-kvc | --azure-key-vault-certificate` - The name of the certificate in Azure Key Vault.
- `-d | --description` - Description of the signing certificate.
- `-u | --descriptionUrl` - Description Url of the signing certificate.

Optional:
- `-kvt | --azure-key-vault-tenant-id` - The Tenant Id to authenticate to the Azure Key Vault.
- `-kvi | --azure-key-vault-client-id` - The Client ID to authenticate to the Azure Key Vault.
- `-kvs | --azure-key-vault-client-secret` - The Client Secret to authenticate to the Azure Key Vault.
- `-kvm | --azure-key-vault-managed-identity` - Use a Managed Identity to access Azure Key Vault.

### Sample
```dotnetcli
sign code azure-key-vault [options] <files>
```

### ClickOnce Special Case
ClickOnce files can be signed with this tool, but it requires an extra step -- you must zip up the `publish` directory containing the `setup.exe`, `foo.application` or `foo.vsto` files along with the `Application Files` directory. The `Application Files` must only have a single subdirectory (version you want to sign). Zip these and then rename the extension to `.clickonce` before submitting to the tool. Once done, you can extract the signed files wherever you'd like for publication. If the `name` parameter is supplied, it's used in the `Mage` name to update the `Product` in the manifests. If the `descriptionUrl` parameter is supplied, it's used as the `supportUrl` in the manifests.  You should also use the `filter` parameter with the file list to sign, something like this:

## Sign Using Certificate Store or USB Device
Only VSIX packages can be signed using a certificate store or USB device, with more adding support in the future. 

We support certificates and private keys stored in any combination of these locations:
- `PFX`, `P7B`, or `CER` files
- Imported into Windows Certificate Manager
- Stored in a USB device with access via a Cryptographic Service Provider (CSP)

```dotnetcli
sign code certificate-store [options] <files>
```

### Options:
Includes [General Options](#general-options).

Required:
- `-s | --sha1` - SHA-1 thumbprint used to identify a certificate.
- `-d | --description` - Description of the signing certificate.
- `-u | --descriptionUrl` - Description Url of the signing certificate.

Optional:
- `-cf | --certificate-file` - PFX, P7B, or CER file containing a certificate and potentially a private key.
- `-p | --password` - Optional password for certificate file.
- `-csp | --crypto-service-provider` - Cryptographic Service Provider containing the private key.
- `-k | --key-container` - Private key container name.
- `-km | --use-machine-key-container` - Use a machine-level private key container.  (The default is user-level.) [default: False]

### VSIX Sample

- Signing using a PFX file with certificate and private key:
```shell
sign code certificate-store -s f5ec6169345347a7cd2f83af662970d5d0bfc914  -cf "D:\Certs\f5ec6169345347a7cd2f83af662970d5d0bfc914.pfx" -d "VSIX Signature" -u "http://timestamp.acs.microsoft.com/" "C:\Users\Contoso\Downloads\FingerSnapper2022.vsix"
```

- Signing using Microsoft Certificate Manager:
```shell
code certificate-store -s f5ec6169345347a7cd2f83af662970d5d0bfc914 -csp "Microsoft Software Key Storage Provider" -d "VSIX Signature" -u "http://timestamp.acs.microsoft.com/" "C:\Users\Contoso\Downloads\FingerSnapper2022.vsix"
```

- Signing using a private key in a USB drive:
```shell
code certificate-store -s f5ec6169345347a7cd2f83af662970d5d0bfc914 -csp "eToken Base Cryptographic Provider" -d "VSIX Signature" -u "http://timestamp.acs.microsoft.com/" "C:\Users\Contoso\Downloads\FingerSnapper2022.vsix"
```

- Signing using a USB drive using a specific key container:
```shell
code certificate-store -s f5ec6169345347a7cd2f83af662970d5d0bfc914 -csp "eToken Base Cryptographic Provider" -k "NuGet Signing.629c9149345347cd2f83af6f5ec70d5d0a7bf616" -d "VSIX Signature" -u "http://timestamp.acs.microsoft.com/" "C:\Users\Contoso\Downloads\FingerSnapper2022.vsix"
```