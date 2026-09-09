@echo off
rem Separate local diagnostic executable; never overwrites the released helper.
setlocal
cd /d "%~dp0"
rem Optional explicit SDK include directory and x64 import library.
set "BVR_LATENCY_NGX_INCLUDE=%~1"
set "BVR_LATENCY_NGX_LIB=%~2"
if "%BVR_LATENCY_NGX_INCLUDE%"=="" set "BVR_LATENCY_NGX_INCLUDE=..\external\ngx"
if "%BVR_LATENCY_NGX_LIB%"=="" set "BVR_LATENCY_NGX_LIB=..\external\ngx\libs\nvsdk_ngx_d.lib"
call "%~dp0..\tools\vcvars.bat" x64 || exit /b 1
rc /nologo /fo BioShockVR-DLSS45-Host64-Latency1.res bvr-dlss45-host.rc
if errorlevel 1 exit /b 1
cl /nologo /O2 /Gy /Gw /EHsc /W3 /MD /DBVR_DLSS45_ONLY=1 /DBVR_LATENCY_PROBE=1 /I"%BVR_LATENCY_NGX_INCLUDE%" dlss5-feed-host64.cpp ^
   /Fo:BioShockVR-DLSS45-Host64-Latency1.obj /Fe:BioShockVR-DLSS45-Host64-Latency1.exe ^
   /link /OPT:REF /OPT:ICF BioShockVR-DLSS45-Host64-Latency1.res "%BVR_LATENCY_NGX_LIB%" version.lib winmm.lib kernel32.lib user32.lib gdi32.lib advapi32.lib ole32.lib
if errorlevel 1 exit /b 1
endlocal
