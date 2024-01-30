param(
    [string] $WintrustIniPath
)

# Test if the Wintrust.ini file exists
if (!(Test-Path $WintrustIniPath -PathType Leaf)) {
    exit 1
}

$wintrustini = Get-Content $WintrustIniPath -Raw

# Update the Wintrust.ini file to include the NavSIP.dll
$navSipIni = @'


[5]
DLL=NavSip.dll
GUID={36FFA03E-F824-48E7-8E07-4A2DCB034CC7}
CryptSIPDllCreateIndirectData=NavSIPCreateIndirectData
CryptSIPDllGetSignedDataMsg=NavSIPGetSignedDataMsg
CryptSIPDllIsMyFileType2=NavSIPIsFileSupportedName
CryptSIPDllPutSignedDataMsg=NavSIPPutSignedDataMsg
CryptSIPDllRemoveSignedDataMsg=NavSIPRemoveSignedDataMsg
CryptSIPDllVerifyIndirectData=NavSIPVerifyIndirectData
'@

if ($wintrustini -notmatch 'NavSip.dll') {
    Write-Host "Adding NavSip.dll to Wintrust.ini - $WintrustIniPath"
    $wintrustini += $navSipIni
    Set-Content -Path $WintrustIniPath -Value $wintrustini
}