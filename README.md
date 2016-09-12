# Authenticode Signing Service and Client

This project aims to make it easier to integrate Authenticode signing into a CI process by providing a secured API
for submitting artifacts to be signed by a code signing cert held on the server. It uses Azure AD with two application
entries for security:

1. One registration for the service itself
2. One registration to represent each code signing client you want to allow

Azure AD was chosen as it makes it easy to restrict access to a single application/user in a secure way. Azure App Services 
also provide a secure location to store certificates, so the combination works well.

The service currently supports either individual files, anything that can be signed with `signtool` or a zip archive
that contains `.dll` and `.exe` files to sign (works well for NuGet packages). The service code is easy to extend if
additional filters or functionality is required.


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
Create a new App Service on Azure (I used a B1 for this as it's not high-load). Build/deploy the service however you see fit. I used VSTS
connected to this GitHub repo along with a Release Management to auto-deploy to my site.

In the Azure App Service, upload your code signing certificate and take note of the thumbprint id. In the Azure App Service,
go to the settings section and add the following setting entries:


| Name | Value | Notes |
|-------|------| ------ |
| CertificateInfo:Thumbprint | *thumbprint of your cert* | Thumbprint of the cert to sign with |
| CertificateInfo:TimeStampUrl | *url of timestamp server* | 
| WEBSITE_LOAD_CERTIFICATES | *thumbprint of your cert* | This exposes the cert's private key to your app in the user store |
| ApplicationInsights:InstrumentationKey | *guid of your app insights instance* | monitoring |
| Authentication:AzureAd:Audience | *App ID URI of your service from the application entry* |
| Authentication:AzureAd:ClientId | *client id of your service app from the application entry* |
| Authentication:AzureAd:TenantId | *Azure AD tenant ID* | either the guid or the name like *mydirectory.onmicrosoft.com* |

Enable "always on" if you'd like and disable PHP then save changes. Your service should now be configured.

## Client Configuration
The client is uses both a json config file and command line parameters. Common settings, like the client id and service url are stored in a config file, while per-file parameters and the client secret are passed in on the command line.

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
  - cmd: nuget install SignClient -Version 0.5.0-beta2 -SolutionDir %APPVEYOR_BUILD_FOLDER% -Verbosity quiet -ExcludeVersion -pre

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

	dotnet $appPath $appSettings $nupkg $nupkg 'zip' $env:SignClientSecret 'Zeroconf' 'https://github.com/onovotny/zeroconf' 

	Write-Host "Finished signing $nupkg"
}

Write-Host "Sign-package complete"
```

The parameters to the signing client aren't yet well documented, but the order is as follows:

1. Full path to the `appsettings.json`
2. Full path to the file to sign (input)
3. Full path to the output (can be the same as the input and will overwrite)
4. Type: either `zip` or `file`. `zip` supports any zip type archive of files and will sign all `.dll` and `.exe` files within. `file` supports any single file of any type that can be signed with `signtool`
5. Description passed in to the `/d` switch to `signtool`. This is optional, but required if the destination url is used.
6. Description url passed in to the `/du` switch to `signtool`. This is optional. If you want to use `/du` then `/d` must be passed in before.
