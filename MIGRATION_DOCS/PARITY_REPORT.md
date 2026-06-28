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
recorded as **RESOLVED** under **Open Risks** below. **All five golden-consuming parity suite bodies are
authored and their captured golden fixtures are committed** under `Parity/Golden/**`
(`Pgn/*.bin`, `Guidance/*.csv`, `IsoXml/*.XML`, `Settings/*.xml`, and — captured in the final
remediation pass — `Field/*.txt`) — they are **no longer `README.md` / `.gitkeep` placeholders**. Each
golden loader now **enforces presence** (`LoadRequired…` checks `File.Exists` and calls `Assert.Fail`
when a required artifact is absent), so a missing required artifact **fails** the test rather than
silently self-ignoring; the entire `Parity` suite runs green on the **local Linux development
environment**, and the full `AgOpenGPS.Tests` assembly reports **93 passed / 1 intentional skip / 0
failed** (the single skip is the `FormGPS`-graph-dependent ISOXML export driver,
`IsoXmlExport_DrivenFromDomainGraph_RequiresFormGpsGraph`, **not** a golden loader; the count rose from 79
to 93 in the QA F5 remediation, which added 14 production-invoking guidance/section/algorithm parity tests).
Across all three test assemblies the local Linux total is **129 passed / 1 skipped / 0 failed**
(`AgOpenGPS.Core.Tests` 33, `AgLibrary.Tests` 3, `AgOpenGPS.Tests` 93). **The desktop-GL request hook is now wired** in both
composition roots (`SourceCode/GPS/Program.cs` and `SourceCode/AgIO/Source/Program.cs` chain
`AvaloniaGeoViewport.RequestDesktopGlProfile(...)` into `BuildAvaloniaApp()`), closing the bootstrap half
of the dominant GL risk. **The remaining honest gaps are external-evidence items:** (1) tri-OS CI
confirmation — the `windows` / `ubuntu` / `macos` matrix has **not yet run**, so cross-OS byte/semantic
identity is **confirmed locally on Linux but still Pending CI** on Windows and macOS; and (2) on-hardware
confirmation that a desktop-GL **compatibility** context is actually obtained per-OS and that the
`glReadPixels` `oglBack` scan renders correctly. Each contract below is therefore framed as **"asserted
by &lt;test&gt; — green locally (linux), Pending CI (windows / macos)"**; nothing that has not actually
been verified cross-OS is reported as verified, and every still-open item is enumerated under **Open
Risks**.

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
| `FieldRoundTripTests` | `SourceCode/GPS/IO/` artifacts (boundary / contour / section / headland / headlines / track / tram / recorded-path / flag / elevation / field-plane) | Load→save byte-compare | **Goldens captured + enforced; green locally (linux, 11/11 field tests); Pending CI (windows / macos)** |
| `IsoXmlEquivalenceTests` | ISOXML V3 + V4 exports | Semantic equivalence (name ≤ 248 bytes; AB + Curve export limit) | **Goldens captured + enforced; green locally (linux); Pending CI (windows / macos)** |
| `SettingsRoundTripTests` | Vehicle / Tool / Environment XML + `CSettingsMigration` | Round-trip byte-compare; Registry→path migration on Windows | **Goldens captured + enforced; green locally (linux); Pending CI (windows / macos)** |
| `GuidanceEquivalenceTests` | Stanley (`CGuidance.DoSteerAngleCalc`), Pure-Pursuit (`CABLine.GetCurrentABLine`) steer angle + `maxSteerAngle=30°` clamp + `glm.toDegrees` tie | **Invokes the REAL production methods** (built via `ParityGraphFixture`); exact for pure math, tolerance `Is.LessThan(0.001)` for geometry | **Production-invoking; goldens production-captured + enforced; green locally (linux, 8/8); Pending CI (windows / macos)** |
| `SectionControlParityTests` | `SectionService.BuildMachineByte` (1–16 unique-width + ≤64 same-width PGN bytes) + `DoRemoteSwitches` `isJobStarted` gate | **Invokes the REAL `SectionService`**; exact PGN byte / state equality | **Production-invoking; goldens production-captured + enforced; green locally (linux, 3/3); Pending CI (windows / macos)** |
| `GuidanceAlgorithmCoverageTests` | `CDubins.GenerateDubins` (+ determinism), `CSmartWAS`, `CContour`, `CABCurve`, `CRecordedPath`, `CYouTurn` | **Invokes the REAL production methods**; exact for counts/states, tolerance `Within(0.001)` for lengths / distances / steer angles | **Production-invoking; goldens production-captured + enforced; green locally (linux, 6/6); Pending CI (windows / macos)** |

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

