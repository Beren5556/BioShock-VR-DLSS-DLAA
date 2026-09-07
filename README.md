# BioShock VR · DLSS/DLAA Beta

Fork comunitario y experimental de [BioShock VR v0.8.2](https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr/releases/tag/v0.8.2) para BioShock Remastered. Añade una ruta temporal independiente por ojo y un lanzador nativo de Windows con tres modos claros: NORMAL, DLAA y DLSS 4.5.

> Versión actual: **v0.2.0-beta**. El proyecto permanece privado durante esta fase de pruebas.

## Descargar e instalar

La distribución recomendada es el instalador autónomo de la [Release v0.2.0-beta](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/releases/tag/v0.2.0-beta). No hace falta instalar antes el mod original.

1. Cierra BioShock Remastered y sus componentes VR.
2. Ejecuta **Instalador BioShock VR DLSS-DLAA Beta 0.2.exe**.
3. Selecciona la carpeta del juego que contiene BioshockHD.exe; normalmente termina en BioShock Remastered\Build\Final.
4. Instala y abre el acceso directo **BioShock VR DLSS-DLAA Beta**.
5. Elige NORMAL, DLAA o DLSS y lanza el juego.

El instalador no propone una ruta, comprueba que la copia del juego sea compatible, verifica cada recurso por SHA-256 y conserva una copia recuperable de cualquier archivo sustituido. La opción **Restaurar situación anterior** devuelve los archivos previos byte a byte.

### Compatibilidad conocida

- BioShock Remastered para Windows, versión Steam compatible con BioShock VR v0.8.2.
- BioshockHD.exe x86 esperado: SHA-256 AEC21A0072CFDB15E4B525E2320C87256F14F16894F714272069270AD099A05B.
- Un visor y runtime PCVR compatibles con el mod original.
- GPU NVIDIA RTX y controlador compatible para DLAA/DLSS. NORMAL no usa DLSS.

Esta beta no contiene DLSS 5 Neural Rendering. Tampoco expone FXAA ni el antiguo reescalado espacial en el lanzador: la experiencia publicada se limita intencionadamente a NORMAL, DLAA y DLSS 4.5. La pestaña general de Bioshock.ini no forma parte de esta edición.

## Integridad de la versión

| Archivo | SHA-256 |
|---|---|
| Instalador BioShock VR DLSS-DLAA Beta 0.2.exe | 08EEE09B4DF57997C84DE441BC3061FCBFD0E9983F5DD1819C20300E80F9D5E8 |

La lista verificable también está en [release/SHA256SUMS-v0.2.0-beta.txt](release/SHA256SUMS-v0.2.0-beta.txt).

## Qué contiene el repositorio

- El historial completo del proyecto original hasta v0.8.2.
- La integración x86 del mod, transporte compartido y guías temporales por ojo.
- El host auxiliar x64 dedicado a DLSS 4.5/DLAA.
- El código del lanzador y del instalador nativos para Windows.
- Scripts de construcción, importación de payload y pruebas reversibles.
- Documentación de arquitectura, compilación, pruebas, procedencia y licencias.

Los binarios de NVIDIA, el ejecutable del juego, el payload local y los artefactos de compilación no se almacenan sueltos en Git. El instalador probado se publica exclusivamente como activo de la Release.

## Compilar y verificar

Clona también los submódulos:

    git clone --recursive https://github.com/Beren5556/BioShock-VR-DLSS-DLAA.git
    cd BioShock-VR-DLSS-DLAA
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-Repository.ps1 -BuildLauncher

La construcción del mod requiere Visual Studio 2022 con MSVC x86 y CMake. El host requiere MSVC x64 y un SDK NVIDIA NGX aportado localmente por el desarrollador. Consulta [docs/BUILDING.md](docs/BUILDING.md) y [docs/TESTING.md](docs/TESTING.md).

## Créditos y procedencia

- **BioShock VR v0.8.2**, creado por [Mohamad Balouza](https://github.com/mohamad-balouza) y publicado por [VR-Stereo-Hub](https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr), es la base de este fork bajo licencia MIT.
- El host x64 parte de [DLSS5-Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder), de Jean-Laurent ROUZIES, e incorpora por procedencia partes de [dlss5-bridge](https://github.com/NIGos/dlss5-bridge), de NIGos; ambos bajo licencia MIT. Su nombre histórico en algunos archivos se conserva por trazabilidad; esta Release se compila en modo exclusivo DLSS 4.5.
- NVIDIA DLSS/NGX se utiliza conforme a la licencia incluida en [docs/licenses/NVIDIA-DLSS-LICENSE.txt](docs/licenses/NVIDIA-DLSS-LICENSE.txt). This software contains source code provided by NVIDIA Corporation.
- Las demás atribuciones se detallan en [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) y [PROVENANCE.md](PROVENANCE.md).

## Licencia y marcas

El código de este repositorio se ofrece bajo [licencia MIT](LICENSE), salvo los componentes que indiquen sus propios términos. NVIDIA DLSS/NGX no queda relicenciado bajo MIT.

Proyecto no oficial, sin afiliación ni respaldo de 2K Games, Take-Two Interactive, NVIDIA ni los autores del mod original. No incluye BioShock Remastered ni recursos del juego; se necesita una copia legítima. BioShock y las demás marcas pertenecen a sus respectivos titulares.

## Estado beta

La integración se ha validado funcionalmente con el binario de juego indicado, pero sigue siendo experimental. Antes de compartir diagnósticos, conserva los registros de %LOCALAPPDATA%\BioshockVR y consulta las [notas de la versión](docs/releases/v0.2.0-beta.md).
