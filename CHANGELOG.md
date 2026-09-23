# Cambios

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/); versiones según [SemVer](https://semver.org/lang/es/).

## [1.3.1] - 2026-09-23

### Corregido
- Instalar encima de una versión anterior podía fallar con «Acceso denegado» si el teclado antiguo tardaba en cerrarse. Ahora el instalador reintenta y, si el archivo sigue bloqueado, aparta el antiguo y coloca el nuevo.
- Mensaje de error de instalación más claro, con los pasos a seguir.

## [1.3.0] - 2026-09-23

Pensada para usar en tablet sin conocimientos de informática.

### Añadido
- Botón **Actualizar** en la barra del teclado cuando hay una versión nueva, y punto naranja en el botón flotante si está minimizado.
- Ventana de actualización sencilla con dos botones grandes: «Actualizar ahora» y «Más tarde».

### Cambiado
- Menús desplegables más grandes y fáciles de tocar con el dedo, con los colores del tema claro u oscuro.

## [1.2.0] - 2026-09-23

Primera versión publicada, como **beta abierta**.

### Añadido
- Tema claro, además del oscuro, y modo automático que sigue al tema de Windows (menú → Tema).
- Opción «Mostrar al tocar un campo de texto»: al hacer clic o tocar donde se puede escribir, el teclado aparece si estaba oculto.

### Cambiado
- Sin la fila de funciones, la tecla Esc pasa a la izquierda de la barra superior para no perderla.

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
