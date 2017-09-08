
if "%ISEMULATED%"=="true" goto :EOF

%windir%\system32\inetsrv\appcmd set config -section:applicationPools -applicationPoolDefaults.processModel.idleTimeout:00:00:00 /commit:apphost
%windir%\system32\inetsrv\appcmd set config -section:applicationPools -applicationPoolDefaults.autoStart:true /commit:apphost
%windir%\system32\inetsrv\appcmd set config -section:applicationPools -applicationPoolDefaults.managedRuntimeVersion:"" /commit:apphost
%windir%\system32\inetsrv\appcmd set config -section:applicationPools -applicationPoolDefaults.startMode:AlwaysRunning /commit:apphost
%windir%\system32\inetsrv\appcmd set config -section:applicationPools -applicationPoolDefaults.processModel.identityType:ApplicationPoolIdentity /commit:apphost
%windir%\system32\inetsrv\appcmd set config -section:applicationPools -applicationPoolDefaults.processModel.loadUserProfile:true /commit:apphost

%windir%\system32\inetsrv\AppCmd list app /xml  | %windir%\system32\inetsrv\appcmd set site /in -applicationDefaults.preloadEnabled:True
%windir%\system32\inetsrv\appcmd list site /xml | %windir%\system32\inetsrv\appcmd list app /in /xml | %windir%\system32\inetsrv\appcmd list apppool /in /xml | %windir%\system32\inetsrv\appcmd set apppool /in /proccessModel.idleTimeout:00:00:00 /commit:apphost
%windir%\system32\inetsrv\appcmd list site /xml | %windir%\system32\inetsrv\appcmd list app /in /xml | %windir%\system32\inetsrv\appcmd list apppool /in /xml | %windir%\system32\inetsrv\appcmd set apppool /in /startMode:AlwaysRunning /commit:apphost
%windir%\system32\inetsrv\appcmd list site /xml | %windir%\system32\inetsrv\appcmd list app /in /xml | %windir%\system32\inetsrv\appcmd list apppool /in /xml | %windir%\system32\inetsrv\appcmd set apppool /in /autoStart:true /commit:apphost
%windir%\system32\inetsrv\appcmd list site /xml | %windir%\system32\inetsrv\appcmd list app /in /xml | %windir%\system32\inetsrv\appcmd list apppool /in /xml | %windir%\system32\inetsrv\appcmd set apppool /in /managedRuntimeVersion:"" /commit:apphost
%windir%\system32\inetsrv\appcmd list site /xml | %windir%\system32\inetsrv\appcmd list app /in /xml | %windir%\system32\inetsrv\appcmd list apppool /in /xml | %windir%\system32\inetsrv\appcmd set apppool /in /proccessModel.loadUserProfile:true /commit:apphost
%windir%\system32\inetsrv\appcmd list site /xml | %windir%\system32\inetsrv\appcmd list app /in /xml | %windir%\system32\inetsrv\appcmd list apppool /in /xml | %windir%\system32\inetsrv\appcmd set apppool /in /proccessModel.identityType:ApplicationPoolIdentity /commit:apphost

net stop w3svc
net start w3svc