# BioShock VR · DLSS/DLAA

Fork comunitario y experimental de [BioShock VR v0.8.2](https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr/releases/tag/v0.8.2) para BioShock Remastered. Añade una ruta temporal independiente por ojo y un lanzador nativo de Windows con tres modos claros: NORMAL, DLAA y DLSS 4.5.

> Versión pública actual: **[0.2.11](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/releases/tag/v0.2.11)**.
> Fork comunitario no oficial. Conserva las optimizaciones de rendimiento y el
> menú en el visor, incorpora el instalador MSI y corrige la recuperación del
> estéreo y la actualización/reinstalación del paquete.
> Las publicaciones anteriores quedan archivadas como borradores, no como
> descargas públicas. El historial del código se conserva.

> **Agradecimiento principal:** este proyecto existe gracias a **[Mohamad Balouza](https://github.com/mohamad-balouza)**, creador de BioShock VR. Él realizó el trabajo fundamental y más difícil: llevar BioShock Remastered a VR con renderizado estereoscópico, seguimiento 6DOF y controladores de movimiento. Este fork construye sobre esa enorme base; no pretende sustituirla ni atribuirse su autoría. Visita y apoya el [proyecto original de VR-Stereo-Hub](https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr).

## Descargar e instalar

Descarga **[BioShock-VR-DLSS-DLAA-0.2.11.msi](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/releases/download/v0.2.11/BioShock-VR-DLSS-DLAA-0.2.11.msi)**.
Incluye el mod completo: no hace falta instalar antes el mod original.

1. Antes de instalar el mod, abre BioShock Remastered una vez desde Steam, llega al menú principal y ciérralo. Esto crea `Bioshock.ini`.
2. Con el juego y el lanzador cerrados, ejecuta **BioShock-VR-DLSS-DLAA-0.2.11.msi**. Si ya tienes el mod, instala sobre la misma carpeta sin borrar nada.
3. Confirma la carpeta detectada o selecciónala con **Explorar**; debe contener BioshockHD.exe y normalmente termina en BioShock Remastered\Build\Final.
4. Instala y abre el acceso directo **BioShock VR DLSS-DLAA 0.2.11**. La casilla para crearlo viene marcada; el instalador no abre automáticamente el lanzador. Si no creas el acceso, encontrarás **Lanzador BioShock VR DLSS-DLAA.exe** en la carpeta elegida.
5. Elige NORMAL, DLAA o DLSS y pulsa **Guardar e iniciar**. El lanzador se cerrará cuando detecte que BioShock se ha abierto; si Steam no responde en 30 segundos, ofrecerá el arranque directo y permanecerá abierto si tampoco funciona.

El instalador busca la instalación previa y las bibliotecas de Steam, comprueba
la compatibilidad y conserva copias recuperables de los archivos sustituidos.
Para reinstalar el mismo MSI, ábrelo y elige **Reparar**. Actualizar y reparar
conservan los ajustes personales; reparar recupera los componentes incluidos.
Windows permite desinstalar el paquete desde Aplicaciones y recuperar los
archivos previos. No se incluye ni se sustituye el ejecutable del juego.

### Menú en el visor

**F1 abre el menú en el visor y navega por las opciones disponibles.**
F2 baja/anterior y F3 sube/siguiente. La resolución cambia en pasos de 100 píxeles
y la calidad DLSS en pasos de 5 puntos porcentuales. Solo en la página
**Opciones gráficas**, F2/F3 seleccionan una fila y **F4** cambia su valor.
F4 no modifica las demás páginas. Si un efecto no se actualiza en caliente,
reinicia el juego para aplicarlo.

### Compatibilidad conocida

- BioShock Remastered para Windows, versión Steam compatible con BioShock VR v0.8.2.
- BioshockHD.exe x86 esperado: SHA-256 AEC21A0072CFDB15E4B525E2320C87256F14F16894F714272069270AD099A05B.
- Un visor y runtime PCVR compatibles con el mod original.
- GPU NVIDIA RTX y controlador compatible para DLAA/DLSS. NORMAL no usa DLSS.

Esta versión no contiene DLSS 5 Neural Rendering. Tampoco expone FXAA ni el antiguo reescalado espacial en el lanzador: la experiencia publicada se limita intencionadamente a NORMAL, DLAA y DLSS 4.5. La pestaña general de Bioshock.ini no forma parte de esta edición.

El instalador coloca `nvngx_dlss.dll` **310.7.0.0**, la única versión probada y recomendada. Se permite sustituirla manualmente por otra DLL x64: el lanzador advertirá, pero no bloqueará el uso. Con otras versiones no se garantizan el funcionamiento, la estabilidad ni la calidad de imagen y el cambio corre por cuenta del usuario. Reinstalar restaura la 310.7.0.0.

## Rendimiento

Las pruebas de BioShock 1 identifican los reflejos como la opción de mayor
impacto y las ondulaciones como una penalización moderada. Se conservan las
optimizaciones probadas del mod; la investigación adicional queda aparcada.
Consulta el [apartado de rendimiento](docs/PERFORMANCE.md) para las conclusiones,
las configuraciones contrastadas y sus límites. Desde 0.2.9, los predeterminados
desactivan reflejos y ondulaciones y mantienen el resto de la batería activado,
con avisos de impacto. Actualizar conserva las preferencias personales.

## Integridad de la versión

| Archivo | SHA-256 |
|---|---|
| BioShock-VR-DLSS-DLAA-0.2.11.msi | 2A5365E332DC311C0E010AD0BECEEAB34E905F66C6F40AB5CF44824EDAD9F660 |

La lista verificable está en [release/SHA256SUMS-v0.2.11.txt](release/SHA256SUMS-v0.2.11.txt),
y los 23 archivos incluidos en [el manifiesto de la versión](release/manifest-v0.2.11.json).

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

La construcción del mod requiere Visual Studio 2022 con MSVC x86 y CMake.
Usa el preset **stable-win32** y el preset de compilación **stable** para
conservar las cuatro optimizaciones y excluir las sondas de diagnóstico.
El host requiere MSVC x64 y un SDK NVIDIA NGX aportado localmente por el
desarrollador. Consulta [la construcción del MSI](installer/msi/README.md),
[docs/BUILDING.md](docs/BUILDING.md) y [docs/TESTING.md](docs/TESTING.md).

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

## Validación y límites

El paquete publicado es exactamente el instalador probado localmente, sin
regenerarlo. Las pruebas automatizadas cubren instalación, actualización,
reparación, controles y recuperación del estéreo; la instalación también ha
sido confirmada por el usuario. Esto no garantiza idéntico comportamiento en
todas las configuraciones de PC y visor. Consulta las
[notas públicas](docs/releases/v0.2.11-public.md) y el
[informe de pruebas](docs/releases/v0.2.11-validation.md).
