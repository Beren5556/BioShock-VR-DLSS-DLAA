# Procedencia y trazabilidad

Este documento separa la base original, las adaptaciones del fork y los componentes que conservan términos propios.

## Agradecimiento principal

BioShock VR y la mayor parte del trabajo fundamental de realidad virtual fueron creados por **Mohamad Balouza**. Su implementación hizo posible el renderizado estereoscópico, el seguimiento 6DOF, los controladores de movimiento y la integración con BioShock. Este fork añade una ruta DLSS/DLAA y herramientas de distribución sobre esa base; no reclama la autoría del mod original. Nuestro agradecimiento expreso es para Mohamad Balouza y para VR-Stereo-Hub por mantener y publicar el proyecto.

## Base BioShock VR

- Proyecto: https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr
- Autor identificado por el proyecto: Mohamad Balouza, https://github.com/mohamad-balouza
- Versión base: v0.8.2
- Commit base: 5bc599923bf73bf154cc35f7265ff2c568e82016
- Licencia: MIT, conservada en LICENSE.

El historial Git anterior a este fork se conserva para que cada modificación pueda compararse con la base exacta.

## Host auxiliar DLSS

- Proyecto de origen: https://github.com/jlrouzies-fr/DLSS5-Feeder
- Autor: Jean-Laurent ROUZIES
- Commit de referencia: 927d76d30e888bce497f5c5f8d496fcb696da335
- Licencia: MIT, conservada en components/dlss-host/LICENSE.

El directorio components/dlss-host contiene únicamente el subconjunto adaptado al puente de BioShock VR. Algunos nombres históricos conservan la cadena dlss5 para hacer visible su procedencia, pero el binario publicado se construye con BVR_DLSS45_ONLY=1 y solo implementa DLSS 4.5 SR/DLAA.

Partes del host proceden a su vez de [NIGos/dlss5-bridge](https://github.com/NIGos/dlss5-bridge), copyright 2026 NIGos, bajo licencia MIT. La copia exigida de esa licencia se conserva en components/dlss-host/external/bridge-1.0.19/LICENSE.

## NVIDIA DLSS/NGX

- Runtime incluido en el instalador de la versión: nvngx_dlss.dll 310.7.0.0.
- SHA-256: BE6E434A94CA32499515EB62CA0E6C274526055D568D0426E4C652DCDFB6EE6E.
- Licencia aplicable: docs/licenses/NVIDIA-DLSS-LICENSE.txt.
- Aviso requerido: This software contains source code provided by NVIDIA Corporation.

El SDK de desarrollo, sus cabeceras y bibliotecas de importación no se almacenan en Git. El runtime no se publica como producto independiente: está incorporado en el instalador de la aplicación y acompañado por sus términos.

## Código propio de esta distribución

- Adaptación de render, transporte y guías temporales para DLSS/DLAA.
- Lanzador WinForms: apps/launcher.
- Instalador MSI y pruebas: installer/msi. El instalador WinForms anterior
  se conserva como código histórico en installer/src.
- Scripts, documentación y manifiestos específicos de la versión.

## Otros terceros

Las dependencias heredadas, sus revisiones y licencias están descritas en THIRD_PARTY_NOTICES.md y dentro de cada submódulo. El archivo del juego BioshockHD.exe y los recursos de BioShock Remastered no forman parte del repositorio ni de la Release.

## Artefacto público v0.2.11

- Archivo: BioShock-VR-DLSS-DLAA-0.2.11.msi
- SHA-256: 2A5365E332DC311C0E010AD0BECEEAB34E905F66C6F40AB5CF44824EDAD9F660
- Lanzador: D6340CD90684DE6733BB10B52019AC81E755C3F60A5BC442829F91A5B550BDDA
- Mod x86: DADA33F07F38B3E617B63E0A1119D290F1D69C05E4C30A9E14970169EBA92BFB
- Host x64: 480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453
- Runtime NVIDIA: 310.7.0.0, sin cambios respecto a la distribución anterior.
- Manifiesto completo: [release/manifest-v0.2.11.json](release/manifest-v0.2.11.json).

Los artefactos anteriores se describen a continuación solo por trazabilidad;
sus publicaciones se conservan como borradores.

## Artefacto v0.2.1-beta

- Archivo: Instalador BioShock VR DLSS-DLAA Beta 0.2.1.exe
- SHA-256: 7B79BF92BDFEFFF1F857E783F8D9EDA11A62010BBA9C915C1F167BFBA07A6A69
- Lanzador contenido, SHA-256: 298E4E7E744DBD5EC11FF7A32083B1CA5EB787C7BD8A7F23C504E3062B57D0D4
- Host x64 contenido, SHA-256: 480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453

## Artefacto v0.2.2-beta

- Archivo: Instalador BioShock VR DLSS-DLAA Beta 0.2.2.exe
- SHA-256: 1C35B82A417C1A8AE5F9A71C688E27221E7C6E8E9859513C6F806E58C96977A4
- Lanzador contenido, SHA-256: A325009A20BC13680D2215C0805D0705D7D5DE811911C178364225916DC0499A
- Fork x86 contenido, SHA-256: 3EB2347E57669C1036A94CFC3C3D9ECEF713CD41F81776A2B50A1BD0DCA37677
- Host x64 contenido, SHA-256: 480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453
- Runtime NVIDIA incluido: nvngx_dlss.dll 310.7.0.0, SHA-256 BE6E434A94CA32499515EB62CA0E6C274526055D568D0426E4C652DCDFB6EE6E

## Artefacto v0.2.3-beta

- Activo publicado: Instalador-BioShock-VR-DLSS-DLAA-Beta-0.2.3.exe
- SHA-256: 2722C00F1C354781428213CA7CE5C28FDE56D86EA13CB8A8FAF59D36FE616EE0
- Lanzador contenido, SHA-256: 403B43DA8980622C4B85FAFDC4574F0D369C8B707489955F0C1C414E8123740A
- Fork x86 contenido, SHA-256: 7107B2CEBE567913888CD2FE6F58F304F435438466C81FF5E002627F0B192C78
- Host x64 contenido, SHA-256: 480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453
- Runtime NVIDIA incluido: nvngx_dlss.dll 310.7.0.0, SHA-256 BE6E434A94CA32499515EB62CA0E6C274526055D568D0426E4C652DCDFB6EE6E
