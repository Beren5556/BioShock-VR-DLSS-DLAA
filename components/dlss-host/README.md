# BioShock VR DLSS 4.5 host

This directory preserves the code subset used by the fork's x64 host.
BioShock Remastered and bioshockvr.dll are x86, while NVIDIA NGX runs in
a separate x64 helper process.

It derives from [DLSS5-Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder)
by Jean-Laurent ROUZIES and includes portions from
[NIGos/dlss5-bridge](https://github.com/NIGos/dlss5-bridge), also under MIT.
The historical filename `dlss5-feed-host64.cpp` is retained for provenance.
This distribution is built with `BVR_DLSS45_ONLY=1` and offers only DLSS
4.5 Super Resolution and DLAA, not DLSS 5 Neural Rendering.

## Build

Requires Visual Studio C++ x64 and a developer-supplied NVIDIA NGX SDK:

    components/dlss-host/external/ngx/
      nvsdk_ngx.h
      nvsdk_ngx_helpers.h
      nvsdk_ngx_defs_dlssd.h
      libs/nvsdk_ngx_d.lib

Then run from an x64 tools environment:

    components\dlss-host\host\build-bvr-dlss45-host.bat

SDK headers and libraries are not stored in Git. Their use and distribution
are governed by the NVIDIA license in
`docs/licenses/NVIDIA-DLSS-LICENSE.txt`.

The frozen host originally distributed with beta 0.2.3 is also reused by the
0.2.16 base and 0.2.17 English edition, with SHA-256:

`480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453`.

The English edition does not rebuild or alter this host.
