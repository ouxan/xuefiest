@echo off
rem Builds with the C# compiler that ships with .NET Framework 4.8 on Windows; nothing else to install.
setlocal
cd /d "%~dp0"

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
    echo csc.exe not found. .NET Framework 4.8 is required.
    exit /b 1
)

if not exist bin mkdir bin

set "RES="
for %%f in (ui\*.xaml) do call set "RES=%%RES%% /resource:ui\%%~nxf,ui.%%~nxf"

set "ICON="
if exist app.ico set "ICON=/win32icon:app.ico"

"%FW%\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu ^
    /out:bin\Xuefiest.exe /win32manifest:app.manifest %ICON% /lib:"%FW%\WPF" ^
    /r:System.dll /r:System.Core.dll /r:System.Xaml.dll /r:WindowsBase.dll ^
    /r:PresentationCore.dll /r:PresentationFramework.dll ^
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.Extensions.dll ^
    %RES% src\*.cs
if errorlevel 1 exit /b 1

echo.
echo Built: %~dp0bin\Xuefiest.exe
