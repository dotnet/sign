# Signing CLI tool

## Background

Code signing is a way to provide tamper resistance to binary files and provide a way of establishing identity. There are different code signing mechanisms, but the most common on Windows and .NET are based on X.509 certificates. Other code signing formats may be based on PGP and other non-certificate-based systems.

There are several technology areas within the Windows and .NET ecosystem that support code signing:

- PE files & certain scripts via Authenticode (dll, exe, ps1, sys)
- MSIX via Authenticode (msix, msixbundle) & related manifests
- Visual Studio Extensions (VSIX) via Open Packaging Convention
- ClickOnce & VSTO via Mage (XML Digital Signatures)
- NuGet Packages

Today each of these areas has their own tools (SignTool, VISXSignTool, Mage, NuGet) to create the signatures. Each tool has its own set of parameters and are written to assume use of the local Certificate Store API's by default. Without any common implementation, any changes to the code signing landscape requires updates to all of the tools. In May 2022, the CA/B forum updates its baseline requirements to require that all new code signing certificates issued after June 2023 use hardware security modules (HSMs) to prevent key theft. While some HSMs contain CSP/KSP support to expose certificates through the CertStore API's, they frequently contain significant limitations, such as requiring an interactive session to authenticate to the device. This makes signing code in the cloud, on build agents, extremely difficult for mainline scenarios.

There are many HSM cloud services, including Azure Key Vault, that meet the updated key storage requirements, however we do not have first-party support for signing code with those services. There are third-party community solutions to fill this gap: [AzureSignTool](https://github.com/vcsjones/AzureSignTool), [NuGetKeyVaultSignTool](https://github.com/novotnyllc/NuGetKeyVaultSignTool), [OpenOpcSignTool](https://github.com/vcsjones/OpenOpcSignTool), and the .NET Foundation Signing Service (which uses these plus adds additional supported file formats and orchestrates signing the the various types in the right order).

While the third party tools can help, they leave the complicated work of signing the files in the right order to each user, and support for cloud HSMs is limited to Key Vault. With the [announcement](https://techcommunity.microsoft.com/t5/security-compliance-and-identity/azure-code-signing-democratizing-trust-for-developers-and/ba-p/3604669) of Azure Code Signing, and the move towards HSMs, there's a need to support multiple code signing providers in our signing tools.

## Challenges

There are a few challenges around code signing:

### Local Certificates

Today the code signing tooling in the Windows and .NET SDK uses PFX (public/private certificate key pair files or the local certificate store for obtaining certificates). There risks to this approach:

- PFX files are targets in data breaches; their passwords can be cracked
- Certificates in a local store can be used by any app/malware
- There’s no revocation mechanism for the users’ access to the certificate; they always have it
- No auditing of signing operations possible
- EV code signing certificates aren’t easily supported as they require FIPS 140-2 hardware devices with drivers

In May 2022, the CA/B forum updated its baseline requirements to require HSMs so local certificates will no longer be issued for publically trusted roots. The only support the current signing tools have for this scenario is via CSP/KSP drivers provided by some HSM vendors and those do not work well for cloud-based build agents. The current tools would need new investment to support different backends.

### Orchestration

An application/library package typically contains multiple assets that need to be signed. For example, a NuGet package (`.nupkg` file) contains `.dll` files that also must be signed. An ClickOnce or MSIX package also contains `.dll` or `.exe` that need to be signed. A `.vsix` file can contain `.dll` files, and `.nupkg` file that must be signed. These files need to be signed "inside out" to ensure the proper sequence. That is, a `.vsix` containing a `.nupkg` needs to extract the inner `.nupkg` to sign the contained `.dll` files, then sign the `.nupkg` and any other `.dll` files, then repack and sign the `.vsix`. Other types, like ClickOnce and MSIX may contain manifest files that also must be updated during these operations.

With several different code signing tools required, each with its own command line parameters and defaults. The process of code signing is error-prone and hard to get right. The signing tool addresses these challanges by unifying the interface into a single set of options.

## Plan

Create a modern signing tool to replace the existing tools. The tool will handle all of our first party signing formats, orchestrate signing files in the right order, and have extensibility to support multiple signing providers. As our customers use a variety of clouds and HSMs, the extensibility will enable us to meet customers needs wherever they store their certificates.

While some of this could be done via an MSBuild task, a CLI tool is preferable to MSBuild tasks for a couple reasons:

- **Performance:** During a build, many binary artifacts are created that need to be signed. A multi-targeted NuGet package may contain several `.dll` files. An application will likely contain more than one file that needs to be signed. It's much more efficient to pass them all to a signing tool where parallelism is possible than to sign during the inner-loop.
- **Security:** Code signing is a sensitive operation that requires credentials/secrets. We do not want secrets to be in the MSBuild pipeline as that makes any logs contain those secrets. Ideally, a CI pipeline should contain a separate stage for code signing to ensure that credentials are never unintentionally exposed to a build stage.
- **Platform:** Authenticode is currently limited to Windows. Thus, while it's possible to sign a NuGet or VSIX cross-platform, the DLL's inside can't be signed unless running on Windows. With the NuGet packages being developer-only artifacts--they're not shipped with the apps--it's critical that the DLL's inside are also signed. Builds for binaries may run on any platform, but as signing is a discreet step in most CI pipelines, it's possible to use a Windows agent for this task.

### Roadmap

The scope of the preview release will be limited to the existing funtionality currently in the service. The remaining functionality in this spec will be delivered for the initial release. The .NET Foundation has a dependency on this tool being delivered by the end of 2022.

#### Preview

**Goals**

- Support for Authenticode, VSIX, NuGet (author signature), ClickOnce
- Only run on Windows x64.
- Support a single certificate for all files in the operation.

**Non-Goals**

- Strong Name signing won't be in v1; guidance is to use an snk not based on a cert. If easy, perhaps can revisit.
- Support for Microsoft internal builds and signing services.
- Containers, including Notary v2 support.
- Extensibility. v1 will support different signing providers.
- Support for platforms other than Windows x64. Future work will be required to support ARM64 and non-Windows hosts. Support for certain file types may be limited due to platform support.
- Offline distribution.

#### v1

**Goals**

- Extensibility mechanism to support different code signing providers with a dynamic lookup so the core client remains agnostic of the backend
- Offline distribution for core plus backend provider
- Three providers: Certificate Store, Azure Key Vault, Azure Code Signing
- Support for additional formats: [.HLKX](https://github.com/dotnet/sign/issues/422), [VBA](https://github.com/dotnet/sign/issues/364)
- Verification of signatures


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