# Instalador BioShock 1–2 · 0.2.14

> Histórico: sustituido por [0.2.15](SINGLE-GAME-MSI-0.2.15.md), que corrige
> el acceso «Beta» del 2 y aplica la simplificación de textos solicitada.
> El archivo entregado 0.2.14 se conserva sin sobrescribir.

Requisito confirmado por Carlos el 10/09/2026: primero un desplegable para
elegir **un juego**; después, el asistente normal únicamente para ese mod,
independientemente de que ya esté instalado. El otro juego no se modifica.
Ambos pueden estar instalados, abriendo dos veces el mismo MSI.

## Entrega y contenido

Un solo archivo `BioShock-1-2-VR-DLSS-DLAA-0.2.14-CANDIDATO.msi`, sin EXE
contenedor. La versión **del instalador** es común a los dos juegos.

- BS1: los 23 archivos aceptados de 0.2.11, sin recompilar núcleo ni lanzador.
- BS2: los 23 archivos del candidato 0.2.13, sin recompilar núcleo ni lanzador.
- Los lanzadores se instalan en `Build\Final` del juego correspondiente.
- Acceso directo opcional en el escritorio, independiente para cada juego.
- El instalador no abre automáticamente el lanzador ni el juego al terminar.
- Actualizar/reparar conserva preferencias; desinstalar conserva juego,
  partidas, preferencias y copias recuperables, y restaura los archivos previos.

SHA-256 del candidato:

`4F08745EC9054C6CB0EEBB76435566AE3311D151D6299E7318AEB602E97CA277`

Destino acordado: **Escritorio / Lanzadores MOD VR**. No sustituir ni borrar
el MSI 0.2.13 o el antiguo EXE sin autorización. No se ha publicado en GitHub.

## Funcionamiento del MSI

`installer/single-game` utiliza dos transformaciones de instancia nativas de
Windows Installer incluidas dentro del mismo MSI (`bs1` y `bs2`). Cada juego
tiene identidad de producto, actualización, ruta, registro y copias propias.
Windows muestra una entrada por mod instalado, ambas con versión 0.2.14.

El producto base solo muestra el desplegable: no instala archivos ni se registra.
Al pulsar Siguiente, una acción exclusiva de interfaz abre el mismo MSI para la
instancia elegida y cierra el selector antes de iniciar ninguna transacción.
No hay acción MSI anidada ni instalación de ambos juegos por defecto.

En el asistente elegido se confirma carpeta y acceso; si ya existe esta versión,
se puede continuar para reparar/reinstalar o desinstalar únicamente ese mod.
Las características del otro juego están deshabilitadas y una comprobación
adicional impide planificar sus archivos, incluso con `ADDLOCAL=ALL` externo.

Se conservan los códigos de actualización y los 46 identificadores de
componentes de los MSI de origen. La actualización de un MSI individual retira
solo ese producto. Si procede del antiguo MSI conjunto 0.2.13, se retiran solo
las características del juego elegido; las del otro siguen registradas hasta
que ese juego se seleccione. Al migrar el último se retira el producto anterior.

La beta antigua del 2 se reconoce únicamente al elegir el 2 y conserva la
trazabilidad de sus originales. Elegir el 1 no adopta ni reinstala la beta del 2.

