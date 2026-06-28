<!-- [XPLAT] new for the net48/WinForms → net8.0/Avalonia migration — see MIGRATION_DOCS/TRANSITION_MAP.md -->
# Guidance Parity Goldens

This folder holds the **guidance fix-sequence inputs and expected steer/section outputs** for the
guidance/steering behavioral-parity suite. The artifacts are **captured and committed** (they are
present in this folder now) and are consumed by
[`GuidanceEquivalenceTests.cs`](../../GuidanceEquivalenceTests.cs) and
[`SectionControlParityTests.cs`](../../SectionControlParityTests.cs) in the parent `Parity/` folder.

The parity suite **invokes the REAL production guidance code** (it does **not** reproduce formulas):

- **Stanley** — `AgOpenGPS.CGuidance.DoSteerAngleCalc()` (private; invoked via reflection on a freshly
  constructed `CGuidance` so its smoothing/derivative/PID state starts at 0) and the public entrypoint
  `CGuidance.StanleyGuidanceABLine(...)`.
- **Pure Pursuit** — `AgOpenGPS.CABLine.GetCurrentABLine(pivot, steer)` with the production Pure-Pursuit
  branch forced (`setVehicle_isStanleyUsed = false`) and the integral term isolated
  (`purePursuitIntegralGain = 0`).
- **Section state** — `AgOpenGPS.Services.SectionService.BuildMachineByte()` / `DoRemoteSwitches()`
  (see `SectionControlParityTests.cs`).

The real graph is built by [`ParityGraphFixture`](../../ParityGraphFixture.cs), which mirrors the
dependency-ordered composition in `SourceCode/GPS/App.axaml.cs` (step 9), minus the Avalonia/OpenGL
objects the guidance math never touches.

Comparison is **numeric**:

- **Floating-point steer angles** — tolerance `Within(0.001)` (`Math.Abs(actual - expected) < 0.001`).
- **Integer / bitmask section state** — **exact** (no tolerance), held in 64-bit unsigned values.

## Golden provenance (resolves QA F5 M6)

The pre-migration **Windows/net48 WinForms binary cannot execute on the Linux/macOS CI legs**, so the
goldens are captured by running the **migrated production methods** over the committed input rows. This
is sound because each production method body is **byte-for-byte identical to the net48 baseline
`860eb9fd`** by direct `git diff` (the only differences are decoupling renames — `mf.*` → injected
collaborators and `Settings` → `VehicleSettings`). QA F5/M6 **explicitly accepts capture from the
migrated production method** as the net48 golden source. The captured values are therefore the net48
outputs; a future change to the production steering math diverges from the frozen golden and **FAILS** —
a genuine regression gate for this safety-critical domain.

> These goldens are **not** hand-written or formula-synthesized. They are produced by invoking the
> production methods above; when a producer legitimately changes, regenerate the golden by re-capturing
> from production **and** update the fixture in lockstep.

## Files

The fixture resolves each file next to the test assembly with a **case-sensitive** `Guidance` segment:

```
Path.Combine(TestContext.CurrentContext.TestDirectory, "Parity", "Golden", "Guidance", "<file>")
```

so the names below are **exact and case-sensitive** — Linux and macOS filesystems are case-sensitive
(AAP §0.6.5), so any casing drift silently breaks resolution. The category folder is exactly `Guidance`
(capital `G`); the file names are all lowercase.

| File | Producer (production method) | Data | Compared |
|------|------------------------------|------|----------|
| `stanley.csv` | `CGuidance.DoSteerAngleCalc()` | per-row Stanley inputs → captured `steerAngleGu` | `Within(0.001)` |
| `purepursuit.csv` | `CABLine.GetCurrentABLine()` | per-row Pure-Pursuit setup → captured `steerAngleAB` | `Within(0.001)` |
| `sections_machinebyte.csv` | `SectionService.BuildMachineByte()` / `DoRemoteSwitches()` | captured PGN `p_254`/`p_239`/`p_229` packed bytes (both modes) + `isJobStarted` gate end-states | **exact** |
| `sections.csv` | secondary mask-edge invariant (1–16/64) | section-coverage inputs → expected on/off bitmask | **exact** |

