# Rendimiento — BioShock 1 VR DLSS/DLAA

## Estado de la investigación

Investigación aparcada por decisión de Carlos el 9 de septiembre de 2026.
Se conservan las optimizaciones y los controles que se han probado en el juego.
No hay identificada una corrección adicional suficientemente concreta para
seguir investigando la compatibilidad con los efectos más costosos.

La versión de cierre 0.2.7 consolida el mod, simplifica únicamente Imagen y
añade Windows Installer (MSI), sin abrir el lanzador al finalizar. El mod
conserva los valores, la rueda F1/F2/F3 y las optimizaciones probadas.
La revisión 0.2.9 mantiene esas optimizaciones, añade una página gráfica al
final de F1 y cambia los predeterminados según la decisión posterior de Carlos.

## Qué hemos aprendido

Las pruebas de efectos usaron la misma DLL `0.2.6-perf5`, cambiando únicamente
la configuración gráfica. Los resultados son observaciones de usuario en la
escena probada, no porcentajes de mejora generalizables a todos los equipos.

| Opción | Resultado observado |
| --- | --- |
| Reflejos en tiempo real | Mayor penalización identificada. Reduce mucho el margen en DLAA. |
| Ondulaciones del agua | Penalización moderada, también con los reflejos apagados. |
| Sombras | Añadieron un empeoramiento menor sobre la combinación anterior. Impacto moderado observado. |
| Partículas suaves de alta calidad | Poco impacto percibido; la última prueba no aisló este ajuste del cambio de reflejos. |
| Posprocesado, posprocesado de alta calidad, distorsión y shaders de alto detalle | No reprodujeron la caída importante al reactivarlos en la secuencia. |
| Detalle de fluidos | No tuvo prueba independiente; el valor Alto estuvo presente en las tres pruebas extra. |

- No existe un techo fijo demostrado de DLAA en 3072: sin reflejos ni
  ondulaciones se pudo subir la resolución por encima de ese valor con buen
  funcionamiento, conservando el resto de efectos originales.
- El vapor continuaba visible cuando el rendimiento era bueno. Su mera
  presencia no basta para explicar el problema.
- Reflejos y ondulaciones acumulan coste. No se ha cuantificado una interacción
  superior a la suma de sus costes ni identificado un shader defectuoso.
- Las pruebas localizan opciones que perjudican el rendimiento, pero no
  separan completamente el coste propio del juego de una posible amplificación
  por la integración temporal del mod. No prueban un fallo de NVIDIA.
- El salto de latencia total de Virtual Desktop observado al cambiar de modo
  sigue sin explicar. No se da por un error de su indicador ni se confunde con
  el coste individual de un efecto.

## Configuraciones contrastadas

| Configuración | Reflejos | Ondulaciones | Partículas de alta calidad | Resto de efectos de la batería |
| --- | --- | --- | --- | --- |
| Extra 1 | Desactivados | Desactivadas | Desactivadas | Activados; fluidos Alto |
| Extra 2 | Desactivados | Activadas | Desactivadas | Activados; fluidos Alto |
| Extra 3 | Activados | Activadas | Activadas | Activados; fluidos Alto |

Extra 1 ofreció el mayor margen de rendimiento de esta batería. Extra 2 permite
conservar las ondulaciones con una penalización menor que los reflejos.

**Predeterminados desde 0.2.9, por decisión posterior del usuario:** reflejos
y ondulaciones desactivados; las otras siete opciones activadas, fluidos en Alto.
Partículas de alta calidad permanece activada. No se atribuye a esta combinación
exacta una medición que no se haya hecho: toma como referencia lo aprendido en
las pruebas extra. 0.2.7 y 0.2.8 usaban Extra 3 como predeterminado.

Reflejos, ondulaciones y sombras llevan un asterisco rojo y un único pie
«Alto impacto en el Rendimiento», según la simplificación solicitada para la
interfaz. Ese aviso compartido no altera las diferencias observadas en la tabla.
No bloquea la activación de ninguna opción y una actualización conserva los
ajustes personales. «Valores predeterminados» permite adoptar la nueva selección.
«Todo activado» se refiere exclusivamente a las nueve opciones contrastadas;
no reactiva FXAA ni el antiguo reescalado espacial.

## Optimizaciones que se conservan

- Solapamiento del trabajo temporal del ojo izquierdo con la escena derecha.
- Reutilización de la captura de profundidad cuando no ha cambiado, evitando
  copias redundantes sin retirar las necesarias tras escrituras.
- Reutilización de la copia de color ya enviada y solapamiento de trabajo
  independiente al final del fotograma.
- Entrega al visor antes del trabajo exclusivo del espejo de escritorio en
  la ruta temporal compatible.

La primera mejora importante fue confirmada en el juego. No se atribuye una
ganancia individual demostrada a cada cambio posterior. Los contadores y
modos A/B/C de diagnóstico no son nuevas optimizaciones ni opciones de usuario.
Se conserva la sincronización necesaria entre ojos, juego y host DLSS.

## Trazabilidad

El historial técnico se conserva en
[Investigación de rendimiento](investigations/bioshock1-water-performance.md).
Los INI, registros y resultados por prueba están en el archivo local
`artifacts/effects-isolation-2026-09-09`, excluido de la distribución pública.
La DLL común de la batería tiene SHA-256:

`79EA8CFB4058F6ECB592B6072C918EE5B800FA9D01730AB93B117CBA204DB8A2`.

Esta conclusión se limita a BioShock 1. No modifica BioShock 2, el proyecto
DLSS 5 ni la versión NVIDIA 310.7.0.0 probada.
