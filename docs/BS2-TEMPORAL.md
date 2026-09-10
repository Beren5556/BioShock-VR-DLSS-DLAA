# BioShock 2: contrato temporal DLAA/DLSS

Estado: port técnico en validación, no declaración de calidad final en visor.
Juego objetivo: **BioShock 2 Remastered**, `Bioshock2HD.exe`, Steam AppID `409720`.
El ejecutable original de BioShock 2 no está soportado por este adaptador.

## Separación respecto de BioShock 1

El proveedor es `src/game/bioshock2r/temporal_guides.*`. Conserva el contrato de
recursos del backend común, pero no usa cámaras, hooks, matrices ni direcciones
de `bioshock1r`. Los datos de producción del mod BS2 viven en
`%LOCALAPPDATA%\BioshockVR\bs2\`, incluido `dlss.ini`.

La configuración gráfica del juego es distinta: `Shared.ini` es la autoridad de
resolución, con espejo en `Bioshock2SP.ini`, bajo
`%APPDATA%\BioshockHD\Bioshock2\`. No se modifica `Bioshock.ini` de BS1.

El flujo de cada ojo es:

```text
Draw BS2 etiquetado -> pose final + proyección WORLD del mismo build
                   -> profundidad D24 del intervalo de render
                   -> profundidad R32F + movimiento R16G16F
                   -> host NGX x64 independiente por ojo
                   -> resultado a la swapchain OpenXR del mismo ojo
