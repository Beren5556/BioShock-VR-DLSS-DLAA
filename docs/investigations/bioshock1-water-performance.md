# BioShock 1: rendimiento de DLSS y DLAA cerca del agua

> Investigación aparcada por decisión de Carlos el 9 de septiembre de 2026.
> Se conservan las optimizaciones probadas. Las conclusiones finales están en
> [Rendimiento](../PERFORMANCE.md): reflejos, impacto alto; ondulaciones, moderado.
> Los siguientes pasos descritos en el historial no autorizan nuevas pruebas.

## Conclusión

Conclusión inicial, conservada como historial: la pérdida de rendimiento
descrita al mirar superficies con agua es compatible
con costes adicionales de la ruta DLSS/DLAA que NORMAL no ejecuta. La evidencia
actual identifica dos puntos relevantes: capturas repetidas de profundidad a
resolución completa y una espera síncrona del resultado del host por cada ojo.
Todavía no demuestra cuál domina en esa escena ni permite atribuir la caída
exclusivamente al shader del agua, a NVIDIA o a las copias.

El registro de BioShock 1 muestra un intervalo de DLAA a 3584 × 3584 con 52
segundos ojos construidos por segundo y otro posterior de NORMAL, con la misma
resolución, cerca de 90. Es una correlación importante, no una comparación
controlada: el registro no contiene marcas de agua visible, posición o dirección
de la cabeza. Por tanto, no equivale a afirmar una mejora medida de 52 a 90 FPS
en idéntica vista.

La candidata 0.2.6 conserva el procesamiento que ya funcionaba y añade las
mediciones que faltan. Las siguientes decisiones se podrán tomar con una prueba
breve en una vista reproducible. No se ha aplicado una supuesta optimización
del agua que cambie la profundidad capturada o permita enviar ojos incompletos.

## Alcance y evidencia

El análisis se limita a BioShock Remastered, primer juego, y a la base del mod
con DLSS 4.5 utilizada por las candidatas 0.2.5/0.2.6. No se extrapolan resultados
de otros juegos o desarrollos. La referencia funcional es NORMAL; DLAA ayuda a
comparar a igual resolución interna y de salida, y DLSS permite comprobar el
efecto de reducir la resolución interna.

El registro principal es `bioshockvr.log`, correspondiente a la sesión del
8 de septiembre de 2026, hasta las 21:16:35. Su copia analizada tiene 254.538
bytes y SHA-256
`A66638C7E425E23B6DCEB542058E1A85BF664B6F916CF109E4F870E23B999E78`.
Los datos derivados se conservan en `analysis.json` dentro de la evidencia local
de `artifacts/candidate-0.2.6/evidence/`; los registros originales no se incluyen
en la documentación pública.

El [analizador reproducible](../../scripts/Analyze-BioShock1-Performance.ps1)
separa intervalos de modo y geometría, excluye muestras de tasa cero y los tres
primeros segundos tras el cambio, y exige al menos tres muestras positivas para
publicar un intervalo. Esa exclusión no garantiza que haya finalizado la carga
inicial del nivel. Las tasas proceden del contador `2nd/s`, no de la suma de
presentaciones de ambos ojos ni de un medidor de fotogramas mostrados por el
visor.

Se registran 20 cambios aplicados, un cambio rechazado y ningún `stereo auto-off`.
El rechazo ocurrió al pedir DLSS con render de 1434 y salida de 3584; la ruta
anterior de 1792 a 3584 se recuperó. Este dato aconseja conservar el rechazo y
la reversión existentes, no alterar los tramos de calidad para ocultarlo.

## Comportamiento observado

La siguiente tabla contiene intervalos representativos. Cada dimensión es por
ojo y cuadrada. Las medias no son percentiles ni promedios ponderados de toda
la sesión; solo resumen las muestras aceptadas dentro de cada intervalo.

| Inicio | Modo | Render → salida | Muestras | `2nd/s`, mín.–máx. | Media |
|---|---|---|---:|---:|---:|
| 21:12:00.984 | DLAA | 3072 → 3072 | 36 | 71–72 | 71,94 |
| 21:12:39.561 | NORMAL | 3072 → 3072 | 11 | 71–72 | 71,91 |
| 21:13:46.383 | DLSS | 2048 → 3072 | 22 | 71–72 | 71,91 |
| 21:14:24.996 | DLSS | 2390 → 3584 | 40 | 58–90 | 76,72 |
| 21:15:19.128 | DLSS | 1792 → 3584 | 36 | 60–90 | 75,75 |
| 21:16:00.147 | DLAA | 3584 → 3584 | 3 | 52–52 | 52,00 |
| 21:16:05.978 | NORMAL | 3584 → 3584 | 23 | 89–90 | 89,96 |

Fuente: registro privado identificado arriba y `analysis.json`.

La primera parte de la sesión está próxima a un techo de 72, mientras que la
segunda alcanza 90. No se deben mezclar ambas como si el límite de presentación
fuera constante. Tampoco se debe concluir que pasar de 2390 a 1792 no mejora
DLSS: son intervalos diferentes, con posible cambio de escena o carga.

La observación más útil es que DLAA puede quedar claramente por debajo de
NORMAL aun con idéntica geometría declarada. Eso da prioridad al trabajo
adicional compartido por DLSS/DLAA. No descarta que el agua aumente también el
coste del render original, ni prueba que la evaluación de NVIDIA sea barata.
Los últimos archivos del host solo cubren un intervalo corto y su muestra
inicial de GPU no sirve como tiempo representativo de toda la sesión.

## Captura de profundidad

La función `finish_current_pass` selecciona candidatos de profundidad D24 a
resolución completa. Un cambio de destino o la finalización del pase puede
copiar la superficie completa cuando el candidato es el líder actual o supera
al anterior por número de draws. Volver al líder en subpases posteriores puede
producir nuevas copias del mismo tamaño; no hay una única copia garantizada
por ojo. Véase [temporal_guides.cpp](../../src/game/bioshock1r/temporal_guides.cpp),
funciones `finish_current_pass`, `on_setrt` y `generate_eye`.

En 32 ventanas válidas del registro, sin crecimiento de fallback ni cambios de
modo o geometría, el cociente entre copias y ojos procesados oscila entre
7,075 y 10,231; la media de los cocientes es 8,412. Se trata de diferencias de
contadores dentro de cada ventana, no de dividir dos acumulados de sesiones
distintas. Ese trabajo repetido está observado; su relación concreta con el
agua todavía no está etiquetada.

