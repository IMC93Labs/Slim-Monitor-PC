# Slim Monitor PC

A tiny Windows 10/11 Wi-Fi traffic meter that shows live download and upload speed directly on the taskbar.

```text
↓ 3.8 MB/s
↑ 420 KB/s
```

## English

### Features

- Real-time Wi-Fi download and upload speed.
- Updates every second.
- Automatic B/s, KB/s, MB/s and GB/s units.
- Uses Windows network-interface counters: no packet capture, no extra driver and no administrator rights.
- Uses a very compact two-line layout (`↓` download / `↑` upload) directly on the taskbar.
- Drag it with the left mouse button to place it anywhere along the taskbar, even over existing taskbar icons. The position is remembered.
- Does not appear as a normal taskbar/Alt+Tab window.
- Adapts to the Windows light/dark taskbar theme.
- Hover tooltip shows data received/sent since the app started.
- Right-click menu includes **Start with Windows**, reset position and exit.
- Single-instance protection.
- Distributed as one portable, self-contained Windows x64 EXE with its icon embedded.

### Installation

1. Download `SlimMonitorPC.exe` from the latest GitHub Release.
2. Put it wherever you want to keep it.
3. Run it.
4. Optional: right-click the meter and enable **Iniciar con Windows** / Start with Windows.

If startup is enabled and you later move the EXE, disable and enable the startup option again so Windows stores the new path.

### Build

Requires the .NET 8 SDK:

```text
build-release.cmd
```

The project publishes for `win-x64`, self-contained and single-file. The resulting executable is created in `release\SlimMonitorPC.exe`.

### Windows taskbar note

Windows 11 does not provide a modern public API for third-party apps to become native taskbar modules. Slim Monitor PC therefore uses a small borderless, non-activating window layered above the taskbar. It can be dragged along the taskbar without modifying Explorer.

---

## Español

Medidor mínimo de tráfico Wi-Fi para Windows 10/11 que muestra en tiempo real la velocidad de descarga y subida directamente sobre la barra de tareas.

```text
↓ 3.8 MB/s
↑ 420 KB/s
```

### Funciones

- Velocidad de descarga y subida Wi-Fi en tiempo real.
- Actualización cada segundo.
- Unidades automáticas B/s, KB/s, MB/s y GB/s.
- Usa los contadores de interfaz de Windows: no captura paquetes, no instala drivers y no necesita permisos de administrador.
- Diseño más compacto en dos líneas (`↓` descarga / `↑` subida).
- Se puede arrastrar con el botón izquierdo a cualquier punto de la barra, incluso sobre otros iconos; recuerda la posición.
- No aparece como una ventana normal en la barra de tareas ni en Alt+Tab.
- Se adapta al tema claro/oscuro de Windows.
- Al pasar el ratón muestra los datos recibidos/enviados desde que se abrió la aplicación.
- Menú con clic derecho: **Iniciar con Windows**, restablecer posición y salir.
- Evita abrir varias instancias a la vez.
- Se distribuye como un único EXE portátil y autocontenido para Windows x64, con el icono integrado.

### Instalación

1. Descarga `SlimMonitorPC.exe` desde la última Release de GitHub.
2. Guárdalo en la ubicación donde quieras mantenerlo.
3. Ejecútalo.
4. Opcional: haz clic derecho sobre el medidor y activa **Iniciar con Windows**.

Si activas el inicio con Windows y después mueves el EXE a otra carpeta, desactiva y vuelve a activar esa opción para que Windows guarde la nueva ruta.

### Compilación

Requiere .NET 8 SDK. Ejecuta `build-release.cmd`; el EXE final se genera en `release\SlimMonitorPC.exe`.
