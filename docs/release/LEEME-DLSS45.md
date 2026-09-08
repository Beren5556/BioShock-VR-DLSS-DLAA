# DLSS 4.5 experimental 0.2.3 (diagnostica)

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
perfiles K/M/L siguen siendo aplicados por el host. Reinstalar la 0.2.3 restaura
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
pueda leer y cambiar la resolucion desde el primer uso. El instalador 0.2.3 lo
comprueba y no habilita la instalacion mientras falte ese archivo.

Si los archivos se instalan pero Windows no consigue abrir automaticamente el
lanzador, la instalacion sigue siendo valida. El instalador muestra la ruta exacta
y recuerda que tambien puede abrirse desde el acceso directo del Escritorio.

**Restaurar situacion anterior** repone los archivos previos y retira las carpetas
del paquete que queden vacias. Los ajustes personales de `%LOCALAPPDATA%` y una
copia de recuperacion se conservan por seguridad.

## Limites conocidos de esta version

Esta 0.2.3 es diagnostica: corrige errores objetivos, pero la mejora visual debe
confirmarse dentro del visor.

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
