# AJAZZ AK680 V2 Toolkit — custom RGB animations + analog gamepad control

**Short answer:** AJAZZ AK680 V2 Toolkit is an unofficial Windows app for the **AJAZZ AK680 V2** Hall-effect keyboard. Its two headline features are **host-generated custom RGB animations with 17 click-reactive effects** and **analog gamepad emulation driven by real magnetic key travel**. It also replaces the AJAZZ web driver for lighting, macros, key remapping, performance settings, profiles, and diagnostics.

- **App name:** AK680 V2 Toolkit
- **Repository / package name:** `ajazz-ak680-v2-toolkit`
- **Supported device:** AJAZZ AK680 V2 only
- **Platform:** Windows 10/11, WPF, .NET 10
- **Official status:** Unofficial. Not affiliated with AJAZZ.

---

## Headline feature 1: custom RGB animations for AK680 V2

The official web driver gives you a fixed set of lighting effects. This app adds a full **custom animation engine** that generates frames on the PC and streams them to the keyboard through its firmware-rendered LED path, so motion stays smooth instead of stepping.

- **20 animation patterns**: Rainbow Wave, Breathing, Chase, Ripple, Sparkle, Matrix Rain, Fire, Checkerboard, Kitt Scanner, Aurora, Color Fade, Click Reactive, Plasma, Diagonal Wave, Spiral, Meteor, Color Wipe, Breathing Rainbow, Equalizer, and Typing Trail
- **17 click effects**: Ripple, Flash, Spread, Sparkle Burst, Prism Bloom, Liquid Bloom, Nova, Comet, Thunder, Pulse, Wave, Cross, Echo, Rainbow, Full Flash, Heartbeat, and Spiral
- **Thunder** fires jagged bolts from all four keyboard edges into the pressed key
- **Prism Bloom**, **Liquid Bloom**, **Nova**, and **Comet** add richer one-shot lighting than the stock driver
- Adjustable **frame rate** and **animation speed**
- Physical-key detection through a global Windows keyboard hook
- On-screen keyboard preview for firing effects with the mouse
- Smooth firmware output path for continuous animation

If you are searching for **AK680 V2 custom animations**, **AK680 V2 RGB software**, or **AK680 V2 lighting effects**, this is the core feature of the app.

---

## Headline feature 2: Hall-effect analog gamepad emulation

The AJAZZ AK680 V2 uses magnetic switches, so each key has a measurable travel depth. The **Hall Gamepad** feature samples that travel and turns the keyboard into a virtual Xbox 360 controller.

- **W/A/S/D map to the left analog stick**
- Stick intensity follows **real key depth from 0–3.4 mm**, not just on/off key presses
- Gentle presses produce small analog values; deeper presses push the stick further
- Guided **full-travel calibration** for each mapped key
- Uses the keyboard's 1 kHz Hall telemetry stream
- Requires the **ViGEmBus** driver for virtual controller output
- Useful for games that support analog controller input

This is the feature that makes the AK680 V2's Hall-effect hardware useful beyond normal keyboard input: **AK680 V2 analog gamepad**, **AK680 V2 Hall effect controller**, and **AK680 V2 gamepad emulation**.

---

## What else the app does

### AJAZZ AK680 V2 RGB lighting control

- 20 built-in lighting effects: modes 1–19 plus Custom (128)
- Per-key RGB colour picker with an on-screen keyboard
- Adjustable brightness, speed, direction, and colour mode
- Live update so changes appear immediately
- Optional raw mode value for testing undocumented effect bytes

### Hall-effect and performance settings

- Actuation point and trigger travel
- Dead zone and axis / switch type settings
- Polling rate options: 1000 Hz, 4000 Hz, and 8000 Hz
- Game mode, sleep time, key delay, NKRO, stability mode, auto calibration, and single-key wakeup

### Key remapping, macros, and advanced keys

- **Custom Keys:** remap keys to characters or functions
- **Shortcut Command:** create, store, import, and export macros
- **Advanced Keys:** RS, SOCD, DKS, MT, and TGL behaviours
- **Profiles:** create, rename, copy, export, and switch keyboard profiles

### Device Inspector and diagnostics

