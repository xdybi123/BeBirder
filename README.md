# Ear Picker Desktop Viewer

A lightweight Windows desktop application for controlling and viewing a Wi-Fi ear picker at `192.168.5.1`.

The application connects directly to the device over UDP, displays its MJPEG camera feed, reads orientation and battery data, controls camera brightness, and can republish the video as a standard MJPEG stream.

## Features

- Automatic device connection detection
- Live MJPEG video with validated UDP fragment reassembly
- Optional sensor-based image rotation with a circular mask
- Pause and resume for both video and sensor streams
- Automatic brightness detection and adjustment
- Rolling two-second FPS measurement
- Video resolution display
- Battery percentage and charging-status display
- Pitch, roll, and rotation display
- Standard HTTP MJPEG publishing on a configurable port
- Compact connection screen and responsive viewer layout

## Requirements

- Windows
- .NET Framework 4
- A Wi-Fi ear picker available at `192.168.5.1`
- The computer connected to the ear picker's Wi-Fi network

No external libraries or package manager are required.

## Build

Run:

```bat
build.bat
```

Or compile manually:

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
  /nologo ^
  /target:winexe ^
  /optimize+ ^
  /out:EarPicker.exe ^
  /reference:System.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  EarPicker.cs
```

## Usage

1. Connect the computer to the ear picker's Wi-Fi network.
2. Start `EarPicker.exe`.
3. Wait for the status to change to **Connected**.
4. Select **Start** to open the viewer.
5. Use **Pause** and **Resume** to control video and sensor streaming.
6. Adjust the brightness slider as needed.

Brightness changes are applied automatically. The current brightness is queried whenever streaming starts or resumes.

## MJPEG publishing

The viewer can republish the camera feed as a standard HTTP MJPEG stream.

1. Enter a TCP port, such as `8080`.
2. Enable **Publish MJPEG**.
3. Open the stream locally:

```text
http://127.0.0.1:8080/
```

Other devices on the LAN can use:

```text
http://<PC-IP>:8080/
```

The output uses `multipart/x-mixed-replace` with JPEG frames and can be opened by browsers, VLC, OBS, OpenCV, and similar clients.

The published stream contains the original camera frames. The circular mask and sensor-based rotation are applied only to the desktop preview.

## Device protocol

| Port | Transport | Purpose |
|---:|---|---|
| `58080` | UDP | MJPEG video fragments |
| `58090` | UDP | Commands, board information, brightness, and battery |
| `58098` | UDP | Orientation sensor data |

Important commands used by the application:

| Operation | Bytes |
|---|---|
| Start video | `20 36` |
| Stop video | `20 37` |
| Read board information | `66 39 01 01` |
| Read battery | `66 3A` |
| Read brightness | `66 3C FE` |
| Set brightness | `66 3C VALUE` |
| Start sensors | `86 06 01` |
| Stop sensors | `86 06 00` |

Video datagrams contain a four-byte fragment header followed by JPEG data. Incomplete or out-of-order frames are discarded rather than displayed with corruption.

## Sensor interpretation

The sensor packet is interpreted as big-endian signed 16-bit values:

- Field `0`: acceleration/gravity X
- Field `1`: acceleration/gravity Y
- Field `2`: acceleration/gravity Z
- Field `9`: camera rotation in degrees

Pitch and roll are inferred from fields `0–2`. The device appears to use approximately `1000` units per `1 g`.

## Project files

- `EarPicker.cs` — application source
- `build.bat` — .NET Framework compiler command
- `EarPicker.exe` — compiled application, when built locally

## Security note

The device protocol does not provide authentication or encryption. The optional MJPEG publisher listens on all local network interfaces and does not require authentication. Use it only on trusted networks.

## License

No license has been selected yet. Add a `LICENSE` file before distributing or accepting contributions.
