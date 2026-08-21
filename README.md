# Slim Monitor PC

A tiny Windows 10/11 Wi-Fi traffic meter integrated into the clock/date area of the taskbar.

## English

### Features

- Unified taskbar block with current **time, date, download speed and upload speed**.
- Reuses the space normally occupied by the Windows clock/date/notification area while keeping the **Show desktop** strip free.
- Real-time Wi-Fi traffic updated every second.
- Automatic B/s, KB/s, MB/s and GB/s units with adaptive text sizing so rates are not clipped.
- Left-click opens a built-in calendar styled to match Windows 11, including month navigation and today's date.
- Right-click menu includes **Start with Windows**, calendar, taskbar realignment and exit.
- Uses Windows network-interface counters: no packet capture, additional driver or administrator rights.
- Adapts to Windows light/dark theme.
- Tooltip shows current rates and session totals.
- Single-instance protection.
- Distributed as one portable, self-contained Windows x64 EXE.
- The EXE uses the same simple **upload/download arrows icon** shown for the app in Task Manager.

### Installation

1. Download `SlimMonitorPC.exe` from the latest GitHub Release.
2. Put it wherever you want to keep it.
3. Run it.
4. Optional: right-click the taskbar block and enable **Iniciar con Windows** / Start with Windows.

If startup is enabled and you later move the EXE, disable and enable the startup option again so Windows stores the new path.

### Build

Requires the .NET 8 SDK:

```text
build-release.cmd
```

The project publishes for `win-x64`, self-contained and single-file. The resulting executable is created in `release\SlimMonitorPC.exe`.

### Windows taskbar note

Windows does not expose a supported public API for replacing the native clock module. Slim Monitor PC therefore uses a borderless, non-activating overlay aligned to the clock/date/notification area. It does not patch or modify Explorer. The narrow **Show desktop** strip at the far right is intentionally left uncovered.

---

## Español

Medidor mínimo de tráfico Wi-Fi para Windows 10/11 integrado en la zona del reloj de la barra de tareas.

### Funciones

- Bloque unificado con **hora, fecha, velocidad de descarga y velocidad de subida**.
- Aprovecha el espacio que normalmente ocupa la zona de reloj/fecha/notificaciones y deja libre la franja de **Mostrar escritorio**.
- Tráfico Wi-Fi en tiempo real, actualizado cada segundo.
- Unidades automáticas B/s, KB/s, MB/s y GB/s con tamaño de texto adaptativo para evitar textos cortados.
- Con un clic izquierdo abre un calendario propio con estética Windows 11, navegación entre meses y resaltado del día actual.
- Menú con clic derecho: **Iniciar con Windows**, abrir calendario, reajustar a la barra y salir.
- Usa los contadores de red de Windows: no captura paquetes, no instala drivers y no requiere permisos de administrador.
- Se adapta al tema claro/oscuro de Windows.
- El tooltip muestra velocidades actuales y totales de la sesión.
- Evita abrir varias instancias.
- Se distribuye como un único EXE portátil y autocontenido para Windows x64.
- El EXE utiliza el mismo icono sencillo de **flechas de subida/bajada** que se ve para la aplicación en el Administrador de tareas.

### Instalación

1. Descarga `SlimMonitorPC.exe` desde la última Release de GitHub.
2. Guárdalo donde quieras conservarlo.
3. Ejecútalo.
4. Opcional: clic derecho sobre el bloque y activa **Iniciar con Windows**.

Si después mueves el EXE, desactiva y vuelve a activar esa opción para actualizar la ruta guardada.

### Compilación

Requiere .NET 8 SDK. Ejecuta `build-release.cmd`; el EXE final se genera en `release\SlimMonitorPC.exe`.
