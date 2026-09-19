# USB Control

A visual USB port manager for Windows, built for gamers with a drawer full of controllers.
Think "Fan Control, but for USB": every physical port on your machine becomes a labeled tile
you can rename, describe with a note and a photo, and switch on or off with one click —
without reaching behind the case or unplugging anything.

## Why

- Physically replugging devices wears out connectors and your patience.
- The ports you actually use are the ones hardest to reach behind the case.
- Devices you don't need (flight stick, leverless, second pad…) can stay plugged in but disabled.
- Device Manager is a chore: generic names, no port map, no pictures. This fixes all three.
- Bonus: applying a profile enables controllers **in order**, which gives you a deterministic
  controller order for couch co-op without replugging or restarting.

## Features

- **Gaming-HUD design** — deep-charcoal cockpit UI with five switchable neon accent palettes
  (Cyan, Violet, Toxic, Blood, Amber — switch live in Settings), animated hover glows, and
  HUD-style status chips with LED dots (online / off / issue). Every tile shows a device-type
  icon (controller, keyboard, mouse, audio, hub, storage) or your photo, with a spinning status
  indicator while the bus is busy.
- **Live port map** — walks the real USB tree (controllers → root hubs → downstream hubs → ports)
  using the same user-mode hub IOCTLs as Microsoft's USBView sample. Every physical port is a tile,
  grouped by hub, refreshed automatically when devices arrive/leave.
- **One-click enable / disable** — uses CfgMgr32 (`CM_Enable_DevNode` / `CM_Disable_DevNode` with
  `CM_DISABLE_PERSISTENT`), the exact mechanism Device Manager uses, so a disabled device stays
  disabled across reboots and replugs until you re-enable it.
- **Names, notes, photos** — rename any device ("Leverless", "HOTAS throttle") and any port
  ("Rear bottom-left"), attach a picture. Metadata is keyed by VID/PID + serial, so your labels
  re-attach automatically when the device comes back — even on a different port.
- **Profiles** — capture the current state as "Flight sim" or "Co-op — 4 pads", then apply it later:
  everything in the profile is enabled in profile order, everything else is disabled.
- **Controllers-first scope** — by default shows hubs and HID/game devices, hiding storage/network
  noise. "Show all devices" flips to the full tree.
- **Show / hide ports** — per-port hide, plus a global "show hidden" toggle and "show empty ports".
- **Panel layout** — toggle "Panel layout" in the toolbar to switch to a free-form canvas: drag
  tiles to mirror the physical arrangement of your case's rear panel, with grid snapping and an
  auto-arranged starting point. Positions persist in `store.json` (per port, not per device), and
  "Reset panel" returns everything to the auto layout.
- **System tray** — minimizing or closing the window keeps USB Control running in the tray with a
  live status tooltip; the tray context menu lists your profiles for one-click switching (each
  apply pops a confirmation balloon), plus quick Refresh and a real Exit. Disable "minimize &
  close to tray" in Settings to restore normal window behavior.

## Running

`publish/USBControl.exe` is a self-contained single-file build (net8.0-windows, win-x64).

The app **runs elevated** (like Fan Control) because enabling/disabling devices requires the same
rights as Device Manager — that's why you get a UAC prompt.

Data lives in `%APPDATA%\USBControl\`:
- `store.json` — device metadata, port labels, profiles, settings
- `Photos\` — imported device pictures

Command line: `USBControl.exe --minimized` starts hidden in the tray (used by the
"Start with Windows" option).

## Building

Requires the .NET 8 SDK (a user-local install works fine):

```bash
dotnet build USBControl.sln
dotnet test tests/USBControl.Tests/USBControl.Tests.csproj
dotnet publish src/USBControl.App/USBControl.App.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Testing without hardware

`USBControl.Testing` is a fake-hub harness that simulates a USB bus so the enumeration → merge →
identity-matching → profile pipeline can be integration-tested with no hardware plugged in:

- **`FakeUsbBus`** — named hubs with numbered ports; plug/unplug/move devices (Windows-format
  instance ids: serial devices keep their identity across ports, serial-less ones get fresh
  location tails, exactly like the real bus); Device-Manager-style **persistent disable** that
  survives unplug/replug and a simulated `Reboot()`.
- **`FakeTopologyService` / `FakeDevicePowerService`** — `ITopologyService`/`IDevicePowerService`
  implementations over the bus, with a full call log and per-device failure injection
  ("devnode busy") for error-path tests.
- **`TestingRig`** — one object wiring bus + fakes + a temp-dir store + the **real**
  `AppController`, so tests drive the actual application code including hot-plug refreshes.

The integration suite (`FakeBusIntegrationTests`) covers: identity re-attachment after replug
(serial and serial-less), disable persistence across unplug/replug/reboot, ordered profile
enables with idempotent apply, partial-failure reporting, and hot-plug watcher refreshes.

Run everything with:

```bash
dotnet test tests/USBControl.Tests/USBControl.Tests.csproj
```

## How it works (the short version)

- `USBControl.Hardware` enumerates hub interfaces, opens them, and asks every port for its
  connection info (`IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX`), instance id
  (`IOCTL_USB_GET_NODE_CONNECTION_DRIVERKEY_NAME`) and product string. Each port's device is
  correlated to its PnP devnode and enriched with friendly name/class/children, including the
  XINPUT child that carries the name you actually recognize.
- Device changes arrive via a background message-only window receiving `WM_DEVICECHANGE`
  (debounced), so the map stays live without polling.
- `USBControl.Core` holds the models, the identity/matching logic (re-attaching your names to
  replugged devices), the JSON store, and the profile engine.
- `USBControl.App` is the WPF UI (dark theme): port tiles, editor panel, profiles, settings.

## Roadmap ideas

- Webcam snapshot as the device photo
- Per-profile controller-order preview (show which pad becomes Player 1)
- Storage devices support with safe-eject-style actions