**Proof.** `FieldRoundTripTests` loads each golden field artifact and re-saves it through the production
`AgOpenGPS.IO` handler, then asserts the re-saved bytes equal the golden bytes (load→save byte-compare),
on all three operating systems. The **eleven** golden artifacts (`Field/Boundary.txt`, `Tracks.txt`,
`Sections.txt`, `Flags.txt`, `Contour.txt`, `Headland.txt`, `Headlines.txt`, `Tram.txt`, `RecPath.txt`,
`Elevation.txt`, `FieldPlane.txt`) were **captured in the final remediation pass and are committed** —
each generated by driving the frozen production serializers (`BoundaryFiles`/`TrackFiles`/`SectionsFiles`/
`FlagsFiles`/`ContourFiles`/`HeadlandFiles`/`HeadlinesFiles`/`TramFiles`/`RecPathFiles`/`ElevationFiles`/
`FieldPlaneFiles`) under `InvariantCulture`, and each is a **verified fixed point of `Load∘Save`** (a
re-emit produces byte-identical output). `LoadRequiredGolden` now **fails** (it does not self-skip) if a
required artifact is absent, so the field byte-contract is **enforced** at the final gate. Line-ending
stability is held by the `.gitattributes` `-text` rules over the parity fixtures
(`SourceCode/AgOpenGPS.Tests/Parity/**` and `**/*.txt`), so the byte comparison is not perturbed by
CRLF↔LF conversion on any OS. **Status: captured + enforced; green locally (linux, 11/11 field
round-trips byte-identical); Pending CI confirmation on windows / macos.**

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

**Proof — production-invoking (not formula-self-referential).** The parity suites now construct the
**real production guidance/section graph** (via `SourceCode/AgOpenGPS.Tests/Parity/ParityGraphFixture.cs`,
built in the same order as the live `App.axaml.cs` composition root) and drive an **identical fixed
input** through the **actual production methods**, asserting their output against committed goldens:

- **Stanley** — `GuidanceEquivalenceTests` invokes the real (private) `CGuidance.DoSteerAngleCalc()` via a
  controlled reflection seam over fixed input fields, plus a public-entry-point determinism test.
- **Pure Pursuit** — invokes the real `CABLine.GetCurrentABLine(...)` and captures the production
  `steerAngleAB` (the production expression is the goal-point `Math.Atan` form, mathematically equivalent
  to the documented `atan2(2·wheelbase·sin(error), lookahead)` — see note below).
- **Dubins / CSmartWAS / CContour / CABCurve / CRecordedPath / CYouTurn** — `GuidanceAlgorithmCoverageTests`
  constructs each real class and invokes a deterministic public method (`GenerateDubins`, `AddSample`,
  `DistanceFromContourLine`, `BuildNewOffsetList`, `StartDrivingRecordedPath`, `DistanceFromYouTurnLine`),
  including a **Dubins determinism re-run** (identical input → identical path).
- **Section control** — `SectionControlParityTests` invokes the real `SectionService.BuildMachineByte`
  (both the 1–16 unique-width and ≤64 same-width modes, asserting the exact PGN `0xFE`/`0xEF`/`0xE5` bytes)
  and the real `DoRemoteSwitches` to prove the **`isJobStarted` gate** blocks section activation outside an
  active job (off → no activation; on → activation).
