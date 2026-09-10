# Plan propuesto: alinear BioShock 2 con BioShock 1 y unificar el instalador

Fecha: 10 de septiembre de 2026, Europe/Madrid.
Estado: **desarrollo autorizado; candidato 0.2.13 construido y probado en aislamiento**.
Estado ejecutado y límites actuales: [informe de integración](INTEGRATION-0.2.13.md).
El plan de fases se conserva debajo; no todas están terminadas.

La distribución será **BioShock 1–2 VR · DLSS/DLAA 0.2.13**: un proyecto,
un repositorio GitHub y un instalador autocontenido con ambos mods. El lanzador
del 1 es la interfaz de referencia compartida; conservar su aspecto y controles,
adaptando únicamente identidad, rutas y funciones específicas del 2.

Copia de integración: `BioShock12VR-DLSS-DLAA`,
rama `codex/bioshock-1-2-v0.2.13`. Originales del 1 y del 2 sin modificar.
Referencia recuperable del desarrollo BS2: `7b4514090d5d1f319b463f158d076f4155a5347e`,
rama local `codex/bs2-pre-0.2.13`; 85 archivos copiados y verificados antes de integrar.

## Objetivo y decisiones de Carlos

Tomar BioShock 1 DLSS/DLAA 0.2.11, aceptado por Carlos, como referencia de
funcionamiento y experiencia de uso. Incorporar sus mejoras al desarrollo
existente de BioShock 2, conservando la adaptación específica del segundo juego.
Después entregar un instalador común, basado en el instalador actual del 1,
que permita elegir el juego.

**Aclaración final de Carlos: el instalador será UN ÚNICO archivo de distribución,
con UN ÚNICO número de versión, y contendrá dentro los DOS mods.** No se ofrecerán
dos instaladores separados para descargar ni se requerirá obtener otro paquete
para instalar el segundo juego. Las identidades/versiones técnicas internas de
los componentes no sustituyen esa versión única del instalador común.

La elección NO es excluyente: una persona puede tener los dos juegos y los dos
mods instalados. Para la primera entrega propongo ejecutar el mismo instalador
una vez para el 1 y otra para el 2. Instalar, actualizar, reparar o quitar uno
no debe modificar el otro. No hace falta una instalación simultánea de ambos
para cubrir esta necesidad.

La sesión inicial fue solo de planificación. Posteriormente Carlos autorizó
el desarrollo. Se permite implementar, compilar y probar en entornos aislados;
no desplegar sobre juegos reales ni publicar candidatos sin avisar y acordarlo.
El apagado excepcional perteneció únicamente a la sesión anterior: no repetirlo.

## 1. Punto de partida contrastado con MOD BIOSHOCK DLSS

### BioShock 1: referencia congelada

- Repositorio de referencia: `BioShockVR-DLSS-DLAA-GitHub`.
- Versión aceptada: **0.2.11**, tag `v0.2.11`, commit
  `894cae888dbe74611875eb82921a6ac80f423297`.
- Rama local `codex/v0.2.11-stable`, HEAD `065a43e`: mismo código funcional
  que el tag; solo añade corrección UTF-8 de CI y documentación de la decisión.
- **0.2.12 está descartada**: no importar sus cambios, traducciones o binarios,
  ni usar sus cachés o su antigua rama remota como referencia.
- MSI válido: `artifacts/stable-0.2.11/BioShock-VR-DLSS-DLAA-0.2.11.msi`.
- SHA-256 MSI: `2A5365E332DC311C0E010AD0BECEEAB34E905F66C6F40AB5CF44824EDAD9F660`.
- Payload válido: `artifacts/stable-0.2.11/msi-build-0.2.11/payload/`,
  contrastado por la tarea del 1: 23/23 archivos coinciden con
  `release/manifest-v0.2.11.json`.
- No usar `installer/Payload`, salidas genéricas de compilación ni una carpeta
  de juego como fuente de distribución: pueden contener versiones diferentes.
- No regenerar el MSI 0.2.11. Un cambio de producto requiere nueva identidad de
  versión; no reciclar el número descartado sin decisión expresa de Carlos.

La tarea del 1 confirmó su informe y el fin de sus trabajos. Sus cifras de
pruebas son resultados documentados anteriores, no ensayos repetidos esta noche.

### BioShock 2: trabajo que se conserva

- Repositorio de referencia: `BioShock2VR-DLSS-DLAA`.
- Desarrollo 0.1.1-beta, lanzador posterior 0.1.1.1. El instalador EXE congelado
  no contiene todas las mejoras posteriores del lanzador: inventariar código y
  binarios por separado antes de preparar el siguiente paquete.
- Steam AppID **409720**, `Bioshock2HD.exe` x86. Ejecutable admitido SHA-256:
  `C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C`.
