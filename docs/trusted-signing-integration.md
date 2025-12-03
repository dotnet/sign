# Trusted Signing integration for Sign CLI

This document explains how to use the Sign CLI with a Trusted Signing account to perform code signing using the Trusted Signing provider. See `docs/signing-tool-spec.md` for higher-level background of this tool and the implementation at `src/Sign.SignatureProviders.TrustedSigning` for details.

## Overview

The Sign CLI includes a `trusted-signing` provider that invokes the Trusted Signing service to obtain certificates and perform remote sign operations. The CLI uses the Azure SDK (`Azure.Identity`) for authentication.

Key concepts for this provider:
- Endpoint: the service URL for the Trusted Signing account.
- Account name: the account within the Trusted Signing service.
- Certificate profile: the certificate profile configured in the account that will be used to sign.

For more information, see the Trusted Signing [setup documentation](https://learn.microsoft.com/azure/trusted-signing/quickstart).

## Prerequisites

- An Azure subscription and a Trusted Signing account with at least one active certificate profile.
- An identity (user, service principal, or managed identity) that has the `Trusted Signing Certificate Profile Signer` permission to perform signing.

## How the CLI authenticates

Sign CLI uses Azure.Identity's credential chain by default (DefaultAzureCredential). This means the CLI will try an authentication flow automatically (Azure CLI login, environment variables for a service principal, managed identity, etc.). You may also explicitly choose a credential type with `--azure-credential-type`.

## CLI options for Trusted Signing

The Trusted Signing subcommand is `sign code trusted-signing` and it requires the following options (short forms shown):

- `--trusted-signing-endpoint`, `-tse` : the Trusted Signing service endpoint (URL).
- `--trusted-signing-account`, `-tsa` : the account name in the Trusted Signing service.
- `--trusted-signing-certificate-profile`, `-tscp` : the certificate profile name to use for signing.

The Azure authentication options are available on the same command and include `--azure-credential-type` (`-act`) and managed identity options such as `--managed-identity-client-id` (`-mici`). By default, the CLI uses DefaultAzureCredential.

## Examples

Replace placeholders with your values.

Example — sign a file using your current Azure CLI login (DefaultAzureCredential):

```powershell
# Ensure you're signed into Azure CLI
az login

# Sign a file using Trusted Signing
sign code trusted-signing `
  -tse https://<your-trusted-signing-endpoint> `
  -tsa <your-account-name> `
  -tscp <your-certificate-profile> `
  C:\path\to\artifact.dll
```

Example — service principal (PowerShell session variables; prefer secrets or pipeline variables in CI):

```powershell
$env:AZURE_CLIENT_ID = 'your-client-id'
$env:AZURE_TENANT_ID = 'your-tenant-id'

sign code trusted-signing `
  -tse https://<your-trusted-signing-endpoint> `
  -tsa <your-account-name> `
  -tscp <your-certificate-profile> `
  C:\path\to\artifact.dll
```

Example — managed identity (useful for Azure-hosted agents):

```powershell
# Use managed identity by selecting the credential type explicitly and, if needed, the client id
sign code trusted-signing `
  -tse https://<your-trusted-signing-endpoint> `
  -tsa <your-account-name> `
  -tscp <your-certificate-profile> `
  -act managed-identity `
  -mici <managed-identity-client-id> `
  C:\path\to\artifact.dll
```

Notes:
- If you omit `-act`, the CLI uses DefaultAzureCredential, which already supports Azure CLI, environment variables for service principals, managed identities, and workload identity flows.
- The endpoint URL and exact account/profile names are provided by your Trusted Signing onboarding or Azure portal.

## CI/CD integration tips

- Prefer federated identity (OIDC) or managed identities for CI agents to avoid long-lived secrets. Sign CLI supports workload and managed identity credential flows.
- Store any required values (endpoint, account, certificate profile) as pipeline secrets or protected variables.

## Troubleshooting

- Authentication errors: verify the authentication method (Azure CLI login, environment variables, or managed identity) and that the identity has permission to the Trusted Signing account.
- Permission errors: ensure your principal has the necessary rights on the Trusted Signing account and certificate profile. If unsure, contact your Azure admin or the team that provisioned the Trusted Signing account.
- Endpoint/profile not found: confirm the exact endpoint URL, account name, and certificate profile name from your Trusted Signing account metadata or onboarding docs.
- See the [Trusted Signing FAQ](https://learn.microsoft.com/azure/trusted-signing/faq) for more information.

## Where to look in this repository

- Implementation of the provider: `src/Sign.SignatureProviders.TrustedSigning` (see `TrustedSigningService.cs`, `RSATrustedSigning.cs` and `TrustedSigningServiceProvider.cs`).
- CLI wiring: `src/Sign.Cli/TrustedSigningCommand.cs` (shows required flags and how Azure credentials are constructed).
