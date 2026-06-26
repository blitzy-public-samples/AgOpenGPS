# Feature Traceability

<!-- [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md -->

This document is the **per-feature cross-platform disposition checklist** for the AgOpenGPS
migration from **.NET Framework 4.8 / Windows Forms** (Windows-only) to **.NET 8.0 / Avalonia**
(cross-platform: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`). Every Completed feature in the
product is accounted for here with its migration **status** and its **cross-platform code location**.

**Provenance of the feature catalog.** The canonical feature catalog — the identifier space
**F-001 … F-045** — traces to the technical specification **§2.2 "Feature Catalog" (REFERENCE)**,
which is the **authoritative source** for the feature set. This checklist *reconstructs* that catalog
from the §2.2 reference together with the in-repository documentation
(`docs/architecture.md`, `docs/classes.md`, `docs/settings.md`, `docs/pgn-protocol.md`) and the
Agent Action Plan (AAP). Where the §2.2 reference and the in-repo docs are consulted second-hand, the
five identifiers that the **AAP anchors explicitly are fixed at their exact numbers** and are treated
as authoritative pins for the surrounding numbering:

- **F-021 = background map imagery**
- **F-026 = serial communications**
- **F-038 = NMEA serial output (GPS_Out)**
- **F-044 = monitor brightness**
- **F-045 = webcam**

**Acceptance bar — 100% functional parity.** This migration is a **technology-stack and platform
transition, not a feature change**. No feature is added and no feature is removed: every capability
that exists in the `net48`/WinForms product must behave identically (or degrade gracefully where an
operating system offers no equivalent) on Windows, Linux, and macOS. Parity — not redesign — is the
sole acceptance criterion.

> **CP5 status (read first).** This is an in-progress, single-solution migration. At the current
> checkpoint **no feature can yet be marked _At parity_**: the cross-platform behavioral proof
> (golden-file byte/round-trip tests and the tri-OS CI matrix) and the `PARITY_REPORT.md` that would
> record it **do not exist yet**, and the **GPS** application project is **not yet buildable** (it still
> declares `UseWindowsForms=true`, so off-Windows builds stop at `NETSDK1100`). Each feature is therefore
> recorded as **Scaffolded** (its cross-platform code exists on disk now), **Deferred** (its
> cross-platform artifact does not exist yet — e.g. the extracted GPS `Services/`, or the GPS `Classes/`
> recompile that is gated by the GPS build), or **Feature-gated (per-OS)**. The `At parity` label is
> **reserved for a later checkpoint** and is applied per feature only once its code compiles on all three
> operating systems and its golden/CI evidence is recorded in the (planned) `PARITY_REPORT.md`.

## Status labels

Each feature below is assigned exactly one of the following statuses:

- **Scaffolded** — The feature's cross-platform code **exists on disk at this checkpoint** and is
  statically validated (and, for the CP5-buildable projects — `AgOpenGPS.Core`, `AgIO`, `AgLibrary`,
  `Keypad`, `ModSim`, `AgDiag`, `GPS_Out`, `Updater` — compiles), **but full tri-OS behavioral parity is
  unproven**: there are no golden tests, no tri-OS CI run, and no `PARITY_REPORT.md` yet. The note
  records exactly what is on disk and what remains.
- **Deferred** — The feature's cross-platform artifact **does not exist on disk yet** (for example the
  extracted GPS `Services/` — `PositionService` / `PgnDispatcher` / `SectionService` / `FieldIoService`
  / `RenderCoordinator` — or the `SourceCode/GPS/Classes/**` recompile, which is gated by the pending GPS
  `csproj` Avalonia conversion), **or** it is an explicitly out-of-scope sub-capability per the AAP.
- **Feature-gated (per-OS)** — The feature is **full on some operating systems and gracefully degraded
  or no-op where no cross-platform equivalent exists**. Gating never breaks application startup or core
  guidance; the pre-existing no-op/degradation pattern is preserved.
- **At parity** *(not used at CP5)* — Reserved for a later checkpoint: the feature behaves identically
  on all three operating systems, **proven** by golden-file/round-trip results across the tri-OS CI
  matrix and recorded in `PARITY_REPORT.md`. Because that proof does not exist yet, **no feature carries
  this status at CP5**.

**Cross-references.** The proof that would back an `At parity` claim — byte-equivalence and round-trip
golden-file results across the tri-OS CI matrix — will be recorded in a **`PARITY_REPORT.md`** that is
**planned but not yet on disk**. The **file-by-file** old→new disposition and per-checkpoint on-disk
status for each artifact named below is recorded in **`TRANSITION_MAP.md`** (on disk). The narrative
spine of the migration is in `CHANGELOG.md` (on disk); the non-technical brief `VALUE_SUMMARY.md` is
**not yet authored**.

> All tables use four columns — **ID** | **Feature** | **Status** | **Cross-platform location /
> notes** — and are grouped under the product's functional areas.

---

## Phase 1 — Positioning & Connectivity (F-001 … F-010)

Of the ten positioning and connectivity features, the **AgIO-side and Core-side** code is on disk now
(`Scaffolded`): the AgIO Avalonia app + transport services, the platform-services layer, and the Core
geo conversion. The features that depend on the **extracted GPS `Services/`** (`PgnDispatcher`,
`PositionService`) are `Deferred`, because those services are **not yet on disk**.

| ID | Feature | Status | Cross-platform location / notes |
|---|---|---|---|
| F-001 | Two-program architecture (AgOpenGPS `FormGPS` + AgIO `FormLoop`, separate single-instance programs) | Scaffolded | AgIO re-platformed as an Avalonia app under `SourceCode/AgIO` (on disk + compiling); the GPS shell `SourceCode/GPS/App.axaml(.cs)` + `Views/MainView.axaml(.cs)` are on disk with code-behind but **not yet compiled** (GPS `csproj` conversion pending). Two-program model + loopback fabric retained. See `TRANSITION_MAP.md` → UI Shell. |
| F-002 | UDP loopback PGN fabric (ports 15555 / 17777, additive-checksum CRC, header `0x80 0x81 0x7F`) | Scaffolded | AgIO `Source/Services/UdpLoopbackService.cs` is on disk + compiling; the **GPS** counterpart `SourceCode/GPS/Services/PgnDispatcher.cs` is a **target path not yet on disk** (deferred half). Frame format, CRC (sum of bytes 2..N-1), and ports frozen by contract; byte-equivalence **to be proven** by the planned `PgnFrameGoldenTests`. |
| F-003 | AgIO auto-start / auto-stop by AgOpenGPS | Scaffolded | AgIO `Program.cs` migrated to the Avalonia bootstrap (on disk + compiling) with `Restart()` preserved cross-platform; the GPS-side bootstrap that launches AgIO lands with the GPS `csproj` conversion. |
| F-004 | GPS position ingestion (PGN `0xD6`, 52 bytes) | Deferred | Target `SourceCode/GPS/Services/PgnDispatcher.cs` → `PositionService.cs` **not yet on disk**; the 52-byte decode is frozen by contract for the port. |
| F-005 | NTRIP / RTK client (AgIO) | Scaffolded | AgIO `Source/Services/NtripService.cs` (extracted from `NTRIPComm.Designer.cs`) is **on disk + compiling**; tri-OS runtime parity pending CI. |
| F-006 | External IMU + disconnect (PGN `0xD3` / `0xD4`) | Deferred | Handled by the GPS `Services/PgnDispatcher.cs` target — **not yet on disk**; contract frozen. |
| F-007 | GPS / IMU heading & roll fusion (CAHRS) | Deferred | `SourceCode/GPS/Classes/CAHRS.cs` recompile (behavior frozen) is gated by the GPS build; its `PositionService` driver is **not yet on disk**. |
| F-008 | Dual-antenna heading & reverse detection | Deferred | Depends on the GPS `Services/PositionService.cs` target (**not yet on disk**) + `AgOpenGPS.Core` geo models. |
| F-009 | WGS84 ↔ local-plane conversion | Scaffolded | `AgOpenGPS.Core` geo conversion is **on disk + compiling and covered by the Core test suite (33/33 pass)**; numeric I/O via `InvariantCulture`. The `PositionService` that drives it per-fix is `Deferred`. |
| F-010 | Single-instance enforcement | Scaffolded | `IPlatformServices.TryAcquireSingleInstance` + `PlatformServicesFactory` on disk + compiling in Core; AgIO impls compile; the **GPS** `Platform/{Windows,Linux,Mac}PlatformServices.cs` are on disk + harness-verified (Windows named `Mutex`; Linux/macOS lockfile **+ advisory lock, fail-closed**); GUID `{516-0AC5-B9A1-55fd-A8CE-72F04E6BDE8F}` preserved. Factory `Register(...)` wiring is a **CP6** bootstrap item (`Create()` throws until wired). See `TRANSITION_MAP.md` → Platform Services. |

---

## Phase 2 — Guidance & Steering (F-011 … F-020)

All ten guidance and steering features are **Deferred** at CP5. Their cross-platform homes are the
`SourceCode/GPS/Classes/**` algorithm recompile and (for the AutoSteer PGN output) the extracted GPS
`PgnDispatcher` — **none of which are on disk yet**, because they are gated by the pending GPS `csproj`
Avalonia conversion. The guidance mathematics is behavior-frozen by contract; its output equivalence is
**to be proven** by the planned `GuidanceEquivalenceTests` once the GPS project builds. Logic may move
(decoupled from the `FormGPS` god-object via constructor injection) but outputs may not change.

| ID | Feature | Status | Cross-platform location / notes |
|---|---|---|---|
| F-011 | AB line guidance | Deferred | `SourceCode/GPS/Classes/CABLine.cs` recompile (behavior frozen) gated by the GPS build. |
| F-012 | AB curve guidance | Deferred | `SourceCode/GPS/Classes/CABCurve.cs` recompile (behavior frozen) gated by the GPS build. |
| F-013 | Contour guidance | Deferred | `SourceCode/GPS/Classes/CContour.cs` recompile (behavior frozen) gated by the GPS build. |
| F-014 | Track management (create / select / nudge / snap / cycle) | Deferred | `SourceCode/GPS/Classes/CTrack.cs` + `CTrackMethods` recompile gated by the GPS build. |
| F-015 | Pure Pursuit steering (`CTrackMethods.GoalPoint()`, `atan2(2·wheelbase·sin(error), lookahead)`) | Deferred | Frozen math in `SourceCode/GPS/Classes/CTrackMethods.cs`; recompile gated by the GPS build. Output equivalence **to be proven** by the planned `GuidanceEquivalenceTests`. |
| F-016 | Stanley steering (`CGuidance.DoSteerAngleCalc()`) | Deferred | Frozen math in `SourceCode/GPS/Classes/CGuidance.cs`; safety guards `maxSteerAngle = 30°` and `maxAngularVelocity = 0.64°/s` preserved by contract (`docs/settings.md` L54-L55); recompile gated by the GPS build. |
| F-017 | AutoSteer output (PGN `0xFE`, 14 bytes) + module response (`0xFD`) | Deferred | Target `SourceCode/GPS/Services/PgnDispatcher.cs` **not yet on disk**; 14-byte encode/decode frozen by contract. |
| F-018 | U-turn / YouTurn + Dubins paths | Deferred | `SourceCode/GPS/Classes/CYouTurn.cs`, `CDubins.cs` recompile gated by the GPS build. |
| F-019 | Recorded path | Deferred | `SourceCode/GPS/Classes/CRecordedPath.cs` recompile gated by the GPS build. |
| F-020 | Steering-angle-sensor (WAS) calibration | Deferred | `SourceCode/GPS/Classes/CSmartWAS.cs` (`CSmartWAS(FormGPS)` → `CSmartWAS(ApplicationModel)` injection already done); full recompile gated by the GPS build. |

---

## Phase 3 — Field, Mapping & Sections (F-021 … F-035)

Field management, boundary/headland/tramline handling, and section control are **Deferred**: their
cross-platform code is the extracted GPS `Services/` (`FieldIoService`, `SectionService`) and the
`GPS/Classes/**` recompile, **none of which are on disk yet**. **Serial communications (F-026)** is
`Scaffolded` (the AgIO serial service + the platform port-name enumeration are on disk + compiling).
**Background map imagery (F-021)** is `Feature-gated (per-OS)`.

| ID | Feature | Status | Cross-platform location / notes |
|---|---|---|---|
| F-021 | Background map imagery (GMap online tiles) | **Feature-gated (per-OS)** | `GMap.NET.WinForms 2.1.7` removed; online background imagery is replaced by an Avalonia map control **or feature-gated** on **all OSes** (Windows / Linux / macOS). The SQLite tile cache stays cross-platform, and the field renders correctly without imagery — gating never breaks startup or core guidance. The GPS-side gating lands with the GPS `csproj` conversion. See `TRANSITION_MAP.md` → Windows-Coupled Classes. |
| F-022 | Field create / open / save / close | Deferred | Target `SourceCode/GPS/Services/FieldIoService.cs` (from `SaveOpen.Designer.cs`) **not yet on disk**; `Path.Combine` + `File`/`Directory.Exists` validation and `InvariantCulture` numeric I/O frozen by contract; round-trip **to be proven** by the planned `FieldRoundTripTests`. |
| F-023 | Boundary & geofence | Deferred | `SourceCode/GPS/Classes/CFence.cs` / `CBoundary.cs` recompile gated by the GPS build. |
| F-024 | Headland | Deferred | `SourceCode/GPS/Classes/CHead.cs` recompile gated by the GPS build. |
| F-025 | Tramlines | Deferred | `SourceCode/GPS/Classes/CTram.cs` recompile gated by the GPS build. |
| F-026 | Serial communications (GPS / IMU / steer, AgIO) | Scaffolded | `System.IO.Ports 9.0.0` (cross-platform NuGet) retained; the AgIO `Source/Services/SerialCommService.cs` is on disk + compiling and port-**name** enumeration (`COMx` vs `/dev/ttyUSB*`, `/dev/ttyACM*`, `/dev/cu.*`) is abstracted via `IPlatformServices.GetSerialPortNames` (on disk). Serial I/O behavior preserved; the GPS-side serial usage lands with the GPS conversion. |
| F-027 | Section control (manual + auto) | Deferred | Target `SourceCode/GPS/Services/SectionService.cs` (from `Sections.Designer.cs`) **not yet on disk**. |
| F-028 | Multi-section / zone width (1–16 unique / up to 64 same-width via PGN `0xE5`) | Deferred | Target `SourceCode/GPS/Services/SectionService.cs` **not yet on disk**; PGN `0xE5` (8 section bitmask bytes → sections 1–64) and `isJobStarted` gating frozen by contract. |
| F-029 | Coverage / worked-area mapping | Deferred | `SourceCode/GPS/Classes/CFieldData.cs` recompile + the `RenderCoordinator` target **not yet on disk**. |
| F-030 | Flags / markers | Deferred | `SourceCode/GPS/Classes/CFlag.cs` recompile gated by the GPS build. |
| F-031 | Vehicle configuration + brand presets | Deferred | `SourceCode/GPS/Classes/CVehicle.cs`, `Brands.cs` recompile gated by the GPS build; vehicle/brand textures route via Avalonia/Skia images feeding the OpenGL textures at the conversion. |
| F-032 | Tool / implement configuration & geometry | Deferred | `SourceCode/GPS/Classes/CTool.cs` recompile gated by the GPS build. |
| F-033 | Machine / relay control (PGN `0xEF` / `0xEC` / `0xEE`) | Deferred | Targets `SourceCode/GPS/Services/SectionService.cs` / `PgnDispatcher.cs` **not yet on disk**; machine-data, relay-pin, and machine-config frames frozen by contract. |
| F-034 | ISOBUS / Task Controller | Deferred | `SourceCode/GPS/Classes/CISOBUS.cs` recompile gated by the GPS build; ISOBUS heartbeat / process-data PGNs (`0xF0`…`0xF3`) frozen by contract. |
| F-035 | ISOXML V3 / V4 import / export | Deferred | `Dev4Agriculture.ISO11783.ISOXML 0.23.1.1` kept (cross-platform); the V3/V4 import/export code path runs in the GPS project and is gated by the GPS build. Equivalence (name ≤ 248 bytes; AB+Curve export limit) **to be proven** by the planned `IsoXmlEquivalenceTests`. |

---

## Phase 4 — Data, Tools, UI & Peripherals (F-036 … F-045)

The data, tools, UI, and peripheral features are a **mix at CP5**: features whose cross-platform code
is on disk and compiling are **Scaffolded** (settings template, GPS_Out, the ModSim / AgDiag shells,
the GPS day/night theme, and the Keypad controls); the peripheral capabilities that depend on
Windows-only hardware APIs are **Feature-gated per-OS** (monitor brightness, webcam); and features
whose cross-platform artifact is not yet on disk are **Deferred** (audio abstraction, the AgShare
port — whose `GetPublicFieldsAsync` sub-capability is additionally out of scope per the AAP).

| ID | Feature | Status | Cross-platform location / notes |
|---|---|---|---|
| F-036 | Settings system (split Vehicle / Tool / Environment) + `CSettingsMigration` | Scaffolded | XML schema **frozen**; the round-trip template is on disk and tested — `SourceCode/AgLibrary/.../XmlSettingsHandler` with `AgLibrary.Tests/Settings/XmlSettingsHandlerTests` (3/3 passing). The GPS-side swap of the Windows Registry / `%AppData%` backing (`docs/settings.md` L26-L28, L34-L36) for the `IPlatformServices` config root (Windows `%AppData%\AgOpenGPS`, Linux `~/.config/AgOpenGPS`, macOS `~/Library/Application Support/AgOpenGPS`) and the `CSettingsMigration` one-time Registry read **are gated by the GPS build**; round-trip equivalence **to be proven** by the planned `SettingsRoundTripTests`. |
| F-037 | AgShare field upload / download | Deferred | Target `SourceCode/GPS/Classes/AgShare` port **not yet on disk** (gated by the GPS build); `AgShareEnabled=false` by default (`docs/settings.md` L422) frozen by contract. **The `GetPublicFieldsAsync` sub-capability is additionally out of scope** per AAP §0.2.3 ("remains not implemented") and is **not** implemented as part of this migration. |
| F-038 | NMEA serial output (GPS_Out, 4-second timeout) | Scaffolded | `SourceCode/GPS_Out` is on disk and **builds 0/0**, keeps `System.IO.Ports 9.0.0` with port-name enumeration abstracted via `IPlatformServices`; the 4-second NMEA-forwarding timeout is preserved. |
| F-039 | Simulators (in-app `CSim` + standalone `ModSim`) | Scaffolded | `ModSim` is re-platformed onto an Avalonia shell on disk (`MainSimView` + `App.axaml`) and **builds 0/0**. The in-app `SourceCode/GPS/Classes/CSim.cs` recompile is gated by the GPS build. |
| F-040 | Diagnostics (AgDiag) | Scaffolded | `SourceCode/AgDiag` re-platformed onto Avalonia; `csproj` on disk. |
| F-041 | Day / night theming & display preferences | Scaffolded | Avalonia Fluent theme + custom day/night palette in `SourceCode/GPS/App.axaml` (derived from the `FormGPS` colors), consumed by all 60 GPS views via `{DynamicResource}`; authored and consistency-validated but **not yet compiled** (gated by the GPS build). |
| F-042 | On-screen keypad / keyboard (touch) | Scaffolded | `SourceCode/Keypad` `GenericKeypad` / `NumKeypad` / `Keyboard` reimplemented as Avalonia `UserControl`s (`Keyboard.axaml`, `NumKeypad.axaml`), shared by GPS + AgIO; the project **builds 0/0**. |
| F-043 | Audio alerts / sounds (`CSound`) | Deferred | Cross-platform replacement for `System.Media.SoundPlayer` (Windows-only) **not yet on disk** as a building artifact; gated by the GPS build. |
| F-044 | Monitor brightness (`CBrightness` / WMI) | **Feature-gated (per-OS)** | **Windows:** full port (WMI relocated to `WindowsPlatformServices` under `net8.0-windows`, on disk). **Linux:** best-effort via sysfs `/sys/class/backlight` (on disk). **macOS:** gated / no-op (on disk). `CBrightness` already returns `-1` gracefully when no controllable display exists, so the no-op fallback is pre-existing and never breaks startup. The GPS-side routing through `IPlatformServices.SetBrightness` lands with the GPS conversion. |
| F-045 | Webcam (Accord DirectShow) | **Feature-gated (per-OS)** | **Windows:** keep-or-replace the Accord DirectShow capture. **Linux / macOS:** gated (DirectShow is Windows-only and abandoned). Default `isWebCamOn=false` (`docs/settings.md` L446) — the lowest-priority optional convenience; gating never affects core guidance. The GPS-side gating lands with the GPS conversion. |

> **Note on steering/heading graphs.** The on-screen steering, heading, cross-track-error, and
> correction **graphs** were formerly drawn with the Windows-only
> `System.Windows.Forms.DataVisualization` charting control. They are **reimplemented via custom
> Avalonia drawing** — the `RollChart` and `XteChartControl` controls are on disk (series / axes /
> zoom / autoscale + rolling-data buffer preserved) and statically validated, but are **not yet
> compiled** (gated by the GPS build), so they are **Scaffolded** rather than proven At parity. These
> graphs are visualization surfaces under the guidance features (F-015 / F-016) and diagnostics
> (F-040); the Windows-only `<Reference>` is removed from the GPS project at the conversion. See
> `TRANSITION_MAP.md` → Windows-Coupled Classes.

---

## Summary & validation

**Status counts at CP5 (F-001 … F-045) — no feature is `At parity` yet:**

- **Scaffolded: 13** — cross-platform code/structure is on disk now: **F-001** (two-program model),
  **F-002** (PGN loopback fabric, AgIO `UdpLoopbackService`), **F-003** (AgIO auto-start),
  **F-005** (`NtripService`), **F-009** (WGS84 ↔ local-plane, Core 33/33 tests passing),
  **F-010** (single-instance, advisory-lock fixed; factory `Register(...)` wiring is a CP6 item),
  **F-026** (serial — AgIO `SerialCommService` + `IPlatformServices.GetSerialPortNames`),
  **F-036** (settings round-trip template + `AgLibrary.Tests` 3/3),
  **F-038** (GPS_Out, builds 0/0), **F-039** (ModSim Avalonia shell, builds 0/0),
  **F-040** (AgDiag Avalonia), **F-041** (GPS day/night theme across 60 views),
  **F-042** (Keypad controls, builds 0/0).
- **Feature-gated (per-OS): 3** — **F-021** (background map imagery), **F-044** (monitor brightness),
  and **F-045** (webcam, gated off-Windows).
- **Deferred: 29** — every remaining feature, because its cross-platform artifact is either not yet on
  disk or is gated by the GPS project build (which does not yet compile cross-platform — `NETSDK1100`,
  to be resolved at the GPS `csproj` conversion). This includes the guidance/steering math
  (F-011 … F-020), the extracted GPS `Services/` (`PositionService`, `PgnDispatcher`, `SectionService`,
  `FieldIoService`, `RenderCoordinator` — **none yet on disk**), the `GPS/Classes/**` recompile, the
  AgShare port (**F-037**, whose **`GetPublicFieldsAsync`** sub-capability is additionally out of scope
  per AAP §0.2.3), and the audio abstraction (F-043).

Every feature in the catalog is accounted for — nothing is added and nothing is removed, in keeping
with the **100% functional-parity** acceptance bar, which remains the *target*. **No feature can be
marked `At parity` at this checkpoint**, because that status requires both the cross-platform code to
exist *and* golden-file / CI evidence to prove byte-equivalence, and neither the GPS compile nor the
parity proof exists yet. The byte-equivalence and round-trip proof behind each future `At parity`
claim will be recorded in **`PARITY_REPORT.md`** (planned — **not yet on disk** — with the golden-file
test names and tri-OS CI-matrix results); the **file-by-file** old→new disposition for every artifact
named above is recorded in **`TRANSITION_MAP.md`** (on disk).

*This checklist is kept current as the migration proceeds. The feature identifier space
(F-001 … F-045) is the technical specification's §2.2 "Feature Catalog" (REFERENCE); the five
AAP-anchored identifiers (F-021, F-026, F-038, F-044, F-045) are fixed at their exact numbers. See
also `CHANGELOG.md` (narrative spine — on disk), `TRANSITION_MAP.md` (file-level mapping — on disk),
`PARITY_REPORT.md` (behavioral-parity proof and open risks — planned, not yet on disk), and
`VALUE_SUMMARY.md` (executive brief — planned, not yet on disk).*
