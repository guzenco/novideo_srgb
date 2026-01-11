@echo off

:: Check for admin rights
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator privileges...
    powershell -Command "Start-Process cmd -ArgumentList '/c %~dp0%~nx0' -Verb RunAs"
    exit /b
)

:: Path to register
set "BASE=HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\nvlddmkm\State\DisplayDatabase"
echo %BASE%

:: Output file in same folder as the BAT
set "outfile=%~dp0permissions.txt"

:: Clear or create the file
echo %BASE% [7] > "%outfile%"

:: Sets permisions for DisplayDatabase
regini.exe "%outfile%"

:: Enumerate all subkeys under DisplayDatabase and write permissions
for /f "skip=2 tokens=*" %%K in ('reg query "%BASE%" ^| findstr /r "."') do (
	echo %%K
    echo %%K [7] >> "%outfile%"
)

:: Sets permisions for subkeys
regini.exe "%outfile%"

echo Done. Permissions applied.
pause
