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
Agent Action Plan (AAP). The five identifiers that the **AAP anchors explicitly are fixed at their
exact numbers** and are treated as authoritative pins for the surrounding numbering:

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

> **Current status (read first).** The migration is **integrated**: all twelve projects target
> `net8.0` (GPS and AgIO multi-target `net8.0;net8.0-windows`), the WinForms/WPF dependencies are
> removed, and the GPS solution **builds cleanly** in Debug **and** Release (`TreatWarningsAsErrors`)
> for every declared target — 0 errors and 0 warnings (including **0 `AVLN3001`** — the eight views that formerly emitted these
> advisories now carry the standard public parameterless constructor, and `MSBuildTreatWarningsAsErrors`
> promotes any such notice to an error). The extracted GPS `Services/` (`PositionService`, `PgnDispatcher`,
> `SectionService`, `FieldIoService`, `RenderCoordinator`), the `AvaloniaGeoViewport` OpenGL host, the
> ~88 Avalonia views, and the `GPS/Classes/**` algorithm recompile are **all on disk and compiled into
> the normal build**. The GPS shell is **functionally wired** — every operator command, the section
> controls, the field/guidance dialog navigation, the steer wizard, the field open/close lifecycle, and
> GPS↔AgIO auto-start/stop. **All five golden-file parity suites** (PGN, Guidance, ISOXML, Settings,
> **Field**) are committed and **enforcing**, and the full local Linux test run is **133 passed / 1
> skipped / 0 failed**. The remaining residuals are **external-evidence / follow-up items** —
> documented per feature below and consolidated in `PARITY_REPORT.md` → Open Risks.

## Status labels

Each feature below is assigned exactly one of the three AAP-mandated statuses:

- **At parity (local)** — The feature's behavior-frozen contract is **preserved**, its cross-platform
  code is **on disk, compiled, and integrated/wired**, and it is **green on the local Linux
  development environment** (golden-file / round-trip / unit evidence as applicable). The **single
  universal residual** is **tri-OS CI confirmation** — the `windows` / `ubuntu` / `macos` matrix
  workflow is on disk (`.github/workflows/build.yml`) but has **not yet been executed** on
  GitHub-hosted runners, an external-evidence item this offline environment cannot dispatch. Where a
  feature carries an *additional* residual beyond tri-OS CI (the GL on-hardware confirmation, or the
  latent guidance peer-wiring gap), that residual is named explicitly in the feature note and
  cross-referenced to the relevant `PARITY_REPORT.md` open risk. "At parity (local)" is the honest
  form of the AAP's **At parity** status: contract-preserved and locally proven, with cross-OS
  execution as the documented residual.
- **Feature-gated (per-OS)** — The feature is **full on some operating systems and gracefully degraded
  or no-op where no cross-platform equivalent exists**. Gating never breaks application startup or core
  guidance; the pre-existing no-op/degradation pattern is preserved.
- **Deferred** — An explicitly **out-of-scope** sub-capability per the AAP. (No *whole* feature is
  Deferred; the only Deferred item is the `GetPublicFieldsAsync` sub-capability within F-037, which AAP
  §0.2.3 holds out of scope.)

**Cross-references.** Behavioral-parity proof (byte-equivalence and round-trip golden results, the GL
risk, the vulnerability audit, and every open risk) is in **`PARITY_REPORT.md`** (on disk). The
**file-by-file** old→new disposition for each artifact named below is in **`TRANSITION_MAP.md`** (on
disk). The narrative spine is in **`CHANGELOG.md`** (on disk); the non-technical brief is in
**`VALUE_SUMMARY.md`** (on disk).

> All tables use four columns — **ID** | **Feature** | **Status** | **Cross-platform location /
> notes** — and are grouped under the product's functional areas.

---

## Phase 1 — Positioning & Connectivity (F-001 … F-010)

All ten positioning and connectivity features are integrated and locally green. The two-program model
is fully wired (GPS auto-starts/stops AgIO), the PGN fabric is golden-tested and length-hardened, and
the Core geo conversion is unit-covered.

