# DLSS 4.5 experimental 0.2 (diagnostica)

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
`protocol=8`. El runtime incluido es el oficial NVIDIA DLSS 310.7.0.0
(DLSS 4.5). No contiene DLSS 5, Neural Rendering, RenoDX ni ReShade.

La lanzadera escribe `%LOCALAPPDATA%\BioshockVR\dlss.ini` de forma
transaccional y conserva copia antes de reemplazarlo. DLSS/DLAA y el filtro
espacial del mod son rutas distintas y no se habilitan simultaneamente.

## Limites conocidos de esta version

Esta 0.2 es diagnostica: corrige errores objetivos, pero la mejora visual debe
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

