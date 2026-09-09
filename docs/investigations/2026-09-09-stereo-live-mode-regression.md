# Pérdida de estéreo tras cambio de modo — 9 septiembre 2026

Corregido en el código de 0.2.11 y pendiente de probar en visor. La revisión
de interfaz de 0.2.10 se entregó por separado; no incluía esta solución.

## Evidencia conservada

`artifacts/stereo-regression-20260909-225743/bioshockvr.log` y `dlss.ini`.
DLL instalada comprobada por SHA-256:
`FDCF823158C871E040C036A1ED2AF4D24B30A486227F0BFF5377411AE9B85952`.
El inicio del log identifica 0.2.10, build `beren5556-v0.2.10-msi-steps`.
No se ha modificado la instalación ni la configuración del usuario.

## Secuencia observada

- 22:56:54.488: NORMAL → DLAA, salida 3428 × 3428; empieza la reconstrucción.
- 22:56:55.972: ambos hosts listos; cambio confirmado. Sigue habiendo
  construcciones L/R tras la reconstrucción.
- 22:56:57.418: el watchdog de reentrada detecta 300 ms sin progreso.
- 22:56:58.321: desactiva el estéreo por su umbral de 1,2 segundos.
- 22:56:58.895: el puente informa timeout del ojo 1, fotograma 54.
- 22:57:01.895: cierre de helpers agota 3000 ms y termina sus procesos propios.
- Desde 22:57:03: vuelve el progreso a 72 presents/s, pero `2nd=0/s` y nunca
  aparece `stereo RE-ARMED`. Cambiar de nuevo de modo no recupera el segundo ojo.

## Diferencia respecto al fallo antiguo

La protección `ReconfigureWindow` sigue presente en la DLL actual y cubre
la reconstrucción. Esta vez el timeout ocurre DESPUÉS de `APPLIED`, ya en
evaluación DLAA. No es evidencia de que se haya eliminado el arreglo anterior.

Hipótesis a comprobar: espera acotada del puente confundida con bloqueo del
motor, seguida de una recuperación demasiado dependiente de muestrear
`g_activeDepth == 0` durante cinco ticks. No basta ampliar la ventana de
reconstrucción ni forzar estéreo a ciegas: conservar la intención del usuario,
la recuperación de fallos genuinos y la separación de ambos ojos.

## Corrección implementada en 0.2.11

- `BoundedActivity` describe las esperas ya limitadas de IPC, fence de salida
  y cierre de helpers. Los dos watchdogs consultan esa actividad, incluso
  cuando el puente deja de estar listo por un fallo. No se cambian los tiempos
  de espera, el protocolo ni el orden de entrega de los ojos. Un ámbito
  anidado no renueva el plazo y un bloqueo más allá del límite vuelve a ser visible.
- El watchdog ya no intenta reactivar estéreo desde su hilo muestreando
  `g_activeDepth == 0`. `BuildDetour` lo recupera antes de etiquetar el ojo
  izquierdo, en un nuevo build de gameplay y con al menos 500 ms de progreso.
- Solo se recupera un apagado automático: se exige intención de estéreo activa,
  motor no envenenado, renderizado inline, hooks activos, gameplay y CalcView
  recientes. Un OFF explícito cancela esa intención aunque el watchdog ya hubiera
  apagado estéreo. No se modifican cámara, configuración ni opciones de imagen.
- Pruebas puras: 40/40. Cliente D3D11/WARP: 53/53, incluida espera real de
  1,4 segundos que antes superaba el umbral de apagado; salida fallida del helper
  sin espera GPU insatisfecha y sin mezclar ojos. Cero avisos D3D11 de depuración.

Límite: el test no reproduce NVIDIA ni el visor. El origen del timeout del host
no está demostrado y no se afirma que todos esos timeouts desaparezcan.
Se corrige la conversión de ese fallo recuperable en pérdida persistente de 3D.