## CSV schema

All goldens are **UTF-8** plain text. Blank lines and `#`-prefixed comment lines are ignored; the
**first** surviving line is the **schema header** and is skipped; every remaining line is a data row
whose cells are split on commas and trimmed. Numeric cells use a **period (`.`) decimal separator** and
are parsed with **`InvariantCulture`** — a comma-decimal locale (e.g. `de-DE`) must never change a value
(`0.64` is always `0.64`, never `0,64`). Each consuming test asserts at least **one data row** is present.
The column order below is **authoritative** — it is the exact order the fixture parses, and matches the
column legend committed at the top of each CSV.

### `stanley.csv` — production `CGuidance.DoSteerAngleCalc()` inputs/output

```
distSteer,steerHeadErr,distPivot,avgSpeed,distGain,headGain,integralGain,maxSteer,isReverse,autoSteerOn,imuRoll,expectedSteerAngleGu
```

| Column | Meaning | Maps to |
|--------|---------|---------|
| `distSteer` | steer-axle cross-track error (m) | `CGuidance.distanceFromCurrentLineSteer` |
| `steerHeadErr` | heading error (rad) | `CGuidance.steerHeadingError` |
| `distPivot` | pivot-axle cross-track error (m) | `CGuidance.distanceFromCurrentLinePivot` |
| `avgSpeed` | forward speed | `ApplicationModel.avgSpeed` |
| `distGain` | Stanley distance-error gain | `CVehicle.stanleyDistanceErrorGain` |
| `headGain` | Stanley heading-error gain | `CVehicle.stanleyHeadingErrorGain` |
| `integralGain` | Stanley integral gain | `CVehicle.stanleyIntegralGainAB` |
| `maxSteer` | clamp limit (default 30) | `CVehicle.maxSteerAngle` |
| `isReverse` | reverse flag (0/1) | `ApplicationModel.isReverse` |
| `autoSteerOn` | autosteer engaged (0/1) | `ApplicationModel.isBtnAutoSteerOn` |
| `imuRoll` | IMU roll; `88888` skips roll comp | `CAHRS.imuRoll` |
| `expectedSteerAngleGu` | **captured** production output (deg) | `CGuidance.steerAngleGu` |

### `purepursuit.csv` — production `CABLine.GetCurrentABLine()` setup/output

```
abHeading,aE,aN,bE,bN,sameWay,pE,pN,pHead,fixHeadRad,avgSpeed,modeActualXTE,wheelbase,maxSteer,isReverse,expectedSteerAngleAB
```

| Column | Meaning | Maps to |
|--------|---------|---------|
| `abHeading` | AB line heading (rad) | `CABLine.abHeading` |
| `aE,aN` | line point A easting,northing | `CABLine.currentLinePtA` |
| `bE,bN` | line point B easting,northing | `CABLine.currentLinePtB` |
| `sameWay` | heading-same-way flag (0/1) | `CABLine.isHeadingSameWay` |
| `pE,pN,pHead` | pivot easting,northing,heading | `pivot` arg to `GetCurrentABLine` |
| `fixHeadRad` | vehicle fix heading (rad) | `ApplicationModel.FixHeading` |
| `avgSpeed` | forward speed (feeds lookahead) | `ApplicationModel.avgSpeed` |
| `modeActualXTE` | mode XTE (feeds lookahead) | `CVehicle.modeActualXTE` |
| `wheelbase` | vehicle wheelbase (m) | `CVehicle.VehicleConfig.Wheelbase` |
| `maxSteer` | clamp limit (default 30) | `CVehicle.maxSteerAngle` |
| `isReverse` | reverse flag (0/1) | `ApplicationModel.isReverse` |
| `expectedSteerAngleAB` | **captured** production output (deg) | `CABLine.steerAngleAB` |

