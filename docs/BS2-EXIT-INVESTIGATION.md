# BioShock 2: investigación del fallo al cerrar

Fecha: 2026-09-08, Europe/Madrid. Estado: **reproducido y acotado; no corregido**.
Alcance autorizado: diagnóstico. No se ha cambiado código de producción ni se ha
recompilado, instalado o publicado la beta.

## Conclusión

La variante `Bioshock2HD.exe+0x4FF0FE` es una lectura de un puntero nulo en el
ejecutable del juego después de solicitar el cierre de su ventana. Se reproduce
sin DLSS, sin crear una instancia OpenXR, con los cuatro subsistemas del mod
omitidos y también **sin cargar `bioshockvr.dll`**.

Por tanto, ni NGX/DLSS, ni OpenXR, ni los nuevos hooks temporales de esta beta son
necesarios para provocar esta variante. Coincide con un fallo documentado en el
mod base antes de nuestra adaptación. La evidencia apunta a la ruta de cierre
del motor, pero no demuestra quién deja nulo el objeto ni excluye toda influencia
del entorno: el último control conserva el proxy XInput de aislamiento y los
componentes externos cargados por el juego. No es una prueba de «vanilla pura».

Esto tampoco demuestra que los otros sitios observados de madrugada
(`+0xC312D2` y `+0xC37362`) compartan exactamente la misma causa.

## Pruebas nuevas

Todas se ejecutaron desde el menú, con ventana, resolución privada 1024×1024,
perfiles y copias de partidas separados. Se solicitó `WM_CLOSE`; no se probó la
ruta de menú «Salir a Windows» con confirmación y guardado.

Raíz de evidencias:
`D:\BioShock2VR-DLSS-Lab\game-cc819bfd-7129-4494-97ff-2b9fd80f81a6\runs`.

| Run / PID | Configuración real | Primera excepción de acceso al cerrar | Final de la prueba |
| --- | --- | --- | --- |
| `off-5e58b01c` / 3684 | NORMAL; `BVR_SKIP=xr`; `BVR_VEH=1`; adaptador y D3D activos | `0xC0000005`, `+0x4FF0FE`, READ de dirección 0 | CDB alcanzó segunda oportunidad; el cierre superó 10 s. Se terminó exclusivamente el proceso de laboratorio. No es una medición de cierre natural. |
| `off-83222d24` / 20668 | NORMAL; `BVR_SKIP=input,adapter,d3d11,xr`; `BVR_VEH=1` | La misma instrucción y lectura nula | CDB capturó y se desconectó; proceso terminó con `0xC0000005`. |
| `off-76963be7` / 20120 | Núcleo `bioshockvr.dll` ausente durante todo el proceso; proxy LAB presente | La misma instrucción y lectura nula | CDB capturó y se desconectó; proceso terminó con `0xC0000005`. |

Los logs de inicialización del segundo control confirman que no se instalaron
entrada, adaptador ni hooks D3D11 y que no se creó OpenXR. En el tercero, el
inventario de módulos de CDB no contiene `bioshockvr.dll`; `lm m bioshockvr` no
devuelve ningún módulo y no se genera `bioshockvr.log`.

Cada control conserva `exit-debugger-trace.log` dentro de su run. Los dos últimos
conservan además `close-result.json`. El `run.json` lo genera la preparación:
sus hashes describen los archivos preparados, no prueban que se cargaron. En el
tercer control la DLL se renombró **después** de preparar el run; el inventario
del depurador y esta anotación describen su configuración efectiva.

## Captura técnica

Se utilizó CDB x86 10.0.26100.3916, ya instalado, unido a cada PID cuando la
ventana respondía. No se parcheó memoria del juego para el diagnóstico.

- Excepción: `0xC0000005`, lectura de `0x00000000`.
- Dirección: `Bioshock2HD.exe+0x4FF0FE`; registro `EAX=0`.
- El acceso sigue la cadena global del motor `+0x1A638F0 → +0x4C → +0x44`;
  el último miembro produce el puntero nulo que la siguiente instrucción lee.
- La pila estimada coincide en los tres controles: `+0x30DEF9`, `+0x30CE43`,
  `+0x2E4C49`, `+0x30F8CE`, `+0xCDBC5E`.
- CDB advierte que no dispone de información de desenrollado: esos retornos no
  se presentan como una pila simbólica fiable. Las capturas actuales no muestran
  USER32 en esa cadena; no prueban que la excepción ocurra dentro del WndProc.
- En la primera captura `.ecxr` falló, pero el registro de excepción, los
  registros actuales y la instrucción concuerdan. Las dos siguientes capturas
  obtuvieron los datos actuales sin depender de `.ecxr`.

El depurador cambia el tratamiento de excepciones y los tiempos de cierre.
Estos ensayos sirven para localizar la primera AV, no para comparar el tiempo
de salida normal ni el comportamiento natural del filtro de excepciones.

