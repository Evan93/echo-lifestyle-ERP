@echo off
REM ============================================================
REM  Echo Lifestyle ERP - vendor client libraries
REM
REM  Downloads DataTables and Alpine into wwwroot/lib and commits
REM  them to the repository, so the application never depends on a
REM  CDN at runtime and builds are reproducible offline.
REM
REM  Versions are pinned to a MAJOR version in the jsDelivr URL, so
REM  re-running picks up patches without guessing exact numbers.
REM
REM  Safe to re-run. Output: tools\get-client-libs-output.txt
REM ============================================================
setlocal
set "ROOT=%~dp0.."
set "LOG=%~dp0get-client-libs-output.txt"
set "LIB=%ROOT%\src\EchoLifestyle.Web\wwwroot\lib"

pushd "%ROOT%"

echo Echo Lifestyle ERP - client libraries> "%LOG%"
echo Generated: %DATE% %TIME%>> "%LOG%"
echo.>> "%LOG%"

where curl >nul 2>&1
if errorlevel 1 (
    echo ERROR: curl.exe not found. It ships with Windows 10 1803 and later.>> "%LOG%"
    goto :done
)

if not exist "%LIB%\datatables\js" mkdir "%LIB%\datatables\js"
if not exist "%LIB%\datatables\css" mkdir "%LIB%\datatables\css"
if not exist "%LIB%\alpinejs" mkdir "%LIB%\alpinejs"

call :Fetch "https://cdn.jsdelivr.net/npm/datatables.net@2/js/dataTables.min.js" "%LIB%\datatables\js\dataTables.min.js"
call :Fetch "https://cdn.jsdelivr.net/npm/datatables.net-bs5@2/js/dataTables.bootstrap5.min.js" "%LIB%\datatables\js\dataTables.bootstrap5.min.js"
call :Fetch "https://cdn.jsdelivr.net/npm/datatables.net-bs5@2/css/dataTables.bootstrap5.min.css" "%LIB%\datatables\css\dataTables.bootstrap5.min.css"
call :Fetch "https://cdn.jsdelivr.net/npm/alpinejs@3/dist/cdn.min.js" "%LIB%\alpinejs\alpine.min.js"

echo.>> "%LOG%"
echo ===== resulting files =====>> "%LOG%"
dir /s /b "%LIB%\datatables" "%LIB%\alpinejs">> "%LOG%" 2>&1

:done
echo.>> "%LOG%"
echo ===== END =====>> "%LOG%"
popd

type "%LOG%"
echo.
pause
exit /b

REM ============================================================
REM  :Fetch <url> <destination>
REM ============================================================
:Fetch
echo Downloading %~1>> "%LOG%"
curl -sSL --fail -o "%~2" "%~1">> "%LOG%" 2>&1
if errorlevel 1 (
    echo   *** FAILED ***>> "%LOG%"
) else (
    for %%A in ("%~2") do echo   saved %%~nxA ^(%%~zA bytes^)>> "%LOG%"
)
exit /b
