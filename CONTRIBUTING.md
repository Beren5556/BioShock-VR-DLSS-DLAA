# Contribuir

El repositorio está en beta pública. Los colaboradores deben abrir una rama corta, explicar el efecto observable del cambio y adjuntar la validación realizada.

Este es un fork de [BioShock VR](https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr), creado por Mohamad Balouza. Toda contribución debe conservar esa atribución y distinguir con claridad el trabajo original de los cambios específicos de este fork.

## Reglas del proyecto

- Mantén la compatibilidad con la base BioShock VR v0.8.2 indicada en PROVENANCE.md.
- No presentes cambios experimentales como funciones del proyecto original.
- No añadas archivos del juego, volcados, capturas RenderDoc, credenciales, el SDK NGX ni binarios NVIDIA sueltos.
- Conserva los modos públicos NORMAL, DLAA y DLSS 4.5; DLSS 5 pertenece a una línea de investigación distinta.
- No vuelvas a exponer FXAA, el reescalador espacial ni la pestaña completa de Bioshock.ini sin una decisión explícita de producto.
- Añade pruebas o una justificación verificable para cada cambio funcional.

Antes de proponer un cambio ejecuta:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-Repository.ps1 -BuildLauncher

Para cambios en el instalador, sigue además docs/TESTING.md y prueba solo sobre copias aisladas. Nunca uses una instalación real como fixture modificable.
