if "%ISEMULATED%"=="true" goto :EOF

ECHO Starting Startup.ps1 >> "C:\Logs\StartupLog.txt" 2>&1
powershell -executionpolicy unrestricted -file %~dp0Startup.ps1 >> "C:\Logs\StartupLog.txt" 2>&1
IF %ERRORLEVEL% NEQ 0 GOTO ExitWithError

ECHO Startup.ps1 Started Successfully >> "C:\Logs\StartupLog.txt" 2>&1

ECHO Running IIS Config

%~dp0IISConfig.cmd
IF %ERRORLEVEL% NEQ 0 GOTO ExitWithError

EXIT /B 0

:ExitWithError
ECHO An error occurred in Startup.ps1. The ERRORLEVEL = %ERRORLEVEL%.  
EXIT %ERRORLEVEL%