# Integración BioShock 1–2 · 0.2.13

> **Documento histórico.** El cierre vigente es
> [la distribución 0.2.16](RELEASE-0.2.16.md), con MSI único y los dos mods
> corregidos y aceptados por el usuario. Los estados pendientes y la
> arquitectura EXE descritos debajo corresponden a fases anteriores.

> **Requisito corregido el 10/09/2026:** Carlos exige un único MSI nativo.
> La arquitectura EXE + dos MSI descrita más abajo queda como historial, no como
> entrega aprobada. **Corrección posterior:** la selección múltiple del MSI
> 0.2.13 también queda descartada. El desarrollo vigente está en
> `installer/single-game`: [desplegable individual 0.2.14](SINGLE-GAME-MSI-0.2.14.md).

Estado: candidato local en desarrollo, no publicado ni instalado en los juegos
reales. Trabajo autorizado el 10 de septiembre de 2026. La prueba en visor es un
paso separado; las pruebas automáticas no certifican FPS ni calidad visual.
Hashes y cierre de la batería final: [validación del candidato](VALIDATION-0.2.13-CANDIDATE.md).

## Distribución y límites

### Carpeta de entrega acordada con Carlos

Desde el 10 de septiembre de 2026, **cada instalador entregado de este proyecto
debe copiarse siempre a Escritorio / Lanzadores MOD VR**, además de conservar
el artefacto de compilación. Resolver el escritorio real del usuario, verificar
el SHA-256 de la copia y enlazar esa copia al indicar qué archivo debe abrir.
No sobrescribir otra build con el mismo nombre ni retirar versiones anteriores
sin confirmación. Copiar el instalador no significa instalarlo o ejecutarlo.
Los lanzadores instalados siguen en Build/Final; el escritorio recibe accesos.

Un único EXE autocontenido permite seleccionar un juego por ejecución. Se puede
ejecutar otra vez para instalar el otro juego. No son elecciones excluyentes.

| Elemento | BioShock 1 | BioShock 2 |
| --- | --- | --- |
| Paquete interno | MSI 0.2.11 aceptado, sin regenerar | MSI 0.2.13 candidato |
| Steam / ejecutable | 409710 / BioshockHD.exe | 409720 / Bioshock2HD.exe |
| Perfil local | BioshockVR | BioshockVR\bs2 |
| Resolución del juego | Bioshock.ini, WinDrv | Shared.ini, SharedOptions; SP sincronizado |
| Lanzador instalado | Binario aceptado 0.2.11 | Interfaz compartida 0.2.13 con armas BS2 |
| Gráficos durante la partida | Ruta nativa existente | Lectura INI; cambiar en lanzador y reiniciar |

Las identidades ProductCode, UpgradeCode, componentes, accesos y registro Windows
son independientes. El EXE común no se registra como dueño de ambos productos:
cada MSI se mantiene por separado desde Aplicaciones de Windows.

El código permite compilar ambos lanzadores para comprobar la interfaz compartida.
**El lanzador BS1 recién compilado no se distribuye en este candidato**: dentro del
EXE se conserva el MSI completo 0.2.11 con SHA-256
2A5365E332DC311C0E010AD0BECEEAB34E905F66C6F40AB5CF44824EDAD9F660.
0.2.12 sigue descartada.

## Implementado

- Adaptadores explícitos por juego para cámara/profundidad y controles de imagen.
  No se copian direcciones de motor del 1 al 2.
- Cuatro optimizaciones temporales de BS1 activadas en el candidato BS2:
  solapamiento por ojo, reutilización de profundidad invalidada por escrituras,
  solapamiento del tramo final y entrega temprana del par al visor.
- Se conservan la proyección exacta, aislamiento temporal por ojo, armas y guardas
  de cierre de BS2. Se elimina la captura automática de depuración de la primera
  cinemática de las compilaciones de distribución.
- Pestaña Imagen común: NORMAL/DLSS/DLAA, salida por ojo, calidad, resolución
  interna calculada y nueve opciones gráficas. BS2 conserva su pestaña Armas.
- Cambios de modo/resolución solicitados desde el visor en el hilo adecuado;
  guardado solo después de confirmar el resultado. La ruta de redimensionado
  nativa de BS2 todavía requiere comprobarse en el visor.
- Guardado coordinado de Shared.ini, Bioshock2SP.ini y dlss.ini desde el núcleo.
  Preparación y copias verificadas antes de sustituir; rollback ante fallos
  ordinarios; se conservan bytes de recuperación si hay una edición externa
  incompatible. No se promete atomicidad multifichero ante corte de corriente.
- Lanzador BS2 en ventana al guardar, sin escribir al abrir/recargar. Se cierra
  tras detectar el proceso nuevo correcto y su ventana respondiendo tres segundos;
  espera máxima de 60 segundos. Un fallo deja la ventana abierta con explicación.
- Migración de los manifiestos BS2 Format=3 / 0.1.0-beta y 0.1.1-beta: se verifican
  los 21 originales de la beta; el original de desinstalación es anterior a la
  beta, no sus DLL instaladas. Reparación conserva esa primera copia. Una
  migración fallida recupera el estado beta; una completada no reactiva después
  su antiguo manifiesto. Las copias beta se conservan.
- Selector común con ambos MSI verificados dentro del EXE. No abre automáticamente
  el lanzador/juego y no reinicia Windows.

## Construcción reproducible

Desde la raíz de esta copia de integración, con MSVC x86 y CMake de VS 2022:

