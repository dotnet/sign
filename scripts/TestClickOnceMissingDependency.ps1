#Requires -Version 7.0
param
(
    [Parameter(Mandatory = $True)]
    [string] $PackageFilePath,

    [Parameter(Mandatory = $True)]
    [string] $EvidenceDirectoryPath
)

$ErrorActionPreference = 'Stop'
$package = Get-Item -LiteralPath $PackageFilePath
$evidence = New-Item -ItemType Directory -Path $EvidenceDirectoryPath
$extracted = Join-Path $evidence.FullName 'package'
[System.IO.Compression.ZipFile]::ExtractToDirectory($package.FullName, $extracted)
$results = [System.Collections.Generic.List[object]]::new()
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
$certificate = $null
$pfx = Join-Path $evidence.FullName 'synthetic.pfx'

Try
{
    $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=ClickOnce missing dependency regression', $rsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned(
        [DateTimeOffset]::Now.AddMinutes(-5), [DateTimeOffset]::Now.AddHours(1))
    [System.IO.File]::WriteAllBytes($pfx, $certificate.Export(
        [System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx))
    $fingerprint = $certificate.GetCertHashString(
        [System.Security.Cryptography.HashAlgorithmName]::SHA256)

    ForEach ($framework in @('net8.0', 'net10.0'))
    {
        $tool = Join-Path $extracted "tools\$framework\any"
        $dependency = Join-Path $tool 'Microsoft.Build.Tasks.Core.dll'
        If (-not (Test-Path -LiteralPath $dependency))
        {
            Throw "The original package lacks $dependency"
        }
        Remove-Item -LiteralPath $dependency
    }

    ForEach ($framework in @('net8.0', 'net10.0'))
    {
        $source = New-Item -ItemType Directory -Path (Join-Path $evidence.FullName $framework)
        $manifest = Join-Path $source.FullName 'App.manifest'
        [System.IO.File]::WriteAllText($manifest, @'
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <assemblyIdentity name="Regression" version="1.0.0.0" processorArchitecture="msil" type="win32" />
  <application xmlns="urn:schemas-microsoft-com:asm.v2" />
</assembly>
'@)
        $sourceHash = (Get-FileHash -LiteralPath $manifest).Hash
        $output = Join-Path $source.FullName 'output'
        $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
        $start.UseShellExecute = $False
        $start.RedirectStandardOutput = $True
        $start.RedirectStandardError = $True
        $start.WorkingDirectory = $source.FullName
        $start.Environment.Remove('DOTNET_STARTUP_HOOKS') | Out-Null
        $start.Environment.Remove('DOTNET_ADDITIONAL_DEPS') | Out-Null
        $start.Environment.Remove('DOTNET_SHARED_STORE') | Out-Null
        ForEach ($argument in @(
            (Join-Path $extracted "tools\$framework\any\sign.dll"), 'code',
            '--clickonce-signing-version', '2', '--no-update-clickonce-manifest',
            '--timestamp-url', 'http://127.0.0.1:1', '--verbosity', 'Debug',
            '--base-directory', $source.FullName, '--output', $output,
            'certificate-store', '--certificate-file', $pfx,
            '--certificate-fingerprint', $fingerprint, 'App.manifest'))
        {
            $start.ArgumentList.Add($argument)
        }
        $process = [System.Diagnostics.Process]::Start($start)
        Try
        {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            If (-not $process.WaitForExit(60000))
            {
                $process.Kill($True)
                Throw "Missing-dependency regression timed out for $framework"
            }
            $log = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
            [System.IO.File]::WriteAllText((Join-Path $source.FullName 'sign.log'), $log)
            $published = Join-Path $output 'App.manifest'
            $result = [ordered]@{
                framework = $framework
                exitCode = $process.ExitCode
                dependencyFailureReported = $log.Contains('Microsoft.Build.Tasks.Core')
                publishedManifestExists = (Test-Path -LiteralPath $published)
                sourceUnchanged = ((Get-FileHash -LiteralPath $manifest).Hash -eq $sourceHash)
                packageSha256 = (Get-FileHash -LiteralPath $package.FullName).Hash
            }
            $results.Add($result)
            $results | ConvertTo-Json | Set-Content (Join-Path $evidence.FullName 'results.json')
            If ($result.exitCode -eq 0 -or -not $result.dependencyFailureReported -or
                $result.publishedManifestExists -or -not $result.sourceUnchanged)
            {
                Throw "Missing signing dependency did not fail closed for ${framework}: $log"
            }
        }
        Finally
        {
            $process.Dispose()
        }
    }
    $results | Format-Table
}
Finally
{
    If (Test-Path -LiteralPath $pfx)
    {
        Remove-Item -LiteralPath $pfx
    }
    If ($null -ne $certificate)
    {
        $certificate.Dispose()
    }
    $rsa.Dispose()
}
