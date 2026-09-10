#requires -Version 7
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repo ('artifacts\bs2-tests\path-guard-' + [guid]::NewGuid().ToString('N'))
$physical = Join-Path $testRoot 'physical'
$junction = Join-Path $testRoot 'junction'
New-Item -ItemType Directory -Path (Join-Path $physical 'child') -Force | Out-Null
# Both endpoints are fresh fixtures owned by this test, never user data.
New-Item -ItemType Junction -Path $junction -Target $physical | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$devcmd = & $vswhere -latest -products * -find 'Common7\Tools\VsDevCmd.bat' | Select-Object -First 1
if (!$devcmd) { throw 'Visual Studio C++ Build Tools not found.' }
$source = Join-Path $repo 'tools\lab_profile_path_tests.cpp'
$exe = Join-Path $testRoot 'lab-profile-path-tests.exe'
$object = Join-Path $testRoot 'lab-profile-path-tests.obj'
$command = 'call "' + $devcmd + '" -arch=x86 -host_arch=x64 >nul && cl.exe /nologo /std:c++17 /EHsc /MT /W4 /Fe:"' + $exe + '" /Fo:"' + $object + '" "' + $source + '"'
& $env:ComSpec /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw 'Path guard test compilation failed.' }
& $exe $physical $junction | Tee-Object -FilePath (Join-Path $testRoot 'results.txt')
if ($LASTEXITCODE -ne 0) { throw "Path guard tests failed; see $testRoot" }
Write-Output "Path guard tests passed: $testRoot"
