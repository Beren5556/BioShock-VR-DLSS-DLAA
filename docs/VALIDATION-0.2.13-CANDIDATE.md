# Validación local · candidato 0.2.13 · 2026-09-10

## Resultado

Candidato construido y verificaciones aisladas superadas. **No instalado en
los juegos reales, no publicado y no validado todavía en visor.**

Código de la integración: 27c175ca7d5e95bf5788265dc382b9b331dde7c7,
rama codex/bioshock-1-2-v0.2.13. Merge local completado; la documentación
posterior no modifica los binarios ensayados.

## Artefactos identificados

- EXE único: BioShock-1-2-VR-DLSS-DLAA-0.2.13-CANDIDATO.exe (70,9 MiB).
  SHA-256: 63701688677BFF132917677893B60AE6460391719CA4793422ED94ECD4ED6E1A.
- Dentro, MSI BS1 0.2.11 aceptado, sin regenerar:
  2A5365E332DC311C0E010AD0BECEEAB34E905F66C6F40AB5CF44824EDAD9F660.
- Dentro, MSI BS2 0.2.13 candidato:
  EE45605FEC0BAD54D11EB48C29B13CED5041F1A884ED5A6F8B987AF080947033.
- MSI de familia aislada, NO distribuible:
  CF181C9DBEC5C47C1F97C73FCF3D24FC25F97D55A6D7E8E0D9F231010C1122C0.
  Sus 23 archivos de payload coinciden con el MSI BS2 candidato.

Los archivos están bajo artifacts/integration-0.2.13/bundle y msi/bs2.
Son artefactos locales ignorados por Git; sus hashes identifican esta build
concreta, no una publicación estable.

## Comprobaciones realizadas

| Batería | Resultado |
| --- | --- |
| Núcleo | 12/12 ejecutables correctos |
| Temporal WARP | BS1 y BS2 correctos, 13 casos de reutilización en cada uno |
| Guardado BS2 | 20/20 casos, rollback y conflicto externo incluidos |
| Lanzadores | Ambos compilan y pasan --self-test; Imagen revisada visualmente |
| Migración beta | 29/29 comprobaciones unitarias |
| MSI BS2 final | 114 comprobaciones, 21 ejecuciones MSI, 0 avisos de seguridad de rollback |
| EXE común | 24/24: recursos, identidades separadas, interfaz y cierre |
| Sintaxis PowerShell | 50 scripts sin errores de análisis |
| Repositorio | Política, versiones, licencias/manifiestos y git diff --check correctos |

El producto MSI aislado quedó desinstalado. Se conservaron los informes y
sus copias recuperables. Los fixtures MSI usan solo el EXE legítimo necesario
para comprobar el destino y datos ficticios; no se copió ni ejecutó el juego
completo.

Evidencias locales:

- artifacts/integration-0.2.13/tests/20260910-105023/results.json
- artifacts/integration-0.2.13/migration-tests/results.txt
- artifacts/msi-isolated/0bd55642b6e640158728629532379e54/0.2.13/msi-test-result.json
- artifacts/integration-0.2.13/bundle/verification.json
- artifacts/integration-0.2.13/bundle/manifest.json
- artifacts/integration-0.2.13/bundle/selector-final.png

La última batería MSI conserva su fixture BvrMsiBattery-18ac224b6a5b471db1b2cd71aeba8eef.
Los 85 archivos modificados/nuevos de la referencia BS2 original siguen
coincidiendo con el snapshot 7b4514090d5d1f319b463f158d076f4155a5347e.
La referencia BS1 permanece en 065a43e y su payload aceptado conserva 23/23 hashes.

Lectura del manifiesto BS2 realmente instalado: Format=3, GameId=bs2,
0.1.1-beta, 21 registros. Sus cuatro archivos que tenían original previo
conservan las copias con el SHA esperado. Esta lectura no ejecutó la migración.

## Lo que falta

- Acordar la instalación del candidato BS2 y probar NORMAL/DLSS/DLAA, cambios
  de resolución, agua vista desde fuera, manos/HUD, carga y cierre en visor.
- Comprobar la coexistencia real e instalación de los productos en ambos órdenes.
  Las identidades no se solapan, pero eso no sustituye la prueba funcional.
- Derivar F4 nativo para gráficos BS2 si se exige paridad en caliente.
  Actualmente se cambian en el lanzador, se guardan y se reinicia.
- Completar la revisión de paridad de arranque: el BS2 conserva inicio directo
  cuando falla la solicitud Steam; la oferta adicional de inicio directo tras
  agotar el timeout del BS1 no se ha trasladado todavía.
- Validar en otro ordenador. Publicar solo tras aceptación.

No declarar idénticos FPS, paridad completa ni estabilidad de esta build en VR.
No apagar el equipo: la autorización de apagado era excepcional de otra sesión.
