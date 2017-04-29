Param(
	[string]$signClientSecret
)

$currentDirectory = split-path $MyInvocation.MyCommand.Definition

# See if we have the ClientSecret available
if([string]::IsNullOrEmpty($signClientSecret)){
	Write-Host "Client Secret not found, not signing packages"
	return;
}

# Setup Variables we need to pass into the sign client tool

$appSettings = "$currentDirectory\appsettings.json"
$fileList = "$currentDirectory\filelist.txt"

$appPath = "$currentDirectory\..\src\SignClient\bin\Release\netcoreapp1.1\publish\SignClient.dll"

$nupgks = ls $currentDirectory\..\*.nupkg | Select -ExpandProperty FullName

foreach ($nupkg in $nupgks){
	Write-Host "Submitting $nupkg for signing"

	dotnet $appPath 'sign' -c $appSettings -i $nupkg -f $fileList -s $signClientSecret -n 'SignClient' -d 'SignClient' -u 'https://github.com/onovotny/SignService' 

	Write-Host "Finished signing $nupkg"
}

Write-Host "Sign-package complete"