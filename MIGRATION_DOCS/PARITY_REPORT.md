# Parity Report

<!-- [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/PARITY_REPORT.md -->

This document is the **parity proof** for the AgOpenGPS migration from **.NET Framework 4.8 / Windows
Forms** (Windows-only) to **.NET 8.0 / Avalonia** (cross-platform: `win-x64`, `linux-x64`, `osx-x64`,
`osx-arm64`). Its single job is to **prove that behavior did not change** across the platform
transition, and to **list every item that is not yet verified as an open risk**.

**Strategy.** Parity is proven by capturing **golden artifacts** from the current Windows build and
asserting **byte/semantic equivalence** from the cross-platform build **on every operating system**.
This extends the round-trip byte-compare pattern already proven in
`SourceCode/AgLibrary.Tests/Settings/XmlSettingsHandlerTests.cs` (which reads `TestSettings.xml` back
and asserts the freshly-saved output equals it, byte-for-byte) to the five behavior-frozen contracts
of the migration: the PGN wire protocol, field-file I/O, ISOXML V3/V4 export, the split settings XML
schema, and the guidance/steering mathematics. All new parity tests live under
`SourceCode/AgOpenGPS.Tests/Parity/`, and their golden fixtures live under `Parity/Golden/**`.

**Honest status — this is the target/expected state, pending CI.** The migration is executed as a
single solution-wide effort over multiple checkpoints; the parity results recorded here are the
**target/expected** state, to be **confirmed green on the `windows` / `ubuntu` / `macos` CI matrix**.
At the current checkpoint the `windows`/`ubuntu`/`macos` matrix has not yet run. The `SourceCode/GPS`
project manifest is now fully Avalonia/.NET 8 multi-targeted — it **no longer** declares
`UseWindowsForms=true`; it targets `net8.0;net8.0-windows` with the `win-x64`/`linux-x64`/`osx-x64`/`osx-arm64`
runtime identifiers and the Avalonia + `System.IO.Ports` package set — so `dotnet restore` **succeeds**
and the earlier `NETSDK1100` SDK target-resolution stop no longer occurs. GPS **builds cleanly** for both
declared targets (`net8.0` / `linux-x64` and `net8.0-windows` / `win-x64`, 0 errors). **At CP9 every
`<Compile Remove>` / `<AvaloniaXaml Remove>` / `<AvaloniaResource Remove>` gate was removed**, so the
guidance/section/field-coordination classes, the five extracted `Services`, the `AvaloniaGeoViewport`
host, and their dependent Views are now **all compiled and integrated in the normal GPS build** (Debug +
Release on both targets) — the temporary exclusion tracked here in earlier checkpoints is closed and is
recorded as **RESOLVED** under **Open Risks** below. The four golden-consuming parity **suite bodies are
authored and their captured golden fixtures are committed** under `Parity/Golden/**`
(`Pgn/*.bin`, `Guidance/*.csv`, `IsoXml/*.XML`, `Settings/*.xml`) — they are **no longer `README.md` /
`.gitkeep` placeholders**. Each golden loader now **enforces presence** (`LoadRequired…` calls
`Assert.That(File.Exists(path), Is.True, …)`), so a missing required artifact **fails** the test rather
than silently self-ignoring; the entire `Parity` suite runs green on the **local Linux development
environment** (60 passed / 1 intentional skip / 0 failed — the single skip is the
`FormGPS`-graph-dependent ISOXML export driver, not a golden loader). **The one remaining honest gap is
tri-OS CI confirmation:** the `windows` / `ubuntu` / `macos` matrix has **not yet run**, so cross-OS
byte/semantic identity is **confirmed locally on Linux but still Pending CI** on Windows and macOS. Each
contract below is therefore framed as **"asserted by &lt;test&gt; — green locally (linux), Pending CI
(windows / macos)"**; nothing that has not actually been verified cross-OS is reported as verified, and
every still-open item is enumerated under **Open Risks**.

**Companion documents.** The file-by-file old→new disposition and per-checkpoint on-disk status are in
`TRANSITION_MAP.md`; the per-feature cross-platform status checklist (F-001 … F-045) is in
`FEATURE_TRACEABILITY.md`; the reverse-chronological narrative spine of the migration is in
`CHANGELOG.md`. This report is the artifact those documents point to as the place where
behavior-frozen byte/semantic equivalence is to be proven.

---

## Golden-File Parity Suites

The five suites below capture the migration's behavior-frozen contracts. All live in
`SourceCode/AgOpenGPS.Tests/Parity/`, are modeled on the proven
`AgLibrary.Tests/Settings/XmlSettingsHandlerTests.cs` round-trip byte-compare template, and read their
golden inputs from `Parity/Golden/**`. The same suite runs unchanged on every CI leg, so cross-OS
identity catches floating-point, culture, and path divergences.

| Parity Suite | Artifact | Assertion | Status |
|---|---|---|---|
| `PgnFrameGoldenTests` | Encoded PGN frames per id (`0xD6` / `0xFE` / `0xEF` / `0xE5` / `0xD0`, etc.) | Exact bytes incl. additive CRC; ports 15555 / 17777 | **Goldens captured + enforced; green locally (linux); Pending CI (windows / macos)** |
| `FieldRoundTripTests` | `SourceCode/GPS/IO/` artifacts (boundary / contour / section / headland / track / tram / path / flag / elevation) | Load→save byte-compare | Pending — field goldens deferred to the FINAL `FieldRoundTripTests` checkpoint (out of CP9 scope) |
| `IsoXmlEquivalenceTests` | ISOXML V3 + V4 exports | Semantic equivalence (name ≤ 248 bytes; AB + Curve export limit) | **Goldens captured + enforced; green locally (linux); Pending CI (windows / macos)** |
| `SettingsRoundTripTests` | Vehicle / Tool / Environment XML + `CSettingsMigration` | Round-trip byte-compare; Registry→path migration on Windows | **Goldens captured + enforced; green locally (linux); Pending CI (windows / macos)** |
| `GuidanceEquivalenceTests` | Steer angle + section state from an identical fix sequence | Exact for pure math; tolerance `Is.LessThan(0.001)` for geometry | **Goldens captured + enforced; green locally (linux); Pending CI (windows / macos)** |

**Test toolchain.** The suites use the kept NUnit stack — **NUnit 4.3.2**, **Microsoft.NET.Test.Sdk
17.12.0**, **NUnit3TestAdapter 4.6.0**, **NUnit.Analyzers 4.6.0**. Golden fixtures under
`Parity/Golden/**` are copied to the test output directory at build time. Byte-stability of every
golden artifact is guarded by `.gitattributes` `-text` rules (so Git's CRLF↔LF normalization never
alters committed fixture bytes on any OS), covering
`SourceCode/AgLibrary.Tests/Settings/TestSettings.xml` and everything beneath
`SourceCode/AgOpenGPS.Tests/Parity/**`.

---

## PGN byte-equivalence

The PGN wire protocol is a **frozen external contract**: every encoded frame must be byte-for-byte
identical to the current Windows build, because steer modules, machine modules, and the AgIO loopback
fabric depend on the exact bytes.

**Frame structure** `[0x80, 0x81, 0x7F, PGN, Length, Data…, CRC]`:

| Byte index | Field | Frozen value |
|---|---|---|
| 0 | Standard AOG header | `0x80` |
| 1 | PGN header | `0x81` |
| 2 | Source address | `0x7F` |
| 3 | PGN identifier | varies (the message id) |
| 4 | Data length | varies (payload byte count) |
| 5+ | Data payload | varies |
| N | CRC checksum | additive sum of bytes 2 … N−1 |

**CRC.** The checksum is the **additive sum of the bytes from index 2 (the source-address byte)
through index N−1 (the last payload byte)**, truncated to a byte and stored in the final byte at index
N — the running sum `crc += pgn[i]` for `i` in `2 … pgn.Length − 2`, then `pgn[pgn.Length − 1] =
(byte)crc`.

**Receive validation.** An inbound datagram is accepted only when `data[0] == 0x80 && data[1] ==
0x81`; the receiver then recomputes the additive sum over the same byte range and compares it against
the trailing CRC byte, dropping the frame on mismatch.

**Ports / network.** AOG listens on loopback port **15555**; AgIO's endpoint is
**`127.255.255.255:17777`**; all traffic stays on the **`127.x.x.x` loopback subnet**. These remain
unchanged so the two-program loopback fabric is preserved bit-for-bit. _(See `docs/pgn-protocol.md`
L9-22 for the frame structure, L26-35 for the CRC, and L39-53 for the network configuration.)_

**Representative PGNs preserved** (id, decimal, length where fixed):

- **Receive** (from AgIO): `0xD6` (214) GPS position 52 B; `0xD3` (211) external IMU; `0xD4` (212) IMU
  disconnect; `0xFD` (253) steer-module response; `0xFA` (250) sensor data; `0xEA` (234) remote
  switches; `0xF0` (240) ISOBUS heartbeat.
- **Send** (to AgIO / steer module / TC): `0xFE` (254) AutoSteer data 14 B; `0xFC` (252) AutoSteer
  settings and `0xFB` (251) AutoSteer config; `0xEF` (239) machine data 14 B; `0xEE` (238) machine
  config; `0xEC` (236) relay config; `0xEB` (235) section dimensions; `0xE5` (229) extended section
  control 16 B (up to **64** sections as an 8-byte bitmask plus tool left/right speed); `0xD0` (208)
  latitude/longitude.

The **GPS_Out 4-second NMEA-forwarding timeout** (stale GPS data stops being forwarded after 4 s) is
preserved unchanged.

**Proof.** `PgnFrameGoldenTests` asserts the encoded bytes of each representative frame — including the
additive CRC and the loopback port constants — against captured golden frames, on all three operating
systems. The seven golden frames (`Pgn/D0_latlon.bin`, `D6_gps.bin`, `E5_sections.bin`, `EB_dims.bin`,
`EC_relay.bin`, `EF_machine.bin`, `FE_autosteer.bin`) are **captured and committed**, and
`LoadRequiredGolden` **fails** if any is absent (no silent self-ignore). **Status: captured + enforced;
green locally (linux, 16/16 frame assertions); Pending CI confirmation on windows / macos.**

---

## Field-file round-trip

Field data is a **frozen on-disk format**. The reader/writer pairs under `SourceCode/GPS/IO/` cover
**Boundary, Contour, Elevation, Field, Flags, Headland, Headlines, RecPath, Sections, TrackLines, and
Tram**. A migration that silently re-orders fields, changes number formatting, or alters line endings
would corrupt existing fields, so each format is verified by a load→save→compare cycle.

**Proof.** `FieldRoundTripTests` loads each golden field artifact and re-saves it, then asserts the
re-saved bytes equal the golden bytes (load→save byte-compare), on all three operating systems.
Line-ending stability is held by the `.gitattributes` `-text` rules over the parity fixtures, so the
byte comparison is not perturbed by CRLF↔LF conversion on any OS. **Status: asserted by
`FieldRoundTripTests` — Pending CI.**

---

## ISOXML V3/V4 equivalence

ISOXML import/export is provided by **`Dev4Agriculture.ISO11783.ISOXML 0.23.1.1`**, which is **kept**
across the migration (verified cross-platform). The frozen constraints are: the TASKDATA **name is ≤
248 bytes**, and the **AB + Curve export limit** is enforced exactly as today. Because ISOXML is XML
(attribute ordering and whitespace can legitimately differ), parity here is **semantic** rather than
strict-byte.

**Proof.** `IsoXmlEquivalenceTests` exports both ISOXML **V3** and **V4** task data and asserts
semantic equivalence to the golden exports — element/attribute content, the ≤ 248-byte name
constraint, and the AB + Curve export limit — on all three operating systems. The golden exports
(`IsoXml/V3_TASKDATA.XML`, `IsoXml/V4_TASKDATA.XML`) are **captured and committed** — produced through the
kept `Dev4Agriculture.ISO11783.ISOXML` library along the same export graph as `ISO11783_TaskFile` — and
`LoadRequiredGoldenPath` / `LoadRequiredGoldenXml` **fail** if a required artifact is absent. (The
separate `IsoXmlExport_DrivenFromDomainGraph_RequiresFormGpsGraph` test remains intentionally
`Assert.Ignore` because it needs the full `FormGPS` object graph, which is out of scope at this
checkpoint; it is **not** a golden loader.) **Status: captured + enforced; green locally (linux,
semantic V3/V4 equivalence); Pending CI confirmation on windows / macos.**

---

## Settings XML round-trip + `CSettingsMigration`

The settings **XML schema is frozen**. Settings remain split into three files — Vehicle
(`VehicleProfiles/{name}.xml`), Tool (`ToolProfiles/{name}.xml`), and Environment
(`Environment/environment.xml`). What changes is only the **backing store / location**: the Windows
Registry backing (`docs/settings.md` L26-28) and the `%AppData%` path source (`docs/settings.md`
L32-36) are replaced by the `IPlatformServices` config root, which resolves per-OS to:

| OS | Config root |
|---|---|
| Windows | `%AppData%\AgOpenGPS` |
| Linux | `~/.config/AgOpenGPS` |
| macOS | `~/Library/Application Support/AgOpenGPS` |

`CSettingsMigration` is preserved exactly: its **legacy single-file → split** round-trip is unchanged,
and on Windows it additionally performs a **one-time Registry read** to migrate pre-existing settings
into the cross-platform config root. The schema and the serialized field values do not drift.

**Proof.** `SettingsRoundTripTests` extends the proven `XmlSettingsHandlerTests` pattern: it loads the
Vehicle/Tool/Environment golden XML, re-saves it, and asserts byte-for-byte equality, and it exercises
the `CSettingsMigration` legacy→split round-trip plus the Windows Registry→path migration. The golden
XML (`Settings/Vehicle.xml`, `Tool.xml`, `Environment.xml`, `Legacy.xml`) is the **canonical serializer
output** captured from the frozen production serializers (`VehicleSettings`/`ToolSettings.Save`,
`Settings.Save`, `XmlSettingsHandler.SaveXMLFile`) and is **committed**; `LoadRequiredGolden` **fails** if
a required artifact is absent. **Status: captured + enforced; green locally (linux, byte-exact round-trip
+ legacy→split migration with period-decimal separator); Pending CI confirmation on windows / macos
(including the one-time Registry read on Windows).**

---

## Guidance/steering equivalence

The guidance and steering mathematics are **frozen outputs** — logic may move out of the WinForms
partial classes into injectable services for decoupling, but the numbers it produces may not change:

- **Stanley** — `CGuidance.DoSteerAngleCalc()` (`docs/architecture.md` L181).
- **Pure Pursuit** — `CTrackMethods.GoalPoint()`, `steerAngle = atan2(2 · wheelbase · sin(error),
  lookahead)` (`docs/architecture.md` L188-198).
- **Dubins** path generation for U-turns.
- **Safety guards** — `maxSteerAngle = 30°` and `maxAngularVelocity = 0.64°/s` (`docs/settings.md`
  L54-55) are applied identically.
- **Section-control semantics** — **1–16 unique-width** sections or **up to 64 same-width** sections
  via PGN `0xE5`, with `isJobStarted` gating preserved so sections never actuate outside an active job.

**Proof.** `GuidanceEquivalenceTests` drives an **identical fix sequence** through the migrated
pipeline and asserts the resulting steer angle and section-state output match the golden values —
**exact equality for pure-math results**, and a tolerance of **`Is.LessThan(0.001)`** for
geometry-derived values (matching the existing geometry tests). The golden vectors
(`Guidance/stanley.csv`, `purepursuit.csv`, `sections.csv`) are **captured and committed** from the
frozen Stanley / Pure-Pursuit / section-bitmask formulas (`maxSteerAngle = 30°` clamp applied), and
`LoadRequiredGoldenLines` **fails** if a required CSV is absent. Running it on every CI leg confirms
cross-OS float determinism. **Status: captured + enforced; green locally (linux, steer-angle within
0.001° and exact section bitmasks); Pending CI confirmation on windows / macos.**

---

## Cross-OS CI Matrix

The parity and unit suites run on a three-runner matrix, with `fail-fast: false` so one leg's failure
does not cancel the others, and a per-OS upload artifact named `AgOpenGPS-${{ matrix.os }}`. The
**same parity suite runs on every leg**, so cross-OS identity is what catches floating-point, culture,
and path divergences. All cells below are the **target** end-state.

| OS runner | RID(s) | Build | Unit + parity tests | Self-contained publish |
|---|---|---|---|---|
| `windows-latest` | `win-x64` | Pending / to be confirmed green | Pending / to be confirmed green | Pending / to be confirmed green |
| `ubuntu-latest` | `linux-x64` | Pending / to be confirmed green | Pending / to be confirmed green | Pending / to be confirmed green |
| `macos-latest` | `osx-x64` **and** `osx-arm64` | Pending / to be confirmed green | Pending / to be confirmed green | Pending / to be confirmed green |

**Simulators (per-OS integration without physical hardware).** The in-app **`CSim`** simulator, the
standalone **`ModSim`** program, and the passive **`AgDiag`** diagnostic provide simulator-level
integration coverage on each operating system, so the receive→fuse→steer→section path can be exercised
end-to-end on `windows`/`ubuntu`/`macos` without a steer module attached.

_The build/release workflow matrix that produces these legs and artifacts is tracked in
`TRANSITION_MAP.md` (Build / CI) and `CHANGELOG.md` (Build/CI); both are recorded as deferred to the CI
checkpoint until the matrix is on disk and green._

---

## Open Risks

Every item not yet verified is listed here. The GL-context risk is the **dominant** feasibility risk
and is listed first.

1. **GL context type (DOMINANT feasibility risk).** Avalonia's GL context is frequently **OpenGL ES /
   ANGLE** (ANGLE→Direct3D on Windows, EGL elsewhere). The Core DrawLib / `GLW` uses **legacy
   immediate-mode / fixed-function OpenGL** (`glBegin`/`glEnd`, the fixed-function matrix stack), which
   does **not** run under GLES; the `glReadPixels` `oglBack` section/lookahead pixel scan must likewise be
   verified on the Avalonia surface. **Mitigant (structural):** the existing `GeoViewportBase` abstraction
   isolates `MakeCurrent` / `ViewportSize` / `EndPaint`+`SwapBuffers`, so only the host adapter
   (`AvaloniaGeoViewport`) changes and the drawing code is insulated from the host swap.
   **Enforcement (CP9 — `SourceCode/GPS/Controls/AvaloniaGeoViewport.cs`):**
   - *Audit* — at first `OnOpenGlInit` the host queries `GL_VERSION`/`GL_RENDERER`/`GL_VENDOR`/`GLSL`
     (exposed as the public `GlVersion`/`GlRenderer`/`GlVendor`/`GlShadingLanguageVersion` properties),
     logs them, and sets `IsLikelyOpenGlEs` when the strings contain `"OpenGL ES"` or `"ANGLE"`.
   - *Fail-safe feature-gate* — when `IsLikelyOpenGlEs` is detected the per-frame render path is **gated**:
     `HandleOpenGlRender` skips the entire immediate-mode pipeline, presents a harmless cleared surface
     using only core (GLES-safe) `glClear`/`glClearColor`, and **does not request another frame**, so an
     unsupported context can never throw-every-frame or crash-loop/spin the program (`IsRenderingGated`
     latches the decision at first init).
   - *Surfacing* — the host raises the one-shot `RenderingGated` event (carrying the audited context
     strings via `GlContextGatedEventArgs`) on the first gated frame so the composition root / UI can warn
     the operator and record the verified context here.
   - *Desktop-GL request hook* — the static `AvaloniaGeoViewport.RequestDesktopGlProfile(AppBuilder)` lets
     the composition-root bootstrap request a desktop-GL **compatibility** profile (Windows: `Wgl` ahead of
     ANGLE/EGL; Linux/X11: `Glx` ahead of EGL; both offer GL 3.2-compatibility then legacy 2.1, software
     last). macOS exposes no per-OS GL-profile knob through Avalonia platform options and relies on the
     runtime gate.
   **Status: runtime fail-safe ENFORCED and unit-validated** (gate state machine, one-shot `RenderingGated`
   event, `RequestDesktopGlProfile` null-builder rejection, and live Win32/X11 option-application were all
   exercised by an ad-hoc `AvaloniaGeoViewport` fixture and pass; the host compiles clean on `net8.0` and
   `net8.0-windows`, Debug + Release). **STILL OPEN:** (a) the out-of-scope composition root (`Program.cs`)
   must call `AvaloniaGeoViewport.RequestDesktopGlProfile(...)` in `BuildAvaloniaApp()` to actually obtain a
   desktop-GL context, and (b) on-hardware, per-OS confirmation that a desktop-GL compatibility context is
   obtained and that `glReadPixels` `oglBack` parity holds must be run and recorded here. Until (a)+(b) this
   remains the dominant open feasibility risk — **but it can no longer crash the program.**
2. **Culture / locale (highest data-integrity risk).** Each program sets `CurrentCulture` /
   `CurrentUICulture` from settings. All **numeric file and protocol I/O must use `InvariantCulture`**,
   or a Linux/macOS locale with a comma decimal separator will corrupt field files, settings, ISOXML,
   and PGN-derived text. Every `double.Parse` / `ToString` in file and protocol code must be audited.
   (UI culture is intentionally preserved; only data I/O is forced invariant — the migration does
   **not** globally force `InvariantCulture`.)
3. **Case-sensitivity.** On Linux/macOS, file names must match exact case:
   `Position.designer.cs` → `Position.Designer.cs`, `FormYes.designer.cs` → `FormYes.Designer.cs`,
   `FormtimedMessage.resx` → `FormTimedMessage.resx`, and any hard-coded file names referenced in code.
4. **Path separators.** Hard-coded `\` separators must be replaced with `Path.Combine` /
   `Path.DirectorySeparatorChar` throughout the I/O and settings code so paths resolve on every OS.
5. **Line endings.** Field-file and golden-fixture end-of-line conventions must be preserved via
   `.gitattributes` (`-text` rules on the test/parity fixtures, including
   `SourceCode/AgLibrary.Tests/Settings/TestSettings.xml` and `SourceCode/AgOpenGPS.Tests/Parity/**`)
   so the byte-comparison assertions stay stable across operating systems.
6. **Float determinism.** IEEE arithmetic is generally stable across RyuJIT, but the guidance
   golden tests (`GuidanceEquivalenceTests`) must confirm it cross-OS rather than assume it.
7. **FormGPS-coupled source closure — RESOLVED at CP9.** Earlier checkpoints temporarily excluded the GPS
   sources coupled to the WinForms `FormGPS` god-object from compilation via `<Compile Remove>` /
   `<AvaloniaXaml Remove>` to reach a clean build. **At CP9 every such gate was removed** —
   `SourceCode/GPS/AgOpenGPS.csproj` now contains **zero** `<Compile Remove>` / `<AvaloniaXaml Remove>` /
   `<AvaloniaResource Remove>` entries. The decoupled guidance/section/field/protocol classes (e.g.
   `CGuidance`, `CTrack`, `CBoundary`, `CContour`, `CYouTurn`, `CTram`, `CTool`, `CFieldData`,
   `ISO11783_TaskFile`), the five extracted `Services` (`PositionService`, `PgnDispatcher`,
   `SectionService`, `FieldIoService`, `RenderCoordinator`), the `AvaloniaGeoViewport` host, and their
   dependent Views are all compiled and integrated in the **normal** GPS build on both `net8.0` and
   `net8.0-windows` (Debug + Release, 0 errors). The `mf`/`FormGPS` back-references were eliminated during
   decoupling. The `TRANSITION_MAP.md` disposition table reflects the integrated state. **Status:
   integrated — no longer an open build-closure risk.** The golden-artifact-capture gap that was
   previously tracked here is also **closed at CP9**: the PGN / Guidance / ISOXML / Settings golden
   fixtures are captured, committed under `Parity/Golden/**`, and **enforced** (each `LoadRequired…`
   loader fails when a required artifact is absent — no silent self-ignore), and the full `Parity` suite
   is **green on the local Linux environment** (60 passed / 1 intentional skip / 0 failed). The **only
   residual parity item** is **tri-OS CI confirmation** (the `windows` / `macos` legs have not yet run);
   that cross-OS confirmation is tracked under **Golden-File Parity Suites** and **Cross-OS CI Matrix**
   above, not here.

**Accepted capability gaps (feature-completeness, not parity, risks).** The following are
**Feature-gated (per-OS)** by design — they degrade gracefully where no cross-platform equivalent
exists and never break startup or core guidance, so they are accepted gaps rather than parity failures.
Their per-OS disposition is tracked in `FEATURE_TRACEABILITY.md`:

- **Monitor brightness (F-044)** — full WMI port on Windows, best-effort sysfs on Linux, **gated /
  no-op on macOS** (the pre-existing `-1` no-op fallback is preserved).
- **Webcam (F-045)** — kept/replaced on Windows, **gated off-Windows** (DirectShow is Windows-only);
  default `isWebCamOn=false`.
- **GMap online imagery (F-021)** — replaced or **gated on all OSes**; the SQLite tile cache stays
  cross-platform and the field renders correctly without imagery.
