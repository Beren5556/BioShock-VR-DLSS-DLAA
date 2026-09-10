# Lanzadores BioShock 1–2 VR DLSS/DLAA

La integración 0.2.13 compila la misma interfaz e Imagen para ambos juegos con
Build-Launcher.ps1 -Game both. GameProfile fija rutas e identidad por binario;
BS2 mantiene Armas y Shared.ini. El paquete instala el lanzador en Build\Final,
y el escritorio recibe un acceso directo. BS1 se distribuye todavía mediante
el MSI 0.2.11 intacto. [Estado y pruebas](../../docs/INTEGRATION-0.2.13.md).

## Referencia histórica BS2 0.1.1 / 0.1.1.1

Aplicación nativa WinForms independiente para BioShock 2 Remastered Steam, MOD 0.1.1-beta y lanzador corregido 0.1.1.1. El EXE puede residir fuera del juego: descubre bibliotecas Steam o admite `--game "<ruta a Bioshock2HD.exe>"`.

La corrección 0.1.1.1 se entrega como actualización del lanzador; el instalador
0.1.1-beta congelado no se regenera y sigue incluyendo el lanzador 0.1.1.0.

Validación local 2026-09-08: compilación, `--self-test`, `--self-test-ui` y
`Verify-Repository.ps1` correctos. QA:
`artifacts/bs2-tests/launcher-windowed-0.1.1.1-babfbb9057a34a418e2ec0071c8140b7`.
SHA-256 del lanzador 0.1.1.1:
`06D04339030C193281C40F1672541D6F22E98A50140D1D98B6BAC363F2CEBE33`.
Instalado en Build/Final y copiado a la carpeta de lanzadores del escritorio;
el acceso directo existente apunta al EXE actualizado. El registro instalado
sólo cambia el hash de ese componente: 21/21 archivos registrados verificados,
copias originales intactas. El EXE anterior y el manifiesto anterior se guardan
en `artifacts/bs2-tests/launcher-hotfix-installed-9dba3bca9b7d42799b730c7ef3930a8f`.
Los 34 archivos protegidos comparados antes/después (configuración real, partidas,
núcleo del mod, archivos BS1 e instalador) no cambiaron durante esta actualización.
No se inició el juego. La calidad/resolución efectiva en el visor sigue pendiente
de repetir: seleccionar el perfil deseado y guardar con el lanzador nuevo.

## Compilación

```powershell
.\apps\launcher\Build-Launcher.ps1
```

Salida: `artifacts\launcher\Lanzador BioShock 2 VR DLSS-DLAA.exe`. Se usa el compilador .NET Framework y se sustituye la salida final después de una compilación correcta. Los textos se compilan como UTF-8.

## Configuración de BioShock 2

- Steam AppID 409720; proceso Bioshock2HD; SHA-256 admitido: `C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C`.
- `%APPDATA%\BioshockHD\Bioshock2\Shared.ini [SharedOptions] ViewportX/ViewportY` gobierna el render. El lanzador sincroniza las cuatro claves PC de Bioshock2SP.ini cuando existen.
- Selector: 1920×1080 plano y cuadrados de 1500 a 4050 en pasos de 150, conservando además 2048, 2560, 3072 y 4096. Los valores personalizados siguen disponibles. DLAA mantiene salida=entrada.
- Guardar y Guardar/iniciar preparan `StartupFullscreen=False` en SharedOptions y WinDrv.WindowsClient, también cuando True era heredado y no se editó la resolución. Se valida la pareja completa antes de modificarla; claves ausentes, duplicadas o no booleanas bloquean el guardado. Abrir/recargar no escribe ni cambia esta configuración en disco.
- `%LOCALAPPDATA%\BioshockVR\bs2\vrpreset.ini`: cámara, escala, manos independientes, apuntado, giro, cine y HUD.
- `%LOCALAPPDATA%\BioshockVR\bs2\weapons.ini`: ocho perfiles de arma y 16 campos por perfil, contrastados con `src/game/bioshock2r/aim.cpp`.
- `%LOCALAPPDATA%\BioshockVR\bs2\dlss.ini`: Normal, DLAA nativo y DLSS SR; resolución de salida y calidad calculada a partir de la relación render/salida.

Los valores predeterminados están incorporados al EXE desde los presets oficiales BS2 de la base v0.8.2. Las claves incompatibles de BS1 no se exponen. El gesto de llave inglesa no se ofrece como opción de BS2.

El backend debe declarar en su manifiesto `[backend] game=bs2` y `adapter=bioshock2r`, junto con `phase=DLSS45`, `eyeHosts=2` y `runtime=310.7.0`. El plano cercano se conserva como dato interno; la interfaz pública no ofrece un ajuste manual porque el adaptador usa la proyección capturada.

## Escrituras y arranque

Abrir o recargar no escribe ficheros. Guardar detecta cambios externos, verifica copias de seguridad y sustituye archivos de forma atómica. Shared.ini y su espejo SP forman un lote; vrpreset.ini y weapons.ini forman otro. Si falla la segunda sustitución de un lote, se restauran los bytes originales del primero. Los lotes de imagen, VR y DLSS se guardan secuencialmente; un fallo posterior se informa como guardado parcial. Las claves desconocidas y valores de controles sin editar se conservan.

El botón Guardar e iniciar verifica primero nombre y hash del ejecutable. Solicita Steam AppID 409720 y espera hasta 60 segundos. Solo cierra cuando observa un proceso NUEVO de ruta exacta, iniciado después de la solicitud, y con una ventana que responde durante tres segundos consecutivos. Un proceso antiguo, Steam abierto o un juego de otra ruta no cuentan como éxito. Una salida temprana o timeout conserva el lanzador abierto con un aviso útil. La vía directa solo se intenta si Windows rechaza abrir la URI de Steam.

## Pruebas

```powershell
$exe = '.\artifacts\launcher\Lanzador BioShock 2 VR DLSS-DLAA.exe'
$p = Start-Process -FilePath $exe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
$p.ExitCode
# Para QA visual usar un directorio NUEVO, que todavía no exista:
$p = Start-Process -FilePath $exe -ArgumentList '--self-test-ui "<directorio nuevo de prueba>"' -Wait -PassThru
$p.ExitCode
```

`--self-test` incluye los parsers y las políticas de resolución, perfiles BS2, rollback con segundo archivo bloqueado, copias byte a byte, detección de edición externa, estados de arranque (proceso antiguo, ruta incorrecta, ventana ausente, bloqueo, salida temprana y timeout), y un helper temporal llamado Bioshock2HD.exe con ventana transparente que responde. Nunca inicia el juego real.

`--self-test-ui` crea INIs ficticios con pantalla completa heredada, muestra el lanzador brevemente y verifica apertura sin escrituras. Recorre el guardado común sin otros cambios, ventana coherente, todos los presets y DLAA 1:1, resolución personalizada, espejo SP, Normal/DLAA/DLSS, manos, armas y claves desconocidas. Genera capturas y PASS.txt o FAILURE.txt en el directorio de prueba.

`--sandbox "<directorio>"` permite inspeccionar manualmente la aplicación con AppData aislado y desactiva por completo el inicio del juego.

La comprobación del arranque no acredita calidad DLSS ni una sesión VR estable. Quedan pendientes la validación del MOD nuevo en visor y la prueba de la versión futura en otro ordenador.