- Cámara mundial, proyección, datos temporales y selección de escenas propios
  del 2; no sustituir por direcciones ni supuestos del 1.
- Historial independiente por ojo, procesamiento DLSS/DLAA ya adaptado,
  guardas de cierre nativo y limpieza OpenXR; preservar todo ello.
- `Shared.ini` / `[SharedOptions]` es la autoridad de resolución; sincronización
  con `Bioshock2SP.ini`. El lanzador del 2 sí fuerza el arranque en ventana.
- Guardados protegidos, configuración separada en
  `%LOCALAPPDATA%\BioshockVR\bs2`, armas y calibraciones específicas del 2.
- Confirmación del nuevo proceso correcto y ventana que responde antes de
  cerrar el lanzador; copias y recuperación de los archivos de configuración.
- Instalador previo con manifiesto `GameId=bs2`, restauración del original y
  conservación de modificaciones posteriores: migrar esta información, no perderla.

El repositorio del 2 tiene cambios existentes sin consolidar. Se conservarán
completos; no hacer reset, sustitución de carpetas ni fusión masiva sobre ellos.
El repositorio local `BIOSHOCK`, dedicado también a
investigación DLSS 5, queda fuera de esta integración.

## 2. Arquitectura recomendada

| Capa | Reutilizar del 1 | Conservar/adaptar para el 2 |
| --- | --- | --- |
| Procesamiento VR temporal | Cuatro optimizaciones, transporte, comprobaciones y políticas de imagen | Producción de cámara/profundidad/movimiento, escenas auxiliares, proyección, cierre |
| Interfaz | Imagen compacta, menú en visor, catálogo gráfico, mensajes | Identidad, rutas, archivos INI, armas y funciones realmente soportadas |
| Instalación | Experiencia visual y transacciones MSI verificadas | Segundo paquete y familia de producto, destino, manifiestos y copias propios |

Un punto de entrada común no exige una única DLL para ambos juegos. Mantener
inicialmente paquetes y adaptadores separados, compartiendo lógica bien delimitada.
Si generalizar una pieza exige rehacer el núcleo estable, portar localmente la
mejora al 2 primero y posponer la refactorización.

### Elección de formato del instalador

Recomendación: **un ejecutable de entrada con selector de juego y un MSI por
juego**, reutilizando el MSI exacto del 1 y creando el del 2 a partir de su
infraestructura probada. Los dos paquetes serán internos y vendrán incluidos:
para el usuario habrá un único instalador autocontenido con versión propia y
única. El contenedor, no el MSI 0.2.11, es el nuevo producto de distribución.
Confirmar en un prototipo aislado cómo registra y mantiene cada
paquete; el contenedor no debe reclamar ni quitar automáticamente ambos mods.

Un solo MSI con dos conjuntos de funcionalidades y destinos también es posible,
pero obliga a rediseñar el mantenimiento de la instalación existente del 1.
No es mi primera elección: actualmente cada producto admite una ubicación, con
identidades y restauración propias. No basta añadir un selector y cambiar GAMEDIR.

La primera entrega permitirá elegir un juego por ejecución. Si el otro mod ya
está instalado, se conservará. Cada juego tendrá su mantenimiento independiente
y accesos inequívocos; los ejecutables de lanzador vivirán en su `Build\Final`,
no serán copias funcionales independientes en el escritorio.

## 3. Fases de trabajo y criterios para avanzar

### Fase 0 — Preservar e inventariar

Después de aprobar el plan: crear una copia de trabajo/instantánea recuperable
del desarrollo del 2, incluyendo sus cambios no confirmados y archivos nuevos.
Registrar código, manifiestos, binarios y configuraciones de referencia del 1
sin modificar su rama, MSI ni instalación real. Registrar también las versiones
efectivamente instaladas cuando se vaya a desplegar.

Preparar una matriz de funciones: compartida, específica del 1, específica del
2 o pendiente. Entregable: base reproducible y lista de paridad. No avanzar si
hay dudas sobre qué binarios o fuentes son los aceptados.

### Fase 1 — Llevar las mejoras de rendimiento y estabilidad al 2

Portar de forma incremental y comprobable:

1. Solapar el procesamiento del ojo izquierdo con el render del derecho.
2. Reutilizar profundidad solo mientras no haya escrituras que la invaliden.
3. Solapar trabajo independiente al final del fotograma y evitar copias de color
   redundantes, manteniendo la propiedad correcta de cada recurso.
4. Entregar la pareja compatible al visor antes del trabajo exclusivo del espejo.

Conservar comprobaciones de generación/fotograma, un trabajo pendiente por ojo,
esperas acotadas, detección de host muerto y respaldo coherente por ojo. Llevar
la recuperación de estéreo tras reconfiguración a los puntos seguros del motor
del 2, sin anular apagados voluntarios ni sus protecciones de cierre.

