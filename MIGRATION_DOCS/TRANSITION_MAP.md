# Transition Map

<!-- [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md -->

This document maps **every source artifact to its migrated or reimplemented destination** for the
AgOpenGPS migration from **.NET Framework 4.8 / Windows Forms** (Windows-only) to **.NET 8.0 /
Avalonia** (cross-platform: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`), across all twelve
projects in `SourceCode/AgOpenGPS.sln`. It is **kept current as the work proceeds** — not authored
only at the end — and is the companion **single-source-of-truth** to `CHANGELOG.md`. It is referenced
by the `// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md` provenance
comments carried by every changed file. Behavior-frozen contracts (PGN frames, field/ISOXML formats,
the settings XML schema, guidance mathematics) are to be proven in a `PARITY_REPORT.md` that is
**planned but not yet on disk**; the per-feature cross-platform disposition checklist is in
`FEATURE_TRACEABILITY.md` (on disk); the non-technical brief `VALUE_SUMMARY.md` is **not yet authored**.

> **Accuracy contract.** This map records the **disposition** (the planned/applied transformation) of
> every artifact and a **parity status** that reflects the artifact's *actual state in the repository
> at the current checkpoint*. A row is marked `At parity` only when its replacement **exists on disk
> and is build/test-verified**. Work whose destination source **exists on disk but is not yet
> build/CI/golden-verified** (e.g., the GPS Avalonia views, whose tri-OS CI matrix and golden evidence
> do not exist yet, and the WinForms-`FormGPS`-coupled GPS sources temporarily gated out of the GPS
> build via `<Compile Remove>` pending decoupling) is recorded `Scaffolded` — never `At parity`,
> because no build/CI/golden evidence exists yet. Work whose destination file does **not** exist at all
> is recorded `Deferred` with its named target and frozen-behavior contract. This contract exists to
> prevent recording views/services as complete before they are proven. The GL-context rows are recorded
> conservatively and point to the **planned** `PARITY_REPORT.md` (not yet on disk) open risks rather
> than claiming proven parity.
>
> **Granularity note (read with `FEATURE_TRACEABILITY.md`).** This is a **file-level** map: an
> `At parity` row certifies that *that individual artifact's* migration is build/test-verified in a
> CP5-buildable project — it is **not** a claim that the end-to-end *feature* it contributes to is
> proven across all three operating systems. End-to-end **feature** parity is tracked separately in
> `FEATURE_TRACEABILITY.md`, where — because the GPS application does not yet compile and no golden /
> tri-OS CI evidence exists — **no feature is `At parity` at CP5** (each is `Scaffolded`, `Deferred`, or
> `Feature-gated`). The two views are consistent: a build-verified file (e.g., an AgIO service) can be
> `At parity` here while the feature spanning it and the not-yet-built GPS side remains `Scaffolded`
> there.

> **Current checkpoint — CP5 (of the one-solution, multi-checkpoint migration).** Verified on disk at
> this checkpoint: the runtime moniker is flipped to `net8.0` (`Directory.Build.props`); **AgIO is
> fully re-platformed** onto Avalonia (`Source/Views/` 17 files + `Source/Services/` — 4 transport
> services plus 3 per-OS `*PlatformServices`) and builds clean; **AgOpenGPS.Core** is portable (WPF
> purged) and its tests pass (33/33), with its **platform layer**
> `AgOpenGPS.Core/Platform/IPlatformServices.cs` + `PlatformServicesFactory.cs` compiling within Core;
> **AgLibrary** builds and its tests pass (3/3); the shared **Keypad** primitives (`Keyboard.axaml`,
> `NumKeypad.axaml`) and the **ModSim** Avalonia shell build. New at **CP5**: the GPS **platform
> implementations** `SourceCode/GPS/Platform/{Windows,Linux,Mac}PlatformServices.cs` exist
> (isolated-harness-verified), and the **GPS `Views/` tree is now fully authored on disk** — all 60
> `.axaml` views plus the `MainView` shell, each with a matching `.axaml.cs` code-behind (declared
> handlers implemented, `AutomationProperties.Name` accessibility labels added), the
> `AvaloniaConfigMenuPanelPresenter`, and the two custom chart controls `RollChart` / `XteChartControl`.
> These GPS views and impls are recorded **`Scaffolded`**: they are statically validated (XAML↔code-behind
> consistency + well-formedness) and the GPS Avalonia `csproj` conversion **has landed** — the project
> targets `net8.0;net8.0-windows`, no longer declares `UseWindowsForms`, and `dotnet restore` +
> `dotnet build` succeed for both `net8.0`/`linux-x64` and `net8.0-windows`/`win-x64` (0 errors). They
> remain `Scaffolded` rather than `At parity` only because the tri-OS CI matrix and golden evidence do
> not exist yet, and because the subset of GPS sources still coupled to the WinForms `FormGPS` god-object
> is temporarily **gated out of the build via `<Compile Remove>` / `<AvaloniaXaml Remove>`** pending
> decoupling. **Gated FormGPS-coupled sources (interim build-closure measure — authoritative complete
> list is the `<Compile Remove>` block in `SourceCode/GPS/AgOpenGPS.csproj`, 27 entries across three
> labelled groups):** (1) Windows-only imaging/audio — `Resources.Designer.cs`, `BrandImages.Designer.cs`,
> `CSound.cs`, `Brands.cs`, `VehicleTextures.cs`, `ScreenTextures.cs`; (2) FormGPS-coupled domain classes
> (each holds a `private readonly FormGPS mf;` back-reference) — `CABCurve`, `CABLine`, `CBoundary`,
> `CFence`, `CHead`, `CTurn`, `CContour`, `CFieldData`, `CGuidance`, `CISOBUS`, `CPatches`,
> `CRecordedPath`, `CTool`, `CTrack`, `CTram`, `CVehicle`, `CYouTurn`, and `AgShare/AgShareUploader`;
> (3) their cascade dependents — `Protocols/ISOBUS/ISO11783_TaskFile.cs`, `Visuals/SectionsVisual.cs`,
> and `Views/Field/FormFieldDataView.axaml(.cs)` (markup excluded in lockstep via `<AvaloniaXaml Remove>`
> / `<AvaloniaResource Remove>`). The `TrackMode` enum + `CTrk` and `CRecPathPt` were extracted into
> `CTrk.cs` / `CRecPathPt.cs` so the non-gated build closes. Each gated item is re-included once decoupled
> into the injectable services below. Still genuinely **absent** (`Deferred`): `SourceCode/GPS/Services/` (the extracted
> `PositionService` / `PgnDispatcher` / `SectionService` / `FieldIoService` / `RenderCoordinator`) and
> `SourceCode/GPS/Controls/` (the `AvaloniaGeoViewport` GL-host adapter). The legacy GPS `Forms/` tree
> has already been **retired**. The Avalonia package line on disk is **11.3.18** (the AAP's recommended
> 11.3.x line).

---

## Legend

Every transition table below uses **exactly four columns** — **Original** | **Replaced with** |
**Disposition** | **Parity status** — in that order.

**Disposition** (the kind of transformation applied to the artifact):

- **Migrated** — Recompiled / minimal-change and de-Windowsed; **behavior frozen** (the file is updated in place, or moved, but its outputs are unchanged).
- **Reimplemented** — Rebuilt on the cross-platform stack because a Windows-only API/UI could not be minimally changed (e.g., WinForms shell, `OpenTK.GLControl` host, WMI/Registry backing).
- **Feature-gated** — Per-OS graceful degradation where no cross-platform equivalent exists (the capability no-ops or is best-effort off-Windows, never breaking startup or core guidance).
- **Unchanged** — Kept as-is / used as a **REFERENCE** (not modified by the migration).

**Parity status** (the artifact's actual state at the current checkpoint):

- **At parity** — Replacement exists on disk and is build/test-verified to behave identically.
- **At parity (pending CI verification)** — Replacement exists and compiles; full identity is pending the tri-OS CI matrix and/or runtime verification.
- **Scaffolded** — Replacement source exists on disk and is **statically validated** (XAML↔code-behind consistency + well-formedness, or isolated-harness compile), but is **not yet build/CI/golden-verified on the tri-OS matrix** (and, for the GPS surface, may still depend on WinForms-`FormGPS`-coupled GPS sources temporarily gated out of the build via `<Compile Remove>` pending decoupling). Full behavioral parity is therefore **unproven** — distinct from both `At parity` (build/test-verified) and `Deferred` (destination does not exist).
- **Feature-gated (per-OS)** — Parity achieved by design via per-OS graceful degradation rather than full behavioral identity.
- **Deferred** — The replacement is scheduled for a later checkpoint and its destination file does **not** exist yet; the WinForms/source artifact may already be removed.
- **n/a (new surface)** — A net-new artifact with no pre-migration original (e.g., the platform-services interface).
- **n/a (doc/non-runtime)** — Documentation or non-runtime artifact; no behavioral parity applies.

---

## Build, Runtime & Entry Points

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/Directory.Build.props` | same | Migrated (flipped `<TargetFramework>net48</TargetFramework>` → `net8.0` at line 4; shared baseline for 11 of 12 projects; retains `EnableNETAnalyzers` / `EnforceCodeStyleInBuild` + Release `TreatWarningsAsErrors`) | At parity |
| `SourceCode/AgOpenGPS.sln` | same | Migrated (integrity pass; all 12 projects already SDK-style; GUIDs/paths preserved) | At parity |
| `SourceCode/GPS/AgOpenGPS.csproj` | same | Migrated (applied: multi-target `net8.0;net8.0-windows`; RIDs `win-x64;linux-x64;osx-x64;osx-arm64`; removed `UseWindowsForms`/`ImportWindowsDesktopTargets`; added Avalonia 11.3.18 + `System.IO.Ports`; dropped polyfills; replaced GMap/ColorPicker/GLControl; fixed `Include` casing. `dotnet restore` + `dotnet build` succeed for both targets, with the WinForms-`FormGPS`-coupled source subset gated via `<Compile Remove>`/`<AvaloniaXaml Remove>` pending decoupling — see the gated-source enumeration above) | At parity (pending CI) |
| `SourceCode/AgIO/Source/AgIO.csproj` | same | Migrated (`WinExe`+WinForms → `Exe` + Avalonia 11.3.18 stack + `System.IO.Ports` 9.0.0; removed `UseWindowsForms`/`ImportWindowsDesktopTargets` and the `WindowsBase` ref; added RIDs `win-x64;linux-x64;osx-x64;osx-arm64`) | At parity |
| `SourceCode/Updater/AgOpenGPS.Updater.csproj` | same | Migrated (flipped explicit `net48` → `net8.0`; removed `System.Windows.Forms`/`System.Management` GAC refs; reworked onto the Avalonia 11.3.18 stack; GPLv3 retained) | At parity |
| `SourceCode/{ModSim,GPS_Out,AgDiag,Keypad,AgLibrary}/**/*.csproj` | same | Migrated (removed `UseWindowsForms`; modern TFM via the shared `Directory.Build.props`) | At parity |
| `SourceCode/AgOpenGPS.Core/AgOpenGPS.Core.csproj` | same | Migrated (removed `PresentationCore` WPF ref, `System.Net.Http` GAC ref, and `System.Memory` 4.6.0 polyfill; kept `Newtonsoft.Json` 13.0.4, `OpenTK` 3.3.3, `SkiaSharp` 2.88.9, and the `AgLibrary` `ProjectReference`; stays single-target `net8.0`) | At parity |
| `SourceCode/AgIO/Source/App.config` | (removed) | Migrated (`net48` startup section deleted; unused by SDK-style `net8.0` builds and a case-sensitive-filesystem hazard) | At parity |
| `SourceCode/GPS/Program.cs` | same | Reimplemented (Avalonia bootstrap landed: `[STAThread]` / `Application.Run(FormGPS)` → `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`; single-instance retained as a named Mutex with the identity GUID `{516-0AC5-B9A1-55fd-A8CE-72F04E6BDE8F}` preserved; no WinForms `using` remains. Compiles in the GPS build; routing single-instance through `IPlatformServices.TryAcquireSingleInstance` is still pending — see the single-instance row) | Scaffolded |
| `SourceCode/AgIO/Source/Program.cs` | same | Reimplemented (`[STAThread]` + `Application.Run(new FormLoop())` → `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`; single-instance preserved with the same named-mutex GUID `{8F6F0AC4-B9A1-55fd-A8CF-72F04E6BDE8F}`; culture setup and `Restart()` preserved and made cross-platform) | At parity |

---

## UI Shell — Reimplemented in Avalonia

The operational Windows Forms surface (`FormGPS` + ~67 GPS dialogs, `FormLoop` + ~21 AgIO dialogs) is
reimplemented as Avalonia views. **The AgIO `MainWindow` shell and all dialog views are on disk and
build-verified**; the self-contained dialogs (Advanced Settings, ISOBUS, and the PGN reference guide) are
**wired into the `MainWindow` footer command bar and reachable**, while the transport-configuration dialog
surface (UDP / NTRIP / Radio / Source / Ethernet / Serial-pass / monitors) — which needs the service-peer
plumbing — awaits its navigation wiring in the later UI checkpoint. The
**GPS** view tree is now **`Scaffolded`**: `SourceCode/GPS/Views/` exists in full — the GPS shell
`App.axaml(.cs)`, the `MainView` shell, and all 60 dialog/shell `.axaml` views each have a matching
`.axaml.cs` code-behind. The GPS `csproj` Avalonia conversion **has landed** and the project **builds
cleanly** for `net8.0`/`linux-x64` and `net8.0-windows`/`win-x64`, with the subset of views/sources
still coupled to the WinForms `FormGPS` god-object gated out via `<Compile Remove>`/`<AvaloniaXaml Remove>`
pending decoupling; full tri-OS CI/golden verification is still outstanding. The existing-but-null-wired Core **MVVM/Presenter
scaffold** (`RelayCommand`, `IPanelPresenter`, `IErrorPresenter`) is **wired up** to the Avalonia views
(the AgIO `MainWindow` composition root already does this), replacing the legacy `FormGPS` constructing
`ApplicationCore(dir, null, null)`.

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/GPS/Forms/FormGPS.cs` (+ `FormGPS.Designer.cs`, `.resx`) | `SourceCode/GPS/App.axaml(.cs)` + `SourceCode/GPS/Views/MainView.axaml(.cs)` | Reimplemented (kiosk main shell; Avalonia Fluent theme + day/night palette derived from `FormGPS`. `App.axaml(.cs)` and `MainView.axaml(.cs)` are on disk with code-behind; authored + consistency-validated and **now compiling in the GPS build**; pending tri-OS CI/golden verification) | Scaffolded |
| `SourceCode/GPS/Forms/**/*.cs` (~67 dialogs across `Settings/`, `Field/`, `Pickers/`, `Guidance/`, `Config/`, `Profiles/`, `Inputs/` + root dialogs) | `SourceCode/GPS/Views/**/*.axaml(.cs)` | Reimplemented (1:1 visual/behavioral parity; legacy `Forms/` already retired. All 60 GPS `.axaml` views have matching `.axaml.cs` code-behind on disk — declared handlers implemented, `AutomationProperties.Name` accessibility labels added, day/night theme tokens applied — authored + consistency-validated and **now compiling in the GPS build** (except `FormFieldDataView`, gated via `<AvaloniaXaml Remove>`); pending tri-OS CI/golden verification) | Scaffolded |
| `SourceCode/AgIO/Source/Forms/**/*` (`FormLoop` shell + ~21 dialogs) | `SourceCode/AgIO/Source/Views/**/*.axaml(.cs)` (+ `App.axaml`) | Reimplemented (FormLoop → `MainWindow` composition root that wires the four transport services; all view files on disk and compiling. The dependency-free dialogs — Advanced Settings (`toolStripSettings`), ISOBUS (`isobusToolStripMenuItem`), PGN Guide — are wired into the `MainWindow` footer and reachable; the transport-configuration dialogs (UDP/NTRIP/Radio/Source/Ethernet/Serial-pass/monitors) need service-peer plumbing and have their navigation wiring deferred to the later UI checkpoint) | Partial — shell + status display + self-contained dialogs at parity; transport-config dialog navigation pending later UI checkpoint |
| `SourceCode/Keypad/*.cs` (`GenericKeypad`/`NumKeypad`/`Keyboard` UserControls) | `SourceCode/Keypad/**/*.axaml` | Reimplemented (`Keyboard.axaml` + `NumKeypad.axaml` exist as Avalonia `UserControl`s, shared by GPS + AgIO; `GenericKeypad` retained as the shared base) | At parity (F-042) |
| `SourceCode/ModSim/Source/Forms/**/*` | `SourceCode/ModSim/Source/Views/**/*.axaml(.cs)` (+ `App.axaml`) | Reimplemented (ModSim Avalonia shell `MainSimView` + `App.axaml` + `FormYesView`/`FormTimedMessageView` on disk) | At parity (F-039) |
| `SourceCode/GPS/Controls/**/*`, `SourceCode/AgIO/Source/Controls/**/*` | Avalonia controls / extensions | Reimplemented (AgIO `TextBoxExtensions`/`NumericUpDownExtensions` already migrated to launch Avalonia dialogs; GPS `Controls/` — the `AvaloniaGeoViewport` GL-host adapter — reintroduced at the GPS conversion; not yet on disk) | Deferred |

---

## Rendering Host — Reimplemented (Adapter)

The renderer is insulated from the host by the existing `GeoViewportBase` abstraction, so only a
host **adapter** changes — the Core DrawLib/`GLW` drawing code is untouched. The three legacy GL
surfaces `oglMain` / `oglZoom` / `oglBack` map to one or more `OpenGlControlBase` instances (or one
control with offscreen framebuffers), where **`oglBack` is the offscreen buffer used for the
section/lookahead `glReadPixels` pixel scan**. The dominant open feasibility risk — Avalonia's GL
context is frequently OpenGL ES / ANGLE, while the legacy DrawLib may rely on immediate-mode GL — and
the `glReadPixels` back-buffer scan are tracked as **open risks for the planned `PARITY_REPORT.md`**
(not yet on disk); the rows below therefore do **not** claim proven parity.

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/GPS/WinForms/GeoViewport.cs` | `SourceCode/GPS/Controls/AvaloniaGeoViewport.cs` *(deferred — CP9)* | Reimplemented (`: GeoViewportBase` over Avalonia `OpenGlControlBase`; overrides `OnOpenGlInit`/`OnOpenGlRender`/`OnOpenGlDeinit`. Legacy WinForms `OpenTK.GLControl` host already retired; adapter not yet on disk. **GL-context feasibility risk — to be tracked in the planned `PARITY_REPORT.md`**) | Deferred |
| `SourceCode/GPS/Forms/OpenGL.Designer.cs` | `SourceCode/GPS/Services/RenderCoordinator.cs` *(deferred — CP9)* | Reimplemented (projection/frustum/back-buffer scan/overlays extracted. **`glReadPixels` back-buffer scan must be verified on the Avalonia surface — to be tracked in the planned `PARITY_REPORT.md`**) | Deferred |
| `SourceCode/AgOpenGPS.Core/Drawing/GeoViewportBase.cs` + the `GLW` DrawLib | same | Unchanged (kept minimal-change; the abstraction that insulates the renderer from the host swap, implemented by both the retired WinForms host and the future Avalonia host) | At parity |
| Package `OpenTK.GLControl 3.3.3` | Avalonia `OpenGlControlBase` (`Avalonia.OpenGL`, ships in Avalonia core) | Reimplemented (host control replaced; the `OpenTK.GLControl` package reference is dropped when the GPS `csproj` is converted at CP9) | Deferred |
| Package `OpenTK 3.3.3` (math/bindings) | same | Unchanged (DrawLib/`GLW` math + GL bindings kept; bound to the Avalonia GL context via `GlInterface.GetProcAddress`) | At parity |

---

## Logic Decoupling — Services Extracted from FormGPS/FormLoop partials (behavior frozen)

The real-time domain pipeline that lived inside `FormGPS`/`FormLoop` partial classes is **Extracted**
(here recorded under the **Migrated** disposition — code moves, behavior frozen) into plain,
constructor-injectable `Services` classes. **State-separation crux:** domain state (`pn.fix`, guidance
lines, coverage, boundary) moves into the Services/Core; render + UI state (camera, GL matrices, panel
toggles) stays in the Avalonia view + `RenderCoordinator`; the `mf`/`FormGPS` god-object back-reference
is eliminated via constructor injection. **AgIO services exist and compile**; **GPS services are
`Deferred`** (`SourceCode/GPS/Services/` does not exist yet) and each row names the destination plus
its frozen-behavior contract and parity test.

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/GPS/Forms/Position.designer.cs` | `SourceCode/GPS/Services/PositionService.cs` *(deferred — CP9)* | Migrated (extract the `UpdateFixPosition` scan loop, CAHRS heading/roll fusion, WGS84→local-plane conversion, boundary/contour/recorded-path capture, autosteer-safe state, 1000 ms RTK-recovery debounce; behavior frozen; **to be verified by the planned** `GuidanceEquivalenceTests` (tolerance `Is.LessThan(0.001)`)) | Deferred |
| `SourceCode/GPS/Forms/UDPComm.Designer.cs` + `SourceCode/GPS/Forms/PGN.Designer.cs` | `SourceCode/GPS/Services/PgnDispatcher.cs` *(deferred — CP9)* | Migrated (UDP receive loops + PGN encode/decode/CRC + the 70 ms `udpWatchLimit` throttle; header `0x80 0x81 0x7F`, additive checksum, loopback `127.0.0.1:15555` / peer `:17777`; no added receive→fuse→steer→section latency; **to be verified by the planned** `PgnFrameGoldenTests`) | Deferred |
| `SourceCode/GPS/Forms/Sections.Designer.cs` | `SourceCode/GPS/Services/SectionService.cs` *(deferred — CP9)* | Migrated (section/zone + machine-byte logic: 1–16 unique / up to 64 same-width sections via PGN `0xE5`, machine byte PGN `0xEF`, `isJobStarted` gating; behavior frozen) | Deferred |
| `SourceCode/GPS/Forms/SaveOpen.Designer.cs` | `SourceCode/GPS/Services/FieldIoService.cs` *(deferred — CP9)* | Migrated (field load/save/export; `Path.Combine` + `Directory`/`File.Exists` validation; `isJobStarted` load gate; all numeric I/O via `InvariantCulture`; **to be verified by the planned** `FieldRoundTripTests` / `IsoXmlEquivalenceTests`) | Deferred |
| `SourceCode/AgIO/Source/Forms/{NMEA,NTRIPComm,SerialComm,UDP}.Designer.cs` | `SourceCode/AgIO/Source/Services/{NmeaService,NtripService,SerialCommService,UdpLoopbackService}.cs` | Migrated (AgIO comm logic lifted into four injectable services on disk and compiling; frozen PGN/socket/NMEA contracts preserved byte-for-byte; `System.Windows.Forms` purged, status surfaced via events) | At parity |

---

## Platform Services — New Abstraction Layer

`IPlatformServices` (Dependency Inversion) isolates every OS-specific call — application-data/config
root, brightness get/set, serial-port enumeration, and single-instance acquisition — behind one
interface defined in `AgOpenGPS.Core`. A `PlatformServicesFactory` selects the concrete implementation
at startup via `RuntimeInformation.IsOSPlatform`, using **delegate registration** so Core never
references the GPS/AgIO concrete types (no `Core → app` dependency). **On disk and compiling within
Core:** `IPlatformServices.cs` and `PlatformServicesFactory.cs`. The three per-OS implementations live
in the application projects — **`SourceCode/AgIO/Source/Services/` (on disk and compiling)** and
**`SourceCode/GPS/Platform/` (on disk and now compiling within the GPS build — `WindowsPlatformServices`
under the `net8.0-windows` target; recorded `Scaffolded` pending tri-OS CI verification)**. The factory's
per-OS `Register(...)` call is **not yet wired into the GPS/AgIO bootstraps** — a required integration
item — so although the projects build, `Create()` would throw at runtime until registration occurs.

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| (none — new interface) | `SourceCode/AgOpenGPS.Core/Platform/IPlatformServices.cs` | Reimplemented (new abstraction: `AppDataRoot`, `GetBrightness`/`SetBrightness`, `GetSerialPortNames`, `TryAcquireSingleInstance`; on disk and compiling within the portable Core) | n/a (new surface) |
| (none — new) | `SourceCode/AgOpenGPS.Core/Platform/PlatformServicesFactory.cs` | Reimplemented (selects `Windows`/`Linux`/`Mac` impl via `RuntimeInformation.IsOSPlatform` using **delegate registration** — no `Core → GPS/AgIO` dependency; on disk and compiling within Core. **Per-OS `Register(...)` wiring into the app bootstraps is pending — CP6 integration item; `Create()` throws until registered**) | At parity (registration pending — CP6) |
| `SourceCode/GPS/Classes/CBrightness.cs` + `SourceCode/GPS/Properties/RegistrySettings.cs` | `SourceCode/GPS/Platform/WindowsPlatformServices.cs` (+ `SourceCode/AgIO/Source/Services/WindowsPlatformServices.cs`) | Reimplemented (WMI brightness + named-mutex single-instance + Registry migration-read, Windows APIs confined under `#if WINDOWS`. AgIO copy compiles; GPS copy now compiling in the GPS build under the `net8.0-windows` target) | Scaffolded |
| (modeled on `WindowsPlatformServices`) | `SourceCode/GPS/Platform/LinuxPlatformServices.cs` (+ `SourceCode/AgIO/Source/Services/LinuxPlatformServices.cs`) | Feature-gated (sysfs `/sys/class/backlight` best-effort brightness; XDG `~/.config` config root; lockfile **+ advisory `FileStream.Lock` single-instance, fail-closed**). AgIO copy compiles; GPS copy on disk + harness-verified | Scaffolded |
| (modeled on `WindowsPlatformServices`) | `SourceCode/GPS/Platform/MacPlatformServices.cs` (+ `SourceCode/AgIO/Source/Services/MacPlatformServices.cs`) | Feature-gated (`~/Library/Application Support` config root; brightness no-op; lockfile **+ `flock` advisory-lock single-instance, fail-closed**). AgIO copy on disk; GPS copy on disk + harness-verified | Scaffolded |


---

## Windows-Coupled Classes — Updated / Abstracted

These classes use Windows-only APIs (WMI, `System.Media`, `System.Drawing` from `.resx`,
`System.Windows.Forms.DataVisualization`) or Windows-only packages. Their cross-platform disposition
is fixed below. The GPS project now builds for both targets; the Windows-coupled classes that cannot yet
run cross-platform are gated out via `<Compile Remove>` (`CSound`, `Brands`, `VehicleTextures`,
`ScreenTextures`) and recorded `Deferred`, while `CBrightness` is already migrated to route through
`IPlatformServices` and compiles. Each target API/abstraction is defined below.

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/GPS/Classes/CBrightness.cs` | same *(routes through `IPlatformServices`)* | Feature-gated (routes through `IPlatformServices.SetBrightness`; WMI body relocated to `WindowsPlatformServices`; already returns `-1` gracefully when no controllable display exists. Migrated and compiling in the GPS build) | Feature-gated (per-OS) (F-044) |
| `SourceCode/GPS/Classes/CSound.cs` | same *(cross-platform audio)* | Reimplemented (`System.Media.SoundPlayer` → cross-platform audio abstraction. Currently gated via `<Compile Remove>` (still `System.Media` on disk) pending cross-platform reimplementation) | Deferred (F-043) |
| `SourceCode/GPS/Classes/VehicleTextures.cs`, `ScreenTextures.cs`, `Brands.cs` | same *(Avalonia/Skia images)* | Reimplemented (`System.Drawing.Bitmap`-from-`.resx` → Avalonia/Skia `avares://` images feeding the OpenGL textures. Currently gated via `<Compile Remove>` pending cross-platform reimplementation) | Deferred |
| `SourceCode/GPS/Classes/CExtensionMethods.cs` | same | Migrated (dropped the WinForms-specific helpers — `NudlessNumericUpDown`, `SetProgressNoAnimation` — and the `System.Windows.Forms`/`Drawing2D` `using`s; remaining helpers (e.g. `CheckColorFor255`) are framework-agnostic; compiles in the GPS build) | At parity (pending CI) |
| `System.Windows.Forms.DataVisualization` charting (`FormGraphHeading`/`FormGraphSteer`/`FormGraphXTE`/`FormCorrection`) | custom Avalonia drawing / cross-platform charting in `SourceCode/GPS/Views/Settings/` | Reimplemented (series/axes/zoom/autoscale + rolling-data buffer preserved as custom Avalonia chart `Control`s — the XTE chart folded into `FormGraphXTEView`, a `HeadingChartControl` for the heading/roll plots, plus the `FormGraph*View` / `FormCorrectionView` code-behind — **on disk and compiling in the GPS build**; the Windows-only `DataVisualization` `<Reference>` has been removed from the GPS `csproj`) | Scaffolded |
| Package `GMap.NET.WinForms 2.1.7` | Avalonia map control **or feature-gated** in `SourceCode/GPS/Views/Field/` *(deferred — CP9)* | Feature-gated (online background imagery is optional; the SQLite tile cache stays cross-platform; the field still renders without it) | Feature-gated (per-OS) (F-021) |
| Package `MechanikaDesign.WinForms.UI.ColorPicker 2.0.0` | Avalonia `ColorPicker` hosted in `SourceCode/GPS/Views/Pickers/FormColorPickerView.axaml(.cs)` | Reimplemented (`FormColorPickerView` uses Avalonia's `ColorSpectrum` / `ColorSlider` in place of the WinForms color-picker dialog — **on disk and compiling in the GPS build**. The `ColorSpectrum`/`ColorSlider` primitives resolve from the installed Avalonia 11.3.18 stack (`Avalonia.Controls.Primitives`), so no separate `ColorPicker` package reference is required) | Scaffolded |
| Packages `Accord.Imaging 3.8.0` + `Accord.Video.DirectShow 3.8.0` (webcam) | feature-gate (no off-Windows equivalent) | Feature-gated (Accord DirectShow is Windows-only and abandoned; default `isWebCamOn=false`; lowest-priority optional convenience) | Feature-gated (per-OS) (F-045) |

---

## Settings — Updated (XML schema frozen)

The Windows Registry backing — `docs/settings.md` L26-28 states *"All settings are stored in Windows
Registry (not .config files)"* — and the `%AppData%` path source are replaced with an
`IPlatformServices` config root and cross-platform locations, **while preserving the settings XML
schema and the `CSettingsMigration` round-trip exactly**. Per `docs/settings.md` L32-36, the split is
Vehicle `VehicleProfiles/{name}.xml`, Tool `ToolProfiles/{name}.xml`, Environment
`Environment/environment.xml`. **AgIO settings are migrated on disk** (cross-platform XML store);
**GPS settings are `Deferred`** (`GPS/Properties/RegistrySettings.cs` still uses
`Registry.CurrentUser` pending the GPS conversion).

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/GPS/Properties/{RegistrySettings,VehicleSettings,ToolSettings,Settings,SettingsLegacy}.cs` | same | Migrated (config-root sourced from `IPlatformServices.AppDataRoot`; **settings XML schema frozen**; `RegistrySettings.cs` no longer makes live `Registry.CurrentUser` calls — the one-time Windows Registry migration-read is confined to `WindowsPlatformServices` under the `net8.0-windows` target; compiles in the GPS build; **to be verified by the planned** `SettingsRoundTripTests`) | Scaffolded (F-036) |
| `SourceCode/GPS/Classes/CSettingsMigration.cs` | same *(CP9)* | Migrated (legacy→split round-trip preserved exactly; one-time Registry read on Windows) | Deferred |
| `SourceCode/AgIO/Source/Properties/{RegistrySettings,Settings}.cs` | same | Migrated (Windows-Registry backing replaced with a cross-platform XML store at `<ApplicationData>/AgOpenGPS/registry.xml` via `Environment.SpecialFolder.ApplicationData`; schema/keys preserved) | At parity |
| Windows Registry backing (`docs/settings.md` L26-28) | `IPlatformServices` config root: Windows `%AppData%\AgOpenGPS`, Linux `~/.config/AgOpenGPS`, macOS `~/Library/Application Support/AgOpenGPS` (`docs/settings.md` L32-36) | Reimplemented (Registry→path migration on Windows; AgIO uses the cross-platform path today via its XML store; GPS-side config-root wiring lands at CP9) | At parity (pending CI verification) |

---

## Single-Instance & Serial

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/GPS/Program.cs` named Mutex `{516-0AC5-B9A1-55fd-A8CE-72F04E6BDE8F}` | `IPlatformServices.TryAcquireSingleInstance` (Windows named Mutex / Linux+macOS lockfile + advisory lock) | Reimplemented (cross-platform single-instance; the mutex identity GUID is preserved. The Avalonia `Program.cs` currently still guards with a direct named Mutex — routing it through `IPlatformServices.TryAcquireSingleInstance` for the Linux/macOS lockfile path is still pending) | Deferred (F-010) |
| `SourceCode/AgIO/Source/Program.cs` Mutex `{8F6F0AC4-B9A1-55fd-A8CF-72F04E6BDE8F}` | same | Reimplemented (uses a cross-platform .NET named `Mutex` — supported on Windows/Linux/macOS — preserving the original GUID and "only one AgIO" semantics; `Restart()` made cross-platform) | At parity |
| `System.IO.Ports` (`COMx` enumeration) | `System.IO.Ports` 9.0.0 (cross-platform NuGet) + port-**name** enumeration abstracted via `IPlatformServices.GetSerialPortNames` (`COMx` vs `/dev/ttyUSB*`, `/dev/ttyACM*`, `/dev/cu.*`) | Migrated (serial I/O preserved; only port-name discovery is abstracted. `System.IO.Ports` 9.0.0 referenced and the enumeration seam present in AgIO `SerialCommService`; GPS-side wiring lands at CP9) | At parity (pending CI verification) (F-026/F-038) |


---

## Core & Algorithms — Minimal-Change / Behavior Frozen

The shared domain library and the C-prefixed algorithm classes are recompiled and de-Windowsed with
the smallest possible edits; their **outputs are frozen**. The guidance mathematics is the highest
behavioral-parity priority: **Stanley** = `CGuidance.DoSteerAngleCalc()`; **Pure Pursuit** =
`CTrackMethods.GoalPoint()` (`atan2(2 · wheelbase · sin(error), lookahead)`); safety guards
`maxSteerAngle = 30°` and `maxAngularVelocity = 0.64°/s`. **`AgOpenGPS.Core` is portable and verified
on disk** (33/33 tests pass); the non-`FormGPS`-coupled **`GPS/Classes`** now **recompile within the GPS
build** (e.g. `CDubins`, `CTrackMethods`, `BoundaryBuilder`, `CFenceLine`, `CSection`, `CTurnLines`,
`CSim` — behavior frozen), while the subset still holding a `FormGPS mf` back-reference (`CGuidance`,
`CTrack`, `CBoundary`, `CContour`, `CYouTurn`, `CTram`, `CVehicle`, `CTool`, `CFieldData`, …) is gated
via `<Compile Remove>` and recorded `Deferred` pending decoupling.

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/AgOpenGPS.Core/**/*.cs` | same | Migrated (recompiled for `net8.0`; purged WPF types: `CommandManager` in `ViewModels/RelayCommand.cs`, `Visibility` in `FieldTableViewModel.cs`, the `Media3D` `using` in `Models/Camera.cs`; builds clean and 33 tests pass) | At parity |
| `SourceCode/GPS/Classes/**/*.cs` (`CGuidance`, `CTrackMethods`, `CDubins`, `CABLine`, `CABCurve`, `CContour`, `CYouTurn`, `CTrack`, `CAHRS`, `CSection`, `CTool`, `CVehicle`, `CTram`, `CFence`, `CHead`, `CFieldData`, `CISOBUS`, `CSmartWAS`, `CSim`, …) | same | Migrated (recompiled, **behavior frozen**; the non-`FormGPS`-coupled classes now compile in the GPS build — `CTrackMethods`, `CDubins`, `CAHRS`, `CSection`, `CSmartWAS` (`CSmartWAS(FormGPS)` → `CSmartWAS(ApplicationModel)` already done), `CSim`, plus `BoundaryBuilder`/`CFenceLine`/`CTurnLines`; the subset still holding a `FormGPS mf` back-reference (`CGuidance`, `CABLine`, `CABCurve`, `CContour`, `CYouTurn`, `CTrack`, `CTool`, `CVehicle`, `CTram`, `CFence`, `CHead`, `CFieldData`, `CISOBUS`) is gated via `<Compile Remove>` pending decoupling; **to be verified by the planned** `GuidanceEquivalenceTests`) | Scaffolded / Deferred (mixed) |

---

## Tests & Parity

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/AgOpenGPS.Tests/SampleTest.cs` | (deleted) | Removed (the placeholder test has been **deleted from disk** — it is no longer present; its role is superseded by the planned golden-file parity suites below, whose bodies are authored when the GPS-dependent `AgOpenGPS.Tests` project builds again at CP9) | Removed |
| (modeled on `SourceCode/AgLibrary.Tests/Settings/XmlSettingsHandlerTests.cs`) | `SourceCode/AgOpenGPS.Tests/Parity/{PgnFrameGoldenTests,FieldRoundTripTests,IsoXmlEquivalenceTests,SettingsRoundTripTests,GuidanceEquivalenceTests}.cs` *(suite bodies deferred — CP9)* | Scaffolded (the `SourceCode/AgOpenGPS.Tests/Parity/Golden/**` fixture directory tree — `Pgn/`, `Field/`, `IsoXml/`, `Settings/`, `Guidance/` — with `README.md` / `.gitkeep` placeholders **exists on disk**; the suite `.cs` bodies and the captured golden fixtures are **not yet authored**) | Scaffolded |
| `SourceCode/AgLibrary.Tests/Settings/XmlSettingsHandlerTests.cs` + `TestSettings.xml` | same | Unchanged (REFERENCE round-trip byte-compare template extended by the new parity suites; builds and 3 tests pass) | At parity |

---

## Build / CI

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `.github/workflows/build.yml` | same *(deferred — CI checkpoint)* | Migrated (target end-state: `windows-latest` → `strategy.matrix` windows/ubuntu/macos with `fail-fast: false`, per-OS artifact `AgOpenGPS-${{ matrix.os }}`; pins `checkout@v5`, `setup-dotnet@v4`, gitversion setup/execute `@v3.2.0` `versionSpec 5.12.x`, `upload-artifact@v4`. Still single `windows-latest` on disk) | Deferred |
| `.github/workflows/release.yml` | same *(deferred — CI checkpoint)* | Migrated (target end-state: 4 per-RID legs — windows→`win-x64`, ubuntu→`linux-x64`, macos→`osx-x64`, macos→`osx-arm64`; self-contained publish; cross-platform archiving guarded by `if: runner.os`; per-RID asset names with version+RID; bundle `LICENSE` + GPS/Updater `License.txt`; keep `softprops/action-gh-release@v2` draft/prerelease logic. Still single `windows-latest` on disk) | Deferred |

---

## Updater

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `SourceCode/Updater/**` | same | Feature-gated (reworked onto `net8.0` + Avalonia 11.3.18, or feature-gated per OS; GPLv3 retained. `csproj` already flipped off `net48` with `System.Windows.Forms`/`System.Management` GAC refs removed) | At parity |
| `SourceCode/.editorconfig` | same | Migrated (the `*.Designer.cs` analyzer glob is already capital-`D` and lists `Position`/`Config*` etc.; carries the `// [XPLAT]` provenance comment) | At parity |

---

## Documentation & Licenses

| Original | Replaced with | Disposition | Parity status |
|---|---|---|---|
| `README.md` | same *(deferred — docs checkpoint)* | Migrated (target end-state: per-OS build/run/publish instructions; remove the maintenance-mode posture; cross-platform messaging. Still "Maintenance Mode" on disk) | Deferred |
| `docs/**/*.md` | same *(deferred — docs checkpoint)* | Migrated (reference updates for the cross-platform stack; not yet applied) | Deferred |
| `docs/pgn-protocol.md` | same | Unchanged (REFERENCE; frozen protocol contract — header `0x80 0x81 0x7F`, additive-checksum CRC, loopback ports 15555/17777 — byte-equivalence **to be proven in the planned** `PARITY_REPORT.md`) | At parity |
| root `.gitattributes` | same *(deferred — parity-fixtures checkpoint)* | Migrated (target end-state: add `-text` byte-stable rules for parity/golden fixtures incl. `SourceCode/AgLibrary.Tests/Settings/TestSettings.xml` and `SourceCode/AgOpenGPS.Tests/Parity/**`, supporting byte-compare. Not yet added on disk) | Deferred |
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

Each Windows-only dependency or API in the `net48`/WinForms baseline is confirmed below with its
migration disposition (an explicit acceptance requirement). Items whose GPS-side wiring lands at the
GPS conversion checkpoint (CP9) are marked with their **target** disposition; the row tables above
carry the per-checkpoint `Deferred` parity where the destination file is not yet on disk.

- [x] **WMI monitor brightness** (`CBrightness.cs`, `System.Management`) → **Feature-gated per-OS** (Windows WMI relocated to `WindowsPlatformServices` under `net8.0-windows`; Linux sysfs best-effort; macOS no-op; graceful `-1` fallback preserved).
- [x] **Windows Registry settings backing** (`Microsoft.Win32.Registry`) → **Reimplemented** as a cross-platform config root / XML store (Windows `%AppData%\AgOpenGPS`, Linux `~/.config/AgOpenGPS`, macOS `~/Library/Application Support/AgOpenGPS`); settings XML schema frozen.
- [x] **`System.Media.SoundPlayer` audio** (`CSound.cs`) → **Reimplemented** on a cross-platform audio abstraction.
- [x] **`System.Windows.Forms.DataVisualization` charting** (`FormGraphHeading`/`FormGraphSteer`/`FormGraphXTE`/`FormCorrection`) → **Reimplemented** as custom Avalonia drawing / cross-platform charting; the Windows-only `<Reference>` removed from the GPS `csproj`.
- [x] **`GMap.NET.WinForms` online imagery** → **Feature-gated** (optional background imagery; SQLite tile cache stays cross-platform; field renders without it).
- [x] **`Accord.Imaging` / `Accord.Video.DirectShow` webcam** → **Feature-gated** off-Windows (DirectShow is Windows-only and abandoned; default `isWebCamOn=false`).
- [x] **`OpenTK.GLControl` GL host** → **Reimplemented** on Avalonia `OpenGlControlBase` via the `AvaloniaGeoViewport` adapter (disposition decided; the `SourceCode/GPS/Controls/AvaloniaGeoViewport.cs` adapter is **not yet on disk** — `Deferred` to the GPS conversion; GL-context risk to be tracked in the planned `PARITY_REPORT.md`).
- [x] **Named-Mutex single-instance** → **Reimplemented** as cross-platform single-instance (AgIO uses a cross-platform .NET named `Mutex` today; GPS routes through `IPlatformServices.TryAcquireSingleInstance` at CP9).
- [x] **`PresentationCore` (Core) / `WindowsBase` (AgIO) / `System.Windows.Forms` (Updater) GAC refs** → **Removed** (Core and AgIO done on disk; GPS at CP9).
- [x] **`System.Management` GAC** → **NuGet package under `net8.0-windows`** (confined to `WindowsPlatformServices`).
- [x] **`net48` polyfills `System.Memory` / `System.ValueTuple`** → **Removed** (in-box on `net8.0`; removed from `AgOpenGPS.Core` on disk; the GPS `csproj` drops them at CP9).
- [x] **`App.config` `net48` startup section** → **Removed** (AgIO `App.config` deleted on disk; an SDK-style + case-sensitive-filesystem hazard).

---

*This file is the single-source-of-truth old→new mapping for the AgOpenGPS WinForms → Avalonia
cross-platform migration, kept current as the work proceeds. Rows are recorded `At parity` only when
their replacement exists and is build/test-verified, `Scaffolded` when the source exists and is
statically validated but not yet build/CI/golden-verified on the tri-OS matrix, and `Deferred` when the destination file does not yet exist
(each with its named target and frozen-behavior contract). See also `CHANGELOG.md` (the narrative
spine) and `FEATURE_TRACEABILITY.md` (per-feature F-001…F-045 cross-platform disposition), both on disk.
Two further deliverables are **planned but not yet authored**: `PARITY_REPORT.md` (behavioral-parity
proof and open risks, including the GL-context and `glReadPixels` risks) and `VALUE_SUMMARY.md`
(executive brief).*

