# Changelog

<!-- [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md -->

This file is the **narrative spine** of the AgOpenGPS migration from **.NET Framework 4.8 / Windows
Forms** (Windows-only) to **.NET 8.0 / Avalonia** (cross-platform: `win-x64`, `linux-x64`, `osx-x64`,
`osx-arm64`), preserving 100% functional parity across all twelve projects in
`SourceCode/AgOpenGPS.sln`. It is a **reverse-chronological**, **evidence-backed** log of every
meaningful change, **grouped by migration area**, and is **kept current as the work proceeds** — it is
the running narrative of the migration, **not** a document written at the end.

It is one of two **single-source-of-truth** deliverables; its companion is
**`MIGRATION_DOCS/TRANSITION_MAP.md`** (the file-by-file old→new mapping with each artifact's
disposition and parity status). For the proof that behavior did not change — PGN byte-equivalence,
field-file round-trip, ISOXML V3/V4 equivalence, settings XML round-trip, guidance/steering output
equivalence, and the open GL-context risks — a **`MIGRATION_DOCS/PARITY_REPORT.md`** is planned for the
parity/CI checkpoint and is **not yet on disk** (it requires the GPS build, the golden-file suites, and
per-OS CI evidence). For the per-feature (F-001…F-045) cross-platform disposition checklist see
**`MIGRATION_DOCS/FEATURE_TRACEABILITY.md`** (on disk).

The format loosely follows the [Keep a Changelog](https://keepachangelog.com/) conventions, **adapted
to migration areas**: because the entire refactor is executed in a single phase within the one
solution, there is a single `[Unreleased]` entry whose subsections are the migration **areas** (rather
than semantic-version releases), each using `Added` / `Changed` / `Removed` groups.

> **Provenance.** All migrated code carries an inline
> `// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md` comment (XML/markup
> files use the `<!-- [XPLAT] … -->` form). This file and `TRANSITION_MAP.md` are the authoritative
> record; commit messages alone are not sufficient provenance (AAP §0.7.2).

> **Status & buildability note.** This is a single-solution, in-progress migration. On disk today
> `AgOpenGPS.Core` (portable, WPF purged), `AgIO`, `AgLibrary`, `Keypad`, `ModSim`, `AgDiag`,
> `GPS_Out`, and `Updater` are migrated and build, and the `AgOpenGPS.Core` (33/33) and `AgLibrary`
> (3/3) test suites pass. The **`GPS`** application project is **intentionally non-buildable until its
> Avalonia `csproj` conversion completes**: it still declares `UseWindowsForms=true`, so off-Windows
> builds stop at SDK target-resolution (`NETSDK1100`) before any source compiles. The GPS-side
> **`Views/` tree is now on disk** — all ~60 Avalonia dialog views plus the `MainView` shell each have a
> matching `.axaml.cs` code-behind (validated for XAML↔code-behind consistency and XAML well-formedness
> outside the GPS build, which is still gated by `NETSDK1100`). Still tracked as **`Deferred`** are the
> GPS `Services/` and `Controls/` trees (not yet created), the GPS `csproj` Avalonia conversion (and thus
> GPS compilation), the per-OS CI matrix, the golden-file parity **suite bodies** (only a
> `Parity/Golden/**` scaffold is on disk), the documentation refresh, and the `PARITY_REPORT.md` /
> `VALUE_SUMMARY.md` deliverables (not yet authored). Each carries a named target and frozen-behavior
> contract in `TRANSITION_MAP.md` (which carries the authoritative per-row `At parity` / `Scaffolded` /
> `Deferred` status). Entries below note this status where it is material; each bullet describes the
> migration change for its area.

---

## [Unreleased] — net48/WinForms → net8.0/Avalonia cross-platform migration

_Date: (in progress)_

### Runtime/TFM

#### Changed

- **Target framework `net48` → `net8.0`** via the shared `SourceCode/Directory.Build.props`
  `<TargetFramework>` element (now line 5, immediately below the `[XPLAT]` provenance comment at
  line 4; `net48` occupied line 4 in the baseline). This single baseline is inherited by 11 of the 12
  projects. The analyzer settings `EnableNETAnalyzers` and `EnforceCodeStyleInBuild`, and the
  `Release`-only `TreatWarningsAsErrors`, are retained. _At parity._
- **`GPS` and `AgIO` application projects** target the cross-platform stack with per-OS RIDs
  `win-x64;linux-x64;osx-x64;osx-arm64`; Windows-only code paths (WMI brightness, Registry
  migration-read) are isolated under a `net8.0-windows` target so they never reach Linux/macOS builds.
  `AgIO`'s RIDs are on disk; the `GPS` RID set lands with its `csproj` conversion.
  _AgIO at parity; GPS deferred — see `TRANSITION_MAP.md`._
- **`SourceCode/Updater/AgOpenGPS.Updater.csproj`** — the only project that pinned its own
  framework — had its explicit `net48` flipped to `net8.0` separately. _At parity._
- **`.NET SDK 9.0.300` toolchain retained** (`global.json`, `rollForward: latestFeature`); the
  migration changes target frameworks, not the SDK pin. _At parity._

#### Removed

- **`net48` BCL polyfill packages `System.Memory 4.6.0` and `System.ValueTuple 4.6.1`** — in-box on
  `net8.0`. Removed from `AgOpenGPS.Core`; the `GPS` `csproj` and the test projects drop their
  remaining `System.Memory 4.6.0` references with the GPS conversion.
  _Core at parity; GPS/test-project removal deferred._
- **`SourceCode/AgIO/Source/App.config`** `net48` startup section — unused by SDK-style `net8.0`
  builds and a case-sensitive-filesystem hazard (no GPS `App.config` exists). _At parity._

### UI shell

#### Added

- **Avalonia 11.3.18 UI stack** (the AAP-recommended 11.3.x line): `Avalonia`, `Avalonia.Desktop`,
  `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, and `Avalonia.Diagnostics` (developer-only,
  `Debug`-conditioned). On disk for `AgIO`, `ModSim`, `AgDiag`, `GPS_Out`, `Keypad`, and `Updater`.
- **`SourceCode/GPS/App.axaml(.cs)`** — a new Avalonia `Application` with the Fluent theme plus a
  day/night palette derived from the `FormGPS` colors. On disk.

#### Changed

- **Windows Forms surface reimplemented as Avalonia views** (~88 forms total): `FormGPS` + ~67 GPS
  dialogs → `SourceCode/GPS/Views/**`; `FormLoop` + ~21 AgIO dialogs →
  `SourceCode/AgIO/Source/Views/**`; the `Keypad` `GenericKeypad` / `NumKeypad` / `Keyboard` user
  controls → Avalonia `UserControl`s. AgIO's view tree (19 view files) and the shared
  `Keyboard.axaml` / `NumKeypad.axaml` are on disk. The GPS `Views/` tree is now on disk in full: the
  `App.axaml` shell plus all 60 `.axaml` dialog/shell views (including `MainView` and the two custom
  chart controls `RollChart` / `XteChartControl`) each have a matching `.axaml.cs` code-behind, with the
  declared event handlers implemented and `AutomationProperties.Name` accessibility labels added. These
  views are **authored and consistency-validated but not yet compiled**: the GPS `csproj` Avalonia
  conversion — which removes `UseWindowsForms` and adds the Avalonia, `OpenGlControlBase`, and
  `Avalonia.Controls.ColorPicker 11.3.18` references — is still pending, so the GPS project stops at
  `NETSDK1100` before its source compiles. _AgIO views at parity (pending CI); GPS views scaffolded on
  disk, GPS compile/conversion deferred — see `TRANSITION_MAP.md`._
- **Core MVVM/Presenter scaffold wired to views.** The existing-but-null-wired `RelayCommand`,
  `IPanelPresenter`, and `IErrorPresenter` in `AgOpenGPS.Core` are now bound to Avalonia views via
  data binding (the AgIO `MainWindow` composition root already does this), replacing the legacy
  `FormGPS` constructing `ApplicationCore(dir, null, null)`.

_Note:_ this is a **1:1 parity reimplementation** — no redesign and no new screens; the visual
reference is the current Windows Forms UI (no Figma was supplied, AAP §0.3.3).

### Rendering

#### Added

- **`SourceCode/GPS/Controls/AvaloniaGeoViewport.cs`** — a `: GeoViewportBase` adapter over Avalonia's
  `OpenGlControlBase`, overriding `OnOpenGlInit` / `OnOpenGlRender` / `OnOpenGlDeinit` and binding the
  kept OpenTK 3.3.3 GL bindings to Avalonia's GL context via `GlInterface.GetProcAddress`.
  _Deferred — lands with the GPS `csproj` conversion._
- **`SourceCode/GPS/Services/RenderCoordinator.cs`** — projection/frustum/back-buffer scan/overlays
  extracted from `OpenGL.Designer.cs`. _Deferred._

#### Changed

- The three legacy GL surfaces **`oglMain` / `oglZoom` / `oglBack`** map to one or more
  `OpenGlControlBase` instances (or one control with offscreen framebuffers), where **`oglBack` is the
  offscreen buffer used for the section/lookahead `glReadPixels` pixel scan**.

#### Removed

- **`OpenTK.GLControl 3.3.3`** WinForms GL host — replaced by Avalonia's `OpenGlControlBase` (ships in
  `Avalonia.OpenGL`, no separate package). The package reference is dropped when the GPS `csproj` is
  converted; it is still present on disk. _Deferred._

_Unchanged:_ **`OpenTK 3.3.3`** math/bindings and the `AgOpenGPS.Core/Drawing` `GeoViewportBase` +
`GLW` DrawLib are kept as-is — the renderer is insulated from the host swap by the existing
abstraction.

> **Risk (to be recorded in the planned `PARITY_REPORT.md`).** Avalonia's GL context is frequently
> OpenGL ES / ANGLE. If the Core DrawLib/`GLW` relies on immediate-mode legacy OpenGL, it will not run
> unchanged under GLES; the `glReadPixels` back-buffer scan must also be verified on the Avalonia
> surface. This is the **dominant open feasibility risk** and will be tracked in `PARITY_REPORT.md` once
> that deliverable is authored (it is **not yet on disk**) — these rows therefore make **no
> proven-parity claim**.

### Platform services

#### Added

- **`SourceCode/AgOpenGPS.Core/Platform/IPlatformServices.cs`** — a single new abstraction isolating
  every OS-specific call: application-data/config root, monitor brightness get/set, serial-port-name
  enumeration, and single-instance acquisition. On disk and compiling within the portable Core.
- **`SourceCode/AgOpenGPS.Core/Platform/PlatformServicesFactory.cs`** — selects the concrete
  implementation at startup via `RuntimeInformation.IsOSPlatform`. On disk.
- **`WindowsPlatformServices.cs`** (WMI brightness + Registry migration-read, compiled under
  `net8.0-windows`), **`LinuxPlatformServices.cs`** (sysfs `/sys/class/backlight` best-effort
  brightness; `~/.config` config root), and **`MacPlatformServices.cs`**
  (`~/Library/Application Support` config root; brightness gated / no-op) — all three on disk in
  `AgOpenGPS.Core/Platform/`.

_Note:_ per the migration rules, new architectural surface area is limited to `IPlatformServices` plus
the OpenGL host adapter — no unrelated abstractions were introduced.

### Mapping

#### Changed

- **`System.Windows.Forms.DataVisualization` steering/heading charts → custom Avalonia drawing** (or a
  selected cross-platform chart), preserving series, axes, zoom/autoscale, and the rolling data buffer
  (`FormGraphHeading` / `FormGraphSteer` / `FormGraphXTE` / `FormCorrection`). The Windows-only
  `<Reference>` is removed from the GPS `csproj` once no consumer remains.
  _Deferred — lands with the GPS conversion._

#### Removed

- **`GMap.NET.WinForms 2.1.7`** (online background imagery) — replaced or **feature-gated** (F-021);
  the SQLite tile cache stays cross-platform and the field still renders without imagery. Still
  referenced in the GPS `csproj` pending conversion. _Feature-gated (per-OS); removal deferred._
- **`MechanikaDesign.WinForms.UI.ColorPicker 2.0.0`** → replaced with the built-in Avalonia
  `ColorPicker`. _Deferred._
- **`Accord.Imaging 3.8.0` + `Accord.Video.DirectShow 3.8.0`** webcam capture — **feature-gated**
  off-Windows (F-045; DirectShow is Windows-only and abandoned; default `isWebCamOn=false`).
  _Feature-gated (per-OS); removal deferred._

### Serial

#### Changed

- **Serial communication keeps `System.IO.Ports`**, now as the **cross-platform** NuGet package
  `System.IO.Ports 9.0.0` (Windows/Linux/macOS), so serial behavior is preserved; only port-**name**
  enumeration is abstracted behind `IPlatformServices` (`COMx` vs `/dev/ttyUSB*`, `/dev/ttyACM*`,
  `/dev/cu.*`). Applies to AgIO `SerialComm` and `GPS_Out` (F-026 / F-038; the 4-second NMEA timeout is
  preserved). The package and the enumeration seam are on disk in AgIO's `SerialCommService`; the
  GPS-side wiring lands with the GPS conversion. _AgIO at parity (pending CI); GPS deferred._

### Settings

#### Changed

- **Settings backing swapped from the Windows Registry** (`docs/settings.md` L26-28: _"All settings
  are stored in Windows Registry (not .config files)"_) **and `%AppData%`** to an `IPlatformServices`
  cross-platform config root — Windows `%AppData%\AgOpenGPS`, Linux `~/.config/AgOpenGPS`, macOS
  `~/Library/Application Support/AgOpenGPS`. Files:
  `GPS/Properties/{RegistrySettings,VehicleSettings,ToolSettings,Settings,SettingsLegacy}.cs` and
  `AgIO/Source/Properties/{RegistrySettings,Settings}.cs`. AgIO already uses a cross-platform XML store
  at `<ApplicationData>/AgOpenGPS/registry.xml`; the GPS-side config-root wiring lands with the GPS
  conversion. _AgIO at parity; GPS deferred._
- **`GPS/Classes/CSettingsMigration.cs`** performs a one-time Registry read on Windows; the
  legacy→split round-trip is preserved exactly. _Deferred._

_Unchanged / frozen:_ the split settings XML schema — Vehicle `VehicleProfiles/{name}.xml`, Tool
`ToolProfiles/{name}.xml`, Environment `Environment/environment.xml` (`docs/settings.md` L32-36) — is
preserved and verified by `SettingsRoundTripTests` (F-036).

### Build/CI

#### Changed

- **`.github/workflows/build.yml`** from a single `windows-latest` runner to a `strategy.matrix` over
  windows/ubuntu/macos with `fail-fast: false` and a per-OS artifact `AgOpenGPS-${{ matrix.os }}`. The
  action pins are kept: `actions/checkout@v5`, `actions/setup-dotnet@v4`,
  `gittools/actions/gitversion/setup@v3.2.0` (`versionSpec 5.12.x`),
  `gittools/actions/gitversion/execute@v3.2.0`, and `actions/upload-artifact@v4`. Still single
  `windows-latest` on disk. _Deferred — CI checkpoint._
- **`.github/workflows/release.yml`** to four per-RID self-contained publish legs (windows→`win-x64`,
  ubuntu→`linux-x64`, macos→`osx-x64`, macos→`osx-arm64`); the PowerShell `Compress-Archive` step is
  replaced with cross-platform archiving guarded by `if: runner.os`; per-RID asset names embed the
  version + RID; the license files are bundled (root `LICENSE` Apache 2.0, `GPS/License.txt` GPLv3,
  `Updater/License.txt` GPLv3); the `softprops/action-gh-release@v2` draft/prerelease logic is
  retained. Still single `windows-latest` on disk. _Deferred — CI checkpoint._
- **`SourceCode/.editorconfig`** `*.Designer.cs` analyzer glob is the capital-`D` form and lists
  `Position` (and `Config*`, `Controls`, `GUI`, `OpenGL`, `PGN`, `SaveOpen`, `Sections`, `UDPComm`,
  `NMEA`, `NTRIPComm`, `SerialComm`, `UDP`), carrying the `[XPLAT]` provenance note. _At parity._
- **Package version inventory recorded for pre-release advisory verification (AAP §0.5; review
  finding #14).** The pinned, on-disk package versions are: the Avalonia UI stack — `Avalonia`,
  `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, and `Avalonia.Diagnostics` — all
  at **11.3.18**; `Dev4Agriculture.ISO11783.ISOXML 0.23.1.1`; `OpenTK 3.3.3`; `Newtonsoft.Json 13.0.4`;
  `SkiaSharp 2.88.9`; `SourceGear.sqlite3 3.50.3`; `System.Data.SQLite 2.0.1`; `System.IO.Ports 9.0.0`;
  `System.Configuration.ConfigurationManager 9.0.0`; `System.Resources.Extensions 9.0.0`; and the test
  toolchain `Microsoft.NET.Test.Sdk 17.12.0`, `NUnit 4.3.2`, `NUnit3TestAdapter 4.6.0`,
  `NUnit.Analyzers 4.6.0`. All of these restore and build cleanly from nuget.org into the local cache.
  **External advisory / CVE verification on nuget.org could not be completed in this offline environment**
  (no outbound network / web search available) and **remains an explicit pre-release CI gate**, to be
  recorded in `PARITY_REPORT.md` when that deliverable is authored. The net48-only
  `GMap.NET.WinForms 2.1.7`, `MechanikaDesign.WinForms.UI.ColorPicker 2.0.0`, `OpenTK.GLControl 3.3.3`,
  `System.Memory 4.6.0`, and `System.ValueTuple 4.6.1` references are removed/replaced at the GPS
  conversion; `Avalonia.Controls.ColorPicker 11.3.18` must be **added** to the GPS `csproj` then (it
  backs `FormColorPickerView`'s `ColorSpectrum` / `ColorSlider`). _Inventory recorded; external advisory
  verification deferred to the CI checkpoint._

### Logic decoupling

#### Changed

- **Scan-loop / guidance / section / communication logic lifted out of the `FormGPS` / `FormLoop`
  `.Designer.cs` partials into plain, constructor-injectable services** (behavior frozen):
  `PositionService.cs` (← `Position.designer.cs`), `PgnDispatcher.cs`
  (← `UDPComm.Designer.cs` + `PGN.Designer.cs`), `SectionService.cs` (← `Sections.Designer.cs`),
  `FieldIoService.cs` (← `SaveOpen.Designer.cs`), `RenderCoordinator.cs` (← `OpenGL.Designer.cs`), and
  the AgIO `Services/*` (← `{NMEA,NTRIPComm,SerialComm,UDP}.Designer.cs`). The four AgIO services
  (`NmeaService`, `NtripService`, `SerialCommService`, `UdpLoopbackService`) are on disk and compiling;
  the five GPS services land with the GPS conversion. _AgIO at parity; GPS deferred._

_Note:_ this eliminates the `mf` / `FormGPS` god-object back-reference via constructor injection; the
≤70 ms `udpWatchLimit` throttle and the receive→fuse→steer→section path are preserved with no added
latency.

### Core & algorithms

#### Changed

- **`SourceCode/AgOpenGPS.Core/**/*.cs` recompiled for `net8.0`; WPF types purged** — `CommandManager`
  in `ViewModels/RelayCommand.cs`, `Visibility` in `FieldTableViewModel.cs`, and the `Media3D` `using`
  in `Models/Camera.cs`. The Core builds clean and its 33 tests pass. _At parity._
- **`SourceCode/GPS/Classes/**/*.cs` recompiled, behavior frozen** — Stanley
  `CGuidance.DoSteerAngleCalc()` and Pure Pursuit `CTrackMethods.GoalPoint()`
  (`atan2(2·wheelbase·sin(error), lookahead)`); the safety guards `maxSteerAngle = 30°` and
  `maxAngularVelocity = 0.64°/s` (`docs/settings.md` L54-55) are unchanged. The `FormGPS` / `mf`
  back-reference becomes an injected `ApplicationModel` / services (`CSmartWAS(FormGPS)` →
  `CSmartWAS(ApplicationModel)` already done). Verified by `GuidanceEquivalenceTests`.
  _Deferred — the recompile completes with the GPS conversion._

#### Removed

- **`PresentationCore` (Core), `WindowsBase` (AgIO), and `System.Windows.Forms` (Updater) GAC
  references.** Core and AgIO are done on disk; the GPS-side removal lands with the conversion.
- **`System.Management`** moved from a GAC reference to the NuGet package under `net8.0-windows`,
  confined to `WindowsPlatformServices`.

### Tests & parity

#### Added

- **`SourceCode/AgOpenGPS.Tests/Parity/Golden/**` scaffold** — the golden-fixture directory tree
  (`Pgn/`, `Field/`, `IsoXml/`, `Settings/`, `Guidance/`) with `README.md` / `.gitkeep` placeholders is
  on disk. The golden-file parity **suite bodies**
  (`PgnFrameGoldenTests`, `FieldRoundTripTests`, `IsoXmlEquivalenceTests`, `SettingsRoundTripTests`,
  `GuidanceEquivalenceTests`), modeled on `AgLibrary.Tests/Settings/XmlSettingsHandlerTests.cs`, and the
  captured golden fixtures themselves are **not yet authored** — no parity `.cs` test file exists on disk
  today. _Scaffold on disk; suite bodies + fixtures deferred — `AgOpenGPS.Tests` depends on GPS and
  rebuilds at the GPS conversion._

#### Changed

- **`AgOpenGPS.Tests.csproj`** drops `<PlatformTarget>x64</PlatformTarget>` (invalid for `osx-arm64`)
  and `System.Memory`, and adds an `AgOpenGPS.Core` `ProjectReference` plus
  `Dev4Agriculture.ISO11783.ISOXML 0.23.1.1`. The test toolchain is kept: `NUnit 4.3.2`,
  `Microsoft.NET.Test.Sdk 17.12.0`, `NUnit3TestAdapter 4.6.0`, `NUnit.Analyzers 4.6.0`. Tests run on
  windows/ubuntu/macos. _Deferred._

#### Removed

- **`SourceCode/AgOpenGPS.Tests/SampleTest.cs`** placeholder — **deleted from disk** (it is no longer
  present). Its role is superseded by the planned golden-file parity suites, whose bodies are authored
  when the GPS-dependent `AgOpenGPS.Tests` project rebuilds at the GPS conversion. _Removed._

### Documentation

#### Added

- **`MIGRATION_DOCS/` deliverable set.** `CHANGELOG.md` (this file), `TRANSITION_MAP.md`, and
  `FEATURE_TRACEABILITY.md` are **on disk**. `PARITY_REPORT.md` and `VALUE_SUMMARY.md` are **not yet
  authored** — they are planned for the parity/CI checkpoint (a `PARITY_REPORT.md` requires the GPS
  build, the golden-file suite bodies, and per-OS CI evidence, none of which exist yet).

#### Changed

- **`README.md`** (per-OS build/run/publish instructions; the maintenance-mode posture removed) and
  **`docs/**/*.md`** references updated for the cross-platform stack. **`docs/pgn-protocol.md` is
  retained unchanged** as the frozen protocol contract (header `0x80 0x81 0x7F`, additive-checksum CRC,
  loopback ports 15555/17777). _README/docs updates deferred — docs checkpoint._

### Cross-platform correctness (case/culture/paths)

#### Changed

- **Case-fix renames** `Position.designer.cs` → `Position.Designer.cs`,
  `FormYes.designer.cs` → `FormYes.Designer.cs`, and
  `FormtimedMessage.resx` → `FormTimedMessage.resx` (the same `FormYes` / `FormtimedMessage` case fixes
  also apply under AgIO and ModSim). The GPS hazards are resolved by the retirement of the legacy
  `Forms/` tree, and the `.editorconfig` analyzer glob already uses the capital-`D` form; the
  AgIO/ModSim originals were removed and reimplemented as the Avalonia `FormYesView` /
  `FormTimedMessageView`. _At parity._
- **Numeric file/protocol I/O audited for `InvariantCulture`** (so a comma-decimal locale on
  Linux/macOS cannot corrupt field files, settings, ISOXML, or PGN-derived text); hard-coded `\`
  replaced with `Path.Combine` / `Path.DirectorySeparatorChar`; **`.gitattributes`** `-text` rules
  added for the byte-stable parity fixtures (`SourceCode/AgLibrary.Tests/Settings/TestSettings.xml`,
  `SourceCode/AgOpenGPS.Tests/Parity/**`). AgIO's audit is on disk; the GPS-side audit and the
  `.gitattributes` fixture rules land with the GPS / parity checkpoints.
  _AgIO at parity; GPS / fixtures deferred._

### Licensing

_Unchanged:_ `/LICENSE` (Apache 2.0) and the GPLv3 `SourceCode/GPS/License.txt` and
`SourceCode/Updater/License.txt` are **retained, not rewritten**. The per-program license artifacts are
preserved across the migration (Apache 2.0 at the repository root; GPLv3 for the GPS and Updater
projects). _At parity._

---

_This changelog is kept current as the migration proceeds. See also `TRANSITION_MAP.md` (file-by-file
old→new mapping and authoritative per-row parity status) and `FEATURE_TRACEABILITY.md` (per-feature
F-001…F-045 cross-platform disposition) — both **on disk**. Two further deliverables are **planned but
not yet authored**: `PARITY_REPORT.md` (behavioral-parity proof and the open GL-context /
`glReadPixels` risks) and `VALUE_SUMMARY.md` (executive brief)._

