# Distribución · BioShock 1–2 VR DLSS/DLAA · 0.2.16

El usuario confirmó el funcionamiento de BioShock 2 y, después, BioShock 1,
y autorizó cerrar una distribución con ambos mods. Posteriormente, el
10/09/2026, aprobó expresamente publicarla en GitHub. La
[publicación 0.2.16](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/releases/tag/v0.2.16)
utiliza el mismo MSI cerrado, sin regenerar el instalador ni sus binarios.
Las [notas públicas](releases/v0.2.16-public.md) resumen instalación y novedades.

## Artefacto de distribución

- `BioShock-1-2-VR-DLSS-DLAA-0.2.16.msi`: un MSI nativo con desplegable individual.
- 23 archivos por juego, 46 en total; cada ejecución modifica solo el elegido.
- Núcleo compartido y lanzadores exactamente iguales a los probados: versión
  interna 0.2.13, identidad `beren5556-bs12-v0.2.13-candidate2-sr-range-audit`.
- Correcciones del rango DLSS y del perfil de capacidades BS2 incluidas.
- Guía instalada y notas de rendimiento actualizadas; licencias conservadas.
- Lanzadores en `Build\Final`, accesos opcionales, sin arranque automático.

SHA-256 del MSI: `1C881F0A27416198FC25A068A79044BEED7FCC37160F38E7AFE2AE09CE9A2371`.
No tiene firma Authenticode. La integridad se comprueba con la suma facilitada;
no se recomienda desactivar SmartScreen ni el antivirus.

Se conserva el MSI anterior sin sobrescribirlo. No utilizar 0.2.15 para reparar
los mods corregidos: sus archivos volverían a introducir los fallos anteriores.
La versión 0.2.16 tiene nuevas identidades de producto; conserva los componentes
y las familias de actualización independientes de cada juego.

## Alcance de la validación

El MSI final se inspecciona sin instalarlo sobre los juegos de uso diario:
extracción de sus 46 archivos, hashes, identidades históricas, selector, perfiles,
instancias nativas, ausencia de instalaciones anidadas y de arranque automático.
Las pruebas transaccionales usan otra identidad MSI, la misma fuente y los
mismos archivos. Copian únicamente los ejecutables legítimos necesarios para
validar las carpetas; nunca los ejecutan ni copian los recursos completos de los
juegos. Comparan también los archivos y registros reales antes y después.

La prueba nueva de actualización instala primero el 2 y luego el 1 con los
payloads antiguos, e incorpora los archivos corregidos de uno en uno. Provoca
fallos antes y después de copiar los archivos para verificar la recuperación
del producto anterior y de sus bytes. Comprueba preferencias, primera copia
de originales, accesos y que el otro juego no cambie.

Los informes locales quedan en `artifacts/single-game-isolated/` y los de
verificación del artefacto en `artifacts/integration-0.2.16/msi/single-game/`.
El resumen final verificable se conserva en `release/validation-v0.2.16.json`.

Resultados finales: 74 comprobaciones / 12 operaciones del recorrido normal y
56 / 10 de actualización desde 0.2.15, todas correctas; 40 pruebas unitarias de
migración y la regresión de snapshots históricos también correctas. Las 12
baterías del núcleo y 27 comprobaciones de perfil pasaron de nuevo al cerrar.
La extracción final verifica además las 46 identidades históricas de componente.

Los logs de juego confirman candidate2 en ambos títulos y cambios DLSS/DLAA
guardados correctamente. BS2 finaliza con salida ordenada. BS1 usa su guarda
heredada para terminar tras un fallo del host durante la destrucción de la
ventana; no se presenta como una salida nativa libre de fallos. No se modifica
esa ruta después de la aceptación del usuario.

### Permisos de las pruebas silenciosas

La primera ejecución de laboratorio sin elevación recuperó los archivos pero
no pudo reconstruir el registro nativo al provocar una desinstalación fallida
(errores 1401/1406). Se recuperó y retiró esa instalación aislada mediante MSI,
sin editar permisos, ACL o políticas de Windows. No afectó a los juegos reales.

Las pruebas silenciosas `/qn` deben ejecutarse con elevación normal de **la misma
cuenta**, como las pruebas anteriores: esa interfaz no permite solicitar
credenciales durante la operación. El arnés ahora rechaza su ejecución sin
elevación antes de crear fixtures. Para el uso normal se abre el MSI con su
interfaz y se acepta, si aparece, la solicitud habitual de Windows. La metadata
del paquete permite esa solicitud; no se cambia el alcance por usuario ni se
habilita ninguna política de elevación global. Véanse
[Windows Installer y UAC](https://learn.microsoft.com/en-us/windows/win32/msi/using-windows-installer-with-uac)
y [metadata de elevación](https://learn.microsoft.com/en-us/windows/win32/msi/word-count-summary).

## Construcción con binarios congelados

Requisitos: PowerShell, .NET Framework, .NET para las herramientas WiX 6.0.2,
payload aceptado 0.2.11 y candidato BS2 0.2.13 original, ambos verificables,
y directorio de los binarios candidate2 probados. No se toma un payload de la
carpeta de un juego ni se recompilan los núcleos o lanzadores durante el cierre.

```powershell
.\installer\single-game\Prepare-Release.ps1 -Version 0.2.16 `
  -BasePayloadDirectory '<payload aceptado 0.2.11>' `
  -Bs2BasePayloadDirectory '<payload congelado BS2 0.2.13>' `
  -Bs2BaseManifestPath '<manifest-0.2.13.json de BS2>' `
  -CandidateDirectory '<candidate2 con DLL y launchers/bs1, launchers/bs2>' `
  -ModBuildDirectory '<build del núcleo candidate2>'

.\installer\single-game\Build-Msi.ps1 -Version 0.2.16 -Release `
  -Bs1PayloadDirectory '.\artifacts\distribution-0.2.16\payloads\bs1' `
  -Bs1ManifestPath '.\artifacts\distribution-0.2.16\payloads\manifest-bs1.json' `
  -Bs2PayloadDirectory '.\artifacts\distribution-0.2.16\payloads\bs2' `
  -Bs2ManifestPath '.\artifacts\distribution-0.2.16\payloads\manifest-bs2.json' `
  -BuildToolsDirectory '<WiX 6.0.2 verificado>'
```

El inventario `release/validated-mods-v0.2.16.json` fija los hashes aceptados.
La preparación normaliza las rutas antes de seleccionar perfiles, verifica las
bases completas y las cuatro optimizaciones, y rechaza sobrescribir su staging.
El constructor final rechaza los manifiestos viejos y un payload sin las
correcciones. La comprobación por extracción repite esos contratos sobre el MSI.

Una vez cerrado `release/SHA256SUMS-v0.2.16.txt`, el constructor no permite
regenerar esta versión de distribución. **Publicar siempre el mismo MSI,
no una nueva compilación con el mismo nombre.** Los paquetes de pruebas usan
`-TestFamily` con identidades privadas y nunca se entregan.

## Entrega y límites

El instalador verificado se copia a **Escritorio / Lanzadores MOD VR** y se
conserva también en `artifacts/distribution-0.2.16/`. Se entregan las notas,
el manifiesto y la suma SHA-256 junto al paquete de distribución local.

La confirmación del usuario y las pruebas técnicas no equivalen a una garantía
universal. Quedan para trabajo futuro la validación en otro ordenador y los
cambios de efectos gráficos del 2 en caliente (hoy: guardar en lanzador y
reiniciar). No se promete igualdad de FPS, ni se reabre la política de captura
de profundidad después de aceptar los binarios.