## Qué hacía ya el mod base

`src/core/util/crash.cpp`, `src/core/ui/overlay.cpp` y
`src/core/framework/dllmain.cpp` no tienen diferencias respecto a HEAD
`1de552a`. El comportamiento de cierre no fue introducido por esta adaptación.

El WndProc marca el inicio de cierre ante `WM_CLOSE`, `WM_DESTROY` o
`WM_ENDSESSION`. Desde ese momento, el filtro clasifica genéricamente una
excepción como fallo conocido del host, omite el dump y llama a
`TerminateProcess(..., 0)`. También existe un vigilante de 15 segundos.

**Código de salida 0 y ausencia de dump no equivalen a cierre limpio.** El texto
actual «terminating cleanly» describe una terminación forzada que oculta el
resultado de error, no una liberación ordenada de recursos. La clasificación
genérica tampoco demuestra por sí misma la causa de cualquier excepción futura.

En los cuatro ensayos de madrugada la AV llegó 8–16 ms después de `WM_CLOSE`,
antes de que se registrara la limpieza normal de OpenXR o
`DLL_PROCESS_DETACH`. No fue el vencimiento del vigilante de 15 s ni la espera
de salida de 3 s del host DLSS.

Antecedentes locales relevantes:

- `docs/bioshock2/ENGINE_NOTES.md:1236–1341`: análisis previo de `+0x4FF0FE` y
  control con todos los hooks omitidos. El control antiguo sin proxy no tenía
  observador de excepciones; por sí solo no demostraba que vanilla fallase.
- `docs/bioshock2/ENGINE_NOTES.md:1414–1433`: ruta alternativa de salida desde
  el menú y guardado previo. Desactivar el flush forzado durante el cierre
  produjo entonces un bloqueo y se revirtió; no debe proponerse de nuevo como
  arreglo sin demostrar un protocolo seguro.
- `docs/STATUS.md:4423`: antecedente de la misma dirección durante una partida
  inactiva. Una dirección coincidente no permite clasificar un fallo como
  «solo al cerrar» sin conocer el estado de la ejecución.
- `docs/BS2-TEST-RESULTS.md:94`: incidencia de los ensayos de la beta.

## Límites y trabajo posterior propuesto

1. Separar en el diagnóstico «cierre ordenado», «fallo durante el cierre» y
   «terminación de protección», conservando el error real y sin llamar limpio
   al resultado de `TerminateProcess`.
2. Para corregir la causa, localizar qué destruye o vacía el objeto y por qué
   sigue siendo consultado. No aplicar un salto por dirección fija ni limitarse
   a tragar la AV: el sitio también tiene antecedentes fuera del cierre.
3. Capturar por separado `+0xC312D2` y `+0xC37362`, y contrastar la salida por
   menú con la salida de ventana. Comprobar guardado, cancelación de salida y
   posible lentitud antes de cambiar el vigilante existente.
4. Validar cualquier parche con NORMAL/DLAA/DLSS, visor real y posteriormente
   otro ordenador. Las pruebas de hoy no sustituyen esas validaciones.

Estos son próximos pasos sugeridos, **no cambios implementados**.

## Incidencia independiente del laboratorio

El primer intento `off-a4248059` terminó durante el arranque del simulador,
antes de pedir cierre: evento Application Error 1000, módulo
`bvr_xrsim32.dll`, código `0xC0000409`, desplazamiento `0x315DE`.
No es la AV investigada ni un fallo demostrado del runtime OpenXR real. Se
excluyó ese intento y los controles posteriores omitieron XR. El simulador es
una herramienta de laboratorio, no el runtime que se entrega en la beta.

## Estado al finalizar

- Verificación final: los 30 archivos originales protegidos —incluidas
  configuraciones, partidas y binarios de la instalación real— coinciden byte
  por byte con el inventario previo.
- La DLL de laboratorio temporalmente renombrada se restauró con su mismo hash.
- No quedan juegos, depuradores ni auxiliares de estas pruebas ejecutándose.
- Núcleo de producción, lanzador e instalador no se reconstruyeron ni cambiaron.
- No se ha aplicado un arreglo ni se ha marcado el fallo como resuelto.

Hashes SHA-256 verificados al finalizar:

| Archivo | SHA-256 |
| --- | --- |
| Instalador 0.1.0-beta del escritorio | `A61BCCE385C0095645C67FE27A937E0D2B661E3C8CE2805F401C273EA73D9D6D` |
| Núcleo de producción | `8DFD11347E8B80623CC3678BD030BD9B77B76FACA9BEF3766C1E66E224D0554A` |
| Lanzador de la beta | `613772D164550745AB3A58924CF8A156EBCB95DE312869D01166361CA4A0EF8D` |
| Núcleo LAB restaurado | `A32EABCE3EE62A2EC502875AF45A21D19857E215A5630DB675A2B7D8A503E4F6` |
