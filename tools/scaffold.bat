@echo off
REM ============================================================
REM  Echo Lifestyle ERP - Phase 1 solution scaffold
REM
REM  Creates the solution and project skeleton using YOUR SDK's
REM  templates, so package versions match your installed SDK
REM  instead of being guessed.
REM
REM  Handles both solution formats: .NET 10's default .slnx and
REM  the classic .sln.
REM
REM  Safe to re-run: every step is skipped if it already exists.
REM  Output is written to tools\scaffold-output.txt.
REM ============================================================
setlocal
set "ROOT=%~dp0.."
set "LOG=%~dp0scaffold-output.txt"
pushd "%ROOT%"

echo Echo Lifestyle ERP - Scaffold> "%LOG%"
echo Generated: %DATE% %TIME%>> "%LOG%"
echo Root: %CD%>> "%LOG%"
echo.>> "%LOG%"

echo ===== dotnet --version =====>> "%LOG%"
dotnet --version>> "%LOG%" 2>&1
if errorlevel 1 (
    echo ERROR: dotnet SDK not found on PATH.>> "%LOG%"
    goto :done
)
echo.>> "%LOG%"

REM ---------- solution ----------
echo ===== solution =====>> "%LOG%"
call :DetectSln
if not defined SLN (
    dotnet new sln -n EchoLifestyle>> "%LOG%" 2>&1
    call :DetectSln
)
if not defined SLN (
    echo ERROR: could not create or locate a solution file.>> "%LOG%"
    goto :done
)
echo Using solution file: %SLN%>> "%LOG%"
echo.>> "%LOG%"

REM ---------- source projects ----------
echo ===== projects =====>> "%LOG%"

call :NewProject classlib "EchoLifestyle.Domain"           "src\EchoLifestyle.Domain"
call :NewProject classlib "EchoLifestyle.Application"      "src\EchoLifestyle.Application"
call :NewProject classlib "EchoLifestyle.Infrastructure"   "src\EchoLifestyle.Infrastructure"
call :NewProject mvc      "EchoLifestyle.Web"              "src\EchoLifestyle.Web"
call :NewProject worker   "EchoLifestyle.Worker"           "src\EchoLifestyle.Worker"
call :NewProject xunit    "EchoLifestyle.UnitTests"        "tests\EchoLifestyle.UnitTests"
call :NewProject xunit    "EchoLifestyle.IntegrationTests" "tests\EchoLifestyle.IntegrationTests"

echo.>> "%LOG%"

REM ---------- remove template placeholder classes ----------
echo ===== cleaning template placeholders =====>> "%LOG%"
if exist "src\EchoLifestyle.Domain\Class1.cs"         del /q "src\EchoLifestyle.Domain\Class1.cs"
if exist "src\EchoLifestyle.Application\Class1.cs"    del /q "src\EchoLifestyle.Application\Class1.cs"
if exist "src\EchoLifestyle.Infrastructure\Class1.cs" del /q "src\EchoLifestyle.Infrastructure\Class1.cs"
echo done.>> "%LOG%"
echo.>> "%LOG%"

REM ---------- project references ----------
REM  Dependency direction is enforced here: Domain references nothing,
REM  so business rules can never take a dependency on EF Core or SQL.
echo ===== project references =====>> "%LOG%"
dotnet add "src\EchoLifestyle.Application"    reference "src\EchoLifestyle.Domain">> "%LOG%" 2>&1
dotnet add "src\EchoLifestyle.Infrastructure" reference "src\EchoLifestyle.Application">> "%LOG%" 2>&1
dotnet add "src\EchoLifestyle.Web"            reference "src\EchoLifestyle.Infrastructure" "src\EchoLifestyle.Application">> "%LOG%" 2>&1
dotnet add "src\EchoLifestyle.Worker"         reference "src\EchoLifestyle.Infrastructure" "src\EchoLifestyle.Application">> "%LOG%" 2>&1
dotnet add "tests\EchoLifestyle.UnitTests"        reference "src\EchoLifestyle.Domain" "src\EchoLifestyle.Application">> "%LOG%" 2>&1
dotnet add "tests\EchoLifestyle.IntegrationTests" reference "src\EchoLifestyle.Infrastructure" "src\EchoLifestyle.Web">> "%LOG%" 2>&1
echo.>> "%LOG%"

REM ---------- add projects to solution ----------
echo ===== solution membership =====>> "%LOG%"
dotnet sln "%SLN%" add "src\EchoLifestyle.Domain" "src\EchoLifestyle.Application" "src\EchoLifestyle.Infrastructure" "src\EchoLifestyle.Web" "src\EchoLifestyle.Worker" "tests\EchoLifestyle.UnitTests" "tests\EchoLifestyle.IntegrationTests">> "%LOG%" 2>&1
echo.>> "%LOG%"

REM ---------- local tool manifest (dotnet-ef pinned to the repo) ----------
echo ===== local tools =====>> "%LOG%"
if exist ".config\dotnet-tools.json" goto :toolsExist
if exist "dotnet-tools.json" goto :toolsExist
dotnet new tool-manifest>> "%LOG%" 2>&1
:toolsExist
dotnet tool install dotnet-ef>> "%LOG%" 2>&1
echo.>> "%LOG%"

REM ---------- report ----------
echo ===== resulting solution =====>> "%LOG%"
dotnet sln "%SLN%" list>> "%LOG%" 2>&1
echo.>> "%LOG%"

echo ===== END =====>> "%LOG%"

:done
popd
echo.
type "%LOG%"
echo.
echo ------------------------------------------------------------
echo Scaffold finished. Full log: tools\scaffold-output.txt
echo ------------------------------------------------------------
pause
exit /b

REM ============================================================
REM  :DetectSln - sets SLN to whichever solution file exists
REM ============================================================
:DetectSln
set "SLN="
if exist "EchoLifestyle.slnx" set "SLN=EchoLifestyle.slnx"
if exist "EchoLifestyle.sln"  set "SLN=EchoLifestyle.sln"
exit /b

REM ============================================================
REM  :NewProject  <template> <name> <output-dir>
REM  Creates the project only if the output directory is absent.
REM ============================================================
:NewProject
if exist "%~3" (
    echo [skip] %~2 already exists at %~3>> "%LOG%"
) else (
    echo [new ] %~2 ^(%~1^) -^> %~3>> "%LOG%"
    dotnet new %~1 -n %~2 -o "%~3" -f net10.0>> "%LOG%" 2>&1
)
exit /b
