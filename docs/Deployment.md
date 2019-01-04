# Deployment

This service must run on Windows Server 2016+ due to dependencies on new APIs for signing. It may be deployed to either a Virtual Machine or it can use an Azure Website (recommended).

You will need an Azure AD tenant and an Azure subscription. For easiest deployment, it's easiest if you are a global admin on your AAD tenant, but you may opt to have a global admin consent to the applications separately as well. Admin consent is ultimately required, however. The Azure subscription must be tied to the Azure AD tenant you want to use. 

While you can create the required entries manually, it's far easier to run the provided `InstallUtility` application.

## Overview

Below are the minimum steps needed to get this deployed to Azure App services. You may configure custom domains with custom SSL certificates if you'd like.

1. Clone this repo
2. Build & Run `InstallUtility`
3. Create App Service (Web Site) and Key Vault for runtime settings
4. Update `ReplyUrl` in the `SignService` application to point to your hostname
5. Build and publish service to Azure
6. Login to SignService admin UI, create user account, upload cert or create CSR
7. Provide sign client configuration to your users


While not required, it is strongly recommended that a global admin run the `InstallUtility` tool. If a non-admin
user runs the tool, a global admin will still need to perform the following actions to complete configuration:

1. Run the tool again to enable admin consent (or go to the Azure Portal and click "Grant Permissions" on the two apps that were created.
2. In the "Enterprise Applications" area, add application role assignements to themselves and to whomever needs to access the Sign Service Admin UI (step 6 below).
3. Login to the Admin UI, go to the "Adv Setup" tab and click "Register Extension Attributes."

Once that's done, any user granted the "Admin SignService" role can administer the sign service. 

Note: Only global admins can reset passwords if they need to be changed; any user can disable an account if needed.

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

## 3. Create App Service

Azure Web Sites is the easiest way to host this service. You can use the ARM template or create the resources manually.
You only need to complete either **3a** or **3b**.

### 3a. ARM template

The template is located in the `src\ArmDeploy` directory. 

1. The easiest way to invoke the template is to load the solution in 
Visual Studio, right-click the `ArmDeploy` project and click `Deploy`.
2. Visual Studio will prompt for a destination Resource Group. The recommendation is to create a new one for this (and don't use the `KeyVaults-SignService` one configured to hold the resource key vaults).
3. Click `Edit Parameters` and specify the required values. The last four came from the output of the `InstallUtility` for the Service application.
4. Save the values then hit `Deploy`. It will take several minutes to complete.


### 3b. Manual

If you'd like to create the Azure resources manually, here are the steps:

A `B1` or higher instance works for this. The service keeps its runtime configuration and secrets in an Azure Key Vault, so one of those is required as well. A Managed Service Identity secures access from the website to the Key Vault without requiring explicit credentials.

1. Create a new Web Service. Enable Application Insights, if desired, to capture logging/telemetry data.
2. On the website configuration in the portal, [enable its Managed Service Identity](https://docs.microsoft.com/en-us/azure/app-service/app-service-managed-service-identity)
3. On the website [extensions](https://azure.microsoft.com/en-us/blog/azure-web-sites-extensions/), Add/ensure both "Application Insights" and the ".NET Core" extensions are present. Add them if required.
4. Create a new Azure Key Vault (standard is fine since it's just holding secrets). Add an access policy for your managed service identity granting it `Get` and `List` permissions for secrets.
5. Add the following secrets (you may omit ones that match the defaults in the `appsettings.json`, like `AzureAd--AADInstance` for most people). The  ones are most likely:
   - `Admin--ResourceGroup`
   - `Admin--SubscriptionId`
   - `AzureAd--Audience`
   - `AzureAd--ClientId`
   - `AzureAd--ClientSecret`
   - `AzureAd--TenantId`
6. In your website [configuration](https://docs.microsoft.com/en-us/azure/app-service/web-sites-configure), add the URL to the Key Vault as a configuration option `ASPNETCORE_HOSTINGSTARTUP__KEYVAULT__CONFIGURATIONVAULT`. That's where the app will pull its configuration from. Also set `ASPNETCORE_HOSTINGSTARTUP__KEYVAULT__CONFIGURATIONENABLED` to `true`.

## 4. Update `ReplyUrl`

In the Azure Portal, navigate to your `SignService Server` application, and add a `ReplyUrl` entry with your hostname, such as `https://your-website-name.azurewebsites.net/signin-oidc`

## 5. Build and publish

There are many ways to push code to App Services. You can publish directly from Visual Studio, you can use Azure Pipelines to setup a CI/CD pipeline, or you can do it with another set of tools.

### Visual Studio Publish

While generally discouraged for production scenarios, you can use VS to quickly publish to your App Service. Open the solution, right-click the "SignService" project and select `Publish...` and follow the prompts.

### Azure Pipelines

The recommended way to build and publish this service is with Azure Pipelines. It's free for up to five users.

Create a new build definition that points to your git clone. This lets you control updates by pulling from the source at your discretion. Use the YAML builds definition point it to the `azure-pipelines.server.yml` file to create a publish artifact.

Create a new Release Management definition and add an App Service task. You may need to create an Azure Service Endpoint if you don't have one for your subscription yet.

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

# Tips

## Azure AD / Global Admin

Due to the way permissions and consent works in Azure AD, a Global Admin is required to be involved
in the setup in some form. If this is not possible, there's an alternative: create a new Azure AD
environment just for this. Azure AD itself is free for these scenarios.

Here are the instructions for creating a new Azure AD Tenant: https://docs.microsoft.com/en-us/azure/active-directory/develop/active-directory-howto-tenant

Once you have the tenant, make sure you have a local user who configured as a Global Admin. You'll
need to login with that account to the Azure Portal to create a new Azure Subscription (Pay as you go). 
When you create a new subscription, it'll be attached to the Azure AD instance you're signed in with by default.

Once configured, you can use the "Add Guest User" button to add your existing Azure AD/Microsoft accounts and use
those to sign in to the Sign Service Admin UI (I would add them as a global admin to your tenant). 

It is critical that the Azure subscription and resources (Key Vaults) reside in the same tenant that the application
is, since the application needs to create users and authenticate to the Key Vault using that directory.
