@echo off
setlocal
cd /d "%~dp0"
call "%~dp0..\tools\vcvars.bat" x64 || exit /b 1
cl /nologo /EHsc /W4 /WX bvr-sr-quality-test.cpp /Fe:bvr-sr-quality-test.exe
if errorlevel 1 exit /b 1
endlocal
