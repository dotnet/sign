# Signing CLI tool

## Background

Code signing is a way to provide tamper detection to binary files and provide a way of establishing identity. There are different code signing mechanisms, but the most common on Windows and .NET are based on X.509 certificates.

There are several technology areas within the Windows and .NET ecosystem that support code signing:

- PE files & certain scripts via Authenticode (dll, exe, ps1, sys)
- MSIX via Authenticode (msix, msixbundle) & related manifests
- Visual Studio Extensions (VSIX) via Open Packaging Convention
- ClickOnce & VSTO via Mage (XML Digital Signatures)
- NuGet Packages

Today each of these areas has their own tools (SignTool, VISXSignTool, Mage, NuGet) to create signatures. Each tool has its own set of parameters and are written to assume use of the local certificate store API's by default. Without a shared implementation, a new code signing requirement can require individual updates to each tool. In May 2022, the CA/Browser Forum updated its [baseline requirements for publicly trusted code signing certificates](https://cabforum.org/wp-content/uploads/Baseline-Requirements-for-the-Issuance-and-Management-of-Code-Signing.v3.2.pdf) to require that all new code signing certificates issued after June 2023 use hardware security modules (HSM's) to prevent private key theft. While some HSM's contain CSP/KSP support to expose certificates through Windows' certificate store API's, they frequently contain significant limitations, such as requiring an interactive session to authenticate to the device. This makes signing code in the cloud and on build agents extremely difficult for mainline scenarios.

There are many HSM cloud services, including Azure Key Vault, that meet the updated key storage requirements, however we do not have first-party support for signing code with those services. There are open source community solutions to fill this gap, such as:

* [AzureSignTool](https://github.com/vcsjones/AzureSignTool)
* [NuGetKeyVaultSignTool](https://github.com/novotnyllc/NuGetKeyVaultSignTool)
* [OpenOpcSignTool](https://github.com/vcsjones/OpenOpcSignTool)

The [.NET Foundation Signing Service](https://github.com/dotnet/sign/tree/legacy-service/servicing) builds on these solutions, adds additional supported file formats, and orchestrates signing the various file types in the right order.

While existing community solutions help, they leave the complicated work of signing the files in the right order to each user and support only Azure Key Vault. With the [announcement](https://techcommunity.microsoft.com/t5/security-compliance-and-identity/azure-code-signing-democratizing-trust-for-developers-and/ba-p/3604669) of Azure Code Signing and the move towards HSM's, there's a need to support multiple code signing providers in our signing tools.

## Challenges

There are a few challenges around code signing:

### Local Certificates

Today the code signing tooling in the Windows and .NET SDK uses PFX (public/private certificate key pair files or the local certificate store for obtaining certificates). There risks to this approach:

- PFX files are targets in data breaches; their passwords can be cracked
- Certificates in a local store can be used by any app/malware
- There’s no revocation mechanism for a user's access to the certificate; they always have it
- No auditing of signing operations possible
- EV code signing certificates aren’t easily supported as they require FIPS 140-2 hardware devices with drivers

In May 2022, the CA/Browser Forum updated its baseline requirements to require HSM's so local certificates will no longer be issued for publicly trusted code signing certificates. The only support the current signing tools have for this scenario is via CSP/KSP drivers provided by some HSM vendors, and those do not work well for cloud-based build agents. The current tools would need new investment to support different backends.

### Orchestration

An application/library package typically contains multiple assets that need to be signed. For example, a NuGet package (`.nupkg` file) contains `.dll` files that also must be signed. A ClickOnce or MSIX package also contains `.dll` or `.exe` files that need to be signed. A `.vsix` file can contain `.dll` files and `.nupkg` file that must be signed. These files need to be signed "inside out" to ensure the proper sequence. That is, a `.vsix` containing a `.nupkg` needs to extract the inner `.nupkg` to sign the contained `.dll` files, then sign the `.nupkg` and any other `.dll` files, then repack and sign the `.vsix`. Other types, like ClickOnce and MSIX may contain manifest files that also must be updated during these operations.

To properly sign all assets, multiple signing tools must be used, and each tool has its own command line syntax, options, and default. The process of code signing is error-prone and hard to get right. The signing tool addresses these challenges by unifying the interface into a single set of options.

## Proposal

Create a modern signing tool to eventually replace the existing tools. The tool will handle all of our first party signing formats, orchestrate signing files in the right order, and have extensibility to support multiple raw signature providers. As our customers use a variety of clouds and HSM's, the extensibility will enable us to meet our customers' needs wherever they store their certificates.

While some of this could be done via an MSBuild task, a CLI tool is preferable to MSBuild tasks for a couple reasons:

- **Performance:** During a build, many binary artifacts are created that need to be signed. A multi-targeted NuGet package may contain several `.dll` files. An application will likely contain more than one file that needs to be signed. It's much more efficient to pass them all to a signing tool where parallelism is possible than to sign during the inner-loop.
- **Security:** Code signing is a sensitive operation that requires credentials/secrets. Use of these secrets should be as limited as possible to prevent leakage into the rest of a build pipeline, such as log files or unrelated build tasks. Ideally, a CI pipeline should contain a separate stage for code signing to ensure that credentials are never unintentionally exposed to a build stage.
- **Platform:** Authenticode is currently limited to Windows. Thus, while it's possible to sign a NuGet or VSIX cross-platform, the DLL's inside can't be signed unless running on Windows. With the NuGet packages being developer-only artifacts--they're not shipped with the apps--it's critical that the DLL's inside are also signed. Builds for binaries may run on any platform, but as signing is a discreet step in most CI pipelines, it's reasonable to require a Windows build agent for this task.

### Roadmap

The scope of the preview release will be limited to the existing funtionality currently in the service. The remaining functionality in this spec will be delivered in a later 1.0 release. The .NET Foundation has a dependency on this tool being delivered by [June 30, 2023](https://learn.microsoft.com/en-us/answers/questions/768833/when-is-adal-and-azure-ad-graph-reaching-end-of-li.html).

#### Preview

**Goals**

- Support for Authenticode, VSIX, NuGet (author signature), ClickOnce
- Only run on Windows x64.
- Support a single certificate for all files in the operation.

**Non-Goals**

- Strong Name signing won't be in v1; guidance is to use an snk not based on a cert. If easy, perhaps can revisit.
- Containers, including Notary v2 support.
- Extensibility. v1 will support different signing providers.
- Support Authenticode on platforms other than Windows x64. Future work will be required to support ARM64 and non-Windows hosts. Support for certain file types may be limited due to platform support.
- Offline distribution.

#### v1

**Goals**

- Extensibility mechanism to support different code signing providers with a dynamic lookup so the core client remains agnostic of the backend
- Offline distribution for core plus backend provider
- Three providers: Certificate Store, Azure Key Vault, Azure Code Signing
- Support for additional formats: [.HLKX](https://github.com/dotnet/sign/issues/422), [VBA](https://github.com/dotnet/sign/issues/364)
- Verification of signatures


