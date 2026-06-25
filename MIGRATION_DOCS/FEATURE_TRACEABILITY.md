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

## Status labels

Each feature below is assigned exactly one of three statuses:

- **At parity** — The feature behaves **identically on all three operating systems** (Windows, Linux,
  macOS). The behavior-frozen contracts behind these features (PGN frames, field/ISOXML formats, the
  settings XML schema, and guidance mathematics) are proven in `PARITY_REPORT.md`.
- **Feature-gated (per-OS)** — The feature is **full on some operating systems and gracefully
  degraded or no-op where no cross-platform equivalent exists**. Gating never breaks application
  startup or core guidance; the pre-existing no-op/degradation pattern is preserved.
- **Deferred** — The feature (or a named sub-capability of it) is **explicitly not implemented in
  this migration**, consistent with the scope boundaries in the AAP.

**Cross-references.** The proof behind every **At parity** claim — byte-equivalence and round-trip
golden-file results across the tri-OS CI matrix — is recorded in **`PARITY_REPORT.md`**. The
**file-by-file** old→new disposition and per-checkpoint on-disk status for each artifact named below
is recorded in **`TRANSITION_MAP.md`**. The narrative spine of the migration is in `CHANGELOG.md`, and
the non-technical brief is in `VALUE_SUMMARY.md`.

> All tables use four columns — **ID** | **Feature** | **Status** | **Cross-platform location /
> notes** — and are grouped under the product's functional areas.

---

## Phase 1 — Positioning & Connectivity (F-001 … F-010)

All ten positioning and connectivity features migrate **At parity**: the two-program model, the UDP
loopback PGN fabric, GPS/IMU ingestion and fusion, and single-instance enforcement are preserved
byte-for-byte and behavior-for-behavior across all three operating systems.

| ID | Feature | Status | Cross-platform location / notes |
|---|---|---|---|
| F-001 | Two-program architecture (AgOpenGPS `FormGPS` + AgIO `FormLoop`, separate single-instance programs) | At parity | Both re-platformed as Avalonia apps under `SourceCode/GPS` and `SourceCode/AgIO`; the two-program model and loopback fabric are retained unchanged. See `TRANSITION_MAP.md` → UI Shell. |
| F-002 | UDP loopback PGN fabric (ports 15555 / 17777, additive-checksum CRC, header `0x80 0x81 0x7F`) | At parity | `SourceCode/GPS/Services/PgnDispatcher.cs` (GPS) and AgIO `Source/Services/*`; frame format, CRC (sum of bytes 2..N-1), and loopback ports frozen. Proof: `PARITY_REPORT.md` → `PgnFrameGoldenTests`. |
| F-003 | AgIO auto-start / auto-stop by AgOpenGPS | At parity | Preserved across the `Program.cs` Avalonia bootstrap; AgIO `Restart()` retained and made cross-platform. |
| F-004 | GPS position ingestion (PGN `0xD6`, 52 bytes) | At parity | `SourceCode/GPS/Services/PgnDispatcher.cs` → `PositionService.cs`; 52-byte frame decode unchanged. |
| F-005 | NTRIP / RTK client (AgIO) | At parity | AgIO `Source/Services/NtripService` (extracted from `NTRIPComm.Designer.cs`); on disk and compiling. |
| F-006 | External IMU + disconnect (PGN `0xD3` / `0xD4`) | At parity | `SourceCode/GPS/Services/PgnDispatcher.cs`; IMU data and disconnect-notification handling frozen. |
| F-007 | GPS / IMU heading & roll fusion (CAHRS) | At parity | `SourceCode/GPS/Classes/CAHRS.cs` (behavior frozen), driven by `PositionService.cs`. |
| F-008 | Dual-antenna heading & reverse detection | At parity | `SourceCode/GPS/Services/PositionService.cs` + `AgOpenGPS.Core` geo models. |
| F-009 | WGS84 ↔ local-plane conversion | At parity | `AgOpenGPS.Core` geo (`LocalPlane.ConvertWgs84ToGeoCoord`) + `PositionService.cs`; numeric I/O via `InvariantCulture`. |
| F-010 | Single-instance enforcement | At parity | `IPlatformServices.TryAcquireSingleInstance` (Windows named `Mutex`; Linux/macOS lockfile + advisory lock); GPS mutex-identity GUID `{516-0AC5-B9A1-55fd-A8CE-72F04E6BDE8F}` preserved. See `TRANSITION_MAP.md` → Single-Instance & Serial. |

