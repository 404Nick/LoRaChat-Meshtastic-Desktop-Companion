# Building LoRaChat — complete per-OS instructions

LoRaChat is one [Uno Platform](https://platform.uno) app that builds for **Windows, macOS, Linux, and
Android** from a single codebase. Two target frameworks cover all four:

| TFM | Produces | Runs on |
|-----|----------|---------|
| `net10.0-desktop` | Skia desktop app | Windows, macOS, Linux |
| `net10.0-android` | signed `.apk` | Android |

```
LoRaChat.slnx
├── LoRaChat.Core/     platform-agnostic backend (protocol, services, UI bridge)
└── LoRaChat.App/      Uno single project (the two heads above)
```

Run the commands below **from the repository root** (the folder containing `LoRaChat.slnx`). Substitute
your Android SDK path where shown.

---

## 0. Common prerequisites (every OS)

1. **.NET 10 SDK** ≥ `10.0.301` — https://dotnet.microsoft.com/download/dotnet/10.0
   Verify: `dotnet --version`
2. **Uno Platform tooling** (one-time). The Uno.Sdk version is pinned in `global.json`, so no manual SDK
   install is needed, but the environment checker installs any missing native bits:
   ```
   dotnet new install Uno.Templates
   dotnet tool install -g uno.check
   uno-check                     # follow its prompts; fixes missing workloads/deps
   ```
3. Restore happens automatically on first build, or force it: `dotnet restore LoRaChat.slnx`.

> **Folder name must be ASCII for Android.** The Android packager (`aapt2`) rejects non-ASCII paths
> (error `APT2265`). This repo is already named `LoRaChat-Meshtastic-Desktop-Companion` (ASCII). Desktop
> builds are unaffected either way.

---

## 1. Windows

### Prerequisites
- .NET 10 SDK.
- **Edge WebView2 Runtime** — preinstalled on Windows 10/11. (If missing:
  https://developer.microsoft.com/microsoft-edge/webview2/)

### Build & run
```
dotnet build   LoRaChat.App/LoRaChat.App.csproj -f net10.0-desktop -c Release
dotnet run --project LoRaChat.App/LoRaChat.App.csproj -f net10.0-desktop -c Release
```

### Produce a distributable
Framework-dependent (target needs the .NET 10 runtime):
```
dotnet publish LoRaChat.App/LoRaChat.App.csproj -f net10.0-desktop -c Release
# -> LoRaChat.App\bin\Release\net10.0-desktop\publish\LoRaChat.exe  (+ WebAssets\, DLLs)
```
Self-contained (no runtime needed on target):
```
dotnet publish LoRaChat.App/LoRaChat.App.csproj -f net10.0-desktop -c Release -r win-x64 --self-contained
# -> ...\bin\Release\net10.0-desktop\win-x64\publish\
```

### Connect a radio
Plug in over USB, pick the **COM** port in the bottom bar, press **Listen**. Demo mode without a radio:
set `LORACHAT_DEMO=1` before launching.

---

## 2. macOS

### Prerequisites
- .NET 10 SDK (`pkg` installer, or `brew install --cask dotnet-sdk`; use the arm64 SDK on Apple Silicon).
- **Xcode Command Line Tools**: `xcode-select --install`.
- WebView (WKWebView) is built into macOS — nothing to install.

### Build & run
```
dotnet build   LoRaChat.App/LoRaChat.App.csproj -f net10.0-desktop -c Release
dotnet run --project LoRaChat.App/LoRaChat.App.csproj -f net10.0-desktop -c Release
```

### Produce a distributable
```
# Apple Silicon:
dotnet publish LoRaChat.App/LoRaChat.App.csproj -f net10.0-desktop -c Release -r osx-arm64 --self-contained
# Intel:
dotnet publish LoRaChat.App/LoRaChat.App.csproj -f net10.0-desktop -c Release -r osx-x64  --self-contained
# -> ...\bin\Release\net10.0-desktop\<rid>\publish\   (run the produced executable)
```
To ship a double-clickable `.app`, wrap the publish output with Uno's macOS packaging
(https://aka.platform.uno/skia-macos); for personal use the published executable runs directly.

### Connect a radio
Serial devices appear as `/dev/cu.usbserial-*` or `/dev/cu.usbmodem*`; the app lists these. macOS may
prompt to allow the accessory the first time.

---

## 3. Linux

### Prerequisites
- .NET 10 SDK (distro package, Snap, or the official install script).
- **WebKitGTK** — required, or the WebView won't render:
  - Debian/Ubuntu: `sudo apt install libwebkit2gtk-4.1-0 libgtk-3-0`
  - Fedora: `sudo dnf install webkit2gtk4.1 gtk3`
  - Arch: `sudo pacman -S webkit2gtk-4.1 gtk3`
- Serial access: `sudo usermod -aG dialout $USER` (then log out/in).

### Build & run
```
dotnet build   LoRaChat.App/LoRaChat.App.csproj -f net10.0-desktop -c Release
dotnet run --project LoRaChat.App/LoRaChat.App.csproj -f net10.0-desktop -c Release
```

### Produce a distributable
```
dotnet publish LoRaChat.App/LoRaChat.App.csproj -f net10.0-desktop -c Release -r linux-x64 --self-contained
# -> ...\bin\Release\net10.0-desktop\linux-x64\publish\LoRaChat   (chmod +x if needed)
```
The target machine still needs WebKitGTK/GTK3 (system libraries, not bundled).

### Connect a radio
Devices appear as `/dev/ttyUSB0`, `/dev/ttyACM0`, … If the list is empty, confirm the device shows under
`/dev` and that you're in the `dialout` group.

---

## 4. Android

### Prerequisites
- .NET 10 SDK.
- **Android workload**: `dotnet workload install android`
- **JDK 17** (Microsoft OpenJDK recommended; `uno-check` installs one if missing).
- **Android SDK** — if absent, install into a chosen folder:
  ```
  dotnet build LoRaChat.App/LoRaChat.App.csproj -f net10.0-android \
    -t:InstallAndroidDependencies -p:AcceptAndroidSDKLicenses=true \
    -p:AndroidSdkDirectory="<SDK_DIR>"
  ```
  (On this machine it's at `C:\Users\user\AppData\Local\Android\Sdk`.)
- Set `ANDROID_HOME=<SDK_DIR>`, or pass `-p:AndroidSdkDirectory=<SDK_DIR>` on every build.

### Build the APK
```
dotnet build LoRaChat.App/LoRaChat.App.csproj -f net10.0-android -c Release \
  -p:AndroidSdkDirectory="<SDK_DIR>"
# -> LoRaChat.App\bin\Release\net10.0-android\com.lorachat.companion-Signed.apk   (debug-signed)
```
Google Play bundle (`.aab`) with a real release key:
```
dotnet publish LoRaChat.App/LoRaChat.App.csproj -f net10.0-android -c Release \
  -p:AndroidPackageFormat=aab \
  -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=<your.keystore> \
  -p:AndroidSigningStorePass=<pass> -p:AndroidSigningKeyAlias=<alias> -p:AndroidSigningKeyPass=<pass>
```

### Install & run
```
adb install -r LoRaChat.App/bin/Release/net10.0-android/com.lorachat.companion-Signed.apk
```
Or copy the `.apk` to the phone and sideload it. It requests Bluetooth permissions on first launch.

### Connect a radio
- **BLE (primary):** tap **Scan BLE**, choose the radio, press Listen.
- **USB-OTG:** works for native-USB (CDC-ACM) radios; CP210x/CH340/FTDI chips need a
  `usb-serial-for-android` binding — prefer BLE on those.

---

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `APT2265` on Android build | Non-ASCII char in the project path — build from an ASCII path. |
| `XA5300 … Android SDK not found` | Pass `-p:AndroidSdkDirectory=<SDK_DIR>` or set `ANDROID_HOME`. |
| Blank window on Linux | WebKitGTK not installed (see Linux prerequisites). |
| Empty serial port list (Linux/macOS) | Device not under `/dev`, or not in the `dialout` group (Linux). |
| WebView doesn't load on macOS/Linux | The app auto-falls back from virtual-host mapping to `file://`; check the console log. |
| Demo without a radio | Set `LORACHAT_DEMO=1`. |

## Verifying a build without hardware

- Desktop: launch with `LORACHAT_DEMO=1` — node list, chat, map, signal and telemetry-sim populate from a
  fake backend.
- Protocol/logic: the harnesses under `scratchpad/prototest` (serial/BLE framing + admin commands) and
  `scratchpad/uitest` (UiBridge message flows) run with `dotnet run` and need no device.
