# Arquitectura DLSS/DLAA

> Documento heredado de la edición **BioShock 1**. Los nombres de ejecutable,
> rutas de datos y proveedor `bioshock1r` descritos aquí no son el contrato de
> BioShock 2. Para el port BS2 y sus límites temporales, consulta
> [BS2-TEMPORAL.md](BS2-TEMPORAL.md).

La Release v0.2.0-beta mantiene el mod dentro del proceso x86 de BioShock Remastered y ejecuta NVIDIA NGX en procesos auxiliares x64. Esta separación evita cargar una biblioteca x64 dentro del juego de 32 bits.

## Flujo por ojo

    BioshockHD.exe x86
      -> xinput1_3.dll carga bioshockvr.dll
      -> BioShock VR renderiza cada ojo en D3D11
      -> se generan color, profundidad y vectores de movimiento
      -> dlss45_client.cpp publica recursos y sincronización IPC
      -> Host64 ojo izquierdo / Host64 ojo derecho
      -> NVIDIA NGX DLAA o DLSS 4.5 Super Resolution
      -> bioshockvr.dll copia cada resultado a su swapchain OpenXR
      -> compositor del visor

Cada ojo tiene proceso, recursos, historial y sincronización independientes. Nunca se reutiliza el historial temporal del ojo contrario.

## Modos publicados

| Modo | Entrada | Salida | Host NGX |
|---|---|---|---|
| NORMAL | resolución de render | misma resolución | no |
| DLAA | resolución de render | misma resolución, relación 1:1 | sí |
| DLSS | resolución inferior | resolución de salida mayor con igual proporción | sí |

La configuración pública vive en %LOCALAPPDATA%\BioshockVR\dlss.ini. El lanzador valida dimensiones pares, límites, proporción y contrato del runtime antes de guardar. En esta edición también fuerza UseFxaa=0 y desactiva cualquier upscaler.ini heredado.

## Componentes

- src/core/gfx/dlss45_client.*: cliente IPC, validación y ciclo de los dos hosts.
- src/game/bioshock1r/temporal_guides.*: profundidad y movimiento necesarios para el procesamiento temporal.
- src/core/vr/openxr_runtime.*: resolución de render/salida, envío por ojo y composición OpenXR.
- components/dlss-host: host x64 adaptado y compilado en modo BVR_DLSS45_ONLY.
- apps/launcher: configuración segura y lanzador WinForms.
- installer: empaquetado autónomo, instalación transaccional y restauración.

## Fallos y recuperación

Las dimensiones o el manifiesto de capacidades inválidos impiden activar DLSS/DLAA. Una pérdida del host invalida el frame temporal y evita presentar una mezcla de ojos o historiales. Los registros distinguen el cliente del juego y cada host por ojo.

El instalador mantiene un manifiesto local fuera del juego y una copia por archivo sustituido. Restaurar utiliza ese estado para devolver el contenido anterior, no una aproximación de la versión original.

## Fuera de alcance

- DLSS 5 Neural Rendering.
- Frame Generation o Multi Frame Generation.
- Ray Reconstruction.
- FXAA como opción pública.
- Reescalado espacial como opción pública.

Los nombres heredados que contienen dlss5 dentro del host se conservan únicamente como rastro del proyecto de origen; no describen la capacidad de esta Release.
