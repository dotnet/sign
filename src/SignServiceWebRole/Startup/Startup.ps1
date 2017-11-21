# This script must be run with Admistrator privileges in order for .NET Core to be able to be installed properly

# Following modifies the Write-Verbose behavior to turn the messages on globally for this session
$VerbosePreference = "Continue"
$nl = "`r`n"

Write-Verbose "========== Check cwd and Environment Vars ==========$nl"
Write-Verbose "Current working directory = $(Get-Location)$nl"
Write-Verbose "IsEmulated = $Env:IsEmulated $nl" 

[Boolean]$IsEmulated = [System.Convert]::ToBoolean("$Env:IsEmulated")

# Set Env Vars from configuration
[Reflection.Assembly]::LoadWithPartialName("Microsoft.WindowsAzure.ServiceRuntime")

$keys = @(
	"AzureAd__AADInstance",
	"AzureAd__Audience",
	"AzureAd__ClientId",
	"AzureAd__TenantId",
	"AzureAd__ClientSecret",
	"AzureAd__Domain",
	"AzureAd__ApplicationObjectId",
	"Admin__SubscriptionId",
	"Admin__Location",
	"Admin__ResourceGroup",
	"ApplicationInsights__InstrumentationKey"
)

foreach($key in $keys){
  [Environment]::SetEnvironmentVariable($key, [Microsoft.WindowsAzure.ServiceRuntime.RoleEnvironment]::GetConfigurationSettingValue($key), "Machine")
}

## Custom temp path that has a 1GB limit instead of 100 MB
$tempPath = [Microsoft.WindowsAzure.ServiceRuntime.RoleEnvironment]::GetLocalResource("CustomTempPath").RootPath.TrimEnd('\\')
[Environment]::SetEnvironmentVariable("TEMP", $tempPath, "Machine")
[Environment]::SetEnvironmentVariable("TEMP", $tempPath, "User")

[Environment]::SetEnvironmentVariable("CustomTempPath", $tempPath, "Machine")

###

Write-Verbose "========== .NET Core Windows Hosting Installation ==========$nl" 

## Check the Host FXR since that has a version in the path, allows us to update
if (Test-Path "$Env:ProgramFiles\dotnet\host\fxr\2.0.3\hostfxr.dll")
{
    Write-Verbose ".NET Core Installed $nl" 
}
elseif (!$isEmulated) # skip install on emulator
{
    Write-Verbose ".NET Core Installed - Install .NET Core$nl"

    Write-Verbose "Downloading .NET Core$nl" 

	[void]([System.Reflection.Assembly]::LoadWithPartialName("Microsoft.WindowsAzure.ServiceRuntime"))
    $tempPath = [Microsoft.WindowsAzure.ServiceRuntime.RoleEnvironment]::GetLocalResource("CustomTempPath").RootPath.TrimEnd('\\')

	
	# Install the VC Redist first
	$tempFile = New-Item ($tempPath + "\vcredist.exe")
    Invoke-WebRequest -Uri https://go.microsoft.com/fwlink/?LinkId=746572 -OutFile $tempFile

    $proc = (Start-Process $tempFile -PassThru "/quiet /install /log C:\Logs\vcredist.x64.log")
    $proc | Wait-Process
	

	# Get and install the hosting module
	$tempFile = New-Item ($tempPath + "\netcore-sh.exe")
    Invoke-WebRequest -Uri https://download.microsoft.com/download/5/C/1/5C190037-632B-443D-842D-39085F02E1E8/DotNetCore.2.0.3-WindowsHosting.exe -OutFile $tempFile

	$proc = (Start-Process $tempFile -PassThru "/quiet /install /log C:\Logs\dotnet_install.log")
	$proc | Wait-Process
}
