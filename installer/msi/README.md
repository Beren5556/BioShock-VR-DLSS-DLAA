# Windows Installer — 0.2.11

Paquete MSI por usuario para BioShock 1 Remastered. Instala el mod completo
en `Build\Final`, registra su mantenimiento en Aplicaciones de Windows y crea
un acceso directo de escritorio si se deja marcada la casilla
«Crear acceso directo en tu escritorio» (marcada por defecto). La elección
se conserva en las reparaciones. No ejecuta el lanzador ni el juego.
La pantalla final explica dónde y cómo abrirlo, con instrucciones diferentes
según se haya creado el acceso. El texto del runtime solo muestra
«Incluye NVIDIA DLSS 310.7.0.0.».

La 0.2.11 incluye la corrección de recuperación del estéreo, pendiente de
confirmación en visor. Lleva ProductCode y versión nuevos para actualizar
0.2.10 sin el error 1638. El mismo archivo MSI admite reparación/reinstalación.

La ruta se obtiene, en este orden, de la selección explícita, la instalación
MSI registrada, el manifiesto del instalador anterior y las bibliotecas de
Steam. Varias copias compatibles sin una selección previa dejan la decisión
al usuario. La carpeta raíz del juego se normaliza a `Build\Final`.

## Construcción

Requisitos: MSVC 2022 x86, CMake, .NET Framework 4.7.2 o superior, runtime
.NET 6 para las herramientas WiX 6.0.2. No hace falta instalar un SDK dotnet.
El payload base local se importa con las herramientas existentes del proyecto;
no se obtiene de una carpeta completa del juego ni se añade al repositorio.

Desde la raíz del repositorio:

```powershell
cmake --preset stable-win32
cmake --build --preset stable --parallel 4 --target bioshockvr
& .\installer\msi\Build-Msi.ps1
```

El script verifica las cuatro optimizaciones, desactiva los tres modos de
diagnóstico, compila el lanzador y crea `artifacts/stable-0.2.11/*.msi` con
su manifiesto SHA-256. Incluye el host de distribución y NVIDIA 310.7.0.0.
La primera ejecución descarga herramientas WiX fijadas a 6.0.2 de NuGet y
el código fuente/licencia de ese tag oficial; se conservan sus avisos MS-RL.
Las condiciones de uso de las herramientas están en su `OSMFEULA.txt`.

