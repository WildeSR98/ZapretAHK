@echo off
set AHK="C:\Program Files\AutoHotkey\v2\AutoHotkey64.exe"
set ERRORS=0

echo === Checking ZapretManager.ahk ===
%AHK% /ErrorStdOut "ZapretManager.ahk" 2>tmp_err.txt
if %ERRORLEVEL% NEQ 0 (
    echo FAIL: ZapretManager.ahk
    type tmp_err.txt
    set ERRORS=1
) else (
    echo OK:   ZapretManager.ahk
)

echo === Checking lib\AdminHelper.ahk ===
%AHK% /ErrorStdOut "lib\AdminHelper.ahk" 2>tmp_err.txt
if %ERRORLEVEL% NEQ 0 (
    echo FAIL: lib\AdminHelper.ahk
    type tmp_err.txt
    set ERRORS=1
) else (
    echo OK:   lib\AdminHelper.ahk
)

echo === Checking lib\AppConfig.ahk ===
%AHK% /ErrorStdOut "lib\AppConfig.ahk" 2>tmp_err.txt
if %ERRORLEVEL% NEQ 0 (
    echo FAIL: lib\AppConfig.ahk
    type tmp_err.txt
    set ERRORS=1
) else (
    echo OK:   lib\AppConfig.ahk
)

echo === Checking lib\HttpService.ahk ===
%AHK% /ErrorStdOut "lib\HttpService.ahk" 2>tmp_err.txt
if %ERRORLEVEL% NEQ 0 (
    echo FAIL: lib\HttpService.ahk
    type tmp_err.txt
    set ERRORS=1
) else (
    echo OK:   lib\HttpService.ahk
)

echo === Checking lib\Logger.ahk ===
%AHK% /ErrorStdOut "lib\Logger.ahk" 2>tmp_err.txt
if %ERRORLEVEL% NEQ 0 (
    echo FAIL: lib\Logger.ahk
    type tmp_err.txt
    set ERRORS=1
) else (
    echo OK:   lib\Logger.ahk
)

echo === Checking lib\WinService.ahk ===
%AHK% /ErrorStdOut "lib\WinService.ahk" 2>tmp_err.txt
if %ERRORLEVEL% NEQ 0 (
    echo FAIL: lib\WinService.ahk
    type tmp_err.txt
    set ERRORS=1
) else (
    echo OK:   lib\WinService.ahk
)

del tmp_err.txt 2>nul

echo.
if %ERRORS%==0 (
    echo === ALL FILES OK - No syntax errors found ===
) else (
    echo === ERRORS FOUND - See above ===
)
