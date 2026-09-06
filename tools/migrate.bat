@echo off
REM ============================================================
REM  Echo Lifestyle ERP - EF Core migrations
REM
REM  Usage:
REM    tools\migrate.bat add InitialCreate   Create a migration
REM    tools\migrate.bat update              Apply migrations to the database
REM    tools\migrate.bat list                Show migrations and their status
REM    tools\migrate.bat script              Generate an idempotent SQL script
REM
REM  The connection comes from ECHO_CONNECTION if set, otherwise the default
REM  in DesignTimeDbContextFactory (localhost / EchoLifestyle).
REM
REM  Output: tools\migrate-output.txt
REM ============================================================
setlocal
set "ROOT=%~dp0.."
set "LOG=%~dp0migrate-output.txt"
set "PROJ=src\EchoLifestyle.Infrastructure"
set "CMD=%~1"
set "NAME=%~2"

pushd "%ROOT%"

echo Echo Lifestyle ERP - Migrations> "%LOG%"
echo Generated: %DATE% %TIME%>> "%LOG%"
echo Command: %CMD% %NAME%>> "%LOG%"
echo.>> "%LOG%"

dotnet tool restore>> "%LOG%" 2>&1
if errorlevel 1 (
    echo *** TOOL RESTORE FAILED ***>> "%LOG%"
    goto :report
)

if /i "%CMD%"=="add" goto :add
if /i "%CMD%"=="update" goto :update
if /i "%CMD%"=="list" goto :list
if /i "%CMD%"=="script" goto :script

echo Unknown command '%CMD%'. Use: add ^<Name^> ^| update ^| list ^| script>> "%LOG%"
goto :report

:add
if "%NAME%"=="" (
    echo ERROR: migration name required, e.g. tools\migrate.bat add InitialCreate>> "%LOG%"
    goto :report
)
echo ===== dotnet ef migrations add %NAME% =====>> "%LOG%"
dotnet ef migrations add %NAME% -p "%PROJ%" -s "%PROJ%" -o "Persistence\Migrations">> "%LOG%" 2>&1
goto :report

:update
echo ===== dotnet ef database update =====>> "%LOG%"
dotnet ef database update -p "%PROJ%" -s "%PROJ%">> "%LOG%" 2>&1
goto :report

:list
echo ===== dotnet ef migrations list =====>> "%LOG%"
dotnet ef migrations list -p "%PROJ%" -s "%PROJ%">> "%LOG%" 2>&1
goto :report

:script
echo ===== dotnet ef migrations script (idempotent) =====>> "%LOG%"
dotnet ef migrations script --idempotent -p "%PROJ%" -s "%PROJ%" -o "artifacts\migrate.sql">> "%LOG%" 2>&1
goto :report

:report
echo.>> "%LOG%"
echo ===== END =====>> "%LOG%"
popd

type "%LOG%"
echo.
pause