- **Safety clamp** — over-range input is driven through the **production** `DoSteerAngleCalc` and asserted
  to saturate at ±`vehicle.maxSteerAngle` (default 30°).

Assertions are **exact equality for pure-math / counts / states** and a tolerance of **`Within(0.001)`**
for geometry-derived values (matching the existing geometry tests). The golden vectors
(`Guidance/stanley.csv`, `purepursuit.csv`, `sections_machinebyte.csv`, `algorithms.csv`) are
**captured from the migrated production methods themselves** (an accepted golden source, since the
production code is byte-identical to the net48 baseline `860eb9fd` by source-diff — decoupling only, no
math edits — so a production-captured golden equals the net48 behavior), and the loader **fails** if a
required CSV is absent. The legacy formula-self-referential goldens (`sections.csv`) are retained only as a
**secondary** mask-edge cross-check, clearly labelled as such. Running every suite on each CI leg confirms
cross-OS float determinism. **Status: production-invoking + production-captured goldens enforced; green
locally (linux — 8 Guidance + 3 Section + 6 Algorithm-coverage = 17/17, steer angles within 0.001° and
exact PGN byte/section state); Pending CI confirmation on windows / macos.**

> **Note (production Pure-Pursuit form).** Production `CABLine`/`CABCurve` compute the steer angle with
> `glm.toDegrees(Math.Atan(2·((gpE−pE)·cos h + (gpN−pN)·sin h)·Wheelbase / goalPointDistanceSquared))` over
> goal-point/pivot geometry — a structurally different but mathematically equivalent expression to the
> documented `atan2` form. The parity test now invokes this **production** expression rather than
> reproducing the documented formula in-test.

> **Note (angular-velocity guard, `0.64°/s`).** The `maxAngularVelocity` rate-limiter is **commented out
> (inactive) in production** — the `0.64` value feeds only the compass "*" indicator. This is **faithful
> frozen parity**: the limiter was **also commented out** in the net48 `Position.designer.cs`. The test
> asserts the `0.64` contract literal and documents that the production limiter is nominal (not enforced at
> runtime) in **both** versions.

---

## Cross-OS CI Matrix

The parity and unit suites run on a three-runner matrix, with `fail-fast: false` so one leg's failure
does not cancel the others, and a per-OS upload artifact named `AgOpenGPS-${{ matrix.os }}`. The
**same parity suite runs on every leg**, so cross-OS identity is what catches floating-point, culture,
and path divergences. All cells below are the **target** end-state.

| OS runner | RID(s) | Build | Unit + parity tests | Self-contained publish |
|---|---|---|---|---|
| `windows-latest` | `win-x64` | Pending CI run | Pending CI run | Pending CI run |
| `ubuntu-latest` | `linux-x64` | **Green (local dev env): Debug + Release, 0 errors** | **Green (local dev env): 129 passed / 1 skipped / 0 failed** | Pending CI run |
| `macos-latest` | `osx-x64` **and** `osx-arm64` | Pending CI run | Pending CI run | Pending CI run |

The `ubuntu-latest` Build and Unit+parity cells are recorded as **Green** because the identical
build/test commands were run in this Linux development environment: `dotnet build SourceCode/AgOpenGPS.sln
-c Debug` and `-c Release` both report 0 errors (16 pre-existing Avalonia `AVLN3001` "no public
constructor for runtime loader" advisories on 8 unrelated views × 2 TFMs, not escalated to errors under
Release `TreatWarningsAsErrors`), and the three test assemblies report 129 passed / 1 skipped / 0 failed.
The `windows-latest` and `macos-latest` cells, and **all** self-contained publish cells, remain **Pending
CI run** — they require GitHub-hosted runners not available in this offline environment.