Una textura D24 de 3584 × 3584 ocupa lógicamente 49 MiB. Ocho a diez copias
equivalen a 392–490 MiB copiados por ojo. No es una medida del tráfico real de
memoria: cachés, compresión, lectura/escritura y otras operaciones cambian esa
relación. Además, `CopySubresourceRegion` encola trabajo asíncrono; el tiempo
de la llamada en CPU no mide cuánto tarda la GPU en ejecutarlo.
[Microsoft, CopySubresourceRegion](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-copysubresourceregion).

Reducir el número a una copia por ojo sin reconstruir el orden de los pases
sería prematuro. El motor puede limpiar o reutilizar la superficie antes del
final del ojo, y una copia temprana puede quedar incompleta. El número de draws
tampoco prueba que todos escriban profundidad. La captación correcta de la
escena y la separación de ambos ojos deben verificarse antes de sustituir esta
heurística.

## Medición aislada de las copias

Se ha ejecutado un microbenchmark D3D11 independiente en una RTX 4090, con
versión de controlador de Windows 32.0.16.1656. No ejecuta BioShock, NVIDIA NGX
ni OpenXR. Cada caso usa 24 iteraciones y cronometra en GPU un lote equivalente
a un ojo, con distintos números de copias D24 de tamaño completo.

| Tamaño | Copias por ojo | GPU ms/ojo, fuente sin modificar | GPU ms/ojo, clear antes de cada copia |
|---|---:|---:|---:|
| 2048² | 1 | 0,0194 | 0,0228 |
| 2048² | 8 | 0,1530 | 0,2423 |
| 3072² | 1 | 0,0445 | 0,0505 |
| 3072² | 8 | 0,3538 | 0,5300 |
| 3584² | 1 | 0,0640 | 0,0718 |
| 3584² | 4 | 0,2555 | 0,3703 |
| 3584² | 8 | 0,5105 | 0,7215 |
| 3584² | 12 | 0,7653 | 1,0045 |

Fuente: `hardware-depth-benchmark.txt` en la evidencia privada; código
reproducible en [dlss_sharpen_test/main.cpp](../../src/tools/dlss_sharpen_test/main.cpp),
opción `--hardware-benchmark`.

La columna con clear incluye el coste de esa limpieza. No reproduce draws del
agua, ni la contención de una partida VR. Tampoco representa un límite superior
o inferior fiable del coste dentro del juego. Sí confirma que repetir copias
completas tiene un coste creciente y que el tamaño importa.

En estas condiciones aisladas, ocho copias de 3584 consumen aproximadamente
0,51–0,72 ms por ojo. El resultado no basta para explicar por sí solo toda la
diferencia observada entre los intervalos de DLAA y NORMAL. No se debe anunciar
una ganancia de FPS a partir de este microbenchmark ni restar directamente sus
tiempos a los del juego.

## Sincronización del procesamiento DLSS/DLAA

En `process_eye`, el cliente copia color, profundidad y movimiento a los
recursos compartidos, señala una fence, envía trabajo al host y espera en CPU
el resultado de ese ojo. Después establece la dependencia GPU de salida y
copia el resultado al destino de OpenXR. NORMAL no realiza este recorrido.
Véase [dlss45_client.cpp](../../src/core/gfx/dlss45_client.cpp), función
`process_eye`.

La espera tiene límite y detecta la terminación del proceso auxiliar. Esta
protección evita dejar en la cola GPU una espera que nunca se pueda satisfacer
si el host falla. Eliminarla sin otra estrategia de recuperación puede convertir
una caída recuperable del host en un bloqueo del juego. La separación de los
resultados de ambos ojos también impide reutilizar una imagen antigua como
si fuera el resultado de la pareja actual.

Como hipótesis, la espera por ojo puede reducir el solapamiento entre el render
del juego y el trabajo de reconstrucción. Una escena más costosa podría
amplificar el hueco de ejecución. Sin una medida del tiempo de espera y de las
copias no se puede decidir si domina esa serialización, el render original,
NGX o una combinación.

`Flush` tampoco significa que la GPU haya terminado: remite comandos y es
asíncrono, con sobrecoste propio. Añadir más llamadas o interpretar su duración
como tiempo de reconstrucción no resolvería esa incertidumbre.
[Microsoft, ID3D11DeviceContext::Flush](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-flush).

## Agua, movimiento y calidad temporal

La ruta de guías usa movimiento derivado de cámara. No equivale a disponer de
vectores completos de cada elemento animado, transparencia o reflejo. Por eso
pueden existir problemas visuales temporales específicos de esas superficies,
independientes del cuello de botella de tiempo.

Es plausible que pases adicionales alrededor del agua hagan más visible la
heurística de captura o la espera, pero el registro actual no identifica un
shader concreto como agua. Tampoco basta encontrar un destino de menor tamaño
o una profundidad con otro formato: el capturador descarta candidatos que no
cumplen su contrato. No hay evidencia suficiente para señalar una textura de
agua erróneamente elegida como causa confirmada.

Estas limitaciones aconsejan observar por separado rendimiento y artefactos.
Una mejora de estabilidad temporal no implica automáticamente una mejora de
FPS; una reducción de copias tampoco garantiza profundidad correcta.

## Mediciones añadidas en la 0.2.6

La candidata registra ventanas de tiempos junto al diagnóstico habitual, sin
crear una espera adicional por cada medida GPU. Utiliza cuatro posiciones de
consulta y muestrea una de cada 64 operaciones. La lectura usa `DONOTFLUSH`;
si no hay una consulta libre, omite la muestra, no el fotograma. El mecanismo
está en [sampled_gpu_timer.h](../../src/core/gfx/sampled_gpu_timer.h).

| Registro o campo | Qué mide | Qué no debe inferirse |
|---|---|---|
| `[depth-perf] copyCalls` | Copias de captura en la ventana | Número de pases de agua |
| `copiedMiB` | Volumen lógico de las copias | Tráfico DRAM efectivo |
| `gpuCopyAvgMs/MaxMs` | Duración GPU de las copias muestreadas | Coste de todos los shaders del nivel |
| `[dlss45-perf] cpuSubmitAvgMs` | Trabajo CPU para preparar y enviar cada ojo | Duración GPU de NGX |
| `cpuWaitAvgMs` | Espera CPU hasta el resultado del host | Tiempo exclusivo de la DLL NVIDIA |
| `cpuTotalAvgMs/MaxMs` | Tiempo CPU dentro de `process_eye` | Tiempo total de una pareja VR |
| `gpuInputAvgMs/MaxMs` | Lote muestreado de copias de entrada | Render previo, guías completas o NGX |
| `gpuSamples` / `gpuInputSamples` | Número de consultas recuperadas | Número de ojos procesados |

