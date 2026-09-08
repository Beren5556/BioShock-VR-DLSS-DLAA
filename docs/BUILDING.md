# Compilación

## Requisitos

- Windows 10 u 11 de 64 bits.
- Git con soporte de submódulos.
- Visual Studio 2022 Build Tools con C++ para escritorio, MSVC x86/x64 y herramientas CMake.
- .NET Framework 4.x incluido en Windows para compilar las aplicaciones WinForms.
- Para el host: SDK NVIDIA NGX obtenido por el desarrollador y sujeto a su propia licencia.

## Obtener el código

    git clone --recursive https://github.com/Beren5556/BioShock-VR-DLSS-DLAA.git
    cd BioShock-VR-DLSS-DLAA

Si el repositorio ya estaba clonado:

    git submodule update --init --recursive

## Mod x86

BioShock Remastered es x86. El script localiza el CMake incluido con Visual Studio:

    .\tools\build.ps1 -Release

No utilices una configuración x64 para bioshockvr.dll ni xinput1_3.dll.

## Lanzador

    .\apps\launcher\Build-Launcher.ps1
    .\artifacts\launcher\Lanzador BioShock VR DLSS-DLAA.exe --self-test

El ejecutable se genera en artifacts/launcher, directorio excluido de Git.

## Host DLSS x64

Coloca localmente las cabeceras y la biblioteca NGX esperadas bajo:

    components\dlss-host\external\ngx\
      nvsdk_ngx.h
      nvsdk_ngx_helpers.h
      nvsdk_ngx_defs_dlssd.h
      libs\nvsdk_ngx_d.lib

Después abre un entorno de herramientas de Visual Studio x64 y ejecuta:

    components\dlss-host\host\build-bvr-dlss45-host.bat

El script define BVR_DLSS45_ONLY=1. No añadas el SDK, la biblioteca de importación ni el ejecutable resultante a Git.

## Instalador autónomo

El instalador depende de ocho binarios congelados y verificados en installer/payload-manifest.json. Impórtalos desde una carpeta local autorizada:

    .\installer\Import-Local-Payload.ps1 -PayloadDirectory C:\ruta\al\payload-validado
    .\installer\Build-Installer.ps1

El resultado aparece en artifacts/release. El script rechaza cualquier payload cuyo hash no coincida con la versión.

El artefacto de distribución es el adjunto a la Release correspondiente, no una compilación local no firmada ni verificada. Consulta docs/TESTING.md antes de distribuir una reconstrucción.
