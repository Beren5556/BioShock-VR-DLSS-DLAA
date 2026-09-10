# BioShock 2: corrección del orden de cierre, 0.1.1-beta

Fecha: 2026-09-08, Europe/Madrid.

**Estado: validación local del cierre; visor y otro ordenador pendientes.** La
sección final separa los cierres de ventana y menú verificados en laboratorio,
la comprobación de guardado y los hashes de la compilación/paquete. El historial
intermedio conserva sus fallos. Estos resultados no certifican el runtime del
visor real ni convierten una recuperación forzada del laboratorio en un pase.

Este trabajo continúa el diagnóstico conservado, sin reescribirlo, en
[BS2-EXIT-INVESTIGATION.md](BS2-EXIT-INVESTIGATION.md). Todas las pruebas descritas
en este documento usan la copia aislada y perfiles/partidas privados; no el juego
real del usuario. Raíz de evidencias:

`D:\BioShock2VR-DLSS-Lab\game-cc819bfd-7129-4494-97ff-2b9fd80f81a6\runs`.

## Causa localizada y corrección

El análisis completo de la función que contiene `+0x4FF0FE` corrige una
interpretación incompleta de las notas antiguas: no es solamente una función de
aplicación de ajustes de pantalla. Es `UGameEngine::Tick(float)`, con ese trabajo
al principio. El motor accede al primer elemento de `Client->Viewports` sin
comprobar antes si la lista está vacía.

El propio Tick **ya tiene una rama de salida para una lista de viewports vacía**,
pero la ejecuta después del acceso inválido. Esa rama llama a la función nativa
del motor `RequestExit(0,0)` y retorna. La corrección adelanta esa misma decisión
cuando se han verificado las identidades del motor y del cliente. No inventa un
valor de retorno, no escribe directamente los flags del motor y no captura una
excepción del Tick para continuar desde otra instrucción.

Derivación estática reproducible sobre `Bioshock2HD.exe` x86, SHA-256
`C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C`:

- Vtable del motor `+0x10BD7DC`, slot `+0xF4`, thunk `+0x174E` y cuerpo
  `+0x4FF0D0`. La llamada en `+0x30DEF3` pasa un float; el callee termina con
  `ret 4` y el caller no consume un resultado.
- Global del motor `+0x1A638F0`; cliente en motor `+0x4C`; datos de la lista en
  cliente `+0x44` y número de elementos en `+0x48`.
- Rama nativa `+0x4FF33C…+0x4FF365`: cliente no nulo y número de viewports cero,
  `RequestExit(0,0)`, retorno. El acceso que fallaba está antes, en `+0x4FF0FE`.
- `RequestExit`: thunk `+0x9345`, cuerpo `+0xB60C00`, firma cdecl con dos enteros.
  Con el primer argumento cero no llama a `ExitProcess`: publica `WM_QUIT`,
  activa la salida del bucle principal y conserva el estado de salida indicado.

El guard verifica prólogo, accesos, rama original, epílogo, thunks, slot de vtable
y operandos relocalizados. Si la versión del ejecutable o la identidad del
objeto no coincide, no aplica el cambio. Con viewports presentes, el Tick
original recibe los mismos argumentos. `BVR_SKIP=exit_guard` permite una
comparación controlada sin reconstruir el núcleo.

## Por qué no bastaba con corregir el Tick

El primer arreglo pasó el cierre plano, pero **falló en el siguiente ensayo con
VR**. Ese resultado negativo forma parte del diagnóstico, no se descarta:

| Run | Variante intermedia | Resultado observado |
| --- | --- | --- |
| `off-06348445`, PID 20448 | Guard del Tick, XR omitido | `WM_CLOSE` → `WM_DESTROY` → lista vacía → salida nativa → limpieza runtime → `DLL_PROCESS_DETACH`, código 0, sin AV registrada. |
| `off-566074aa`, PID 23544 | Mismo guard, NORMAL con simulador OpenXR activo | AV de otro hilo en `+0xC3CFBD`, lectura de `0x80`; cierre no limpio, código real `0xC0000005`. |

