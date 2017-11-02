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

## 5. Build and publish

There's a few parts to this, and I strongly recommend using Azure Key Vault to hold all of the settings you need for your service. 

Follow the steps in my [blog](https://oren.codes/2017/10/18/continuous-deployment-of-cloud-services-with-vsts/) to create a CI/CD pipeline for the cloud service. Create a key vault to hold the configuration values and link them into VSTS as a variable group.

You should have the following secrets with the appropriate values in the Vault:

- AccountEncryptedPassword
- AccountUsername
- Admin-Location
- Admin-ResourceGroup
- Admin-SubscriptionId
- AzureAd-AADInstance
- AzureAd-ApplicationObjectId
- AzureAd-Audience
- AzureAd-ClientId
- AzureAd-ClientSecret
- AzureAd-Domain
- AzureAd-TenantId
- DiagnosticConnectionString
- PasswordEncryption
- SslCertificateSha1

Most of these values map to the `appsettings.json` values. `AccountUsername` comes from your Cloud Service config. `PasswordEncryption` is the thumbprint of the certificate used by the tools to encrypt the password. `AccountEncryptedPassword` is the encrypted password value. `SslCertificateSha1` is the thumbprint of your configured SSL certificate. One way to get these values is to publish to cloud services using the `Local` configuration, setting the values in the UI and then copying them into Key Vault.

Once you have those variables in Key Vault and linked into a Variable Group in VSTS, add that variable group to your Release Management definition to make them available there. The tokenize step, as described in my blog, will take put those values in the `cscfg` before it's deployed in the final step.

## 6. First time login

When you first configure the application using the `InstallUtility`, only the user who ran the utility is an admin of the Signing Service. To add additional administrators, go to the AAD Portal, then to Enterprise Applications, find the `SignService - Server` application, then under `Users and Groups`, you can add a role assignment to any additional users who need admin access.

Then you can proceed to the portal by navigating to the root URL in a browser and following the steps in the [admin guide](Administration.md) to 
create a sign service user account, specify the configuration settings, and create/upload a certificate.

## 7. Sign Client configuration

You'll need to provide the client configuration to your users. There are two parts to the configuration: 

1. Public part, the `appsettings.json` file that the Sign Client requires. You'll need the `ClientId`, `TenantId`, Service Url, and `ResourceId`. An example of this is in the main readme.
2. Secret part. The username and password should be treated securely as secrets. AppVeyor allows you to encrypt certain settings; VSTS has secret variables and other build systems have something similar. Technically the username isn't really a secret, but there's no reason to provide any additional information to give an attacker a head-start, so best keep that secret too.

# VM configuration

Use IIS on Server 2016. Under the App Pool advanced settings, Set the App Pool CLR version to `No Managed Code` and "Load User Profile" to `true`. Edit your `appsettings.json` accordingly as per the above table. You can put the `appsettings.json` file in the `~/App_Data` directory and the application will pick it up. You'll need to install the .NET Core as described here: https://docs.microsoft.com/en-us/aspnet/core/publishing/iis.