# File List Filtering and Globbing

The `code` signing command supports the `--file-list` or `-fl` option. This option specifies a file that contains paths of files to sign or to exclude from signing.

When using the file list option you must use a path relative to the working directory (or base directory, if used). You can change the base directory using `--base-directory` or `-b`.

Example:

`sign.exe code certificate-store -cf test.pfx -fl F:\Sign\file_sign_list.txt *`


## File List Format

You can provide a list of string patterns (one pattern per line) which describe files to include or exclude, or literal file paths. Filtering uses globbing, and supports advanced features such as brace expansion and negation.

The following is supported:

* Standard globbing: `*`, `?`, `**` wildcards.
* Brace expansion: `{a,b}` expands to both `a` and `b`.
  - Nested braces also work: `a{b,c{d,e}f}g` expands to `abg` `acdfg` `acefg`
* Numeric ranges: `{1..3}` expands to `1`, `2`, `3`.
* Negation: Patterns starting with `!` exclude files matching that pattern.
* Escaping: Use `\{`, `\}`, or `\!` to treat these characters literally.


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

Manifest traversal follows only the deployment-selected version, and the file list leaves `ThirdParty.dll.deploy` unsigned while keeping it available for metadata refresh. Do not add a broad positional glob such as `"**/*"` when the goal is to limit signing to the deployment-selected version. Positional matches that survive the file list become explicit inputs, so allowed files and application manifests in older version directories can add signing work outside the deployment-selected graph. File-list exclusions still apply to both explicit standalone inputs and manifest-selected payloads, so `ThirdParty.dll.deploy` remains unsigned in this example. Enumerate any additional ordinary inputs that should be signed.

`--no-sign-clickonce-deps` and `--no-update-clickonce-manifest` require version 2 and limit implicit ClickOnce work, not normal user-matched inputs. A payload independently matched by a glob can still be signed through its ordinary signing path. To re-sign only manifests, specify just those manifests, rather than a broad glob.

See the [ClickOnce signing guide](clickonce-signing.md) for complete signing and multi-version examples.
