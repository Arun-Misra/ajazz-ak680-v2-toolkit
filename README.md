# AJAZZ AK680 V2 Toolkit — unofficial Windows driver software

**Short answer:** AJAZZ AK680 V2 Toolkit is an unofficial desktop control app for the **AJAZZ AK680 V2** 68-key Hall-effect keyboard. It replaces the AJAZZ web driver with a native Windows app for **RGB lighting, per-key colours, custom animations, macros, key remapping, performance settings, profiles, device diagnostics, and analog gamepad input**.

- **App name:** AK680 V2 Toolkit
- **Repository / package name:** `ajazz-ak680-v2-toolkit`
- **Supported device:** AJAZZ AK680 V2 only
- **Platform:** Windows 10/11, WPF, .NET 10
- **Official status:** Unofficial. Not affiliated with AJAZZ.

---

## What is AJAZZ AK680 V2 Toolkit?

AJAZZ AK680 V2 Toolkit is a native Windows application built specifically for the **AJAZZ AK680 V2 Hall-effect keyboard**. It gives you desktop control over the features normally found in the AJAZZ web driver, plus extra tools the web driver does not provide, such as host-generated custom animations, a Device Inspector, and analog gamepad emulation using the keyboard's magnetic switch travel.

This project is **only for the AJAZZ AK680 V2**. It is not for AK680 V1, AK680 Pro, AK820, or other AJAZZ keyboard models.

---

## Why use this instead of the AJAZZ web driver?

If you are searching for **AJAZZ AK680 V2 driver software**, **AK680 V2 control software**, or an **AK680 V2 web driver alternative**, this app provides:

- A native Windows UI instead of a browser page
- Direct Windows HID access, with no browser WebHID picker
- Smooth custom RGB animations rendered through the keyboard's firmware LED path
- Click-reactive effects, including Ripple, Thunder, Prism Bloom, Nova, and Comet
- Analog gamepad output based on real Hall-effect key travel
- Full device readback for diagnostics and troubleshooting
- Local macro/profile management and import/export

---

## Main features

### AJAZZ AK680 V2 RGB lighting control

- 20 built-in lighting effects: modes 1–19 plus Custom (128)
- Per-key RGB colour picker with an on-screen keyboard
- Adjustable brightness, speed, direction, and colour mode
- Live update so changes appear immediately on the keyboard
- Optional raw mode value for testing undocumented effect bytes

### AK680 V2 custom animations

The animation engine generates frames on the PC and streams them to the keyboard, allowing effects beyond the built-in firmware modes.

- 20 animation patterns: Rainbow Wave, Breathing, Chase, Ripple, Sparkle, Matrix Rain, Fire, Checkerboard, Kitt Scanner, Aurora, Color Fade, Click Reactive, Plasma, Diagonal Wave, Spiral, Meteor, Color Wipe, Breathing Rainbow, Equalizer, and Typing Trail
- Adjustable frame rate and animation speed
- 17 click effects: Ripple, Flash, Spread, Sparkle Burst, Prism Bloom, Liquid Bloom, Nova, Comet, Thunder, Pulse, Wave, Cross, Echo, Rainbow, Full Flash, Heartbeat, and Spiral
- Physical-key detection through a global Windows keyboard hook
- On-screen keyboard preview for firing effects with the mouse
- Smooth firmware-rendered output path for continuous animation

### Hall-effect and performance settings

The AK680 V2 uses magnetic switches. This app exposes Hall-effect configuration such as:

- Actuation point and trigger travel
- Dead zone
- Axis / switch type settings
- Polling rate options: 1000 Hz, 4000 Hz, and 8000 Hz
- Game mode, sleep time, key delay, NKRO, stability mode, auto calibration, and single-key wakeup

### AK680 V2 analog gamepad emulation

The **Hall Gamepad** feature reads each key's physical travel from the keyboard's Hall sensors and maps it to a virtual Xbox 360 controller.

- W/A/S/D map to the left analog stick
- Stick intensity follows real key depth from 0–3.4 mm
- Guided full-travel calibration per key
- Requires the ViGEmBus virtual gamepad driver
- Useful for games that support analog controller input