En el ensayo negativo, `WM_DESTROY` llegó a las 10:08:56.387; la AV del hilo
15464, a las .389. El hilo principal 18588 alcanzó el guard y empezó la limpieza
XR a las .390. Por tanto, esa AV sucedió **antes** de nuestra liberación del
runtime. La instrucción intentaba llamar un método a través de una vtable nula
de otro objeto. El registro contiene un escaneo de direcciones de pila, no un
desenrollado simbólico fiable: no identifica por sí solo ese hilo como render o
Flash, ni demuestra que todas las variantes de cierre tengan una única causa.
El intento de escribir el minidump falló; el registro de excepción y su código
se conservaron. No se convirtió el error en código 0.

La segunda parte del arreglo cambia únicamente el cierre de la ventana BS2
verificada: al recibir `WM_CLOSE`, llama a `RequestExit(0,0)` **mientras el
viewport sigue vivo** y consume ese mensaje. La destrucción queda a cargo de la
secuencia nativa de salida del motor, en lugar de permitir que `DestroyWindow`
invalide objetos mientras siguen trabajando otros hilos. La prueba causal
resultante es una mejora del orden de cierre; no un parche por dirección a cada
excepción posible.

Las rutas de menú no se interceptan mediante comandos, teclas o respuestas
automáticas a sus diálogos. Una identidad no verificada devuelve el mensaje al
procedimiento original. La recepción de `WM_CLOSE` se registra separadamente de
la aceptación de la salida por la función nativa.

## Cobertura del menú: fallo encontrado y punto común de salida

La prueba posterior `sr-a75cfe6c`, PID 23752, encontró un hueco independiente:
**el menú podía aceptar la salida y terminar el bucle sin otro Tick ni un
`WM_CLOSE` previo**. El guard de lista vacía y la ruta de ventana no bastaban
para solicitar la limpieza XR en ese caso.

El ensayo se hizo después de gameplay con SR real procesado y conservó estos
resultados por separado:

- Cancelar «Salir a Windows» con Escape permitió reanudar el juego sin activar
  la limpieza terminal. La telemetría volvió a mostrar pares estéreo coherentes
  e historial válido; cancelar no se trató como salida aceptada.
- Aceptar salir y después aceptar guardar creó una ranura privada nueva,
  `9_8_2026_10_31_35.bsb`, de 967136 bytes. Esto confirma que se escribió el
  archivo; su existencia no convierte el cierre posterior en correcto ni
  sustituye una recarga de la partida.
- Una traza CDB del cuerpo verificado `+0xB60C00` registró
  `RequestExit(force=0,status=0)` en el hilo `0x5CB8` / 23736, el hilo de juego
  observado. El retorno inmediato fue `+0x4F176F`; la pila lo sitúa dentro de
  Tick, pasando por `+0x50050E`, el wrapper y `+0x30DEF9`. No muestra una llamada
  desde Present. Los marcos sin símbolos son evidencia de dirección, no una
  garantía de desenrollado completo.
- Después llegó `WM_DESTROY` a las 10:31:38.218, sin registro de limpieza XR.
  El depurador continuó desde el breakpoint y acabó registrando una excepción
  de segundo chance `0xC0000409`, subcódigo 7 `FAST_FAIL_FATAL_APP_EXIT`, en el
  runtime de laboratorio `bvr_xrsim32+0x315DE`. No se observó una AV
  `0xC0000005` antes de ese fallo. El código 1 del proceso CDB **no es** el
  código de salida del juego; el observador de proceso tampoco produjo un
  resultado de cierre limpio utilizable para este ensayo.

El registro privado es `sr-a75cfe6c/native-request-trace.log`. Se conserva este
resultado como **fallo de cierre del menú**, aunque la cancelación y la escritura
del guardado anteriores funcionaron. La captura demuestra que faltaba invocar
la limpieza explícita en esa ruta; no demuestra por sí sola todos los detalles
internos que llevan al fail-fast del simulador.

