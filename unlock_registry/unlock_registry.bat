@echo off

:: Path to register
set "BASE=HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\nvlddmkm\State\DisplayDatabase"
echo %BASE%

:: Output file in same folder as the BAT
set "outfile=%~dp0permissions.txt"

:: Clear or create the file
echo %BASE% [7] > "%outfile%"

:: Sets permisions for DisplayDatabase
regini.exe "%outfile%"
if %errorlevel% neq 0 (
	goto need_admin
)

:: Enumerate all subkeys under DisplayDatabase and write permissions
for /f "skip=1 tokens=*" %%K in ('reg query "%BASE%" ^| findstr "%BASE%"') do (
	echo %%K
    echo %%K [7] >> "%outfile%"
)

:: Sets permisions for subkeys
regini.exe "%outfile%"
if %errorlevel% neq 0 (
	goto need_admin
)

echo Done. Permissions applied.
pause
exit /b

:need_admin
:: First check for admin rights
net session >nul 2>&1
if %errorlevel% neq 0 (
	:: Second check for admin rights
	fsutil dirty query %systemdrive% >nul
	if %errorlevel% neq 0 (
		echo Requesting administrator privileges...
		powershell -Command "Start-Process cmd -ArgumentList '/c %~dp0%~nx0' -Verb RunAs"
		exit /b
	)
)
echo Something went wrong...
pause