- Decoded device info, firmware version, game/system settings, LED effect state, key bindings, custom colours, trigger points, Rapid Trigger settings, DKS slots, and macro offsets
- Raw reply dumps and a saved text report
- **Key Map Debug** page shows each key's label and LED id and can light a single LED for mapping verification

---

## Why use this instead of the AJAZZ web driver?

If you are searching for **AJAZZ AK680 V2 driver software**, **AK680 V2 control software**, or an **AK680 V2 web driver alternative**, this app provides:

- Smooth custom RGB animations and click effects
- Analog gamepad emulation from Hall-effect travel
- Native Windows UI instead of a browser page
- Direct Windows HID access, with no browser WebHID picker
- Device Inspector and full diagnostic readback
- Local macro/profile management and import/export

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

```powershell
dotnet build
dotnet run
```

Or launch the built executable directly:

```text
bin\Debug\net10.0-windows\AK680V2Toolkit.exe
```

The app connects automatically at startup and shows the connected keyboard in the sidebar. If the keyboard is disconnected or still held by another app, click **Refresh device** after freeing it.

---

## Project structure

| Area | Main files |
|---|---|
| Application shell | `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs` |
| HID transport and protocol | `NativeHid.cs`, `DeviceService.cs`, `Protocol.cs`, `Commands.cs` |
| Lighting UI | `LightingPage.xaml`, `LightingPage.xaml.cs` |
| **Custom animation engine** | `AnimationEngine.cs`, `AnimationsPage.xaml`, `AnimationsPage.xaml.cs` |
| **Hall telemetry and gamepad** | `HallStream.cs`, `VirtualGamepad.cs`, `GamepadPage.xaml`, `GamepadPage.xaml.cs` |
| Device configuration | `CustomKeysPage`, `MacroPage`, `PerformancePage`, `AdvancedKeysPage`, `SettingsPage`, `ProfilesPage` |
| Diagnostics | `InspectorPage.xaml`, `InspectorPage.xaml.cs`, `DebugPage.xaml`, `DebugPage.xaml.cs` |
| Keyboard mapping | `KeyboardHook.cs`, `KeyMap.cs`, `KeyboardView.xaml` |

---

## FAQ

### Is this an official AJAZZ app?

No. AJAZZ AK680 V2 Toolkit is an independent desktop project and is not affiliated with AJAZZ.

### Does it work with other AJAZZ keyboards?

No. It is built specifically for the **AJAZZ AK680 V2**. The key map, LED ids, and commands are model-specific.

### What makes the custom animations different from built-in lighting modes?

Built-in modes are fixed firmware effects. The custom animation engine generates its own frames on the PC, supports click-reactive effects, and can be tuned with frame rate and speed controls.

### How does analog gamepad input work?

The AK680 V2's magnetic switches report how far each key is pressed. The app samples that travel, calibrates rest and bottom-out values, and maps W/A/S/D to analog stick axes through a ViGEmBus virtual Xbox controller.

### Can it replace the AJAZZ web driver?

For most desktop use, yes. Lighting, key remapping, macros, advanced keys, settings, and profiles are available in the app. Custom animations and analog gamepad input are extra features beyond the official web driver.

### Does it need internet access?

No. The app talks to the keyboard over USB. Internet access is only needed if you build from source and restore NuGet packages.

### Why does the virtual gamepad need ViGEmBus?

Windows cannot create an Xbox controller input device from a normal user application. ViGEmBus provides the virtual controller driver that the app writes to.

### Why should I close the browser driver first?

Because both the browser WebHID driver and this app try to open the same HID interface. Closing the browser tab releases the keyboard.

## Web-driver feature parity

- Custom keys: key remapping, FN-layer remapping, reset
- Lighting: 20 built-in effects, per-key colours, colour mode/brightness/speed, native system-audio Music Rhythm
- Shortcut command / macros: create, edit, save to keyboard, real JSON import/export
- Performance: mode presets, per-key trigger distance, selectable axes, Fast Trigger, calibration, keyboard settings
- Advanced keys: RS, SOCD, DKS, MT and TGL configuration and device writes
- Profiles: create/delete/import/export, apply onboard slot, save keyboard state to profile
- Key Test: live per-key Hall travel in millimetres
- Device Inspector: decoded device state and raw replies

Author: Arun-Misra