---

## Phase 2 — Guidance & Steering (F-011 … F-020)

All ten guidance and steering features migrate **At parity (behavior frozen)**. The guidance
mathematics is the highest behavioral-parity priority: outputs are required to be identical, and the
algorithm classes are recompiled with the smallest possible edits. Logic may move (decoupled from the
`FormGPS` god-object via constructor injection) but outputs may not change.

| ID | Feature | Status | Cross-platform location / notes |
|---|---|---|---|
| F-011 | AB line guidance | At parity | `SourceCode/GPS/Classes/CABLine.cs`; behavior frozen. |
| F-012 | AB curve guidance | At parity | `SourceCode/GPS/Classes/CABCurve.cs`; behavior frozen. |
| F-013 | Contour guidance | At parity | `SourceCode/GPS/Classes/CContour.cs`; behavior frozen. |
| F-014 | Track management (create / select / nudge / snap / cycle) | At parity | `SourceCode/GPS/Classes/CTrack.cs` + `CTrackMethods`. |
| F-015 | Pure Pursuit steering (`CTrackMethods.GoalPoint()`, `atan2(2·wheelbase·sin(error), lookahead)`) | At parity | Frozen math in `SourceCode/GPS/Classes/CTrackMethods.cs`. Proof: `PARITY_REPORT.md` → `GuidanceEquivalenceTests`. |
| F-016 | Stanley steering (`CGuidance.DoSteerAngleCalc()`) | At parity | Frozen math in `SourceCode/GPS/Classes/CGuidance.cs`; safety guards `maxSteerAngle = 30°` and `maxAngularVelocity = 0.64°/s` preserved (`docs/settings.md` L54-L55). |
| F-017 | AutoSteer output (PGN `0xFE`, 14 bytes) + module response (`0xFD`) | At parity | `SourceCode/GPS/Services/PgnDispatcher.cs`; 14-byte encode/decode frozen. |
| F-018 | U-turn / YouTurn + Dubins paths | At parity | `SourceCode/GPS/Classes/CYouTurn.cs`, `CDubins.cs`. |
| F-019 | Recorded path | At parity | `SourceCode/GPS/Classes/CRecordedPath.cs`. |
| F-020 | Steering-angle-sensor (WAS) calibration | At parity | `SourceCode/GPS/Classes/CSmartWAS.cs` (`CSmartWAS(FormGPS)` → `CSmartWAS(ApplicationModel)` injection). |

---

## Phase 3 — Field, Mapping & Sections (F-021 … F-035)

Field management, boundary/headland/tramline handling, and section control all migrate **At parity**.
The single exception in this area is **F-021 (background map imagery)**, which is **Feature-gated**:
the online-imagery provider has no maintained cross-platform equivalent, so it is replaced or gated
while the field continues to render fully without it.

