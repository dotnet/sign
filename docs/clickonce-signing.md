# ClickOnce and VSTO Signing

Sign CLI supports ClickOnce `.application` and VSTO `.vsto` deployment manifests. A deployment manifest identifies an application manifest, which identifies the application's published payloads. Application manifests commonly have names such as `App.exe.manifest` or `AddIn.dll.manifest`; the name alone does not establish the format.

Version 2 also accepts explicitly specified valid ClickOnce application `.manifest` inputs. It identifies the manifest type from its contents, not just its extension. Unrelated side-by-side/fusion or standard `.manifest` files retain their ordinary format handling; enabling version 2 does not make them ClickOnce or make an otherwise unsupported XML file signable.

This guide uses **ClickOnce signing** as the umbrella feature name for both ClickOnce and VSTO artifacts. Commands, diagnostics, and examples may identify the specific deployment (`.application` or `.vsto`) or application (`.manifest`) artifact involved.

## Runtime and package requirements

ClickOnce and VSTO signing require a recent, ideally the latest, .NET Framework on the Windows signing machine. The installed Sign CLI package carries the MSBuild manifest-utility assemblies used by version 2, so it does not require a separate Visual Studio or MSBuild installation. Those packaged assemblies are an intentional run-time dependency: if a required assembly is absent, the operation fails rather than loading a replacement from Visual Studio, the .NET SDK, or a global cache.

## Initial rollout and options

Omission of `--clickonce-signing-version` selects **version 1**. Explicit `--clickonce-signing-version 1` selects the same legacy algorithm, including recursive directory discovery and Mage-based metadata orchestration. Version 1 permits the historical deployment-only re-signing workflow when the related files are absent, but its recursive discovery can encounter other versions or unrelated manifests.

**Version 2 remains opt-in.** It uses manifest-selected traversal and manifest APIs with the existing programmatic manifest signer, rather than version 1's Mage orchestration. Other signing formats are unchanged. Neither omitted nor explicit version 1 emits a migration/deprecation warning during this rollout. A future warning or default switch is not current behavior.

The options are available on `sign code` and its signing-provider subcommands. None has a short alias.

| Option | Behavior |
|---|---|
| `--clickonce-signing-version 1\|2` | Accepts only the literal value `1` or `2`; omission selects `1`. Specify it at most once: every repeated occurrence is invalid, even when the values match. |
| `--no-sign-clickonce-deps` | Requires effective version 2. Updates and signs only explicitly specified ClickOnce manifests. It may discover referenced files for metadata refresh, but does not implicitly sign dependent manifests or payloads. |
| `--no-update-clickonce-manifest` | Requires effective version 2. Signs specified ClickOnce manifests as-is without dependency resolution or file-information updates. Referenced files need not be available; the caller owns consistency with related manifests. |

Each dependency option is invalid with omitted version or explicit version 1. They are mutually exclusive. The exact English diagnostic for combining them is:

```text
The '--no-sign-clickonce-deps' and '--no-update-clickonce-manifest' options cannot be combined.
```

Both modes leave normal user-matched non-ClickOnce inputs on their ordinary signing path. They are not invocation-wide "manifests only" filters: explicitly specifying or glob-matching a DLL or executable still selects its ordinary signing work.

## Version 2's graph and signing order

Given a deployment manifest, version 2 follows its reference to one application manifest, then that application's physical assembly and file references. Prerequisite assemblies and the CLR platform reference are not published payloads. It does not recursively treat all files below the deployment directory as dependencies.

Version 2 classifies `.manifest` inputs from the captured original source, including container provenance, rather than from a temporary staging path. An unreadable or unrelated candidate is not claimed as ClickOnce during selection. After a valid ClickOnce application manifest has been selected in an invocation, later disappearance, unreadability, mutation, or a type mismatch is a signing failure, not a fallback to another format.

For full signing, keep every required referenced file available, including files excluded from signing. Within deployment-driven traversal, the order is:

1. Sign supported, selected payloads, including a referenced `Launcher.exe`.
2. Refresh the application manifest's payload hashes, sizes, and assembly identities, then sign the application manifest.
3. Refresh the deployment manifest's entry-point hash, size, and identity from the signed application manifest, then sign the deployment manifest.
4. Process selected applicable adjacent executables through ordinary signing.

