# DLSS 4.5 / DLAA beta 0.2.6 (rueda de controles)

Reorganiza las teclas sin cambiar las resoluciones ni los tramos de calidad
que ya funcionan. El panel tiene letra más pequeña y está algo más abajo.
Conserva las correcciones de cierre y estéreo de la 0.2.5:

- El botón Cerrar cierra la ventana principal y el instalador termina al abrir
  correctamente el lanzador. Si la apertura falla, permanece abierto y lo explica.
- El detector de bloqueo del estéreo respeta el mismo plazo acotado de cambio
  que OpenXR. Ya no confunde la reconstrucción de DLSS con un cuelgue de 1,2 s.
  Fuera de esa operación y tras 15 s conserva sus protecciones habituales.

La candidata requiere confirmar las nuevas teclas y el panel en el visor.
No sustituye todavía la release 0.2.3 publicada.

## Controles nuevos

- F1 en DLSS: Modo → Resolución → Calidad DLSS → Sharpness DLSS → ocultar.
- F1 en NORMAL/DLAA: Modo → Resolución → ocultar.
- F2 aumenta y F3 disminuye el valor seleccionado. En Modo recorren
  NORMAL, DLSS y DLAA; F6 deja de cambiar el modo.
- Después de la última opción, otra pulsación de F1 oculta el panel y conserva
  el modo y los ajustes. La siguiente empieza por Modo. Oculto, F2/F3 no hacen nada.
- Salida cuadrada por ojo: 1024–8192, en pasos de 256. Se omiten combinaciones
  que producirían un render interno menor de 1024.
- Tramos DLSS: 1/3, 40 %, 50 %, 58 %, 60 %, 2/3, 70 %, 80 % y 90 %.
  Son porcentajes lineales; el redondeo par puede variar ligeramente el efectivo.
- NORMAL y DLAA son 100 %. La calidad DLSS se recuerda al pasar por ambos.
- El panel del visor es independiente de la imagen reconstruida.
- Sharpness/nitidez: 0–100 %, pasos de 5, únicamente en DLSS. 0 % (inicial)
  conserva exactamente la salida anterior, sin ejecutar el filtro adicional.
  Se aplica un filtro de nitidez posterior a DLSS, no el antiguo parámetro
  de NVIDIA, que ya no está soportado. No cambia resolución ni calidad.
  Cambiar nitidez no reconstruye OpenXR, NGX ni los historiales de los ojos.
  La preferencia se recuerda al pasar por NORMAL/DLAA, pero no actúa en ellos.

Los cambios se muestran como aplicados solo tras confirmar la resolución real
del motor y el par OpenXR. Se guardan para que el lanzador los lea al volver.
La ruta nueva de resolución usa el comando del juego en modo ventana; no se
fuerza el modo ventana en el arranque del lanzador. Si el cambio falla se
intenta restaurar el ajuste anterior; un fallo que impida recuperarlo se
indica expresamente y requiere reiniciar.

## Base que se conserva

Esta version corrige dos errores confirmados de la 0.1 que podian degradar la
imagen y producir movimiento temporal incorrecto:

- BioShock usa profundidad D3D normal y finita (`cerca=0`, `lejos=1`). La 0.1
  la declaraba invertida y trataba el plano lejano como infinito.
- El host podia escoger un tramo DLSS equivocado porque varios rangos admitidos
  por NGX se solapan. Ahora elige el modo cuya resolucion optima coincide mejor
  con el render solicitado y luego valida su rango. Un 50 % selecciona
  realmente **Rendimiento**.

Tambien enlaza cada render con un identificador monotono entre Build, ojo,
camara y profundidad; rechaza parejas L/R incoherentes; y reinicia ambos
historiales al detectar una carga, pausa, fallo de captura o discontinuidad.

## Que significa cada resolucion

- **Anchura/altura de render**: resolucion a la que BioShock dibuja cada ojo.
- **Salida VR**: resolucion objetivo que recibe OpenXR por ojo.
- **DLAA**: render y salida deben ser iguales.
- **DLSS SR**: la salida permanece como objetivo y el tramo de calidad calcula
  un render menor. Por eso cambiar el porcentaje o la calidad modifica la
  resolucion de arriba, no la salida VR.

La salida VR si puede editarse. Conviene elegir primero esa salida y despues el
tramo Calidad/Equilibrado/Rendimiento/Ultra rendimiento. El ajuste fino de la
resolucion de render convierte el perfil en personalizado.

