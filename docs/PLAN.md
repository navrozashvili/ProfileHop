# ProfileHop implementation plan (condensed copy)

Source of truth: https://claude.ai/artifact/EyUgfVjthtANXvwGPeYwdM. This copy keeps the technical detail.

## Goal

A global keyboard hotkey switches the PRO X2 SUPERSTRIKE to another onboard profile, without G HUB.
Profile 1 "Normal" (3200 DPI), profile 2 "Game" (800 DPI, HITS actuation level 2). The app sends one HID++ 2.0
command, OnboardProfiles (0x8100) setCurrentProfile, through the LIGHTSPEED receiver or USB cable. It never
writes onboard memory. Optional secondary mode: setCurrentDpiIndex cycles DPI slots inside the active profile.

## Stack

| Choice | Decision |
| --- | --- |
| Runtime | .NET 10, net10.0-windows, x64 |
| Publish | NativeAOT win-x64, InvariantGlobalization |
| Win32 interop | CsWin32 (NativeMethods.txt), no hand-written P/Invoke |
| HID access | Own wrapper over SetupDi*, HidD_*, HidP_GetCaps, CreateFile, overlapped ReadFile/WriteFile |
| Hotkeys | RegisterHotKey on a hidden message-only window |
| Tray | Shell_NotifyIcon, no UI framework |
| Config | System.Text.Json with a source-generated context |
| Tests | xUnit against a fake transport |

Projects: `ProfileHop.Core` (HID + HID++ + controller), `ProfileHop.Cli` (list/probe/status/set-profile),
`ProfileHop.Tray` (WinExe), `tests/ProfileHop.Core.Tests`.

## HID transport (Windows)

Finding candidates (`HidDeviceFinder`):
1. HidD_GetHidGuid → SetupDiGetClassDevs(DIGCF_PRESENT | DIGCF_DEVICEINTERFACE) → SetupDiEnumDeviceInterfaces /
   SetupDiGetDeviceInterfaceDetail to list every HID path.
2. For each path, CreateFile with desired access 0 (query only), HidD_GetAttributes, keep VendorID 0x046D.
3. HidD_GetPreparsedData → HidP_GetCaps; keep UsagePage 0xFF00: Usage 0x0001 = short reports (ID 0x10, 7 bytes),
   Usage 0x0002 = long reports (ID 0x11, 20 bytes).
4. Group collections of one physical device by the path prefix before `&col0x` (or CM_Get_Parent). Log PID and
   HidD_GetProductString; do not hard-code PIDs.

Opening and I/O (`HidChannel`):
- GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, OPEN_EXISTING, FILE_FLAG_OVERLAPPED.
- Write: buffer exactly OutputReportByteLength (20 for long), byte 0 = report ID. If WriteFile fails, retry once
  with HidD_SetOutputReport.
- Read: dedicated background thread, overlapped ReadFile with InputReportByteLength buffer, push into a
  System.Threading.Channels channel. One reader per opened collection.
- Always send long reports (0x11). Replies may arrive on either collection; read both if both exist.
- Dispose: CancelIoEx before CloseHandle.

Finding the mouse (`MouseController.Connect`):
- Wired: device index 0xFF. Via receiver: receiver answers at 0xFF, paired devices at 0x01–0x06 (mouse ≈ 0x01).
- Probe 0x01…0x06 then 0xFF with Root getProtocolVersion. Keep the first that replies with HID++ ≥ 2.0 and has
  feature 0x8100. Cache path + index. If cable and receiver both answer, stick with the first until it errors.

Hot-plug: RegisterDeviceNotification for GUID_DEVINTERFACE_HID on the message window; on WM_DEVICECHANGE mark the
connection stale, reconnect lazily on next hotkey press. Do not poll.

## HID++ 2.0 layer

Long report (20 bytes): [0] 0x11, [1] device index, [2] feature index (looked up at runtime), [3] (functionId << 4)
| swId, [4..19] params zero-padded. Software ID fixed non-zero, 0x0B. swId 0 = unsolicited notification.
Reply match: same device index, same feature index, same byte 3. Timeout 500 ms, one retry.

Errors → typed `HidppException` with the code:
- HID++ 2.0: byte 2 = 0xFF, byte 3 = original feature index, byte 4 = original function/swId, byte 5 = code.
  1 Unknown, 2 InvalidArgument, 3 OutOfRange, 4 HwError, 6 InvalidFeatureIndex, 7 InvalidFunctionId, 8 Busy,
  9 Unsupported.