| ID | Feature | Status | Cross-platform location / notes |
|---|---|---|---|
| F-001 | Two-program architecture (AgOpenGPS `FormGPS` + AgIO `FormLoop`, separate single-instance programs) | At parity (local) | GPS shell `SourceCode/GPS/App.axaml(.cs)` + `Views/MainView.axaml(.cs)` and the AgIO Avalonia app under `SourceCode/AgIO` are on disk and compiled. Two-program model + loopback fabric retained; **no GPS↔AgIO project reference** (UDP loopback only). GPS↔AgIO auto-start/stop wired (see F-003). See `TRANSITION_MAP.md` → UI Shell. |
| F-002 | UDP loopback PGN fabric (ports 15555 / 17777, additive-checksum CRC, header `0x80 0x81 0x7F`) | At parity (local) | AgIO `Source/Services/UdpLoopbackService.cs` + GPS `Services/PgnDispatcher.cs` on disk and compiled; frame format, CRC (sum of bytes 2..N-1), and ports frozen by contract and asserted by `PgnFrameGoldenTests` (green locally). Inbound length guards hardened (UDP handlers check `Length >= 4` before header/PGN indexing). |
| F-003 | AgIO auto-start / auto-stop by AgOpenGPS | At parity (local) | **Implemented in this remediation pass:** GPS `Program.cs` exposes `StartAgIO()`/`StopAgIO()` (safe `Process.Start` / terminate via `Environment.ProcessPath`), called from `App.axaml.cs` startup/`ShutdownRequested`, gated by `setDisplay_isAutoStartAgIO` / `setDisplay_isAutoOffAgIO` (matching the WinForms gating). AgIO `Restart()` preserved cross-platform. |
| F-004 | GPS position ingestion (PGN `0xD6`, 52 bytes) | At parity (local) | `SourceCode/GPS/Services/PgnDispatcher.cs` → `PositionService.cs` on disk and compiled; the 52-byte decode is frozen by contract and golden-covered. |
| F-005 | NTRIP / RTK client (AgIO) | At parity (local) | AgIO `Source/Services/NtripService.cs` on disk and compiled; **CR/LF header-injection hardened** (mountpoint validated against control/CR/LF chars before the request line — this remediation pass, SEC-1). |
| F-006 | External IMU + disconnect (PGN `0xD3` / `0xD4`) | At parity (local) | Handled by GPS `Services/PgnDispatcher.cs`; contract frozen, golden-covered. |
| F-007 | GPS / IMU heading & roll fusion (CAHRS) | At parity (local) | `SourceCode/GPS/Classes/CAHRS.cs` recompiled (behavior frozen) and driven per-fix by `PositionService`. **QA F5: the inline IMU+GPS fusion was extracted verbatim into the pure static `CAHRS.FuseImuGpsHeading(...)` (a behavior-preserving testable seam); `PositionService` delegates to it, and a production-invoking test certifies the fused heading against a production-captured golden.** |
| F-008 | Dual-antenna heading & reverse detection | At parity (local) | `PositionService` + `AgOpenGPS.Core` geo models on disk and compiled; behavior frozen. |
| F-009 | WGS84 ↔ local-plane conversion | At parity (local) | `AgOpenGPS.Core` geo conversion **covered by the Core test suite (33/33 pass locally)**; numeric I/O via `InvariantCulture`. |
| F-010 | Single-instance enforcement | At parity (local) | `IPlatformServices.TryAcquireSingleInstance` + `PlatformServicesFactory` in Core; GPS/AgIO `Platform/{Windows,Linux,Mac}PlatformServices.cs` on disk (Windows named `Mutex`; Linux/macOS lockfile + advisory lock, fail-closed); GUIDs `{516-0AC5-…6BDE8F}` (GPS) / `{8F6F0AC4-…6BDE8F}` (AgIO) preserved. App composition root now **reuses the single process-level `IPlatformServices`** created by `Program` (this remediation pass, APP-1). |

---

## Phase 2 — Guidance & Steering (F-011 … F-020)

The guidance/steering **mathematics are recompiled (behavior frozen) and certified locally by
production-invoking parity tests** (QA F5 remediation): `GuidanceEquivalenceTests` now invokes the
**real** `CGuidance.DoSteerAngleCalc` (Stanley), `CABLine.GetCurrentABLine` (Pure Pursuit) and the
production clamp; `GuidanceAlgorithmCoverageTests` invokes the **real** `CDubins`, `CSmartWAS`,
`CContour`, `CABCurve`, `CRecordedPath`, and `CYouTurn`; and `SectionControlParityTests` invokes the
**real** `SectionService` — all built through `ParityGraphFixture` (which constructs a live `CGuidance`
and wires the cyclic peers via `SetGuidanceReferences`) and asserted against **production-captured**
goldens (exact for pure math/counts/states; `Within(0.001)` for geometry). Logic was decoupled from the
`FormGPS` god-object via constructor injection; outputs are unchanged. **Beyond the universal tri-OS CI
residual, the guidance features carry one additional documented residual:** while the parity fixture now
constructs a live `CGuidance` and calls `SetGuidanceReferences(...)` (so the seam is production-proven and
NRE-free), the **application composition root** does not yet replicate that wiring — the *math and seam*
are proven, but the *live end-to-end* startup wiring is a latent gap tracked as `PARITY_REPORT.md` Open
Risk #8 (pre-existing; out of scope of the F5 parity findings).