No copiar el código entero de `openxr_runtime.cpp` sobre el del 2: hay llamadas
directas al adaptador del 1. Identificar y adaptar esos puntos explícitamente.
La identificación de cámara debe seguir siendo exacta: no eliminar el rechazo
de cámaras ambiguas ni usar sencillamente la última cámara publicada.

Entregable: núcleo BS2 candidato con optimizaciones y pruebas de coherencia,
recursos, reconfiguración y cierre. NORMAL debe conservar su ruta sin coste
temporal añadido innecesario.

### Fase 2 — Paridad del menú del visor y opciones gráficas

- NORMAL, DLSS y DLAA, resolución efectiva y modos coherentes.
- F1 para recorrer las páginas; F2/F3 para cambiar selección/valor; F4 únicamente
  para cambiar la opción seleccionada en la página gráfica.
- Resolución por ojo en pasos de **100 píxeles**, como la referencia actual del
  1; sustituye la antigua propuesta de 150 del 2 sin redondear valores guardados.
- Calidad DLSS en el visor de 35 a 90 %, en pasos de 5 puntos, saltando las
  geometrías inválidas. Conservar ratios antiguos y preferencia al cambiar modo.
- Nitidez post-DLSS 0–100 %, paso 5, predeterminada 0; solo en DLSS y sin reconstruir
  todo el procesamiento. Está en el visor, no visible en Imagen compacta del 1.
- Nueve controles: shaders, sombras, reflejos, posprocesado, ondulaciones,
  partículas, distorsión, efectos de posprocesado y detalle de fluidos.
- Comprobar lectura/escritura efectiva en el motor del 2 desde su hilo seguro.
  Si una opción necesita reiniciar, indicarlo; no presentar como aplicado un
  cambio no confirmado o confundir aplicado con guardado.
- Mantener la presentación compacta aceptada del visor, sin nuevo rediseño.

Predeterminados propuestos para una instalación realmente nueva: reflejos y
ondulaciones desactivados; las otras seis opciones booleanas activadas y fluidos
en Alto. Mantenerlos editables y marcar sombras/reflejos/ondulaciones por su coste.
Una actualización no impondrá estos valores sobre elecciones personales.

Entregable: misma experiencia de controles, conectada al motor y archivos del 2,
sin prometer que todos los efectos se reconstruyen en caliente.

### Fase 3 — Actualizar el lanzador del 2

Reutilizar la pestaña Imagen compacta: resolución de salida, modo, calidad DLSS
y resolución interna de solo lectura. Calidad del lanzador en saltos de **100
píxeles internos** con porcentaje calculado: no es la escalera de 5 puntos del
visor; esta diferencia es deliberada en el 1.

Conservar las pestañas pertinentes de cámara, manos, movimiento, cinemáticas y
HUD, y el catálogo de armas propio del 2. No trasladar el gesto de llave inglesa,
offsets ni calibraciones personales del 1. No recuperar la interfaz antigua de
FXAA, reescalado espacial o edición general de INI que ahora está oculta.

Mantener lo que el 2 ya hace mejor: arranque en ventana, escritura coordinada
Shared.ini/Bioshock2SP.ini, validación y recuperación de ambos archivos, y cierre
del lanzador solo cuando arranca realmente el juego correcto. Incorporar los
mensajes y alternativa de arranque directo del 1 sin debilitar esa comprobación.

Abrir/recargar no escribe. Guardar detecta cambios externos, verifica y conserva
preferencias. Si falta el INI original, pedir ejecutar el juego una vez; no
fabricar un perfil entero. Mantener NVIDIA 310.7.0.0 como base probada, aviso no
bloqueante para otra DLL x64 y reparación explícita para restaurar la incluida.

Entregable: lanzador BS2 actualizado y probado sin juego, con paridad visible y
funciones específicas intactas.

### Fase 4 — Instalador común y migraciones

Separar por juego ejecutable/hash, Steam ID, búsqueda de bibliotecas, rutas INI,
capacidades, registro Windows, identidad MSI, componentes, acceso directo,
copias, configuración y propiedad de archivos. No reutilizar la familia MSI del
1 para el 2. Mantener la ruta explícita por encima de la detección automática
y preguntar si existen varias copias válidas del mismo juego.

El MSI del 1 se conserva byte a byte. Para BS2, migrar su instalador anterior
0.1.0/0.1.1 y el lanzador posterior sin perder el original previo a la primera
instalación ni las modificaciones personales. Describir qué repara, qué restaura
y qué conserva; mantener conflictos recuperables y rechazar instalaciones
incompatibles o manifiestos que no pertenecen al juego elegido.