Adjacent discovery checks `setup.exe` and an unreferenced `Launcher.exe` in the deployment manifest's own directory. A referenced launcher is a payload and belongs before the application metadata refresh, not in the final adjacent step. Independently matched executables may already have been signed through their ordinary input path; coordination reuses those results. A differently named executable or one in another directory is not implicitly discovered as adjacent, but can still be signed when independently matched by normal inputs. An explicit application-manifest input does not discover deployment manifests or their adjacent executables.

Implicit traversal and input matching are separate. Traversal follows only the application version referenced by the deployment manifest; a broad positional glob can independently select signable files in other versions. Exclude unwanted inputs explicitly. Within one invocation, each canonical logical source path and container ancestry has one signing owner; overlapping explicit and manifest-selected requests wait for and reuse the same result or failure. Differently named hard-link aliases are distinct identities because each path is an independently replaceable publication target. Coordination is not persisted across invocations: running the command again re-signs selected files. This exactly-once coordination does not make the invocation transactional; earlier completed operations may remain published if a later operation fails.

Nested ClickOnce work inside supported containers uses the same invocation coordinator and retains the original source/container ancestry. Temporary staging controls placement, not discovery or identity. `--max-concurrency` bounds runnable signing owners, including nested work; dependency waits release capacity until their prerequisites are ready. Completed selected outputs are published deterministically, but `--output` includes only the results selected for publication and does not assemble a complete distributable.

### File-extension mapping and metadata

For deployment-based signing, `mapFileExtensions="true"` maps a manifest target such as `App.dll` to the physical file `App.dll.deploy`. Lookup searches the application manifest's directory first, then the deployment directory as a fallback. When mapping is disabled, deployment-based lookup uses the target filename without adding a suffix.

For an explicit application manifest, lookup uses its own directory, trying the exact target path first and then that path with one additional `.deploy` suffix. There is no deployment-directory fallback because no deployment manifest was selected.

Mapping-added suffixes are temporarily removed only in staging for signing and metadata refresh, then restored in published results. A `.deploy` suffix already part of the manifest target is not stripped: target `data.deploy` may map to physical `data.deploy.deploy`. File-list filters use physical published filenames, including added suffixes.

Whenever version 2 refreshes file information, it uses `UpdateFileInfo("v4.5")` to select **SHA-256** hashes rather than the parameterless API's SHA-1 behavior. `"v4.5"` selects the manifest utility's hashing behavior; it is not the application's target framework and does not retarget the application. The `--file-digest` option does not change this manifest-reference hash policy. Version 2 does not add support for legacy ClickOnce runtimes requiring SHA-1 manifest hashes.

### Re-signing responsibilities

With `--no-sign-clickonce-deps`, a deployment-only input refreshes its reference from the current application manifest without updating/signing that application or resolving its payloads. The referenced application manifest must exist and be readable. An application input refreshes metadata from its required payloads without implicitly signing them. A referenced payload that was independently selected through normal input matching is signed by its ordinary operation, and the application waits for and consumes that completed result before refreshing metadata. If both application and deployment manifests are explicit inputs in one invocation, the application is processed first regardless of input order; this ordering also applies to full version-2 signing. In update-only mode, failure of the application prevents deployment signing.

Across separate invocations, callers must arrange the order: payloads first, application refresh/signing second, deployment refresh/signing last. Use the same certificate for related application and deployment manifests and ensure referenced components have the signatures required by the deployment.

With `--no-update-clickonce-manifest`, only signature-related changes are made; existing dependency metadata is retained. This can re-sign a valid manifest with unavailable referenced files, but it does not repair stale references or make an invalid deployment installable. Explicit application manifests are processed before explicit deployment manifests in the same batch, regardless of input order. Re-signing an application manifest changes its signed bytes and can change its identity/public key token, invalidating deployment references. Update and re-sign related deployment manifests afterward as needed. Signing both manifests as-is does not repair that relationship.

## Examples

These examples use **PowerShell on Windows** and sign in place. Assume the published layouts shown below already exist. Set `$certificateFingerprint` to the SHA-256, SHA-384, or SHA-512 fingerprint of your actual RSA code-signing certificate in `CurrentUser\My`, with access to its private key. Supply hexadecimal without separators (64, 96, or 128 characters respectively); the usual SHA-1 store thumbprint is not accepted. This is an environment-specific value, not a literal placeholder to pass to the CLI. No certificate or timestamp arguments are elided from the signing commands.

