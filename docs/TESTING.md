# Pruebas

## Comprobación del repositorio

    .\scripts\Verify-Repository.ps1 -BuildLauncher

Esta comprobación valida identidad de versión, manifiesto de payload, suma del instalador, política visual del lanzador, archivos prohibidos y self-test del lanzador.

## Prueba del instalador

Se necesita una copia legítima y compatible de BioshockHD.exe. El script solo la usa para crear fixtures bajo el directorio temporal de Windows:

    .\installer\Test-Installer.ps1 -GameExecutable C:\ruta\BioShock Remastered\Build\Final\BioshockHD.exe

Opcionalmente, ProtectedPaths admite rutas reales que deben permanecer idénticas durante la batería. El script toma su huella antes y después.

La batería comprueba:

- self-test y SHA-256 de 20 recursos embebidos;
- apertura de las interfaces del instalador y el lanzador;
- instalación limpia sin alterar BioshockHD.exe;
- restauración completa de una instalación limpia;
- sustitución y restauración byte a byte de 20 archivos preexistentes;
- rechazo de una carpeta que no contiene un juego compatible;
- ausencia de cambios en todos los archivos reales protegidos.

Las pruebas crean un directorio con GUID bajo %TEMP%\BvrInstallerTests y solo lo eliminan tras un PASS completo. Si fallan, lo conservan para diagnóstico. Usa TestOutputRoot si necesitas otro directorio corto y dedicado.

## Validación registrada para v0.2.0-beta

La versión publicada superó:

- 20 recursos embebidos verificados;
- instalación y restauración limpias;
- actualización y restauración byte a byte;
- rechazo de ruta incompatible sin escrituras;
- 13 archivos de la instalación real vigilados sin cambios.

Instalador validado: SHA-256 08EEE09B4DF57997C84DE441BC3061FCBFD0E9983F5DD1819C20300E80F9D5E8.

## Límites

Las pruebas automatizadas no sustituyen una sesión dentro del visor. Antes de promover una beta, valida al menos arranque, ojo izquierdo/derecho, cambio NORMAL/DLAA/DLSS, guardado del lanzador, salida limpia y restauración desde el instalador.
