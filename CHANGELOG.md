# Registro de cambios

Este proyecto sigue versiones con el formato MAJOR.MINOR.PATCH y sufijos de prepublicación cuando corresponde.

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