Referencias técnicas: [instancias nativas de Windows Installer](https://learn.microsoft.com/en-us/windows/win32/msi/installing-multiple-instances-with-instance-transforms),
[retirada selectiva mediante Upgrade](https://learn.microsoft.com/en-us/windows/win32/msi/upgrade-table).

## Mensaje observado por Carlos

El registro MsiInstaller del 10/09/2026 a las 16:05:39 mostró
«Archivo no reconocido en la copia del instalador» (instalación 1603).
El `Original.xml` histórico del 1 usa `/` en tres rutas `host64`; el paquete
0.2.13 comparaba esas rutas con una lista que utilizaba `\`.

`MsiStorage.cs` ahora normaliza los separadores **solo en memoria**, conserva
la lista exacta de archivos permitidos y rechaza duplicados, rutas absolutas,
traversal y archivos ajenos. No se reescribe la copia original del usuario.
Los identificadores MSI siguen calculándose con las rutas canónicas originales.

## Validación y límites

La extracción del MSI final verifica los 46 SHA-256, las dos instancias
incluidas, los 46 identificadores históricos de componentes, ámbito por usuario
y capacidad de solicitar elevación normal de Windows. No se alteran permisos,
políticas de seguridad ni el registro de Windows Installer manualmente.

La batería usa identidades privadas bajo `BioShockVRInstallerTests` y carpetas
`BvrMsiTest-<familia>`. Copia únicamente los ejecutables legítimos necesarios para
validar las rutas; **no copia los juegos completos ni ejecuta los juegos**.
Comprueba los archivos y registros reales antes y después.

Cobertura: instalación individual, reparación, accesos independientes,
desinstalación individual, fallos simulados y recuperación, copias históricas
con `/`, migración de MSI individual + beta y separación del MSI conjunto.
La interfaz se revisa hasta Confirmar y se cancela sin pulsar Aplicar.

Batería final del 10/09/2026: **161 comprobaciones, 24 operaciones, todas PASS**.

| Escenario | Comprobaciones | Operaciones |
| --- | ---: | ---: |
| Instalar, mantener y retirar cada juego por separado | 73 | 12 |
| Separar una instalación conjunta 0.2.13 | 35 | 6 |
| MSI previo del 1 + beta del 2, sin adopción cruzada | 53 | 6 |

Los informes se conservan en
`artifacts/single-game-isolated/ea32089c9d36404b994d146f03bcd183/single-game-0.2.14/`:
`test-result.json`, `test-result-combined-upgrade.json` y
`test-result-standalone-beta.json`. El MSI aislado de esta misma fuente tiene
SHA-256 `DD644FD0CFA340DE6D88E413D4D7ABB68C8A7C6B5478EBEF0E9AAE6BD544FCC5`;
solo difieren sus identidades/rutas de prueba respecto al candidato real.
El contenido y las identidades históricas del candidato real están auditados
por `artifacts/integration-0.2.14/msi/single-game/package-verification.json`.

Se probaron también los planes de mantenimiento de ambas instancias.
`Installer.OpenProduct` expone la base sin su transformación; el test aplica
la transformación incluida a una copia privada antes de ejecutar únicamente
las acciones inmediatas de planificación. Las 24 operaciones de integración
sí se realizan con `msiexec`, sus instancias y su registro nativos.

Esta revisión no certifica FPS, paridad gráfica en caliente ni calidad de BS2
en visor. La prueba personal del nuevo instalador y la validación en otro PC
siguen pendientes. No se modifica el estado de los juegos reales para entregar
el archivo MSI.

## Construir y verificar

`Build-Msi.ps1` requiere `-Bs1PayloadDirectory`, `-Bs2PayloadDirectory`,
`-Bs2ManifestPath` y `-BuildToolsDirectory` (WiX 6.0.2). El manifiesto BS1
aceptado se toma de `release/manifest-v0.2.11.json`. `-TestFamily <32 hex>` crea
un paquete de prueba con identidades disjuntas; nunca distribuir ese paquete.

`Verify-Package.ps1 -ManifestPath ... -BuildToolsDirectory ...` verifica el
contenido. Añadir `-Bs1SourceMsi` y `-Bs2SourceMsi` compara también los GUID
de los componentes del candidato real con los MSI originales.

`Test-Msi.ps1` requiere el manifiesto aislado y las rutas `-Bs1Exe`/`-Bs2Exe`.
`-PredecessorManifest` habilita la batería de actualización; `-FixtureBase`
permite una carpeta nueva por escenario. Las recuperaciones del registro
nativo se prueban en un proceso elevado con consentimiento normal de Windows.
Las pruebas son silenciosas: sus fallos intencionados no se muestran a Carlos.

Una vez entregada una versión, su archivo `release/SHA256SUMS-vX.Y.Z.txt`
impide regenerarla bajo el mismo número. Cualquier cambio posterior exige
otra versión.
