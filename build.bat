@echo off
REM ============================================================
REM  Echo Lifestyle ERP - build and test
REM
REM  Runs: tool restore -> restore -> build -> test
REM  Writes the full transcript to tools\build-output.txt so it
REM  can be reviewed (and read back by Claude) after the run.
REM
REM  Usage:
REM    build.bat                 (Debug, runs tests)
REM    build.bat Release         (Release, runs tests)
REM    build.bat Debug notest    (skip tests)
REM ============================================================
setlocal
set "ROOT=%~dp0"
set "LOG=%ROOT%tools\build-output.txt"

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"
set "RUNTESTS=%~2"

pushd "%ROOT%"
if not exist "tools" mkdir "tools"

set "SLN="
if exist "EchoLifestyle.slnx" set "SLN=EchoLifestyle.slnx"
if exist "EchoLifestyle.sln"  set "SLN=EchoLifestyle.sln"

echo Echo Lifestyle ERP - Build> "%LOG%"
echo Generated: %DATE% %TIME%>> "%LOG%"
echo Configuration: %CONFIG%>> "%LOG%"
echo Solution: %SLN%>> "%LOG%"
echo.>> "%LOG%"

set "FAILED="

REM A running instance holds its output assemblies open, so the build fails
REM with a wall of MSB3027 file-lock errors that look like code problems.
REM Say what is actually wrong before that happens.
tasklist /fi "imagename eq EchoLifestyle.Web.exe" 2>nul | find /i "EchoLifestyle.Web.exe" >nul
if not errorlevel 1 (
    echo *** EchoLifestyle.Web is already running. ***>> "%LOG%"
    echo Stop it first ^(Shift+F5 in Visual Studio, or close the dotnet run window^),>> "%LOG%"
    echo otherwise the build cannot overwrite its own DLLs.>> "%LOG%"
    set "FAILED=1"
    goto :report
)

if not defined SLN (
    echo *** No solution file found. Run tools\scaffold.bat first. ***>> "%LOG%"
    set "FAILED=1"
    goto :report
)

echo ===== dotnet tool restore =====>> "%LOG%"
dotnet tool restore>> "%LOG%" 2>&1
if errorlevel 1 (
    echo *** TOOL RESTORE FAILED ***>> "%LOG%"
    set "FAILED=1"
    goto :report
)
echo.>> "%LOG%"

echo ===== dotnet restore =====>> "%LOG%"
dotnet restore "%SLN%" --nologo>> "%LOG%" 2>&1
if errorlevel 1 (
    echo *** RESTORE FAILED ***>> "%LOG%"
    set "FAILED=1"
    goto :report
)
echo.>> "%LOG%"

echo ===== dotnet build =====>> "%LOG%"
dotnet build "%SLN%" -c %CONFIG% --no-restore --nologo -v m>> "%LOG%" 2>&1
if errorlevel 1 (
    echo *** BUILD FAILED ***>> "%LOG%"
    set "FAILED=1"
    goto :report
)
echo.>> "%LOG%"

if /i "%RUNTESTS%"=="notest" (
    echo ===== tests skipped by request =====>> "%LOG%"
    goto :report
)

echo ===== dotnet test =====>> "%LOG%"
dotnet test "%SLN%" -c %CONFIG% --no-build --nologo>> "%LOG%" 2>&1
if errorlevel 1 (
    echo *** TESTS FAILED ***>> "%LOG%"
    set "FAILED=1"
)
echo.>> "%LOG%"

:report
echo ===== END =====>> "%LOG%"
popd

type "%LOG%"
echo.
echo ------------------------------------------------------------
if defined FAILED (
    echo RESULT: FAILED  -  see tools\build-output.txt
) else (
    echo RESULT: SUCCESS
)
echo ------------------------------------------------------------
pause