La corrección resultante añade un hook al mismo `RequestExit` cuya huella completa
ya se verificaba. Es el punto común donde el motor acepta salir, después de sus
diálogos y del flujo nativo de guardado:

1. El original recibe siempre los dos argumentos intactos y se ejecuta primero.
   No se captura ni se transforma una excepción de esa llamada.
2. Sólo un retorno con `force=0` confirma la salida para nuestra limpieza.
   `force!=0` conserva el comportamiento forzado original, sin reinterpretarlo
   como cierre limpio. El valor `status` no se sustituye por cero.
3. Un latch común, activado después del original y antes de liberar recursos,
   evita repetir la limpieza, incluso ante reentrada. Los originales de las
   llamadas nativas repetidas siguen recibiendo sus argumentos.
4. Las rutas propias de ventana y lista vacía llaman a ese mismo punto; ya no
   tienen una segunda implementación de la limpieza. Los hooks de Tick y
   RequestExit se publican como pareja sólo si ambos se habilitan. Ante un fallo
   se intenta retirar ambos; un fallo de rollback se registra explícitamente y
   cualquier detour aún alcanzable permanece como reenvío pasivo.

Esto no selecciona respuestas de guardado ni evita el diálogo de cancelación.
La cobertura de menú con guardar/cancelar se repitió después con la compilación
que contiene este hook; el resultado y la revisión del observador figuran en la
sección final. Los pases de ventana anteriores no sustituyen esa comprobación.

## Limpieza VR y tratamiento de errores

Después de aceptar la salida nativa se solicita una limpieza terminal fuera de
`DllMain`. Un gate BS2 impide entrar en nuevas operaciones propias de
Present/Resize/inicialización y protege también los tres callbacks de las guías
temporales. Los hilos observadores y de pacing deben terminar antes de liberar
los recursos que usan; las esperas son acotadas. Si no se pueden retirar de
forma segura, los recursos se conservan y se registra cierre incompleto.

La liberación incluye sesión/instancia OpenXR, swapchains y auxiliares DLSS.
Los fallos detectados al finalizar el frame o destruir recursos OpenXR dejan un
error persistente y no permiten etiquetar la limpieza como completa. OpenXR no
ofrece timeout para todas esas llamadas: el vigilante de cierre confirmado
sigue siendo la protección final, no una prueba de liberación ordenada.

La política de errores BS2 también cambia:

- Una solicitud sin confirmación no arma por sí sola el vigilante.
- Un fallo durante un cierre confirmado conserva informe y código de error.
- El vigilante de 15 segundos termina con `WAIT_TIMEOUT` si vence; no con cero.
- La protección heredada de los otros adaptadores no se cambia en esta tarea.

Un código 0 aislado sigue sin bastar. Para los ensayos se exige además ausencia
de AV registrada, limpieza runtime completa y `DLL_PROCESS_DETACH` ordenado.

## Pruebas intermedias de la ruta de ventana corregida

Estos dos ensayos son **anteriores al último refuerzo de registro de errores de
destrucción OpenXR**. Se conservan como intermedios; las repeticiones con la
compilación final están separadas al final del documento.

| Run | Alcance real | Evidencia |
| --- | --- | --- |
| `off-e6e8e456`, PID 18904 | NORMAL, menú, simulador OpenXR | A las 10:14:13.040 la salida nativa se pidió con 1 viewport vivo; limpieza completa .305; detach .455. Código 0, sin AV registrada. |
| `dlaa-1a5027ad`, PID 22516 | Modo DLAA seleccionado, menú, simulador OpenXR | A las 10:16:00.867 la salida nativa se pidió con 1 viewport vivo; limpieza completa 10:16:01.301; detach .464. Código 0, sin AV registrada. |

El segundo ensayo registra `processed=0`: el menú permaneció en fallback por
falta de profundidad útil. **No valida evaluación DLAA activa en gameplay**, ni
calidad visual o rendimiento en el visor. El runtime simulado tampoco sustituye
Virtual Desktop/OpenXR en un visor real.

