# File List Filtering and Globbing

Positional file paths or quoted globs select the inputs. `--file-list` (`-fl`) filters standalone inputs with one include/exclude pattern per line. It does not add inputs or replace the required file arguments.

Standalone patterns are relative to the working directory or `--base-directory` (`-b`), not the file-list file's location. The list file itself can have an absolute or base-relative path. Standalone files outside the base directory do not match these patterns.

## Standalone Files

Suppose `C:\build` contains `myapp.exe`, `my.first.dll`, `my.second.dll`, and `other.dll`. Create `C:\build\sign-list.txt`:

```text
myapp.exe
my.*.dll
```

In the commands below, replace the certificate path and `<SHA256_FINGERPRINT>` with your certificate's values. The certificate-store provider requires `-cfp` even with `-cf`.

```powershell
sign code certificate-store -cf 'C:\certs\test.pfx' -cfp '<SHA256_FINGERPRINT>' `
  --base-directory 'C:\build' --file-list 'sign-list.txt' '*'
```

This signs the three `my...` binaries and leaves `other.dll` unchanged. Replacing `'*'` with `'myapp.exe'` selects only the executable; the list does not add the DLLs. Quote glob arguments so `sign` receives the patterns.

## Containers and ClickOnce

For `C:\packages\MyPackage.nupkg`, create `C:\packages\package-sign-list.txt`:

```text
lib/**/*.dll
```

```powershell
sign code certificate-store -cf 'C:\certs\test.pfx' -cfp '<SHA256_FINGERPRINT>' `
  --base-directory 'C:\packages' --file-list 'package-sign-list.txt' 'MyPackage.nupkg'
```

This signs matching DLLs, then repacks and signs the NuGet package. The package name need not match the list. Selected top-level containers are not removed by inclusion or exclusion patterns.

With container recursion enabled (the default), content patterns are relative to each container root. Unmatched entries remain in the package. Plain ZIP archives are repacked after matching contents are signed, but are not themselves cryptographically signed.

ClickOnce `.application` and `.vsto` inputs remain selected for deployment processing. The list filters payloads and manifests relative to the deployment input's directory; retaining the input does not guarantee its manifest is signed. Dependencies can still be copied for manifest processing even when excluded from signing.

## Existing Commands

Since #1076, lists also filter standalone inputs, including explicitly named files. If a command previously used a list only for package contents, add patterns for standalone files that must still be signed. Without `--file-list`, selection is unchanged.

Output destinations are planned before filtering, so filtering does not move retained outputs.

## File List Format

You can provide a list of string patterns (one pattern per line) which describe files to include or exclude, or literal file paths. Filtering uses globbing, and supports advanced features such as brace expansion and negation.

Exclusions (`!`) win regardless of line order. An empty or exclusion-only list does not implicitly include all files. To include everything except test DLLs:

```text
**/*
!**/*.Tests.dll
```

The top-level container and ClickOnce processing exceptions above still apply.

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
