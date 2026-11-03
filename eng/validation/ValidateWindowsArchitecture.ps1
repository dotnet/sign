param(
    [ValidateSet('X64', 'Arm64')]
    [string] $ExpectedArchitecture = 'Arm64',
    [ValidateSet('Infrastructure', 'Product')]
    [string] $ValidationMode = 'Infrastructure',
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [Parameter(Mandatory = $true)]
    [string] $SourceRevisionId,
    [string] $PackageDirectory,
    [switch] $PackagedToolOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($ValidationMode -eq 'Infrastructure' -and $PackagedToolOnly)
{
    throw 'PackagedToolOnly requires Product validation mode.'
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$validationRoot = Join-Path $repoRoot "artifacts\architecture-validation\$([System.IO.Path]::GetRandomFileName())"
$expandedPackage = Join-Path $validationRoot 'package'
$toolPath = Join-Path $validationRoot 'tool'
$resultsPath = Join-Path $validationRoot 'TestResults'
$logPath = Join-Path $repoRoot "artifacts\log\$Configuration"

New-Item -ItemType Directory -Path $validationRoot, $resultsPath, $logPath -Force | Out-Null

if ($ValidationMode -eq 'Product')
{
    if (!$PackageDirectory)
    {
        $PackageDirectory = Join-Path $repoRoot '..\package'
    }

    $packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter 'sign.*.nupkg' -File)
    if ($packages.Count -ne 1)
    {
        throw "Expected exactly one x64-built Sign CLI package in $PackageDirectory."
    }

    $package = $packages[0]
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($package.FullName, $expandedPackage)

    [xml] $nuspec = Get-Content -LiteralPath (Join-Path $expandedPackage 'sign.nuspec') -Raw
    $version = $nuspec.package.metadata.version
    $mageDirectory = Join-Path $expandedPackage 'tools\net8.0\any\tools\SDK\x86'
    if (!(Test-Path -LiteralPath (Join-Path $mageDirectory 'mage.exe') -PathType Leaf))
    {
        throw 'The x64-built tool package does not contain mage.exe.'
    }
}

function Invoke-Checked([string] $Command, [string[]] $Arguments)
{
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repoRoot
try
{
    if ($PackagedToolOnly)
    {
        $dotnet = (Get-Command 'dotnet' -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    }
    else
    {
        $frameworkDirectory = if ($ExpectedArchitecture -eq 'Arm64') { 'FrameworkArm64' } else { 'Framework64' }
        $clrPath = Join-Path $env:windir "Microsoft.NET\$frameworkDirectory\v4.0.30319\clr.dll"
        if (!(Test-Path -LiteralPath $clrPath -PathType Leaf))
        {
            throw "The native .NET Framework runtime is missing: $clrPath. ARM64 requires .NET Framework 4.8.1 or later."
        }

        . (Join-Path $repoRoot 'eng\common\tools.ps1')
        $installer = Join-Path $repoRoot 'eng\common\dotnet-install.ps1'
        $architecture = $ExpectedArchitecture.ToLowerInvariant()
        InstallDotNetSdk (Join-Path $repoRoot '.dotnet') $GlobalJson.tools.dotnet $architecture

        # Runtime versions are shared across architectures; install them natively, not as x64 emulation.
        $installedRuntimes = @{}
        foreach ($runtime in $GlobalJson.tools.runtimes.PSObject.Properties)
        {
            $family = $runtime.Name.Split('/')[0]
            foreach ($runtimeVersion in $runtime.Value)
            {
                $key = "$family/$runtimeVersion"
                if (!$installedRuntimes.ContainsKey($key))
                {
                    Invoke-Checked 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $installer,
                        '-architecture', $architecture, '-runtime', $family, '-version', $runtimeVersion)
                    $installedRuntimes[$key] = $true
                }
            }
        }

        $env:NUGET_PACKAGES = Join-Path $validationRoot 'packages'
        $dotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
    }

    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    Set-Item -Path "env:DOTNET_ROOT_$($ExpectedArchitecture.ToUpperInvariant())" -Value $env:DOTNET_ROOT
    $env:DOTNET_HOST_PATH = $dotnet
    $env:PATH = "$($env:DOTNET_ROOT);$($env:PATH)"
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    Invoke-Checked $dotnet @('--info')

    if ($ValidationMode -eq 'Infrastructure')
    {
        $probeProject = Join-Path $PSScriptRoot 'ArchitectureProbe\ArchitectureProbe.csproj'
        $probeOutput = Join-Path $validationRoot 'probe'
        Invoke-Checked $dotnet @('build', $probeProject, '--configuration', $Configuration,
            '--output', $probeOutput, '-nodeReuse:false', '-p:UseSharedCompilation=false',
            "-bl:$logPath\Infrastructure-$ExpectedArchitecture.binlog")
        Invoke-Checked $dotnet @((Join-Path $probeOutput 'ArchitectureProbe.dll'), $ExpectedArchitecture)
        return
    }

    $buildProperties = @(
        '-nodeReuse:false',
        '-p:UseSharedCompilation=false',
        '-p:UpdateXlfOnBuild=false',
        '-p:EnableSourceControlManagerQueries=false',
        '-p:EnableSourceLink=false',
        "-p:SourceRevisionId=$SourceRevisionId",
        "-p:NetSdkBinDir=$mageDirectory"
    )
    if (!$PackagedToolOnly)
    {
        Invoke-Checked $dotnet (@('build', 'sign.sln', '--configuration', $Configuration,
            "-bl:$logPath\Build-$ExpectedArchitecture.binlog") + $buildProperties)
    }

    $configPath = Join-Path $validationRoot 'NuGet.Config'
    $escapedPackageDirectory = [System.Security.SecurityElement]::Escape($package.DirectoryName)
    "<configuration><packageSources><clear /><add key='local' value='$escapedPackageDirectory' /></packageSources></configuration>" |
        Set-Content -LiteralPath $configPath -Encoding UTF8
    $originalNuGetPackages = $env:NUGET_PACKAGES
    try
    {
        # Avoid reusing a cached package with the same development version.
        $env:NUGET_PACKAGES = Join-Path $validationRoot 'tool-packages'
        Invoke-Checked $dotnet @('tool', 'install', 'sign', '--tool-path', $toolPath,
            '--version', $version, '--configfile', $configPath)
    }
    finally
    {
        $env:NUGET_PACKAGES = $originalNuGetPackages
    }

    $env:SIGN_TEST_EXPECTED_ARCHITECTURE = $ExpectedArchitecture
    $env:SIGN_TEST_TOOL_PATH = Join-Path $toolPath 'sign.exe'
    Invoke-Checked $env:SIGN_TEST_TOOL_PATH @('--version')
    Invoke-Checked $env:SIGN_TEST_TOOL_PATH @('code', 'certificate-store', '--help')
    $testArguments = @('test')
    if ($PackagedToolOnly)
    {
        $testArguments += @('test\Sign.Core.Test\Sign.Core.Test.csproj',
            '--filter', 'FullyQualifiedName~Sign.Core.Test.PackagedToolTests')
    }
    else
    {
        $testArguments += 'sign.sln'
    }
    Invoke-Checked $dotnet ($testArguments + @('--configuration', $Configuration, '--no-build', '--no-restore',
        '--logger', 'trx', '--results-directory', $resultsPath,
        "-bl:$logPath\Test-$ExpectedArchitecture.binlog") + $buildProperties)
}
finally
{
    Pop-Location
    if ($env:HELIX_WORKITEM_UPLOAD_ROOT)
    {
        Copy-Item -LiteralPath $logPath -Destination $env:HELIX_WORKITEM_UPLOAD_ROOT -Recurse -Force
        Get-ChildItem -LiteralPath $resultsPath -Filter '*.trx' -File |
            Copy-Item -Destination $env:HELIX_WORKITEM_UPLOAD_ROOT -Force
    }
}
