# BioShock 2 DLSS/DLAA — adaptación 0.1.1-beta

## Base y alcance

Copia de trabajo independiente creada a partir del commit BS1
`1de552a` y su candidato local 0.2.2, el 7/8 de septiembre de 2026.
El repositorio y la instalación original de BioShock 1 siguen separados.
No se ha publicado esta adaptación.

La prueba inicial del MOD VR oficial 0.8.2 fue confirmada por el usuario:
VirtualDesktopXR 1.0.10, Quest 3 y estéreo funcionando. Ese resultado valida
la base VR; no acredita todavía la nueva ruta DLSS/DLAA.

## Contrato propio de BioShock 2

| Elemento | Valor |
|---|---|
| Steam | AppID 409720 |
| Ejecutable | Bioshock2HD.exe, x86 |
| SHA-256 | C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C |
| Ajustes juego | Bioshock2SP.ini y Shared.ini |
| Resolución efectiva | Shared.ini, sección SharedOptions |
| Ajustes MOD y hosts | %LOCALAPPDATA%\BioshockVR\bs2 |
| Capacidades paquete | game=bs2, adapter=bioshock2r, IPC v8 |

El núcleo genera los datos temporales del adaptador BioShock 2. La cámara,
la etiqueta de dibujo y la proyección deben pertenecer al mismo ojo y dibujo;
los datos ausentes o incoherentes provocan un respaldo sin DLSS para ese par.
Cada ojo conserva un host NGX e historial independiente.

El host x64 y NVIDIA DLSS 310.7.0.0 se reutilizan de la versión BS1: su
contrato de recursos D3D11 e IPC no depende del ejecutable del juego.
La DLL inyectada, el lanzador, los ajustes y el instalador sí se adaptan.

## Mejoras solicitadas incorporadas al alcance

- Separar instalación completada de fallo posterior al abrir el lanzador.
- Confirmar un nuevo proceso del juego y su ruta antes de cerrar el lanzador.
- Explicar qué restaura y conserva Restaurar, limpiar directorios propios
  vacíos y conservar los cambios posteriores del usuario.
- Preparar una comprobación reproducible en otro ordenador.

## Validación

[BS2-TEST-RESULTS.md](BS2-TEST-RESULTS.md) conserva los resultados del primer candidato 0.1.0-beta:
los tres modos funcionaron en la partida aislada, incluido SR 2048² → 3072²,
y el instalador y el lanzador superaron sus baterías. La versión 0.1.1 tiene
identidad propia para los cambios posteriores en el cierre y se ha vuelto a
probar localmente: cuatro cierres de ventana (plano/NORMAL/DLAA/SR), menú SR
con cancelación/reanudación y guardar, y menú DLAA sin guardar, recargando el
nuevo archivo SR. El instalador final superó 16 comprobaciones locales.
Los hashes, resultados exactos, fallos intermedios y revisión del observador
están en [BS2-EXIT-FIX.md](BS2-EXIT-FIX.md); las pruebas de juego usan la copia y
el núcleo instrumentado LAB, no el juego real del usuario.

El diagnóstico original de la AV, observada también en NORMAL, se conserva en
[BS2-EXIT-INVESTIGATION.md](BS2-EXIT-INVESTIGATION.md). La salida con código 0 de
la protección antigua no equivalía a una liberación limpia. Los pases nuevos
exigen aceptación nativa, limpieza XR completa, detach y código real 0; no
ocultan las excepciones de primer chance manejadas de la sonda diagnóstica.
El visor simulado comprueba ejecución, recursos y composición; no sustituye
la apreciación visual del usuario ni valida universalmente todos los callers
de salida o estados del motor. El visor real sigue pendiente para esta versión.
La prueba con otro ordenador requiere
acceso a otro equipo y permanece pendiente hasta realizarla.
