# File List Filtering and Globbing

The `code` signing command uses positional file paths or glob arguments to discover top-level inputs. These arguments are required even when you supply `--file-list` or `-fl`.

The file list is a text file containing include/exclude patterns. It filters which selected standalone inputs are signed, including explicitly named files. It does not discover or add inputs, and it does not replace the required positional arguments. Selected containers and ClickOnce deployments have the processing exceptions described below.

For standalone inputs, patterns match paths relative to the working directory, or to `--base-directory` (`-b`) when supplied. Standalone inputs outside that base directory are not selected by these base-relative patterns. The path to the file-list text file itself can be absolute or relative to the same base directory; patterns are not relative to the file-list file's location.

## Standalone Input Example

Suppose `F:\Sign` contains these binaries:

```text
myapp.exe
my.first.dll
my.second.dll
other.dll
```

Create `F:\Sign\sign-list.txt` containing:

```text
myapp.exe
my.*.dll
```

The following PowerShell example uses a sample certificate file path and an all-zero SHA-256 certificate fingerprint as placeholders. Replace both with your own certificate values before running it. The certificate-store provider requires the fingerprint (`-cfp`) even when a certificate file (`-cf`) is supplied.

```powershell
sign code certificate-store -cf "F:\Certificates\test.pfx" `
  -cfp "0000000000000000000000000000000000000000000000000000000000000000" `
  --base-directory "F:\Sign" --file-list "sign-list.txt" "*"
```

Quote glob arguments so that `sign`, rather than the shell, expands them. Here, the positional `"*"` discovers the files in the base directory, and the file list limits standalone signing to `myapp.exe`, `my.first.dll`, and `my.second.dll`. `other.dll` is not signed.

Replacing the positional `"*"` with `"myapp.exe"` signs only `myapp.exe`: the file list does not add either DLL as an input. Omitting the positional argument altogether is an error.

## Containers and ClickOnce Deployments

Top-level container inputs selected by positional paths or globs remain eligible for processing and, for signable formats, signing even when their filenames do not match the file list. Inclusion and exclusion patterns do not filter out these top-level containers.

With container recursion enabled (`--recurse-containers true`, the default), the file list filters contents using paths relative to each container's root. Unmatched files are retained in the container, not removed.

For example, create `F:\Sign\package-sign-list.txt` containing:

```text
lib/**/*.dll
```

Using the same placeholder certificate values as above:

```powershell
sign code certificate-store -cf "F:\Certificates\test.pfx" `
  -cfp "0000000000000000000000000000000000000000000000000000000000000000" `
  --base-directory "F:\Sign" --file-list "package-sign-list.txt" "MyPackage.nupkg"
```

The matching DLLs are signed, then the NuGet package is repacked and signed. `MyPackage.nupkg` does not need to appear in the file list. A plain ZIP archive is repacked after signing its matching contents, but is not itself cryptographically signed.

Selected ClickOnce `.application` and `.vsto` inputs also remain selected for processing regardless of top-level filename matches. The existing ClickOnce workflow applies the file list to deployment contents relative to the deployment input's directory, including payloads and manifests. Keeping the deployment input selected does **not** guarantee that its manifest is signed: the manifests must still pass content filtering. Dependencies can be staged or copied even when they are not selected for signing, to support manifest updates.

## Compatibility

The file list now filters standalone inputs as well as container and ClickOnce contents. Commands that previously used a list only for package contents should add inclusion patterns for any standalone inputs they still intend to sign, including explicitly named files. Without `--file-list`, selection and processing are unchanged.

Output destinations are planned from all resolved positional inputs before filtering. Filtering a multi-input invocation down to one retained input does not change that input's output destination.


## File List Format

You can provide a list of string patterns (one pattern per line) which describe files to include or exclude, or literal file paths. Filtering uses globbing, and supports advanced features such as brace expansion and negation.

Exclusions beginning with `!` take precedence over inclusions, regardless of their order in the file. An empty list or a list containing only exclusions does not implicitly include everything. To select all files except test DLLs, include an include-all pattern:

```text
**/*
!**/*.Tests.dll
```

These rules apply to standalone inputs and filtered contents; the top-level container and ClickOnce processing exceptions described above still apply.

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
