@echo off
rem Dedicated x64 BioShock VR DLSS 4.5 SR/DLAA host.
rem BVR_DLSS45_ONLY makes every invocation without --eye fail before legacy init.
cd /d "%~dp0"
setlocal
call "%~dp0..\tools\vcvars.bat" x64 || exit /b 1
rc /nologo /fo BioShockVR-DLSS45-Host64.res bvr-dlss45-host.rc
if errorlevel 1 exit /b 1
rem NVIDIA's bundled nvsdk_ngx_d.lib is built /MD. Mixing it with /MT triggers
rem LNK2038 and duplicate CRT state, so the supported RC build must also use /MD.
cl /nologo /O2 /Gy /Gw /EHsc /W3 /MD /DBVR_DLSS45_ONLY=1 /I..\external\ngx dlss5-feed-host64.cpp ^
   /Fo:BioShockVR-DLSS45-Host64.obj /Fe:BioShockVR-DLSS45-Host64.tmp.exe ^
   /link /OPT:REF /OPT:ICF BioShockVR-DLSS45-Host64.res ..\external\ngx\libs\nvsdk_ngx_d.lib version.lib winmm.lib kernel32.lib user32.lib gdi32.lib advapi32.lib ole32.lib
if errorlevel 1 exit /b 1
move /y BioShockVR-DLSS45-Host64.tmp.exe BioShockVR-DLSS45-Host64.exe >nul
if errorlevel 1 exit /b 1
endlocal
echo BioShock VR DLSS 4.5 Complemento v1.0.0-rc.1 - By Beren5556 built.
