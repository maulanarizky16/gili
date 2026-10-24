@echo off
rem Builds Gili.exe with the C# compiler that ships with Windows (.NET Framework 4.8). No SDK needed.
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WMD=%WINDIR%\System32\WinMetadata
set ROOT=%~dp0

"%FW%\csc.exe" /nologo /target:winexe /optimize+ /platform:x64 /warn:4 ^
  /out:"%ROOT%Gili.exe" ^
  /win32icon:"%ROOT%assets\gili.ico" ^
  /win32manifest:"%ROOT%src\app.manifest" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Management.dll ^
  /r:"%WMD%\Windows.Foundation.winmd" /r:"%WMD%\Windows.Media.winmd" /r:"%WMD%\Windows.Storage.winmd" /r:"%WMD%\Windows.Networking.winmd" ^
  /r:"%WMD%\Windows.UI.winmd" /r:"%WMD%\Windows.ApplicationModel.winmd" ^
  /r:"%FW%\System.Runtime.dll" /r:"%FW%\System.Runtime.InteropServices.WindowsRuntime.dll" /r:"%FW%\System.Runtime.WindowsRuntime.dll" ^
  "%ROOT%src\*.cs"
if errorlevel 1 (
  echo Build GAGAL.
  exit /b 1
)
echo Build OK: %ROOT%Gili.exe