| ID | Feature | Status | Cross-platform location / notes |
|---|---|---|---|
| F-011 | AB line guidance | At parity (local) | `SourceCode/GPS/Classes/CABLine.cs` recompiled (behavior frozen); UI wired from MainView. **Steer output certified by `GuidanceEquivalenceTests` invoking the REAL `CABLine.GetCurrentABLine` (production-captured golden).** Live-pipeline peer-wiring residual: `PARITY_REPORT.md` #8. |
| F-012 | AB curve guidance | At parity (local) | `SourceCode/GPS/Classes/CABCurve.cs` recompiled; UI wired. **Curve-offset geometry certified by `GuidanceAlgorithmCoverageTests` invoking the REAL `CABCurve.BuildNewOffsetList`.** Residual: #8. |
| F-013 | Contour guidance | At parity (local) | `SourceCode/GPS/Classes/CContour.cs` recompiled. **Steer output certified by `GuidanceAlgorithmCoverageTests` invoking the REAL `CContour.DistanceFromContourLine`.** Residual: #8. |
| F-014 | Track management (create / select / nudge / snap / cycle) | At parity (local) | `SourceCode/GPS/Classes/CTrack.cs` + `CTrackMethods` recompiled; AB-draw/build-tracks and track-cycle UI wired from MainView (this remediation pass). |
| F-015 | Pure Pursuit steering (`CTrackMethods.GoalPoint()`; production `steerAngleAB` via `Math.Atan` goal-point form, equivalent to documented `atan2(2·wheelbase·sin(error), lookahead)`) | At parity (local) | Frozen math; **certified by `GuidanceEquivalenceTests` invoking the REAL `CABLine.GetCurrentABLine` (green locally, production-captured golden)**. |
| F-016 | Stanley steering (`CGuidance.DoSteerAngleCalc()`) | At parity (local) | Frozen math in `SourceCode/GPS/Classes/CGuidance.cs`; safety guard `maxSteerAngle = 30°` enforced (`docs/settings.md` L54-L55) and **certified by `GuidanceEquivalenceTests` invoking the REAL `DoSteerAngleCalc` + clamp (green locally)**. `maxAngularVelocity = 0.64°/s` is a **nominal** guard — the rate-limiter is commented-out (inactive) in **both** net48 and migrated code (feeds only the compass "*" indicator). Residual: #8 (composition-root wiring only). |
| F-017 | AutoSteer output (PGN `0xFE`, 14 bytes) + module response (`0xFD`) | At parity (local) | `SourceCode/GPS/Services/PgnDispatcher.cs` on disk; 14-byte encode/decode frozen and golden-covered (`FE_autosteer.bin`). |
| F-018 | U-turn / YouTurn + Dubins paths | At parity (local) | `SourceCode/GPS/Classes/CYouTurn.cs`, `CDubins.cs` recompiled (behavior frozen). **Certified by `GuidanceAlgorithmCoverageTests` invoking the REAL `CDubins.GenerateDubins` (straight ≈30 m, U-turn ≈53.25 m, + determinism re-run) and `CYouTurn.DistanceFromYouTurnLine` (production-captured goldens).** |
| F-019 | Recorded path | At parity (local) | `SourceCode/GPS/Classes/CRecordedPath.cs` recompiled; `RecPath.txt` field golden enforced. **Drive-start certified by `GuidanceAlgorithmCoverageTests` invoking the REAL `CRecordedPath.StartDrivingRecordedPath`.** |
| F-020 | Steering-angle-sensor (WAS) calibration / steer wizard | At parity (local) | **Wired in this remediation pass:** the steer-wizard workflow is reachable from MainView (`OpenSteerWizard` routes through real adapters — `SteerWizAdapters.cs` / `SteerSettingsAdapters.cs`); `CSmartWAS(ApplicationModel)` injection done. **WAS statistics certified by `GuidanceAlgorithmCoverageTests` invoking the REAL `CSmartWAS` (Mean/Median/StdDev/RecommendedOffset over a 250-sample sequence).** The earlier "Not available yet" stub is removed. |

