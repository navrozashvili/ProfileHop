# ProfileHop

Windows tray app: a global hotkey switches a Logitech PRO X2 SUPERSTRIKE to another onboard
profile via HID++ 2.0 (feature 0x8100 setCurrentProfile). Full plan: `docs/PLAN.md`
(source: https://claude.ai/artifact/EyUgfVjthtANXvwGPeYwdM).

## Build

- `dotnet build ProfileHop.slnx` / `dotnet test ProfileHop.slnx`
- AOT publish: `dotnet publish src/ProfileHop.Cli -c Release` (RID win-x64 is set in the csproj).
  If the link step says `'vswhere.exe' is not recognized`, put
  `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on PATH.
- Everything is x64 (`PlatformTarget` in Directory.Build.props) because CsWin32's SetupDi structs are arch-specific.
- Win32 APIs: add names to the project's own `NativeMethods.txt` — `src/ProfileHop.Core/` for HID, `src/ProfileHop.Tray/`
  for UI (CsWin32, `allowMarshaling: false`, generated `internal`). No hand-written P/Invoke.
- `profilehop.exe --no-mouse [--data-dir <dir>]` runs the tray UI without hardware (grey icon).

## Rules for Claude Code

- **Never write mouse memory.** Do not implement or call 0x8100 functions 6–8 (memory write), firmware
  update or DFU features, or any function whose purpose you have not confirmed. Only use the reads,
  setCurrentProfile, setCurrentDpiIndex, and (behind config) setOnboardMode.
- **Verify before trusting.** Every function number and parameter layout comes from open-source
  implementations (libratbag, Solaar), not Logitech documentation. Confirm each against those sources and
  against real device replies (milestones 3–4) before building on it. If a reply disagrees with the plan,
  the device is right: stop and report.
- **Hardware steps are the user's.** Anything that needs the mouse is a manual check the user runs. Ask them
  to run the CLI command and paste the output; don't assume success. Never run the CLI against the device yourself.
- **AOT-clean.** No reflection-based JSON, no `dynamic`; trim and AOT warnings are errors.
- **Stay tiny.** Idle ~0% CPU, a few MB RAM, exe < 10 MB. UI windows exist only while open; no timers,
  polling or background work at idle; no UI frameworks or extra NuGet UI dependencies.
- Small diffs, one milestone per commit, and unit tests for every encoder and decoder.