The commands explicitly use `-t http://timestamp.acs.microsoft.com`, the CLI's default timestamp URL. Ensure the service is reachable and suitable for your signing policy, or supply your approved service. Manifest signing uses the existing ClickOnce timestamp implementation; do not assume every format uses identical timestamp protocols or that `--timestamp-digest` changes manifest-reference hashing.

For a local PFX workflow, add `-cf C:\Signing\certificate.pfx` and provide its password securely if needed; `-cfp` is still required to identify the certificate. For a machine-backed CSP private key, use the provider and key-container options documented by `sign code certificate-store --help`, including `-csp`, `-k`, and `-km` as applicable; a certificate installed only in `LocalMachine\My` is not selected by the fingerprint-only commands above. Do not embed private keys or passwords in source-controlled scripts. Azure Key Vault and Artifact Signing users should supply their provider's authentication/certificate options instead; consult `sign code azure-key-vault --help` or `sign code artifact-signing --help`. Version 2 does not relax certificate, key-access, timestamp, or machine prerequisites.

In version 1 and version 2 modes that update manifest metadata, omitting `--publisher-name` uses the certificate subject as the publisher name; supply an appropriate explicit publisher name when the deployment environment requires one. Version 2 adds no publisher-name length warning or validation. Long generated publisher/product data can remain an activation limitation inherited from version 1. Version 2 `--no-update-clickonce-manifest` instead preserves the existing publisher metadata and does not apply `--publisher-name`.

### Full version-2 signing

Assume `C:\Signing\publish\App.application` references `Application Files\App_1_0_1_0\App.exe.manifest`, and all referenced payloads are present.

```powershell
sign code certificate-store -cfp $certificateFingerprint -t http://timestamp.acs.microsoft.com --clickonce-signing-version 2 -b C:\Signing\publish App.application
```

With no file list, supported referenced payloads and applicable adjacent executables are selected, even though the positional input is just the deployment manifest. Unsupported payloads remain unchanged but contribute to refreshed metadata.

### Multi-version layout and exclusions

Example layout:

```text
C:\Signing\publish\
    App.application
    setup.exe
    current-version.txt
    Application Files\
        App_1_0_0_0\    (old version)
        App_1_0_1_0\    (referenced current version)
```

The full-signing command above implicitly follows only `App_1_0_1_0`. To leave a mapped third-party payload unsigned while refreshing its metadata, create `C:\Signing\publish\current-version.txt` containing:

```text
**/*
!**/ThirdParty.dll.deploy
```

```powershell
sign code certificate-store -cfp $certificateFingerprint -t http://timestamp.acs.microsoft.com --clickonce-signing-version 2 -b C:\Signing\publish -fl current-version.txt App.application
```

Manifest traversal does not select `App_1_0_0_0`, and the file list excludes the mapped third-party payload from implicit signing. Keep that payload available: its current hash/size still goes into refreshed application metadata. Do not add a broad positional glob such as `"**/*"` when the goal is to limit signing to the deployment-selected version. Positional matches that survive the file list become explicit inputs, so allowed files and application manifests in older version directories can add signing work outside the deployment-selected graph. File-list exclusions still apply to both explicit standalone inputs and manifest-selected payloads, so `ThirdParty.dll.deploy` remains unsigned in this example. Enumerate any additional ordinary inputs that should be signed. See [file globbing](file-globbing.md).

### Explicit application-manifest signing

```powershell
sign code certificate-store -cfp $certificateFingerprint -t http://timestamp.acs.microsoft.com --clickonce-signing-version 2 -b "C:\Signing\publish\Application Files\App_1_0_1_0" App.exe.manifest
```

This signs selected supported referenced payloads, refreshes their metadata, and signs the application manifest. It does not update/sign `App.application`. Follow with the deployment-only command below to refresh that relationship.

### Deployment-only update and re-sign

Use this when the referenced application manifest is already current and signed:

```powershell
sign code certificate-store -cfp $certificateFingerprint -t http://timestamp.acs.microsoft.com --clickonce-signing-version 2 --no-sign-clickonce-deps -b C:\Signing\publish App.application
```

