# Authenticode Signing Service and Client

This project aims to make it easier to integrate Authenticode signing into a CI process by providing a secured API
for submitting artifacts to be signed by a code signing cert held on the server. It uses Azure AD and Azure Key Vault's HSM for security.

## Architecture and Security

There are a few pieces to the security model:

1. One app registration for the service itself
2. One app registration to for the sign client
3. Service account users in AD
4. Key Vault HSM's, one for each service account user

![Architecture Diagram](docs/images/SigningServiceArchitecture.png?raw=true)

The system is designed to support multiple certificates belonging to different users. Many organizations may only have a single certificate, and thus will have a single user service account and a single vault. You may have multiple service accounts configured to the same vault as well for granular auditing.

Certificates are stored in a Key Vault. Due to the way Key Vault's security works - you can control access to the vault as a whole, not per certificate - every user service account gets its own vault. There are no charges per vault; only per certificate.

There is an administrator UI to create sign service user accounts and set the properties. The admin UI also creates the Key Vaults and configures the permissions appropriately.

The Sign Service requires user accounts to be used as service accounts. This is due to a current limitation of Azure AD that doesn't allow a Service Principal to Service Principal On-Behalf-Of flow. That flow is part of the defense-in-depth, preventing the signing service from having direct access to the signing functions on its own. The service can only access the signing method on-behalf-of the user at the time of request.

The service currently supports either individual files, or a zip archive that contains supported files to sign (works well for NuGet packages). The service code is easy to extend if additional filters or functionality is required.

## Supported File Types

- `.msi`, `.msp`, `.msm`, `.cab`, `.dll`, `.exe`, `.appx`, `.appxbundle`, `.sys`, `.vxd`, `.ps1`, `.psm1`, and Any PE file (via [AzureSignTool](https://github.com/vcsjones/AzureSignTool))
- `.vsix` via [OpenOpcSignTool](https://github.com/vcsjones/OpenOpcSignTool)
- ClickOnce `.application` and `.vsto` (via `Mage`). Special instructions below.
- `.nupkg` via [NuGetKeyVaultSignTool](https://github.com/onovotny/NuGetKeyVaultSignTool)

## Installation and Administration

Documentation is here:

- [Deployment](docs/Deployment.md)
- [Administration](docs/Administration.md)

## Client Configuration

The client is distributed via [NuGet](https://www.nuget.org/packages/SignClient) and uses both a json config file and command line parameters. Common settings, like the client id and service url are stored in a config file, while per-file parameters and the client secret are passed in on the command line.

You'll need to create an `appsettings.json` similar to the following:

```json
{
  "SignClient": {
    "AzureAd": {
      "AADInstance": "https://login.microsoftonline.com/",
      "ClientId": "<client id your sign client app>",
      "TenantId": "<guid or domain name>"
    },
    "Service": {
      "Url": "https://<your-service>/",
      "ResourceId": "<app id uri of your service>"
    }
  }
}
```

Then, somewhere in your build, you'll need to call the client tool. I use AppVeyor and have the following in my yml:

```yml
environment:
  SignClientSecret:
    secure: <the encrypted client secret using the appveyor secret encryption tool>
  SignClientUser:
    secure: <the encrypted username using the appveyor secret encryption tool>

install: 
  - cmd: nuget install SignClient -Version 0.9.0 -SolutionDir %APPVEYOR_BUILD_FOLDER% -Verbosity quiet -ExcludeVersion

build: 
 ...

after_build:
  - cmd: msbuild Zeroconf\Zeroconf.csproj /t:pack /p:Configuration=Release /p:PackageOutputPath=%APPVEYOR_BUILD_FOLDER%
  - ps: '.\SignClient\Sign-Package.ps1'

```

Sign-Package.ps1 looks like this:

```powershell
$currentDirectory = split-path $MyInvocation.MyCommand.Definition

# See if we have the ClientSecret available
if([string]::IsNullOrEmpty($env:SignClientSecret)){
	Write-Host "Client Secret not found, not signing packages"
	return;
}

# Setup Variables we need to pass into the sign client tool

$appSettings = "$currentDirectory\appsettings.json"

$appPath = "$currentDirectory\..\packages\SignClient\tools\SignClient.dll"

$nupgks = ls $currentDirectory\..\*.nupkg | Select -ExpandProperty FullName

foreach ($nupkg in $nupgks){
	Write-Host "Submitting $nupkg for signing"

	dotnet $appPath 'sign' -c $appSettings -i $nupkg -r $env:SignClientUser -s $env:SignClientSecret -n 'Zeroconf' -d 'Zeroconf' -u 'https://github.com/onovotny/zeroconf'

	Write-Host "Finished signing $nupkg"
}

Write-Host "Sign-package complete"
```

The parameters to the signing client are as follows:

```
usage: SignClient <command> [<args>]

    sign     Sign a file
```

signing an archive type (`.zip`, `.nupkg`, `.vsix`) will open up the archive and sign any 
supported file types. It is strongly recommended to use the `filter` parameter to explicitly 
list the files inside the archive that should be signed. Signing is recursive; it will sign 
contents of any detectected `Zip`, `NuPkg` or `VSIX` files inside the uploaded one. 
After signing contents of the archive, the archive itself is signed if supported 
(currently `VSIX`).

```
usage: SignClient sign [-c <arg>] [-i <arg>] [-o <arg>] [-h <arg>]
                  [-f <arg>] [-s <arg>] [-n <arg>] [-d <arg>] [-u <arg>]

    -c, --config <arg>            Path to config json file
    -i, --input <arg>             Path to input file
    -o, --output <arg>            Path to output file. May be same
                                  as input to overwrite
    -h, --hashmode <arg>          Hash mode: Sha256.
    -f, --filter <arg>            Path to file containing paths of
                                  files to sign within an archive
    -s, --secret <arg>            Client Secret
    -n, --name <arg>              Name of project for tracking
    -d, --description <arg>       Description
    -u, --descriptionUrl <arg>    Description Url
```

## ClickOnce
ClickOnce files can be signed with this tool, but it requires an extra step -- you must zip up the `publish`
directory containing the `setup.exe`, `foo.application` or `foo.vsto` files along with the `Application Files` directory.
The `Application Files` must only have a single subdirectory (version you want to sign). Zip these and then rename the
extension to `.clickonce` before submitting to the tool. Once done, you can extract the signed files wherever you'd like
for publication. If the `name` parameter is supplied, it's used in the `Mage` name to update the `Product` in the manifests.
If the `descriptionUrl` parameter is supplied, it's used as the `supportUrl` in the manifests.

You should also use the `filter` parameter with the file list to sign, something like this:
```
**/ProjectAddIn1.*
**/setup.exe
```

## Tips

To get certificates into Key Vault, there are several options:

1. Use the Admin UI to create a CSR and then merge the certificate. 
2. Upload a pfx file using the Portal, CLI, or PowerShell
3. Use this GUI tool: https://github.com/elize1979/AzureKeyVaultExplorer. Before you can login, you'll need to go to the settings, put your tenant name in and change the login endpoint to `https://login.microsoftonline.com/common`. The tool makes it easy to upload and manage certificates in Key Vault.

# Contributing

I'm very much open to any collaboration and contributions to this tool to enable additional scenarios. Pull requests are welcome, though please open an [issue](https://github.com/onovotny/SignService/issues) to discuss first. Security reviews are also much appreciated! 
