# Transition Map

<!-- [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md -->

This document maps **every source artifact to its migrated or reimplemented destination** for the
AgOpenGPS migration from **.NET Framework 4.8 / Windows Forms** (Windows-only) to **.NET 8.0 /
Avalonia** (cross-platform: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`), across all twelve
projects in `SourceCode/AgOpenGPS.sln`. It is the companion **single-source-of-truth** to
`CHANGELOG.md`, and is referenced by the `// [XPLAT] migrated from net48/WinForms — see
MIGRATION_DOCS/TRANSITION_MAP.md` provenance comments carried by every changed file. Behavior-frozen
contracts (PGN frames, field/ISOXML formats, the settings XML schema, guidance mathematics) are proven
in **`PARITY_REPORT.md` (on disk)** — all five golden-file suites are committed, enforcing, and green
on the local Linux environment, with tri-OS CI confirmation as the documented residual. The per-feature
cross-platform disposition checklist is in **`FEATURE_TRACEABILITY.md` (on disk)**; the non-technical
brief **`VALUE_SUMMARY.md` is on disk**.

> **Accuracy contract.** This map records the **disposition** (the transformation applied) of every
> artifact and a **parity status** that reflects the artifact's *actual state in the repository now*.
> The migrated suite has converged: the full `AgOpenGPS.sln` builds clean in Debug **and** Release
> (`TreatWarningsAsErrors`) for both `net8.0` and `net8.0-windows`, with **0 errors and 0 new C#
> warnings** (the only residual notices are 8 pre-existing `AVLN3001` Avalonia-XAML runtime-loader
> notices in unmodified views — not promoted by `TreatWarningsAsErrors`, out of scope). The local Linux
> test run is **115 passed / 1 skipped / 0 failed** across all three assemblies, with **all five golden
> suites — PGN, Guidance, ISOXML, Settings, and Field — captured, committed, and enforcing**. A row is
> therefore marked **`At parity (local)`** when its replacement exists on disk, is integrated/wired, and
> is build/test-verified green on the local Linux environment, with the shared residual being **tri-OS
> CI execution** (and, for the GL rows, on-hardware confirmation). Pure build-config / Core / doc rows
> with no cross-OS execution concern are **`At parity`**. Per-OS graceful-degradation rows are
> **`Feature-gated (per-OS)`**. The lone **`Deferred`** item is the out-of-scope `GetPublicFieldsAsync`
> sub-capability (AAP §0.2.3).
>
> **Open residuals (carried honestly, tracked in `PARITY_REPORT.md`).** (1) **Tri-OS CI execution** —
> the `.github/workflows/build.yml` + `release.yml` matrices (windows-latest / ubuntu-latest /
> macos-latest, the last covering `osx-x64` + `osx-arm64`) are **on disk**; what remains is executing
> them on GitHub-hosted runners (external evidence). (2) **On-hardware desktop-GL confirmation
> (Open Risk #1)** — the `RequestDesktopGlProfile` hook is wired in both composition roots and the
> GLES/ANGLE runtime fail-safe is enforced; per-OS confirmation that a desktop-GL context is obtained and
> the `glReadPixels` back-buffer scan renders correctly is the residual. (3) **Latent guidance
> peer-reference wiring (Open Risk #8)** — the guidance math is unit-proven, but the live end-to-end
> `SetGuidanceReferences` composition wiring is a pre-existing gap outside the 31 review findings.
> (4) **ISOXML export driver** — the single skipped test (`IsoXmlExport_DrivenFromDomainGraph_*`)
> requires a full `FormGPS` domain graph; the import/round-trip ISOXML contract is otherwise enforced.
>
> **Historical note.** This migration was executed as a one-solution, multi-checkpoint effort. An interim
> checkpoint (CP5) used a temporary `<Compile Remove>` build-closure gate while the `FormGPS`-coupled
> domain classes were being decoupled into injectable services. **That gate has been fully removed** —
> there are now **zero** `<Compile Remove>` / `<AvaloniaXaml Remove>` / `<AvaloniaResource Remove>`
> entries in any project; the `FormGPS`→services decoupling is complete (the only residual `mf`/`FormGPS`
> tokens are `// [XPLAT]` provenance comments), and the extracted services, the `AvaloniaGeoViewport`
> adapter, and the domain classes all compile into the normal build on every target. The tables below
> reflect this **final, un-gated state**.

---

## Legend

Every transition table below uses **exactly four columns** — **Original** | **Replaced with** |
**Disposition** | **Parity status** — in that order.

**Disposition** (the kind of transformation applied to the artifact):

- **Migrated** — Recompiled / minimal-change and de-Windowsed; **behavior frozen** (the file is updated in place, or moved, but its outputs are unchanged).
- **Reimplemented** — Rebuilt on the cross-platform stack because a Windows-only API/UI could not be minimally changed (e.g., WinForms shell, `OpenTK.GLControl` host, WMI/Registry backing).
- **Feature-gated** — Per-OS graceful degradation where no cross-platform equivalent exists (the capability no-ops or is best-effort off-Windows, never breaking startup or core guidance).
- **Unchanged** — Kept as-is / used as a **REFERENCE** (not modified by the migration).

**Parity status** (the artifact's actual state now):

- **At parity** — Replacement exists on disk and is build/test-verified to behave identically, with no cross-OS execution concern (pure build config, the portable Core with passing tests, docs, licenses).
- **At parity (local)** — Replacement exists on disk, is **integrated/wired**, and is build/test-verified **green on the local Linux environment**; the shared residual is **tri-OS CI execution** (and, where noted, on-hardware GL confirmation).
- **Feature-gated (per-OS)** — Parity achieved by design via per-OS graceful degradation rather than full behavioral identity.
- **Deferred** — Genuinely out of scope / not implemented (the lone case is the `GetPublicFieldsAsync` sub-capability, AAP §0.2.3).
- **n/a (new surface)** — A net-new artifact with no pre-migration original (e.g., the platform-services interface).

---

## Build, Runtime & Entry Points

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/Directory.Build.props` | same | Migrated (flipped `<TargetFramework>net48</TargetFramework>` → `net8.0` at line 4; shared baseline for 11 of 12 projects; retains `EnableNETAnalyzers` / `EnforceCodeStyleInBuild` + Release `TreatWarningsAsErrors`) | At parity |
| `SourceCode/AgOpenGPS.sln` | same | Migrated (integrity pass; all 12 projects already SDK-style; GUIDs/paths preserved) | At parity |
| `SourceCode/GPS/AgOpenGPS.csproj` | same | Migrated (multi-target `net8.0;net8.0-windows`; RIDs `win-x64;linux-x64;osx-x64;osx-arm64`; removed `UseWindowsForms`/`ImportWindowsDesktopTargets`; added Avalonia 11.3.18 + `System.IO.Ports` 9.0.0; dropped `System.Memory`/`System.ValueTuple` polyfills; replaced GMap/ColorPicker/GLControl; fixed `Include` casing. **Zero `<Compile Remove>` entries remain.** `dotnet restore` + `dotnet build` succeed for both targets in Debug and Release) | At parity (local) |
| `SourceCode/AgIO/Source/AgIO.csproj` | same | Migrated (`WinExe`+WinForms → `Exe` + Avalonia 11.3.18 stack + `System.IO.Ports` 9.0.0; removed `UseWindowsForms`/`ImportWindowsDesktopTargets` and the `WindowsBase` ref; added RIDs `win-x64;linux-x64;osx-x64;osx-arm64`) | At parity (local) |
| `SourceCode/Updater/AgOpenGPS.Updater.csproj` | same | Migrated (flipped explicit `net48` → `net8.0`; removed `System.Windows.Forms`/`System.Management` GAC refs; reworked onto the Avalonia 11.3.18 stack; GPLv3 retained) | At parity (local) |
| `SourceCode/{ModSim,GPS_Out,AgDiag,Keypad,AgLibrary}/**/*.csproj` | same | Migrated (removed `UseWindowsForms`; modern TFM via the shared `Directory.Build.props`) | At parity (local) |
| `SourceCode/AgOpenGPS.Core/AgOpenGPS.Core.csproj` | same | Migrated (removed `PresentationCore` WPF ref, `System.Net.Http` GAC ref, and `System.Memory` 4.6.0 polyfill; kept `Newtonsoft.Json` 13.0.4, `OpenTK` 3.3.3, `SkiaSharp` 2.88.9, and the `AgLibrary` `ProjectReference`; stays single-target `net8.0`) | At parity |
| `SourceCode/AgIO/Source/App.config` | (removed) | Migrated (`net48` startup section deleted; unused by SDK-style `net8.0` builds and a case-sensitive-filesystem hazard) | At parity |
| `SourceCode/GPS/Program.cs` | same | Reimplemented (Avalonia bootstrap: `[STAThread]` / `Application.Run(FormGPS)` → `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`; `BuildAvaloniaApp()` chains `AvaloniaGeoViewport.RequestDesktopGlProfile(...)`; single-instance routed through `IPlatformServices.TryAcquireSingleInstance` with the identity GUID `{516-0AC5-B9A1-55fd-A8CE-72F04E6BDE8F}` preserved; per-OS factories registered via `PlatformServicesFactory.Register(...)`; the single process-level `IPlatformServices` is published as `Program.PlatformServices`; GPS auto-start/auto-stop of AgIO wired via `Program.StartAgIO()/StopAgIO()` gated on the auto-start/auto-off settings) | At parity (local) |
| `SourceCode/AgIO/Source/Program.cs` | same | Reimplemented (`[STAThread]` + `Application.Run(new FormLoop())` → `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`; single-instance routed through `IPlatformServices` with the named-mutex GUID `{8F6F0AC4-B9A1-55fd-A8CF-72F04E6BDE8F}` preserved; culture setup and `Restart()` preserved and made cross-platform via `Environment.ProcessPath`) | At parity (local) |

---

## UI Shell — Reimplemented in Avalonia

The operational Windows Forms surface (`FormGPS` + ~67 GPS dialogs, `FormLoop` + ~21 AgIO dialogs) is
reimplemented as Avalonia views. The GPS shell `App.axaml(.cs)`, the `MainView` kiosk shell, and all 60
GPS dialog/shell `.axaml` views (each with a matching `.axaml.cs` code-behind) are **on disk and compile
into the normal GPS build** on both TFMs. The previously-scaffolded view subset is **fully un-gated**.
The `MainView` shell wires its operator buttons, toggles, and hotkeys to view-model/code-behind commands
through a `ShellCommands` delegate bundle assembled in the GPS composition root, and every migrated
Field/Guidance/Settings dialog is reachable through a shell command or menu path (proven by the
`ShellCommandsCoverageTests` command-map coverage suite). The existing Core **MVVM/Presenter scaffold**
(`RelayCommand`, `IPanelPresenter`, `IErrorPresenter`) is **wired up** to the Avalonia views, replacing
the legacy `FormGPS` constructing `ApplicationCore(dir, null, null)`.

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/GPS/Forms/FormGPS.cs` (+ `FormGPS.Designer.cs`, `.resx`) | `SourceCode/GPS/App.axaml(.cs)` + `SourceCode/GPS/Views/MainView.axaml(.cs)` | Reimplemented (kiosk main shell; Avalonia Fluent theme + day/night palette derived from `FormGPS`. Operator buttons/toggles/hotkeys wired to commands via the `ShellCommands` bundle; section/zone buttons bound to live `SectionService` availability; field-close lifecycle, AgIO auto-start/stop, and dialog navigation wired from the composition root. Compiles in the GPS build; theme tokens applied to remove day/night drift) | At parity (local) |
| `SourceCode/GPS/Forms/**/*.cs` (~67 dialogs across `Settings/`, `Field/`, `Pickers/`, `Guidance/`, `Config/`, `Profiles/`, `Inputs/` + root dialogs) | `SourceCode/GPS/Views/**/*.axaml(.cs)` | Reimplemented (1:1 visual/behavioral parity; legacy `Forms/` retired. All 60 GPS `.axaml` views have matching `.axaml.cs` code-behind — declared handlers implemented, `AutomationProperties.Name` accessibility labels added, day/night theme tokens applied — and **all compile into the GPS build** (zero `<AvaloniaXaml Remove>` gating remains). Each is reachable from the shell command/menu graph) | At parity (local) |
| `SourceCode/AgIO/Source/Forms/**/*` (`FormLoop` shell + ~21 dialogs) | `SourceCode/AgIO/Source/Views/**/*.axaml(.cs)` (+ `App.axaml`) | Reimplemented (FormLoop → `MainWindow` composition root that wires the four transport services; all view files on disk and compiling; dialogs reachable through the `MainWindow` command surface) | At parity (local) |
| `SourceCode/Keypad/*.cs` (`GenericKeypad`/`NumKeypad`/`Keyboard` UserControls) | `SourceCode/Keypad/**/*.axaml` | Reimplemented (`Keyboard.axaml` + `NumKeypad.axaml` exist as Avalonia `UserControl`s, shared by GPS + AgIO; `GenericKeypad` retained as the shared base) | At parity (local) (F-042) |
| `SourceCode/ModSim/Source/Forms/**/*` | `SourceCode/ModSim/Source/Views/**/*.axaml(.cs)` (+ `App.axaml`) | Reimplemented (ModSim Avalonia shell `MainSimView` + `App.axaml` + `FormYesView`/`FormTimedMessageView` on disk) | At parity (local) (F-039) |
| `SourceCode/GPS/Controls/**/*`, `SourceCode/AgIO/Source/Controls/**/*` | Avalonia controls / extensions | Reimplemented (AgIO `TextBoxExtensions`/`NumericUpDownExtensions` migrated to launch Avalonia dialogs; GPS `Controls/AvaloniaGeoViewport.cs` GL-host adapter on disk and compiled into the GPS build on both TFMs) | At parity (local) |

---

## Rendering Host — Reimplemented (Adapter)

The renderer is insulated from the host by the existing `GeoViewportBase` abstraction, so only a
host **adapter** changes — the Core DrawLib/`GLW` drawing code is untouched. The three legacy GL
surfaces `oglMain` / `oglZoom` / `oglBack` map to one or more `OpenGlControlBase` instances (or one
control with offscreen framebuffers), where **`oglBack` is the offscreen buffer used for the
section/lookahead `glReadPixels` pixel scan**. The dominant feasibility risk — Avalonia's GL context is
frequently OpenGL ES / ANGLE, while the legacy DrawLib relies on immediate-mode GL — is addressed by two
mechanisms on disk: a **runtime fail-safe** in `AvaloniaGeoViewport` (audits the GL context strings,
gates the immediate-mode path to a harmless cleared surface under GLES/ANGLE so it can never crash-loop)
and a **desktop-GL request hook** (`RequestDesktopGlProfile`) **wired** in the GPS and AgIO composition
roots to request a desktop-GL compatibility profile ahead of ANGLE/EGL. The remaining residual —
on-hardware, per-OS confirmation that a desktop-GL context is obtained and that the `glReadPixels`
back-buffer scan renders correctly — is the dominant **Open Risk #1 in `PARITY_REPORT.md` (on disk)**.

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/GPS/WinForms/GeoViewport.cs` | `SourceCode/GPS/Controls/AvaloniaGeoViewport.cs` | Reimplemented (`: GeoViewportBase` over Avalonia `OpenGlControlBase`; overrides `OnOpenGlInit`/`OnOpenGlRender`/`OnOpenGlDeinit`. Legacy WinForms `OpenTK.GLControl` host retired; adapter on disk and compiled with the GLES/ANGLE fail-safe gate and the `RequestDesktopGlProfile` hook wired from the composition roots) | At parity (local); GL on-hardware pending (Open Risk #1) |
| `SourceCode/GPS/Forms/OpenGL.Designer.cs` | `SourceCode/GPS/Services/RenderCoordinator.cs` | Reimplemented (projection/frustum/back-buffer scan/overlays extracted; on disk and compiled. Three `GL.ReadPixels` paths preserved for the section/lookahead scan, zoom overlap, and flag pick) | At parity (local); GL on-hardware pending (Open Risk #1) |
| `SourceCode/AgOpenGPS.Core/Drawing/GeoViewportBase.cs` + the `GLW` DrawLib | same | Unchanged (kept minimal-change; the abstraction that insulates the renderer from the host swap, implemented by both the retired WinForms host and the Avalonia host) | At parity |
| Package `OpenTK.GLControl 3.3.3` | Avalonia `OpenGlControlBase` (`Avalonia.OpenGL`, ships in Avalonia core) | Reimplemented (host control replaced; the `OpenTK.GLControl` package reference dropped from the converted GPS `csproj`) | At parity (local) |
| Package `OpenTK 3.3.3` (math/bindings) | same | Unchanged (DrawLib/`GLW` math + GL bindings kept; bound to the Avalonia GL context via `GlInterface.GetProcAddress`) | At parity |

---

## Logic Decoupling — Services Extracted from FormGPS/FormLoop partials (behavior frozen)

The real-time domain pipeline that lived inside `FormGPS`/`FormLoop` partial classes is **Extracted**
(recorded under the **Migrated** disposition — code moves, behavior frozen) into plain,
constructor-injectable `Services` classes. **State-separation crux:** domain state (`pn.fix`, guidance
lines, coverage, boundary) moves into the Services/Core; render + UI state (camera, GL matrices, panel
toggles) stays in the Avalonia view + `RenderCoordinator`; the `mf`/`FormGPS` god-object back-reference
is eliminated via constructor injection (the only residual `FormGPS` tokens are `// [XPLAT]` provenance
comments). **Both the AgIO services and the GPS services exist, compile into the normal build, and are
covered by golden-file parity suites green on the local Linux environment.**

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/GPS/Forms/Position.designer.cs` | `SourceCode/GPS/Services/PositionService.cs` | Migrated (extract the `UpdateFixPosition` scan loop, CAHRS heading/roll fusion, WGS84→local-plane conversion, boundary/contour/recorded-path capture, autosteer-safe state, 1000 ms RTK-recovery debounce; behavior frozen; **verified by** `GuidanceEquivalenceTests` (tolerance `Is.LessThan(0.001)`), green locally) | At parity (local) |
| `SourceCode/GPS/Forms/UDPComm.Designer.cs` + `SourceCode/GPS/Forms/PGN.Designer.cs` | `SourceCode/GPS/Services/PgnDispatcher.cs` | Migrated (UDP receive loops + PGN encode/decode/CRC + the 70 ms `udpWatchLimit` throttle; header `0x80 0x81 0x7F`, additive checksum, loopback `127.0.0.1:15555` / peer `:17777`; no added receive→fuse→steer→section latency; **verified by** `PgnFrameGoldenTests`, green locally) | At parity (local) |
| `SourceCode/GPS/Forms/Sections.Designer.cs` | `SourceCode/GPS/Services/SectionService.cs` | Migrated (section/zone + machine-byte logic: 1–16 unique / up to 64 same-width sections via PGN `0xE5`, machine byte PGN `0xEF`, `isJobStarted` gating; behavior frozen; `PerformSectionClick`/`PerformZoneClick` + section/zone state readers exposed for the MainView kiosk shell) | At parity (local) |
| `SourceCode/GPS/Forms/SaveOpen.Designer.cs` | `SourceCode/GPS/Services/FieldIoService.cs` | Migrated (field load/save/export; `Path.Combine` + `Directory`/`File.Exists` validation; `isJobStarted` load gate; all numeric I/O via `InvariantCulture`; before/after field-close lifecycle callbacks wired from the GPS composition root; **verified by** `FieldRoundTripTests` (11/11 field goldens enforcing) / `IsoXmlEquivalenceTests`, green locally) | At parity (local) |
| `SourceCode/GPS/Forms/OpenGL.Designer.cs` | `SourceCode/GPS/Services/RenderCoordinator.cs` | Migrated (projection/frustum/back-buffer scan/overlays; see the Rendering-Host section for the GL residual) | At parity (local); GL on-hardware pending (Open Risk #1) |
| `SourceCode/AgIO/Source/Forms/{NMEA,NTRIPComm,SerialComm,UDP}.Designer.cs` | `SourceCode/AgIO/Source/Services/{NmeaService,NtripService,SerialCommService,UdpLoopbackService}.cs` | Migrated (AgIO comm logic lifted into four injectable services on disk and compiling; frozen PGN/socket/NMEA contracts preserved byte-for-byte; `System.Windows.Forms` purged, status surfaced via events; NTRIP mountpoint CR/LF/control-char validation and UDP minimum-length guards added) | At parity (local) |

---

## Platform Services — New Abstraction Layer

`IPlatformServices` (Dependency Inversion) isolates every OS-specific call — application-data/config
root, brightness get/set, serial-port enumeration, and single-instance acquisition — behind one
interface defined in `AgOpenGPS.Core`. A `PlatformServicesFactory` selects the concrete implementation
at startup via `RuntimeInformation.IsOSPlatform`, using **delegate registration** so Core never
references the GPS/AgIO concrete types (no `Core → app` dependency). **All four bootstraps (GPS
`Program.cs` + `App.axaml.cs`, AgIO `Program.cs` + `App.axaml.cs`) call `PlatformServicesFactory.Register(...)`
and then `Create()`** — the registration gap is closed. GPS publishes the single process-level instance
as `Program.PlatformServices` and the App composition root **reuses** it rather than creating a second
instance.

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| (none — new interface) | `SourceCode/AgOpenGPS.Core/Platform/IPlatformServices.cs` | Reimplemented (new abstraction: `AppDataRoot`, `GetBrightness`/`SetBrightness`, `GetSerialPortNames`, `TryAcquireSingleInstance`; on disk and compiling within the portable Core) | n/a (new surface) |
| (none — new) | `SourceCode/AgOpenGPS.Core/Platform/PlatformServicesFactory.cs` | Reimplemented (selects `Windows`/`Linux`/`Mac` impl via `RuntimeInformation.IsOSPlatform` using **delegate registration** — no `Core → GPS/AgIO` dependency. Per-OS `Register(...)` is wired in all four GPS/AgIO bootstraps; `Create()` resolves the running-OS implementation) | At parity (local) |
| `SourceCode/GPS/Classes/CBrightness.cs` + `SourceCode/GPS/Properties/RegistrySettings.cs` | `SourceCode/GPS/Platform/WindowsPlatformServices.cs` (+ `SourceCode/AgIO/Source/Services/WindowsPlatformServices.cs`) | Reimplemented (WMI brightness + named-mutex single-instance + Registry migration-read, Windows APIs confined under the `net8.0-windows` target; compiles in both the GPS and AgIO Windows builds) | At parity (local) (F-044) |
| (modeled on `WindowsPlatformServices`) | `SourceCode/GPS/Platform/LinuxPlatformServices.cs` (+ `SourceCode/AgIO/Source/Services/LinuxPlatformServices.cs`) | Feature-gated (sysfs `/sys/class/backlight` best-effort brightness; XDG `~/.config/AgOpenGPS` config root; lockfile + advisory `FileStream.Lock` single-instance, fail-closed) | At parity (local); Feature-gated (per-OS) |
| (modeled on `WindowsPlatformServices`) | `SourceCode/GPS/Platform/MacPlatformServices.cs` (+ `SourceCode/AgIO/Source/Services/MacPlatformServices.cs`) | Feature-gated (`~/Library/Application Support/AgOpenGPS` config root; brightness no-op; lockfile + `flock` advisory-lock single-instance, fail-closed) | At parity (local); Feature-gated (per-OS) |

---

## Windows-Coupled Classes — Updated / Abstracted

These classes used Windows-only APIs (WMI, `System.Media`, `System.Drawing` from `.resx`,
`System.Windows.Forms.DataVisualization`) or Windows-only packages. **All of them now compile into the
normal cross-platform build** — there is no `<Compile Remove>` gating remaining. Each disposition is
recorded below.

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/GPS/Classes/CBrightness.cs` | same *(routes through `IPlatformServices`)* | Feature-gated (routes through `IPlatformServices.SetBrightness`; WMI body relocated to `WindowsPlatformServices`; already returns `-1` gracefully when no controllable display exists; compiles in the GPS build) | Feature-gated (per-OS) (F-044) |
| `SourceCode/GPS/Classes/CSound.cs` | same *(cross-platform audio)* | Reimplemented (`System.Media.SoundPlayer` → a cross-platform `CSoundClip` abstraction that writes the wav resource to a temp file once and spawns the platform player — `afplay` on macOS, `paplay`/`aplay` on Linux, Windows `SoundPlayer` on Windows; never blocks the scan-loop/UI thread; compiles in the GPS build) | At parity (local) (F-043) |
| `SourceCode/GPS/Classes/VehicleTextures.cs`, `ScreenTextures.cs`, `Brands.cs` | same *(Avalonia/Skia images)* | Reimplemented (`System.Drawing.Bitmap`-from-`.resx` → Avalonia `Avalonia.Media.Imaging` + `SkiaSharp` `avares://` assets feeding the OpenGL textures via a shared SkiaSharp normalization; compiles in the GPS build) | At parity (local) |
| `SourceCode/GPS/Classes/CExtensionMethods.cs` | same | Migrated (dropped the WinForms-specific helpers — `NudlessNumericUpDown`, `SetProgressNoAnimation` — and the `System.Windows.Forms`/`Drawing2D` `using`s; remaining helpers (e.g. `CheckColorFor255`) are framework-agnostic; compiles in the GPS build) | At parity (local) |
| `System.Windows.Forms.DataVisualization` charting (`FormGraphHeading`/`FormGraphSteer`/`FormGraphXTE`/`FormCorrection`) | custom Avalonia drawing / cross-platform charting in `SourceCode/GPS/Views/Settings/` | Reimplemented (series/axes/zoom/autoscale + rolling-data buffer preserved as custom Avalonia chart `Control`s — the XTE chart folded into `FormGraphXTEView`, a `HeadingChartControl` for the heading/roll plots, plus the `FormGraph*View` / `FormCorrectionView` code-behind — on disk and compiling; the Windows-only `DataVisualization` `<Reference>` removed from the GPS `csproj`) | At parity (local) |
| Package `GMap.NET.WinForms 2.1.7` | Feature-gated (package removed; online background imagery gated off) | Feature-gated (online background imagery is optional; the SQLite tile cache stays cross-platform; the field still renders without it) | Feature-gated (per-OS) (F-021) |
| Package `MechanikaDesign.WinForms.UI.ColorPicker 2.0.0` | Avalonia `ColorPicker` hosted in `SourceCode/GPS/Views/Pickers/FormColorPickerView.axaml(.cs)` | Reimplemented (`FormColorPickerView` uses Avalonia's `ColorSpectrum` / `ColorSlider` in place of the WinForms color-picker dialog — on disk and compiling. The primitives resolve from the installed Avalonia 11.3.18 stack, so no separate `ColorPicker` package reference is required) | At parity (local) |
| Packages `Accord.Imaging 3.8.0` + `Accord.Video.DirectShow 3.8.0` (webcam) | feature-gate (no off-Windows equivalent) | Feature-gated (Accord DirectShow is Windows-only and abandoned; default `isWebCamOn=false`; lowest-priority optional convenience) | Feature-gated (per-OS) (F-045) |

---

## Settings — Updated (XML schema frozen)

The Windows Registry backing — `docs/settings.md` L26-28 states *"All settings are stored in Windows
Registry (not .config files)"* — and the `%AppData%` path source are replaced with an
`IPlatformServices` config root and cross-platform locations, **while preserving the settings XML
schema and the `CSettingsMigration` round-trip exactly**. Per `docs/settings.md` L32-36, the split is
Vehicle `VehicleProfiles/{name}.xml`, Tool `ToolProfiles/{name}.xml`, Environment
`Environment/environment.xml`. **Both the GPS and AgIO settings are migrated on disk** to the
cross-platform config root, with the settings round-trip enforced by `SettingsRoundTripTests`.

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/GPS/Properties/{RegistrySettings,VehicleSettings,ToolSettings,Settings,SettingsLegacy}.cs` | same | Migrated (config-root sourced from `IPlatformServices.AppDataRoot`; **settings XML schema frozen**; `RegistrySettings.cs` no longer makes live `Registry.CurrentUser` calls — the one-time Windows Registry migration-read is confined to `WindowsPlatformServices` under the `net8.0-windows` target; compiles in the GPS build; **verified by** `SettingsRoundTripTests`, green locally) | At parity (local) (F-036) |
| `SourceCode/GPS/Classes/CSettingsMigration.cs` | same | Migrated (legacy→split round-trip preserved exactly; one-time Registry read on Windows; compiles in the GPS build; covered by `SettingsRoundTripTests`) | At parity (local) |
| `SourceCode/AgIO/Source/Properties/{RegistrySettings,Settings}.cs` | same | Migrated (Windows-Registry backing replaced with a cross-platform XML store at `<ApplicationData>/AgOpenGPS/registry.xml` via `Environment.SpecialFolder.ApplicationData`; schema/keys preserved; `XDocument.Load` hardened with `DtdProcessing = Prohibit` + `XmlResolver = null`) | At parity (local) |
| Windows Registry backing (`docs/settings.md` L26-28) | `IPlatformServices` config root: Windows `%AppData%\AgOpenGPS`, Linux `~/.config/AgOpenGPS`, macOS `~/Library/Application Support/AgOpenGPS` (`docs/settings.md` L32-36) | Reimplemented (Registry→path migration on Windows; both GPS and AgIO use the cross-platform config root) | At parity (local) |

---

## Single-Instance & Serial

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/GPS/Program.cs` named Mutex `{516-0AC5-B9A1-55fd-A8CE-72F04E6BDE8F}` | `IPlatformServices.TryAcquireSingleInstance` (Windows named Mutex / Linux+macOS lockfile + advisory lock) | Reimplemented (cross-platform single-instance routed through `IPlatformServices`; the mutex identity GUID is preserved) | At parity (local) (F-010) |
| `SourceCode/AgIO/Source/Program.cs` Mutex `{8F6F0AC4-B9A1-55fd-A8CF-72F04E6BDE8F}` | `IPlatformServices.TryAcquireSingleInstance` | Reimplemented (cross-platform single-instance routed through `IPlatformServices`, preserving the original GUID and "only one AgIO" semantics; `Restart()` made cross-platform) | At parity (local) (F-010) |
| `System.IO.Ports` (`COMx` enumeration) | `System.IO.Ports` 9.0.0 (cross-platform NuGet) + port-**name** enumeration abstracted via `IPlatformServices.GetSerialPortNames` (`COMx` vs `/dev/ttyUSB*`, `/dev/ttyACM*`, `/dev/cu.*`) | Migrated (serial I/O preserved; only port-name discovery is abstracted; the enumeration seam present in AgIO `SerialCommService` and the GPS serial path) | At parity (local) (F-026/F-038) |

---

## Core & Algorithms — Minimal-Change / Behavior Frozen

The shared domain library and the C-prefixed algorithm classes are recompiled and de-Windowsed with
the smallest possible edits; their **outputs are frozen**. The guidance mathematics is the highest
behavioral-parity priority: **Stanley** = `CGuidance.DoSteerAngleCalc()`; **Pure Pursuit** =
`CTrackMethods.GoalPoint()` (`atan2(2 · wheelbase · sin(error), lookahead)`); safety guards
`maxSteerAngle = 30°` and `maxAngularVelocity = 0.64°/s`. **`AgOpenGPS.Core` is portable and verified on
disk** (33/33 tests pass), and **all of `GPS/Classes` now recompile within the normal GPS build**
(behavior frozen) — there is no `<Compile Remove>` gate. The guidance math is unit-proven by
`GuidanceEquivalenceTests`; the one honest residual is the latent live **`SetGuidanceReferences`**
peer-wiring (Open Risk #8 in `PARITY_REPORT.md`), a pre-existing composition gap outside the 31 review
findings.

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/AgOpenGPS.Core/**/*.cs` | same | Migrated (recompiled for `net8.0`; purged WPF types: `CommandManager` in `ViewModels/RelayCommand.cs`, `Visibility` in `FieldTableViewModel.cs`, the `Media3D` `using` in `Models/Camera.cs`; builds clean and 33 tests pass) | At parity |
| `SourceCode/GPS/Classes/**/*.cs` (`CGuidance`, `CTrackMethods`, `CDubins`, `CABLine`, `CABCurve`, `CContour`, `CYouTurn`, `CTrack`, `CAHRS`, `CSection`, `CTool`, `CVehicle`, `CTram`, `CFence`, `CHead`, `CFieldData`, `CISOBUS`, `CSmartWAS`, `CSim`, …) | same | Migrated (recompiled, **behavior frozen**; **all classes now compile in the normal GPS build** — the `FormGPS` god-object back-reference is decoupled into injected collaborators / delegates / bindable properties (the only residual `FormGPS` tokens are `// [XPLAT]` provenance comments); `CSmartWAS(FormGPS)` → `CSmartWAS(ApplicationModel)`; **guidance math verified by** `GuidanceEquivalenceTests`, green locally. Residual: live `SetGuidanceReferences` peer-wiring — Open Risk #8) | At parity (local) |

---

## Tests & Parity

All five golden-file parity suites are **authored, committed, and enforcing** (each loader checks
`File.Exists` and **fails** — not Ignores — when a required golden is absent). The local Linux run is
115 passed / 1 skipped / 0 failed; the single skip is the `FormGPS`-graph-dependent ISOXML **export**
driver (the import/round-trip ISOXML contract is enforced). Byte-stable fixtures are pinned via
`.gitattributes` (`-text`). See `PARITY_REPORT.md` for the full proof.

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/AgOpenGPS.Tests/SampleTest.cs` | (deleted) | Removed (the placeholder test is deleted from disk; superseded by the golden-file parity suites below) | Removed |
| (modeled on `SourceCode/AgLibrary.Tests/Settings/XmlSettingsHandlerTests.cs`) | `SourceCode/AgOpenGPS.Tests/Parity/{PgnFrameGoldenTests,FieldRoundTripTests,IsoXmlEquivalenceTests,SettingsRoundTripTests,GuidanceEquivalenceTests}.cs` | Migrated (all five suite bodies authored on disk; the `Parity/Golden/**` fixtures — including the 11 enforcing `Field/*.txt` goldens — are committed and byte-stable via `.gitattributes`. **Green on the local Linux environment**; tri-OS CI is the residual) | At parity (local) |
| `SourceCode/AgLibrary.Tests/Settings/XmlSettingsHandlerTests.cs` + `TestSettings.xml` | same | Unchanged (REFERENCE round-trip byte-compare template extended by the new parity suites; builds and 3 tests pass) | At parity |

---

## Build / CI

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `.github/workflows/build.yml` | same | Migrated (`windows-latest` → `strategy.matrix` windows-latest / ubuntu-latest / macos-latest (the macOS leg covering `osx-x64` + `osx-arm64`) with `fail-fast: false`, per-OS artifacts; on disk. Residual: **execution on GitHub-hosted runners** (external evidence)) | At parity (local); CI execution pending |
| `.github/workflows/release.yml` | same | Migrated (4 per-RID legs — windows→`win-x64`, ubuntu→`linux-x64`, macos→`osx-x64`, macos→`osx-arm64`; self-contained publish; cross-platform archiving guarded by `if: runner.os`; per-RID asset names; bundles `LICENSE` + GPS/Updater `License.txt`; draft/prerelease logic retained; on disk. Residual: **execution on GitHub-hosted runners**) | At parity (local); CI execution pending |

---

## Updater

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/Updater/**` | same | Feature-gated (reworked onto `net8.0` + Avalonia 11.3.18, or feature-gated per OS; GPLv3 retained. `csproj` flipped off `net48` with `System.Windows.Forms`/`System.Management` GAC refs removed) | At parity (local) |
| `SourceCode/.editorconfig` | same | Migrated (the `*.Designer.cs` analyzer glob is capital-`D` and lists `Position`/`Config*` etc.; carries the `// [XPLAT]` provenance comment) | At parity |

---

## Documentation & Licenses

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `README.md` | same | Migrated (per-OS build/run/publish instructions; maintenance-mode posture removed; cross-platform messaging — "AgOpenGPS is now cross-platform … runs natively on Windows, macOS, and Linux"; multi-target publish recipes with framework paired to RID) | At parity |
| `docs/**/*.md` | same | Migrated (reference updates for the cross-platform stack) | At parity |
| `docs/pgn-protocol.md` | same | Unchanged (REFERENCE; frozen protocol contract — header `0x80 0x81 0x7F`, additive-checksum CRC, loopback ports 15555/17777 — byte-equivalence proven in `PARITY_REPORT.md` via `PgnFrameGoldenTests`) | At parity |
| root `.gitattributes` | same | Migrated (added `-text` byte-stable rules for parity/golden fixtures — `SourceCode/AgOpenGPS.Tests/Parity/**`, `**/*.txt`, `**/*.xml`, and `SourceCode/AgLibrary.Tests/Settings/TestSettings.xml` — supporting byte-compare) | At parity |
| `MIGRATION_DOCS/{CHANGELOG,TRANSITION_MAP,PARITY_REPORT,FEATURE_TRACEABILITY,VALUE_SUMMARY}.md` | (new) | Reimplemented (all five deliverables on disk and kept current; this final pass reconciles them with the integrated on-disk state and the honest open residuals) | At parity |
| `/LICENSE` (Apache 2.0), `SourceCode/GPS/License.txt` (GPLv3), `SourceCode/Updater/License.txt` (GPLv3) | same | Unchanged (retained, not rewritten) | At parity |

---

## Cross-Platform Case-Sensitivity Fixes (renames)

On Linux/macOS, file names must match exact case. The legacy GPS `Forms/` tree has been **retired**,
so these GPS case-hazards are resolved by deletion; the `.editorconfig` analyzer glob already uses the
capital-`D` `.Designer.cs` form. The same `FormYes`/`FormtimedMessage` case-fixes also apply under
`AgIO/Source/Forms/` and `ModSim/Source/Forms/`, where the mis-cased originals were removed and
reimplemented as Avalonia `FormYesView`/`FormTimedMessageView`.

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/GPS/Forms/Position.designer.cs` | `SourceCode/GPS/Forms/Position.Designer.cs` | Migrated (rename; normalize lowercase `d` for case-sensitive filesystems — resolved by the `Forms/` retirement; `.editorconfig` glob is capital-`D` and lists `Position`) | At parity |
| `SourceCode/GPS/Forms/FormYes.designer.cs` | `SourceCode/GPS/Forms/FormYes.Designer.cs` | Migrated (rename; normalize lowercase `d` — resolved by deletion; reimplemented as the Avalonia `FormYesView`) | At parity |
| `SourceCode/GPS/Forms/FormtimedMessage.resx` | `SourceCode/GPS/Forms/FormTimedMessage.resx` | Migrated (rename; normalize lowercase `t` to match `FormTimedMessage.cs` — resolved by deletion; reimplemented as the Avalonia `FormTimedMessageView`) | At parity |

---

## Every Windows-coupled item accounted for

Each Windows-only dependency or API in the `net48`/WinForms baseline is confirmed below with its final
migration disposition (an explicit acceptance requirement). All items below are **on disk and compiling
into the normal cross-platform build** — there is no remaining `<Compile Remove>` gating.

- [x] **WMI monitor brightness** (`CBrightness.cs`, `System.Management`) → **Feature-gated per-OS** (Windows WMI relocated to `WindowsPlatformServices` under `net8.0-windows`; Linux sysfs best-effort; macOS no-op; graceful `-1` fallback preserved).
- [x] **Windows Registry settings backing** (`Microsoft.Win32.Registry`) → **Reimplemented** as a cross-platform config root / XML store (Windows `%AppData%\AgOpenGPS`, Linux `~/.config/AgOpenGPS`, macOS `~/Library/Application Support/AgOpenGPS`); settings XML schema frozen.
- [x] **`System.Media.SoundPlayer` audio** (`CSound.cs`) → **Reimplemented** on a cross-platform audio abstraction (`afplay`/`paplay`/`aplay`; Windows `SoundPlayer` on Windows).
- [x] **`System.Windows.Forms.DataVisualization` charting** (`FormGraphHeading`/`FormGraphSteer`/`FormGraphXTE`/`FormCorrection`) → **Reimplemented** as custom Avalonia chart controls; the Windows-only `<Reference>` removed from the GPS `csproj`.
- [x] **`GMap.NET.WinForms` online imagery** → **Feature-gated** (optional background imagery; SQLite tile cache stays cross-platform; field renders without it).
- [x] **`Accord.Imaging` / `Accord.Video.DirectShow` webcam** → **Feature-gated** off-Windows (DirectShow is Windows-only and abandoned; default `isWebCamOn=false`).
- [x] **`OpenTK.GLControl` GL host** → **Reimplemented** on Avalonia `OpenGlControlBase` via the `SourceCode/GPS/Controls/AvaloniaGeoViewport.cs` adapter (on disk and compiled; GLES/ANGLE fail-safe + `RequestDesktopGlProfile` hook wired; on-hardware GL confirmation = Open Risk #1 in `PARITY_REPORT.md`).
- [x] **Named-Mutex single-instance** → **Reimplemented** as cross-platform single-instance via `IPlatformServices.TryAcquireSingleInstance` in both GPS and AgIO (Windows named Mutex / Linux+macOS lockfile + advisory lock), GUIDs preserved.
- [x] **`PresentationCore` (Core) / `WindowsBase` (AgIO) / `System.Windows.Forms` (Updater) GAC refs** → **Removed**.
- [x] **`System.Management` GAC** → **NuGet package under `net8.0-windows`** (confined to `WindowsPlatformServices`; confirmed present in the GPS Windows publish output).
- [x] **`net48` polyfills `System.Memory` / `System.ValueTuple`** → **Removed** (in-box on `net8.0`).
- [x] **`App.config` `net48` startup section** → **Removed** (AgIO `App.config` deleted; an SDK-style + case-sensitive-filesystem hazard).

---

*This file is the single-source-of-truth old→new mapping for the AgOpenGPS WinForms → Avalonia
cross-platform migration, reconciled with the final integrated on-disk state. Rows are recorded
`At parity` when their replacement is build/test-verified with no cross-OS concern, `At parity (local)`
when on disk + integrated + green on the local Linux environment with tri-OS CI as the residual,
`Feature-gated (per-OS)` for graceful-degradation capabilities, and `Deferred` for the lone out-of-scope
`GetPublicFieldsAsync` sub-capability. See also `CHANGELOG.md` (the narrative spine),
`PARITY_REPORT.md` (behavioral-parity proof + open risks), `FEATURE_TRACEABILITY.md` (per-feature
F-001…F-045 cross-platform disposition), and `VALUE_SUMMARY.md` (executive brief) — all on disk.*
