# BioShock 2 DLSS/DLAA 0.1.0-beta — resultados locales

Fecha: 8 de septiembre de 2026, madrugada (Europe/Madrid).
Estado: candidato local para prueba en visor; no publicado ni instalado sobre
el juego original. No se considera validado en otro ordenador.

## Artefactos congelados

| Componente | SHA-256 |
|---|---|
| Instalador 0.1.0-beta | A61BCCE385C0095645C67FE27A937E0D2B661E3C8CE2805F401C273EA73D9D6D |
| Lanzador BS2 | 613772D164550745AB3A58924CF8A156EBCB95DE312869D01166361CA4A0EF8D |
| bioshockvr.dll de producción | 8DFD11347E8B80623CC3678BD030BD9B77B76FACA9BEF3766C1E66E224D0554A |

El instalador contiene 21 recursos: ocho entradas de payload y trece documentos
de uso/licencias. El proxy v0.8.2 original, host x64 y NVIDIA 310.7.0.0 están
identificados en `installer/payload-manifest.json`. El ejecutable del juego
no forma parte del paquete.

## Pruebas de aplicaciones y empaquetado

- Compilación x86 de producción y de laboratorio: PASS.
- WARP del contrato temporal BS2 actualizado: PASS (incluye identidad de pose,
  planos finitos, captura requerida, historiales, resets y casos inválidos).
- Lanzador `--self-test`: PASS; incluye parsers, resoluciones, valores BS2,
  detección de cambios externos, backups/rollback y máquina de estados de arranque.
- Helper real llamado `Bioshock2HD.exe`: exige ruta exacta, proceso nuevo y
  ventana respondiendo durante tres segundos; no se ejecutó el juego real en
  esta batería. Timeout, salida temprana y proceso antiguo no se aceptan.
- QA `--self-test-ui`: PASS con INI ficticios, Shared.ini 800×600 autoritativo,
  guardado/espejo 2048×2048, ocho armas, manos y claves desconocidas conservadas.
- Inspección visual nativa del lanzador y del instalador: textos y controles
  legibles; ambas aplicaciones cerradas después, sin instalar desde su UI.
- Instalador: 13 grupos PASS, 21 recursos verificados y 21 archivos originales
  distintos restaurados byte a byte. Incluye reparación, conflictos preservados,
  copias corruptas, manifiesto BS1, acceso directo aislado, fallo tras seis
  escrituras y separación instalación/apertura del lanzador.
- Verificador de repositorio: PASS; inspecciona los recursos realmente
  embebidos y rechaza la DLL real de laboratorio. En PowerShell 7 delega esta
  lectura a Windows PowerShell 5.1 por la API de metadatos .NET Framework.
- Guardas de rutas LAB: 19 casos PASS, incluidos escape `..`, ADS, relativas,
  junction real en hoja y junction en un antecesor.
- Entradas heredadas `tools/install`, `uninstall` y `package`: deshabilitadas y
  comprobadas para fallar antes de escribir, evitando el inventario/rutas BS1.

Evidencia: `artifacts/tests/installer/summary.json`,
`artifacts/launcher-tests/final-ui-f21f47d92e4d442497dc018bcdebfc5f`,
`scripts/Test-BS2-LabPathGuard.ps1` y `src/tools/temporal_guides_bs2_test`.

## Integración con partida y OpenXR simulado

Juego compatible: `Bioshock2HD.exe` x86, SHA-256
`C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C`.
Equipo local con RTX 4090; runtime de prueba `bvr-xrsim`. No se cambió el
runtime OpenXR del registro. El simulador fue validado antes con xr_hello32.

Se usa una copia física del juego y de las partidas, sin enlaces a originales,
con perfil, INI, registros y hosts privados por ejecución. El núcleo de prueba
tiene el mismo código funcional de render y una variante de aislamiento de
rutas; **no es el binario distribuido**. Su SHA-256 es
`A32EABCE3EE62A2EC502875AF45A21D19857E215A5630DB675A2B7D8A503E4F6`.
El proxy de laboratorio tampoco se empaqueta.

