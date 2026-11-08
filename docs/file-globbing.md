# File List Filtering and Globbing

The `code` signing command supports the `--file-list` or `-fl` option. This option specifies a file that contains paths of files to sign or to exclude from signing.

The file names or quoted patterns at the end of the command tell `sign` which files to look at. At least one name or pattern is required. The text file passed to `--file-list` narrows down what gets signed; it cannot add files.

## Sign EXE and DLL Files

Create `sign-list.txt` alongside these files:

```text
C:\build\
|-- myapp.exe
|-- my.first.dll
|-- my.second.dll
|-- other.dll
`-- sign-list.txt
```

Add these patterns to `sign-list.txt`:

```text
myapp.exe
my.*.dll
```

In the commands below, replace the sample certificate path and `<SHA256_FINGERPRINT>` with the actual path and fingerprint. The `certificate-store` command requires `-cfp` even with `-cf`.

```powershell
sign code certificate-store -cf 'C:\certs\test.pfx' -cfp '<SHA256_FINGERPRINT>' `
  --base-directory 'C:\build' --file-list 'sign-list.txt' '*'
```

Here, `-cf` names the certificate. The final `'*'` finds files directly in `C:\build`; `'**/*'` would also look in subfolders. `--file-list 'sign-list.txt'` tells `sign` to read the patterns from `sign-list.txt`.

This signs `myapp.exe`, `my.first.dll`, and `my.second.dll`. It leaves `other.dll` unchanged.

Replace only the final `'*'` with `'myapp.exe'` to sign just that file, even though `sign-list.txt` contains a pattern for DLLs. Leaving out the file name or pattern is an error.

Keep patterns such as `'*'` in quotes.

## Where Patterns Start

Patterns in the text file passed to `--file-list` start in different folders:

- **Standalone files (such as EXEs and DLLs):** the working directory, or the folder set with `--base-directory` (`-b`). Standalone files outside that folder do not match.
- **Package contents:** the root of each package that `sign` opens, including packages inside other packages.
- **ClickOnce files:** the folder containing the `.application` or `.vsto` file named in the command.

The path passed to `--file-list` can be a full path or a path relative to the working directory (or base directory, if set). The location of the text file does not change where its patterns start.

## Packages and ZIP Files

For `C:\packages\MyPackage.nupkg`, create `C:\packages\package-sign-list.txt`:

```text
lib/**/*.dll
```

```powershell
sign code certificate-store -cf 'C:\certs\test.pfx' -cfp '<SHA256_FINGERPRINT>' `
  --base-directory 'C:\packages' --file-list 'package-sign-list.txt' 'MyPackage.nupkg'
```

This signs matching DLLs, then saves and signs `MyPackage.nupkg`. The package name need not be in `package-sign-list.txt`. If the command selects a package or ZIP, an exclusion in the file passed to `--file-list` does not skip it.

The tool looks inside packages by default (`--recurse-containers true`). Files that do not match stay in the package. For ZIP files, matching files are signed and saved back into the ZIP; the ZIP itself is not signed.

### Nested ZIP Files

To sign `app.dll` in this ZIP layout:

```text
outer.zip
`-- inner.zip
    `-- lib
        `-- app.dll
```

When signing `outer.zip`, put both lines in the file passed to `--file-list`:

```text
inner.zip
lib/**/*.dll
```

The first line lets the tool open `inner.zip`; the second selects the DLL inside it. With only `lib/**/*.dll`, the tool does not open `inner.zip`.

### APPX/MSIX Bundles

With `--recurse-containers` enabled (the default), a bundle's inner `.appx` and `.msix` packages are still signed even if the file passed to `--file-list` excludes them. For example, putting these lines in that file does not skip `inner.msix`:

```text
**/*
!inner.msix
```

Those patterns still filter the files inside `inner.msix`.

With `--recurse-containers false`, `sign` signs the bundle without opening or signing the packages inside it.

## ClickOnce Deployments

Name the `.application` or `.vsto` file at the end of the command. `sign` looks in that file's folder even if the file itself is not signed. The patterns in the text file passed to `--file-list` start in that folder and narrow down which app files and manifests get signed.

For example, to sign this app without signing `ThirdParty.dll.deploy`:

```text
C:\publish\
|-- MyApp.application    <- deployment manifest named in the command
|-- setup.exe
|-- clickonce-list.txt
`-- Application Files\
    `-- MyApp_1_0_0_0\
        |-- MyApp.exe.manifest    <- application manifest for this version
        |-- MyApp.exe.deploy
        |-- MyApp.Core.dll.deploy
        `-- ThirdParty.dll.deploy
```

Create `C:\publish\clickonce-list.txt` with these patterns:

```text
**/*
!**/ThirdParty.dll.deploy
```

```powershell
sign code certificate-store -cf 'C:\certs\test.pfx' -cfp '<SHA256_FINGERPRINT>' `
  --base-directory 'C:\publish' --file-list 'clickonce-list.txt' 'MyApp.application'
```

This signs `setup.exe`, the manifests, and the other `.deploy` files shown above. With `--output`, files skipped for signing, such as `ThirdParty.dll.deploy`, can still be copied to the output.

When writing patterns in `clickonce-list.txt`:

