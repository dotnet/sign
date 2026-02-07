# ClickOnce Signing Algorithm

ClickOnce signing has been the source of numerous bugs, primarily because of fragile assumptions in Sign CLI's ClickOnce signing algorithm. This spec proposes algorithm changes that will fix those bugs while improving ClickOnce signing accuracy and predictability.

## Overview of a ClickOnce application

A ClickOnce application consists of:

* a deployment manifest:  a ClickOnce `.application` or `.vsto` file.
* an application manifest:  a ClickOnce `.manifest` file, not to be confused with a [side-by-side or fusion manifest file](https://learn.microsoft.com/windows/win32/sbscs/application-manifests) with the same extension.
* payload files:  assemblies and other files required by the application.
* a bootstrapper:  a `setup.exe` file for installing the ClickOnce application, or a `Launcher.exe` file for activating and launching it via the ClickOnce runtime.

Publishing a ClickOnce application generates a bootstrapper, deployment and application manifest, and payload files. The application manifest and payload files are published to a versioned directory, and the deployment manifest is updated to point to the new application manifest. The bootstrapper points to the deployment manifest.

![ClickOnce file relationships](images/file-relationships.gif)

## Problem

Sign CLI assumes:

- The directory containing the deployment manifest file contains a single ClickOnce application version. In reality, this directory can be the parent directory for multiple versions of the same ClickOnce application and/or the parent directory for multiple ClickOnce applications.
- The directory containing the deployment manifest file contains at most one `.manifest` file in the directory tree. This assumption overlaps with the previous assumption, but even if the directory only contains a single ClickOnce application version, the application may contain multiple `.manifest` files (e.g., an application manifest and one or more side-by-side manifests).
- All `.manifest` files are ClickOnce application manifests, when in reality side-by-side (fusion) manifests also use the `.manifest` extension.
- All non-`.deploy` `.exe` files found anywhere in the deployment manifest's directory tree are treated as signable artifacts. The algorithm does not distinguish bootstrappers (`setup.exe`, `Launcher.exe`) from other `.exe` files, and it searches recursively rather than checking only the deployment manifest's directory.
- Manifests are well-formed and files referenced within them exist at the expected locations. When these assumptions fail, the algorithm's behavior is undefined or produces unclear error messages.
- Files are unique within the staging directory and won't be copied or processed multiple times due to directory tree traversal or references from multiple manifests.
- A single invocation should process an entire ClickOnce application, making it difficult to support partial re-signing scenarios where users want to re-sign only specific components.

The impact is that the algorithm is subject to over-copying, over-signing, failing to sign ClickOnce applications containing a side-by-side manifest, and difficulty with batch signing multiple ClickOnce applications.

There are two special cases that complicate signing:

1. VSTO publishing [signs the deployment manifest then copies it to the versioned application manifest file directory](https://devdiv.visualstudio.com/DevDiv/_git/VS?path=/src/ConfigData/BuildTargets/Microsoft.VisualStudio.Tools.Office.targets&version=GCa9fb919e0a7b3a62050cc77d5dc7dd7c38d50b0e&line=473&lineEnd=483&lineStartColumn=9&lineEndColumn=11&lineStyle=plain&_a=contents) for archival purposes. The current algorithm will discover each deployment manifest file and, in separate operations, attempt to sign each manifest and its dependencies.
1. Sometimes [manifests need to be re-signed](https://learn.microsoft.com/visualstudio/deployment/how-to-re-sign-application-and-deployment-manifests?view=vs-2022). For re-signing, users need to be able to disable implicit signing of related files. For example, a user should be able to re-sign only a deployment manifest or just deployment and application manifests without re-signing payload files.

## Proposed solution

Given a deployment manifest file as a starting point, the algorithm will be updated to:

1. Load the deployment manifest, locate the referenced application manifest, and refuse to continue if it is missing.
1. Stage only the files referenced by the manifests, sign the payloads first, then the application manifest, then the deployment manifest, and finally the bootstrapper.
1. After each signing stage, refresh manifest metadata so hashes, sizes, and entry-point information are consistent with the newly signed bits.

Implementation specifics, including path resolution, `.deploy` renaming, and `ManifestUtilities` API calls, are detailed in Appendix B.

The proposed solution will not attempt to mirror VSTO publishing and copy a signed deployment manifest into the application manifest directory.

### File deduplication

To prevent signing the same file multiple times when users specify overlapping inputs (e.g., both a deployment manifest and its dependencies via glob patterns), Sign CLI will track signed files using a `ConcurrentDictionary<string, byte>` in `SignOptions`. Using `ConcurrentDictionary` provides O(1) thread-safe lookups with minimal memory overhead (byte is the smallest value type), making it efficient for parallel signing of large file sets.

This deduplication is scoped to a single CLI invocation. The dictionary lives in memory for the duration of one `sign` command and is not persisted to disk. Running the same command twice will re-sign all files on the second invocation.

Before signing any file, signers will check if the file's canonical path has already been processed. This deduplication mechanism is independent of other algorithm changes and applies to all file types, not just ClickOnce files.

For re-signing scenarios, two new options will be introduced (both require `--use-new-clickonce-signing`):

* `--no-sign-clickonce-deps`: When specified, Sign CLI will update and sign only the explicitly specified manifest files without signing their dependencies (referenced manifests or payload files). Manifests are still updated before signing to refresh metadata. This allows users to re-sign only a deployment manifest, or only an application manifest, while ensuring the manifest's metadata remains consistent with its dependencies.
* `--no-update-clickonce-manifest`: When specified, Sign CLI will sign manifest files without calling `ResolveFiles()` and `UpdateFileInfo()`. This is useful when re-signing a manifest whose dependencies have not changed.

These options are mutually exclusive (see [Option interactions](#option-interactions)). Without these options, Sign CLI will discover, update, and sign the complete ClickOnce application (deployment manifest, application manifest, and all referenced payload files).

**Note**: Both `--no-sign-clickonce-deps` and `--no-update-clickonce-manifest` are only valid when used with `--use-new-clickonce-signing`. Attempting to use these options without enabling the new ClickOnce signing behavior will result in an error.

### Rollout strategy

The new ClickOnce signing algorithm is **opt-in**. Users must pass the `--use-new-clickonce-signing` flag to enable the proposed behavior. Without this flag, Sign CLI continues to use the current algorithm described in Appendix A.

This approach avoids breaking existing workflows, particularly the deployment-manifest-only re-signing scenario, where the current algorithm succeeds when no application manifest is present, while the new algorithm requires one. It also gives users who depend on auto-discovery of inner deployment manifests (e.g., VSTO archival copies) or signing of non-`setup.exe` bootstrappers time to adapt their pipelines.

The three new CLI options introduced by this spec are:

| Option | Requires `--use-new-clickonce-signing` | Purpose |
|---|---|---|
| `--use-new-clickonce-signing` | No | Enables the manifest-driven signing algorithm described in Appendix B. |
| `--no-sign-clickonce-deps` | Yes | Updates and signs only the specified manifests; does not sign referenced payload files or dependent manifests. |
| `--no-update-clickonce-manifest` | Yes | Signs manifests without calling `ResolveFiles()` / `UpdateFileInfo()`. |

When `--use-new-clickonce-signing` is omitted:

* The current algorithm (Appendix A) is used.
* Passing `--no-sign-clickonce-deps` or `--no-update-clickonce-manifest` results in an error.

In a future major release, the new algorithm may become the default and `--use-new-clickonce-signing` may be deprecated. That transition will be communicated through release notes and deprecation warnings in advance.

## Appendix A:  Current algorithm

In a temporary directory:

1. [[source](https://github.com/dotnet/sign/blob/d4a580a9232e9d7aac931ea57b844e87e255af9a/src/Sign.Core/Signer.cs#L137-L149)]  Copy the deployment manifest to a random file name with the same file extension (`.application` or `.vsto`).
1. [[source](https://github.com/dotnet/sign/blob/d4a580a9232e9d7aac931ea57b844e87e255af9a/src/Sign.Core/DataFormatSigners/ClickOnceSigner.cs#L261-L274)]  Copy all files from the deployment manifest's source directory and all its subdirectories to the temporary directory, while preserving the source's directory structure. _Because copying does not filter down to manifests and payload files, this step can result in overcopying._
1. [[source](https://github.com/dotnet/sign/blob/d4a580a9232e9d7aac931ea57b844e87e255af9a/src/Sign.Core/DataFormatSigners/ClickOnceSigner.cs#L97-L113)]  Sign all `.deploy` and `.exe` files included by user's file matching patterns. _Previous overcopying can lead to oversigning in this step._
1. [[source](https://github.com/dotnet/sign/blob/d4a580a9232e9d7aac931ea57b844e87e255af9a/src/Sign.Core/DataFormatSigners/ClickOnceSigner.cs#L115-L123)]  Remove the `.deploy` extension on any remaining files _excluded_ by file matching patterns.  While these files may not be signed, they're still necessary to update the application manifest.
1. [[source](https://github.com/dotnet/sign/blob/d4a580a9232e9d7aac931ea57b844e87e255af9a/src/Sign.Core/DataFormatSigners/ClickOnceSigner.cs#L130-L139)]  Find files with the `.manifest` file extension.
   * If there are none, continue without signing application manifest.
   * If there is exactly one, assume it is the application manifest and sign it.
   * If there are multiple files, fail.  _This can happen because of earlier overcopying or because side-by-side manifests are not ignored._
1. [[source](https://github.com/dotnet/sign/blob/d4a580a9232e9d7aac931ea57b844e87e255af9a/src/Sign.Core/DataFormatSigners/ClickOnceSigner.cs#L155-L183)]  Sign all deployment manifests in file path length order descending.  _Previous overcopying can lead to oversigning in this step._
1. [[source](https://github.com/dotnet/sign/blob/d4a580a9232e9d7aac931ea57b844e87e255af9a/src/Sign.Core/DataFormatSigners/ClickOnceSigner.cs#L185-L189)]  Restore `.deploy` extensions.
1. [[source](https://github.com/dotnet/sign/blob/d4a580a9232e9d7aac931ea57b844e87e255af9a/src/Sign.Core/Signer.cs#L160-L161)]  Copy files from the temporary directory back to the source location.  _Previous overcopying can lead to overcopying in this step._

Here are two examples of how the current algorithm overcopies and oversigns.

* With the layout as described in [this comment](https://github.com/dotnet/sign/issues/681#issuecomment-2426793329), the current algorithm would sign every version of the application, instead of just the version referenced by App.application:

  ```
  App.application
      Application Files
          App_1_0_0_0
              App.dll.deploy
              App.dll.manifest
              App.exe.deploy
              ...
          App_1_0_1_0
              App.dll.deploy
              App.dll.manifest
              App.exe.deploy
              ...
          ...
  ...
  ```

* With the layout as described in [this comment](https://github.com/dotnet/sign/issues/681#issuecomment-2425548289), each deployment manifest and payload file would be signed _n_ times, where _n_ is the number of `.vsto` files.

  ```
  Output
      myAddin.Word.dll
      myAddin.PowerPoint.dll
      myAddin.Excel.dll

      myAddin.Word.vsto
      myAddin.PowerPoint.vsto
      myAddin.Excel.vsto

      myAddin.Word.dll.manifest
      myAddin.PowerPoint.dll.manifest
      myAddin.Excel.dll.manifest
  ```

## Appendix B:  Proposed algorithm

### Default behavior (no options)

1. Before processing any file, check if its canonical path (via `Path.GetFullPath()`) has already been signed by consulting the deduplication set in `SignOptions`. If already signed, skip the file.
1. Determine the file type and read the manifest:
   - If a file has a `.vsto` or `.application` file extension, read it as a deployment manifest using [`ManifestReader.ReadManifest(...)`](https://learn.microsoft.com/dotnet/api/microsoft.build.tasks.deployment.manifestutilities.manifestreader.readmanifest?view=msbuild-17-netcore#microsoft-build-tasks-deployment-manifestutilities-manifestreader-readmanifest(system-io-stream-system-boolean)). If file reading fails or the returned [`Manifest`](https://learn.microsoft.com/dotnet/api/microsoft.build.tasks.deployment.manifestutilities.manifest?view=msbuild-17-netcore) instance is not a [`DeployManifest`](https://learn.microsoft.com/dotnet/api/microsoft.build.tasks.deployment.manifestutilities.deploymanifest?view=msbuild-17-netcore), stop further processing. The file will not be signed.
   - If a file has a `.manifest` file extension, attempt to read it as an application manifest using `ManifestReader.ReadManifest(...)`. If file reading succeeds and the returned `Manifest` instance is an [`ApplicationManifest`](https://learn.microsoft.com/dotnet/api/microsoft.build.tasks.deployment.manifestutilities.applicationmanifest?view=msbuild-17-netcore), proceed with steps 8-11 below (skipping deployment manifest processing). If reading fails or the manifest is not an `ApplicationManifest`, stop further processing. The file will not be signed.
1. Ensure [`Manifest.ReadOnly`](https://learn.microsoft.com/dotnet/api/microsoft.build.tasks.deployment.manifestutilities.manifest.readonly?view=msbuild-17-netcore) is `false` so the manifest can be updated.
1. Call [`DeployManifest.ResolveFiles()`](https://learn.microsoft.com/dotnet/api/microsoft.build.tasks.deployment.manifestutilities.manifest.resolvefiles?view=msbuild-17-netcore#microsoft-build-tasks-deployment-manifestutilities-manifest-resolvefiles) to resolve file references relative to the deployment manifest's directory. Preserve the resolved relative paths (including `.deploy` suffixes) when staging files.
1. Log all messages in [`Manifest.OutputMessages`](https://learn.microsoft.com/dotnet/api/microsoft.build.tasks.deployment.manifestutilities.manifest.outputmessages?view=msbuild-17-netcore). If any are errors, log them and skip application manifest discovery (proceed to sign only the deployment manifest). `ResolveFiles()` may emit error-level messages for non-fatal conditions such as assembly metadata mismatches or optional references, so hard-failing would break real-world ClickOnce signing scenarios.
1. Obtain the full path of the application manifest file from [`DeployManifest.EntryPoint`](https://learn.microsoft.com/dotnet/api/microsoft.build.tasks.deployment.manifestutilities.deploymanifest.entrypoint?view=msbuild-17-netcore)[`.ResolvedPath`](https://learn.microsoft.com/dotnet/api/microsoft.build.tasks.deployment.manifestutilities.basereference.resolvedpath?view=msbuild-17-netcore#microsoft-build-tasks-deployment-manifestutilities-basereference-resolvedpath).  If the path is empty or the file does not exist, fail signing with an error message that includes the expected path and suggests `--no-update-clickonce-manifest`
1. Read the application manifest file using `ManifestReader.ReadManifest(...)` and ensure `Manifest.ReadOnly` is `false`.
1. Call `ApplicationManifest.ResolveFiles()` to resolve file references, searching the application manifest's directory first, then the deployment manifest's directory as a fallback (if different). The fallback ensures that referenced files located at the deployment root, rather than alongside the application manifest, are resolved and staged correctly. Log all `OutputMessages`. If any are errors, log a warning and continue; `ResolveFiles()` may emit error-level messages for non-fatal conditions (e.g., assembly metadata mismatches, optional references).
1. Copy files referenced by `AssemblyReferences` and `FileReferences` to a temporary directory, preserving the original relative layout rooted at the application manifest directory.
1. Before signing begins, temporarily rename staged files whose names end with `.deploy` to their base names (for example, `MyApp.dll.deploy` → `MyApp.dll`).
1. Discover the bootstrapper by checking if a file named `setup.exe` or `Launcher.exe` exists in the same directory as the deployment manifest. If a bootstrapper exists in a different directory or has a different name, it should be signed separately using standard Authenticode signing outside of the ClickOnce signing algorithm.
1. Sign files in the following order: payload files (if available), the application manifest (if available), the deployment manifest (if available), then the bootstrapper (if available). Mark each file as signed in the deduplication set immediately after signing.
1. After payload files are signed, call `ApplicationManifest.UpdateFileInfo()` to refresh file hashes, sizes, and identities, then restore the `.deploy` suffixes. (`UpdateFileInfo()` hashes files at their `ResolvedPath`, which does not include `.deploy`; the suffixes must be absent when hashes are computed.)
1. After the application manifest is signed, call `DeployManifest.ResolveFiles()` to re-resolve file references, then call `DeployManifest.UpdateFileInfo()` to refresh the deployment manifest's metadata. When signing the deployment manifest with `mage.exe -update`, the `-appm` parameter updates the entry point reference to the application manifest.
1. Copy signed files back to their original locations.

### With `--no-sign-clickonce-deps`

When `--no-sign-clickonce-deps` is specified, Sign CLI will update and sign only the explicitly provided manifest files without signing their dependencies:

1. Before processing any file, check the deduplication set. If already signed, skip the file.
1. For each file provided by the user:
   - If the file has a `.vsto` or `.application` file extension, read it as a deployment manifest, call `DeployManifest.ResolveFiles()` and `DeployManifest.UpdateFileInfo()` to update its metadata based on the current state of referenced files, then sign only the deployment manifest.
   - If the file has a `.manifest` file extension, read it as an application manifest, call `ApplicationManifest.ResolveFiles()` and `ApplicationManifest.UpdateFileInfo()` to update its metadata based on the current state of referenced files, then sign only the application manifest.
   - For other file types, apply the standard signing logic.
1. Mark each signed file in the deduplication set.
1. Referenced manifests and payload files are discovered during the update process but are not signed.
1. The user is responsible for ensuring files are re-signed in the correct order (payload files first, then application manifest, then deployment manifest) if re-signing multiple manifests across separate invocations.

### With `--no-update-clickonce-manifest`

When `--no-update-clickonce-manifest` is specified, Sign CLI will sign manifest files without updating them:

1. Before processing any file, check the deduplication set. If already signed, skip the file.
1. For each file provided by the user:
   - If the file has a `.vsto` or `.application` file extension, read it as a deployment manifest and sign it without calling `DeployManifest.ResolveFiles()` or `DeployManifest.UpdateFileInfo()`.
   - If the file has a `.manifest` file extension, read it as an application manifest and sign it without calling `ApplicationManifest.ResolveFiles()` or `ApplicationManifest.UpdateFileInfo()`.
   - For other file types, apply the standard signing logic.
1. Mark each signed file in the deduplication set.
1. No discovery or metadata updates occur.
1. This option is useful when re-signing manifests whose dependencies have not changed.

### Option interactions

The `--no-sign-clickonce-deps` and `--no-update-clickonce-manifest` options are mutually exclusive:

* `--no-sign-clickonce-deps` alone: Update and sign only specified manifests (dependencies discovered but not signed)
* `--no-update-clickonce-manifest` alone: Sign only specified manifests without updating them (no discovery of dependencies). This is the fastest option, but the user must ensure manifests are already consistent with their dependencies.
* Both options together: Not allowed. `--no-update-clickonce-manifest` skips all discovery and metadata updates, which fully subsumes the dependency-skipping behavior of `--no-sign-clickonce-deps`. Sign CLI will emit an error: `The '--no-sign-clickonce-deps' and '--no-update-clickonce-manifest' options cannot be combined.`
