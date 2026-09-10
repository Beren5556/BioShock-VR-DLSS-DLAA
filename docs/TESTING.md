# Pruebas

Para **0.2.13**, las baterías vigentes son scripts/Test-Integration.ps1,
installer/msi/Test-Msi.ps1 (familia aislada) y Test-LegacyMigration.ps1.
Comandos y resultados en [integración](INTEGRATION-0.2.13.md).
Los apartados del instalador EXE beta conservados debajo son históricos.

## Comprobación del repositorio

    .\scripts\Verify-Repository.ps1 -BuildLauncher

Esta comprobación valida identidad de versión, manifiesto de payload, suma del instalador, política visual del lanzador, archivos prohibidos y self-test del lanzador.

## Prueba del instalador

Se necesita una copia legítima y compatible de BioshockHD.exe. El script solo la usa para crear fixtures bajo el directorio temporal de Windows:

    .\installer\Test-Installer.ps1 -GameExecutable C:\ruta\BioShock Remastered\Build\Final\BioshockHD.exe

Opcionalmente, ProtectedPaths admite rutas reales que deben permanecer idénticas durante la batería. El script toma su huella antes y después.

Para comprobar además una migración real desde la beta anterior:

    .\installer\Test-Installer.ps1 -GameExecutable C:\ruta\BioshockHD.exe -PreviousInstaller '.\artifacts\release\Instalador BioShock VR DLSS-DLAA Beta 0.2.2.exe'

La batería comprueba:

- self-test y SHA-256 de 20 recursos embebidos;
- apertura de las interfaces del instalador y el lanzador;
- instalación limpia sin alterar BioshockHD.exe;
- restauración completa de una instalación limpia y retirada de directorios vacíos del paquete;
- migración real desde la beta anterior y restauración posterior limpia;
- sustitución y restauración byte a byte de 20 archivos preexistentes;
- rechazo de una carpeta que no contiene un juego compatible;
- ausencia de cambios en todos los archivos reales protegidos.

Las pruebas crean un directorio con GUID bajo %TEMP%\BvrInstallerTests y solo lo eliminan tras un PASS completo. Si fallan, lo conservan para diagnóstico. Usa TestOutputRoot si necesitas otro directorio corto y dedicado.

## Validación registrada para v0.2.1-beta

La versión publicada superó:

- 20 recursos embebidos verificados;
- instalación y restauración limpias;
- actualización y restauración byte a byte;
- rechazo de ruta incompatible sin escrituras;
- 13 archivos de la instalación real vigilados sin cambios.

Instalador validado: SHA-256 7B79BF92BDFEFFF1F857E783F8D9EDA11A62010BBA9C915C1F167BFBA07A6A69.

## Validación registrada para v0.2.2-beta

La compilación candidata superó:

- self-test y hashes de los 20 recursos embebidos;
- apertura controlada del instalador y el lanzador;
- instalación y restauración limpias;
- migración real 0.2.1 a 0.2.2 y restauración posterior;
- sustitución y restauración byte a byte de 20 archivos preexistentes;
- rechazo de ruta incompatible sin escrituras;
- vigilancia de la copia fuente sin cambios.

Instalador validado: SHA-256 1C35B82A417C1A8AE5F9A71C688E27221E7C6E8E9859513C6F806E58C96977A4.

## Validación registrada para v0.2.3-beta

La compilación candidata superó:

- self-test y hashes de los 20 recursos embebidos;
- apertura controlada del instalador y el lanzador, cerrados por PID exacto;
- instalación y restauración limpias, incluida la retirada de `host64` y `BioShockVR-DLSS45` cuando quedaron vacías;
- migración real 0.2.2 a 0.2.3 y restauración posterior limpia;
- sustitución y restauración byte a byte de 20 archivos preexistentes;
- rechazo de ruta incompatible sin escrituras;
- vigilancia sin cambios del juego de referencia y de ambos instaladores empleados.

Instalador validado: SHA-256 2722C00F1C354781428213CA7CE5C28FDE56D86EA13CB8A8FAF59D36FE616EE0.

La ejecución externa en un segundo PC sigue pendiente y es requisito antes de promover esta beta como versión estable.

## Límites

Las pruebas automatizadas no sustituyen una sesión dentro del visor. Antes de promover una beta, valida al menos arranque, ojo izquierdo/derecho, cambio NORMAL/DLAA/DLSS, guardado del lanzador, salida limpia y restauración desde el instalador.