Pruebas automáticas del contrato: 364 casos sintéticos, incluidas todas las
mutaciones de bytes de las huellas, dos bases de carga y combinaciones de
identidad/número de viewports. Con `--audit-image` se añaden seis verificaciones
sobre el PE instalado —370 casos—, sin ejecutar el juego. Ambas pasaron en la
compilación intermedia.

El contrato de RequestExit añade 424 casos de preservación de argumentos,
orden original/limpieza, modo forzado, función desactivada, repetición, reentrada
y excepción original. La compilación final pasó **794 casos con el audit del
PE, cero fallos**: incluye los 788 casos sintéticos y las seis verificaciones
del ejecutable. También se repitieron con éxito los 23 casos de política de
cierre, 15 del gate, la prueba WARP y 19 de aislamiento de rutas LAB.

## Riesgos y criterios de aceptación pendientes

1. Las rutas de menú SR con guardar antes de salir y cancelar/reanudar, y DLAA
   sin guardar, ya se repitieron por separado con el hook común de RequestExit;
   véanse sus resultados finales. Esto no cubre universalmente todos los
   diálogos, estados del motor ni posibles callers desde otros hilos.
2. Verificar los archivos guardados, no solo que el proceso desaparezca. La nueva
   ruta de ventana no añade un guardado automático ni promete mostrar un aviso
   de guardado. Durante gameplay debe preferirse la salida desde el menú. La
   recarga del ensayo SR valida la ranura anterior; la nueva ranura escrita en
   SR se volvió a cargar en el ensayo DLAA posterior, conservando su hash.
3. Los cuatro ensayos de ventana de la sección final ya incluyen NORMAL, DLAA
   y DLSS en una partida de laboratorio. Esa cobertura no sustituye la prueba
   independiente de menú/guardado documentada abajo ni la validación del visor
   real todavía pendiente.
4. Repetir con visor real, cambios de foco, posible runtime bloqueado y después
   en otro ordenador. Una API de limpieza reentrante o demasiado ocupada debe
   informar cierre incompleto; no borrar el resultado de error.
5. Las direcciones históricas `+0xC312D2` y `+0xC37362` no tienen aún una captura
   causal individual que permita declararlas resueltas para toda circunstancia.
6. Las rutas propias que piden la salida nativa exigen huellas e identidades
   verificadas. La observación del RequestExit original exige la huella completa
   de la pareja; no necesita que el cliente siga vivo, pues no accede a él. Un
   build distinto queda sin este arreglo y lo indica en el registro.

## Bloqueo de instancia en el laboratorio: no es una prueba pasada

Después del fallo del menú en `sr-a75cfe6c`, PID 23752, quedó un proceso residual
visible mediante CIM con un hilo (23736) y 1163 handles, mientras las consultas
.NET de proceso ya lo trataban como terminado. Un nuevo arranque encontró el
diálogo «BioShock 2 ejecutándose». No se atribuye ese residual a un mecanismo
interno concreto sólo a partir de ese estado contradictorio.

El ensayo `off-1533ecd5`, PID 11556, se quedó en `init complete; waiting for
first Present`: no llegó al gameplay ni constituye una prueba del cierre
corregido. Al cerrar el diálogo registró una excepción no manejada
`0xC0000005` en `Bioshock2HD.exe+0x30CB4C` a las 10:45:19.434; el minidump quedó
en `off-1533ecd5/data/crash/bvr_20260908_104519.dmp`. **No se cuenta como PASS**,
aunque el proceso acabara desapareciendo.

La recuperación del entorno se hizo con `Win32_Process.Terminate` vía CIM,
exclusivamente sobre el PID residual cuya ruta se verificó como la copia LAB.
Se cerraron también los auxiliares identificados de aquel ensayo. Dos
comprobaciones posteriores no encontraron ningún proceso/hilo BS2 residual.
No se cambiaron contextos de hilos, registro, archivos del juego real ni fue
necesario reiniciar Windows. La recuperación forzada del laboratorio no se
presenta como una salida limpia del juego; permitió realizar los ensayos
independientes siguientes.