```

## Cámara y proyección WORLD: identidad exacta

Cada `sr_push_eye` devuelve un identificador monotónico. `scenedraw` lo conserva
en un ámbito local al hilo, incluido el segundo Draw; los Draw anidados no reciben
por accidente la etiqueta del ojo exterior. `camera` publica únicamente la pose
final de gameplay, después de aplicar el desplazamiento del ojo. La publicación
se consulta por **ojo y build exactos**, con antigüedad máxima de 200 ms; nunca se
reemplaza por «la última cámara disponible».

La matriz no se deduce de los valores configurados para el visor. Se observa el
constructor del `FPlayerSceneNode` raíz que crea `UGameEngine::Draw`, y dentro de
ese ámbito se captura exclusivamente su constructor de proyección **primaria
WORLD**. Las cámaras de portales/reflejos y la proyección secundaria de foreground
no son elegibles, aunque compartan la misma tangente por el ajuste de FOV de armas.

Además, la pose que recibió el constructor debe coincidir con la publicación de
CalcView: tolerancia de 0,01 unidades Unreal en posición y coincidencia exacta de
rotación módulo 65536. Una publicación doble de cámara o proyección se rechaza;
no se elige silenciosamente la última.

### Evidencia binaria específica de BS2

Auditoría local de 2026-09-08, SHA256 del ejecutable:

`C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C`

| Punto | RVA BS2 / significado |
| --- | --- |
| Creación desde Draw raíz | CALL `0x4EF44C`, retorno `0x4EF451` |
| `FPlayerSceneNode` | thunk `0x5C95` -> constructor `0x5BBD40`; RTTI/vtable `0x10CF750` |
| Actualización de matrices | CALL `0x5BBEB7` -> thunk `0x59E3` -> cuerpo `0x5C34A0` |
| Proyección primaria WORLD | retorno `0x5C4364`, destino nodo `+0x1D0` |
| Proyección secundaria foreground | retorno `0x5C43AF`, destino nodo `+0x380`; excluida |
| Constructor de perspectiva finita | cuerpo `0x5BBC00`, seis floats, `ret 0x18` |

La rama WORLD incorpora explícitamente la opción FOV del juego antes de la
conversión de aspecto; la secundaria usa su otro parámetro de lente. Esa cadena
de código identifica WORLD independientemente de una coincidencia numérica con
el FOV de foreground. Se comprueban los destinos CALL/JMP y el prólogo conocido
antes de instalar los hooks; una discrepancia desactiva la vía temporal sin
desactivar por sí sola la cámara VR.

Receta de auditoría reproducible, ABI y pruebas:
[temporal_guides_bs2_test/README.md](../src/tools/temporal_guides_bs2_test/README.md).

## Profundidad y planos reales por ojo

El observador lee los argumentos **reales** de near/far y valida la matriz
resultante en el mismo build. La función auditada genera perspectiva D3D finita:

```text
m10 = far / (far - near)
m14 = -near * far / (far - near)
m11 = 1
tanHalfFovX = 1 / m0
tanHalfFovY = 1 / m5
```

Se exigen entradas finitas, diagonal de lente positiva, ceros esperados y
`0 < near < far`. Matrices invertidas o asimétricas inesperadas se rechazan.
El selector de far del motor tiene una rama 1024 y otra que lee una global
inicialmente 65536; near parte de una global inicialmente 10. **No se presupone
qué rama está activa**: los valores capturados por ojo gobiernan la reconstrucción.
Los hints near/far de `PrepareDesc` no sustituyen esa observación.

La convención validada es profundidad normal: near -> 0, far -> 1. La textura D24
se convierte a R32F conservando su valor de profundidad hardware, sin inversión,
para NGX. Near/far se usan para reconstruir posición y movimiento, no para enviar
una distancia lineal como si fuese la profundidad hardware.

El DSV se elige mediante votos de draws dentro del intervalo, limitado a D24 de
dimensiones correctas, una muestra y mip 0. Se observan binds/draws/clears y se
conserva/restaura el estado D3D11 al copiar una profundidad todavía enlazada.
Esta selección sigue siendo heurística por actividad de render, no una identidad
semántica del objeto profundidad del motor.

Las tangentes de composición y, cuando existe, la lectura independiente del
bloque WORLD deben concordar con la matriz capturada. Una discrepancia produce
fallback, no una corrección inventada de FOV.

## Movimiento de cámara y jitter cero

Los vectores son **solo de cámara**, reconstruidos desde profundidad y las poses
actual/anterior de ese mismo ojo. Convención: píxel actual -> píxel anterior,
en píxeles de render, escalas `1,1`, textura `R16G16_FLOAT`.

Las manos, armas duales, enemigos animados, partículas, agua y otros objetos con
movimiento independiente no reciben todavía sus propios vectores. Pueden aparecer
estelas y otros errores temporales aunque la cámara, profundidad e IPC sean
coherentes. El cuerpo/arma VR funcional no equivale a tener sus vectores de
movimiento implementados.

La proyección raster del juego **no recibe jitter temporal**. El cliente envía
`jitterX=0` y `jitterY=0` tanto en DLAA como en SR; el host los entrega como tales
a NGX. No se afirma una secuencia subpíxel que el juego no ha dibujado. La falta de
jitter limita la información temporal disponible, especialmente para SR: procesar
correctamente una imagen no acredita calidad equivalente a una integración
completa con jitter y vectores de objetos.

## Historial y rechazo seguro

Cada ojo posee sus propias texturas e historial. El primer frame válido solicita
reset. También lo solicitan:

- Cambios de tangentes, near o far.
- Saltos en la secuencia del mismo ojo, que normalmente avanza de dos en dos.
- Cambio de época de cámara: vista, gameplay/cinemática, modo, recenter o rearmado
  del estéreo; también un fallo en la segunda pasada.
- Antigüedad del historial superior a 200 ms o reloj no monótono.
- Corte grande: desplazamiento por frame de 256 UU o más, o giro de 45 grados o
  más en cualquiera de los ejes.

Las dimensiones nuevas recrean recursos y limpian ambos historiales. Cámara,
build, profundidad o proyección ausentes/ambiguos, datos no finitos, build fuera
de orden o desacuerdo WORLD devuelven `false` para que core aplique su salida de
seguridad. El fallback espacial no se etiqueta como procesamiento DLSS/DLAA.

## Lectura de registros y validación

Registros de producción: `%LOCALAPPDATA%\BioshockVR\bs2\bioshockvr.log` y los
logs separados de ambos hosts dentro de ese mismo directorio de datos.

| Señal | Qué acredita |
| --- | --- |
| `root WORLD finite projection observation ready` | Firmas y ramas verificadas, hooks instalados; todavía no acredita gameplay |
| `rootWorld=1 samePose=1 finite=1` | Muestra de proyección raíz y pose observadas válidas |
| `pubs=1 projPubs=1`, `build/cam` iguales | Publicaciones únicas para el build exacto del ojo |
| `nearFar=... epoch=...` | Planos realmente capturados y época de cámara |
| `coherent=1 hist=1 reject=none` | Entradas aceptadas e historial continuo de ese ojo |
| `processed` creciente y hosts sin error | Se está ejecutando la vía NGX, no únicamente fallback |

El primer log de captura es one-shot por ojo y puede producirse ya en menú con
`samePose=0`: allí no existe necesariamente publicación de cámara gameplay. No es
por sí solo una avería. En gameplay, `coherent=1` con `projPubs=1` también implica
que pasaron los filtros obligatorios de nodo raíz y pose coincidente.

### Evidencia registrada, 2026-09-08

La prueba automática WARP actualizada se ejecutó con `PASS`, exit code 0. Comprueba
recursos y matemáticas con cámaras deterministas; no ejecuta hooks en el juego.
Las compilaciones de producción y laboratorio también finalizaron correctamente.

Se repitió la integración de juego real **con el filtro de constructor raíz** en
el run `dlaa-f0d5823b`, usando DLAA 1024x1024 -> 1024x1024, ambos hosts NGX reales
y runtime OpenXR `bvr-xrsim`. El artefacto local del run está bajo:

```text
D:\BioShock2VR-DLSS-Lab\game-cc819bfd-7129-4494-97ff-2b9fd80f81a6\runs\dlaa-f0d5823b
```

Se auditaron `run.json`, `data/bioshockvr.log`, los dos logs bajo
`data/DLSS45Host/eye0` y `eye1`, los JSON `evidence/dlaa-world`,
`dlaa-world-motion`, `dlaa-pause`, `dlaa-resumed` y la captura estéreo de gameplay.
Las dimensiones 1032x1104 del JSON corresponden a la captura/composición del
simulador, no a una resolución NGX distinta de la declarada arriba.

| Comprobación | Resultado observado |
| --- | --- |
| WORLD estable a 00:32:20 | 5650 frames NGX, 2825 por ojo; `coherent=1 hist=1 reject=none` |
| Identidad y planos por ojo | `pubs=1 projPubs=1`, build/cámara iguales, `nearFar=10/65536`, época 8 |
| Hosts reales | NGX Init y feature DLAA model K correctos; evaluaciones crecientes en ambos, sin errores NGX en el tramo auditado |
| Captura inicial | FOCUSED, proyección con dos vistas; gate 3415/3415/3415, discarded/outOfOrder 0; IPD observado 0,063 m e imagen presente en ambos ojos |
| Giro simulado | Yaw/pitch 15/5 grados en `dlaa-world-motion`; poses de cámara cambiadas, coherencia e historial conservados |
| Pausa | `dlaa-pause`: una capa quad, sin capa de proyección temporal |
| Reanudación | `dlaa-resumed`: vuelven proyección de dos vistas y tres quads; época 8 -> 10, resets por ojo 1 -> 2 |
| Último diagnóstico 00:33:50 | 12714 frames procesados, 6357 por ojo, `coherent=1 hist=1`, `reject=none`, planos 10/65536 |

`tagMismatch` se mantuvo en 1 durante gameplay tras la carga, y en 2 tras
reanudar, sin incremento en los tramos estables observados; no fue cero durante
toda la sesión. Los fallbacks aumentaron durante menú/pausa y dejaron de aumentar
al recuperar gameplay. Son transiciones observadas, no una prueba de ausencia de
fallos en todas las transiciones posibles.

El cierre solicitado mediante WM_CLOSE a 00:33:53 registró después una excepción
`0xC0000005` en `Bioshock2HD.exe+0x4FF0FE`, clasificada por el manejador existente
como fallo conocido de teardown y terminada sin volcado. No se presenta este run
como «cierre sin errores», aunque no falló la evaluación NGX durante gameplay.
El antecedente exacto consta en el commit upstream `4071543` y en
[ENGINE_NOTES, sesión 38](bioshock2/ENGINE_NOTES.md#the-faulting-site-0x4ff0fe---the-engines-pending-display-apply-virtual),
incluido un bisect anterior con todos los hooks omitidos. El guard de
`src/core/util/crash.cpp` es genérico tras la señal de teardown y llama a
`TerminateProcess(..., 0)`: un exit code 0 tampoco demostraría ausencia de esa AV.
La observación temporal nueva no modifica ese guard ni escribe el objeto de la
instrucción afectada; el run sin volcado no aporta por sí solo una pila causal.

### Integración SR Quality, 2026-09-08

También se auditó el run `sr-7590efc3`, hermano del run DLAA en el mismo directorio
de laboratorio: `run.json`, log principal, ambos logs de host, JSON
`evidence/dlss-sr-world` y `dlss-sr-motion`, y captura estéreo con giro de cabeza.
Constan el filtro raíz WORLD instalado y el runtime `bvr-xrsim`.

| Comprobación | Resultado observado |
| --- | --- |
| Resolución y modo reales | Ambos hosts: 1024x1024 -> 1536x1536, SR Quality elegido por el óptimo NGX 1024x1024 (ratio 2/3 por eje) |
| Evaluación NGX | Features creadas y frames 1, 1800, 3600, 5400, 7200, 9000 evaluados en ambos logs; sin errores NGX en el tramo auditado |
| Último diagnóstico 00:36:19 | 18936 frames procesados, 9468 por ojo; `coherent=1 hist=1 reject=none` |
| Datos temporales | `pubs=1 projPubs=1`, build/cámara iguales, `nearFar=10/65536`, época 8, un reset inicial por ojo, cero gaps |
| Transiciones y continuidad | `tagMismatch=1` tras carga y estable; `mixed=0`; fallback 1911 sin incremento en el tramo de gameplay final |
| JSON WORLD / giro | FOCUSED y proyección de dos vistas; gates 4468/4468/4468 y 7141/7141/7141, discarded/outOfOrder 0; giro yaw/pitch 15/5 grados e imagen presente en ambos ojos |

La captura del simulador sigue siendo 1032x1104 por ojo; no se usa esa dimensión
de captura para afirmar qué resolución procesó NGX. La resolución SR se acredita
con las creaciones de feature de ambos hosts y los mensajes `eye0/eye1 ready`.

El cierre solicitado a 00:36:22 volvió a registrar una AV `0xC0000005`, esta vez en
`Bioshock2HD.exe+0xC312D2`. No es el mismo RVA que en DLAA. Las notas upstream ya
describen un antecedente en esa dirección como parte de las incidencias de
teardown; aun así, el clasificador de cierre es genérico y la ausencia de volcado
no permite probar la causa concreta de este run. Tampoco aquí se afirma cierre
sin errores ni se utiliza el exit code 0 forzado como prueba de ello.

### SR a resolución alta y control NORMAL, 2026-09-08

Se revisaron además dos runs del mismo laboratorio, sin modificar código entre
ellos: `sr-e171beb2` (SR alto) y `off-57e9251e` (NORMAL). La auditoría leyó sus
registros, JSON de evidencia y `close-result.json`, los logs de ambos hosts del
SR alto y las capturas estéreo de gameplay.

| Comprobación | SR alto `sr-e171beb2` | NORMAL `off-57e9251e` |
| --- | --- | --- |
| Modo acreditado | Ambos hosts NGX: 2048x2048 -> 3072x3072, Quality | `dlss.ini` y log: `mode=off`; sin arranque de hosts en log ni directorio DLSS45Host |
| Resultado temporal | A 00:40:06, 15364 procesados / 7682 por ojo; `coherent=1 hist=1 reject=none` | No se atribuyen frames a NGX: es el control VR sin DLSS/DLAA |
| Identidad/historial | `pubs=1 projPubs=1`, planos 10/65536, época 8, un reset inicial por ojo, gaps 0 | El observador raíz WORLD sigue instalado; NORMAL no equivale a vanilla ni a deshabilitar todos los hooks |
| Continuidad | `mixed=0`; `tagMismatch=1` y fallback 1141 estables al final | Estéreo y giro simulado presentes |
| Evidencia JSON | `dlss-high-world`, `dlss-high-motion`: FOCUSED, dos vistas; gates 6343 y 9120 iguales en waited/begun/ended | `normal-world`, `normal-motion`: FOCUSED, dos vistas; gates 5249 y 6369 iguales en waited/begun/ended |
| Cierre solicitado | WM_CLOSE, AV `0xC0000005` en `+0xC37362`, proceso terminado con exit code 0 | WM_CLOSE, AV `0xC0000005` en `+0xC312D2`, proceso terminado con exit code 0 |

Los cuatro JSON conservan `discarded=0`, `outOfOrder=0`, separación ocular 0,063 m
y captura 1032x1104 por ojo. Los JSON de movimiento muestran yaw/pitch 15/5 grados;
las imágenes verifican salida visible en ambos ojos, no una valoración de
ghosting. Los hosts del SR alto registran evaluaciones hasta al menos frame 7200
por ojo, sin errores NGX en el tramo auditado.

La resolución alta **no acredita 90 Hz sostenidos**: aunque el simulador estaba
configurado a 90, los últimos beats de ese run observados antes del cierre fueron
71-73 Draws/segundo y otras tantas segundas pasadas. Las temporizaciones internas
de NGX no equivalen a rendimiento total del juego ni a latencia del visor.

Ambos `close-result.json` señalan `teardownFaultInLog=true`; no se interpreta su
exit code 0 como cierre sin AV. El fallo también presente en NORMAL demuestra
que no requiere una evaluación NGX activa para manifestarse en estas pruebas;
no identifica por sí solo la causa ni exonera todo el código inyectado. El nuevo
RVA `0xC37362` del SR alto no se confunde con los otros dos sitios registrados.

Estos resultados acreditan los contratos técnicos observados de DLAA y las dos
configuraciones SR, no calidad final en visor ni vectores propios de objetos.
Más resoluciones, escenas y transiciones, comodidad y calidad visual en un visor
físico se validan por separado; no se infieren de las pruebas automáticas.

Para laboratorio, `BVR_BS2_TEST_ISOLATION` exige `BVR_LAB_GAME_INI` absoluto y un
`Shared.ini` hermano válido, sin buscar perfiles reales si faltan. El proxy de
laboratorio aísla además las rutas que consulta el motor; no forma parte del
payload de producción. La simulación no demuestra funcionamiento visual en un
visor físico, ausencia de ghosting ni comodidad en sesiones largas.
