# Instalador común BioShock 1–2 · 0.2.16

Entrada vigente: `single-game/Prepare-Release.ps1` y `single-game/Build-Msi.ps1`.
Un MSI nativo contiene ambos mods validados y permite seleccionar un juego por
ejecución, con productos independientes y sin arranque automático. No es un
EXE contenedor. Ver [construcción, migración y pruebas](../docs/RELEASE-0.2.16.md).
El contenido inferior y `unified/` son referencias históricas.

## Referencia histórica MSI 0.2.11

La nueva distribución se construye con `msi/Build-Msi.ps1`. Véase
[Windows Installer](msi/README.md). El instalador EXE descrito debajo se
conserva como herramienta y referencia de versiones anteriores.

## Instalador autónomo anterior

El instalador WinForms contiene todo lo necesario para añadir el mod a una
instalación compatible de BioShock Remastered. No presupone que BioShock VR
esté instalado.

La interfaz pública solo pide la carpeta Build\Final. Antes de escribir:

- valida el ejecutable original compatible;
- verifica por SHA-256 todos los recursos embebidos;
- guarda un manifiesto recuperable y una copia de cada archivo sustituido;
- no modifica los INI durante la instalación.

Restaurar devuelve byte a byte los archivos anteriores a la primera
instalación y retira los que no existían.

## Payload local

Los binarios de distribución no se almacenan sueltos en Git. La Release
contiene el instalador probado y payload-manifest.json fija los hashes.

Para reconstruir localmente:

    .\installer\Import-Local-Payload.ps1 -PayloadDirectory C:\ruta\al\payload-validado
    .\installer\Build-Installer.ps1

El directorio indicado debe tener la estructura definida en
[payload-manifest.json](payload-manifest.json). El script rechaza cualquier
archivo cuyo hash no coincida.

La prueba integral requiere el BioshockHD.exe original compatible:

    .\installer\Test-Installer.ps1 -GameExecutable C:\ruta\Build\Final\BioshockHD.exe

La prueba usa copias aisladas, abre y cierra ambas interfaces por su PID exacto
y ejerce instalación limpia, actualización, restauración y rechazo de rutas
incorrectas.