## Resultados finales y entrega

### Identificación de las compilaciones

Los `run.json` y `exit-test.json` de los cuatro ensayos siguientes identifican
esta pareja de compilaciones. El código ejecutado por estas pruebas es el
núcleo **LAB instrumentado**, no se afirma que ambos binarios sean idénticos.

| Artefacto | SHA-256 |
| --- | --- |
| Núcleo LAB ejecutado (`bioshockvr.dll`) | `B4474C2681C46609A32E98AE848C2216B3EC18C2E94C8383FFE9C844908A751D` |
| Núcleo de producción asociado | `E6FB724A2D8024A85E8879972F8E1612A3724B7CD135EA1FE2B60AC03D0DB5B3` |
| Instalador 0.1.1 probado | `A685E0493294AD0CF0CC44286E6D5201D06CEC9462C5B1DE07C579DDE532C304` |

La suite del instalador registra **16 comprobaciones, `passed=true`**, a las
08:45:27 UTC, en
`artifacts/bs2-tests/installer-0.1.1-real-upgrade-f9043a4c174c40a5a4e6ea9b6630f490/summary.json`.
Incluye instalación/reparación/restauración, actualización desde el instalador
0.1.0 real y rechazo de downgrade, rollback de fallos inyectados, conservación
de conflictos/originales y separación de error de instalación frente a error
al abrir el lanzador. No inicia el juego y mantiene expresamente pendientes el
visor y otro ordenador. El informe anterior
`installer-0.1.1-real-upgrade-0719596bdd8745b1aaf6bc885a815629/summary.json`
conserva `passed=false` y 14 comprobaciones completadas; no se cuenta como pase
ni se elimina del historial.

### Cierres de ventana con la corrección común final

En los cuatro casos, `close-result.json` informa `exited=true`, `exitCode=0`,
`cleanExit=true`, `runtimeShutdownComplete=true`, `orderlyDetach=true`, sin
fallos de primer chance ni de teardown **dentro del intervalo de cierre**. El
log confirma en ese orden la aceptación de `RequestExit(0,0)`, limpieza terminal
completa y `DLL_PROCESS_DETACH`. Todos usan `BVR_VEH=1`.

| Run / PID | Alcance | Última evidencia de procesamiento antes del cierre | Cierre observado (hora local) |
| --- | --- | --- | --- |
| `off-ac33d37f` / 18024 | Menú plano, OpenXR omitido (`skipXR=true`) | No se crea sesión XR ni se evalúa DLSS. | **PASS de cierre**: solicitud 10:46:37.008; limpieza .158; detach .324. |
| `off-d61eb964` / 18716 | NORMAL en partida privada, simulador OpenXR activo | Estado `GAMEPLAY` confirmado a las 10:46:50.963, sin DLSS. | **PASS de cierre**: solicitud 10:46:57.733; limpieza 10:46:58.210; detach .621. |
| `dlaa-0766826d` / 15944 | DLAA en partida privada, simulador OpenXR | A las 10:47:30.569: `processed=1184`, `gen L/R=592/592`, ambos `coherent=1`, `hist=1`, `epoch=8`; `mixed=0`. | **PASS de cierre**: solicitud 10:47:30.778; limpieza 10:47:31.480; detach 10:47:32.060. |
| `sr-22a15855` / 25356 | DLSS SR en partida privada, simulador OpenXR | A las 10:48:04.208: `processed=1050`, `gen L/R=525/525`, ambos `coherent=1`, `hist=1`, `epoch=8`; `mixed=0`. | **PASS de cierre**: solicitud 10:48:04.480; limpieza .996; detach 10:48:05.592. |

Los contadores son el último muestreo escrito, no un total inferido al terminar
el proceso. `gen` es el contador de evaluación por ojo del registro, no una
afirmación de Frame Generation. DLAA y SR también conservan en el log fallback
de arranque/menú (758 y 762 respectivamente) y un `tagMismatch=1` acumulado;
no se borra esa información al describir el estado coherente del último par.