---

## Phase 3 — Field, Mapping & Sections (F-021 … F-035)

Field management, boundary/headland/tramline handling, and section control are **integrated and
wired**: the extracted GPS `Services/` (`FieldIoService`, `SectionService`, `RenderCoordinator`) and
the `GPS/Classes/**` recompile are on disk and compiled, the dialogs are reachable from the shell, the
manual section buttons are enabled and routed, and the **eleven field-file formats are golden-tested
(load→save byte-identical, 11/11 green locally)**. Background map imagery (F-021) is `Feature-gated`.

| ID | Feature | Status | Cross-platform location / notes |
|---|---|---|---|
| F-021 | Background map imagery (GMap online tiles) | **Feature-gated (per-OS)** | `GMap.NET.WinForms 2.1.7` removed; online imagery replaced/gated on **all** OSes. The SQLite tile cache stays cross-platform and the field renders correctly without imagery — gating never breaks startup or core guidance. |
| F-022 | Field create / open / save / close | At parity (local) | `SourceCode/GPS/Services/FieldIoService.cs` on disk; **close lifecycle hooks implemented in this remediation pass** (`onBeforeCloseField`/`onAfterCloseField` — section/contour stop, mapping-off, ISOBUS reset, job close, panel-disable, title). `Path.Combine` + `InvariantCulture` numeric I/O frozen; round-trip **proven by `FieldRoundTripTests` (11/11 green locally)**. |
| F-023 | Boundary & geofence | At parity (local) | `SourceCode/GPS/Classes/CFence.cs` / `CBoundary.cs` recompiled; `FormBoundaryView` + `FormBndToolView` + `FormBuildBoundaryFromTracksView` **wired from MainView Field-Tools menu** (this remediation pass); `Boundary.txt` golden enforced. |
| F-024 | Headland | At parity (local) | `SourceCode/GPS/Classes/CHead.cs` recompiled; `FormHeadAcheView` / `FormHeadLineView` wired from shell; `Headland.txt` / `Headlines.txt` goldens enforced. |
| F-025 | Tramlines | At parity (local) | `SourceCode/GPS/Classes/CTram.cs` recompiled; `FormTramLineView` wired from shell; `Tram.txt` golden enforced. |
| F-026 | Serial communications (GPS / IMU / steer, AgIO) | At parity (local) | `System.IO.Ports 9.0.0` (cross-platform NuGet) retained; AgIO `Source/Services/SerialCommService.cs` on disk; port-**name** enumeration (`COMx` vs `/dev/ttyUSB*` / `/dev/ttyACM*` / `/dev/cu.*`) abstracted via `IPlatformServices.GetSerialPortNames`. Behavior preserved; **on-hardware serial confirmation** is an external-evidence residual alongside tri-OS CI. |
| F-027 | Section control (manual + auto) | At parity (local) | `SourceCode/GPS/Services/SectionService.cs` on disk; **manual section buttons `btnSection1Man..16Man` enabled and routed to `SectionService` public APIs** (previously hard-disabled). **QA F5: `SectionControlParityTests` invokes the REAL `SectionService.BuildMachineByte` (1–16 unique-width + ≤64 same-width PGN `0xFE`/`0xEF`/`0xE5` bytes) and the REAL `DoRemoteSwitches` to certify the `isJobStarted` gate blocks activation outside an active job (production-captured goldens).** |
| F-028 | Multi-section / zone width (1–16 unique / up to 64 same-width via PGN `0xE5`) | At parity (local) | `SectionService` exposes section/zone state + click handlers; PGN `0xE5` (8 section-bitmask bytes → sections 1–64) frozen by contract and golden-covered (`E5_sections.bin`). |
| F-029 | Coverage / worked-area mapping | At parity (local) | `SourceCode/GPS/Classes/CFieldData.cs` recompiled + `RenderCoordinator` on disk. **Additional residual:** live coverage rendering uses the immediate-mode GL pipeline and `glReadPixels` back-buffer scan — shares the GL on-hardware confirmation residual (`PARITY_REPORT.md` #1; desktop-GL hook now wired). |
| F-030 | Flags / markers | At parity (local) | `SourceCode/GPS/Classes/CFlag.cs` recompiled; `Flags.txt` golden enforced (incl. `InvariantCulture` period-decimal assertion). **Additional residual:** flag-pick uses `glReadPixels` — GL on-hardware residual (#1). |
| F-031 | Vehicle configuration + brand presets | At parity (local) | `SourceCode/GPS/Classes/CVehicle.cs` (with `LoadSettings()` extracted this remediation pass), `Brands.cs` recompiled; vehicle/brand textures route via Avalonia/Skia images feeding the OpenGL textures. |
| F-032 | Tool / implement configuration & geometry | At parity (local) | `SourceCode/GPS/Classes/CTool.cs` recompiled; config views reachable from shell. |
| F-033 | Machine / relay control (PGN `0xEF` / `0xEC` / `0xEE`) | At parity (local) | `SectionService` / `PgnDispatcher` on disk; machine-data, relay-pin, and machine-config frames frozen and golden-covered (`EF_machine.bin`, `EC_relay.bin`, `EB_dims.bin`). |
| F-034 | ISOBUS / Task Controller | At parity (local) | `SourceCode/GPS/Classes/CISOBUS.cs` recompiled; ISOBUS heartbeat / process-data PGNs (`0xF0`…`0xF3`) frozen by contract. |
| F-035 | ISOXML V3 / V4 import / export | At parity (local) | `Dev4Agriculture.ISO11783.ISOXML 0.23.1.1` kept (cross-platform); equivalence (name ≤ 248 bytes; AB+Curve export limit) **proven by `IsoXmlEquivalenceTests` (green locally)**. The separate `IsoXmlExport_DrivenFromDomainGraph_RequiresFormGpsGraph` driver test is **intentionally skipped** (needs the full `FormGPS` object graph; accepted limitation — it is not a golden loader and not a field byte-contract test). |

---

## Phase 4 — Data, Tools, UI & Peripherals (F-036 … F-045)

Data, tools, UI, and peripheral features are integrated and locally green; the peripheral capabilities
that depend on Windows-only hardware APIs are **Feature-gated per-OS** (brightness, webcam). The
day/night theme tokens were consolidated in this remediation pass.

| ID | Feature | Status | Cross-platform location / notes |
|---|---|---|---|
| F-036 | Settings system (split Vehicle / Tool / Environment) + `CSettingsMigration` | At parity (local) | XML schema **frozen**; round-trip **proven by `SettingsRoundTripTests` + `AgLibrary.Tests` (green locally)**. Windows Registry / `%AppData%` backing replaced by the `IPlatformServices` config root (Windows `%AppData%\AgOpenGPS`, Linux `~/.config/AgOpenGPS`, macOS `~/Library/Application Support/AgOpenGPS`); `CSettingsMigration` one-time Windows Registry read preserved. AgIO `RegistrySettings.cs` `XDocument.Load` hardened (`DtdProcessing=Prohibit`, this remediation pass, SEC-6). |
| F-037 | AgShare field upload / download | At parity (local) | Migrated AgShare client on disk; `AgShareEnabled=false` by default (`docs/settings.md` L422) frozen by contract. **The `GetPublicFieldsAsync` sub-capability is Deferred — explicitly out of scope** per AAP §0.2.3 ("remains not implemented") and is **not** implemented as part of this migration. |
| F-038 | NMEA serial output (GPS_Out, 4-second timeout) | At parity (local) | `SourceCode/GPS_Out` on disk and **builds 0/0**; keeps `System.IO.Ports 9.0.0` with port-name enumeration abstracted via `IPlatformServices`; the 4-second NMEA-forwarding timeout preserved. On-hardware serial confirmation is an external-evidence residual alongside tri-OS CI. |
| F-039 | Simulators (in-app `CSim` + standalone `ModSim`) | At parity (local) | `ModSim` re-platformed onto an Avalonia shell (`MainSimView` + `App.axaml`), **builds 0/0**; in-app `SourceCode/GPS/Classes/CSim.cs` recompiled and integrated in the GPS build. |
| F-040 | Diagnostics (AgDiag) | At parity (local) | `SourceCode/AgDiag` re-platformed onto Avalonia; on disk and compiled. |
| F-041 | Day / night theming & display preferences | At parity (local) | Avalonia Fluent theme + custom day/night palette in `SourceCode/GPS/App.axaml` (derived from the `FormGPS` colors), consumed by the GPS views via `{DynamicResource}`. **MainView hardcoded colors consolidated onto the `Aog*` theme tokens in this remediation pass (MV-4)**, removing day/night drift; intentional legacy fixed-color exceptions documented inline. |
| F-042 | On-screen keypad / keyboard (touch) | At parity (local) | `SourceCode/Keypad` `GenericKeypad` / `NumKeypad` / `Keyboard` reimplemented as Avalonia `UserControl`s, shared by GPS + AgIO; the project **builds 0/0**. |
| F-043 | Audio alerts / sounds (`CSound`) | At parity (local) | Cross-platform `SourceCode/GPS/Classes/CSound.cs` on disk and compiled (replaces the Windows-only `System.Media.SoundPlayer`); `Resources/*.wav` packaged via `CopyToOutputDirectory`. |
| F-044 | Monitor brightness (`CBrightness` / WMI) | **Feature-gated (per-OS)** | **Windows:** full port (WMI relocated to `WindowsPlatformServices` under `net8.0-windows`). **Linux:** best-effort via sysfs `/sys/class/backlight`. **macOS:** gated / no-op. `CBrightness` already returns `-1` gracefully when no controllable display exists, so the no-op fallback is pre-existing and never breaks startup. On-hardware brightness confirmation is an external-evidence residual. |
| F-045 | Webcam (Accord DirectShow) | **Feature-gated (per-OS)** | **Windows:** keep-or-replace the Accord DirectShow capture. **Linux / macOS:** gated (DirectShow is Windows-only and abandoned). Default `isWebCamOn=false` (`docs/settings.md` L446) — the lowest-priority optional convenience; gating never affects core guidance. |

> **Note on steering/heading graphs.** The on-screen steering, heading, cross-track-error, and
> correction **graphs** were formerly drawn with the Windows-only
> `System.Windows.Forms.DataVisualization` charting control. They are **reimplemented via custom
> Avalonia drawing** (the `RollChart` / `XteChartControl` controls — series / axes / zoom / autoscale +
> rolling-data buffer preserved) and compiled into the GPS build. These graphs are visualization
> surfaces under the guidance features (F-015 / F-016) and diagnostics (F-040); the Windows-only
> `<Reference>` is removed from the GPS project. See `TRANSITION_MAP.md` → Windows-Coupled Classes.

---

## Summary & validation

**Status counts (F-001 … F-045):**

- **At parity (local): 42** — contract-preserved, integrated/wired, and green on the local Linux
  environment, with **tri-OS CI confirmation** as the single universal residual. Two subsets carry one
  *additional* documented residual: the guidance features **F-011 … F-016** (composition-root
  peer-wiring, `PARITY_REPORT.md` #8 — *math and seam are now production-certified by the F5
  production-invoking parity suites; only the application-startup wiring remains*), and **F-029 / F-030**
  plus the live field viewport (GL
  on-hardware confirmation, `PARITY_REPORT.md` #1 — *desktop-GL hook now wired*). Serial features
  **F-026 / F-038** additionally await on-hardware confirmation.
- **Feature-gated (per-OS): 3** — **F-021** (background map imagery), **F-044** (monitor brightness),
  **F-045** (webcam, gated off-Windows). Each degrades gracefully and never breaks startup or core
  guidance.
- **Deferred: 0 whole features** — the only Deferred item is the **`GetPublicFieldsAsync`**
  sub-capability within **F-037**, explicitly out of scope per AAP §0.2.3.

Every feature in the catalog is accounted for — nothing is added and nothing is removed, in keeping
with the **100% functional-parity** acceptance bar. The byte-equivalence and round-trip proof behind
each `At parity (local)` claim is recorded in **`PARITY_REPORT.md`** (golden-file suite names, the
local 133-passed/1-skipped test run, the GL risk, the vulnerability audit, and every open risk); the
**file-by-file** old→new disposition for every artifact named above is in **`TRANSITION_MAP.md`**; the
narrative spine is in **`CHANGELOG.md`**; the executive brief is in **`VALUE_SUMMARY.md`**.

**The remaining work to convert every `At parity (local)` to fully-verified `At parity`** is the
external-evidence set: execute the on-disk `windows`/`ubuntu`/`macos` CI matrix and record green
results; obtain on-hardware per-OS desktop-GL + `glReadPixels` confirmation; wire the guidance peer
references (`SetGuidanceReferences`) in the composition root; and re-run the dependency vulnerability
audit in network-enabled CI. None of these alter a behavior-frozen contract.

*This checklist reflects the integrated, post-remediation state. The feature identifier space
(F-001 … F-045) is the technical specification's §2.2 "Feature Catalog" (REFERENCE); the five
AAP-anchored identifiers (F-021, F-026, F-038, F-044, F-045) are fixed at their exact numbers.*
