# MSI único BioShock 1–2 · 0.2.13

> Histórico: Carlos descartó este flujo de casillas después de probarlo.
> Sustituido por [MSI 0.2.14 con desplegable individual](SINGLE-GAME-MSI-0.2.14.md).
> Se conserva el MSI entregado sin sobrescribirlo.

## Requisito de entrega

Corrección explícita de Carlos el 10 de septiembre de 2026: **un archivo MSI
nativo basado en el MSI de BioShock 1**, con los dos mods dentro. No un EXE que
abre dos MSI, ni un MSI que lanza instalaciones anidadas.

El EXE anterior de Escritorio / Lanzadores MOD VR queda descartado como entrega.
No se ha eliminado, movido ni sobrescrito. No presentar ese EXE como instalador
MSI. El nuevo MSI solo se copiará a esa carpeta cuando esté verificado.
Los lanzadores siempre se instalan en Build/Final de cada juego; el escritorio
recibe accesos directos opcionales.

## Implementación realizada

- Un ProductCode/UpgradeCode de distribución y una entrada en Aplicaciones.
- Dos características MSI independientes: Game_bs1 y Game_bs2; cada una con su
  propio destino, registro del mod, perfil, originales y acceso directo.
- Asistente nativo de Windows Installer (Segoe UI, basado en el del 1):
  selección de juegos, carpetas, accesos, reparación por juego, confirmación,
  progreso y resultado. No arranca automáticamente el juego ni el lanzador.
- Al volver a abrirlo, conserva seleccionados los mods instalados. Se puede
  añadir el otro; desmarcar un mod instalado solicita quitarlo. Desmarcar el
  último usa REMOVE=ALL para retirar también el producto.
- BS1: los 23 archivos del manifiesto aceptado 0.2.11, sin recompilarlos.
  El MSI original sigue intacto como referencia; no va anidado en el nuevo.
- BS2: los 23 archivos del candidato ya compilado, verificados contra su
  manifiesto. No se recompilaron el núcleo ni los lanzadores en esta corrección.
- Las acciones recuperables del MSI del 1 se compilan dos veces, una por juego,
  con nombres de propiedades y acciones independientes. Se reutiliza la lógica
  de migración de la beta del 2.
- Se conservan los GUID de componentes para las mismas rutas del MSI anterior.
  La primera instalación conjunta adopta los MSI independientes ya registrados.
  Se explica en el asistente y se impide desmarcarlos durante esa migración.
  Así no quedan dos productos propietarios de los mismos archivos al añadir
  el segundo juego en una operación posterior.

## Estado actual: MSI entregado para pruebas locales

El bloqueo descrito en los primeros candidatos está resuelto. El 10/09/2026
se verificó y copió a **Escritorio / Lanzadores MOD VR**:

`BioShock-1-2-VR-DLSS-DLAA-0.2.13-CANDIDATO.msi`

SHA-256: `0F344B2160043A8D13695876A81B76A7BA19174BCDD68A4809AD673364AF3C1D`.

El paquete entregado queda congelado. No reconstruirlo ni sustituirlo por bytes
distintos conservando el mismo ProductCode/versión.

- Batería completa: **69 comprobaciones / 11 operaciones**, aprobada.
- Migración aislada de predecesor BS1 simulado y beta BS2: **43 comprobaciones /
  3 operaciones**, aprobada.
- Validación de migración beta: **29 comprobaciones / 0 fallos**.
- Extracción del MSI final: **46 archivos idénticos a los payloads verificados**,
  cuatro características nativas y ninguna instalación MSI anidada.
- Navegación visual revisada hasta la confirmación de ambos juegos, sin Aplicar.
- Las dos instalaciones de prueba que habían quedado registradas se recuperaron
  y desinstalaron con Windows Installer. Se conservan logs y copias de prueba.

El MSI mantiene el alcance por usuario, pero permite solicitar elevación a
Windows Installer. Con ello también se recupera el registro nativo cuando se
provoca un fallo durante la desinstalación. No se cambiaron políticas ni ACL.

Evidencia, identidades, alcance y límites:
[validación final del MSI](COMBINED-MSI-VALIDATION-2026-09-10.md).

No se han modificado los juegos reales, recompilado los núcleos/lanzadores ni
publicado en GitHub. La instalación real, la prueba en visor y la validación
en otro ordenador siguen pendientes. La prueba de migración usa fixtures;
no equivale a haber actualizado ya los juegos de Carlos.

## Construcción y pruebas

```powershell
.\installer\combined\Build-Msi.ps1 `
  -Bs1PayloadDirectory '<stable-0.2.11/msi-build-0.2.11/payload aceptado>' `
  -Bs2PayloadDirectory '.\artifacts\integration-0.2.13\msi\bs2\msi-build-0.2.13\payload' `
  -Bs2ManifestPath '.\artifacts\integration-0.2.13\msi\bs2\manifest-0.2.13.json' `
  -BuildToolsDirectory '<WiX 6.0.2 verificado>' `
  -TestFamily '<GUID minúsculas, 32 dígitos>'

.\installer\combined\Test-Msi.ps1 `
  -ManifestPath '<manifest.json aislado>' `
  -Bs1Exe '<BioshockHD.exe legítimo>' `
  -Bs2Exe '<Bioshock2HD.exe legítimo>'
```

La batería copia exclusivamente los dos ejecutables legítimos y crea
configuraciones ficticias: **no copia juegos completos ni los ejecuta**.
Las identidades y destinos de prueba están separados de los productos reales.
`-TestPredecessor bs1` genera una identidad anterior simulada para migración;
no es una reconstrucción ni una distribución de código 0.2.12.

Base técnica: [características MSI](https://learn.microsoft.com/en-us/windows/win32/msi/feature-table),
[REINSTALL](https://learn.microsoft.com/en-us/windows/win32/msi/reinstall) y
[RemoveExistingProducts](https://learn.microsoft.com/en-us/windows/win32/msi/removeexistingproducts-action).
La retirada de productos anteriores ocurre durante la primera instalación, no
en mantenimiento; por ello la adopción de los MSI anteriores es conjunta.