Los promedios CPU usan sus contadores de operaciones; las medidas GPU usan
solo las consultas completadas. Una muestra puede recuperarse en una ventana
posterior a su emisión, por lo que no se deben interpretar los bordes de cada
ventana como una partición exacta de cada fotograma. Tampoco se deben sumar
directamente campos CPU y GPU: pueden representar trabajo solapado.

La instrumentación tiene coste, aunque no fuerce la terminación del trabajo
para medir. Su impacto real debe contrastarse en el juego si aparecen nuevas
diferencias. La 0.2.6 no reduce el número de capturas, no cambia el preset NGX
y no modifica el protocolo del host.

## Nitidez y separación del diagnóstico

La nueva nitidez es un [filtro posterior al resultado DLSS](../../src/core/gfx/dlss_sharpen.cpp). El encabezado
oficial de NVIDIA marca su antiguo parámetro de sharpness como no soportado;
por eso no se presenta este filtro como nitidez interna activada en NGX.
[NVIDIA, Streamline sl_dlss.h](https://raw.githubusercontent.com/NVIDIA-RTX/Streamline/main/include/sl_dlss.h).

El filtro permanece a 0 % por defecto, donde no se ejecuta. La prueba de
rendimiento del agua debe mantenerlo así para no mezclar su coste con el
problema original. La evaluación visual de nitidez se puede hacer después,
sin reconstruir resolución ni historial. Sus pruebas CPU/WARP comprueban los
21 valores en ambos ojos, alfa y conservación exacta de la imagen al 0 %; no
demuestran por sí solas su calidad percibida en el visor.

## Prueba breve y decisiones siguientes

La validación mínima consiste en una misma posición y mirada cerca del agua,
con resolución de salida constante, mismo límite de refresco y nitidez al 0 %.
Interesan intervalos estables de unos 15–20 segundos después de cada cambio:
NORMAL, DLAA y DLSS con la proporción ya usada. Debe repetirse el contraste
mirando una zona sin agua desde esa posición, sin añadir una batería larga de
perfiles o controladores.

Durante la prueba se comprobará también que sigue habiendo profundidad y que
la rueda de teclas funciona. La recogida se puede realizar en una única sesión
guiada; no requiere cambiar drivers, tocar otros proyectos o volver a instalar
el mod original. Un breve registro de las horas de cada vista permitirá asociar
las nuevas medidas con agua visible o no visible.

Si suben sobre todo el número o tiempo GPU de capturas, la siguiente modificación
debe centrarse en seleccionar el último depth válido con menos copias y validar
ambos ojos. Si las copias siguen siendo pequeñas pero domina la espera, habrá
que separar render previo, evaluación del host y huecos de sincronización antes
de cambiar el orden de envío. Enviar ambos ojos antes de una espera bilateral
es una posibilidad de diseño, no una optimización ya implementada o validada.

Si ambos costes permanecen parecidos entre las vistas, hará falta una captura
GPU de los pases del juego y del host, acotada a esa escena. Eso permitiría
investigar shaders o recursos concretos sin culpar por descarte a NVIDIA.
En todos los casos se mantienen los tramos de resolución y calidad existentes;
no se modifica su significado como parte de este diagnóstico.

## Fuentes y trazabilidad

- Registro privado `bioshockvr.log`, sesión de BioShock 1 del 8 de septiembre
  de 2026; tamaño, hash y condiciones de análisis indicados en este informe.
  Acceso local en la evidencia de la candidata, sin URL pública.
- Resultados privados `analysis.json`, `hardware-depth-benchmark.txt` y
  `tests-cpu-warp.txt`, conservados con la candidata 0.2.6. No son capturas de
  una sesión de juego con la 0.2.6.
- Código de BioShock VR de esta candidata: `temporal_guides.cpp`,
  `dlss45_client.cpp`, `sampled_gpu_timer.h`, `dlss_sharpen.cpp` y el analizador
  enlazados en las secciones correspondientes. La semántica de los contadores
  se deriva de esas implementaciones.
- Microsoft Learn, [ID3D11DeviceContext::CopySubresourceRegion](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-copysubresourceregion).
  Semántica asíncrona y restricciones de las copias D3D11; consultado durante
  la preparación de la candidata.
- Microsoft Learn, [ID3D11DeviceContext::Flush](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-flush).
  Envío asíncrono de comandos y sobrecoste; consultado durante la preparación
  de la candidata.
- NVIDIA, [Streamline, include/sl_dlss.h](https://raw.githubusercontent.com/NVIDIA-RTX/Streamline/main/include/sl_dlss.h),
  rama main consultada durante la preparación de la candidata. Deprecación
  del campo de sharpness; el enlace es una rama móvil, no una versión fijada.

## Seguimiento: comparación A/B/C y efecto parecido a una nube

La prueba privada DLL-only `0.2.6-diag1` se completó en dos ciclos el 8 de
septiembre de 2026. En todos ellos render y salida eran 2560 × 2560, sin
nitidez añadida y sin escribir ajustes temporales al INI. A era NORMAL;
B generaba las mismas guías que DLAA, pero mostraba el color original sin
host/NGX; C usaba el DLAA existente. Carlos confirmó A y B fluidos, C con la
caída, y precisó que en la zona problemática veía algo parecido a una nube.
No se ha identificado todavía si es niebla, humo, vapor u otro efecto.

Descartando los primeros tres segundos de cada fase y los contadores nulos,
A promedió 72 construcciones de pareja/s en ambos ciclos; B también 72
(rango 71–73). C promedió aproximadamente 64,56 y 65,32, con rango 59–72.
Son tasas de construcción, **no FPS medidos del compositor**. B produjo guías
válidas para ambos ojos; no fue una falsa comparación con las capturas apagadas.
La espera CPU de C rondó 2,52–2,54 ms por ojo, pero incluye trabajo anterior
pendiente, transporte y planificación, no únicamente evaluación NVIDIA.

Este contraste hace menos probable que las capturas aisladas expliquen toda
la caída en estas condiciones. No demuestra que sean gratuitas ni que exista
un bug de NVIDIA con niebla: A/B, limitados a 72, pueden ocultar el aumento
del coste de la escena hasta que se suma el coste de DLAA. Tampoco hay marcas
que permitan asignar segundos concretos del registro anterior a la nube.
Se ha conservado ese registro en la evidencia privada de `0.2.6-diag2`.

### Qué añade diag2, sin cambiar el renderizado

La segunda DLL conserva exactamente el F4 A → B → C → salir y los valores
existentes. Añade una muestra cada 16 construcciones por ojo, únicamente en
el camino de renderizado inline y con identificador de ojo válido. No cambia
shaders, agua/niebla, cámaras, resolución, IPC, hosts ni esperas de protección.

Los registros `[scene-probe]` separan cuatro intervalos, CPU y GPU en ms:

1. `Scene`: entrada al build original → entrada al Present, antes de OpenXR.
   Incluye el render del juego y las capturas de profundidad intercaladas en B/C;
   CPU mide la llamada del motor, no solo la duración de sus draw calls.
2. `PreCapture`: entrada al Present → captura del ojo. Contiene espera del visor,
   overlay y adquisición de imagen XR; se mantiene aparte del render de escena.
3. `Guides`: preparación/conversión de guías en el punto final de captura.
   Las copias de profundidad hechas durante la escena siguen contabilizadas en
   `Scene`, no en este intervalo. En A no hay conversión de guías.
4. `Output`: transporte, evaluación y copia del resultado en C; copia directa
   en A/B. No es un cronómetro exclusivo del algoritmo NGX.

GPU mide tiempo transcurrido entre comandos en la cola, que puede incluir
huecos de planificación/CPU y esperas entre procesos. No equivale a tiempo
GPU ocupado en exclusiva. No se deben sumar CPU y GPU entre sí; ni sumar
directamente medidas de ambos ojos con ventanas distintas como si fueran una
pareja exacta. La comparación útil es entre vistas estables de la misma fase
y entre las mismas vistas de A/B/C, excluyendo reconstrucción y fallback.

Las muestras conservan ojo, build, fase, resultado de captura y cámara exacta.
Se resumen aproximadamente cada dos segundos por ojo, con tiempos de origen
y rotación inicial/final. Esas rotaciones ayudan a separar vistas; no reconocen
la nube ni demuestran que toda una ventana estuviera inmóvil. Fallbacks y
muestras incompletas se excluyen de los promedios. Las fases no comparten datos.

Solo hay una consulta disjoint envolvente por ojo muestreado; los muestreadores
GPU anteriores se suspenden durante A/B/C para evitar anidarlos. Se consultan
resultados sin `Flush`, sin bucles de espera y sin bloqueos de GPU. Si la cola
de consultas está llena se pierde una muestra, no se espera por el fotograma.
Esto sigue la frecuencia recomendada para consultas disjoint por
[Microsoft, D3D11_QUERY](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_query).
La instrumentación puede tener sobrecoste y aún requiere contraste en el juego.

Pruebas locales: nuevo medidor y su integración con fases en WARP/debug,
37/37; política 94/94; controlador 54/54; nitidez WARP 48/48; guías WARP
correctas. Sin diagnóstico, política 85/85 y controlador 32/32, incluido F4
inerte. No hay nuevo instalador ni publicación. Pendiente sesión real diag2;
**no se presenta esta DLL como una corrección del rendimiento**.

### Resultado de diag2 y candidata perf1 (9 de septiembre de 2026)

La sesión real de diag2 terminó correctamente a las 00:08:48. El registro
privado de 195.688 bytes se conserva en `artifacts/probe-0.2.6-diag2`, con SHA-256
`FEE88F78298EB7B2642F01407F184C28C39A87FB0C7F860575CBA6B555FD7297`.
Carlos confirmó que la vista con caída corresponde a la nube. No se ha
identificado todavía la clase de efecto ni un shader concreto responsable.

En las ventanas de cámara estable del primer ciclo, DLAA pasó de aproximadamente
1,9/1,7 ms de Scene CPU por ojo en la vista fluida a 4,9/4,6 ms en la vista lenta.
Output CPU, en cambio, se mantuvo aproximadamente entre 2,6 y 2,9 ms por ojo;
Output GPU transcurrido, entre 1,6 y 1,7 ms. La tasa de construcción de parejas
pasó de 72 a 59–61/s. A/B también alcanzaban unos 4,7–4,8 ms de Scene CPU en
la zona pesada, pero conservaban unas 72 parejas/s. Las ventanas se agruparon
por orientación inicial/final y descartando transiciones; no son un benchmark
de poses idénticas. Hubo 96 ventanas válidas, sin muestras fallback o dropped.
El arranque anterior a las fases sí registró dos discrepancias de etiquetas;
no se presenta toda la sesión como libre de incidencias.

La interpretación de trabajo es una escena cara que agota el margen al sumarle
el puente DLAA síncrono. No se ha demostrado una evaluación NGX especialmente
lenta por niebla ni que las capturas sean gratuitas. La candidata privada
`0.2.6-perf1` intenta solapar el trabajo del host izquierdo con la construcción
de la escena derecha, conservando los contenidos y tiempos de predicción de
la pareja. Orden: enviar L → construir R → enviar/resolver R → resolver L →
entregar la pareja. No añade un fotograma de salida antiguo.

La imagen XR izquierda permanece adquirida hasta resolverla o restaurar su
propia copia de color. Se conservan comprobación CPU acotada, fences GPU,
guías e historial independientes. Fallos/cancelaciones restauran el izquierdo
antes de liberarlo y fuerzan respaldo coherente; la asignación de la copia
puede desactivar la optimización y conservar el modo síncrono. En el camino
normal no se aplica una pasada extra de reconstrucción espacial: solo se
guarda el color de respaldo. La optimización está limitada a BioShock 1 con
DLSS/DLAA, pareja secuencial válida y opción de compilación `BVR_DLSS_OVERLAP`;
esta opción permanece desactivada por defecto.

Pruebas perf1: transporte real con pipes locales/fences WARP y política de
adquisición/liberación, 38/38; medidor 38/38; política 94/94; controlador 54/54;
nitidez 48/48; guías y respaldo espacial WARP correctos. El test de transporte
no arranca hosts ni evalúa NGX y el de adquisición usa callbacks simulados,
no un runtime OpenXR. Falta validar rendimiento y estéreo en el visor.

Se aplicó solo la DLL, SHA-256
`FE9C2DF6CA8BC51F1A0845CEF445330E2550AE6F700A7757CEA48A9A9E4F31B5`,
con respaldo diag2 y copia en la carpeta habitual del escritorio. Lanzador,
host, NVIDIA y ambos INI verificados sin cambios. No hay nuevo instalador ni
publicación. Evidencia y restauración: `artifacts/probe-0.2.6-perf1/`.

Atención al comparar registros: con `deferred>0`, Output izquierdo mide solo
el envío; su resolución se contabiliza en Output derecho. Los contadores
`[dlss45-overlap] completed/aborted` deben confirmar que las parejas diferidas
terminaron correctamente. No inferir éxito DLAA del contador de envíos.

### Perf1 aceptada por el usuario; prueba perf2 de copias invariables

Carlos comunicó una mejora grande con perf1: DLSS permite jugar a una resolución
considerablemente más alta y DLAA también mejora. El último registro de esa
sesión, de 00:40:57 a 00:47:20, informa 22.088 parejas de solapamiento completadas
y cero canceladas; el arranque/transiciones sí tienen fallbacks. No se confunden
esos contadores con una garantía de todos los fotogramas ni con FPS del visor.
En ventanas de al menos 100 muestras, la espera CPU del ojo izquierdo redondea
a 0,000 ms. La derecha en DLAA 2560² promedia aproximadamente 3,055 ms; en DLAA
3328², 6,230 ms. Son configuraciones y vistas distintas, no un benchmark
controlado ni tiempo exclusivo de NGX. Los registros se conservan en la
evidencia privada de perf2 antes del siguiente arranque.

La candidata `0.2.6-perf2` conserva perf1 y prueba un único cambio: saltar copias
de profundidad idénticas dentro del mismo intervalo. No pospone el instante de
selección ni modifica la votación del DSV. Compara la identidad del ganador
y un contador de posibles escrituras con los de la última copia. Los dibujos
con escritura de profundidad, borrados, transferencias al recurso y ejecución
de listas invalidan el contador. Se sigue el estado de profundidad mediante
hooks, sin GetState por dibujo; un conjunto de hooks incompleto desactiva la
reutilización. Se ignoran contextos ajenos/diferidos. Cada intervalo y cada ojo
empiezan sin captura reutilizable; los cambios de ganador siempre se copian.

Los nuevos slots de contexto se verificaron contra el SDK local: estado de
profundidad 36, DrawIndexedInstancedIndirect 39, DrawInstancedIndirect 40 y
ClearState 110. El significado de estado y borrados se contrasta con
[D3D11_DEPTH_STENCIL_DESC](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ns-d3d11-d3d11_depth_stencil_desc)
y [ClearDepthStencilView](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-cleardepthstencilview).
El shader sigue leyendo exclusivamente R24, no el canal stencil.

Trece pruebas adicionales WARP validan ahorro de copias, píxeles de profundidad,
identidad/votos, invalidaciones y vuelta a la política anterior. En el caso
sintético, diez subpasadas de solo lectura necesitan una copia: no se afirma
esa reducción en el juego. Pasan además guías con la opción desactivada,
solapamiento 38/38, medidor 38/38, política 94/94, controlador 54/54, nitidez
48/48 y respaldo espacial. La instalación real de hooks, estéreo y rendimiento
de perf2 quedan pendientes de prueba con el usuario.

DLL aplicada/copiada al escritorio con SHA-256
`0A1B76490E99A7A7C9A7AF980B1D40FE98921F4BB688A93982D18E5787AAB3BC`.
Respaldo `bioshockvr.perf1.dll`; lanzador, host, NVIDIA y tres INI intactos.
No se genera instalador ni publicación. Para reproducir: opciones CMake
`BVR_PERFORMANCE_PROBE=ON`, `BVR_DLSS_OVERLAP=ON`, `BVR_DEPTH_COPY_REUSE=ON`.
No se implementan aún las propuestas de trabajo final derecho ni ahorro de la
copia de color. El clic del stick derecho que agacha queda expresamente
pendiente; los controles no se tocan.

## Perf2 probada y diagnóstico latency1 (2026-09-09, mañana)

Carlos no aprecia mejora sobre perf1. En DLAA, 3072 × 3072 es el umbral de
microcaídas y resoluciones superiores empeoran mucho. Informa latencia total
45 → 58 ms al activar DLAA/DLSS y un retorno breve a 45 tras cambiar resolución.
La procedencia exacta del indicador y los 72 Hz aún deben confirmarse con él.

La sesión 08:56:29–09:00:10 confirma perf2, 19/19 hooks y reutilización activa.
A 3072 se evitaron 20062 de 50144 solicitudes de copia (40 %). Espera CPU
derecha media ponderada, ventanas de al menos 100 frames: DLAA 2560 ~2,87 ms,
2816 ~3,15 ms, 3072 ~4,22 ms; DLSS 1792→2560 ~1,95 ms y 2150→3072 ~2,64 ms.
No son poses idénticas ni costes exclusivos de NGX. El registro no contiene
los 45/58 ms del visor. Un refresco a 72 Hz son 13,889 ms: la diferencia
observada es compatible con temporización/cola, pero no demuestra esa causa.

La candidata privada 0.2.6-latency1 conserva las dos optimizaciones y añade
F4 A NORMAL → B PUENTE SIN DLAA → C DLAA → restaurar. Misma geometría interna
en las tres fases, sin SETRES ni escritura de INI durante el ciclo. B conserva
capturas, envíos, sincronización e independencia de ojos; el helper mantiene
una feature DLAA creada, pero copia el color completo en vez de evaluarla.
El uso de transport=2 requiere un acknowledgement explícito: el antiguo test
de transporte de media imagen no puede pasar como esta prueba estéreo.

Se instala un helper con nombre Latency1 propio y directorios LocalAppData
independientes. No se sobrescriben host original, NVIDIA, lanzador ni INI.
No se cambia la política de esperas GPU, predicción o poses. Se añaden medidas
CPU de entrega, periodo OpenXR y margen respecto a predictedDisplayTime,
convertido correctamente a QPC mediante la extensión opcional del runtime.
Ese tiempo predicho NO es el deadline del compositor; cpuPastPrediction NO
es una medida de frames perdidos, y GPU queue elapsed NO es coste exclusivo.
Referencias: https://registry.khronos.org/OpenXR/specs/1.1/man/html/xrConvertTimeToWin32PerformanceCounterKHR.html
y https://learn.microsoft.com/en-us/windows/win32/direct3d12/timing .

A petición adicional de Carlos, el cuadro de configuración/prueba del visor
pasa arriba a la izquierda (x=-0,65; y=+0,50; z=-1,2 m), manteniendo letra,
tamaño y representación binocular. F1/F2/F3 y los valores no se alteran.

Pruebas: integración real con dos hosts y NVIDIA 310.7.0.0, 204/204; política
97/97; controlador 54/54; solapamiento 38/38; medidor 43/43; profundidad 13/13;
guías, nitidez 48/48 y respaldo espacial correctos. Sin juego ni sesión OpenXR.
Falta la comparación en el visor. Artefactos y restauración en
artifacts/probe-0.2.6-latency1/; copia de escritorio en la carpeta habitual.
No se genera instalador, publicación ni apagado.

## Pendiente de interfaz tras la prueba latency1 (2026-09-09)

Carlos completó la nueva prueba con la revisión HUD 2. Solicita dejar anotado
bajar un poco más el cuadro de opciones y desplazarlo un poco más a la derecha.
La posición instalada sigue siendo x=-0.20, y=+0.50, z=-1.20. Este ajuste queda
pendiente: no se aplica durante el análisis de los registros ni se cambia
tamaño, letra, controles o configuración del juego.

## Resultado latency1 A/B/C (2026-09-09, 11:45–11:47)

Prueba completada con HUD 2, render/salida 3072 × 3072 y nitidez cero. Cuatro
cambios aplicados (A/B/C/restaurar), ninguno rechazado. Tras excluir los primeros
tres segundos completos y las transiciones, el ritmo de construcción de parejas
L/R es A NORMAL 72,00/s (72–72), B PUENTE SIN DLAA 71,93/s (70–73), C DLAA
67,05/s (63–72). No son FPS del compositor. Hubo giros de cabeza, por lo que
la comparación no es un benchmark de poses idénticas.

La espera CPU derecha del cliente, ponderada por frames en ventanas estables,
pasa de 2,137 ms en B a 4,533 ms en C. La copia GPU del helper B solo registra
~0,050 ms de cola por ojo: esa espera no equivale al coste exclusivo de NVIDIA
o de la copia. La escena CPU por ojo se mantiene aproximadamente entre 4,0 y
4,4 ms en las medias globales. El puente tiene coste pero mantiene ~72; al
añadir DLAA se pierde ese ritmo. La hipótesis de trabajo sigue siendo margen
agotado por escena + procesamiento temporal/entrega, no un shader de nube
identificado como defectuoso.

OpenXR confirma periodo de 13,889 ms (72 Hz). El tramo CPU entre el retorno de
xrWaitFrame y el inicio de xrEndFrame es A 4,188, B 6,732 y C 8,899 ms; xrEndFrame
ocupa ~0,08–0,11 ms. No representan GPU terminada ni latencia total de VD. Las
predicciones tampoco demuestran una cola extra fija de 13 ms. Se han solicitado
a Carlos sus lecturas visuales de VD para A/B/C; esos valores no están en el log.

Último contador: 4642 parejas solapadas completadas, cero abortadas y ningún
stereo auto-off. Las 518 muestras de escena no contienen fallback; los contadores
globales sí incluyen arranque y tres incrementos asociados a reconstrucciones.
La discrepancia de etiqueta del arranque no vuelve a aumentar durante A/B/C.
No confundir consultas de diagnóstico descartadas con frames perdidos del visor.

Evidencia, reglas de cálculo y resultado completo:
artifacts/probe-0.2.6-latency1-hud2/test-2026-09-09-114526/ANALISIS.md y analysis.json.
El registro principal tiene SHA-256
133E017A2CBDF4447A93B23993A659AAF2DFF4610BAF2B07762E09D4EAB6812C.
Los helpers C se analizan desde .prev.log; los .log finales pertenecen a la
restauración posterior. Este turno no cambia código, binarios, INI o instalador.

## Candidata perf3 aplicada (2026-09-09)

Carlos autoriza el solapamiento final y pide buscar más soluciones. Se aplica
0.2.6-perf3 conservando perf1/perf2/latency1. Tras enviar el ojo derecho, prepara
las capas independientes de HUD/apuntado antes de esperar DLAA. El espejo y
composite de ventana permanecen después de la captura para no destruir el
backbuffer necesario si falla el resultado. No cambia poses, historial ni
fotograma. La llamada auxiliar se realiza una sola vez por Present.

Además, el respaldo izquierdo retiene el color compartido ya enviado al host,
evitando la segunda copia y la textura adicional (36 MiB a 3072² RGBA8). Su
referencia se libera antes de reutilizar la entrada del ojo. El mensaje de cada
ojo usa una escritura al pipe, conservando exactamente el protocolo v8.

Se cumple el ajuste de interfaz pendiente: X=-0.10, Y=+0.38, Z=-1.20, misma letra
y tamaño. Ningún cambio de resolución, porcentajes, sharpness, F1/F2/F3,
presets o NVIDIA. F4 mantiene A NORMAL / B PUENTE SIN DLAA / C DLAA / restaurar.

Verificado: NVIDIA real con dos hosts, entrada 3072² y salida DLSS 4608²,
279/279; copia izquierda intacta tras evaluar NVIDIA y enviar el derecho.
WARP/transporte/fallos 47/47, controles 97/97 y 54/54, medidor 43/43, nitidez
48/48 y respaldo espacial correcto. Sin advertencias D3D11 debug. Pruebas
funcionales, no confirmación de mejora de rendimiento real en el visor.

Instalada/copias verificadas, SHA-256
53A6F78940D77643727032A39A3FD2775415165FE794F48FAA394697B0FCD3A9.
Respaldo bioshockvr.latency1-hud2.dll. Lanzador, helper original, helper Latency1,
NVIDIA y los tres INI personales sin cambios; no hay instalador o publicación.
Evidencia, build, pruebas, restauración y soluciones adicionales priorizadas en
artifacts/probe-0.2.6-perf3/NOTAS.md. Queda pendiente la prueba de Carlos.

Se revisaron también generación directa de profundidad/MV en recursos
compartidos, coordinación de los dos hosts/dispositivos y caché de consultas
de FOV por dibujo. Son alternativas todavía sin implementar o medir; no se
confunden con cambios incluidos en perf3 ni con ganancias garantizadas.

## Resultado perf3 A/B/C a 2150² (2026-09-09, 13:06–13:08)

Carlos informa 72 FPS en las tres fases y latencia total VD 44–45 ms en A/B,
con aproximadamente 14 ms más en C DLAA. El log confirma 72 parejas L/R por
segundo en todas las ventanas estables; no es una lectura del compositor.
F4 se activó desde DLSS 70 % 2150→3072 y conservó los 2150 internos en A/B/C.
El despliegue no había cambiado los INI ni impuesto esa resolución. La prueba
anterior era a 3072²: hoy se procesa el 48,98 % de esos píxeles, así que no se
puede atribuir el mayor ritmo a perf3. Se debe explicar mejor esta regla de F4.

El dato nuevo es el horizonte de predicción de VirtualDesktopXR: asentado
(ageMs>=10000), A 46,136 ms, B 46,178 ms y C 59,916 ms respecto al retorno de
xrWaitFrame. El salto B→C de 13,739 ms es cercano a un refresco de 13,889 ms.
La espera CPU derecha solo pasa de 1,312 a 1,987 ms. Es compatible con una
adaptación de temporización/entrega al activar DLAA, no prueba una cola concreta
ni que NGX calcule durante los 14 ms adicionales. Predicción != latencia total.

El trabajo de capas intercalado por perf3 ocupa ~0,005 ms CPU: el margen de
solapamiento en esta escena es pequeño. El ahorro de color sí está activo:
5291 copias evitadas/parejas completadas, cero abortadas. Las 498 muestras de
escena A/B/C no tienen fallback ni consultas descartadas; los totales globales
incluyen arranque y tres incrementos por reconstrucción. Cámara no idéntica.

Siguiente enfoque propuesto: correlacionar finalización GPU y entrega estéreo
con la adaptación de VDXR, separando FPS, latencia y sus componentes. No hay
nuevo cambio aplicado en este turno. La comparación aísla DLAA, no DLSS.
Evidencia y análisis: artifacts/probe-0.2.6-perf3/test-2026-09-09-130614/ANALISIS.md
y analysis.json. Log principal SHA-256
2BC97AA384454EDB87E7B99ABC0F84C8340E42EA8A7EE6E802B55EDAEE419EDE.

## Candidata perf4 aplicada (2026-09-09, 13:38)

Carlos autoriza continuar con el diagnóstico de entrega/temporización VDXR.
El espejo y la composición HUD de la ventana del PC estaban antes de xrEndFrame;
se trasladan después en parejas temporales completas y enfocadas de BioShock 1,
siempre antes del Present de escritorio. Las capas del visor, sus dos ojos,
poses, historial, valores y esperas GPU no cambian. NORMAL/menús/fallback/otros
juegos mantienen el orden anterior. No se añade un Flush ni un retardo.

Se observa AppGpuTime que VDXR intenta enviar al export ovr_SetFloat del módulo
VD ya cargado en el propio proceso, preservando exactamente argumentos y
retorno. El detour no realiza I/O, consultas GPU ni esperas. Si no puede
instalarse, faltan muestras pero el render sigue. No se altera el tiempo
comunicado a VD ni predictedDisplayTime. Se añaden costes del escritorio,
Present y tramo entre retorno de entrega y siguiente espera XR.

La fuente pública de VDXR mide GPU entre xrBeginFrame/xrEndFrame y serializa
la entrega desde el dispositivo de aplicación. Eso fundamenta retirar el
trabajo exclusivo del escritorio de ese tramo, pero no demuestra aún que
el cambio elimine los ~14 ms. AppGpuTime es retrasado por el runtime y no es
latencia total VD ni tiempo NGX exclusivo. La versión instalada es 1.0.10;
las fuentes consultadas 1.0.9/1.1.0 no se presentan como idénticas al binario.

Pruebas: observador x86/orden/píxeles WARP 36/36; controles 97/97 y 54/54;
medidor 43/43; transporte/fallos 47/47; nitidez 48/48. Sin advertencias D3D11
en las comprobaciones. No se ha iniciado juego/OpenXR ni se da por medida una
mejora real. Cliente, hosts y NVIDIA no cambian; no se repite su prueba como
si fuera una nueva medición de rendimiento.

DLL aplicada y copiada al escritorio, SHA-256
3F4D5E120151E038DAED5A307C7AB10CDD62311DA1443EDFAE0D2ECB49B643A2.
Respaldo bioshockvr.perf3.dll. Lanzador, helpers, NVIDIA y tres INI intactos.
F4 A/B/C/restaurar conserva resolución interna (2150² si se entra desde DLSS
70 % con salida 3072). Primera comparación propuesta a esa misma resolución;
no confundir con una futura prueba del umbral a 3072. Sin instalador, publicación
o cambios en otros proyectos. Detalles, referencias, tests y restauración:
artifacts/probe-0.2.6-perf4/NOTAS.md, TESTS.txt y deployment.json.

## Resultado perf4 A/B/C a 3072² (2026-09-09, 13:43–13:47)

Carlos confirma: A 44 ms total VD / 72 FPS / GAME ~7 ms; B 58 ms / 72 FPS,
GAME no anotado; C 58 ms / ~62 FPS / GAME ~14 ms. Tres ciclos F4, todos
3072² entrada/salida, nitidez cero. La prueba perf3 anterior era a 2150²:
~2,04 veces menos píxeles; no atribuir la diferencia a perf4.

El cambio de escritorio se aplica: desktopAfterXr=1 en B/C estables.
Observador GPU aceptado sin ceros/invalidas. No se demuestra solución:
B ya añade latencia sin evaluar NVIDIA. Debe revisarse la atribución
exclusiva a DLAA; puente/cadencia también intervienen.

Primer ciclo: afterWait A/B/C 4,519/6,715/8,731 ms; entrega CPU
0,109/0,073/0,076; tramo entrega→siguiente wait 5,832/5,804/5,704.
Suma aproximada fuera de XR wait 10,460/12,592/14,511 ms; C2/C3
14,861/14,894, por encima del presupuesto 13,889 ms de 72 Hz.
Son intervalos transcurridos con esperas, no CPU ocupado ni GAME.
El ritmo interno C promedia ~67–68 parejas/s, no la lectura VD ~62 FPS.

Predicción tardía A1 ~46,170 y B1 ~59,962 ms; B2 transitorio, C pierde
ritmo. No confundir predicción y latencia total. AppGpuTime C ~7,7–7,9 ms
es diferido/no exclusivo, no GAME ~14. Próxima vía: separar render del
motor y esperas de salida/host en el ciclo completo, especialmente los
~5,8 ms previos a wait. No llamar IPC puro a la espera ni sumar GPU solapado.

Solo análisis/documentación; sin cambios de código, configuración o DLL.
Evidencia: artifacts/probe-0.2.6-perf4/test-2026-09-09-134312/ANALISIS.md
y analysis.json. Log SHA-256
107FEFDECC762E6231F135F4E7D089D271569548A1B0EA484DBD7E38C029F5A7.

## Candidata perf5 aplicada: partición CPU y disponibilidad de entrada (2026-09-09, 14:13)

Carlos autoriza el siguiente diagnóstico centrado en B, sin otra optimización
especulativa. Se mantiene perf4 y se instrumenta el ciclo entre entregas XR
estéreo correctas: motor L/R, Present, XR, captura, guías, envío, esperas por ojo,
salida y escritorio. Categorías exclusivas, sin doble suma; tiempo transcurrido
incluyendo esperas, no CPU ocupado ni GAME/GPU exclusivo.

El host anterior medía GPU después de su queue Wait de entrada. Por ello sus
~0,05 ms B excluían la espera previa: el cliente derecho ~2,1 ms puede contener
trabajo GPU pendiente del juego/guías/copia antes de que el helper empiece.

Se leen fences al resolver y se observa un evento opcional de entrada cada
16 frames, mientras se sigue esperando la misma salida. Sin nuevo comando GPU,
cambio de fence/protocolo ni helper. Plazo absoluto original cinco segundos,
prioridad salida/proceso/entrada. Eventos conjuntos no inventan tiempos:
coalesced/unobserved; medias ausentes -1 con denominador explícito. Se registran
esperas de frames muestreados/no muestreados para vigilar perturbación.

Contabilidad 35/35; diagnóstico entrada 22/22; cliente/fences/eventos/pipe WARP
56/56; entrega 36/36; política/controlador 97/97 y 54/54. D3D debug limpio.
Auditoría de integración exige RIGHT y par completo al cerrar ciclo, resetea
frame filtrado/expirado y reconstrucción. No se ejecuta juego ni OpenXR.

DLL instalada y copia habitual verificadas:
79EA8CFB4058F6ECB592B6072C918EE5B800FA9D01730AB93B117CBA204DB8A2.
Respaldo bioshockvr.perf4.dll. Launcher, helpers, NVIDIA310.7 y tres INI intactos.
Sin instalador, publicación, apagado ni otros proyectos.

Próxima prueba: una pasada A~20s/B~25s, NORMAL3072² inicial, misma escena;
por ahora C no necesario. Es medición, no mejora garantizada.
Detalles, contratos oficiales, tests, respaldo y huellas:
artifacts/probe-0.2.6-perf5/NOTAS.md, TESTS.txt, deployment.json.

## Resultado perf5 y reenfoque por vista (2026-09-09, 14:41–14:44)

Carlos informa A NORMAL ~42 ms total / GAME ~4 / 72 FPS; B puente ~58 ms /
GAME ~10 aproximado / 72 FPS; C DLAA ~58 ms / GAME 15–16 / caída de FPS.
La misma latencia total B/C no demuestra error de VD ni ausencia de trabajo
adicional. Los 88 informes de partición CPU son consistentes, sin invalidaciones.
La espera derecha de B1 muestreada (~2,10 ms) se reparte entre ~1,69 ms hasta
observar entrada lista y ~0,41 ms posteriores. Incluye cola GPU y despertar CPU;
no es coste exclusivo de copias ni explica por sí sola los 16 ms del overlay.

La observación prioritaria es espacial: al mirar cierta zona el rendimiento
se hunde y fuera de ella vuelve a ir bien, incluso a resoluciones altas.
Releer por dirección confirma variación sin cambiar DLAA/3072²: C1 yaw ~32700
tiene ciclos ~15,18–15,48 ms frente a ~13,88–13,89 ms en yaw ~17800.
El tramo motor L+R baja de ~9,7–9,85 a ~6,1–6,2 ms al cambiar a esa segunda
dirección, mientras la espera derecha SUBE de ~3,9 a ~5,1 ms. No es correcto
buscar la causa simplemente en la orientación con mayor espera del helper.
Los ángulos todavía no están etiquetados con el elemento visual concreto.

La generación de guías derecha se mantiene ~0,125 ms; escena GPU derecha
varía ~5,0 a ~3,5 ms, pero incluye capturas de profundidad y planificación.
No hay fallback en las muestras de escena. Quedan dos explicaciones abiertas:
trabajo anómalo de un pase visible en la ruta temporal, o coste de esa vista
amplificado por sobrecoste/sincronización del mod hasta superar el presupuesto.
No se da por demostrado un shader defectuoso, niebla, agua ni culpa de NVIDIA.

Próximo criterio diagnóstico: misma posición/modo/resolución, vista fluida →
problemática → fluida, y control NORMAL en ambas; separar por orientación
los costes del motor y los hooks/capturas. Si hace falta nueva instrumentación,
que discrimine ese tramo, no otra prueba centrada solo en latencia media.
No se implementan aquí optimizaciones de copias o Present oculto ni se crea
otro instalador. Instalación, configuración y otros proyectos intactos.

Evidencia y detalles: artifacts/probe-0.2.6-perf5/test-2026-09-09-144134/
ANALISIS.md y analysis.json. El test se recuperó de bioshockvr.prev.log; el
arranque posterior corto se conserva separado. SHA-256 del registro principal:
D391117AC5F0317C4C38D11439A9DF84FE725072B76A5687EB9383C64A14434C.

## Cierre de la investigación (9 de septiembre de 2026)

Carlos decide conservar lo optimizado y no continuar con nuevas rondas de
investigación sin una corrección concreta identificada. Las conclusiones de
la batería incremental y las configuraciones Extra 1/2/3 se recogen en
[Rendimiento](../PERFORMANCE.md). Reflejos: mayor impacto; ondulaciones:
moderado. Extra 3 será el valor predeterminado solicitado para la nueva
interfaz. Los «siguientes pasos» que figuran en este documento son históricos,
no son tareas autorizadas o pendientes de ejecución automática.