### Key remapping, macros, and advanced keys

- **Custom Keys:** remap keys to characters or functions
- **Shortcut Command:** create, store, import, and export macros
- **Advanced Keys:** RS, SOCD, DKS, MT, and TGL behaviours
- **Profiles:** create, rename, copy, export, and switch keyboard profiles

### Device Inspector and diagnostics

The Device Inspector reads all supported state blocks from the keyboard and decodes them into readable tables:

- Device information and firmware version
- Game/system settings
- LED effect state
- Key bindings
- Custom LED colours
- Trigger points
- Rapid Trigger settings
- DKS slots
- Macro offsets
- Raw reply dumps and a saved text report

The **Key Map Debug** page shows each key's label and LED id and can light a single LED for mapping verification.

---

## System requirements

- **Keyboard:** AJAZZ AK680 V2 only
- **OS:** Windows 10 or Windows 11
- **Runtime:** .NET 10 (included with the SDK if building from source)
- **Connection:** USB
- **Optional:** ViGEmBus driver for the Hall Gamepad feature

Close the AJAZZ web driver or browser tab before launching the app. Windows generally allows only one application to control the keyboard at a time.

---

## Build and run

From the project directory:

```powershell
dotnet build
dotnet run
```

Or launch the built executable directly:

```text
bin\Debug\net10.0-windows\AK680V2Toolkit.exe
```

The app connects automatically at startup and shows the connected device in the sidebar. If the keyboard is disconnected or still held by another app, click **Refresh device** after freeing it.

---

## Project structure

| Area | Main files |
|---|---|
| Application shell | `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs` |
| HID transport and protocol | `NativeHid.cs`, `DeviceService.cs`, `Protocol.cs`, `Commands.cs` |
| Lighting UI | `LightingPage.xaml`, `LightingPage.xaml.cs` |
| Animation engine | `AnimationEngine.cs`, `AnimationsPage.xaml`, `AnimationsPage.xaml.cs` |
| Hall telemetry and gamepad | `HallStream.cs`, `VirtualGamepad.cs`, `GamepadPage.xaml`, `GamepadPage.xaml.cs` |
| Device configuration | `CustomKeysPage`, `MacroPage`, `PerformancePage`, `AdvancedKeysPage`, `SettingsPage`, `ProfilesPage` |
| Diagnostics | `InspectorPage.xaml`, `InspectorPage.xaml.cs`, `DebugPage.xaml`, `DebugPage.xaml.cs` |
| Keyboard mapping | `KeyboardHook.cs`, `KeyMap.cs`, `KeyboardView.xaml` |

---

## FAQ

### Is this an official AJAZZ app?

No. AJAZZ AK680 V2 Toolkit is an independent desktop project and is not affiliated with AJAZZ.

### Does it work with other AJAZZ keyboards?

No. It is built specifically for the **AJAZZ AK680 V2**. The key map, LED ids, and commands are model-specific.

### Can it replace the AJAZZ web driver?

For most desktop use, yes: lighting, key remapping, macros, advanced keys, settings, and profiles are available in the app. The Hall Gamepad and custom animation features are additional tools not provided by the official web driver.

### Does it need internet access?

No. The app talks to the keyboard over USB. Internet access is only needed if you build from source and restore NuGet packages.

### Why does the virtual gamepad need ViGEmBus?

Windows cannot create an Xbox controller input device from a normal user application. ViGEmBus provides the virtual controller driver that the app writes to.

### Why should I close the browser driver first?

Because both the browser WebHID driver and this app try to open the same HID interface. Closing the browser tab releases the keyboard.

---

## Keywords

AJAZZ AK680 V2, AK680 V2, AK680 V2 driver, AJAZZ AK680 V2 driver, AK680 V2 software, AK680 V2 control software, AK680 V2 web driver alternative, AK680 V2 RGB, AK680 V2 lighting software, AK680 V2 Hall effect, AK680 V2 magnetic switch, AK680 V2 macro software, AK680 V2 key remap, AK680 V2 custom animations, AK680 V2 gamepad, AK680 V2 analog input, AK680 V2 profile manager, AK680 V2 device inspector.

---

Author: Arun-Misra
