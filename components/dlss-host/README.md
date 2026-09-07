# Host DLSS 4.5 para BioShock VR

Este directorio conserva el subconjunto de código utilizado por el host x64
del fork. BioShock Remastered y bioshockvr.dll son x86, mientras que NVIDIA
NGX se ejecuta en el proceso auxiliar x64.

Se deriva de
[DLSS5-Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder), de
Jean-Laurent ROUZIES, e incorpora partes procedentes de
[NIGos/dlss5-bridge](https://github.com/NIGos/dlss5-bridge), también bajo
licencia MIT. El nombre histórico
dlss5-feed-host64.cpp se mantiene por trazabilidad, pero esta versión se
compila con BVR_DLSS45_ONLY=1 y la Release Beta 0.2 solo ofrece DLSS 4.5
Super Resolution y DLAA. No incorpora DLSS 5 Neural Rendering.

## Compilación

Se necesita Visual Studio con C++ x64 y el SDK NGX de NVIDIA aportado por el
desarrollador:

    components/dlss-host/external/ngx/
      nvsdk_ngx.h
      nvsdk_ngx_helpers.h
      nvsdk_ngx_defs_dlssd.h
      libs/nvsdk_ngx_d.lib

Después:

    components\dlss-host\host\build-bvr-dlss45-host.bat

Los encabezados y librerías del SDK no se almacenan en Git. Su uso y
distribución se rigen por la licencia de NVIDIA incluida en
docs/licenses/NVIDIA-DLSS-LICENSE.txt.

El ejecutable congelado en la Release Beta 0.2 tiene SHA-256
480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453.
