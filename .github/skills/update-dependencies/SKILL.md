---
name: update-dependencies
description: Update this repository's .NET SDK, .NET runtimes, and centrally managed NuGet package versions to the latest stable releases compatible with the repository's target framework. Use when asked to update, refresh, upgrade, or check dependencies.
---

# Update dependencies

Update dependency pins without changing the repository's target framework or accepting prerelease versions.

## 1. Create a topic branch from the latest main branch

Do not update dependencies directly on `main`.

1. Check `git status --short`. If the worktree contains changes that would be overwritten or mixed into the dependency update, stop and ask the user how to proceed. Do not stash or discard changes without permission.
2. Fetch the latest main branch:

   ```powershell
   git fetch origin main
   ```

3. Create and switch to a new topic branch based directly on `origin/main`. Use a concise name such as `dependency-updates/YYYY-MM-DD`; add a short numeric suffix if that branch already exists.

   ```powershell
   git switch --create dependency-updates/YYYY-MM-DD origin/main
   ```

4. Confirm that the branch's starting commit is the current `origin/main` commit before editing files.

## Repository constraints

- Read `Directory.Build.props` first and treat its `TargetFramework`, currently `net8.0`, as the compatibility boundary.
- Preserve the existing runtime identifiers and package source configuration.
- Never select a version containing a SemVer prerelease suffix such as `-preview`, `-rc`, `-beta`, or any other text after `-`.
- Do not change `Microsoft.Build.Tasks.Core` in `Directory.Packages.props`. Preserve both its version and its adjacent explanatory comment.
- Do not update `global.json` entries other than:
  - `tools.dotnet`
  - `tools.runtimes.dotnet/x64`
  - `tools.runtimes.windowsdesktop/x64`
  - `sdk.version`
- Keep `tools.dotnet` and `sdk.version` identical.
- Keep the two .NET 8 runtime pins identical unless official release metadata proves that their latest stable releases differ.
- Make only dependency-related edits. Preserve XML/JSON layout, comments, and unrelated settings.

## 2. Determine stable SDK and runtime releases

Use the official .NET release metadata, not search-result snippets or prerelease feeds:

- .NET 10: `https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json`
- .NET 8: `https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/8.0/releases.json`

From the .NET 10 metadata, select the newest stable `10.0.xxx` SDK release and assign it to both `tools.dotnet` and `sdk.version`.

From the .NET 8 metadata, select the newest stable `8.0.xx` runtime release. Confirm that the release contains both x64 .NET Runtime and Windows Desktop Runtime artifacts, then assign the release version to:

- `tools.runtimes.dotnet/x64`
- `tools.runtimes.windowsdesktop/x64`

Reject any value that is not a numeric three-part release version.

## 3. Update central package versions

Process every `PackageVersion` in `Directory.Packages.props` except `Microsoft.Build.Tasks.Core`.

For each package:

1. Query authoritative NuGet V3 metadata for all available versions. Respect `NuGet.Config` and use NuGet.org metadata only for packages mirrored by the configured feeds.
2. Exclude unlisted and prerelease versions.
3. Consider versions from newest to oldest using NuGet version ordering.
4. Select the first version whose package assets and dependency groups are compatible with every target framework and runtime that consumes it, especially `net8.0` and `win-x64`.
5. Pin the exact version in `Directory.Packages.props`; do not use ranges or floating versions.

Do not assume that the numerically newest stable package supports `net8.0`. NuGet restore is the authoritative compatibility check. If a candidate causes `NU1202`, `NU1603`, `NU1605`, `NU1701`, an unavailable-package error, or an incompatible transitive graph, choose the next newest stable compatible version instead of suppressing the diagnostic.

Keep related package families on a mutually compatible stable release set when their dependency constraints require it. In particular, evaluate the resolved graph rather than updating each direct pin in isolation.

## 4. Rebuild and run all tests

After editing, run from the repository root:

```powershell
.\eng\common\build.cmd -configuration Release -restore
.\.dotnet\dotnet.exe package list --project .\sign.sln --include-transitive --no-restore
.\eng\common\build.cmd -configuration Release -restore -rebuild -test
```

The first command bootstraps the exact SDK and runtimes pinned in `global.json` before restoring. Use that repository-local SDK for dependency graph inspection instead of assuming the newly selected SDK is already installed globally.

If the repository-local SDK does not support the noun-first package command, use this equivalent:

```powershell
.\.dotnet\dotnet.exe list .\sign.sln package --include-transitive --no-restore
```

Review restore and build output for compatibility, downgrade, fallback, or unresolved dependency warnings. Do not suppress such warnings. For a failure introduced by an updated package, move only that package or tightly coupled package family to the next newest stable compatible version and rerun all validation commands.

Also confirm before finishing that:

- `tools.dotnet` equals `sdk.version`.
- Both runtime pins are the latest stable .NET 8 runtime release.
- Every changed package version is stable.
- `Microsoft.Build.Tasks.Core` and its explanatory comment are unchanged.
- `git diff -- global.json Directory.Packages.props` contains no unrelated edits.

Do not offer to create an issue or pull request unless the restore, complete Release rebuild, and all tests succeed.

## 5. Offer to create an issue and pull request

After all validation succeeds, report the selected SDK, runtime, and package versions, and identify any package that could not use its absolute latest stable release because of `net8.0` compatibility. Then ask whether the user wants you to create both a GitHub issue and pull request.

Do not create either item without confirmation. If confirmed:

1. Create a concise issue that explains:
   - The dependency update scope.
   - The stable SDK and runtime versions selected.
   - That central packages will use the latest stable `net8.0`-compatible releases.
   - That `Microsoft.Build.Tasks.Core` remains pinned for the reason documented inline.
   - Acceptance criteria limited to successful restore, Release rebuild, and all tests.
2. Commit only the intended dependency files with a short imperative commit subject.
3. Push the topic branch.
4. Create a pull request targeting `main` that links and closes the issue.

Keep the issue and pull request concise and review-friendly:

- Use a specific title such as `Update .NET SDK, runtimes, and dependencies`.
- Summarize the SDK, runtime, and package changes without narrating the update process.
- Call out compatibility-driven exceptions, including the unchanged `Microsoft.Build.Tasks.Core` pin.
- Include a short validation section listing the successful restore, Release rebuild, and complete test run.
- Avoid boilerplate, generated dependency changelogs, raw command output, and exhaustive transitive-package lists.
- Use `Fixes #<issue-number>` in the pull request body.