**Límite de la afirmación «sin AV»:** las tres ejecuciones con gameplay
registraron dos AV de primer chance en `bioshockvr.dll+0x2BF68`, antes de pedir
el cierre: NORMAL 10:46:57.193/.194, DLAA 10:47:19.307 y SR 10:47:53.810.
Los procesos continuaron hasta los cierres ordenados anteriores. La revisión
offline identificó la instrucción como la lectura `v = sp[i]` de
`watchdog_all_threads()`, en `src/core/vr/openxr_runtime.cpp:1406`: una sonda de
pila protegida con `__try/__except` que detiene el escaneo al llegar a memoria
ilegible. La correspondencia se obtuvo del objeto COFF de la compilación
(`openxr_runtime.obj`) y del código de la DLL B447…, no de un PDB disponible:
inicio de función RVA `0x2BCF0` + desplazamiento `0x278` = `0x2BF68`.

Los tres `data/pacetrace.log` sitúan esas excepciones dentro del volcado
diagnóstico de hilos disparado por una pausa de Present de cuatro segundos,
antes del cierre. NORMAL termina la enumeración a las .197, DLAA a las .310 y
SR a las .812, en cada caso con `seen=64`, `reported=64`, `openFail=0`,
`suspFail=0` y `ctxFail=0`. Esta evidencia corresponde a una lectura diagnóstica
SEH manejada, no a una AV no manejada de teardown. Se conservan los registros:
la formulación precisa sigue siendo «cierre ordenado sin AV durante su
intervalo», no «ninguna excepción de primer chance en toda la sesión».

### Menú SR: recarga, cancelación y guardar antes de salir

`sr-af337369`, PID 9992, usa el núcleo LAB B447… y termina con **PASS de cierre
de menú revisado**, con la salvedad documental del observador inicial indicada
abajo. No se deduce este resultado de un cierre WM_CLOSE: el log muestra la
aceptación nativa del menú, sin un WM_CLOSE que la preceda.

| Comprobación | Evidencia |
| --- | --- |
| Recarga de la ranura anterior | `menu-test.json` identifica la copia de `9_8_2026_10_31_35.bsb`, SHA-256 `D58A9F68BABFDD07238A6FE28532EFAA78FD8F017C9C2959B7FEAAB34C76AA3A`, procedente de `sr-a75cfe6c`. El agente principal verificó visualmente la partida después de «Continuar»; el log confirma `GAMEPLAY` a las 10:49:08.210. |
| Cancelar la salida y reanudar | `menu-cancel-result.json`, 10:51:19 local: `cancelAccepted=true`, `gameResumed=true`, `nativeExitAlreadyCalled=false`, `shutdownAlreadyStarted=false`. A las 10:51:20.810, L/R muestran `gen=2026/2026`, `coherent=1`, `hist=1`, `epoch=10`. |
| Guardar antes de salir | Nueva ranura privada `9_8_2026_10_52_33.bsb`, 969738 bytes, SHA-256 `CB844DE5E9F8B76A194D3EED9A03D46A81E9B0C6B5AB44204A6E11B7A8DFD139`. La escritura está inventariada en `menu-close-result.json`; la ranura anterior permanece con el mismo hash. |
| Salida nativa y limpieza | `RequestExit(force=0,status=0)` aceptado a las 10:52:36.900 en hilo 24436; limpieza XR completa 10:52:37.157; `DLL_PROCESS_DETACH` .622; código de proceso observado 0. |

El último contador acumulado es `processed=7122`, `gen L/R=3561/3561`.
Mientras se está en los diálogos, el registro vuelve al fallback por falta de
profundidad: no se presenta el último muestreo de menú como un par nuevo de
gameplay. La reanudación coherente citada en la tabla pertenece a su momento
correspondiente, después de cancelar.