- **Keep the manifests in sync.** Signing only `MyApp.Core.dll.deploy` can leave the old DLL hash in `MyApp.exe.manifest`, causing the install to fail. For this layout, add `**/MyApp.exe.manifest` and `*.application` too, so `sign` updates the application manifest first, then the deployment manifest.
- **Use a wildcard for the top-level deployment manifest.** The command names `MyApp.application`, but `sign` checks a temporary copy such as `j4d9x2ka.application`. `*.application` matches that copy; `MyApp.application` does not. Use `*.vsto` for a `.vsto` deployment.
- **Match the `.deploy` suffix.** `**/*.dll.deploy` matches `MyApp.Core.dll.deploy`; `**/*.dll` does not. `sign` checks the name before temporarily removing `.deploy`.
- **Match at most one `.manifest` file per deployment.** If `**/*.manifest` matches files in two version folders, signing fails. Match the intended version instead, for example `Application Files/MyApp_1_0_0_0/MyApp.exe.manifest`. The `.application` or `.vsto` manifest does not count.

## File List Format

In the file passed to `--file-list`, use one pattern or literal file path per line to include or exclude files. Filtering uses globbing, and supports advanced features such as brace expansion and negation.

Exclusions (`!`) win regardless of line order. If `--file-list` points to an empty file or one with only exclusions, that does not mean "include everything." To include everything except test DLLs:

```text
**/*
!**/*.Tests.dll
```

The package, bundle, and ClickOnce rules above still apply.

The following is supported:

* Standard globbing: `*` and `**` wildcards.
* Brace expansion: `{a,b}` expands to both `a` and `b`.
  - Nested braces also work: `a{b,c{d,e}f}g` expands to `abg` `acdfg` `acefg`
* Numeric ranges: `{1..3}` expands to `1`, `2`, `3`.
* Negation: Patterns starting with `!` exclude files matching that pattern.
* Escaping: Use `\{`, `\}`, or `\!` to treat these characters literally.

The `?` wildcard is not supported: `file?.txt` does not match `file1.txt`.

## Pattern Examples

| Pattern                | Description                              | Matches Example(s)           |
|------------------------|------------------------------------------|------------------------------|
|`File.appx`             | Include `File.appx`                      | `File.appx`                  |
|`!Installer.msix`       | Exclude `Installer.msix`                 | excludes `Installer.msix`    |
|`*.txt`                 | All `.txt` files in the current directory  | `file.txt`, `notes.txt`      |
|`**/*.cs`               | All `.cs` files in all subdirectories      | `src/Program.cs`             |
|`docs/{README,HELP}.md` | `docs/README.md` and `docs/HELP.md`      | `docs/README.md`, `docs/HELP.md` |
|`images/*.{png,jpg}`    | All `.png` and `.jpg` files in images      | `images/a.png`, `images/b.jpg`   |
|`file{1..3}.log`        | `file1.log`, `file2.log`, `file3.log`    | `file2.log`                  |
|`!bin/**`               | Exclude everything under `bin` directory   | excludes `bin/Debug/app.exe` |
|`foo/\{bar\}.txt`       | Matches the literal file `foo/{bar}.txt`   | `foo/{bar}.txt`              |
|`!**/obj/**`            | Exclude all files in any `obj` directory   | excludes `foo/obj/out.log`   |

## ClickOnce version 2

Input matching and implicit ClickOnce traversal are separate. With `--clickonce-signing-version 2`, passing a deployment manifest follows only its referenced application manifest and payloads. A broad input such as `"**/*"` can independently match files in other version directories; version 2 does not automatically exclude those inputs.

Use `--file-list` (`-fl`) to select or exclude signing work. Without a file list, supported referenced payloads and applicable adjacent executables are selected for signing even when the only positional input is a deployment manifest. With a file list, ClickOnce payload and adjacent-executable filtering uses their physical published filenames (including a mapping-added `.deploy` suffix). All required referenced payloads must still be available for metadata refresh, even if excluded from signing.

For example, to sign only the deployment-selected version while leaving one mapped third-party payload unsigned, place this file list at `C:\Signing\publish\current-version.txt`:

```text
**/*
!**/ThirdParty.dll.deploy
```

After setting `$certificateFingerprint` as described in the [ClickOnce signing guide](clickonce-signing.md), specify only the deployment manifest:

```powershell
sign code certificate-store -cfp $certificateFingerprint -t http://timestamp.acs.microsoft.com --clickonce-signing-version 2 -b C:\Signing\publish -fl current-version.txt App.application
```

Manifest traversal follows only the deployment-selected version, and the file list leaves `ThirdParty.dll.deploy` unsigned while keeping it available for metadata refresh. Do not add a broad positional glob such as `"**/*"` when trying to protect old versions or excluded payloads: positional matches are independent ordinary inputs and can still be signed. Enumerate any additional ordinary inputs that should be signed.

`--no-sign-clickonce-deps` and `--no-update-clickonce-manifest` require version 2 and limit implicit ClickOnce work, not normal user-matched inputs. A payload independently matched by a glob can still be signed through its ordinary signing path. To re-sign only manifests, specify just those manifests, rather than a broad glob.

See the [ClickOnce signing guide](clickonce-signing.md) for complete signing and multi-version examples.
