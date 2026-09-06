@echo off
REM ============================================================
REM  Echo Lifestyle ERP - remove leftover template placeholder files
REM
REM  The xUnit templates generate UnitTest1.cs with a placeholder test.
REM  Run once after scaffolding. Safe to re-run.
REM ============================================================
setlocal
pushd "%~dp0.."

if exist "tests\EchoLifestyle.UnitTests\UnitTest1.cs" (
    del /q "tests\EchoLifestyle.UnitTests\UnitTest1.cs"
    echo Removed tests\EchoLifestyle.UnitTests\UnitTest1.cs
) else (
    echo tests\EchoLifestyle.UnitTests\UnitTest1.cs - already gone
)

if exist "tests\EchoLifestyle.IntegrationTests\UnitTest1.cs" (
    del /q "tests\EchoLifestyle.IntegrationTests\UnitTest1.cs"
    echo Removed tests\EchoLifestyle.IntegrationTests\UnitTest1.cs
) else (
    echo tests\EchoLifestyle.IntegrationTests\UnitTest1.cs - already gone
)

REM The MVC template's sample pages are replaced by the back-office shell.
if exist "src\EchoLifestyle.Web\Views\Home\Privacy.cshtml" (
    del /q "src\EchoLifestyle.Web\Views\Home\Privacy.cshtml"
    echo Removed src\EchoLifestyle.Web\Views\Home\Privacy.cshtml
)

popd
echo.
echo Cleanup complete.
pause