| ID | Feature | Status | Cross-platform location / notes |
|---|---|---|---|
| F-021 | Background map imagery (GMap online tiles) | **Feature-gated (per-OS)** | `GMap.NET.WinForms 2.1.7` removed; online background imagery is replaced by an Avalonia map control **or feature-gated** on **all OSes** (Windows / Linux / macOS). The SQLite tile cache stays cross-platform, and the field renders correctly without imagery — gating never breaks startup or core guidance. See `TRANSITION_MAP.md` → Windows-Coupled Classes. |
| F-022 | Field create / open / save / close | At parity | `SourceCode/GPS/Services/FieldIoService.cs` (extracted from `SaveOpen.Designer.cs`); `Path.Combine` + `File`/`Directory.Exists` validation; numeric I/O via `InvariantCulture`. Proof: `PARITY_REPORT.md` → `FieldRoundTripTests`. |
| F-023 | Boundary & geofence | At parity | `SourceCode/GPS/Classes/CFence.cs` / `CBoundary.cs`. |
| F-024 | Headland | At parity | `SourceCode/GPS/Classes/CHead.cs`. |
| F-025 | Tramlines | At parity | `SourceCode/GPS/Classes/CTram.cs`. |
| F-026 | Serial communications (GPS / IMU / steer, AgIO) | At parity | `System.IO.Ports 9.0.0` (cross-platform NuGet) retained; only port-**name** enumeration (`COMx` vs `/dev/ttyUSB*`, `/dev/ttyACM*`, `/dev/cu.*`) is abstracted via `IPlatformServices.GetSerialPortNames`. Serial I/O behavior preserved. |
| F-027 | Section control (manual + auto) | At parity | `SourceCode/GPS/Services/SectionService.cs` (extracted from `Sections.Designer.cs`). |
| F-028 | Multi-section / zone width (1–16 unique / up to 64 same-width via PGN `0xE5`) | At parity | `SourceCode/GPS/Services/SectionService.cs`; PGN `0xE5` (8 section bitmask bytes → sections 1–64) and `isJobStarted` gating preserved. |
| F-029 | Coverage / worked-area mapping | At parity | `SourceCode/GPS/Classes/CFieldData.cs` + `RenderCoordinator`. |
| F-030 | Flags / markers | At parity | `SourceCode/GPS/Classes/CFlag.cs`. |
| F-031 | Vehicle configuration + brand presets | At parity | `SourceCode/GPS/Classes/CVehicle.cs`, `Brands.cs`; vehicle/brand textures sourced via Avalonia/Skia images feeding the OpenGL textures. |
| F-032 | Tool / implement configuration & geometry | At parity | `SourceCode/GPS/Classes/CTool.cs`. |
| F-033 | Machine / relay control (PGN `0xEF` / `0xEC` / `0xEE`) | At parity | `SourceCode/GPS/Services/SectionService.cs` / `PgnDispatcher.cs`; machine-data, relay-pin, and machine-config frames frozen. |
| F-034 | ISOBUS / Task Controller | At parity | `SourceCode/GPS/Classes/CISOBUS.cs`; ISOBUS heartbeat / process-data PGNs (`0xF0`…`0xF3`) preserved. |
| F-035 | ISOXML V3 / V4 import / export | At parity | `Dev4Agriculture.ISO11783.ISOXML 0.23.1.1` kept (cross-platform). Proof: `PARITY_REPORT.md` → `IsoXmlEquivalenceTests` (name ≤ 248 bytes; AB+Curve export limit). |

---

## Phase 4 — Data, Tools, UI & Peripherals (F-036 … F-045)

The data, tools, UI, and peripheral features migrate **At parity**, with the peripheral capabilities
that depend on Windows-only hardware APIs **Feature-gated per-OS** (monitor brightness, webcam) and a
single named sub-capability of AgShare **Deferred** per the AAP scope boundary.

