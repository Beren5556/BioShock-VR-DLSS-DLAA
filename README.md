# BioShock 1–2 VR · DLSS/DLAA

**[Distribución 0.2.16 · BioShock 1–2](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/releases/tag/v0.2.16)**
La entrega es **un único MSI nativo con ambos mods dentro**, no un EXE contenedor.
Su primera pantalla tiene un desplegable: **BioShock 1 o BioShock 2**. Cada
ejecución instala, actualiza, repara o desinstala **solo el mod elegido**.
Para gestionar el otro juego, se vuelve a abrir el mismo MSI.
Ambos mods incorporan la corrección común de DLSS y la interfaz compartida,
conservando sus adaptaciones de motor y perfiles independientes. Los núcleos
y lanzadores son exactamente los binarios probados por el usuario en ambos juegos.

El instalador incorpora los núcleos y lanzadores corregidos, el perfil DLSS
específico del 2 y las mejoras del selector y migración de la revisión 0.2.15.
Uso y novedades: [versión 0.2.16](docs/releases/v0.2.16.md).
Construcción y verificación: [cierre de distribución](docs/RELEASE-0.2.16.md).
Arquitectura del selector: [MSI por juego](docs/SINGLE-GAME-MSI-0.2.14.md).
El anterior selector de varias casillas de 0.2.13 queda descartado como entrega.

Estado, construcción, pruebas y límites: [integración 0.2.13](docs/INTEGRATION-0.2.13.md).
El usuario ha confirmado el funcionamiento de los dos mods. La validación en
otro ordenador sigue pendiente; no se garantiza igualdad de FPS entre juegos.
Los efectos gráficos del 2 se guardan desde el lanzador y requieren reinicio.
0.2.12 está descartada.

## Proyecto y versión de referencia

Fork comunitario de [BioShock VR v0.8.2](https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr/releases/tag/v0.8.2) para BioShock Remastered y BioShock 2 Remastered. Añade una ruta temporal independiente por ojo y un lanzador nativo de Windows con tres modos claros: NORMAL, DLAA y DLSS 4.5.

> Referencia anterior de BioShock 1: **[0.2.11](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/releases/tag/v0.2.11)**.
> Fork comunitario no oficial. Conserva las optimizaciones de rendimiento y el
> menú en el visor, incorpora el instalador MSI y corrige la recuperación del
> estéreo y la actualización/reinstalación del paquete.
> Las publicaciones anteriores quedan archivadas como borradores, no como
> descargas públicas. El historial del código se conserva.

