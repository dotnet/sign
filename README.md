# NOTE: This readme is out-of-date with the server configuration. Updated instructions will be provided shortly. 

# Authenticode Signing Service and Client

This project aims to make it easier to integrate Authenticode signing into a CI process by providing a secured API
for submitting artifacts to be signed by a code signing cert held on the server. It uses Azure AD and Azure Key Vault for security.

## Architecture and Security
There are a few pieces to the security model:

1. One app registration for the service itself
2. One app registration to for the sign client
3. Service account users in AD
4. Key Vault HSM's, one for each service account user

<img src="docs/images/SigningServiceArchitecture.png?raw=true" srcset="docs/images/SigningServiceArchitecture.png?raw=true x2" />

The system is designed to support multiple certificates belonging to different users. Many organizations may only have a single certificate, and thus will have a single user service account and a single vault. You may have multiple service accounts configured to the same vault as well for granular auditing.

Certificates are stored in a Key Vault. Due to the way Key Vault's security works - you can control access to the vault as a whole, not per certificate - every user service account gets its own vault. There are no charges per vault; only per certificate.

There is an administrator UI to create sign service user accounts and set the properties. The admin UI also creates the Key Vaults and configures the permissions appropriately. 

The service currently supports either individual files, or a zip archive that contains supported files to sign (works well for NuGet packages). The service code is easy to extend if additional filters or functionality is required.