| ID | Feature | Status | Cross-platform location / notes |
|---|---|---|---|
| F-036 | Settings system (split Vehicle / Tool / Environment) + `CSettingsMigration` | At parity | XML schema **frozen**; the Windows Registry / `%AppData%` backing (`docs/settings.md` L26-L28, L34-L36) is replaced by the `IPlatformServices` config root (Windows `%AppData%\AgOpenGPS`, Linux `~/.config/AgOpenGPS`, macOS `~/Library/Application Support/AgOpenGPS`); `CSettingsMigration` round-trip preserved with a one-time Registry read on Windows. Proof: `PARITY_REPORT.md` → `SettingsRoundTripTests`. |
| F-037 | AgShare field upload / download | At parity *(upload/download)* | `SourceCode/GPS/Classes/AgShare`; `AgShareEnabled=false` by default (`docs/settings.md` L422) preserved. **Note — the `GetPublicFieldsAsync` sub-capability remains Deferred:** it is explicitly out of scope per AAP §0.2.3 ("remains not implemented") and is **not** implemented as part of this migration. |
| F-038 | NMEA serial output (GPS_Out, 4-second timeout) | At parity | `SourceCode/GPS_Out` keeps `System.IO.Ports` with port-name enumeration abstracted via `IPlatformServices`; the 4-second NMEA-forwarding timeout is preserved. |
| F-039 | Simulators (in-app `CSim` + standalone `ModSim`) | At parity | `SourceCode/GPS/Classes/CSim.cs`; `ModSim` re-platformed onto an Avalonia shell (`MainSimView` + `App.axaml`). |
| F-040 | Diagnostics (AgDiag) | At parity | `SourceCode/AgDiag` re-platformed onto Avalonia. |
| F-041 | Day / night theming & display preferences | At parity | Avalonia Fluent theme + custom styles in `SourceCode/GPS/App.axaml`, with a day/night palette derived from the `FormGPS` colors. |
| F-042 | On-screen keypad / keyboard (touch) | At parity | `SourceCode/Keypad` `GenericKeypad` / `NumKeypad` / `Keyboard` reimplemented as Avalonia `UserControl`s (`Keyboard.axaml`, `NumKeypad.axaml`), shared by GPS + AgIO. |
| F-043 | Audio alerts / sounds (`CSound`) | At parity | `System.Media.SoundPlayer` (Windows-only) replaced with a cross-platform audio abstraction. |
| F-044 | Monitor brightness (`CBrightness` / WMI) | **Feature-gated (per-OS)** | **Windows:** full port (WMI relocated to `WindowsPlatformServices` under `net8.0-windows`). **Linux:** best-effort via sysfs `/sys/class/backlight`. **macOS:** gated / no-op. `CBrightness` already returns `-1` gracefully when no controllable display exists, so the no-op fallback is pre-existing and never breaks startup. |
| F-045 | Webcam (Accord DirectShow) | **Feature-gated (per-OS)** | **Windows:** keep-or-replace the Accord DirectShow capture. **Linux / macOS:** gated (DirectShow is Windows-only and abandoned). Default `isWebCamOn=false` (`docs/settings.md` L446) — the lowest-priority optional convenience; gating never affects core guidance. |

> **Note on steering/heading graphs.** The on-screen steering, heading, cross-track-error, and
> correction **graphs** were formerly drawn with the Windows-only
> `System.Windows.Forms.DataVisualization` charting control. They are **reimplemented via custom
> Avalonia drawing** (series / axes / zoom / autoscale + rolling-data buffer preserved) and therefore
> remain **At parity**. These graphs are visualization surfaces under the guidance features
> (F-015 / F-016) and diagnostics (F-040); the Windows-only `<Reference>` is removed from the GPS
> project. See `TRANSITION_MAP.md` → Windows-Coupled Classes.

---

## Summary & validation

**Status counts (F-001 … F-045):**

- **At parity:** the large majority — F-001 … F-020 and F-022 … F-043 (minus the gated entries),
  plus the upload/download portion of F-037 and the reimplemented steering/heading graphs.
- **Feature-gated (per-OS): 3** — **F-021** (background map imagery), **F-044** (monitor brightness),
  and **F-045** (webcam, gated off-Windows).
- **Deferred: 1 (a sub-capability)** — the **`GetPublicFieldsAsync`** sub-capability of **F-037**,
  explicitly out of scope per AAP §0.2.3.

Every feature in the catalog is therefore accounted for: nothing is added and nothing is removed, in
keeping with the **100% functional-parity** acceptance bar. The byte-equivalence and round-trip proof
behind each **At parity** claim is recorded in **`PARITY_REPORT.md`** (with the golden-file test names
and tri-OS CI-matrix results); the **file-by-file** old→new disposition for every artifact named above
is recorded in **`TRANSITION_MAP.md`**.

*This checklist is kept current as the migration proceeds. The feature identifier space
(F-001 … F-045) is the technical specification's §2.2 "Feature Catalog" (REFERENCE); the five
AAP-anchored identifiers (F-021, F-026, F-038, F-044, F-045) are fixed at their exact numbers. See
also `CHANGELOG.md` (narrative spine), `TRANSITION_MAP.md` (file-level mapping), `PARITY_REPORT.md`
(behavioral-parity proof and open risks), and `VALUE_SUMMARY.md` (executive brief).*
