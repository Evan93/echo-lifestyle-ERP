@echo off
REM ============================================================
REM  Echo Lifestyle ERP - add NuGet packages
REM
REM  Versions are deliberately NOT pinned here: NuGet resolves the
REM  latest stable release compatible with net10.0, so the versions
REM  match the SDK actually installed rather than a guess.
REM  Once restored, the exact versions are recorded in the csproj
REM  files and committed to git.
REM
REM  Safe to re-run.
REM  Output: tools\add-packages-output.txt
REM ============================================================
setlocal
set "ROOT=%~dp0.."
set "LOG=%~dp0add-packages-output.txt"
pushd "%ROOT%"

echo Echo Lifestyle ERP - Add packages> "%LOG%"
echo Generated: %DATE% %TIME%>> "%LOG%"
echo.>> "%LOG%"

echo ===== Application =====>> "%LOG%"
REM EF Core abstractions only - so feature services can compose LINQ against
REM IApplicationDbContext. No provider here: Application never knows it is SQL Server.
dotnet add "src\EchoLifestyle.Application" package Microsoft.EntityFrameworkCore>> "%LOG%" 2>&1
echo.>> "%LOG%"

echo ===== Infrastructure =====>> "%LOG%"
dotnet add "src\EchoLifestyle.Infrastructure" package Microsoft.EntityFrameworkCore.SqlServer>> "%LOG%" 2>&1
dotnet add "src\EchoLifestyle.Infrastructure" package Microsoft.EntityFrameworkCore.Design>> "%LOG%" 2>&1
dotnet add "src\EchoLifestyle.Infrastructure" package Microsoft.AspNetCore.Identity.EntityFrameworkCore>> "%LOG%" 2>&1
echo.>> "%LOG%"

echo ===== Web =====>> "%LOG%"
dotnet add "src\EchoLifestyle.Web" package Microsoft.EntityFrameworkCore.Design>> "%LOG%" 2>&1
dotnet add "src\EchoLifestyle.Web" package Microsoft.AspNetCore.Identity.UI>> "%LOG%" 2>&1
dotnet add "src\EchoLifestyle.Web" package Serilog.AspNetCore>> "%LOG%" 2>&1
dotnet add "src\EchoLifestyle.Web" package Serilog.Sinks.File>> "%LOG%" 2>&1
echo.>> "%LOG%"

echo ===== Integration tests =====>> "%LOG%"
dotnet add "tests\EchoLifestyle.IntegrationTests" package Microsoft.AspNetCore.Mvc.Testing>> "%LOG%" 2>&1
echo.>> "%LOG%"

echo ===== resulting package references =====>> "%LOG%"
dotnet list "src\EchoLifestyle.Infrastructure" package>> "%LOG%" 2>&1
dotnet list "src\EchoLifestyle.Web" package>> "%LOG%" 2>&1
dotnet list "tests\EchoLifestyle.IntegrationTests" package>> "%LOG%" 2>&1
echo.>> "%LOG%"

echo ===== END =====>> "%LOG%"
popd

type "%LOG%"
echo.
echo ------------------------------------------------------------
echo Package installation finished. Log: tools\add-packages-output.txt
echo ------------------------------------------------------------
pause