- HID++ 1.0 from receiver: byte 2 = 0x8F → "mouse not reachable" (asleep, off, not paired at that index).

| Feature | Function | Request params | Reply | Use |
| --- | --- | --- | --- | --- |
| Root 0x0000 (index 0) | 0 getFeature | feature ID, 2 bytes BE | [index, type, version]; index 0 = unsupported | look up 0x0001, 0x8100 |
| Root 0x0000 | 1 getProtocolVersion | 00 00 <ping> | [major, minor, ping echo] | probe device indexes |
| FeatureSet 0x0001 | 0 getCount, 1 getFeatureId(i) | i | [id hi, id lo, type, version] | `probe` dumps all features |
| OnboardProfiles 0x8100 | 0 getProfilesDescription | — | memory model, format, profile count, … | number of slots |
| OnboardProfiles 0x8100 | 2 getOnboardMode | — | 1 onboard, 2 host | precondition |
| OnboardProfiles 0x8100 | 1 setOnboardMode | mode | — | only with config forceOnboardMode |
| OnboardProfiles 0x8100 | 4 getCurrentProfile | — | profile number, 2 bytes BE, 1-based | status |
| OnboardProfiles 0x8100 | 3 setCurrentProfile | profile number, 2 bytes BE | — | the hotkey action |
| OnboardProfiles 0x8100 | 5 memoryRead | sector (2B), offset (2B) | 16 bytes | profile directory (read-only) |
| OnboardProfiles 0x8100 | 11 getCurrentDpiIndex / 12 setCurrentDpiIndex | index | index | optional DPI cycling |

Profile directory: memoryRead sector 0x0000; 4-byte entries [sector hi, sector lo, enabled, reserved], ending at an
entry starting FF FF. Cycle only enabled user profiles; config may override with an explicit list like [1, 2].
**Never call memory write functions (6, 7, 8).**

Notifications: swId 0 on the 0x8100 feature index (profile changed from a mouse button) → re-query
getCurrentProfile, update tray.

## Tray app

WinExe, no visible window. One thread runs the Win32 message loop; HID work runs on a background worker.
Startup: named mutex `Local\ProfileHop` (exit if held) → load config (write defaults on first run) → message-only
window (HWND_MESSAGE), register hotkeys + device notifications, add tray icon → connect in background, show
current profile.

Hotkeys (RegisterHotKey, MOD_NOREPEAT): Ctrl+Alt+F9 next profile; Ctrl+Alt+F10 profile 1; Ctrl+Alt+F11 profile 2;
optional Ctrl+Alt+F12 next DPI slot. On RegisterHotKey failure, tray notification naming the conflicting hotkey.

Handling: post a command with an absolute target ("profile 2") to the worker; coalesce rapid presses, keep the
latest. Worker reconnects if stale, setCurrentProfile, confirms with getCurrentProfile, updates tray.

Feedback: tray icon shows the profile number (GDI-drawn HICON or one .ico per profile). Tooltip
`ProfileHop: Game (profile 2)`. Optional sound / 1 s balloon, off by default. Error: grey icon, tooltip
`Mouse not reachable`; next press retries.
Menu: profile list with check on active, Reload config, Open config folder, Start with Windows (toggle), Exit.
Config: `%APPDATA%\ProfileHop\config.json`. Autostart: HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ProfileHop.
Logging: rolling text log in `%APPDATA%\ProfileHop\logs`, 1 MB max, every HID++ frame hex-dumped at Debug.

## Milestones (each ends with a manual check by the user)

1. Scaffold — AOT publish works with no trim warnings.
2. HID enumeration — `list` shows the receiver (and cable) with a long collection.
3. HID++ core — `probe` finds the device index, prints HID++ version, dumps features; 0x8100 present; save output
   as a test fixture.
4. Read onboard state — `status` shows onboard mode 1, profile count, enabled profiles, current profile, DPI slot.
5. Switch from CLI — `set-profile 2` changes DPI and click feel; survives 10-minute idle. If click feel does not
   change → fallback (HITS feature, not in main path).
6. Tray MVP — hotkeys switch profiles in a full-screen game; tray follows mouse-button changes.
7. Robustness — reconnect after unplug/replug, sleep/resume; grey icon; mutex. 20 switches with replugs, no hang.
8. Polish — config reload, autostart, logging, sound, README, AOT tray publish, GitHub Actions. Exe < 10 MB,
   < 20 MB RAM idle, < 200 ms start.
