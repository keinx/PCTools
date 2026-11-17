@echo off
rem Optional rebuild script. PCTools.exe is fully self-contained and does not need this file.
rem Usage:  build.cmd          release build (self-test/diag modes excluded)
rem         build.cmd diag     diagnostic build (compile in --smoke* / --aitest etc. self-test code, define DIAG)
setlocal
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "WPF=%FW%\WPF"
set "DIAGDEF="
if /i "%~1"=="diag" set "DIAGDEF=/define:DIAG"
set ICONOPT=
if exist "%~dp0PCTools.ico" set ICONOPT=/win32icon:"%~dp0PCTools.ico"
"%FW%\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 %DIAGDEF% /win32manifest:"%~dp0PCTools.manifest" %ICONOPT% /out:"%~dp0PCTools.exe" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" /r:"%FW%\System.Xaml.dll" /r:"%FW%\System.Management.dll" /r:"%FW%\Microsoft.CSharp.dll" /r:"%FW%\System.ServiceProcess.dll" /r:"%FW%\System.Security.dll" /r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:System.Data.dll "%~dp0PCTools.cs" "%~dp0PCTools.Views.cs"
if errorlevel 1 (
  echo Build FAILED.
  exit /b 1
)
echo Build OK: %~dp0PCTools.exe %DIAGDEF%
