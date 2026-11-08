# ProfileHop

A tiny Windows tray app that switches a Logitech **PRO X2 SUPERSTRIKE** between its onboard profiles with a global
hotkey — DPI and HITS click settings change together, because both live in the profile on the mouse. No G HUB
running. It sends one HID++ 2.0 command (OnboardProfiles `0x8100` setCurrentProfile) and never writes mouse memory.

Native AOT, ~3 MB exe, a few MB of RAM, 0% CPU at idle.

## Setup

1. In G HUB, turn on **On-board memory mode**, set up the on-board slots (DPI + HITS per slot), then quit G HUB
   completely (it puts the mouse into host mode while running).
2. Download `ProfileHop-win-x64.zip` from [Releases](../../releases), unzip anywhere, run `profilehop.exe`.
3. Right-click the tray icon → **Start with Windows** if you want it at login.

## Hotkeys

Set them in tray menu → **Hotkeys…** (click a field, press the key; Backspace clears, Esc cancels). Bare F13–F24
work, handy for QMK keys.

| Action | Default |
| --- | --- |
| Next profile | Ctrl+Alt+F9 |
| Previous profile | Ctrl+Alt+F8 |
| Profile 1–5 | Ctrl+Alt+1 … Ctrl+Alt+5 |
| Next DPI slot (inside the profile) | off |

Profile names come from the mouse (the names G HUB stored). Tray menu → **Device** picks the mouse if you have
more than one Logitech receiver.

Each profile hotkey shows a Windows notification with the mouse, the profile and its DPI
(`switchNotification: false` turns it off).

## Per-app profiles

ProfileHop remembers the profile you last chose in each app (by hotkey or from the tray menu) and switches to it the
moment that app comes to the front. Apps you never switched in get the **Default profile**. Automatic switches are
silent. The taskbar, Alt-Tab, Start and ProfileHop's own menu don't count as an app.

- Tray menu → **Default profile** picks the profile for every app with nothing remembered. On first start it is set
  to the mouse's current profile.
- Tray menu → **Switch per app** turns automatic switching and learning off or on. Hotkeys work either way.

## Config and logs

`%APPDATA%\ProfileHop\config.json` (reload from the tray menu) and `%APPDATA%\ProfileHop\logs`. Notable keys:

- `cycle`: e.g. `[1, 5]` to limit next/previous to those profiles
- `forceOnboardMode`: `true` switches the mouse back to onboard mode if G HUB left it in host mode
- `logLevel`: `Debug` hex-dumps every HID++ frame
- `defaultProfile`: profile for apps with nothing remembered (missing = set from the mouse at first start)
- `appProfiles`: remembered profiles, `{ "c:\\games\\cs2\\cs2.exe": 3 }` (lower-cased full exe path → profile);
  edit by hand and use Reload config, or delete an entry so that app follows the default again
- `autoSwitch`: `false` turns per-app switching and learning off (same as the **Switch per app** menu item)
- `switchNotification`: `false` hides the notification after a profile hotkey; `balloon` now only covers errors and
  notices

## CLI

`profilehop-cli list | probe | status [--raw] [--force-onboard] | profiles [--raw] | set-profile <n>` — for
checking the mouse by hand. `profiles` reads each on-board slot (read-only).

## Build

.NET 10 SDK + Visual Studio C++ build tools.

```
dotnet test ProfileHop.slnx
dotnet publish src/ProfileHop.Tray -c Release
```

Every push to `main` builds, tests, and publishes a GitHub release. Protocol notes: `docs/protocol-verification.md`.
