# BioShock VR: prueba sintética estéreo x86 → x64

`bvr-stereo-client32.cpp` prueba el transporte que necesita el mod sin tocar BioShock:

- cliente D3D11 de 32 bits y protocolo IPC v8;
- dos hosts x64, pipes, recursos, fences e historiales completamente independientes;
- orden de envío ojo izquierdo/derecho durante 300 fotogramas por ojo;
- sincronización *same-frame* (`FEED_BUILD_ASYNC_HOME` desactivado);
- DLAA 4.5 a 640×360 o DLSS 4.5 SR a 960×540 → 1920×1080;
- lectura de cada salida: rojo en el ojo 0, azul en el ojo 1, para detectar cruces;
- verificación final de contadores, fences y logs separados.

La preparación copia el paquete limpio (`BioShockVR-DLSS45-Host64.exe`, el
`nvngx_dlss.dll` oficial y `dlss-capabilities.ini`) a
`tests/bvr-stereo-runtime/<modo>/eye0` y `eye1`. El cliente rechaza proxies ReShade,
`nvngx_dlssnr.dll` y complementos de Neural Rendering; esta prueba pertenece solo a la
fase DLSS 4.5 SR/DLAA.

Desde una consola PowerShell:

```powershell
.\tests\Run-BvrStereoBridgeTest.ps1 -Mode Both -Frames 300 `
  -PackageDir .\dist\BioShockVR-DLSS45
```

By default the runner uses `dist\BioShockVR-DLSS45`, so `-PackageDir` may be
omitted.  It stages the exact packaged host/runtime/manifest into isolated
`eye0` and `eye1` directories before every mode.

También se puede ejecutar solo `-Mode DLAA` o `-Mode SR`. Al terminar, cada carpeta de
ojo conserva su `BioShockVR-DLSS45-eyeN.log` para auditoría.

## Regresión de límites de resolución

El cliente admite `--square-output 2950`: utiliza la política real de geometría
del mod para calcular Performance (1476x1476, no 1474x1474) o DLAA (2950x2950).
Se puede pasar este argumento al ejecutable con un runtime de prueba ya preparado.
El cliente sigue sin abrir el juego ni OpenXR.

La integración también dispone del target CMake `dlss_runtime_test32`, que usa
la implementación real de `dlss45_client.cpp`, no solo el contrato IPC. Se ejecuta
de forma opt-in, con GPU NVIDIA y directorios de prueba explícitos ya creados:

```text
dlss_runtime_test32.exe bs2 <host64/BioShockVR-DLSS45-Host64.exe verificado> <directorio de datos de prueba>
```

Usar `bs1` con el perfil de host publicado de BS1 o `bs2` con su perfil identificado.
Nunca indicar la carpeta de datos personal del juego como directorio de prueba.
Verifica transiciones DLAA/NORMAL/DLSS, rechazo del tamaño antiguo 1474 y posterior
recuperación, salida de ambos ojos leída en GPU y cierre acotado de los hosts.
No se incluye en la batería automática sin GPU y no certifica el motor ni el visor.
