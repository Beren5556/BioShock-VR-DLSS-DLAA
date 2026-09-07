# Procedencia y trazabilidad

Este documento separa la base original, las adaptaciones del fork y los componentes que conservan términos propios.

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
- Instalador WinForms y pruebas: installer.
- Scripts, documentación y manifiestos específicos de la versión.

## Otros terceros

Las dependencias heredadas, sus revisiones y licencias están descritas en THIRD_PARTY_NOTICES.md y dentro de cada submódulo. El archivo del juego BioshockHD.exe y los recursos de BioShock Remastered no forman parte del repositorio ni de la Release.

## Artefacto v0.2.0-beta

- Archivo: Instalador BioShock VR DLSS-DLAA Beta 0.2.exe
- SHA-256: 08EEE09B4DF57997C84DE441BC3061FCBFD0E9983F5DD1819C20300E80F9D5E8
- Lanzador contenido, SHA-256: 73572143A1791504BF8207411BD7C0BE37061C8244733013A6D35A8B03718315
- Host x64 contenido, SHA-256: 480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453
