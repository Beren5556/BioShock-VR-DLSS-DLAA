# BioShock VR · DLSS/DLAA Beta

Fork comunitario y experimental de [BioShock VR v0.8.2](https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr/releases/tag/v0.8.2) para BioShock Remastered. Añade una ruta temporal independiente por ojo y un lanzador nativo de Windows con tres modos claros: NORMAL, DLAA y DLSS 4.5.

> Versión actual: **v0.2.3-beta**. Es una beta comunitaria y no oficial.

> **Agradecimiento principal:** este proyecto existe gracias a **[Mohamad Balouza](https://github.com/mohamad-balouza)**, creador de BioShock VR. Él realizó el trabajo fundamental y más difícil: llevar BioShock Remastered a VR con renderizado estereoscópico, seguimiento 6DOF y controladores de movimiento. Este fork construye sobre esa enorme base; no pretende sustituirla ni atribuirse su autoría. Visita y apoya el [proyecto original de VR-Stereo-Hub](https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr).

## Descargar e instalar

La distribución recomendada es el instalador autónomo de la [Release v0.2.3-beta](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/releases/tag/v0.2.3-beta). No hace falta instalar antes el mod original.

1. Antes de instalar el mod, abre BioShock Remastered una vez desde Steam, llega al menú principal y ciérralo. Esto crea `Bioshock.ini`.
2. Ejecuta **Instalador BioShock VR DLSS-DLAA Beta 0.2.3.exe**.
3. Selecciona la carpeta del juego que contiene BioshockHD.exe; normalmente termina en BioShock Remastered\Build\Final.
4. Instala y abre el acceso directo **BioShock VR DLSS-DLAA Beta**.
5. Elige NORMAL, DLAA o DLSS y pulsa **Guardar e iniciar**. El lanzador se cerrará cuando detecte que BioShock se ha abierto; si Steam no responde en 30 segundos, ofrecerá el arranque directo y permanecerá abierto si tampoco funciona.

El instalador no propone una ruta, comprueba que la copia del juego sea compatible, verifica cada recurso por SHA-256 y conserva una copia recuperable de cualquier archivo sustituido. Si solo falla la apertura automática del lanzador, informa de que la instalación sí terminó y muestra cómo abrirlo manualmente. La opción **Restaurar situación anterior** devuelve los archivos previos byte a byte, retira las carpetas del paquete que queden vacías y conserva los ajustes personales y una copia de recuperación.

### Compatibilidad conocida

- BioShock Remastered para Windows, versión Steam compatible con BioShock VR v0.8.2.
- BioshockHD.exe x86 esperado: SHA-256 AEC21A0072CFDB15E4B525E2320C87256F14F16894F714272069270AD099A05B.
- Un visor y runtime PCVR compatibles con el mod original.
- GPU NVIDIA RTX y controlador compatible para DLAA/DLSS. NORMAL no usa DLSS.

Esta beta no contiene DLSS 5 Neural Rendering. Tampoco expone FXAA ni el antiguo reescalado espacial en el lanzador: la experiencia publicada se limita intencionadamente a NORMAL, DLAA y DLSS 4.5. La pestaña general de Bioshock.ini no forma parte de esta edición.

El instalador coloca `nvngx_dlss.dll` **310.7.0.0**, la única versión probada y recomendada. Se permite sustituirla manualmente por otra DLL x64: el lanzador advertirá, pero no bloqueará el uso. Con otras versiones no se garantizan el funcionamiento, la estabilidad ni la calidad de imagen y el cambio corre por cuenta del usuario. Reinstalar restaura la 310.7.0.0.

## Integridad de la versión

| Archivo | SHA-256 |
|---|---|
| Instalador BioShock VR DLSS-DLAA Beta 0.2.3.exe | 2722C00F1C354781428213CA7CE5C28FDE56D86EA13CB8A8FAF59D36FE616EE0 |

La lista verificable también está en [release/SHA256SUMS-v0.2.3-beta.txt](release/SHA256SUMS-v0.2.3-beta.txt).

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

## Créditos y agradecimientos

- **Agradecimiento especial a [Mohamad Balouza](https://github.com/mohamad-balouza)**, creador de **BioShock VR**, por resolver la parte esencial de llevar la trilogía a realidad virtual. El renderizado estéreo, el seguimiento de cabeza, los controladores de movimiento y la integración base con los juegos son fruto de su trabajo. Sin esa base, este fork no existiría.
- **[BioShock VR v0.8.2](https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr/releases/tag/v0.8.2)**, publicado por [VR-Stereo-Hub](https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr), es la versión exacta sobre la que se construye este fork y se conserva bajo licencia MIT.
- Este fork de **Beren5556** se limita a la adaptación DLSS/DLAA, el transporte temporal por ojo, el lanzador, el instalador y su documentación específica.
- El host x64 parte de [DLSS5-Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder), de Jean-Laurent ROUZIES, e incorpora por procedencia partes de [dlss5-bridge](https://github.com/NIGos/dlss5-bridge), de NIGos; ambos bajo licencia MIT. Su nombre histórico en algunos archivos se conserva por trazabilidad; esta Release se compila en modo exclusivo DLSS 4.5.
- NVIDIA DLSS/NGX se utiliza conforme a la licencia incluida en [docs/licenses/NVIDIA-DLSS-LICENSE.txt](docs/licenses/NVIDIA-DLSS-LICENSE.txt). This software contains source code provided by NVIDIA Corporation.
- La relación completa se conserva en [ACKNOWLEDGEMENTS.md](ACKNOWLEDGEMENTS.md), [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) y [PROVENANCE.md](PROVENANCE.md).

## Licencia y marcas

El código de este repositorio se ofrece bajo [licencia MIT](LICENSE), salvo los componentes que indiquen sus propios términos. NVIDIA DLSS/NGX no queda relicenciado bajo MIT.

Proyecto no oficial, sin afiliación ni respaldo de 2K Games, Take-Two Interactive, NVIDIA ni los autores del mod original. No incluye BioShock Remastered ni recursos del juego; se necesita una copia legítima. BioShock y las demás marcas pertenecen a sus respectivos titulares.

## Estado beta

La integración se ha validado funcionalmente con el binario de juego indicado, pero sigue siendo experimental. Antes de compartir diagnósticos, conserva los registros de %LOCALAPPDATA%\BioshockVR y consulta las [notas de la versión](docs/releases/v0.2.3-beta.md).