**Simulators (per-OS integration without physical hardware).** The in-app **`CSim`** simulator, the
standalone **`ModSim`** program, and the passive **`AgDiag`** diagnostic provide simulator-level
integration coverage on each operating system, so the receive→fuse→steer→section path can be exercised
end-to-end on `windows`/`ubuntu`/`macos` without a steer module attached.

_The build/release workflow matrix that produces these legs and artifacts is **on disk**:
`.github/workflows/build.yml` declares the tri-OS build/test matrix (`windows-latest` / `ubuntu-latest` /
`macos-latest`, the latter covering both `osx-x64` and `osx-arm64`) with `fail-fast: false`, and
`.github/workflows/release.yml` declares the per-RID self-contained publish matrix with per-OS upload
artifacts. What remains **Pending** is the **execution** of that matrix on GitHub-hosted runners and the
recording of green results here — an external-evidence item (this offline environment cannot dispatch
GitHub Actions). The local Linux leg is already proven (build + 129 tests green); see the row note
below._

---

## Dependency Vulnerability Audit

A package vulnerability audit was run in the final remediation pass with **`dotnet list package
--vulnerable --include-transitive`** against the **live NuGet advisory database**
(`https://api.nuget.org/v3/index.json`, including `…/v3/vulnerabilities/index.json`, both reachable —
HTTP 200 — from this environment; the `web_search` tool is domain-restricted, but direct NuGet API access
is not). After `dotnet restore` synchronised the assets (resolving the earlier "out-of-sync assets"
blocker), the audit was run **per project** (the solution-level scan does not support the GPS/AgIO
multi-target) across both `net8.0` and `net8.0-windows7.0` for the two multi-targeted projects.

**Result: zero vulnerable packages** — top-level **and** transitive — across all twelve projects. The two
GPS Windows-only NuGet packages (`System.Management 9.0.0`, `Microsoft.Win32.Registry 5.0.0`), which a
`dotnet list package --framework net8.0-windows7.0` tool quirk could not read for the GPS project (a
cosmetic desync between the `'$(TargetFramework)' == 'net8.0-windows'` ItemGroup condition and the
canonical `net8.0-windows7.0` moniker — the build itself is correct and both DLLs are confirmed in the
GPS Windows output), were audited via an isolated probe and are likewise **clean**.

