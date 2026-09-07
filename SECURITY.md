# Seguridad

## Versiones con soporte

Durante la beta pública solo se atiende la última prepublicación disponible. Las versiones anteriores se consideran sin soporte.

## Comunicar una vulnerabilidad

No publiques datos sensibles, volcados de memoria ni rutas personales en una incidencia. Utiliza la sección privada **Security > Advisories > Report a vulnerability** del repositorio o contacta directamente con su propietario.

Incluye, cuando sea posible:

- versión exacta y SHA-256 del instalador;
- versión de Windows, GPU, controlador y runtime VR;
- pasos mínimos para reproducir el problema;
- registro recortado, revisado para retirar información personal.

No adjuntes BioshockHD.exe, archivos del juego, tokens, credenciales ni el SDK de NVIDIA.

## Modelo de confianza del instalador

El instalador v0.2.1-beta valida un ejecutable de juego conocido, verifica por SHA-256 todos sus recursos, escribe mediante reemplazos transaccionales y conserva copias de recuperación. El ejecutable beta no está firmado digitalmente; verifica su hash con release/SHA256SUMS-v0.2.1-beta.txt antes de ejecutarlo.