| Modo | Render → salida por ojo | Ejecución | Resultado de partida |
|---|---|---|---|
| NORMAL | 1024² → 1024² | off-57e9251e | Estéreo y giro; ningún host DLSS abierto |
| DLAA | 1024² → 1024² | dlaa-f0d5823b | 6357 imágenes por ojo; giro, pausa y reanudación |
| DLSS SR | 1024² → 1536² | sr-7590efc3 | 9468 imágenes por ojo y giro |
| DLSS SR alta | 2048² → 3072² | sr-e171beb2 | Al menos 7682 imágenes por ojo y giro |

En los tramos jugables estables de DLAA/SR: `coherent=1`, `hist=1`,
`reject=none`, `pubs=1`, `projPubs=1`, `nearFar=10/65536`, `mixed=0`, `gap=0`.
La proyección WORLD requiere el FPlayerSceneNode raíz de Draw y la misma pose
publicada para ese ojo/dibujo. No se aceptan foreground ni nodos auxiliares.

Los menús utilizan respaldo sin DLSS cuando faltan datos temporales; no son
un fallo de inicialización NGX. Las etiquetas incompletas de transición se
rechazan: una al cargar y otra al reanudar en DLAA, sin incremento recurrente
durante los tramos estables. La pausa pasa a un único quad y la reanudación
recupera dos vistas de proyección, historial y un nuevo epoch (8 → 10).

Se conservaron capturas izquierda, derecha, SBS y JSON de escena/giro por modo.
La inspección confirma imagen de juego en ambos ojos, no una valoración de
nitidez, estelas o comodidad dentro de unas gafas reales.

La pasada alta no acredita 90 Hz sostenidos: los últimos tramos del registro
marcan aproximadamente 71–73 pares por segundo. No se cambió la resolución
del usuario ni se presenta esa combinación como un ajuste óptimo universal.

Los recibos `artifacts/bs2-tests/game-copy.json` y `latest-run.json` localizan
los registros privados bajo `D:\BioShock2VR-DLSS-Lab\game-<GUID>\runs`.
Se conservan para reproducir las pruebas; la copia ocupa aproximadamente 21 GB.

## Incidencia observada al cerrar: pendiente

Todos los procesos respondieron a WM_CLOSE y terminaron sin quedar juegos ni
hosts abiertos. Sin embargo, los registros muestran una AV `0xC0000005` durante
el teardown, también en NORMAL sin NGX. Direcciones observadas: `+0x4FF0FE`
(DLAA), `+0xC312D2` (NORMAL/SR) y `+0xC37362` (SR alta).

Upstream documentó fallos de salida antes de esta adaptación; su manejador
genérico de teardown termina el proceso con código 0 y sin dump. Por tanto,
**exit code 0 no demuestra un cierre sin errores**, ni ese mensaje genérico
determina por sí solo la causa. No se ha corregido ni ocultado esta incidencia.
La evidencia técnica y sus límites están en [BS2-TEMPORAL.md](BS2-TEMPORAL.md).

## Conservación y pendientes

- Huella de 30 archivos originales (configuraciones BS1/BS2, partidas BS2,
  ejecutable y DLL del juego real): idéntica antes y después.
- El juego real conserva el mod VR oficial 0.8.2 y la configuración que el
  usuario probó satisfactoriamente. No se ha desplegado el candidato encima.
- No se modificó ni regeneró el paquete de BioShock 1; tampoco se publicó nada.
- Pendiente: instalar el candidato y comparar NORMAL/DLAA/DLSS en el visor,
  incluidas animaciones, HUD, cargas, cinemáticas y salida desde el menú.
- Pendiente: recorrido completo del instalador/lanzador en otro ordenador.
- Limitaciones conocidas: vectores solo de cámara y jitter cero; no hay
  vectores propios para objetos/manos animados, ni DLSS 5/Frame Generation.

Conclusión: motor, lanzador e instalador preparados y comprobados técnicamente
en este equipo. Es una beta para aceptación en visor, no una versión final
validada universalmente.
