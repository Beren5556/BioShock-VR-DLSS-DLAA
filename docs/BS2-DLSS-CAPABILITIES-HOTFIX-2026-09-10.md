# BS2: bloqueo de DLAA/DLSS tras instalar 0.2.15

Estado: corregido el perfil del mod instalado; pendiente de confirmar en una nueva ejecución del juego. Por petición de Carlos se prioriza el mod, sin regenerar ni reemplazar el MSI.

## Causa comprobada

El registro `%LOCALAPPDATA%\BioshockVR\bs2\bioshockvr.log` del 10 de septiembre, 18:09:49, lee `mode=dlaa`, salida 2950x2950 y runtime válido 310.7.0.0. Después registra:

```text
prepare failed: dlss-capabilities.ini no corresponde al juego, DLSS45, dos hosts, runtime 310.7.0 e IPC v8
unavailable: desactivado - using native direct copy
```

El rechazo ocurre antes de lanzar los hosts DLSS. Las solicitudes posteriores también se rechazan y el overlay vuelve al modo anterior. Los registros de hosts del 8 de septiembre no pertenecen a esta ejecución.

El archivo instalado era el perfil genérico aceptado de BS1, sin identidad. El núcleo de BS2 exige `game=bs2` y `adapter=bioshock2r`; esas entradas ya existían en `installer/profiles/bs2/dlss-capabilities.ini`. La selección del payload no las utilizaba porque comparaba una ruta con barras inversas con las barras normales del manifiesto base.

## Cambio aplicado

Con juego, hosts y lanzador cerrados, se añadieron exclusivamente las dos entradas de identidad al archivo:

```text
BioShock 2 Remastered\Build\Final\host64\dlss-capabilities.ini
```

El resultado coincide byte por byte con el perfil BS2 del repositorio. Copia recuperable del archivo anterior:

```text
artifacts/hotfix-bs2-capabilities-20260910/dlss-capabilities.original.ini
```

SHA-256 anterior: `7C52BD6F6F186C40CDA847F0E143BDCFF94F0CB9BAC355977C27C2E27B857D77`.

SHA-256 corregido: `FCB20488F19FB39851FF1683E0C4DA4B4AA71D25B9BE12634AD95BC5AAD5508C`.

No se modificaron binarios, ajustes gráficos, resolución, cachés de los hosts ni archivos de BS1. El núcleo copiará el perfil correcto a los directorios privados por ojo durante la preparación normal. No se ha debilitado la validación del núcleo.

En fuentes, `installer/msi/Build-Msi.ps1` normaliza los separadores antes de seleccionar el perfil. `Test-PayloadProfiles.ps1` prueba el bucle real de selección sin copiar archivos ni ejecutar un instalador, y comprueba el contrato del perfil mediante las API INI de Windows.

## Verificación y pendientes

- 27 comprobaciones correctas: selección BS1/BS2 con ambos separadores, integridad del perfil BS1, rechazo cruzado de perfiles y aceptación del perfil BS2 instalado.
- Hashes del núcleo/host/runtime de BS2, núcleo/perfil de BS1 y MSI del escritorio iguales a los anteriores a esta intervención.
- La configuración sigue en DLAA, 2950x2950. No se ha arrancado el juego automáticamente.
- Falta confirmar que los hosts nuevos arrancan y DLAA se mantiene activo dentro del juego; después se podrá comprobar DLSS.
- El MSI 0.2.15 y los payloads congelados siguen intactos y contienen el perfil anterior. Una reparación/reinstalación puede deshacer este hotfix. La próxima versión deberá reconstruir el payload BS2 con el perfil corregido, ejecutar esta prueba y usarlo como entrada del MSI único; no reutilizar el payload BS2 defectuoso ni sobrescribir una versión ya entregada.

Para repetir la prueba de perfiles:

```powershell
./installer/msi/Test-PayloadProfiles.ps1 -BasePayloadDirectory '<payload BS1 aceptado 0.2.11>' -InstalledBs2Directory '<Build\Final de BS2>'
```

Este resultado resuelve la causa identificada de la vuelta inmediata a NORMAL. No constituye todavía validación de imagen, rendimiento ni estabilidad dentro del visor.
