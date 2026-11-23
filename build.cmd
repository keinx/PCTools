@echo off
rem Optional rebuild script. PCTools.exe is fully self-contained and does not need this file.
rem Usage:  build.cmd          release build (self-test/diag modes excluded)
rem         build.cmd diag     diagnostic build (compile in --smoke* / --aitest etc. self-test code, define DIAG)
rem The diag build goes to %TEMP%\PCToolsDiag so the project folder never contains a second exe.
setlocal
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "WPF=%FW%\WPF"
set "DIAGDEF="
set "OUTPATH=%~dp0PCTools.exe"
if /i "%~1"=="diag" set "DIAGDEF=/define:DIAG"
if /i "%~1"=="diag" set "OUTPATH=%TEMP%\PCToolsDiag\PCToolsDiag.exe"
if /i "%~1"=="diag" if not exist "%TEMP%\PCToolsDiag" mkdir "%TEMP%\PCToolsDiag"
set ICONOPT=
if exist "%~dp0PCTools.ico" set ICONOPT=/win32icon:"%~dp0PCTools.ico"
"%FW%\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 %DIAGDEF% /win32manifest:"%~dp0PCTools.manifest" %ICONOPT% /out:"%OUTPATH%" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" /r:"%FW%\System.Xaml.dll" /r:"%FW%\System.Management.dll" /r:"%FW%\Microsoft.CSharp.dll" /r:"%FW%\System.ServiceProcess.dll" /r:"%FW%\System.Security.dll" /r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:System.Data.dll "%~dp0PCTools.cs" "%~dp0PCTools.Backup.cs" "%~dp0PCTools.Settings.cs" "%~dp0PCTools.Transfer.cs" "%~dp0PCTools.Views.cs" "%~dp0PCTools.Diag.cs" "%~dp0PCTools.Storage.cs" "%~dp0PCTools.System.cs" "%~dp0PCTools.Ai.cs" "%~dp0PCTools.Theme.cs" "%~dp0PCTools.MainWindow.cs" "%~dp0PCTools.Dialogs.cs" "%~dp0PCTools.Program.cs"
if errorlevel 1 (
  echo Build FAILED.
  exit /b 1
)
echo Build OK: %OUTPATH% %DIAGDEF%