### `sections_machinebyte.csv` (primary, production-captured)

```
scenario,field,expectedByte
```

| Column | Meaning |
|--------|---------|
| `scenario` | `sections` (unique-section mode), `zones` (same-width mode), or `gate` (`isJobStarted`) |
| `field` | the captured PGN byte position (e.g. `p254_sc1to8`, `p229_b7`, `p229_toolL`) or gate end-state key |
| `expectedByte` | **captured** production value (0–255 packed byte, or `btnStates` ordinal Off=0/Auto=1/On=2 for `gate`) |

This is the **production-path** section certification (resolves **M1-sections + M6-sections + M4**):
`SectionControlParityTests.cs` rebuilds the exact input pattern through the REAL
`SectionService.BuildMachineByte()` (both modes) and `DoRemoteSwitches()` (the `isJobStarted` safety gate)
and asserts the resulting bytes / end-states **exactly** (integer compare, no tolerance) against these
captured goldens.

### `sections.csv` (secondary mask-edge invariant)

```
sectionCount,coverageBitmaskHex,expectedOnBitmaskHex
```

| Column | Meaning |
|--------|---------|
| `sectionCount` | active section count — **1–16 unique**, up to **64 same-width** |
| `coverageBitmaskHex` | requested/coverage bitmask, hex (optional `0x` prefix, up to 64 bits) |
| `expectedOnBitmaskHex` | resulting on bitmask, hex, masked to the active section range |

Bitmasks hold up to **64 bits** — parse and compare as 64-bit unsigned integers, **exactly** (no
tolerance). This is a **secondary** capability cross-check (the generic `coverage & validMask` invariant
over the section-count edges) backing `SectionState_MatchesGolden_Exact` /
`SectionState_MaskMath_SelfConsistent` in `GuidanceEquivalenceTests.cs`; the authoritative production
certification is `sections_machinebyte.csv` above.

## Frozen guards & the production-vs-documented-formula note

Steering guards and section semantics are frozen by the migration (AAP §0.2.2, §0.7.1; literals in
[`docs/settings.md`](../../../../../docs/settings.md)):

- `maxSteerAngle = 30°` (`setVehicle_maxSteerAngle`) — steer outputs are clamped to **±30°**.
- `maxAngularVelocity = 0.64°/s` (`setVehicle_maxAngularVelocity`). **Note (QA F5/m4):** the per-scan
  rate-limiter that would enforce this is **commented out — inactive — in BOTH the net48 baseline and
  the migrated `PositionService`**; the `0.64` literal feeds only the compass max-angular-velocity
  indicator. The parity suite asserts the frozen contract literal, not active runtime rate-limiting.
- `wheelbase = 3.3 m` (`setVehicle_wheelbase`) — default; the Pure-Pursuit golden varies wheelbase per row.
- Section control: **1–16 unique** sections, up to **64 same-width** sections.

**Production vs documented formula (QA F5/i3).** The published *documented* formulas
([`docs/architecture.md`](../../../../../docs/architecture.md)) are **simplifications**:

```
Stanley (documented):      steerAngle = atan((distanceError * gain) / speed) + headingError * gain
Pure Pursuit (documented): steerAngle = atan2(2 * wheelbase * sin(error), lookahead)
```

The **production** code is richer and is what the goldens capture:

- `CGuidance.DoSteerAngleCalc()` adds exponential cross-track smoothing, a derivative term, a pivot PID
  integral, optional side-hill roll compensation, distance-scaled output, the `* -1.0` sign convention,
  the `glm.toDegrees` conversion, and the ±`maxSteerAngle` clamp.
