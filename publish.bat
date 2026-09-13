@echo off
setlocal EnableDelayedExpansion
rem Publish NativeCompilerCLI (AOT) then sign the unsigned native exe.
rem Smart App Control / WDAC on this machine blocks unsigned executables,
rem so the published exe is signed with the local code-signing cert.

set "SIGNTOOL=C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
set "THUMBPRINT=63DE8A7D5283718D453AD7CCD825BB27842AD5F0"
set "PUBLISHED=src\NativeCompilerCLI\bin\Release\net10.0\win-x64\publish\NativeCompilerCLI.exe"
set "NATIVE=src\NativeCompilerCLI\bin\Release\net10.0\win-x64\native\NativeCompilerCLI.exe"

dotnet publish -r win-x64 -c Release
if errorlevel 1 exit /b %errorlevel%

if exist "!SIGNTOOL!" goto has_signtool
echo signtool not found at !SIGNTOOL!
exit /b 1

:has_signtool
for %%F in ("%PUBLISHED%" "%NATIVE%") do (
    if exist "%%~F" (
        "!SIGNTOOL!" sign /sha1 %THUMBPRINT% /fd SHA256 "%%~F"
        if errorlevel 1 exit /b 1
    )
)

echo Signed published exe.