# Authenticode Signing Service and Client

This project aims to make it easier to integrate Authenticode signing into a CI process by providing a secured API
for submitting artifacts to be signed by a code signing cert held on the server. It uses Azure AD with two application
entries for security:

1. One registration for the service itself
2. One registration to represent each code signing client you want to allow

Azure AD was chosen as it makes it easy to restrict access to a single application/user in a secure way. Azure App Services 
also provide a secure location to store certificates, so the combination works well.

The service currently supports either individual files, or a zip archive
that contains supported files to sign (works well for NuGet packages). The service code is easy to extend if
additional filters or functionality is required.

## Supported File Types
- `.msi`, `.msp`, `.msm`, `.cab`, `.dll`, `.exe`, `.sys`, `.vxd` and Any PE file (via `SignTool`)
- `.ps1` and `.psm1` via `Set-AuthenticodeSignature`


# Deployment

You will need an Azure AD tenant. These are free if you don't already have one. In the "old" Azure Portal, you'll need to
create two application entries: one for the server and one for your client.
![](docs/images/app-entries.png?raw=true)

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
Create a new App Service on Azure (I used a B1 for this as it's not high-load). Build/deploy the service however you see fit. I used VSTS connected to this GitHub repo along with a Release Management build to auto-deploy to my site.

In the Azure App Service, upload your code signing certificate and take note of the thumbprint id. In the Azure App Service,
go to the settings section and add the following setting entries:


| Name | Value | Notes |
|-------|------| ------ |
| CertificateInfo:Thumbprint | *thumbprint of your cert* | Thumbprint of the cert to sign with |
| CertificateInfo:TimeStampUrl | *url of timestamp server* | 
| WEBSITE_LOAD_CERTIFICATES | *thumbprint of your cert* | This exposes the cert's private key to your app in the user store |
| Authentication:AzureAd:Audience | *App ID URI of your service from the application entry* |
| Authentication:AzureAd:ClientId | *client id of your service app from the application entry* |
| Authentication:AzureAd:TenantId | *Azure AD tenant ID* | either the guid or the name like *mydirectory.onmicrosoft.com* |

Enable "always on" if you'd like and disable PHP then save changes. Your service should now be configured.

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
  - cmd: appveyor DownloadFile https://dist.nuget.org/win-x86-commandline/v3.5.0-rc1/NuGet.exe
  - cmd: nuget install SignClient -Version 0.5.0-beta3 -SolutionDir %APPVEYOR_BUILD_FOLDER% -Verbosity quiet -ExcludeVersion -pre

build: 
 ...

after_build:
  - cmd: nuget pack nuget\Zeroconf.nuspec -version "%GitVersion_NuGetVersion%-bld%GitVersion_BuildMetaDataPadded%" -prop "target=%CONFIGURATION%" -NoPackageAnalysis
  - ps: '.\SignClient\SignPackage.ps1'
  - cmd: appveyor PushArtifact "Zeroconf.%GitVersion_NuGetVersion%-bld%GitVersion_BuildMetaDataPadded%.nupkg"  

```

SignPackage.ps1 looks like this:

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

	dotnet $appPath 'zip' -c $appSettings -i $nupkg -s $env:SignClientSecret -n 'Zeroconf' -d 'Zeroconf' -u 'https://github.com/onovotny/zeroconf' 

	Write-Host "Finished signing $nupkg"
}

Write-Host "Sign-package complete"
```

The parameters to the signing client are as follows. There are two modes, `file` for a single file and `zip` for a zip-type archive:

```
usage: SignClient <command> [<args>]

    file    Single file
    zip     Zip-type file (NuGet, etc)
```

File mode:
```
usage: SignClient file [-c <arg>] [-i <arg>] [-o <arg>] [-h <arg>]
                  [-s <arg>] [-n <arg>] [-d <arg>] [-u <arg>]

    -c, --config <arg>            Full path to config json file
    -i, --input <arg>             Full path to input file
    -o, --output <arg>            Full path to output file. May be same
                                  as input to overwrite. Defaults to
                                  input file if ommited
    -h, --hashmode <arg>          Hash mode: either dual or Sha256.
                                  Default is dual, to sign with both
                                  Sha-1 and Sha-256 for files that
                                  support it. For files that don't
                                  support dual, Sha-256 is used
    -s, --secret <arg>            Client Secret
    -n, --name <arg>              Name of project for tracking
    -d, --description <arg>       Description
    -u, --descriptionUrl <arg>    Description Url
```

Zip-type archive mode, including NuGet:
```
usage: SignClient zip [-c <arg>] [-i <arg>] [-o <arg>] [-h <arg>]
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

# Contributing
I'm very much open to any collaboration and contributions to this tool to enable additional scenarios. Pull requests are welcome, though please open an [issue](https://github.com/onovotny/SignService/issues) to discuss first. Security reviews are also much appreciated! 
