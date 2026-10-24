@echo off
rem Builds MountSheetSync.exe from these sources with the C# compiler that ships with
rem Windows (.NET Framework 4.8). Nothing to install. The exe appears next to this file.
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo C# compiler not found: .NET Framework 4.8 is missing.
  if not defined CI pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:MountSheetSync.exe /win32icon:app.ico /win32manifest:app.manifest /resource:app-small.ico,MountSheetSync.app.ico /resource:warn-small.ico,MountSheetSync.warn.ico /resource:catalog.json,MountSheetSync.catalog.json /resource:i18n.json,MountSheetSync.i18n.json /resource:sheet-i18n.json,MountSheetSync.sheet-i18n.json /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Security.dll /reference:System.IO.Compression.dll *.cs
if errorlevel 1 (
  echo Build failed.
  if not defined CI pause
  exit /b 1
)
echo Done: %~dp0MountSheetSync.exe
echo To use it: exit the running Mount Sheet Sync (tray icon menu, last item),
echo then copy this file over 2-program\MountSheetSync.exe and start it again.
if not defined CI pause
