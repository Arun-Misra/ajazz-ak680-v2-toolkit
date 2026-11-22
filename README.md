# AJAZZ AK680 V2 Toolkit

Unofficial control software for the **AJAZZ AK680 V2** Hall-effect keyboard —
RGB lighting, macros, custom animations, and analog gamepad input in one native
Windows app.

**Repo / package name:** `ajazz-ak680-v2-toolkit`  
**App name:** `AK680 V2 Toolkit`

Built with **WPF / .NET 10**. HID access is implemented directly against the
Windows API (SetupAPI, `CreateFile`, `ReadFile`, `WriteFile`, HID capabilities) —
no third-party HID library.

---

## What it can do

### Lighting
- 20 built-in effects (1–19 plus Custom).
- Per-key RGB painting with an on-screen keyboard; click a key, pick a colour,
  and it lights up on the hardware immediately.
- Live update mode so colour changes appear as you drag them.
- Adjustable brightness, speed, direction, and colour mode.
- Optional raw mode byte for experimenting with undocumented effect values.

### Custom animations
A software animation engine generates frames on the PC and streams them to the
keyboard. It runs on a dedicated thread at a configurable frame rate with a
speed multiplier.

- **20 patterns**: Rainbow Wave, Breathing, Chase, Ripple, Sparkle, Matrix Rain,
  Fire, Checkerboard, Kitt Scanner, Aurora, Color Fade, Click Reactive, Plasma,
  Diagonal Wave, Spiral, Meteor, Color Wipe, Breathing Rainbow, Equalizer,
  Typing Trail.
- **17 click effects**: Ripple, Flash, Spread, Sparkle Burst, Prism Bloom,
  Liquid Bloom, Nova, Comet, Thunder, Pulse, Wave, Cross, Echo, Rainbow,
  Full Flash, Heartbeat, Spiral.
- Click effects fire from a **global keyboard hook** (physical keys) or by
  clicking keys on the on-screen preview.
- Frames are sent through the keyboard's firmware-rendered LED path, so the
  animation flows continuously instead of stepping. Leave the checkbox on the
  Animations page enabled for normal use.

### Hall-effect features
- Adjustable actuation point / trigger travel, dead zone, axis type, and other
  performance settings.
- **Hall Gamepad**: uses each key's physical travel (0–3.4 mm) to drive a virtual
  Xbox 360 controller. W/A/S/D map to the left stick with analog magnitude based
  on how far the key is pressed. Includes a guided full-travel calibration.
  Requires the ViGEmBus driver.

### Configuration
- **Custom Keys**: remap any key to a character or function.
- **Shortcut Command**: create and store macros; import/export them.
- **Advanced Keys**: RS, SOCD, DKS, MT and TGL per-key behaviours.
- **Performance / Settings**: polling rate (1K/4K/8K), game mode, sleep, key
  delay, NKRO, stability mode, auto calibration, single-key wakeup.
- **Profiles**: create, rename, copy, export and switch onboard profiles.

### Diagnostics
- **Device Inspector**: reads every block the keyboard reports — device info,
  game/system settings, LED state, key bindings, custom colours, trigger points,
  rapid-trigger settings, DKS slots and macro offsets — with raw reply dumps and
  a "Save report" button.
- **Key Map Debug**: shows every key's label and LED id; clicking one lights only
  that LED, useful for verifying the physical key map.

---

## Requirements

- Windows 10 or 11
- .NET 10 runtime (or the SDK to build from source)
- AJAZZ AK680 V2 connected over USB
- Optional: **ViGEmBus** for the Hall Gamepad feature

Close the AJAZZ web driver / browser tab before using the app — Windows only
allows one program to own the device at a time.

---

## Build and run

```powershell
cd AK680V2Toolkit
dotnet build
dotnet run
```

Or launch the built executable:

```
AK680V2Toolkit\bin\Debug\net10.0-windows\AK680V2Toolkit.exe
```

The app connects automatically on startup and shows the connected keyboard in
the sidebar. If the device is unplugged or another app has it open, use
**Refresh device** after freeing it.

---

## Project layout

| Area | Files |
|---|---|
| App shell / navigation | `App.xaml.cs`, `MainWindow.xaml(.cs)` |
| HID transport and commands | `NativeHid.cs`, `DeviceService.cs`, `Protocol.cs`, `Commands.cs` |
| Animation engine | `AnimationEngine.cs`, `AnimationsPage.xaml(.cs)` |
| Hall telemetry and gamepad | `HallStream.cs`, `VirtualGamepad.cs`, `GamepadPage.xaml(.cs)` |
| Configuration pages | `CustomKeysPage`, `LightingPage`, `MacroPage`, `PerformancePage`, `AdvancedKeysPage`, `SettingsPage`, `ProfilesPage` |
| Diagnostics | `InspectorPage.xaml(.cs)`, `DebugPage.xaml(.cs)` |
| Keyboard hook / key map | `KeyboardHook.cs`, `KeyMap.cs` |

Author: Arun-Misra
