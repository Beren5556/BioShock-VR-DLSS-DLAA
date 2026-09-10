# Validación del MSI nativo conjunto · 10/09/2026

## Entrega local

`Escritorio / Lanzadores MOD VR / BioShock-1-2-VR-DLSS-DLAA-0.2.13-CANDIDATO.msi`

- MSI nativo único, no EXE contenedor ni instalaciones MSI anidadas.
- Tamaño: 39.313.408 bytes.
- SHA-256: `0F344B2160043A8D13695876A81B76A7BA19174BCDD68A4809AD673364AF3C1D`.
- ProductCode: `{40AB1EBF-2130-97AE-25AA-FC9E11E3C2F9}`.
- UpgradeCode: `{18AFA4D3-F5EC-1895-5F34-7F882D40A18C}`.
- Versión entregada congelada: no reconstruir otro MSI distinto con este mismo
  ProductCode y versión. Las siguientes correcciones necesitan nueva versión.
- El EXE anterior descartado se conserva, pero NO es el archivo que hay que usar.
- No se ha publicado ni enviado nada a GitHub.

## Evidencia de pruebas

| Prueba | Evidencia | Resultado |
|---|---|---|
| Instalación, añadir otro mod, reparación, accesos, retirada individual/total y fallos provocados | `artifacts/combined-isolated/b55d20efc1ed42b38af47fbfba398367/combined-0.2.13/test-result.json` | 69 comprobaciones, 11 operaciones, aprobado |
| Adopción de MSI anterior BS1 simulado + beta BS2 Format=3 simulada | `artifacts/combined-isolated/65c07e21093347ec9bdcd681c701c1e9/combined-0.2.13/test-result.json` | 43 comprobaciones, 3 operaciones, aprobado |
| Validación estricta del manifiesto beta, rutas, copias y rechazo de datos ajenos | `artifacts/integration-0.2.13/migration-tests/results.txt` | 29 comprobaciones, 0 fallos |
| Extracción y SHA-256 del contenido REAL del MSI final, sin instalarlo | `artifacts/integration-0.2.13/msi/combined/package-verification.json` | 46 archivos, 4 características, sin MSI anidados |
| Elevación solicitada por el propio MSI desde proceso no elevado | Familia `2ca3242f11d645d4b3175108344d25fe`, log `01-install-bs1.log` | MSI_LUA confirma petición/consentimiento y MsiRunningElevated=1 |

Los fixtures copian solo los dos ejecutables legítimos y generan archivos de
configuración ficticios; no contienen copias completas de los juegos ni los
ejecutan. La batería contrasta al principio y al final los payloads, ejecutables,
preferencias y registros de las instalaciones reales: sin cambios.

La migración del 1 usa un predecesor **simulado con identidad aislada** para
verificar MajorUpgrade, GUID compartidos, conservación de originales y retirada
del producto anterior. No equivale a haber actualizado la instalación real de
Carlos ni a una certificación en otro ordenador. La beta del 2 es también un
fixture con su inventario y esquema Format=3, no la instalación real.

## Bloqueo de rollback resuelto

Los MSI per-user anteriores declaraban que no necesitaban elevación. Al provocar
un fallo durante la retirada completa, los archivos se recuperaban pero Windows
denegaba restaurar su registro interno (1406). El nuevo MSI conserva el alcance
por usuario y deja que Windows Installer solicite elevación: se limpia el bit
"no elevation required" de SummaryInformation ANTES de calcular su hash.
No se modifican ACL, AlwaysInstallElevated ni políticas de Windows.

Las pruebas ahora verifican explícitamente que tras el rollback el producto y
ambas características siguen registrados, y que la siguiente desinstalación
normal termina correctamente. Primero se validó con UI básica y consentimiento;
Carlos aceptó los avisos de los fallos intencionados. La repetición final se hizo
silenciosamente desde un proceso elevado para evitar más avisos.

Base documental: [metadatos de privilegios del MSI](https://learn.microsoft.com/en-us/windows/win32/msi/word-count-summary)
y [Windows Installer con UAC](https://learn.microsoft.com/en-us/windows/win32/msi/using-windows-installer-with-uac).

Las familias fallidas `a55300c6cb994b4d894cd10d949fdc89` y
`bbff2aeb646c45f089712701b5a61338` se recuperaron y desinstalaron con Windows
Installer; se verificó MsiQueryProductState=-1 para ambas. Sus logs y copias
permanecen para diagnóstico; no se borraron registros MSI manualmente.
El primer informe JSON de la recuperación no llegó a escribirse por un fallo de
serialización de la lista en PowerShell 7, corregido con ToArray(). Los logs de
las operaciones y la comprobación independiente de ausencia sí se conservaron.

## Interfaz y límites

Revisión visual y navegación real sobre el MSI aislado: selección de ambos
juegos, carpeta del 1, carpeta del 2 y confirmación con las dos acciones/rutas.
Se detuvo antes de Aplicar. Los planes de mantenimiento se contrastan también
con las pruebas nativas de selección. No se afirma haber probado visualmente
todas las combinaciones ni ejecutado Aplicar desde la interfaz completa.

El MSI instala cada lanzador dentro de Build/Final de su juego y ofrece accesos
directos independientes en el escritorio. No inicia juegos ni lanzadores.
El payload BS1 aceptado 0.2.11 y el BS2 candidato 0.2.13 son los ya compilados:
esta corrección no recompila los núcleos ni los lanzadores.

Pendiente: prueba voluntaria de instalación real y en visor de BS2, paridad de
opciones gráficas en caliente y validación en otro ordenador. Esta entrega es
un candidato local, no una declaración de estabilidad VR definitiva.