Only the deployment manifest is updated and signed. Its referenced application manifest is required, but that application's payloads need not be available for this operation.

To update and sign both manifests without implicitly signing payloads, explicitly select both. Application payloads must be available for its metadata refresh:

```powershell
sign code certificate-store -cfp $certificateFingerprint -t http://timestamp.acs.microsoft.com --clickonce-signing-version 2 --no-sign-clickonce-deps -b C:\Signing\publish App.application "Application Files\App_1_0_1_0\App.exe.manifest"
```

Despite the displayed input order, the application is processed first.

### Sign as-is with unavailable dependencies

Assume only the already-prepared deployment manifest is available at `C:\Signing\resign\App.application`:

```powershell
sign code certificate-store -cfp $certificateFingerprint -t http://timestamp.acs.microsoft.com --clickonce-signing-version 2 --no-update-clickonce-manifest -b C:\Signing\resign App.application
```

Default version 2 would fail because the referenced application manifest is missing and report the expected reference/path with guidance to use this option. `--no-sign-clickonce-deps` does not bypass that requirement. Sign-as-is is appropriate only when retaining existing dependency metadata is intentional; it is not a workaround for broken references in a full signing pipeline.

### VSTO signing and the archival deployment copy

Assume root `C:\Signing\Output\AddIn.vsto` references `Application Files\AddIn_1_0_1_0\AddIn.dll.manifest`:

```powershell
sign code certificate-store -cfp $certificateFingerprint -t http://timestamp.acs.microsoft.com --clickonce-signing-version 2 -b C:\Signing\Output AddIn.vsto
```

Version 2 preserves the VSTO-specific XML subtree in application manifests while refreshing modeled ClickOnce metadata. VSTO publishing may also place an archival copy of `AddIn.vsto` in the version directory. Version 2 does **not** implicitly recreate or synchronize that copy. If downstream tooling requires identical copies, explicitly copy the signed root deployment manifest after successful signing:

```powershell
Copy-Item -LiteralPath C:\Signing\Output\AddIn.vsto -Destination "C:\Signing\Output\Application Files\AddIn_1_0_1_0\AddIn.vsto" -Force
```

Do not blindly use `"**/*.vsto"` on a layout containing archival copies: those copies are independent inputs and their relative references may not resolve there. For multiple add-ins, select the intended root deployment manifests or exclude archival paths, then copy each signed deployment manifest if parity is required.

## Migration and failure handling

Keep version 1 for pipelines requiring the legacy discovery/re-signing behavior until they are deliberately migrated. Opt into version 2 on a controlled publish copy, check the deployment's selected version and mapping settings, audit broad globs/exclusions, and choose full, update-only, or sign-as-is mode explicitly. Test installation, launch, and update with your actual certificate and supported ClickOnce/VSTO clients; signature production alone does not establish runtime compatibility.

Sign only trusted regular files and directories in signer-controlled locations. Sign CLI does not reject or resolve junctions, symbolic links, mount points, or other reparse points; Windows file APIs may transparently follow them while reading sources or publishing outputs. Do not use an input or output root, ancestor, or descendant path that is or traverses a reparse point, and do not allow lower-trust principals to mutate those paths during the invocation. This pre-existing limitation is shared with legacy behavior; version 2 does not add filesystem-containment enforcement.

Version 2 validates deployment reference structure and required files before metadata updates. A deployment must identify exactly one application-manifest assembly dependency, with no file references; that dependency must not be prerequisite, optional, or resource-fallback. Missing/unreadable references, wrong manifest types, resolution errors, or layouts rejected by version 2's lexical reference and staging checks fail with diagnostics. Those checks do not establish physical filesystem containment across reparse points. Correct the publish layout rather than suppressing failures; sign-as-is preserves metadata and does not certify runtime validity.

Signing is **not transactional**. Earlier successfully signed files may already be published when a later operation fails. Retain a clean publish artifact, inspect failures and partial outputs, and do not ship a partially signed layout. When using `--output`, only selected signed ClickOnce results are published; excluded/unsupported referenced payloads are not automatically copied to make a complete deployment. Assemble and verify the complete distributable yourself.

The [algorithm specification](specs/ClickOnce-Signing-Algorithm.md) preserves design rationale and historical behavior.
