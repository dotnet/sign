
$currentDirectory = split-path $MyInvocation.MyCommand.Definition

# See if we have the ClientSecret available
if([string]::IsNullOrEmpty($Env:SignClientSecret)){
	Write-Host "Client Secret not found, not signing packages"
	return;
}

# Setup Variables we need to pass into the sign client tool

$appSettings = "$currentDirectory\appsettings.json"
$fileList = "$currentDirectory\filelist.txt"

dotnet tool install --tool-path "$currentDirectory" --version $Env:NBGV_SemVer2 --source-feed $Env:ArtifactDirectory SignClient 

# VSTS workaround for now
$env:PATH = "$env:PATH;$env:USERPROFILE/.dotnet/tools"

$nupgks = ls $Env:ArtifactDirectory\*.nupkg | Select -ExpandProperty FullName

foreach ($nupkg in $nupgks){
	Write-Host "Submitting $nupkg for signing"

	& "$currentDirectory\SignClient" 'sign' -c $appSettings -i $nupkg -f $fileList -r $Env:SignClientUser -s $Env:SignClientSecret -n 'SignClient' -d 'SignClient' -u 'https://github.com/onovotny/SignService' 

	Write-Host "Finished signing $nupkg"
}

Write-Host "Sign-package complete"