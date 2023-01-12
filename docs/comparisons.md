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
Linux | ❌ | ✔️ | ❌
macOS | ❌ | ✔️ | ❌

### Requirements

Requirement | NuGet CLI | dotnet CLI | Sign CLI
-- | -- | -- | --
.NET Framework | ✔️ (>= 4.7.2) | ❌ | ❌
.NET SDK | ❌ | ✔️ (>= 5 on Windows, >= 7 on Linux, N/A on macOS) | ❌
.NET Runtime | ❌ | ❌ | ✔️ (>= 6)

## References
* [sign command (NuGet CLI)](https://learn.microsoft.com/en-us/nuget/reference/cli-reference/cli-ref-sign)
* [dotnet nuget sign](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-sign)