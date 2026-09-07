@echo off
rem Synthetic BioShock VR two-eye client.  amd64_x86 selects the MSVC x86 compiler.
cd /d "%~dp0.."
setlocal
call "%~dp0..\tools\vcvars.bat" amd64_x86 || exit /b 1
cl /nologo /O2 /EHsc /W4 /std:c++17 tests\bvr-stereo-client32.cpp ^
   /Fe:tests\bvr-stereo-client32.exe d3d11.lib dxgi.lib
if errorlevel 1 exit /b 1
endlocal
echo BioShock VR stereo client built (x86).
