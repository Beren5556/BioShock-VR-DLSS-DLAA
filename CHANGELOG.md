# Registro de cambios

Este proyecto sigue versiones con el formato MAJOR.MINOR.PATCH y sufijos de prepublicación cuando corresponde.

## 0.2.16 · BioShock 1–2 · 2026-09-10

- Un único MSI distribuible, selección individual de juego y dos mods completos.
- Incluye exactamente los núcleos y lanzadores cuyo funcionamiento confirmó el
  usuario en ambos juegos; no se recompilan para empaquetarlos.
- Corrige el redondeo DLSS al 50 % que podía quedar por debajo del mínimo de
  NVIDIA. Un rechazo conserva el ajuste previo y su explicación en el overlay.
- Incluye el perfil DLSS específico de BS2, omitido por el empaquetado anterior.
- Conserva las cuatro optimizaciones temporales, perfiles separados, mejoras
  del lanzador y la migración del acceso Beta corregida en 0.2.15.
- Inventarios fijados por SHA-256 y pruebas de actualización desde 0.2.15 con
  archivos realmente distintos; no se modifica el otro juego.
- Documentación de instalación, recuperación, créditos y límites actualizada.

La versión del instalador es 0.2.16 para ambos juegos; núcleo y lanzadores
conservan la versión interna 0.2.13 de los binarios aceptados. La publicación
utiliza el mismo MSI aprobado. Otro ordenador y F4 gráfico en caliente para BS2
siguen pendientes.
Detalle: [cierre 0.2.16](docs/RELEASE-0.2.16.md).

## 0.2.13 · integración local · 2026-09-10

- Selector EXE con los dos MSI internos; BS1 0.2.11 se conserva byte por byte.
- BS2 integra optimizaciones temporales y la pantalla Imagen compartida,
  manteniendo cámara, proyección, armas, rutas y guardas de salida específicas.
- Guardado coordinado Shared/SP/DLSS y migración de originales de la beta BS2.
- Pruebas aisladas del núcleo, lanzadores, instalación, rollback y reparación.
- Pendientes: visor/rendimiento, coexistencia real, F4 gráfico BS2 y otro equipo.

No publicado. Detalle: [integración 0.2.13](docs/INTEGRATION-0.2.13.md).

## 0.2.11 · 2026-09-09

Publicación del instalador probado, con las mejoras desarrolladas desde 0.2.3.

- Instalador MSI completo con detección de carpeta, actualización y reparación,
  acceso directo opcional y versionado e instrucciones finales de uso.
- Corregidos los avisos de seguridad RBF y el conflicto de paquete instalado.
- Lanzador compacto, Imagen simplificada y opciones gráficas contrastadas.
- Reflejos y ondulaciones desactivados por defecto; se conservan preferencias
  al actualizar. Cuatro optimizaciones de rendimiento mantenidas.
- Menú F1 en el visor, F2 anterior y F3 siguiente; F4 solo para opciones gráficas.
  Resolución de 100 en 100 píxeles y calidad DLSS de 5 en 5 puntos porcentuales.
- Recuperación del segundo ojo tras una desactivación automática del watchdog.
- NVIDIA 310.7.0.0 incluida; otras versiones x64 permitidas con advertencia.
- Las releases 0.2.0-beta, 0.2.1-beta y 0.2.3-beta pasan a borradores.

Detalles y límites: [notas públicas](docs/releases/v0.2.11-public.md).

## 0.2.3-beta · 2026-09-08

Revisión de fiabilidad del flujo de instalación, arranque y restauración.

### Cambiado

- El instalador diferencia una instalación completada de un fallo posterior al abrir automáticamente el lanzador; en ese caso indica cómo abrirlo manualmente sin declarar fallida la instalación.
- **Guardar e iniciar** mantiene abierto el lanzador hasta detectar `BioshockHD.exe`. Si Steam no abre el juego en 30 segundos, permite intentar el ejecutable directamente y conserva la ventana si tampoco puede iniciarlo.
- **Restaurar situación anterior** elimina las carpetas del paquete que queden vacías e informa de que los ajustes personales y la copia de recuperación se conservan.
- Las pruebas automatizadas cubren la limpieza de directorios y la migración real desde 0.2.2.

### Validación pendiente

- Recorrido completo en un segundo PC para comprobar SmartScreen/antivirus, dependencias de Windows, acceso directo y arranque real con visor antes de promover la beta como versión estable.

## 0.2.2-beta · 2026-09-07

Revisión de primera ejecución y compatibilidad avanzada del runtime, preparada a partir del primer feedback externo.

### Cambiado

- El instalador explica que BioShock Remastered debe ejecutarse una vez antes de instalar el mod y comprueba que `Bioshock.ini` ya exista.
- El lanzador se cierra después de entregar correctamente el arranque del juego a Steam o al ejecutable directo.
- El instalador mantiene NVIDIA DLSS 310.7.0.0 como runtime incluido, probado y recomendado.
- Una `nvngx_dlss.dll` x64 diferente ya no queda bloqueada: el lanzador muestra una advertencia no bloqueante y el mod registra que se usa bajo responsabilidad del usuario.

### Sin cambios

- Se conservan NORMAL, DLAA y DLSS 4.5, la selección automática K/M/L y el host estéreo por ojo.
- No se incorpora DLSS 5 Neural Rendering.

## 0.2.1-beta · 2026-09-07

Primera beta pública del fork, basada en el mismo núcleo funcional validado de 0.2.0-beta.

### Cambiado

- Créditos destacados y agradecimiento expreso a Mohamad Balouza por crear BioShock VR y realizar el trabajo fundamental sobre el que se apoya este fork.
- Enlaces visibles al proyecto original y a su versión v0.8.2 en GitHub, el instalador, el lanzador y la documentación instalada.
- Documentación, política de seguridad y metadatos preparados para publicación abierta.
- Versión del lanzador y del instalador actualizada a 0.2.1 sin modificar el payload funcional del mod ni del host DLSS.

### Validación

- Auditoría del árbol publicado y del historial añadido por el fork.
- Verificación de licencias, procedencia, hashes, construcción y pruebas reversibles del instalador.

## 0.2.0-beta · 2026-09-07

Primera versión preparada para distribución entre probadores.

### Añadido

- Integración experimental DLSS 4.5 Super Resolution y DLAA para BioShock Remastered VR.
- Transporte x86/x64 y procesamiento independiente para cada ojo.
- Lanzador nativo de Windows con los modos NORMAL, DLAA y DLSS.
- Instalador autónomo, verificado y reversible que no depende de una instalación previa del mod.
- Detección estricta del ejecutable compatible y comprobación SHA-256 de 20 recursos embebidos.
- Copia de seguridad y restauración byte a byte de archivos sustituidos.
- Trazabilidad de fuentes, dependencias y binarios de la distribución.

### Cambiado

- Interfaz del instalador reducida a una ruta de juego y su selector.
- Aspecto del instalador y del lanzador adaptado a controles nativos de Windows.
- Eliminada de la interfaz la pestaña completa de Bioshock.ini.

### Retirado de la edición publicada

- Controles de FXAA.
- Controles del reescalado espacial experimental.
- Cualquier opción o afirmación de compatibilidad con DLSS 5 Neural Rendering.

### Validación

- Self-test de 20 recursos.
- Instalación limpia, actualización, restauración y rechazo de rutas incompatibles en copias aisladas.
- Comprobación de que la prueba no alteró los archivos protegidos de la instalación real.