Reutilizar snapshot verificado, rollback, validación final de hashes, progreso
y tratamiento del error de instalación. No desactivar rollback ni cambiar ACL
del sistema. Una instalación correcta se informa independientemente de un
posible problema posterior del lanzador. No abrir automáticamente lanzador/juego.

Mantener licencias, créditos del mod original, dependencias redistribuibles y
ejemplos; excluir ejecutable/recursos del juego, SDK privado y herramientas LAB.
Entregable: instalador candidato común, autocontenido, con productos independientes.

### Fase 5 — Validación automática y en copias aisladas

Matriz mínima: solo BS1, solo BS2, ambos; instalar en ambos órdenes; bibliotecas
Steam en distintas unidades; instalación limpia; actualización del 2 antiguo;
reparación; desinstalación individual; cancelación; rollback por fallo inyectado;
configuración editada; copia ambigua; ruta/ejecutable incorrectos.

Verificar hashes y preferencias del juego no seleccionado antes/después. Al
reparar o quitar el 2, el 1 debe quedar idéntico, y viceversa. Probar también el
desinstalador del contenedor para que no retire el otro juego sin elección expresa.

Repetir pruebas pertinentes de geometría, estado del menú, INI, arranque real,
recuperación estéreo, aislamiento por ojo, fallo del host y cierre. Separar
pruebas sintéticas/WARP/LAB de pruebas NVIDIA y de apreciación real en visor.
No aplicar ni presumir los resultados antiguos como si validaran la nueva build.

Entregable: candidato e informe reproducible sin tocar las instalaciones aceptadas.

### Fase 6 — Prueba corta con Carlos y entrega

Solo con candidato preparado: recorrido conocido de BS2 en NORMAL → DLSS → DLAA
→ NORMAL; cambio de resolución, opciones del visor, profundidad/estéreo, manos,
HUD, carga de partida y salida limpia. Incluir una escena junto al agua vista
desde fuera y otra sin agua, comparando igual salida y ajustes. La comprobación
será breve y proporcional, no reiniciar la investigación larga de agua/latencia.

Si aparece una regresión, conservar evidencias y volver al candidato anterior;
no cambiar varias opciones a la vez ni declarar como causa lo no medido.
Comprobar también una instalación del 1 mediante el contenedor, manteniendo su
payload exacto, y una regresión funcional breve si procede.

Otro ordenador sigue siendo una validación pendiente hasta disponer de él. No
es necesario fingir esa prueba para entregar una candidata local; debe figurar
expresamente antes de afirmar compatibilidad más amplia.

Tras aceptación: versiones nuevas cuando correspondan, hashes/manifiestos,
notas claras y UN instalador con UN número de versión para Carlos, que incluya
ambos mods y sus licencias. Las versiones internas permitirán mantener y reparar
cada componente sin regenerar artefactos aceptados. Publicar en GitHub solo
cuando lo autorice.

## 4. Límites y criterio de éxito

El objetivo es paridad de funciones y estabilidad, no prometer idénticos FPS en
motores y escenas diferentes. Carlos acepta 0.2.11; eso no convierte en pruebas
nuevas las comprobaciones específicas pendientes en la última documentación.

La mejora grande de rendimiento confirmada del 1 fue el solapamiento del primer
ojo. El agua no tiene un shader reparado ni un fallo NVIDIA demostrado: reflejos
y ondulaciones mostraron costes importantes, y su investigación está aparcada.
La explicación del salto de latencia VD sigue sin cerrarse. Los vectores actuales
derivan de cámara, no son movimiento nativo completo de objetos/agua/transparencias.

Se excluirán 0.2.12, DLSS 5, Frame Generation, sondas de rendimiento, hosts de
laboratorio, relajación de validaciones de cámara y configuraciones personales
impuestas. La versión buena del 1 permanecerá disponible y sin alteraciones.

Considerar terminado cuando el 2 cubra el catálogo aplicable y pase la validación
acordada, y el instalador permita mantener ambos mods sin interferencias.

## Fuentes consultadas

Del repositorio BS1: `docs/STATUS.md`, `docs/PERFORMANCE.md`,
`docs/releases/v0.2.11-public.md`, `docs/releases/v0.2.11-validation.md`,
`installer/msi/README.md`, código de Imagen/menú/opciones gráficas y manifiestos.
Informe y confirmación de cierre de la tarea **MOD BIOSHOCK DLSS**,
ID `01a0781f-7569-72f3-9938-77c9c0b61648`, recibidos en esta sesión.

Del repositorio BS2: `docs/BS2-DEVELOPMENT.md`, `docs/BS2-EXIT-FIX.md`,
`docs/BS2-INSTALLER.md`, `apps/launcher/README.md` y estado del código existente.
Los documentos históricos se interpretan por versión; no sustituyen decisiones
posteriores de Carlos ni evidencias de una compilación concreta.
