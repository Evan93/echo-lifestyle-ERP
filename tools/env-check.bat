@echo off
REM ============================================================
REM  Echo Lifestyle ERP - Environment Check
REM  Writes results to tools\env-check.txt for Claude to read.
REM  Safe: reads versions and service names only. Changes nothing.
REM ============================================================
setlocal enabledelayedexpansion
set "OUT=%~dp0env-check.txt"

echo Echo Lifestyle ERP - Environment Check> "%OUT%"
echo Generated: %DATE% %TIME%>> "%OUT%"
echo Machine: %COMPUTERNAME%   User: %USERNAME%>> "%OUT%"
echo.>> "%OUT%"

echo ===== dotnet --version =====>> "%OUT%"
dotnet --version>> "%OUT%" 2>&1
echo.>> "%OUT%"

echo ===== dotnet --list-sdks =====>> "%OUT%"
dotnet --list-sdks>> "%OUT%" 2>&1
echo.>> "%OUT%"

echo ===== dotnet --list-runtimes =====>> "%OUT%"
dotnet --list-runtimes>> "%OUT%" 2>&1
echo.>> "%OUT%"

echo ===== dotnet-ef tool =====>> "%OUT%"
dotnet ef --version>> "%OUT%" 2>&1
echo.>> "%OUT%"

echo ===== git --version =====>> "%OUT%"
git --version>> "%OUT%" 2>&1
echo.>> "%OUT%"

echo ===== SQL Server instance names (registry) =====>> "%OUT%"
reg query "HKLM\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL">> "%OUT%" 2>&1
echo.>> "%OUT%"

echo ===== SQL Server services =====>> "%OUT%"
sc query type= service state= all | findstr /I "SERVICE_NAME: MSSQL">> "%OUT%" 2>&1
echo.>> "%OUT%"

echo ===== LocalDB instances =====>> "%OUT%"
sqllocaldb info>> "%OUT%" 2>&1
echo.>> "%OUT%"

echo ===== sqlcmd availability =====>> "%OUT%"
where sqlcmd>> "%OUT%" 2>&1
echo.>> "%OUT%"

echo ===== NuGet sources =====>> "%OUT%"
dotnet nuget list source>> "%OUT%" 2>&1
echo.>> "%OUT%"

echo ===== Repo state =====>> "%OUT%"
pushd "%~dp0.."
git remote -v>> "%OUT%" 2>&1
git branch --show-current>> "%OUT%" 2>&1
git log --oneline -3>> "%OUT%" 2>&1
popd
echo.>> "%OUT%"

echo ===== END =====>> "%OUT%"

echo.
echo Environment check complete.
echo Results written to: %OUT%
echo.
type "%OUT%"
echo.
pause
