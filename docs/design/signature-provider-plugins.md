# Sign CLI Signature Provider Plugins

**Owners** [Damon Tivel](https://github.com/dtivel) | [Claire Novotny](https://github.com/clairernovotny)

Recent CAB Forum updates to baseline requirements<sup>[1](#r1)</sup> strengthened storage requirements for private keys of publicly trusted code signing certificates.  While older, less secure storage options (e.g.:  [PKCS #12 & PFX](https://en.wikipedia.org/wiki/PKCS_12)) became obsolete, more secure options (e.g.:  [HSM](https://en.wikipedia.org/wiki/Hardware_security_module)) became standard.

As of writing this, Sign CLI only supports digest signing using Azure Key Vault.  To support users with private keys stored elsewhere (e.g.:  a different cloud provider, a signing service, or Windows' certificate store), Sign CLI needs a plugin model for signature providers.  Sign CLI users should be able to install a plugin that provides signing capabilities for their private key.

To be clear, there is nothing in this proposed plugin model that would preclude a plugin author from creating a plugin that enables signing using a PFX file, and such a plugin would be welcome to a subset of users.  However, given the relative lack of support in existing signing tools for more secure private key storage options, the primary driver for this proposal is enabling support for more secure storage options.

Note:  the term _signature provider_ plugin should not be confused with an [existing internal implementation detail already in Sign CLI](https://github.com/dotnet/sign/blob/ef0e6b3ef8281dff1d62cea34445bd88fc3e6714/src/Sign.Core/SignatureProviders/ISignatureProvider.cs).

## Scenarios and User Experience

It is assumed that Sign CLI has already been installed (e.g.:  [`dotnet tool install --global sign --version 0.9.1-beta.23356.1`](https://www.nuget.org/packages/sign/0.9.1-beta.23356.1)).

_The plugin names below are fictitious and for illustration purposes only._

### Sign artifacts using Azure Key Vault

First, the Azure Key Vault plugin must be installed.  The following command would download and install the latest version of the plugin.

```
sign plugin install Microsoft.Azure.KeyVault.Sign
```

Executing `sign code -?` will show the new available command:

```
...
Commands:
  azure-key-vault <file(s)>  Use Azure Key Vault.
```

Similarly, executing `sign code azure-key-vault -?` will show help for the new command and its options.

```
Description:
  Use Azure Key Vault.

Usage:
  sign code azure-key-vault <file(s)> [options]

Arguments:
  <file(s)>  File(s) to sign.

Options:
  -kvc, --azure-key-vault-certificate                    Name of the certificate in Azure Key Vault.
  <azure-key-vault-certificate> (REQUIRED)
  -kvi, --azure-key-vault-client-id                      Client ID to authenticate to Azure Key Vault.
  <azure-key-vault-client-id>
  -kvs, --azure-key-vault-client-secret                  Client secret to authenticate to Azure Key Vault.
  <azure-key-vault-client-secret>
  -kvm, --azure-key-vault-managed-identity               Managed identity to authenticate to Azure Key Vault.
  -kvt, --azure-key-vault-tenant-id                      Tenant ID to authenticate to Azure Key Vault.
  <azure-key-vault-tenant-id>
  -kvu, --azure-key-vault-url <azure-key-vault-url>      URL to an Azure Key Vault.
  -an, --application-name <application-name>             Application name (ClickOnce).
  -d, --description <description> (REQUIRED)             Description of the signing certificate.
  -u, --description-url <description-url> (REQUIRED)     Description URL of the signing certificate.
  -b, --base-directory <base-directory>                  Base directory for files.  Overrides the current working
                                                         directory. [default: F:\git\sign]
  -o, --output <output>                                  Output file or directory. If omitted, input files will be
                                                         overwritten.
  -pn, --publisher-name <publisher-name>                 Publisher name (ClickOnce).
  -fl, --file-list <file-list>                           Path to file containing paths of files to sign within an
                                                         archive.
  -fd, --file-digest <file-digest>                       Digest algorithm to hash files with. Allowed values are
                                                         'sha256', 'sha384', and 'sha512'. [default: SHA256]
  -t, --timestamp-url <timestamp-url>                    RFC 3161 timestamp server URL. [default:
                                                         http://timestamp.acs.microsoft.com/]
  -td, --timestamp-digest <timestamp-digest>             Digest algorithm for the RFC 3161 timestamp server. Allowed
                                                         values are sha256, sha384, and sha512. [default: SHA256]
  -m, --max-concurrency <max-concurrency>                Maximum concurrency. [default: 4]
  -v, --verbosity                                        Sets the verbosity level. Allowed values are 'none',
  <Critical|Debug|Error|Information|None|Trace|Warning>  'critical', 'error', 'warning', 'information', 'debug', and
                                                         'trace'. [default: Warning]
  -?, -h, --help                                         Show help and usage information
```

The new command can then be used to sign artifacts.

```
sign code azure-key-vault -kvu https://fake.url.vault.azure.net/ -kvc MyCertificate -kvm -d Description -u http://description.test -b C:\ClassLibrary1\ClassLibrary1\bin\Debug\net7.0 ClassLibrary1.dll
```

### Sign artifacts using Windows certificate store

First, the plugin must be installed.  The following command would download and install the latest version of the plugin.

```
sign plugin install Microsoft.Windows.CertificateStore.Sign
```

The new command can then be used to sign artifacts.

```
sign code certificate-store --store-location CurrentUser --store-name My --sha1fingerprint da39a3ee5e6b4b0d3255bfef95601890afd80709 -d Description -u http://description.test -b C:\ClassLibrary1\ClassLibrary1\bin\Debug\net7.0 ClassLibrary1.dll
```

## Requirements

### Goals

* Create a plugin model that enables pluggable signature providers.  A signature provider plugin will offer an alternate implementation of [`System.Security.Cryptography.RSA`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsa?view=net-7.0) and the relevant [`System.Security.Cryptography.X509Certificates.X509Certificate2`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate2?view=net-7.0).
* Make Sign CLI plugin-neutral.  While Sign CLI may install some "in-box" plugins (TBD), most plugins should be installed separately from Sign CLI itself, and Sign CLI's only interactions with any plugin should be through this plugin model.
* Enable Sign CLI and plugins to version and release independently.

### Non-Goals

* Create a distribution channel for plugins.  Sign CLI is a .NET tool and is available from <https://nuget.org>.  Plugin packages can be published to any NuGet feed, including <https://nuget.org>.
* Create a dynamic discovery mechanism for plugins.  Initially, we'll probably have a web page that lists common plugins and where to get them.
* Manage (list, update, uninstall) installed plugins.

## Design

### High-level approach

1. Create and publish a new _interfaces-only_ NuGet package that defines plugin-specific interfaces to be implemented by plugins.
2. Implement the [dependency inversion](https://en.wikipedia.org/wiki/Dependency_inversion_principle) pattern by having Sign CLI and plugins reference the interfaces package.
3. Move Azure Key Vault-specific implementations currently in Sign CLI into an Azure Key Vault-specific plugin.
4. Augment Sign CLI commands at runtime with contributions from installed plugins (like [these options](https://github.com/dotnet/sign/blob/ef0e6b3ef8281dff1d62cea34445bd88fc3e6714/src/Sign.Cli/AzureKeyVaultCommand.cs#L25-L31) for Azure Key Vault).
5. Enable Sign CLI to install new plugins and discover locally installed plugins.

This design roughly follows [.NET's existing plugin model](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support). 

### Interfaces package

We will create a new .NET assembly that contains only public interfaces to be implemented by plugins.  Sign CLI will implement a new command for a plugin that loads and interacts with the plugin implementation entirely by interfaces defined in the interfaces assembly.  This approach will enable Sign CLI and plugins to rev their implementations without either having any extraneous compile-time or runtime dependencies.

Proposed interface:

```C#
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Sign.Plugins.SignatureProvider.Interfaces
{
    public interface ISignatureProviderPlugin
    {
        Task InitializeAsync(IReadOnlyDictionary<string, object?> arguments, ILogger logger, CancellationToken cancellationToken);
        Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken);
        Task<RSA> GetRsaAsync(CancellationToken cancellationToken);
    }
}
```

The new interfaces assembly will be packaged and published to <https://nuget.org>, similar to [NuGetRecommender's contracts-only package](https://www.nuget.org/packages/Microsoft.DataAI.NuGetRecommender.Contracts).  The Sign CLI team will manage the source code repository for this package and publish the package to <https://nuget.org>.

The interfaces package itself can have package dependencies; however, because Sign CLI and all plugins would inherit new interfaces package dependencies, we should exercise due restraint and caution before adding new dependencies.  An example of a package dependency worth having is [`Microsoft.Extensions.Logging.Abstractions`](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions).  Because Sign CLI uses [`ILogger`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.ilogger?view=dotnet-plat-ext-7.0) ubiquitously, it makes sense that Sign CLI pass an `ILogger` instance to plugins for logging.  This means that plugin interfaces defined in the interfaces package should have an `ILogger` parameter and that the interfaces package must depend on the `Microsoft.Extensions.Logging.Abstractions` package.

#### Package versioning

The interfaces package version will follow strict [SemVer 2.0.0](https://semver.org/spec/v2.0.0.html) versioning rules.  As long as this specification is valid, it is expected that no interfaces package version will introduce a breaking change over any previous version.  Because the interfaces package is intended to provide stable abstractions to both Sign CLI and plugins, every package version will be fully backwards compatible.  Therefore, it is expected that we will only release packages in the version range [1.0.0, 2.0.0).

It is intended that we will only publish release versions of the interfaces package.  If, for any reason, we decide to publish prerelease versions later, it should still be assumed that official Sign CLI releases (even prerelease versions) will only reference release versions of the interfaces package.  For any given official Sign CLI release, Sign CLI should reference the latest release version of the interfaces package at that point in time.

#### Interface versioning

Interfaces defined in the interfaces package should be considered permanent and immutable.  New interfaces can be added, but existing interfaces should not be modified or removed as long as Sign CLI is expected to load plugins that implement those existing interfaces.

Versioning strategies for plugin interfaces are out of scope for this specification, but it is expected that all interfaces for every supported version of the plugin will be available in the interfaces package to enable Sign CLI users to install not only the latest version but previous, supported versions as well.

> Note:  If a plugin author wanted to remove support for older versions of a plugin, we could achieve that by specifying a minimum version of the plugin package in Sign CLI.  Older versions would be ignored, and plugin interfaces for those older versions could safely be removed from the interfaces package, provided that they are no longer needed.  Then, it would be possible for a plugin author to remove support for older versions and modify existing interfaces (vs. add new interfaces) in one step.  This remains true to the spirit of earlier guidance, that the interfaces package should preserve interfaces for all _supported_ plugin versions.  Enabling plugin authors to drop support for older versions of a plugin is out of scope for this specification.

### Plugins

A Sign CLI signature provider plugin:

* extends Sign CLI functionality
* contains a [`plugin.json`](#plugin-json-file) file in its root directory
* contains implementations for plugin interfaces defined in the interfaces package
* internalizes all necessary dependencies, both direct and indirect, not provided by the .NET runtime or the plugin host (Sign CLI)

#### Creating a plugin

1. Create a .NET class library project that targets the same runtime as Sign CLI.
2. Add a package reference to the latest version of the interfaces package.  In the plugin's project file, update the package reference to have `PrivateAssets="all"` and `ExcludeAssets="runtime"` to exclude the interfaces package dependency and its runtime assets from the plugin's package.

   ```XML
   <ItemGroup>
       <PackageReference Include="Sign.Plugins.SignatureProvider.Interfaces" Version="1.0.0" PrivateAssets="all" ExcludeAssets="runtime" />
   </ItemGroup>
   ```

3. Add all other necessary package references.  In the plugin's project file, update all package reference to have `PrivateAssets="all"` to exclude package dependencies from the plugin's package.  Example:

   ```XML
   <ItemGroup>
       <PackageReference Include="Azure.Identity" Version="1.8.2" PrivateAssets="all" />
       <PackageReference Include="Azure.Security.KeyVault.Certificates" Version="4.4.0" PrivateAssets="all" />
       <PackageReference Include="Azure.Security.KeyVault.Keys" Version="4.2.0" PrivateAssets="all" />
       <PackageReference Include="RSAKeyVaultProvider" Version="2.1.1" PrivateAssets="all" />
   </ItemGroup>
   ```

4. Add public implementations for relevant interfaces defined in the interfaces package.
5. Add a [`plugin.json`](#plugin-json-file) file to the project.
6. Update the plugin's project file to create a NuGet package.

   ```XML
   <PropertyGroup>
       <!-- Enable NuGet package creation. -->
       <IsPackable>true</IsPackable>

       <!--
       Be sure to add other mandatory and optional properties for creating a NuGet package.
       See https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets#pack-target.
       -->

       <!-- Enable rolling forward to a later runtime. -->
       <!-- See https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#rollforward -->
       <RollForward>LatestMajor</RollForward>

       <!-- Copy runtime dependencies to the build output directory. -->
       <!-- https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#enabledynamicloading -->
       <EnableDynamicLoading>true</EnableDynamicLoading>

       <!-- All private dependencies must be internalized.  Enable Sign CLI to load private dependencies using the plugin's deps.json file. -->
       <GenerateDependencyFile>true</GenerateDependencyFile>
       <TargetsForTfmSpecificBuildOutput>$(TargetsForTfmSpecificBuildOutput);CopyProjectReferencesToPackage</TargetsForTfmSpecificBuildOutput>
   </PropertyGroup>
   
   <ItemGroup>
       <!-- This adds plugin.json to the root directory of the plugin's package. -->
       <Content Include="plugin.json">
           <Pack>true</Pack>
           <!-- An empty value means the root directory. -->
           <PackagePath></PackagePath>
       </Content>
   </ItemGroup>
   
   <!-- This target copies runtime dependencies to the build output directory. -->
   <Target Name="CopyProjectReferencesToPackage" DependsOnTargets="ResolveReferences">
       <ItemGroup>
           <BuildOutputInPackage
               Include="@(ReferenceCopyLocalPaths)"
               TargetPath="%(ReferenceCopyLocalPaths.DestinationSubPath)" />
       </ItemGroup>
   </Target>
   
   <!-- This target adds the generated deps.json file to the build output directory. -->
   <Target Name="AddBuildDependencyFileToBuiltProjectOutputGroupOutput"
           BeforeTargets="BuiltProjectOutputGroup"
           Condition=" '$(GenerateDependencyFile)' == 'true'">
   
       <ItemGroup>
           <BuiltProjectOutputGroupOutput
               Include="$(ProjectDepsFilePath)"
               TargetPath="$(ProjectDepsFileName)"
               FinalOutputPath="$(ProjectDepsFilePath)" />
       </ItemGroup>
   </Target>
   ```

#### <a name="plugin-json-file"></a>The `plugin.json` file

Sign CLI needs to load and execute plugins.  The general problem is that Sign CLI needs to know which assemblies in a plugin to load, which types to instantiate, how to initialize those objects, and so forth.  To simplify matters, plugins will embed this information in a `plugin.json` JSON file in their package's root directory.  The file should include the following properties:

* `name`: The plugin's command name (e.g.:  `azure-key-vault` in `sign code azure-key-vault`).
* `description`: The plugin's command descripton, to be displayed in command help.
* `entryPoint`: Information for plugin instantiation.
* `filePath`: The full file path within the package, relative to the package's root directory, for the assembly which contains the public implementation of an interface defined in the interfaces package.
    * The path must be in its simplest form, without `..` or `.` directories.
    * The directory separator must be `/`.
    * The path must not have a leading slash `/`.
    * The path must be case-sensitive.
* `implementationTypeName`: The fully qualified type name of the public type implementing the public interface defined in the interfaces package.
* `interfaceTypeName`: The fully qualified type name of the public type in the interfaces package that `implementationTypeName` implements.
* `parameters`: The plugin's command options.
    * `name`: The option name.  Not displayed.
    * `description`: The option's description.  Displayed in command help.
    * `aliases`: The option's names.  Should include both long and short forms, (e.g.:  `--azure-key-vault-managed-identity` and `-kvm`, respectively).  A user will type these option names.
    * `dataType`: The option's data type.  Lets Sign CLI know how to parse user input.
    * `defaultValue`: The option's default value.  Only used if the effective value for `isRequired` is `false`; otherwise, this is ignored.
    * `isRequired`: Whether the option is required or not.

Parameter | Required | JSON Type | Default | Possible Values
-- | -- | -- | -- | --
`name` | yes | string | N/A | N/A
`description` | yes | string | N/A | N/A
`aliases` | yes | array of strings | N/A | N/A
`dataType` | no | string | `Text` | `Text`, `Boolean`, `Uri`
`defaultValue` | no | string for `Text` and `Uri`, and `true`/`false` for `Boolean` | N/A | N/A
`isRequired` | no | `true`/`false` | `false` | `true`, `false`

Example:

```JSON
{
  "name": "azure-key-vault",
  "description": "Use Azure Key Vault.",
  "entryPoint": {
    "filePath": "lib/net6.0/Microsoft.Azure.KeyVault.Sign.dll",
    "implementationTypeName": "Microsoft.Azure.KeyVault.Sign.SignatureProviderPlugin",
    "interfaceTypeName": "Sign.Plugins.SignatureProvider.Interfaces.ISignatureProviderPlugin"
  },
  "parameters": [
    {
      "name": "certificate-name",
      "description": "Name of the certificate in Azure Key Vault.",
      "aliases": [ "-kvc", "--azure-key-vault-certificate" ],
      "dataType": "Text",
      "isRequired": true
    },
    {
      "name": "client-id",
      "description": "Client ID to authenticate to Azure Key Vault.",
      "aliases": [ "-kvi", "--azure-key-vault-client-id" ],
      "dataType": "Text"
    },
    {
      "name": "client-secret",
      "description": "Client secret to authenticate to Azure Key Vault.",
      "aliases": [ "-kvs", "--azure-key-vault-client-secret" ],
      "dataType": "Text"
    },
    {
      "name": "managed-identity",
      "description": "Managed identity to authenticate to Azure Key Vault.",
      "aliases": [ "-kvm", "--azure-key-vault-managed-identity" ],
      "dataType": "Boolean"
    },
    {
      "name": "tenant-id",
      "description": "Tenant ID to authenticate to Azure Key Vault.",
      "aliases": [ "-kvt", "--azure-key-vault-tenant-id" ],
      "dataType": "Text"
    },
    {
      "name": "url",
      "description": "URL to an Azure Key Vault.",
      "aliases": [ "-kvu", "--azure-key-vault-url" ],
      "dataType": "Uri"
    }
  ]
}
```

This design roughly borrows from [.NET's templating](https://github.com/dotnet/templating/blob/f8aec1818bd9ae82a8849bfe2138e4a76fed1da1/docs/Reference-for-template.json.md#parameter-symbol).

#### Plugin dependencies

Although a plugin will install as a NuGet package, the package should not have any package dependencies.  A plugin package should include all necessary dependencies except what will be provided by the .NET runtime and Sign CLI.  A plugin must not require Sign CLI or the interfaces package to depend on a package or assembly outside of what the .NET runtime already has.  Sign CLI will not resolve runtime dependencies through declared package dependencies.  The motivation here is to simplify Sign CLI's responsibility of loading and executing plugins.  It is the plugin author's responsibility to satisfy all runtime dependencies.

For dependencies in common to both Sign CLI and plugins, Sign CLI should dictate the dependency version, which usually should be the latest release version.  If a plugin depends on a later version than what Sign CLI depends on, the plugin will fail to load.

#### Plugin installation location

By default, plugin packages will install to the directory indicated by [`Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)`](https://learn.microsoft.com/en-us/dotnet/api/system.environment.specialfolder?view=net-7.0#fields).

Example (where `%APPDATA%` is `C:\Users\dtivel\AppData\Roaming`):  `C:\Users\dtivel\AppData\Roaming\Sign\Plugins\SignatureProviders`

This default location could be overridden with an environment variable or CLI option.

The directory structure for the `SignatureProviders` directory will contain one subdirectory for each lower-cased plugin package ID.  Each plugin package ID directory will contain a subdirectory for each lower-cased plugin package version.  Each version subdirectory will contain the extracted contents of the corresponding plugin package.  Example:

```
SignatureProviders
├─microsoft.azure.keyvault.sign
│ ├─0.9.1-beta.23274.1
│ │ └─<package contents>
│ └─0.9.1-beta.23356.1
│   └─<package contents>
└─<another plugin package ID>
  └─<package version>
    └─<package contents>
```

To identify installed plugins, Sign CLI will simply look in this directory for packages and use the latest version (release or prerelease).

Sign CLI will not maintain any installation state.  How a plugin was installed --- using NuGet client tools, Sign CLI, manual package extraction, or some other method --- is immaterial.  The presence of an extracted package in the directory means it has been installed.

#### Plugin instantiation

A plugin's command in Sign CLI will:

1. Read a plugin's `plugin.json` file.
2. Load the assembly at `entryPoint.filePath` location.
3. Create an instance of the type `implementationTypeName`.
4. Cast the instance to an interface defined in the interfaces package with type name `interfaceTypeName`.

As part of this process, Sign CLI will use [`System.Runtime.Loader.AssemblyLoadContext`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext?view=net-7.0) and [`System.Runtime.Loader.AssemblyDependencyResolver`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblydependencyresolver?view=net-7.0) to load a plugin assembly and its dependencies strictly from the directory cone of the plugin's entry point assembly.  For example, if the plugin's entry point is `lib/net6.0/Microsoft.Azure.KeyVault.Sign.dll`, then Sign CLI will attempt to resolve assemblies under `lib/net6.0`.

An plugin author may define more than one interface in the interfaces package.  The above steps only describe how a plugin's entry point is loaded and executed.  How other interfaces are used is up to the plugin author.

Sign CLI should log:

* high-level plugin loading information at the [`Information`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.loglevel?view=dotnet-plat-ext-7.0#fields) log level.
* assembly loading details at `Debug` and `Trace` log levels
* errors at the `Error` log level

### Sign CLI commands

New commands will be added to Sign CLI to manage plugins.

#### `sign plugin <install>`

The `sign plugin` command will expose subcommands for managing plugins.

##### `sign plugin install <PluginPackageId> [--version <Version>]`

This command will install the latest release version of the plugin package identified by `PluginPackageId` using existing package sources and NuGet's default NuGet.config lookup order.  If the latest version is already installed, the command will no-op.

Example:

```
sign plugin install Microsoft.Azure.KeyVault.Sign
```

Specifying `--version <Version>` will enable installation of a specific version.  If the specified version is already installed, the command will no-op.

Example:

```
sign plugin install Microsoft.Azure.KeyVault.Sign --version 1.0.0
```

## Considerations

1. Sign CLI should probably move to using a [lock file](https://devblogs.microsoft.com/nuget/enable-repeatable-package-restores-using-a-lock-file/) to increase transparency in dependency versions and to ensure deterministic builds.

2. Because a plugin package isolates all private dependencies from Sign CLI, a plugin package author is responsible for servicing the plugin package with updates for the plugin and any of its dependencies.

3. Currently, Sign CLI depends on the [`NuGetKeyVaultSignTool`](https://www.nuget.org/packages/NuGetKeyVaultSignTool.Core/3.2.3) package for signing NuGet packages with Azure Key Vault.  Under this proposed specification, Sign CLI must be cloud-provider agnostic.  This dependency should simply take any `RSA` implementation and remove [`RSAKeyVaultProvider`](https://www.nuget.org/packages/RSAKeyVaultProvider/2.1.1) and [`Azure.Security.KeyVault.Certificates`](https://www.nuget.org/packages/Azure.Security.KeyVault.Certificates/4.2.0) package dependencies.

## Q & A

1. How would Sign CLI move from .NET 6.0 runtime to .NET 8.0 (or .NET 10.0)?  Does specifying `<RollForward>LatestMajor</RollForward>` in a plugin suffice?  Is any coordination required with plugin authors?  There's probably some investigation work here.
 
2. Although NuGet CLI's do not respect the `requireLicenseAcceptance` property, should Sign CLI require license acceptance before installing/updating a plugin package?  See the following issues for more context:

   * NuGet:  [Deprecate <requireLicenseAcceptance> from nuspec and VS](https://github.com/NuGet/Home/issues/7439)
   * NuGet:  [Nuget.exe install does not honor requireLicenseAcceptance](https://github.com/NuGet/Home/issues/8299)
   * PowerShellGetv2:  [Changes to support require license acceptance flag](https://github.com/PowerShell/PowerShellGetv2/pull/150)

3. Should we create a JSON schema for `plugin.json`?

4. How do we enable plugin localization?  See [.NET template localization](https://github.com/dotnet/templating/blob/f5fef556632723ecf1387ef1498aa55f54299fba/docs/authoring-tools/Localization.md) for prior art.

## References

<a name="r1"></a>1. ["Baseline Requirements for the Issuance and Management of Publicly‐Trusted Code Signing Certificates"](https://cabforum.org/wp-content/uploads/Baseline-Requirements-for-the-Issuance-and-Management-of-Code-Signing.v3.3.pdf), section 6.2.7.4 (version 3.3.0, June 29, 2023)
