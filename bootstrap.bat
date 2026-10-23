@echo off
setlocal
cd /d "%~dp0"

echo === building test host ===
dotnet build tests\Sharp.Tests\Sharp.Tests.csproj -v q -nologo
if %errorlevel% neq 0 exit /b %errorlevel%

echo.
echo === signing test outputs (required by this machine's code-integrity policy) ===
call "%~dp0sign-test-outputs.bat"
if %errorlevel% neq 0 exit /b %errorlevel%

echo.
echo === running bootstrap pipeline test ===
dotnet test tests\Sharp.Tests\Sharp.Tests.csproj --no-build --filter "Category=Bootstrap" -l "console;verbosity=detailed"
if %errorlevel% neq 0 exit /b %errorlevel%

echo.
echo === full transcript: %TEMP%\sharp-bootstrap-last-run.txt ===
if exist "%TEMP%\sharp-bootstrap-last-run.txt" type "%TEMP%\sharp-bootstrap-last-run.txt"