@echo off
rem Sign the Debug JIT compiler outputs that the CliComparison parity tests launch via
rem `dotnet ...NativeCompilerCLI.dll`. Smart App Control / WDAC on this machine blocks
rem unsigned binaries (see publish.bat), so every `dotnet build` re-writes these dlls
rem unsigned and they must be re-signed before `dotnet test` runs the parity suite.
set "SIGNTOOL=C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
set "THUMBPRINT=63DE8A7D5283718D453AD7CCD825BB27842AD5F0"
set "CLI=src\NativeCompilerCLI\bin\Debug\net10.0\NativeCompilerCLI.dll"
set "LIB=src\NativeCompilerCLI\bin\Debug\net10.0\NativeCompiler.dll"

if not exist "%CLI%" (
    echo NativeCompilerCLI is not built - run `dotnet build NativeCompiler.sln` first. 1>&2
    exit /b 1
)

"%SIGNTOOL%" sign /sha1 %THUMBPRINT% /fd SHA256 "%CLI%" >nul || exit /b 1
"%SIGNTOOL%" sign /sha1 %THUMBPRINT% /fd SHA256 "%LIB%" >nul || exit /b 1
echo Signed Debug compiler outputs.