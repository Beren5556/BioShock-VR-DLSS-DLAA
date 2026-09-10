# BioShock 2: contrato y aceptación del instalador

## Alcance

Instalador autónomo 0.1.1-beta: Steam AppID 409720, `Bioshock2HD.exe` x86 y
hash compatible. Configuración inicial: `%APPDATA%\BioshockHD\Bioshock2\Bioshock2SP.ini`
y `Shared.ini`. No modifica INI durante instalar/restaurar.

Manifiesto formato 3, `GameId=bs2`, bajo
`%LOCALAPPDATA%\BioshockVR\bs2\Installer-DLSS-DLAA`. Rechaza manifiestos BS1 y
rutas fuera del inventario, duplicadas o atravesadas por enlaces/uniones.
Los hashes embebidos se derivan del manifiesto de payload validado al compilar.

La versión 0.1.1 acepta exclusivamente manifiestos BS2 0.1.0-beta o 0.1.1-beta
de formato 3 con el inventario exacto. Al actualizar, conserva la copia anterior
a la primera instalación y adopta los nuevos hashes y versión. Restaurar una
instalación anterior no cambia artificialmente su versión si quedan conflictos.
Las versiones desconocidas, futuras o de otros juegos se rechazan.

`scripts/Verify-Repository.ps1` inspecciona los recursos del EXE mediante la
reflexión de solo lectura de .NET Framework, sin ejecutarlo. Si se inicia desde
PowerShell 7, se relanza automáticamente en Windows PowerShell 5.1 conservando
el argumento opcional `-BuildLauncher`. Para verificar el candidato congelado,
ejecutarlo sin ese argumento: reconstruir el lanzador regeneraría su binario.

## Mejoras heredadas de la siguiente versión de BS1

1. Instalación y apertura son resultados separados. Windows puede fallar al
   abrir la interfaz sin invalidar los archivos y copias ya verificados. El
   aviso mantiene el éxito de instalar y muestra la ruta manual.
2. La confirmación del proceso real del juego corresponde al lanzador BS2.
3. Restaurar genera un informe con números de archivos repuestos/retirados y
   carpetas propias vacías retiradas, rutas de recuperación y conflictos.
   Mantiene partidas, INI, ajustes VR/DLSS y backups. Los cambios posteriores en
   archivos o accesos directos permanecen en su sitio; el manifiesto sigue
   activo para completar Restaurar una vez resueltos. No hay limpieza total.
4. La prueba en otro ordenador se mantiene como requisito pendiente de publicación.

## Recuperación

Las copias originales se validan antes de comenzar Restaurar. Cada operación
captura el estado previo para rollback. No se marca completada una restauración
con conflictos. Reparar puede reemplazar archivos cambiados, pero antes conserva
copias identificadas en `Conflicts-Before-Repair-*`; la copia original inicial
no se sustituye. Los archivos desconocidos nunca entran en el inventario.

Las pruebas con errores inyectados verifican rollback de excepciones en mitad
de instalar/restaurar. Una interrupción forzosa del sistema puede requerir volver
a ejecutar Restaurar usando el manifiesto y las copias persistentes; no se afirma
atomicidad frente a pérdida de energía de todo el sistema de archivos.

## Resultado local del candidato 0.1.1

El instalador SHA-256
`A685E0493294AD0CF0CC44286E6D5201D06CEC9462C5B1DE07C579DDE532C304`
superó las 16 comprobaciones de la suite local, incluida la actualización desde
el instalador 0.1.0 real, restauración y rollback, preservación de conflictos y
la separación entre instalar correctamente y fallar al abrir el lanzador.
Informe:
`artifacts/bs2-tests/installer-0.1.1-real-upgrade-f9043a4c174c40a5a4e6ea9b6630f490/summary.json`.
La suite no ejecuta el juego. Sus resultados no sustituyen las pruebas de
cierre del núcleo LAB ni la validación de imagen dentro del visor.

[BS2-EXIT-FIX.md](BS2-EXIT-FIX.md) relaciona este paquete con los hashes de
producción/LAB y documenta por separado seis cierres locales (ventana y menú),
guardado/recarga, las pruebas intermedias fallidas y los límites del observador.
No se presenta una salida forzada del laboratorio ni una AV de primer chance
diagnóstica como una ejecución completa sin excepciones.

## Otro PC: pendiente, no realizada

Registrar Windows, Steam, .NET Framework, GPU/controlador, visor y runtime OpenXR,
y ejecutar el recorrido con este mismo SHA-256 de instalador:

- Descargar/copiar el EXE final y anotar si aparece SmartScreen o antivirus.
- Abrir la interfaz; comprobar caracteres, escala y botones.
- Juego limpio compatible: primera ejecución Steam, instalación y acceso directo.
- Confirmar que un fallo de abrir el lanzador no aparece como fallo de instalar.
- Probar NORMAL, después DLAA/DLSS dentro del visor, incluyendo cargas y menús.
- Guardar e iniciar: comprobar que el lanzador espera al proceso real y mantiene
  un aviso si Steam acepta la petición pero el juego no aparece.
- Reparar, restaurar limpio y restaurar con un archivo modificado posteriormente.
- Verificar INI, partidas, archivos ajenos y cualquier instalación de BS1 intactos.

No publicar ni marcar esta lista como completada únicamente porque pase la suite
local. Conservar fecha, versión/hash, resultados y problemas observados en ese PC.
