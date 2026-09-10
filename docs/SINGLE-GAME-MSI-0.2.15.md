# Instalador BioShock 1–2 · 0.2.15

Revisión solicitada por Carlos el 10/09/2026. No cambia ninguno de los 46
archivos de los mods ni recompila sus núcleos o lanzadores. Conserva la
[arquitectura MSI por juego](SINGLE-GAME-MSI-0.2.14.md).

## Cambios de interfaz acordados

- Primera pantalla compacta: «Selecciona el juego», desplegable y botones.
  Sin la explicación superior ni el párrafo sobre gestionar el otro juego.
- «BioShock Remastered» sin el sufijo «(1)».
- Confirmación sin el párrafo «El otro juego no se modifica. El instalador
  no abrirá automáticamente el lanzador ni el juego al terminar».

Solo se retiran los textos indicados, sin cambiar el comportamiento. No se
inicia automáticamente el juego o el lanzador después de instalar.

## Fallo real corregido

El intento del 2 con 0.2.14 terminó con **1603**, no con éxito, a las 17:53:20.
El usuario pudo cerrar el asistente, pero la instancia 0.2.14 del 2 no quedó
registrada. El rechazo sucedió durante la lectura de la beta, antes de retirar
sus archivos. Registro local: `BioshockVR/bs2/WindowsInstaller/Logs/bs2-20260910-155240.log`.

La beta real registra `BioShock 2 VR DLSS-DLAA Beta.lnk`; el lector anterior
solo aceptaba `BioShock 2 VR DLSS-DLAA.lnk`. Los fixtures anteriores utilizaban
ese segundo nombre y no cubrían el primero. Por eso las pruebas previas no
detectaron el caso real comunicado por Carlos.

Se aceptan ahora **solo esos dos nombres exactos**, dentro del escritorio
esperado. La ruta «Beta» tiene su propio identificador `@beta-shortcut` en las
copias de recuperación; nunca se confunde con el acceso normal o el nuevo
acceso versionado. Se mantienen las comprobaciones de inventario, hashes,
carpetas, enlaces y originales. No se amplía la autorización a otros archivos.

La validación de la beta se ejecuta también antes de `InstallInitialize`,
sin modificar archivos o registrar componentes. Se repite en la copia de
seguridad para detectar cambios entre la validación y la instalación.

El nuevo lector validó los **21 registros reales** y el acceso «Beta» mediante
una inspección de solo lectura; no reescribió el manifiesto del usuario.

## Verificación

- 40 comprobaciones unitarias de migración: nombres permitidos, ubicación,
  archivos ajenos, originales y recuperación diferenciada del acceso «Beta».
- Integración MSI nativa aislada: instalación/recuperación normal y migración
  de la beta con y sin un acceso previo a ella. Se simulan fallos después de
  retirar archivos, después de copiarlos y durante la desinstalación.
- Extracción y SHA-256 de los 46 archivos del MSI final; comparación de sus
  46 identificadores históricos con los MSI de origen: PASS.
- Auditoría de los textos y controles dentro del MSI final: PASS.

Familia de pruebas: `eed8abaf4d2145029f4bf067b5a9f1ab`. Informes conservados
en `artifacts/single-game-isolated/<familia>/single-game-0.2.15/`.
Resultado final: **204 comprobaciones de integración en 30 operaciones MSI,
todas PASS**, además de las 40 comprobaciones unitarias de migración.
Los informes son `test-result.json` (74/12),
`test-result-standalone-beta-new.json` (65/9) y
`test-result-standalone-beta-original.json` (65/9).
SHA-256 del paquete aislado de la misma fuente:
`745BBE52CFD62F5AAABFAD4BF25902228CC25B95A5D02CB9CDEE3AEFCA88CB40`.
Las pruebas tienen rutas, perfiles y registros privados. No copian los juegos
completos ni ejecutan sus procesos. Los archivos y registros reales se comprueban
antes y después; no se instala el mod en los juegos del usuario al entregar.

La captura de la vista previa no permitió inspeccionar la ventana prevista
porque había otra aplicación en primer plano; no se operó esa aplicación.
Los textos, la geometría de los controles y el contenido del MSI se verificaron
sin instalarlo. La prueba visual final del usuario sigue pendiente.

## Entrega

Archivo: `BioShock-1-2-VR-DLSS-DLAA-0.2.15-CANDIDATO.msi`.

SHA-256: `4776D6F209512CB22213C58006677733F0A7D0DD8E699339EFFB55117DBB5BAC`.

Destino: **Escritorio / Lanzadores MOD VR**. Los lanzadores siguen instalándose
en `Build/Final` de su juego, con acceso opcional en el escritorio.
No se sobrescriben los MSI 0.2.13/0.2.14. No hay publicación en GitHub.
La validación del instalador en otro ordenador y las pruebas VR siguen pendientes.
