# LoRaChat — Meshtastic Desktop & Mobile Companion

A cross-platform companion app for [Meshtastic](https://meshtastic.org) LoRa radios, with a
tactical-console interface for chat, mesh node management, mapping, device configuration, and
telemetry simulation. One codebase runs natively on **Windows, macOS, Linux, and Android**.

> Talk to your mesh, see your nodes on a map, tune your radio, and simulate telemetry — all from a
> single dark, tactical UI, over **USB serial or Bluetooth LE**, with **no Python runtime required**.

---

## Table of contents
- [What it is](#what-it-is)
- [Features](#features)
- [Supported platforms & transports](#supported-platforms--transports)
- [Installation](#installation)
- [Getting started](#getting-started)
- [Configuration](#configuration)
- [Advantages over other apps](#advantages-over-other-apps)
- [Architecture](#architecture)
- [Roadmap](#roadmap)
- [Troubleshooting](#troubleshooting)
- [Credits & license](#credits--license)

---

## What it is

LoRaChat connects to a Meshtastic radio and gives you a full-featured control station: send and read
messages on the mesh, manage the node database, view nodes on an interactive (optionally offline) map,
push a complete device configuration, and run a built-in telemetry-substitution engine for testing.

The entire UI is a single-page tactical console rendered in an embedded WebView, backed by a native
**C# implementation of the Meshtastic protocol** — it speaks directly to the radio over the wire, so
there is **no dependency on Python, the `meshtastic` CLI, or any external bridge**.

---

## Features

### 💬 Messaging
- **General channel chat** — broadcast text on the primary channel, with live incoming messages
  attributed to the sending node by name.
- **Private / direct chat** — per-node encrypted direct-message conversations on a dedicated private
  channel (PSK-protected), with per-contact history you can open, review, and delete.
- **Unread badges**, delivery to the mesh, and a live **airtime log** of every event.

### 🛰️ Mesh node management
- Live **node list** sorted by favorites then last-heard, showing name, role, hardware model, hops away,
  SNR, battery, and voltage.
- Per-node actions: **favorite / unfavorite**, **ignore / unignore**, **remove**, **request position**,
  and **reboot** — all issued to the radio as native admin commands.
- **Show/hide ignored nodes**, reset the node database, and automatic staleness tracking.

### 🗺️ Mapping
- Interactive **Leaflet map** of local mesh nodes with position, plus a **Global map** of nodes seen
  over MQTT.
- **Offline tiles** — download the world tile pyramid (zoom 0–7) once and use the map with no internet
  in the field.
- **Zones** — draw and name rectangular areas on the map, persisted between sessions.
- Online (OpenStreetMap), satellite, and offline tile sources.

### 📡 Signal & status
- Live **SNR / RSSI** readouts with a rolling signal-quality bar chart.
- **RX / TX packet counters**, **connection uptime**, region, channel, frequency, and active-node count
  in the header.

### ⚙️ Device settings & control
A comprehensive **Settings** screen for every Meshtastic parameter, covering:
- **LoRa** — region, modem preset/profile, hop limit, TX power, channel number, frequency override,
  bandwidth/spread-factor/coding-rate (manual mode), duty-cycle override.
- **Position / GPS** — GPS mode, update interval, broadcast interval, smart broadcast, fixed position.
- **Channels** — primary channel name/PSK/uplink/downlink, private channel, secondary channel count.
- **Power, WiFi, Bluetooth, Display** — power saving, WiFi SSID/PSK, Bluetooth mode/PIN, screen options.
- **Telemetry module** — device/environment/air-quality/power-metrics intervals.
- **Device MQTT module, security/admin** (remote admin, admin key), and feature **modules**
  (serial, external-notification, store & forward, range test, canned messages, neighbor info,
  paxcounter, ambient lighting, audio).

App-side settings (identity/owner, MQTT credentials, private-channel PSK, region/channel display, node
timeout, ignored nodes, theme, language) apply immediately and persist. Operations that reach the radio
today are issued as native admin commands: **owner name, node favorite/ignore/remove, request position,
fixed position, and reboot**, plus **text messaging**. Bulk over-the-air **read/write of the full device
config** (pushing the whole LoRa/module configuration to the radio) is on the roadmap — see
[Roadmap](#roadmap).

### 🧪 Telemetry simulation (unique)
A built-in **Simulation / Telemetry-Substitution engine** for testing dashboards, maps, and alerts with
controlled data instead of waiting for real sensor conditions:
- Three modes per metric — **static** (fixed value), **offset** (real value ± delta), and **random**
  (value within a range).
- **Hot-reload**: rules live in `telemetry_mock.json` and reload the instant you save the file.
- Optionally **broadcast** the simulated position/telemetry to the rest of the mesh so other nodes
  record the substituted values.
- Every substitution is logged, so simulated data is never silently mistaken for real data.

### 🌐 MQTT integration
- Connect to a Meshtastic MQTT broker (e.g. the public `mqtt.meshtastic.org`) to populate the **Global
  map** with nodes reported across the wider network, with auto-reconnect.

### 🎨 Presentation
- Five tactical **themes** — Tactical, Midnight Violet, Daylight, Solar Amber, High Contrast.
- **Bilingual UI** — English and Russian.
- A console tab for quick status commands.

---

## Supported platforms & transports

| Platform | How to build | Transport(s) |
|----------|--------------|--------------|
| **Windows** | `net10.0-desktop` | USB serial |
| **macOS**   | `net10.0-desktop` | USB serial |
| **Linux**   | `net10.0-desktop` | USB serial |
| **Android** | `net10.0-android` (`.apk`) | **Bluetooth LE** (primary), USB-OTG (CDC-ACM radios) |

The Meshtastic protocol layer is shared across all of them; only the transport (serial framing vs. BLE
GATT) and a few OS adapters differ.

---

## Installation

### Prebuilt
Grab a build for your OS (see **[BUILD.md](BUILD.md)** to produce one). On Android, install the signed
`.apk` via `adb install -r <apk>` or by sideloading.

### Build from source
Full, per-OS instructions — prerequisites, build, publish, and troubleshooting — are in
**[BUILD.md](BUILD.md)**. In short (from the repo root):

```bash
# Desktop (Windows / macOS / Linux)
dotnet run --project LoRaChat.App/LoRaChat.App.csproj -f net10.0-desktop -c Release

# Android APK
dotnet build LoRaChat.App/LoRaChat.App.csproj -f net10.0-android -c Release \
  -p:AndroidSdkDirectory="<path-to-android-sdk>"
```

> **No radio handy?** Launch the desktop app with the environment variable `LORACHAT_DEMO=1` to explore
> the whole UI driven by a built-in demo backend (fake nodes, chat, signal, and telemetry).

---

## Getting started

1. **Connect your radio.**
   - *Desktop:* plug in over USB, pick the **COM/tty port** in the bottom bar, press **Listen**.
   - *Android:* tap **Scan BLE**, choose your radio, press **Listen** (grant Bluetooth permission on
     first use).
2. On connect, LoRaChat runs the Meshtastic config handshake and populates the **node list**,
   **your node identity**, and the **header** (region, channel, frequency).
3. Type in **General Channel** to broadcast, or open a node's **Private** chat for a direct message.
4. Open **Map** to see positioned nodes; **Nodes** to manage them; **Settings** to configure the device.

---

## Configuration

### Connecting
- **Serial (desktop):** select the port and click **Listen**. Ports appear as `COMx` (Windows),
  `/dev/ttyUSB*` or `/dev/ttyACM*` (Linux), `/dev/cu.*` (macOS).
- **BLE (Android):** **Scan BLE** → select device → **Listen**.

### Device settings
Open **Settings**, adjust any fields (LoRa/region, channels, GPS, power, modules, …) and **Apply** to
save them. App-side values (identity/owner, MQTT credentials, private-channel PSK, region/channel
display, node timeout, ignored nodes, theme, language) take effect immediately; **Reset** restores
defaults while preserving your node identity. Node-level actions (favorite/ignore/remove, request
position, fixed position, reboot) and messaging are sent to the radio natively. Full over-the-air
config read/write is in progress (see [Roadmap](#roadmap)).

### Your node identity
LoRaChat adopts your radio's real node ID automatically on connect. You can also set/generate a local
ID and owner long/short name from Settings.

### MQTT (Global map)
In Settings, set the **MQTT broker**, **username**, and **password**, then **Connect** on the Global
tab. For the public broker `mqtt.meshtastic.org`, the community credentials are `meshdev` /
`large4cats`. Secrets are encrypted at rest.

### Telemetry simulation
Enable the **telemetry simulation** toggle, then edit `telemetry_mock.json` (in the app's data folder;
the app logs the exact path). Each rule targets a metric with a mode:
```jsonc
{
  "enabled": true,
  "realTelemetryIntervalSecs": 3600,
  "rules": [
    { "metric": "latitude",  "enabled": true, "mode": "static", "value": 50.45 },
    { "metric": "longitude", "enabled": true, "mode": "offset", "offset": 0.0005 },
    { "metric": "battery",   "enabled": true, "mode": "random", "min": 20, "max": 40 }
  ]
}
```
Saving the file hot-reloads the rules immediately.

### Offline maps
On the **Map** tab, use **Download world tiles** to cache zoom 0–7 for offline use.

### Themes & language
Pick one of five themes and switch between English/Russian in Settings — both persist.

### Where settings live
Settings, node database, chat history, zones, telemetry rules, and the tile cache are stored per-user in
the platform app-data directory. Secret fields (MQTT/channel passwords, admin key) are **encrypted at
rest** — DPAPI on Windows, AES-256/GCM on macOS/Linux, the Android Keystore on Android.

---

## Advantages over other apps

Compared to the official Meshtastic clients (web client, Android/Apple apps) and CLI:

- **Truly cross-platform, one app.** Native Windows, macOS, **Linux**, and Android from a single
  codebase — including a first-class **Linux desktop** experience that the official clients don't offer.
- **No Python / CLI dependency.** The radio protocol is implemented natively in C#; there's nothing to
  `pip install` and no external bridge process — just run the app.
- **Desktop-class tactical console.** A dense, information-rich dark UI (chat + nodes + map + signal +
  config + console) designed for operators, with five themes and English/Russian.
- **Built-in telemetry simulation.** Static/offset/random substitution with hot-reload and optional
  mesh broadcast — a QA/field-testing capability not present in the standard apps.
- **Offline maps.** Download and cache world tiles for use with no internet in the field.
- **Local mesh + global MQTT in one map.** See both what your radio hears and what the wider network
  reports.
- **Private encrypted direct chat** on a dedicated PSK channel, with per-contact history.
- **Secrets encrypted at rest** on every platform (DPAPI / AES-GCM / Android Keystore).

---

## Architecture

- **`LoRaChat.Core`** — a platform-agnostic library: the native Meshtastic protocol (protobuf framing,
  serial + BLE), the services (settings, node DB, MQTT, telemetry, tiles), and a **UI bridge** that
  speaks a JSON message contract to the web UI. No UI or platform APIs.
- **`LoRaChat.App`** — an [Uno Platform](https://platform.uno) single project that hosts the HTML/JS UI
  in a WebView and provides the per-platform adapters (serial, BLE, secret storage, dialogs).
- **UI** — a self-contained HTML/CSS/JS single-page app rendered identically on every platform.

See **[BUILD.md](BUILD.md)** for the full project layout and build details.

---

## Roadmap

- **Over-the-air device config sync** — read the full `Config`/`ModuleConfig` from the radio into the
  Settings form and write the whole configuration back (currently the app stores settings locally and
  sends per-node admin commands; bulk config push/pull is not yet wired).
- **Desktop Bluetooth LE** — the BLE protocol stack is complete and used on Android; a desktop GATT
  transport is a follow-up (desktop currently uses USB serial).
- **Android USB-OTG for CP210x/CH340/FTDI** chips via a `usb-serial-for-android` binding (native-USB
  CDC-ACM radios already work).

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| No serial ports listed (Linux) | Add yourself to the `dialout` group and re-login. |
| Blank window on Linux | Install **WebKitGTK** (`libwebkit2gtk-4.1`). |
| BLE won't scan (Android) | Grant the Bluetooth permission prompt on first launch. |
| Android build error `APT2265` | Build from a path with only ASCII characters. |
| Want to explore without a radio | Launch with `LORACHAT_DEMO=1`. |

More per-OS notes are in **[BUILD.md](BUILD.md)**.

---

## Credits & license

- Built for the [Meshtastic](https://meshtastic.org) ecosystem; not an official Meshtastic project.
- UI hosted with [Uno Platform](https://platform.uno); maps by [Leaflet](https://leafletjs.com) /
  [OpenStreetMap](https://www.openstreetmap.org).

Released under the [MIT License](LICENSE).
