# File List Filtering and Globbing

**File arguments choose the files to consider. `--file-list` (`-fl`) narrows that selection; it cannot add files.**

The example below uses individual `.exe` and `.dll` files. Packages and ClickOnce deployments are covered separately afterward.

## Standalone Files

Create `sign-list.txt` alongside the binaries:

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

The same list produces different results depending on the file argument:

| File argument | Result |
|---|---|
| `'*'` | Signs `myapp.exe`, `my.first.dll`, and `my.second.dll`; leaves `other.dll` unchanged. |
| `'myapp.exe'` | Signs only `myapp.exe`. The list cannot add the DLLs. |
| No file argument | Error: file arguments are still required, even with `--file-list`. |

For example, the first row uses this complete command.

In the commands below, replace the certificate path and `<SHA256_FINGERPRINT>` with your certificate's values. The certificate-store provider requires `-cfp` even with `-cf`.

```powershell
sign code certificate-store -cf 'C:\certs\test.pfx' -cfp '<SHA256_FINGERPRINT>' `
  --base-directory 'C:\build' --file-list 'sign-list.txt' '*'
```

Quote glob arguments so `sign` receives the patterns rather than relying on the shell to expand them.

### Paths and Folders

In the command above, `--base-directory 'C:\build'` tells `sign` where to look. Without `--base-directory` (`-b`), it uses the current working directory.

* Patterns inside the list are relative to that folder, **not** to the folder containing the list.
* The list file's own path can be absolute or relative to that same folder.
* Standalone files outside that folder do not match these patterns.

## Packages and ZIP Files

For `C:\packages\MyPackage.nupkg`, create `C:\packages\package-sign-list.txt`:

```text
lib/**/*.dll
```

```powershell
sign code certificate-store -cf 'C:\certs\test.pfx' -cfp '<SHA256_FINGERPRINT>' `
  --base-directory 'C:\packages' --file-list 'package-sign-list.txt' 'MyPackage.nupkg'
```

This signs matching DLLs, then repacks and signs `MyPackage.nupkg`. The list does not have to include the package name. It also cannot exclude a package or ZIP chosen by a file argument.

With recursion enabled (the default), the list selects files inside NuGet packages, APPX/MSIX packages, and ZIP files. Paths start at each package's root. Unmatched files remain inside. A plain ZIP is repacked after its matching contents are signed, but the ZIP itself is not signed.

### Packages Inside Packages

To sign `app.dll` in this ZIP layout:

```text
outer.zip
`-- inner.zip
    `-- lib
        `-- app.dll
```

Include both the inner ZIP and the files you want to sign:

```text
inner.zip
lib/**/*.dll
```

The first line lets the tool open `inner.zip`; the second selects the DLL inside it. With only `lib/**/*.dll`, the tool does not open `inner.zip`.

### APPX/MSIX Bundles

Bundles work differently. With recursion enabled, the tool still opens and signs `inner.msix` inside a bundle with this list:

```text
**/*
!inner.msix
```

Excluding the inner package does not skip it. The list still filters the files inside that package.

## ClickOnce Deployments

ClickOnce `.application` and `.vsto` inputs remain selected for deployment processing. The list filters payloads and manifests relative to the deployment input's directory; retaining the input does not guarantee its manifest is signed. Dependencies can still be copied for manifest processing even when excluded from signing.

## Existing Commands

Since #1076, lists also filter standalone inputs, including explicitly named files. If a command previously used a list only for package contents, add patterns for standalone files that must still be signed. Without `--file-list`, selection is unchanged.

Output destinations are planned before filtering, so filtering does not move retained outputs.

## File List Format

You can provide a list of string patterns (one pattern per line) which describe files to include or exclude, or literal file paths. Filtering uses globbing, and supports advanced features such as brace expansion and negation.

When the list is used to select files, exclusions (`!`) win regardless of line order. An empty or exclusion-only list does not mean "include everything." To include everything except test DLLs:

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