## Arquitectura y archivos

BioShock Remastered y el mod son x86; NVIDIA NGX es x64. El mod inicia dos
ayudantes x64 independientes, uno por ojo, con texturas, fences e historial
separados.

Archivos requeridos junto al mod:

```
host64\BioShockVR-DLSS45-Host64.exe
host64\nvngx_dlss.dll
host64\dlss-capabilities.ini
```

El manifiesto declara `phase=DLSS45`, `eyeHosts=2`, `runtime=310.7.0` y
`protocol=8`. El instalador siempre coloca el runtime oficial NVIDIA DLSS
310.7.0.0 (DLSS 4.5), que es la unica version probada con esta integracion.
No contiene DLSS 5, Neural Rendering, RenoDX ni ReShade.

Un usuario avanzado puede sustituir manualmente `host64\nvngx_dlss.dll` por
otra version x64. El lanzador la detecta y muestra una advertencia, pero no la
bloquea. El funcionamiento, la estabilidad y la calidad con otras versiones
no estan garantizados y la sustitucion corre por cuenta del usuario. Los
perfiles K/M/L siguen siendo aplicados por el host. Reinstalar la 0.2.6 restaura
la DLL 310.7.0.0 probada.

La lanzadera escribe `%LOCALAPPDATA%\BioshockVR\dlss.ini` de forma
transaccional y conserva copia antes de reemplazarlo. DLSS/DLAA y el filtro
espacial del mod son rutas distintas y no se habilitan simultaneamente. Al usar
**Guardar e iniciar**, el lanzador espera hasta 30 segundos a detectar
`BioshockHD.exe`. Solo se cierra tras confirmar el proceso del juego o iniciar
correctamente la via directa; si ambas fallan, permanece abierto.

## Primera instalacion

Antes de instalar el mod, abre BioShock Remastered una vez desde Steam, espera
a llegar al menu principal y cierra el juego. Asi Windows crea
`%APPDATA%\BioshockHD\Bioshock\Bioshock.ini`, necesario para que el lanzador
pueda leer y cambiar la resolucion desde el primer uso. El instalador 0.2.6 lo
comprueba y no habilita la instalacion mientras falte ese archivo.

Si los archivos se instalan pero Windows no consigue abrir automaticamente el
lanzador, la instalacion sigue siendo valida. El instalador muestra la ruta exacta
y recuerda que tambien puede abrirse desde el acceso directo del Escritorio.

**Restaurar situacion anterior** repone los archivos previos y retira las carpetas
del paquete que queden vacias. Los ajustes personales de `%LOCALAPPDATA%` y una
copia de recuperacion se conservan por seguridad.

## Limites conocidos de esta version

Esta 0.2.6 es una candidata: las pruebas CPU y D3D11 no sustituyen la prueba
de los controles, la resolución y la imagen dentro del visor. La caída de
rendimiento cerca del agua se ha investigado en BioShock 1 y sigue pendiente
de atribución definitiva. La versión añade mediciones muestreadas de GPU y
tiempos de espera por ojo, sin esperas adicionales ni cambios en la captura.
No se anuncia una mejora de FPS. NORMAL conserva su ruta directa.

- El juego elige dinamicamente un plano lejano de 1024 o 65536 uu. La ruta de
  mundo usa provisionalmente 65536; la telemetria permitira verificar si alguna
  escena temporal necesita 1024.
- Aun no se aplica jitter real a la proyeccion raster. Se envia `(0,0)` a NGX
  porque enviar un desplazamiento que la imagen no contiene seria incorrecto.
  Sin jitter, DLAA puede no superar todavia la nitidez del modo apagado.
- Los vectores reconstruyen el movimiento de camara y profundidad. Manos y
  objetos animados no tienen aun vectores propios y pueden dejar estela.
- Si el ojo derecho falla despues de que NGX haya terminado el izquierdo, ese
  unico par ya no puede deshacerse; se invalidan ambos historiales y el par
  siguiente se fuerza completo por la ruta espacial.
- Menus, cargas, cinematicas o fotogramas sin guias coherentes usan salida
  directa/espacial de seguridad y no alimentan un historial temporal antiguo.

Los registros principales quedan en:

```
%LOCALAPPDATA%\BioshockVR\bioshockvr.log
%LOCALAPPDATA%\BioshockVR\BioShockVR-DLSS45-eye0.log
%LOCALAPPDATA%\BioshockVR\BioShockVR-DLSS45-eye1.log
```
