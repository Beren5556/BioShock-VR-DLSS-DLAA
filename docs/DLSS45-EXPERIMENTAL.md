# DLSS 4.5 experimental

Esta fase integra el runtime oficial NVIDIA DLSS Super Resolution 310.7.0.0
(DLSS 4.5) con BioShock Remastered VR. Incluye dos modos reales:

- `dlaa`: resolución de render y salida iguales; DLSS se usa como antialiasing.
- `sr`: render interno menor y salida OpenXR mayor, con la misma relación de aspecto.

No contiene DLSS 5, Neural Rendering, RenoDX ni un complemento ReShade. El
reescalado espacial del mod es una alternativa distinta y nunca se etiqueta como
DLSS o DLAA.

## Arquitectura

BioShock Remastered y el mod son procesos x86, mientras que NVIDIA NGX es x64.
El mod inicia dos ayudantes x64 aislados, uno por ojo. Cada ojo tiene sus propias
texturas compartidas, fences e historial temporal; los fotogramas se procesan de
forma síncrona para impedir que una imagen izquierda llegue al ojo derecho.

Archivos requeridos junto al mod:

```
host64\BioShockVR-DLSS45-Host64.exe
host64\nvngx_dlss.dll
host64\dlss-capabilities.ini
```

El manifiesto debe declarar exactamente `phase=DLSS45`, `eyeHosts=2`,
`runtime=310.7.0` y `protocol=8`. El cliente también valida que
`nvngx_dlss.dll` sea x64 y tenga FileVersion `310.7.0.0`.

## Configuración

La lanzadera escribe `%LOCALAPPDATA%\BioshockVR\dlss.ini` de forma transaccional
y guarda una copia de seguridad antes de reemplazarlo. El ejemplo distribuido
queda desactivado de forma predeterminada.

Para DLAA, `outputWidth` y `outputHeight` deben ser idénticos a la resolución de
render de BioShock. Para SR, ambos deben ser mayores y mantener exactamente la
misma relación de aspecto. La lanzadera calcula y muestra la calidad que se
deduce de esa relación.

DLSS y el reescalado espacial no pueden habilitarse simultáneamente. En menús,
cinemáticas o fotogramas sin guías temporales válidas, el modo SR puede usar el
filtro espacial solamente como salida de seguridad para conservar el tamaño de
la swapchain; eso no convierte ese fotograma en DLSS.

## Límites de esta primera versión

- Los vectores de movimiento se reconstruyen de la cámara y la profundidad.
  Las manos y objetos animados todavía no tienen vectores propios y pueden dejar
  estela.
- La proyección del juego aún no recibe jitter temporal. El cliente envía jitter
  cero en vez de inventar un desplazamiento que no existe en la imagen.
- El FOV, el plano cercano y la profundidad invertida deben corresponder a la
  proyección real de BioShock. `nearPlaneUu` es un ajuste avanzado conservador.
- La validación final de comodidad y artefactos requiere una prueba dentro del
  visor; las pruebas automáticas solo validan recursos, sincronización y salida.

Los dos hosts escriben registros separados en
`%LOCALAPPDATA%\BioshockVR\BioShockVR-DLSS45-eye0.log` y `eye1.log`.