## Supported File Types
- `.msi`, `.msp`, `.msm`, `.cab`, `.dll`, `.exe`, `.appx`, `.appxbundle`, `.sys`, `.vxd`, `.ps1`, `.psm1`, and Any PE file (via [AzureSignTool](https://github.com/vcsjones/AzureSignTool))
- `.vsix` via [OpenOpcSignTool](https://github.com/vcsjones/OpenOpcSignTool)
- ClickOnce `.application` and `.vsto` (via `Mage`). Special instructions below.

# Deployment

This service must run on Windows Server 2016 due to dependencies on new APIs for signing. It may be deployed to either a Virtual Machine or it can use an Azure Cloud Service Web Role for a PaaS offering (recommended).

You will need an Azure AD tenant and an Azure subscription. For easiest deployment, it's easiest if you are a global admin on your AAD tenant, but you may opt to have a global admin consent to the applications separately as well. Admin consent is ultimately required, however.

While you can create the required entries manually, it's far easier to run the provided `InstallUtility` application.

## Overview

1. Clone this repo
2. Build & Run `InstallUtility`
3. Configure DNS for your service, get an SSL certificate
4. Update `ReplyUrl` in the `SignService` application to point to your hostname
5. Build and publish service to Azure with appropriate config values.
6. Login to SignService admin UI, create user account, upload cert or create CSR.
7. Provide sign client configuration to your users


## 1. Clone Repo

`git clone https://github.com/onovotny/SignService`

## 2. Build & Run `InstallUtility`

The `InstallUtility` automates the creation of resources the application needs:

- Application for SignService
- Application for SignClient
- Extension Attributes for storing per-user configuration
- OAuth permissions between services, Graph, and Key Vault
- Azure Resource Group for holding the Key Vaults
- Granting Admin Consent for the required permissions

The application entries will be created in the directory as `SignService Server` and `SignService Client - {service appId}`. You may install multiple SignService environments in a directory. If you choose, you can add a suffix, so they appear as `SignService Server (PROD)` or `SignService Server (TEST)`, etc. The `InstallUtility` takes an optional single argument, like `PROD` or `TEST` if you want.

The `InstallUtility` can be run multiple times to add/update configuration, should it change, and also display the settings you need to configure in your applications. Note that the `clientSecret` is only displayed the first time since the value cannot be retrieved. You can always create additional ones in the Azure Portal.

Before you run the utility, have the following information on-hand:

- Azure Subscription Id
- Azure Region (`eastus`, `westus`, etc)

You'll need those during the installer steps.

1. Open `SignService.sln` in VS and build the solution.
2. Set `InstallUtility` as the startup project. If you want a per-environment application name, specify it in the debug arguments.
3. Run the `InstallUtility` and follow the prompts. When prompted, enter your subscription id, resource group name, and desired region. 
4. When prompted, allow the admin consent if you're a global admin in the AAD tenant. If you're not, you can have a global admin re-run the tool or manually hit the "Grant Permissions" button in the Azure Portal. Admin Consent is required before you can use the application.
5. When the utility completes, it'll print the configuration values you'll need later. Copy these and set them aside for later.

## 3. Configure DNS for your service, get an SSL certificate

You'll need a DNS name and matching SSL certificate. Something like `codesign.yourorganization.com`. If you plan on using Azure Cloud Service Web Roles, which is the recommended option as it's PaaS, create a Cloud Service Web Role in order to get the correct CNAME values for DNS. You'll also need to upload the SSL certificate to the Cloud Service configuration. Take note of the SSL Certificate's `SHA-1 Thumbprint` as you'll need it later.

## 4. Update `ReplyUrl`

In the Azure Portal, navigate to your `SignService Server` application, and add a `ReplyUrl` entry with your hostname, such as `https://codesign.yourorganization.com/signin-oidc`

## Build and publish

There's a few parts to this, and I strongly recommend using Azure Key Vault to hold all of the settings you need for your service. 

Follow the steps in my [blog](https://oren.codes/2017/10/18/continuous-deployment-of-cloud-services-with-vsts/) to create a CI/CD pipeline for the cloud service. Create a key vault

## Azure AD Configuration
### Server
Create a new application entry for a web/api application. Use whatever you want for the sign-on URI and App ID Uri (but remember what you use for the App ID Uri as you'll need it later). On the application properties, edit the manifest to add an application role. 

![](docs/images/service-manifest.png?raw=true)


In the `appRoles` element, add something like the following:

```json
{
  "allowedMemberTypes": [
    "Application"
  ],
  "displayName": "Code Sign App",
  "id": "<insert guid here>",
  "isEnabled": true,
  "description": "Application that can sign code",
  "value": "application_access"
}
```

After updating the manifest, you'll likely want to edit the application configuration to enable "user assignment." This means that only assigned users and applications can get an access token to/for this service. Otherwise, anyone who can authenticate in your directory can call the service.
![](docs/images/user-assignment.png?raw=true)


### Client
Create a new application entry to represent your client application. The client will use the "client credentials" flow to login to Azure AD
and access the service as itself. For the application type, also choose "web/api" and use anything you want for the app id and sign in url.

Under application access, click "Add application" and browse for your service (you might need to hit the circled check to show all). Choose your service app and select the application permission.
![](docs/images/client-permissions-1.png?raw=true)
![](docs/images/client-permissions-2.png?raw=true)
![](docs/images/client-permissions-3.png?raw=true)
![](docs/images/client-permissions-4.png?raw=true)

Finally, create a new client secret and save the value for later (along with the client id of your app).

## Server Configuration

### Note: As of right now, Azure App Service does not support this service since its OS is too old. This service must run on a Server 2016 VM.
Create a new App Service on Azure (I used a B1 for this as it's not high-load). Build/deploy the service however you see fit. I used VSTS connected to this GitHub repo along with a Release Management build to auto-deploy to my site.

In the Azure App Service, upload your code signing certificate and take note of the thumbprint id. In the Azure App Service,
go to the settings section and add the following setting entries:


| Name | Value | Notes |
|-------|------| ------ |
| CertificateInfo:KeyVaultUrl | *url of the key Vault* | The URL to the Key Vault, e.g., *https://my-vault.vault.azure.net* |
| CertificateInfo:KeyVaultCertificateName | *Certificate Name in Key Vault* | The name of the certificate as stored in Key Vault |
| CertificateInfo:Thumbprint | *thumbprint of your cert* | Thumbprint of the cert to sign with. Only used for ClickOnce  |
| CertificateInfo:TimeStampUrl | *url of timestamp server* | 
| WEBSITE_LOAD_CERTIFICATES | *thumbprint of your cert* | This exposes the cert's private key to your app in the user store. Only used for ClickOnce |
| Authentication:AzureAd:Audience | *App ID URI of your service from the application entry* |
| Authentication:AzureAd:ClientId | *client id of your service app from the application entry* |
| Authentication:AzureAd:TenantId | *Azure AD tenant ID* | either the guid or the name like *mydirectory.onmicrosoft.com* |

Enable "always on" if you'd like and disable PHP then save changes. Your service should now be configured.

### VM configuration
Use IIS on Server 2016. Under the App Pool advanced settings, Set the App Pool CLR version to `No Managed Code` and "Load User Profile" to `true`. Edit your `appsettings.json` accordingly as per the above table. You'll need to install the .NET Core as described here: https://docs.microsoft.com/en-us/aspnet/core/publishing/iis.

### Key Vault Configuation. 
You need an Azure Key Vault instance. Standard enables software encryption and Premium is backed in a hardware HSM. Premium is recommended (and required if you need to store EV certificates). Make sure to grant your Sign Service app principal access with the following permission:

| Category | Permission |
| ----- | ---- |
| Key | Get, Sign, Decrypt |
| Certificate | Get |

To get certificates into Key Vault, there are several options:
1. Use the CLI/PowerShell to create a CSR and then merge the certificate. When creating the CSR, you can use anything as the subject name since the CA will ignore it. If you're creating an EV certificate request, specify `keyType` as `RSA-HSM` to ensure the key stays in the hardware.
2. Upload a pfx file using the CLI/PowerShell
3. Use this GUI tool: https://github.com/elize1979/AzureKeyVaultExplorer. Before you can login, you'll need to go to the settings, put your tenant name in and change the login endpoint to `https://login.microsoftonline.com/common`. The tool makes it easy to upload and manage certificates in Key Vault.

## Client Configuration
The client is distributed via [NuGet](https://www.nuget.org/packages/SignClient) and uses both a json config file and command line parameters. Common settings, like the client id and service url are stored in a config file, while per-file parameters and the client secret are passed in on the command line.

You'll need to create an `appsettings.json` similar to the following:

```json
{
  "SignClient": {
    "AzureAd": {
      "AADInstance": "https://login.microsoftonline.com/",
      "ClientId": "<client id of your client app entry>",
      "TenantId": "<guid or domain name>"
    },
    "Service": {
      "Url": "https://<your-service>.azurewebsites.net/",
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

install: 
  - cmd: appveyor DownloadFile https://dist.nuget.org/win-x86-commandline/v4.1.0/NuGet.exe
  - cmd: nuget install SignClient -Version 0.7.0 -SolutionDir %APPVEYOR_BUILD_FOLDER% -Verbosity quiet -ExcludeVersion

build: 
 ...

after_build:
  - cmd: nuget pack nuget\Zeroconf.nuspec -version "%GitVersion_NuGetVersion%-bld%GitVersion_BuildMetaDataPadded%" -prop "target=%CONFIGURATION%" -NoPackageAnalysis
  - ps: '.\SignClient\Sign-Package.ps1'
  - cmd: appveyor PushArtifact "Zeroconf.%GitVersion_NuGetVersion%-bld%GitVersion_BuildMetaDataPadded%.nupkg"  

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

	dotnet $appPath 'sign' -c $appSettings -i $nupkg -s $env:SignClientSecret -n 'Zeroconf' -d 'Zeroconf' -u 'https://github.com/onovotny/zeroconf' 

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

    -c, --config <arg>            Full path to config json file
    -i, --input <arg>             Full path to input file
    -o, --output <arg>            Full path to output file. May be same
                                  as input to overwrite
    -h, --hashmode <arg>          Hash mode: either dual or Sha256.
                                  Default is dual, to sign with both
                                  Sha-1 and Sha-256 for files that
                                  support it. For files that don't
                                  support dual, Sha-256 is used
    -f, --filter <arg>            Full path to file containing paths of
                                  files to sign within an archive
    -s, --secret <arg>            Client Secret
    -n, --name <arg>              Name of project for tracking
    -d, --description <arg>       Description
    -u, --descriptionUrl <arg>    Description Url
```

## ClickOnce
ClickOnce files can be signed with this tool, but it requires an extra step -- you must zip up the `publish`
directory containing the `setup.exe`, `foo.application` or `foo.vsto` files alongwith the `Application Files` directory.
The `Application Files` must only have a single subdirectory (version you want to sign). Zip these and then rename the
extension to `.clickonce` before submitting to the tool. Once done, you can extract the signed files wherever you'd like
for publication. If the `name` parameter is supplied, it's used in the `Mage` name to update the `Product` in the manifests.
If the `descriptionUrl` parameter is supplied, it's used as the `supportUrl` in the manifests.

You should also use the `filter` parameter with the file list to sign, something like this:
```
ProjectAddIn1.dll.deploy
ProjectAddIn1.dll.manifest
ProjectAddIn1.vsto
setup.exe
```
or
```
MyApp1.exe.deploy
MyApp1.exe.manifest
MyApp1.application
setup.exe
```

# Contributing
I'm very much open to any collaboration and contributions to this tool to enable additional scenarios. Pull requests are welcome, though please open an [issue](https://github.com/onovotny/SignService/issues) to discuss first. Security reviews are also much appreciated! 
