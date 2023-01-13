# Signing Comparisons

## NuGet

The following tables summarize differences between NuGet, dotnet, and Sign CLI's. 

### Features

Feature | NuGet CLI | dotnet CLI | Sign CLI
-- | -- | -- | --
Use signing certificate from the file system | ✔️ | ✔️ | ❌
Use signing certificate from a local store | ✔️ | ✔️ | ❌
Use signing certificate from Azure Key Vault | ❌ | ❌ | ✔️
Identify signing certificate by fingerprint | ✔️ | ✔️ | ❌
Identify signing certificate by subject name | ✔️ | ✔️ | ❌
Identify signing certificate by name (user-defined) | ❌ | ❌ | ✔️
Can skip timestamping | ✔️ | ✔️ | ❌
Opt-in required to overwrite already signed package | ✔️ | ✔️ | ❌
Can sign files (e.g.: *.dll) inside package | ❌ | ❌ | ✔️
Can verify signed package | ✔️ | ✔️ | ❌

### Platform support

Platform | NuGet CLI | dotnet CLI | Sign CLI
-- | -- | -- | --
Windows x86 | ✔️ | ✔️ | ❌
Windows x64 | ✔️ | ✔️ | ✔️
Windows ARM64 | ❌ | ✔️ | ❌
Linux | ❌ | ✔️* | ❌
macOS | ❌ | ✔️* | ❌

\* NuGet signs packages not files within a package (e.g.:  DLL's).  On every platform where signing is supported, it is possible to sign a package that contains signable files which are unsigned.  Because Authenticode signing is only available on Windows, signing a NuGet package on Linux or macOS can more easily result in a signed package with unsigned files inside.  See https://github.com/NuGet/Home/issues/12362.

### Requirements

Requirement | NuGet CLI | dotnet CLI | Sign CLI
-- | -- | -- | --
.NET Framework | ✔️ (>= 4.7.2) | ❌ | ❌
.NET SDK | ❌ | ✔️ (>= 5 on Windows, >= 7 on Linux, N/A on macOS) | ❌
.NET Runtime | ❌ | ❌ | ✔️ (>= 6)

## References
* [sign command (NuGet CLI)](https://learn.microsoft.com/en-us/nuget/reference/cli-reference/cli-ref-sign)
* [dotnet nuget sign](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-sign)