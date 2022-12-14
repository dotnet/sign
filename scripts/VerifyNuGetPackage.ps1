param
(
    [Parameter(Mandatory = $True)]
    [string] $PackageDirectoryPath
)

Add-Type -AssemblyName 'System.IO.Compression'

[int] $ErrorExitCode = 1

Function VerifyPackage([System.IO.FileInfo] $packageFile, [string[]] $expectedEntryFullNames)
{
    [System.IO.FileStream] $stream = $packageFile.OpenRead()

    Try
    {
        $zipArchive = New-Object System.IO.Compression.ZipArchive -ArgumentList @($stream, [System.IO.Compression.ZipArchiveMode]::Read)

        Try
        {
            ForEach ($expectedEntryFullName in $expectedEntryFullNames)
            {
                [System.IO.Compression.ZipArchiveEntry] $actualEntry = $zipArchive.GetEntry($expectedEntryFullName)

                If ($actualEntry)
                {
                    Write-Host "The NuGet package contains $expectedEntryFullName"
                }
                Else
                {
                    Throw "The NuGet package does not contain $expectedEntryFullName"
                }
            }
        }
        Finally
        {
            $zipArchive.Dispose()
        }
    }
    Finally
    {
        $stream.Dispose()
    }
}

[string[]] $packageFilePaths = [System.IO.Directory]::GetFiles($PackageDirectoryPath, '*.nupkg')

If ($packageFilePaths.Count -ne 1)
{
    Write-Error "Exactly one package was expected (but not found) in $PackageDirectoryPath"

    Exit $ErrorExitCode
}

[string] $sourcePackageFilePath = $packageFilePaths[0]
[System.IO.FileInfo] $destinationFile = [System.IO.FileInfo]::new(
    [System.IO.Path]::Combine(
        [System.IO.Path]::GetTempPath(),
        "$([System.IO.Path]::GetRandomFileName()).zip"))
[bool] $overwrite = $True
[System.IO.File]::Copy($sourcePackageFilePath, $destinationFile.FullName, $overwrite)

[string[]] $expectedEntryFullNames =
    'tools/net6.0/any/tools/SDK/x64/appxpackaging.dll',
    'tools/net6.0/any/tools/SDK/x64/appxsip.dll',
    'tools/net6.0/any/tools/SDK/x64/makeappx.exe',
    'tools/net6.0/any/tools/SDK/x64/makepri.exe',
    'tools/net6.0/any/tools/SDK/x64/Microsoft.Windows.Build.Appx.AppxPackaging.dll.manifest',
    'tools/net6.0/any/tools/SDK/x64/Microsoft.Windows.Build.Appx.AppxSip.dll.manifest',
    'tools/net6.0/any/tools/SDK/x64/Microsoft.Windows.Build.Appx.OpcServices.dll.manifest',
    'tools/net6.0/any/tools/SDK/x64/Microsoft.Windows.Build.Signing.mssign32.dll.manifest',
    'tools/net6.0/any/tools/SDK/x64/Microsoft.Windows.Build.Signing.wintrust.dll.manifest',
    'tools/net6.0/any/tools/SDK/x64/mssign32.dll',
    'tools/net6.0/any/tools/SDK/x64/opcservices.dll',
    'tools/net6.0/any/tools/SDK/x64/SignTool.exe.manifest',
    'tools/net6.0/any/tools/SDK/x64/wintrust.dll',
    'tools/net6.0/any/tools/SDK/x64/wintrust.dll.ini',
    'tools/net6.0/any/tools/SDK/x86/mage.exe'

Try
{
    VerifyPackage -packageFile $destinationFile -expectedEntryFullNames $expectedEntryFullNames
}
Catch
{
    Write-Error $_.Exception.Message

    Exit $ErrorExitCode
}
Finally
{
    $destinationFile.Delete()
}