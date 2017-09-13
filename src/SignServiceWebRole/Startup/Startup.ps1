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
	"Authentication__AzureAd__AADInstance",
	"Authentication__AzureAd__Audience",
	"Authentication__AzureAd__ClientId",
	"Authentication__AzureAd__TenantId",
	"Authentication__AzureAd__ClientSecret",
	"CertificateInfo__KeyVaultUrl",
	"CertificateInfo__KeyVaultCertificatename",
	"CertificateInfo__TimeStampUrl"
)

foreach($key in $keys){
  [Environment]::SetEnvironmentVariable($key, [Microsoft.WindowsAzure.ServiceRuntime.RoleEnvironment]::GetConfigurationSettingValue($key), "Machine")
}


###

Write-Verbose "========== .NET Core Windows Hosting Installation ==========$nl" 

if (Test-Path "$Env:ProgramFiles\dotnet\dotnet.exe")
{
    Write-Verbose ".NET Core Installed $nl" 
}
elseif (!$isEmulated) # skip install on emulator
{
    Write-Verbose ".NET Core Installed - Install .NET Core$nl"

    Write-Verbose "Downloading .NET Core$nl" 

	
	# Install the VC Redist first
	$tempFile = [System.IO.Path]::GetTempFileName() |
    Rename-Item -NewName { $_ -replace 'tmp$', 'exe' } -PassThru
	Invoke-WebRequest -Uri https://go.microsoft.com/fwlink/?LinkId=746572 -OutFile $tempFile


	$proc = (Start-Process $tempFile -PassThru "/quiet /install /log C:\Logs\vcredist.x64.log")
	$proc | Wait-Process

	$tempFile = [System.IO.Path]::GetTempFileName() |
    Rename-Item -NewName { $_ -replace 'tmp$', 'exe' } -PassThru
	Invoke-WebRequest -Uri https://aka.ms/dotnetcore.2.0.0-windowshosting -OutFile $tempFile


	$proc = (Start-Process $tempFile -PassThru "/quiet /install /log C:\Logs\dotnet_install.log")
	$proc | Wait-Process

}
