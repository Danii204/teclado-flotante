# Cambios

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/); versiones según [SemVer](https://semver.org/lang/es/).

## [1.1.0] - 2026-09-23

### Añadido
- Bloque numérico con Bloq Num (con Bloq Num desactivado funciona como flechas, Inicio, Fin, RePág, AvPág, Insert y Supr).
- Distribución **English (US)** además de **Español (España)**, seleccionable desde el menú.
- Opciones para mostrar u ocultar el bloque numérico y la fila de funciones; la ventana se ajusta sola y las teclas mantienen su tamaño.
- Actualizaciones automáticas desde GitHub Releases: aviso en la bandeja e instalación con un clic, con verificación SHA-256.
- Registro local de errores y reapertura automática tras un fallo inesperado.

### Cambiado
- Al arrancar, el teclado aparece centrado en la pantalla y conserva el tamaño guardado (opción «Recordar la posición al reiniciar» para mantener también la posición).
- Los ajustes se guardan de forma atómica: un apagado repentino no puede dejarlos corruptos.
- Si se desconecta un monitor o cambia la resolución, el teclado vuelve a la vista automáticamente.

## [1.0.0] - 2026-09-23

### Añadido
- Primera versión: teclado flotante en español, redimensionable, que no roba el foco, siempre encima opcional, botón flotante al minimizar, atajo Ctrl+Alt+K, arranque oculto con Windows e instalador por usuario.
