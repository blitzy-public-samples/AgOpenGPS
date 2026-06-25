# Changelog — AgOpenGPS net48/WinForms → net8.0/Avalonia Migration

<!-- [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md -->

This is the single-source-of-truth narrative spine for the AgOpenGPS migration from
`.NET Framework 4.8` / Windows Forms (Windows-only) to `.NET 8/9` / Avalonia UI
(cross-platform: Windows, macOS, Linux), covering all twelve projects in
`SourceCode/AgOpenGPS.sln`. It is maintained **incrementally as the work proceeds**, not written
at the end (AAP §0.7.2). Entries are **reverse-chronological** (most recent checkpoint first) and
grouped by area: **Runtime/TFM · UI shell · Rendering · Platform services · Mapping · Serial ·
Settings · Build/CI · Docs**.

> **Provenance.** Every migration code change carries an inline `// [XPLAT]` (or `<!-- [XPLAT] -->`)
> comment pointing back to this narrative, per AAP §0.7.2 (R13). Commit messages alone are **not**
> sufficient provenance; this file plus `TRANSITION_MAP.md` are the authoritative record (R14).

> **Buildability contract.** The migration is executed across multiple checkpoints (CP) within the
> one solution. Because the runtime moniker was flipped to `net8.0` early (CP1) while the GPS UI is
> re-platformed later, the **GPS** project is **intentionally non-buildable from CP1 until its
> Avalonia conversion completes** (see *Build Sequencing & Temporary Non-Buildable State* under
> CP3). This is recorded honestly here rather than hidden. AgIO, AgOpenGPS.Core, AgLibrary, and the
> test projects build and their tests pass throughout.

---

## [CP4] — GPS WinForms Dialog Catalog Part 2 (Settings/Guidance/Inputs/Profiles/Field) + Charting Retirement — 2026-06-25

**Theme:** Complete the retirement of the **entire** legacy GPS Windows Forms UI catalog. The
**second and final** batch — **128 WinForms files (57,919 lines)** across **47 dialog/config
groups** — was deleted under `SourceCode/GPS/Forms/{Field,Guidance,Inputs,Profiles,Settings}/` over
six commits (`821e7b45`, `c32e2420`, `99b40cfd`, `d70988a8`, `38fb7dbb`, `deb53c67`). With this
checkpoint `SourceCode/GPS/Forms/` is **absent**, and `Forms/Settings/FormButtonsRightPanel.*` was
the **last legacy WinForms dialog** removed from the repository. File breakdown by family: Field 32,
Guidance 36, Inputs 6, Profiles 12, Settings/Config/Charting 42. Their cross-platform replacements
(Avalonia Views/ViewModels) are scheduled for later GPS checkpoints (CP4–CP9) and are recorded as
**`Deferred`** — `SourceCode/GPS/Views/` and `SourceCode/GPS/Services/` do not exist yet — each with
a named target file and a frozen-behavior contract in `TRANSITION_MAP.md` (new section *GPS — Dialog
Catalog Part 2 (CP4)*). `// [XPLAT]` provenance is recorded for every deletion in that map and for
the `csproj` changes below (R13/R14).

### UI shell — dialog families removed (Avalonia Views deferred)
Removed and recorded as `Deferred` → named Avalonia Views in `TRANSITION_MAP.md`:
- **Field dialogs — Part 2 (11 groups):** `FormCopyTracks`, `FormEasyDrive`, `FormEnterFlag`,
  `FormFieldData`, `FormFieldDir`, `FormFieldExisting`, `FormFieldISOXML`, `FormFieldKML`,
  `FormFlags`, `FormJob`, `FormSaveOrNot`. (`FormSaveOrNot` was attributed to CP3 in the prior map
  draft; its direct replacement row is **consolidated** into the CP4 Field Part 2 section.)
- **Guidance dialogs (12 groups):** `FormABDraw`, `FormBuildTracks`, `FormGrid`, `FormHeadAche`,
  `FormHeadLine`, `FormNudge`, `FormQuickAB`, `FormRecordName`, `FormRefNudge`, `FormSmoothAB`,
  `FormTram`, `FormTramLine`.
- **Input dialogs (2 groups):** `FormKeyboard`, `FormNumeric` (GPS on-screen keyboard/numeric keypad,
  hosting the already-migrated Avalonia `Keypad.Keyboard`/`Keypad.NumKeypad` controls).
- **Profile dialogs (4 groups):** `FormConvertProfiles`, `FormLoadProfile`, `FormLoadVehicleTool`,
  `FormNewProfile`.
- **Settings dialogs (9 groups):** `FormAllSettings`, `FormButtonsRightPanel` (final dialog),
  `FormColor`, `FormColorSection`, `FormConfig` (the config shell), `FormCorrection`, `FormSimCoords`,
  `FormSteer`, `FormSteerWiz`.
- **Config controls — Part 2 (6 groups):** the `ConfigData`/`ConfigHelp`/`ConfigMenu`/`ConfigModule`/
  `ConfigTool`/`ConfigVehicle` `.Designer.cs` partial tab-panels of `FormConfig` → Avalonia
  `Config*View` user controls composed by `FormConfigView`.

### Charting — Windows-only `DataVisualization` retired (R10)
- The three live-graph dialogs `FormGraphSteer`, `FormGraphXTE`, and `FormGraphHeading` (which used
  the **Windows-only** `System.Windows.Forms.DataVisualization` control) were deleted. Their
  replacement is a **cross-platform Avalonia charting control** (custom Avalonia drawing or a
  selected cross-platform chart package) preserving series, axes, zoom/autoscale, and the rolling
  data buffer — **`Deferred`**, recorded in `TRANSITION_MAP.md` *GPS — Charting Dialogs (CP4)*.
- Because no consumer remains, the Windows-only `<Reference Include="System.Windows.Forms.DataVisualization" />`
  is **removed** from `SourceCode/GPS/AgOpenGPS.csproj` (see Build/CI below).

### Settings / Guidance / Inputs / Profiles safety & validation — destinations recorded (no silent loss)
Deletion removed *visible* validation/safety controls; to prevent silent loss (AAP §0.7.1 R1) each is
mapped to its destination ViewModel/service and a parity test in `TRANSITION_MAP.md` *GPS — Security
& Safety Control Destinations (CP4)*:
- **Minimum button-count** (`FormButtonsRightPanel`: block save when `buttonOrder.Count < 2`,
  `"Not Enough Buttons Added"`); **numeric min/max/clamp** (`FormNumeric`: out-of-range red highlight
  + clamp, `InvariantCulture`); **steering safety bounds** (`FormSteer`/`FormSteerWiz`:
  `maxSteerAngle=30°`, `maxAngularVelocity=0.64°/s`, PWM/pulse/snap limits, steer-config PGN `p_252`).
- **Profile filename sanitization + duplicate + job-state guards** (`FormNewProfile`/
  `FormConvertProfiles`/`FormLoadVehicleTool`: `InvalidFileRegex` + `glm.fileRegex`, `File.Exists`
  duplicate block, `isJobStarted` close-gate, `Directory.Exists`); **guidance invalid-line/distance
  guards** (`FormHeadAche` "Start = End", `FormHeadLine` "Nothing to Move", `FormTramLine` spacing);
  **flag-entry format guard** (`FormEnterFlag`: `DeduplicateFlags`, `"Invalid line"`); **color parse**
  (`FormColor`/`FormColorSection`: `int.Parse(InvariantCulture)` + ARGB); **ISOXML import/export**
  (`FormFieldISOXML` → `FieldIoService`: V3/V4 semantics, name ≤248 bytes, AB+Curve export limit).

### UI / Design / Accessibility parity — tracked for the deferred Views
- `TRANSITION_MAP.md` gains a *GPS — Dialog Catalog Part 2 UI / Design / Accessibility Parity (CP4)*
  section recording, for every replacement View: use of the existing `GPS/App.axaml` Fluent theme +
  day/night `Aog*` brush resources via `{DynamicResource}` (no hard-coded colors), keyboard/focus
  order (Enter/Esc, on-screen keypad), kiosk touch sizing, disabled/hover/pressed/focus states, and
  accessibility — with 1:1 parity to the current Windows Forms UI as the acceptance bar (AAP §0.3.3;
  no Figma supplied).

### Build/CI — project metadata aligned to the deletion state
- `SourceCode/GPS/AgOpenGPS.csproj`: **removed** the Windows-only
  `<Reference Include="System.Windows.Forms.DataVisualization" />` (no consumer after the charting
  deletions; R10) and the **six** now-orphaned `Forms\Settings\Config*.Designer.cs` `<Compile Update>`
  entries (`ConfigData`/`ConfigHelp`/`ConfigMenu`/`ConfigModule`/`ConfigTool`/`ConfigVehicle`, which
  were `DependentUpon` the **CP4-deleted** `FormConfig.cs`), replacing both with `[XPLAT]` explanatory
  comments. The `Resources`/`BrandImages` resource metadata are intentionally **preserved**. Verified:
  csproj XML well-formed; the GPS build still fails only at the pre-existing SDK target-resolution
  stage (see below) with **no new errors** and **0 source-compile (`CS####`) errors**.

### Docs
- `TRANSITION_MAP.md` updated to the authoritative CP4 record: 47 direct old→new replacement rows,
  the charting/safety/UI-parity sections above, and reconciliation of the now-stale CP3
  retained-reference notes (six former retained callers were themselves deleted in CP4, so their
  dangling references are *resolved by deletion*; only `Program.cs` and `RegistrySettings.cs` remain
  genuinely retained).

### ⚠ Build Sequencing & Temporary Non-Buildable State (unchanged from CP3)
- The **GPS** project remains **intentionally non-buildable** at CP4. CP4 deletes only dialog sources
  and updates documentation + the GPS `csproj` charting/metadata; it does **not** touch the
  `UseWindowsForms=true` / `ImportWindowsDesktopTargets=true` / TFM blocker. The build therefore still
  fails **identically** to CP3 — **NETSDK1100** without `EnableWindowsTargeting`, **NETSDK1136** with
  it — at SDK target-resolution, **before any source is compiled**. The remaining dangling references
  to deleted types (`Program.cs`, `Properties/RegistrySettings.cs`) and the 19 GPS `Classes/*` still
  coupled to `FormGPS` are **not yet reachable as compiler errors** and are resolved as the GPS UI is
  re-platformed (CP4–CP9: convert `AgOpenGPS.csproj` to the Avalonia stack, rewrite `Program.cs`, and
  create the `Views/`/`Services/` trees). AgIO, AgOpenGPS.Core, AgLibrary, and the test projects build
  and their tests pass throughout.

---

## [CP3] — GPS WinForms Shell, FormGPS Partials & Dialog Catalog Part 1 — 2026-06-25

**Theme:** Retire the GPS Windows Forms UI surface so the GPS project can be de-Windows-ified and
re-platformed onto Avalonia. **91 WinForms files (35,162 lines) were deleted** under
`SourceCode/GPS/Forms/` across four commits (`8247a393`, `3ddd2128`, `acd263cb`, `ef18f49d`).
Their cross-platform replacements (Avalonia Views/ViewModels and extracted Services) are scheduled
for later GPS checkpoints (CP4–CP9) and are recorded as **`Deferred`** with named target files and
frozen-behavior contracts in `TRANSITION_MAP.md`.

### UI shell — removed (Avalonia `MainView`/`App` deferred)
- Removed the main kiosk shell `FormGPS.cs` / `FormGPS.Designer.cs` / `FormGPS.resx`. Its Avalonia
  replacement (`App.axaml(.cs)` + `Views/MainView.axaml(.cs)` bound to the Core view-models) is
  **Deferred** — `SourceCode/GPS/Views/` does not exist yet. The shell-coordination ("god-object")
  role is to be replaced by view-models + a thin application controller (AAP §0.3.2).
- Removed the non-visual shell composition partials `GUI.Designer.cs` and `Controls.Designer.cs`
  (panel/text/day-night/viewport wiring and command-button/menu/event handlers). Their role moves
  to `MainView` composition.

### FormGPS partial extraction sources — removed (Services deferred, behavior frozen)
The real-time domain pipeline that lived inside `FormGPS` partial classes was removed; its logic is
to be lifted into plain injectable `GPS/Services/*` classes (AAP §0.6.1, Extract Class / Move
Method), with each service **parity-tested** against the WinForms baseline:
- `Position.designer.cs` → **`PositionService`** — `UpdateFixPosition()` scan loop, CAHRS
  heading/roll fusion, WGS84→local-plane conversion, the 1000 ms RTK-recovery debounce, and
  `CalculateSectionLookAhead`. Must also re-establish the **CP2 Smart WAS live-state sync** into
  `ApplicationModel` before `CSmartWAS.AddSample(...)` (otherwise sampling receives default state).
- `UDPComm.Designer.cs` + `PGN.Designer.cs` → **`PgnDispatcher`** — the UDP receive path and the
  PGN encode/decode/CRC contract (see Platform/Security below).
- `Sections.Designer.cs` → **`SectionService`** — section/zone manual-auto logic and the machine
  byte (PGN `0xE5`/`0xEF`).
- `SaveOpen.Designer.cs` → **`FieldIoService`** — field/ISOXML/KML/AgShare file I/O.
- `OpenGL.Designer.cs` → **`RenderCoordinator`** — projection/frustum/back-buffer scan/overlays
  (`frustum[24]`, `CalcFrustum`, `glReadPixels` section lookahead).

### Rendering / Security & safety controls — destinations recorded (no silent loss)
Deletion removed *visible* safety/security controls. To prevent silent loss in reimplementation,
each control's destination and frozen contract is recorded in `TRANSITION_MAP.md`:
- **PGN transport** (`UDPComm.Designer.cs`): inbound header validation `data[0]==0x80 && data[1]==0x81`,
  additive-checksum CRC (`CK_A += data[j]` for `j=2..Length`, reject on mismatch), the 70 ms
  `udpWatchLimit` throttle, and loopback endpoints (bind `127.0.0.1:15555`, peer `:17777`) → must be
  preserved byte-for-byte in **`PgnDispatcher`** (AAP §0.7.1 R1/R2; `docs/pgn-protocol.md`).
- **PGN frames** (`PGN.Designer.cs`): the `CPGN_*` frame classes (`0xD0`/`0xFE`/`0xFD`/`0xFC`/`0xFB`…)
  with header `0x80 0x81 0x7F` and CRC trailer → preserved in the dispatcher/protocol model.
- **Field/file validation** (`SaveOpen.Designer.cs`): `Path.Combine` on `RegistrySettings.fieldsDirectory`,
  `Directory.Exists`/`File.Exists` guards, and the `isJobStarted` gate → preserved in **`FieldIoService`**.
- **Input sanitization**: `FormInputDialog.cs` filename regex (`glm.fileRegex`) and `Form_Keys.cs`
  hotkey character whitelist (`[^0-9a-zA-Z]`) → reimplemented in the Avalonia input view-models.
- **AgShare gating**: `FormAgShareDownloader.cs` `isJobStarted` close-gate and `FormAgShareUploader.cs`
  duplicate-name handling/credential flow → preserved in the AgShare views and `AgShareClient`
  (AgShare remains disabled by default, `AgShareEnabled=false`, AAP §0.7.2).

### Dialog catalog Part 1 — removed (Avalonia Views deferred)
Removed and recorded as `Deferred` → planned Avalonia Views in `TRANSITION_MAP.md`:
- **Root dialogs (41 files):** `FormAgShareSettings`, `FormDialog`, `FormEventViewer`, `FormGPSData`,
  `FormHelp`, `FormInputDialog`, `FormPan`, `FormSaving`, `FormShiftPos`, `FormTermsAndConditions`,
  `FormTimedMessage`, `FormWebCam` (feature-gate, F-045), `FormYes`, `Form_Keys`.
- **Config controls (6 files):** `ConfigSummaryControl`, `ConfigVehicleControl` (→ `FormConfig`
  composition).
- **Pickers (12 files):** `FormColorPicker` (→ Avalonia ColorPicker), `FormDrivePicker`,
  `FormFilePicker`, `FormRecordPicker`.
- **Field dialogs (21 files):** `FormAgShareDownloader`, `FormAgShareUploader`, `FormBndTool`,
  `FormBoundary`, `FormBoundaryPlayer`, `FormBuildBoundaryFromTracks`, `FormMap` (GMap → replace/gate, F-021).

### Build/CI — project metadata aligned to the deletion state
- `SourceCode/GPS/AgOpenGPS.csproj`: removed the eight stale `<Compile Update>` entries that were
  `DependentUpon` the now-deleted `FormGPS.cs` (`Controls`, `GUI`, `Position`, `SaveOpen`, `OpenGL`,
  `PGN`, `Sections`, `UDPComm` `.Designer.cs`), replacing them with an `[XPLAT]` explanatory comment.
  The `Forms\Settings\Config*.Designer.cs` entries (`DependentUpon FormConfig.cs`, retained CP4 file)
  and the `Resources`/`BrandImages` resource metadata are intentionally **preserved**. Verified:
  csproj XML well-formed; build emits **0 warnings** and the unchanged pre-existing target-resolution
  error (see below).

### Cross-platform case-sensitivity (AAP G7)
- The lower-cased `Position.designer.cs` and `FormYes.designer.cs`, and the mis-cased
  `FormtimedMessage.resx` (vs `FormTimedMessage.cs`), were removed as part of the deletion set —
  eliminating the case-collision hazards on Linux/macOS filesystems.

### ⚠ Build Sequencing & Temporary Non-Buildable State (F8 — read this)
The **GPS** project does **not** build at CP3, by design of the checkpoint sequencing:
- `dotnet build SourceCode/GPS/AgOpenGPS.csproj` on Linux fails with **NETSDK1100** (a
  Windows-targeted project needs `EnableWindowsTargeting=true` off-Windows); adding that flag then
  fails with **NETSDK1136** (a project using Windows Forms/WPF needs a `-windows` target framework).
- **Root cause:** the runtime moniker was flipped to `net8.0` in CP1 (`Directory.Build.props`), but
  the GPS project still declares `UseWindowsForms=true` / `ImportWindowsDesktopTargets=true` and
  references WinForms-coupled packages (`OpenTK.GLControl`, `GMap.NET.WinForms`,
  `MechanikaDesign.WinForms.UI.ColorPicker`, `System.Windows.Forms.DataVisualization`). It fails at
  the SDK target-resolution stage, **before** any source is compiled.
- **Consequence:** the retained dangling references to deleted CP3 types (in `Program.cs`,
  `Properties/RegistrySettings.cs`, the retained CP4 forms `FormJob`/`FormColor`/`FormColorSection`/
  `FormLoadVehicleTool`/`FormLoadProfile`/`FormNewProfile`/`FormConfig`) and the 19 GPS `Classes/*`
  that still type-depend on `FormGPS` (the `mf` god-object) are **not yet reachable as compiler
  errors**; they will surface only once the WinForms/TFM blocker is removed.
- **Resolution plan:** these are resolved as the GPS UI is re-platformed (CP4–CP9): convert
  `AgOpenGPS.csproj` to the Avalonia stack (remove `UseWindowsForms`, add Avalonia packages + RIDs,
  drop polyfills, replace GMap/ColorPicker/GLControl), rewrite `Program.cs` to the Avalonia bootstrap
  + `IPlatformServices` single-instance, create the `Views/` and `Services/` trees, and migrate the
  retained callers + GPS classes off `FormGPS` (constructor/service injection of `ApplicationModel`).
  Each deleted file's destination and frozen behavior is tracked in `TRANSITION_MAP.md`.

---

## [CP2] — AgIO Comms-Hub Re-platform + GPS Low-Level + WinForms GL Host Retired — 2026-06-25

**Theme:** Re-platform the AgIO program onto Avalonia and de-Windows-ify the GPS low-level layer.
(`79124198` plus `22dd1306`, `836094fd`, `18ea5fdd`, `930611c6`.)

### UI shell (AgIO)
- `FormLoop.cs` / `FormLoop.Designer.cs` / `FormLoop.resx` **reimplemented** as Avalonia
  `Views/MainWindow.axaml(.cs)` + `App.axaml(.cs)`; the `MainWindow` code-behind is the comms-hub
  composition root (mirroring the former `FormLoop` ownership of collaborators and the scan timer).
- Five touch dialogs reimplemented on Avalonia (`FormYes`, `FormTimedMessage`, `FormKeyboard`,
  `FormNumeric`, `FormPGN` → `Form*View.axaml` + view-models). The complex AgIO configuration
  dialogs were removed and their Avalonia replacements recorded as `Deferred`.

### Serial / Platform services (AgIO)
- Extracted the non-visual transport/parse logic from `FormLoop` partials into injectable services:
  `UdpLoopbackService`, `NmeaService` (UDP transport now a required ctor dependency, field-count
  guards, sanitized checksum logging), `SerialCommService` (six port roles; `GetAvailablePortNames()`
  enumeration seam; `InvariantCulture` parsing), `NtripService`. The frozen PGN/socket/NMEA contracts
  (header `0x80 0x81 0x7F`; NMEA PGN `0xD6` source byte `0x7C`; additive CRC; ports 15555/17777;
  module scan `255.255.255.255:8888` / bind `:9999`) are preserved byte-for-byte.

### Settings (AgIO)
- `AgIO Properties/RegistrySettings.cs`: Windows-Registry backing replaced with a cross-platform XML
  store at `<ApplicationData>/AgOpenGPS/registry.xml`; **schema/keys preserved** (AAP G5).
- `AgIO App.config` removed (`net48` startup section; case-sensitive-filesystem hazard).

### Rendering (GPS)
- Retired the WinForms `OpenTK.GLControl`-hosted `GPS/WinForms/GeoViewport.cs`. The cross-platform
  `AvaloniaGeoViewport : GeoViewportBase` over `OpenGlControlBase` is **Deferred to CP9**. The Core
  `GeoViewportBase`/`GLW` DrawLib is unchanged — only the host adapter changes.

### GPS low-level (behavior frozen)
- `CSmartWAS` constructor migrated `CSmartWAS(FormGPS)` → `CSmartWAS(ApplicationModel)`; the live
  gating state is synchronized into `ApplicationModel` (from `Position.designer.cs`) before each
  `AddSample()` so sampling parity is preserved. (The Position-side sync is re-homed to
  `PositionService` in CP3+ — see CP3.)
- `BrandImages.resx` restored byte-for-byte to keep `Brands.cs`/csproj consumers building; the
  GPS brand-image → Avalonia `avares://` migration is deferred.

---

## [CP1] — Runtime/TFM Flip + Core De-Windows-ification + Foundations — 2026-06-25

**Theme:** Establish the cross-platform runtime baseline and the migration documentation. (`f549b00f`.)

### Runtime/TFM
- `SourceCode/Directory.Build.props`: `<TargetFramework>` flipped **`net48` → `net8.0`** (analyzers
  `EnableNETAnalyzers`/`EnforceCodeStyleInBuild` retained; `Release` keeps `TreatWarningsAsErrors`).
  The `.NET 9 SDK 9.0.300` toolchain (`global.json`) is retained.

### Core
- `AgOpenGPS.Core`: purged WPF/GDI+ (`PresentationCore`) types so the shared domain library is truly
  portable; Avalonia code-behind foundations introduced.

### Docs
- `MIGRATION_DOCS/TRANSITION_MAP.md` introduced as the file-by-file mapping with an explicit
  accuracy contract (a row may claim a replacement exists only when it is on disk; later-checkpoint
  work is marked `Deferred`).

---

## Conventions

- **Disposition vocabulary** (shared with `TRANSITION_MAP.md`): `Migrated` (minimal-change recompile /
  de-Windowsed) · `Reimplemented` (rebuilt on Avalonia, 1:1 parity) · `Extracted` (non-visual logic
  lifted into an injectable service) · `Feature-gated` (Windows-only capability, graceful no-op
  elsewhere) · `Deferred` (WinForms code removed; replacement scheduled for a later checkpoint) ·
  `Deleted`/`Unchanged`.
- **Parity is proven** via golden-file tests (PGN byte-equivalence, field-file round-trip, ISOXML
  V3/V4 equivalence, settings XML round-trip + `CSettingsMigration`, guidance/steering output
  equivalence) on the `windows`/`ubuntu`/`macos` CI matrix; status is tracked in `PARITY_REPORT.md`
  and feature coverage in `FEATURE_TRACEABILITY.md` (authored at the parity/CI checkpoint).

*See also `TRANSITION_MAP.md` (file-by-file old→new mapping). This changelog and the transition map
are kept current at every checkpoint boundary (R14).*