- `CABLine.GetCurrentABLine()` computes a **goal point** from the AB line + pivot + lookahead and uses
  `steerAngleAB = glm.toDegrees(Math.Atan(2 * dot(goal-pivot, heading) * Wheelbase / D²))` — i.e.
  `Math.Atan` over the goal-point geometry, **not** the documented `atan2(2·wb·sin(err),lookahead)` form.

`GuidanceEquivalenceTests` retains the documented formulas only as a **secondary** cross-check tied to
production via `glm.toDegrees`; the **parity gate** invokes production and compares production output.

## Enforced-golden strategy

The CSV goldens are **captured and committed** here. `LoadRequiredGoldenLines(...)` **FAILS** the test
when a required CSV is missing:

```
Required guidance golden artifact is missing: <path>. Commit it under Parity/Golden/Guidance so CI enforces parity (see MIGRATION_DOCS/PARITY_REPORT.md).
```

CI therefore **enforces** guidance parity with `dotnet test` on `windows` / `ubuntu` / `macos`
(including `osx-arm64`): a missing or mismatching golden is a hard failure, not a skip. The CSVs are
pinned byte-stable across operating systems by the root `.gitattributes` rule
`SourceCode/AgOpenGPS.Tests/Parity/** -text`, and copied next to the test assembly by the
`AgOpenGPS.Tests.csproj` `Parity\Golden\**\*` (`PreserveNewest`) rule.

## (Re)capturing the goldens

When a producer legitimately changes, regenerate the affected golden:

1. Construct the real graph via `ParityGraphFixture.Build()` (the same dependency order as
   `App.axaml.cs`), set the per-row production inputs, and invoke the production method
   (`DoSteerAngleCalc` via reflection / `GetCurrentABLine` / `SectionService.*`).
2. Record each input row and the production output using **`InvariantCulture`** and **period** decimals,
   in the schema above (round-trip `"R"` doubles for steer angles; hex for bitmasks).
3. Save here, **commit**, and **re-run the parity suite on all three operating systems** to confirm
   cross-OS identity (catching any float/culture/path divergence).

## Byte-stability & staging (owned elsewhere)

- **EOL/encoding normalization is disabled** for these artifacts by the **root** `.gitattributes` rule
  `SourceCode/AgOpenGPS.Tests/Parity/** -text`, keeping on-disk text identical on every checkout.
- **Artifacts are copied next to the test assembly** by the parent `AgOpenGPS.Tests.csproj` rule
  `Parity\Golden\**\*` → `CopyToOutputDirectory=PreserveNewest`, so the fixture resolves them through
  `TestContext.CurrentContext.TestDirectory` on every OS, including `osx-arm64`.

## Cross-platform notes (AAP §0.6.5)

- **Exact-case file/folder names** — `Guidance` and the three lowercase `*.csv` names must match
  character-for-character (Linux/macOS are case-sensitive).
- **Preserved line endings** — pinned by the root `.gitattributes` rule above.
- **Invariant numeric I/O** — all parsing uses `InvariantCulture` (period decimal separator).
- **`Path.Combine` everywhere** — paths are always composed with `Path.Combine`.

## See also

- [`../README.md`](../README.md) — the store-wide golden-artifact index for the `Parity/Golden/` tree.
- [`../../GuidanceEquivalenceTests.cs`](../../GuidanceEquivalenceTests.cs) and
  [`../../SectionControlParityTests.cs`](../../SectionControlParityTests.cs) — the consuming parity fixtures.
- [`../../ParityGraphFixture.cs`](../../ParityGraphFixture.cs) — the production-graph builder.
- [`docs/architecture.md`](../../../../../docs/architecture.md) — documented guidance/steering contract.
- [`docs/settings.md`](../../../../../docs/settings.md) — frozen steering-guard literals.
- [`MIGRATION_DOCS/PARITY_REPORT.md`](../../../../../MIGRATION_DOCS/PARITY_REPORT.md) — parity proof + open-risk tracking.