> **Agradecimiento principal:** este proyecto existe gracias a **[Mohamad Balouza](https://github.com/mohamad-balouza)**, creador de BioShock VR. Él realizó el trabajo fundamental y más difícil: llevar BioShock Remastered a VR con renderizado estereoscópico, seguimiento 6DOF y controladores de movimiento. Este fork construye sobre esa enorme base; no pretende sustituirla ni atribuirse su autoría. Visita y apoya el [proyecto original de VR-Stereo-Hub](https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr).

## Instalar la distribución 0.2.16

Descarga **[BioShock-1-2-VR-DLSS-DLAA-0.2.16.msi](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/releases/download/v0.2.16/BioShock-1-2-VR-DLSS-DLAA-0.2.16.msi)**
desde la [publicación 0.2.16](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/releases/tag/v0.2.16).
Es el mismo MSI verificado y aprobado, sin regenerarlo para publicarlo.
Se acompaña de su manifiesto, suma SHA-256 y notas de uso.
Incluye el mod completo: no hace falta instalar antes el mod original.

1. Abre una vez el juego elegido desde Steam, llega al menú principal y ciérralo.
2. Con el juego y su lanzador cerrados, ejecuta el MSI y elige el juego en el desplegable.
3. Confirma la carpeta `Build\Final`: debe contener `BioshockHD.exe` para el 1 o `Bioshock2HD.exe` para el 2. Puedes usar **Explorar** para cambiarla.
4. Instala el mod. El lanzador queda en la carpeta del juego; su acceso directo de escritorio es opcional. El instalador no abre automáticamente ninguno.
5. Abre el lanzador, elige NORMAL, DLAA o DLSS y pulsa **Guardar e iniciar**. Se cierra tras confirmar el arranque; un fallo lo mantiene abierto con una explicación.

Para instalar el otro mod, vuelve a ejecutar el mismo MSI y elige ese juego.
La versión única de distribución es 0.2.16; los binarios validados conservan
su versión interna 0.2.13 para no recompilarlos después de la prueba.

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
**Opciones gráficas** del 1, F2/F3 seleccionan una fila y **F4** cambia su valor.
F4 no modifica las demás páginas. En el 2, guarda los efectos gráficos desde
el lanzador y reinicia para aplicarlos.

### Compatibilidad conocida

- BioShock Remastered y BioShock 2 Remastered para Windows, versiones Steam compatibles con BioShock VR v0.8.2.
- BioshockHD.exe x86 esperado: SHA-256 AEC21A0072CFDB15E4B525E2320C87256F14F16894F714272069270AD099A05B.
- Bioshock2HD.exe x86 esperado: SHA-256 C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C.
- Un visor y runtime PCVR compatibles con el mod original.
- GPU NVIDIA RTX y controlador compatible para DLAA/DLSS. NORMAL no usa DLSS.

Esta versión no contiene DLSS 5 Neural Rendering. Tampoco expone FXAA ni el antiguo reescalado espacial en el lanzador: la experiencia publicada se limita intencionadamente a NORMAL, DLAA y DLSS 4.5. La pestaña general de Bioshock.ini no forma parte de esta edición.

El instalador coloca `nvngx_dlss.dll` **310.7.0.0**, la única versión probada y recomendada. Se permite sustituirla manualmente por otra DLL x64: el lanzador advertirá, pero no bloqueará el uso. Con otras versiones no se garantizan el funcionamiento, la estabilidad ni la calidad de imagen y el cambio corre por cuenta del usuario. Reinstalar restaura la 310.7.0.0.

## Rendimiento

Las pruebas de BioShock 1 identifican los reflejos como la opción de mayor
impacto y las ondulaciones como una penalización moderada. Se conservan las
optimizaciones probadas del mod; la investigación adicional queda aparcada.
Consulta el [apartado de rendimiento](docs/releases/v0.2.16-performance.md) para las conclusiones,
las configuraciones contrastadas y sus límites. Desde 0.2.9, los predeterminados
desactivan reflejos y ondulaciones y mantienen el resto de la batería activado,
con avisos de impacto. Actualizar conserva las preferencias personales.

## Integridad de la versión

| Archivo | SHA-256 |
|---|---|
| BioShock-1-2-VR-DLSS-DLAA-0.2.16.msi | 1C881F0A27416198FC25A068A79044BEED7FCC37160F38E7AFE2AE09CE9A2371 |

La lista verificable está en [release/SHA256SUMS-v0.2.16.txt](release/SHA256SUMS-v0.2.16.txt),
y los 46 archivos incluidos en [el manifiesto de la versión](release/manifest-v0.2.16.json).
Los binarios aceptados están fijados en [el inventario validado](release/validated-mods-v0.2.16.json).

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
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-Repository.ps1

La construcción del mod requiere Visual Studio 2022 con MSVC x86 y CMake.
Usa el preset **integration-win32** y el preset de compilación **integration** para
conservar las cuatro optimizaciones y excluir las sondas de diagnóstico.
El host requiere MSVC x64 y un SDK NVIDIA NGX aportado localmente por el
desarrollador. El paquete cerrado reutiliza los binarios validados y no los
recompila. Consulta [la construcción del MSI dual](docs/RELEASE-0.2.16.md),
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

El usuario ha confirmado los dos mods. El MSI final se verifica por extracción
y hashes; las transacciones se prueban en una variante con identidades aisladas
de la misma fuente y los mismos 46 archivos. Esto no instala el MSI final sobre
los juegos de uso diario ni garantiza idéntico comportamiento en otros equipos.
Consulta las [notas de uso](docs/releases/v0.2.16.md) y el
[informe de cierre](docs/RELEASE-0.2.16.md). El MSI no está firmado digitalmente;
se facilita SHA-256 para comprobar su integridad.