Una versión entregada es inmutable: cuando existe su
`release/SHA256SUMS-v<versión>.txt`, el script impide regenerar su MSI público.
Los cambios, incluso de interfaz, deben llevar versión nueva. Cambiar solo
PackageCode manteniendo ProductCode y versión requiere mecanismos de pequeña
actualización que no sirven como instalación normal con doble clic; fue el
origen del aviso 1638 en la revisión de interfaz de 0.2.10. Usamos una
[actualización mayor MSI](https://learn.microsoft.com/en-us/windows/win32/msi/major-upgrades)
con ProductCode nuevo y UpgradeCode compartido; no se pide al usuario que
desinstale primero ni se manipula su registro para evitar el control de Windows.

## Verificación aislada

```powershell
& .\installer\msi\Test-Msi.ps1 -GameExeSource 'E:\ruta\Build\Final\BioshockHD.exe'
```

El ejecutable se COPIA a una carpeta temporal única; nunca se incluye en el
MSI ni se inicia. Se redirigen la configuración, las copias y el escritorio
al fixture de prueba. La prueba se niega a sustituir una instalación MSI real
registrada. Cubre instalación limpia, valores predeterminados, reparación, preservación de
ajustes, desinstalación, fallo controlado con rollback y recuperación del mod
anterior. Comprueba también los hashes de la instalación e INI reales.

La versión 0.2.10 mantiene el lanzador compacto, el acceso directo versionado,
el progreso separado de su barra y las cuatro optimizaciones. En el visor la
resolución cambia exactamente 100 píxeles y la calidad DLSS usa una lista de
5 puntos porcentuales. F4 continúa limitado a la página gráfica.
Las notas deben existir antes de empaquetar.
La publicación y la prueba final en visor son pasos aparte.

Para probar con un producto real registrado se construyen paquetes de prueba
con `Build-Msi.ps1 -TestFamily <32 caracteres hexadecimales>`. La familia de
prueba cambia ProductCode, UpgradeCode, todos los GUID de componentes y la
clave de registro; no comparte ninguno con el MSI de distribución. Sus acciones
rechazan cualquier destino que no sea su carpeta privada `BvrMsiTest-<familia>`.
Estos paquetes nunca se distribuyen ni se colocan junto al instalador público.

`Test-Msi.ps1 -ManifestPath <manifiesto aislado> -FixtureBase X:\BibliotecaSteam`
prueba en la misma unidad del fallo sin entrar en la instalación real.
`-UpgradeManifestPath <manifiesto nuevo de la misma familia>` verifica la
actualización con los hashes del payload nuevo, no con los antiguos. Un MSI
de la familia real sigue siendo rechazado si hay una instalación registrada.
El test comprueba también que el registro y los archivos reales no cambian.
`-ReproducePackageCollision`, solo con una familia aislada, copia el MSI
antiguo y cambia su PackageCode para reproducir 1638; no toca el original.
La batería de actualización verifica después la instalación normal de la
versión nueva y la reinstalación de ese mismo archivo, incluidos los accesos.
`-TestShortcutChoice` añade instalación sin acceso, reparación conservando
esa elección, activación/desactivación y recuperación si falla cualquiera
de esos cambios. La propiedad de instalación silenciosa
`BVR_DESKTOPSHORTCUT=0` desactiva el acceso; `=1` lo activa. En la interfaz
la casilla desmarcada deja la propiedad vacía y el marcador de inicialización
evita que la secuencia de ejecución la vuelva a activar por defecto.
Los directorios y logs privados se conservan como evidencia; los productos
MSI de prueba se desinstalan al terminar.

`Test-MsiPresentation.ps1` comprueba el MSI en modo solo lectura: diálogo
activo, ausencia de solapamientos, versión y destino del acceso, retirada
de su nombre antiguo y compatibilidad de rutas de copias anteriores. Puede
ejecutarse aunque exista una instalación real, porque no instala nada.
`Preview-Progress.cs` es una utilidad de desarrollo que usa exclusivamente
las API de vista previa de MSI y se cierra al cabo de 60 segundos. No se
incluye en el instalador. La prueba visual se realiza con una copia del MSI
con un texto de estado de ejemplo de varias líneas.

## Corrección de 1926 / error 5

Reproducido con el algoritmo 0.2.9 en una carpeta aislada de E:, usando el
mismo usuario sin elevar. Windows Installer intentaba proteger los `.rbf`
generados al retirar archivos existentes en `E:\Config.Msi`; el usuario puede
modificar la carpeta del juego, pero no administrar esa carpeta del sistema.

0.2.10 guarda primero la instantánea recuperable, verifica todos sus hashes
y que los destinos no hayan cambiado, y retira únicamente los archivos de
esa lista antes de las acciones estándar RemoveFiles/InstallFiles. La acción
incluye el acceso directo exacto de la versión registrada anterior, también
cuando el escritorio se encuentra en otra unidad. La acción
RollbackFiles ya está programada antes de la retirada. Windows Installer sigue
gestionando y recuperando sus archivos nuevos, accesos, registro y actualización
del producto anterior. No se desactiva rollback, no se cambia ALLUSERS, no se
elevan privilegios y no se tocan las ACL de Config.Msi.

Se comprueba tanto el fallo justo después de retirar los archivos como el
fallo después de copiarlos. Un código MSI 0 no basta: la prueba falla si el
registro del paquete corregido contiene Error 1926.
Referencia del mecanismo estándar: [Rollback Installation de Microsoft](https://learn.microsoft.com/en-us/windows/win32/msi/rollback-installation).

## Recuperación

Antes de sustituir archivos se guarda una instantánea recuperable. El MSI
restaura byte a byte el estado anterior si falla. Al desinstalar recupera los
archivos que había antes del primer MSI, deja los INI personales y conserva
las copias en `%LOCALAPPDATA%\BioshockVR\WindowsInstaller`.

Una instalación nueva aplica los predeterminados a las nueve opciones de
`Engine.RenderConfig` ensayadas, cuando ya existe el INI del juego:
`RealTimeReflection` y `UseRippleSystem` en False, `FluidSurfaceDetail` en High
y el resto en True. Una
actualización o reparación conserva las preferencias existentes. Reparar
vuelve a instalar NVIDIA 310.7.0.0; una DLL x64 alternativa no dispara
reparación automática ni bloquea el lanzamiento por su número de versión.