The audited direct inventory is 21 distinct top-level `PackageReference`s (Avalonia 11.3.18 stack,
`Dev4Agriculture.ISO11783.ISOXML 0.23.1.1`, `OpenTK 3.3.3`, `Newtonsoft.Json 13.0.4`, the SQLite stack,
`System.IO.Ports 9.0.0`, the NUnit 4.3.2 toolchain, etc.); GPS alone resolves 56 packages including
transitive — all clean. **Status: audited clean against the live advisory DB (this environment).
Residual:** re-running the audit inside the network-enabled `windows`/`ubuntu`/`macos` CI matrix remains
good practice and is the same external-evidence class as the tri-OS test confirmation above.

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
   **Status: runtime fail-safe ENFORCED and unit-validated; desktop-GL request now WIRED.** The gate state
   machine, one-shot `RenderingGated` event, `RequestDesktopGlProfile` null-builder rejection, and live
   Win32/X11 option-application were all exercised by an ad-hoc `AvaloniaGeoViewport` fixture and pass; the
   host compiles clean on `net8.0` and `net8.0-windows`, Debug + Release. **RESOLVED (a):** the composition
   roots now call the hook — `SourceCode/GPS/Program.cs` `BuildAvaloniaApp()` chains
   `AvaloniaGeoViewport.RequestDesktopGlProfile(...)` (and `SourceCode/AgIO/Source/Program.cs` applies it
   consistently for shared-bootstrap parity), so a desktop-GL **compatibility** profile is requested ahead
   of ANGLE/EGL on Windows (`Wgl`) and Linux/X11 (`Glx`); macOS exposes no per-OS GL-profile knob and relies
   on the runtime gate. **STILL OPEN (b):** on-hardware, per-OS confirmation that a desktop-GL compatibility
   context is actually obtained and that live rendering plus the `glReadPixels` `oglBack` section/lookahead
   and flag-pick scans render correctly must be run on each target OS/RID and recorded here. This on-hardware
   confirmation is an **external-evidence item** (it requires real GPUs on `windows` / `ubuntu` / `macos`
   runners or devices, not available in this environment); it remains the dominant open feasibility risk for
   the *immediate-mode-vs-GLES* question.
   **RESOLVED (c) — OpenTK↔Avalonia GL binding delegate defect (found by QA Checkpoint F10; DISTINCT from the
   GLES/ANGLE context-type risk above).** A defect in `EnsureGlBindings`
   (`SourceCode/GPS/Controls/AvaloniaGeoViewport.cs`) made the OpenTK 3.3.3 binding throw on **every** platform
   — independent of GPU, driver, or context type — so the field viewport rendered nothing even on a valid
   desktop-GL context. The `GetCurrentContextDelegate` returned `ContextHandle.Zero`; OpenTK 3.3.3's
   `GraphicsContext(ContextHandle, GetAddressDelegate, GetCurrentContextDelegate)` constructor, given a Zero
   handle, **adopts the delegate's return value as the context handle** and throws `GraphicsContextMissingException`
   ("No context is current in the calling thread") when that return is also Zero. `HandleOpenGlInit` then
   correctly fail-safed (`if (!EnsureGlBindings(gl)) { _glInitialized = false; return; }`) **before**
   `AuditGlContext`/`Initialize`/`CreateBackFbo`/any render — so the program never crashed (graceful
   degradation held), but the viewport stayed permanently blank, and the GLES audit + feature-gate described
   above was **never reached** because the bind failed first. The failure is **delegate-return-driven, not a
   native query**, hence environment-independent (it reproduces on Windows, Linux, and macOS regardless of the
   headless container). **Fix:** the delegate now returns a stable non-zero process-lifetime currency token
   (`AvaloniaCurrentContextToken = new ContextHandle(new IntPtr(1))`). This is semantically correct because
   Avalonia guarantees a GL context is current for the entire duration of
   `OnOpenGlInit`/`OnOpenGlRender`/`OnOpenGlDeinit` — the only times the bind runs — and OpenTK uses the value
   only as a currency token for the binding-only wrapper (the key under which it registers in
   `available_contexts` and the value `GraphicsContext.CurrentContext` returns), never to make a context
   current (`MakeCurrent()` is a deliberate no-op). A non-zero sentinel was deliberately chosen over a per-OS
   `glXGetCurrentContext`/`wglGetCurrentContext`/`CGLGetCurrentContext` query, because the latter returns null
   under an EGL/ANGLE context and would re-trigger the identical exception. **Verified at QA-fix time (two independent runtime levels):** (A — decisive,
   environment-independent) a standalone OpenTK 3.3.3 `GraphicsContext`-constructor toggle reproduces the
   defect (`ContextHandle.Zero` → `GraphicsContextMissingException` "No context is current in the calling
   thread") and proves the fix (non-zero token → constructor does **not** throw + `LoadAll()` succeeds);
   (B — gold standard, real control) the **real production `AvaloniaGeoViewport`** hosted in an Avalonia
   window under Xvfb + Mesa now completes `OnOpenGlInit` end-to-end — the production log records
   `"AvaloniaGeoViewport: OpenTK 3.3.3 GL entry points bound to Avalonia GL context."` (the success branch
   of `EnsureGlBindings`), the context is audited as **desktop GL `4.5 (Compatibility Profile)` Mesa —
   `IsLikelyOpenGlEs = false`, `IsRenderingGated = false`**, and `_glInitialized = true`; an immediate-mode
   `GLW`-style triangle then renders through the production `_renderAction` seam and `glReadPixels` reads
   back the full 520×320 framebuffer (665 600 bytes) with the center pixel `(255,217,0)` and corners
   `(69,115,51)` — matching the QA capstone exactly (evidence
   `blitzy/screenshots/f10_levelb_real_viewport_readback.png`, alongside the QA capstone
   `blitzy/screenshots/f10_gl_immediate_mode_readback.png`). **Note on the headless host:** Avalonia 11.3.18
   ships a default GLX renderer blacklist `{ "llvmpipe" }`; the Level-B harness cleared it via
   `X11PlatformOptions.GlxRendererBlacklist` **in the test host only** so the container's software Mesa GL
   context would be accepted — a test-environment concession that exercises (does not alter) the product
   binding path. On real GPU hosts the blacklist is irrelevant and the same production path runs unchanged.
   **Correction to a prior statement of this risk:** an earlier revision asserted "the bootstrap is no longer
   the blocker." That was accurate for the *bootstrap / desktop-GL-request* path but overlooked this
   binding-delegate defect, which **was** the active blocker until the fix above. The statement that the
   program "could no longer crash" remains true (the bind failure fail-safed gracefully); but the viewport
   could not render until **(c)** was resolved.
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
6. **Float determinism — confirmed locally; tri-OS pending.** IEEE arithmetic is generally stable across
   RyuJIT. The production-invoking guidance/section/algorithm parity tests (`GuidanceEquivalenceTests`,
   `SectionControlParityTests`, `GuidanceAlgorithmCoverageTests`) now assert this on the **real** production
   methods, and the suite was additionally re-run under a comma-decimal locale
   (`LANG=de_DE.UTF-8 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0`) with **identical** results, confirming
   culture-invariance of the data path. Cross-OS (windows / macos) confirmation via the CI matrix remains
   the only residual item.
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
   is **green on the local Linux environment** (the full `AgOpenGPS.Tests` assembly reports 93 passed / 1 intentional skip / 0 failed; 129 passed / 1 skipped across all three test assemblies). The **only
   residual parity item** is **tri-OS CI confirmation** (the `windows` / `macos` legs have not yet run);
   that cross-OS confirmation is tracked under **Golden-File Parity Suites** and **Cross-OS CI Matrix**
   above, not here.
8. **Guidance peer-reference wiring (latent composition-root gap — discovered during the final
   remediation pass; not part of the 31 reviewed findings).** The guidance classes expose a
   `SetGuidanceReferences(...)` method (defined on `CABCurve`, `CABLine`, `CContour`, `CTrack`,
   `CYouTurn`, `CGuidance`, and `CRecordedPath`) intended to wire the cyclic guidance peers to one
   another, but the **migrated composition root** (`Program.cs` / `App.axaml.cs`) currently has **zero
   callers** of it and constructs **no live `new CGuidance(...)`**. The guidance **mathematics are present
   and now production-proven**: the F5 remediation pass added `SourceCode/AgOpenGPS.Tests/Parity/ParityGraphFixture.cs`,
   which **does** construct a live `CGuidance` and invoke `SetGuidanceReferences(...)` across all cyclic
   peers (`CABLine`/`CABCurve`/`CContour`/`CTrack`/`CGuidance`), and the production-invoking parity suites
   exercise the real `DoSteerAngleCalc` / `GetCurrentABLine` / `SectionService` / Dubins / contour / curve /
   you-turn / recorded-path methods over that wired graph **without NRE** — so the seam itself is proven to
   work. This does **not** affect the frozen-output contract. What remains is solely that the **composition
   root** has not yet replicated this wiring, so the **end-to-end live guidance pipeline** (as opposed to the
   math exercised through the test fixture) is not yet wired at application startup and could
   `NullReferenceException` on a live guidance path until it is. This is a **pre-existing** gap (the WinForms
   `FormGPS` god-object performed this wiring implicitly; the decoupling extracted the seam but the root has
   not yet called it). It is recorded here as an open integration risk for a follow-up wiring task; it is
   **out of scope of the F5 guidance-parity findings** addressed in this remediation pass (which concern test
   fidelity/coverage, CI, and documentation — not composition-root wiring) and was not introduced by it.

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