**El informe original negativo no se reescribe.**
`menu-close-result.json` conserva `cleanExit=false` y `faultInLog=true`, aunque
también registra código 0, aceptación nativa, limpieza completa y detach. La
primera versión del observador buscaba AV de primer chance en **todo** el log:
las dos líneas de la sonda `+0x2BF68` ocurrieron a las 10:49:13.853, más de tres
minutos antes de la salida aceptada.

La revisión separada `menu-close-reviewed.json` conserva el enlace al informe
original y declara `cleanExit=true`, `faultDuringExit=false`,
`unhandledFaultInRun=false`, `firstChanceLogLinesBeforeExit=2`,
`originalReportPreserved=true`. Es un resultado revisado por ámbito temporal,
no una eliminación de excepciones ni un cambio del código de proceso.

Para los ensayos siguientes, `scripts/Watch-BS2-MenuExit.ps1` usa como inicio
del intervalo la aceptación nativa de RequestExit. Sigue exigiendo código 0,
limpieza completa y detach, rechaza fallos de primer chance/limpieza dentro de
ese intervalo y rechaza señales de fallo no manejado en cualquier parte de la
ejecución. Las líneas de primer chance anteriores se cuentan explícitamente en
el informe. Esto aplica el criterio de cierre usado en WM_CLOSE sin esconder
los diagnósticos previos.

### Menú DLAA: recarga del nuevo guardado SR y salida sin guardar

`dlaa-fb6f44e2`, PID 26140, núcleo LAB B447…: **PASS de cierre de menú sin
guardar**. Es una ejecución nueva y separada de SR. `menu-test.json` identifica
la ranura `9_8_2026_10_52_33.bsb`, SHA-256
`CB844DE5E9F8B76A194D3EED9A03D46A81E9B0C6B5AB44204A6E11B7A8DFD139`, que
acaba de crear el ensayo anterior. El agente principal la cargó y comprobó la
partida; el log confirma `GAMEPLAY` a las 10:56:03.413. La prueba de guardado
queda así complementada por una recarga posterior de ese mismo archivo.

| Comprobación | Evidencia |
| --- | --- |
| DLAA después de cargar la nueva ranura SR | Contadores acumulados `processed=4006`, `gen L/R=2003/2003`. Al volver a los diálogos el registro muestra fallback por falta de profundidad; no se atribuye a esos diálogos una nueva evaluación de gameplay. |
| Elección de salida sin otro guardado | El agente principal confirmó «Sí» para salir y «No guardar» en el segundo diálogo (Escape), comprobado en la interfaz. Después se produjo la salida nativa. |
| Partidas preservadas | El inventario final contiene las mismas tres ranuras del perfil privado del ensayo. Nombres, tamaños y hashes se conservaron; no se creó ni modificó ninguna partida al salir sin guardar. La ranura nueva SR mantiene 969738 bytes y el hash CB844… indicado. |
| Salida y limpieza | Aceptación nativa 10:57:37.785 en hilo 6632; limpieza completa .989; detach 10:57:38.434. `menu-close-result.json`: código 0, `cleanExit=true`, aceptación/limpieza/detach verdaderos, `faultDuringExit=false`, `unhandledFaultInRun=false`. |

Este informe ya usa el observador con ámbito temporal explícito. Conserva
`firstChanceLogLinesBeforeExit=2`: el probe SEH `+0x2BF68` se registró a las
10:56:09.766, antes de aceptar salir, no durante la limpieza. No requiere una
reescritura del resultado ni se cuentan esas líneas como inexistentes.

La validación local documentada suma **cuatro cierres por ventana y dos por
menú**, con las limitaciones de cada fila y la revisión transparente del
observador SR. No es una garantía universal para otra versión del ejecutable,
un RequestExit desde un worker que mantenga locks del motor, reentrada dentro
de Present/Resize ni un runtime externo bloqueado. Visor real y otro ordenador
siguen pendientes. Las comprobaciones finales de archivos originales protegidos
y el estado de entrega corresponden al agente principal.
