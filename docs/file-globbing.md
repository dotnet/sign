# File List Filtering and Globbing

The `code` signing command supports the `--file-list` or `-fl` option. This option specifies a file that contains paths of files to sign or to exclude from signing.

The command still needs file names or a quoted pattern such as `'*'`. Use `--file-list` to narrow down what gets signed, not to add files to the command.

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

In the commands below, replace the certificate path and `<SHA256_FINGERPRINT>` with your certificate's values. The `certificate-store` command requires `-cfp` even with `-cf`.

```powershell
sign code certificate-store -cf 'C:\certs\test.pfx' -cfp '<SHA256_FINGERPRINT>' `
  --base-directory 'C:\build' --file-list 'sign-list.txt' '*'
```

Here, `-cf` names the certificate and the final `'*'` finds files in `C:\build`. `--file-list 'sign-list.txt'` tells `sign` to read the patterns from `sign-list.txt`.

This signs `myapp.exe`, `my.first.dll`, and `my.second.dll`. It leaves `other.dll` unchanged.

Replace only the final `'*'` with `'myapp.exe'` to sign just that file, even though `sign-list.txt` contains a pattern for DLLs. Leaving out the file name or pattern is an error.

Keep patterns such as `'*'` in quotes.

### Paths and Folders

For files like the EXE and DLLs above, paths and patterns **inside the file passed to `--file-list`** are relative to the working directory (or base directory, if used). You can change the base directory using `--base-directory` or `-b`.

The path you give `--file-list` can be a full path or a path relative to that same directory. Where you store that file does not change where its patterns start. Files outside the working or base directory do not match those patterns.

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

The tool looks inside packages by default (`--recurse-containers true`). Paths in the file passed to `--file-list` start at the root of each package, and files that do not match stay there. For ZIP files, matching files are signed and saved back into the ZIP; the ZIP itself is not signed.

### Packages Inside Packages

To sign `app.dll` in this ZIP layout:

```text
outer.zip
`-- inner.zip
    `-- lib
        `-- app.dll
```

When signing `outer.zip`, put both lines in the file you pass to `--file-list`:

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

## ClickOnce Deployments

When you give `sign` a ClickOnce `.application` or `.vsto` file, it looks in that file's folder. Use `--file-list` to narrow down what gets signed.

For example, to sign `setup.exe` without signing `MyApp.application`:

```text
C:\release\
|-- clickonce-list.txt      <- pass this path to --file-list
`-- publish\
    |-- MyApp.application   <- put this path at the end of the command
    `-- setup.exe
```

Put just `setup.exe` in `clickonce-list.txt`. Patterns start in `publish\` and also apply to `.manifest` files.

Use `*.application` or `*.vsto` to match the deployment file itself: `sign` checks a copy with a temporary name. Unsigned files may still be copied with the app.

## Existing Commands

The `--file-list` option now also filters files outside packages, even files named in the command. If you used it only for package contents, add patterns for any other files you still want signed. Commands without `--file-list` work as before.

Skipping some files does not change where the other files are written.

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