~~~powershell
cmake --preset integration-win32
cmake --build --preset integration --parallel 4 --target bioshockvr xinput_proxy
.\apps\launcher\Build-Launcher.ps1 -Game both
~~~

El payload de referencia es exclusivamente el inventario de 23 archivos aceptados
de 0.2.11, contrastado con release/manifest-v0.2.11.json. No usar installer/Payload,
0.2.12 ni una carpeta del juego. NVIDIA 310.7.0.0 y las licencias se conservan.
WiX 6.0.2 y su fuente/licencia deben estar disponibles previamente.

~~~powershell
$acceptedPayload = '<payload verificado de 0.2.11>'
$wixTools = '<herramientas WiX 6.0.2 verificadas>'
$acceptedMsi = '<BioShock-VR-DLSS-DLAA-0.2.11.msi aceptado>'
.\installer\msi\Build-Msi.ps1 -GameId bs2 -BasePayloadDirectory $acceptedPayload -BuildToolsDirectory $wixTools
.\installer\unified\Build-Bundle.ps1 -BioShock1Msi $acceptedMsi -BioShock2Manifest '.\artifacts\integration-0.2.13\msi\bs2\manifest-0.2.13.json'
~~~

Salida: artifacts/integration-0.2.13/bundle/
BioShock-1-2-VR-DLSS-DLAA-0.2.13-CANDIDATO.exe, manifest.json y verificación de los
recursos embebidos. Cada regeneración cambia los hashes del candidato; no
reutilizar un candidato instalado con el mismo ProductCode para una actualización
normal. Una versión entregada se congela y el siguiente cambio requiere versión.

## Pruebas y evidencia

- scripts/Test-Integration.ps1: 12 ejecutables, todos correctos.
  Política de imagen 107; controlador 46; recuperación estéreo BS1 40; opciones
  gráficas 21; buzón de resolución 13; política de cierre 23; guardas BS2 788
  casos; puerta de salida; temporal WARP BS1 y BS2, incluidos 13 casos de
  reutilización de profundidad en cada juego; transporte/solapamiento 53;
  guardado BS2 20 casos.
- Ambos lanzadores: --self-test correcto; vistas previas Imagen revisadas.
- installer/msi/Test-LegacyMigration.ps1: 29 comprobaciones unitarias.
- installer/msi/Test-Msi.ps1 -TestLegacyMigration: instalación, reparación
  estándar, rollback antes/después de retirar archivos, desinstalación y migración
  beta con recuperación exacta. Comprueba hashes de archivos reales protegidos
  y que no queda producto de prueba registrado.
- Selector: --verify contrasta ambos recursos; --window-test abre, permanece vivo
  brevemente y se cierra solo; --preview genera una imagen sin instalar nada.
- installer/unified/Test-Bundle.ps1: 24 comprobaciones; versiones y payloads,
  accesos al lanzador correcto, retirada por nombre exacto y ausencia de GUID
  de componente, ProductCode, UpgradeCode o claves de registro compartidos.
  Abre las bases MSI solo en lectura y no instala ninguno.

Las baterías MSI usan identidades Windows aisladas y archivos de configuración
ficticios. Copian solo el ejecutable legítimo necesario para validar el destino
(27,7 MB en BS2), nunca el juego completo ni sus recursos, y no lo ejecutan.
Los directorios de evidencia y copias se retienen; los productos de prueba se
desinstalan al terminar.

~~~powershell
.\scripts\Verify-Repository.ps1 -BuildLauncher
.\scripts\Test-Integration.ps1
.\installer\msi\Test-LegacyMigration.ps1 -BuildToolsDirectory $wixTools
# Construir primero una familia de pruebas aislada:
$testFamily = [Guid]::NewGuid().ToString('N')
.\installer\msi\Build-Msi.ps1 -GameId bs2 -TestFamily $testFamily -BasePayloadDirectory $acceptedPayload -BuildToolsDirectory $wixTools
.\installer\msi\Test-Msi.ps1 -GameExeSource '<Bioshock2HD.exe legítimo>' -ManifestPath ".\artifacts\msi-isolated\$testFamily\0.2.13\manifest-0.2.13.json" -FixtureBase '<directorio de pruebas>' -TestLegacyMigration -TestShortcutChoice
~~~

Test-Msi exige una familia aislada y rechaza el MSI de distribución. El paquete
de pruebas no puede instalar fuera de BvrMsiTest-<familia> ni entrar en el EXE
común. No usar Prepare-BS2-TestCopy para estas pruebas: es una herramienta LAB
histórica para otro escenario y sí puede copiar recursos del juego.

## Próximos pasos y criterios de entrega

1. Paquete final e identidades verificadas: consultar el informe de validación.
2. Con Carlos, instalación BS2 acordada y prueba breve NORMAL → DLSS → DLAA →
   NORMAL, resolución, carga, agua vista desde fuera, manos/HUD y salida.
3. Comprobar coexistencia real de ambos productos e instalación en ambos órdenes.
   Los sentinelas/hashes y la comparación de identidades son evidencia técnica,
   pero no sustituyen esa prueba de usuario.
4. Derivar la ruta nativa de opciones gráficas BS2 si se requiere F4 en caliente.
   Hoy está expresamente pendiente; el lanzador permite guardarlas y reiniciar.
5. Validar en otro ordenador y publicar únicamente tras autorización.

No afirmar todavía paridad completa, estabilidad en visor ni mejora de FPS.
El agua no tiene un shader “reparado” ni un fallo NVIDIA demostrado; se portan las
optimizaciones aceptadas sin reabrir la investigación de DLSS 5.
