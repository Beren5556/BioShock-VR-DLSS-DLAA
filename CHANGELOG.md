# Registro de cambios

Este proyecto sigue versiones con el formato MAJOR.MINOR.PATCH y sufijos de prepublicación cuando corresponde.

## 0.2.0-beta · 2026-09-07

Primera versión privada preparada para distribución entre probadores.

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
