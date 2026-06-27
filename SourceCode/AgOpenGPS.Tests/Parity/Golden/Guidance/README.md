<!-- [XPLAT] new for the net48/WinForms → net8.0/Avalonia migration — see MIGRATION_DOCS/TRANSITION_MAP.md -->
# Guidance Parity Goldens

This folder holds the **guidance fix-sequence inputs and expected steer/section outputs**
captured from the current **Windows/net48 baseline** build. The artifacts are consumed by
[`GuidanceEquivalenceTests.cs`](../../GuidanceEquivalenceTests.cs) (in the parent `Parity/`
folder), which feeds the identical fix sequence through the migrated guidance math and asserts the
outputs reproduce the baseline — proving the **Stanley** and **Pure-Pursuit** steering algorithms
and the **section-state** logic are unchanged after the migration (AAP §0.2.2, §0.6.4; frozen
contract [`docs/architecture.md`](../../../../../docs/architecture.md)).

This README is **documentation only**: it *describes* what each golden must contain so engineers can
later capture the real CSVs and so anyone reading the parity suite understands the contract. It
contains **no code**, declares **no dependencies**, and neither adds nor re-declares any
`.gitattributes` or `.csproj` (those are owned elsewhere — see
[Byte-stability & staging](#byte-stability--staging-owned-elsewhere)).

Comparison is **numeric**:

- **Floating-point steer angles** — tolerance `Within(0.001)` (equivalently `Math.Abs(actual - expected) < 0.001`).
- **Integer / bitmask section state** — **exact** (no tolerance), held in 64-bit unsigned values.

## Expected artifacts

The fixture resolves each file next to the test assembly with a **case-sensitive** `Guidance`
segment:

```
Path.Combine(TestContext.CurrentContext.TestDirectory, "Parity", "Golden", "Guidance", "<file>")
```

so the names below are **exact and case-sensitive** — Linux and macOS filesystems are
case-sensitive (AAP §0.6.5), so any casing drift silently breaks resolution. The category folder
name is exactly `Guidance` (capital `G`); the file names are all lowercase.

| File | Data | Compared |
|------|------|----------|
| `stanley.csv` | Stanley steer-angle inputs → expected steer angle per row | `Within(0.001)` |
| `purepursuit.csv` | Pure-Pursuit steer-angle inputs → expected steer angle per row | `Within(0.001)` |
| `sections.csv` | Section-coverage inputs → expected on/off bitmask per row | **exact** |

> **These files are not present yet — and must not be fabricated.** A `.gitkeep` keeps the folder
> under version control until they are captured. See [Capturing the goldens](#capturing-the-goldens).

## CSV schema

All three goldens are **UTF-8** plain text. Blank lines and `#`-prefixed comment lines are ignored;
the **first** surviving line is the **schema header** and is skipped; every remaining line is a data
row whose cells are split on commas and trimmed. Numeric cells use a **period (`.`) decimal
separator** and are parsed with **`System.Globalization.CultureInfo.InvariantCulture`**. A
comma-decimal locale (e.g. `de-DE`) must never change a value: `0.64` is always `0.64`, never
`0,64`. Each consuming test also asserts at least **one data row** is present once the artifact is
captured.

The column order below is **authoritative** — it is the exact order the fixture parses (a mismatch
in column count or order fails the row). The headers are illustrative *schema* lines, not golden
data.

### `stanley.csv`

```
distanceError,headingErrorRad,speed,distanceGain,headingGain,expectedSteerAngleDeg
```

| Column | Meaning | Units |
|--------|---------|-------|
| `distanceError` | cross-track distance error of the steer axle (XTE) | meters |
| `headingErrorRad` | heading error fed to the algorithm | radians |
| `speed` | forward speed used in the documented denominator (`> 0`) | m/s |
| `distanceGain` | Stanley distance-error gain (`stanleyDistanceErrorGain`) | — |
| `headingGain` | Stanley heading-error gain (`stanleyHeadingErrorGain`) | — |
| `expectedSteerAngleDeg` | baseline steer angle, clamped to ±`maxSteerAngle` | degrees |

### `purepursuit.csv`

```
error,wheelbase,lookahead,expectedSteerAngleDeg
```

| Column | Meaning | Units |
|--------|---------|-------|
| `error` | goal-point heading error | radians |
| `wheelbase` | vehicle wheelbase | meters (default `3.3`) |
| `lookahead` | goal-point look-ahead distance | meters |
| `expectedSteerAngleDeg` | baseline steer angle, clamped to ±`maxSteerAngle` | degrees |

Exact units and sign conventions follow the Windows/net48 baseline producer (`CGuidance` applies
separate distance/heading gains and a sign convention; `CTrackMethods` supplies the goal-point
math). **The capture step is authoritative** — these tables document the contract, not the bytes.

### `sections.csv`

```
sectionCount,coverageBitmaskHex,expectedOnBitmaskHex
```

| Column | Meaning |
|--------|---------|
| `sectionCount` | active section count — **1–16 unique**, up to **64 same-width** |
| `coverageBitmaskHex` | requested/coverage bitmask, hex (optional `0x` prefix, up to 64 bits) |
| `expectedOnBitmaskHex` | resulting on bitmask, hex, masked to the active section range |

Bitmasks hold up to **64 bits** — parse and compare them as 64-bit unsigned integers, **exactly**
(no tolerance).

## Frozen guards & formulas

Steering guards and section semantics are frozen by the migration (AAP §0.2.2, §0.7.1; literals in
[`docs/settings.md`](../../../../../docs/settings.md)):

- `maxSteerAngle = 30°` (`setVehicle_maxSteerAngle`) — steer outputs are clamped to **±30°**.
- `maxAngularVelocity = 0.64°/s` (`setVehicle_maxAngularVelocity`).
- `wheelbase = 3.3 m` (`setVehicle_wheelbase`).
- Section control: **1–16 unique** sections, up to **64 same-width** sections.

The documented algorithm formulas the goldens encode
([`docs/architecture.md`](../../../../../docs/architecture.md)):

- **Stanley** (`setVehicle_isStanleyUsed = true`) — `Classes/CGuidance.cs` → `DoSteerAngleCalc()`:

  ```
  steerAngle = atan((distanceError * gain) / speed) + headingError * gain
  ```

- **Pure Pursuit** (`setVehicle_isStanleyUsed = false`) — `Classes/CTrackMethods.cs` → `GoalPoint()`:

  ```
  steerAngle = atan2(2 * wheelbase * sin(error), lookahead)
  ```

## Guarded-golden strategy

Until the real CSVs are captured, `GuidanceEquivalenceTests` resolves each path and, if the file is
missing, calls **`Assert.Ignore(...)`** with a message of the form:

```
Golden artifact not yet captured: <path> — tracked as an open risk in MIGRATION_DOCS/PARITY_REPORT.md
```

The suite therefore stays **green and discoverable** with `dotnet test` on `windows` / `ubuntu` /
`macos` (including `osx-arm64`) while goldens are still being produced. **Ignored** is the expected
state for any not-yet-captured artifact; the test flips to an enforced numeric assertion the moment
its CSV is committed. Every not-yet-captured artifact is tracked as an **open risk** in
[`MIGRATION_DOCS/PARITY_REPORT.md`](../../../../../MIGRATION_DOCS/PARITY_REPORT.md). The `.gitkeep`
beside this README keeps the folder under version control before any CSV exists.

## Capturing the goldens

The CSVs are captured **once**, from the running **Windows/net48 baseline** build:

1. Drive the baseline guidance producers in `SourceCode/GPS/Classes/` — `CGuidance.cs`
   (Stanley `DoSteerAngleCalc()`), `CTrack.cs` / `CTrackMethods.cs` (Pure-Pursuit goal-point math),
   `CAHRS.cs` (heading/roll fusion), and `CSection.cs` (section state) — over a representative fix
   sequence.
2. Record each input row and the baseline's resulting steer angle / section bitmask using
   **`InvariantCulture`** and **period** decimals, in the schema above (steer angles in degrees,
   already clamped to ±`maxSteerAngle`; bitmasks in hex).
3. Save as `stanley.csv` / `purepursuit.csv` / `sections.csv` here, **commit**, and **re-run the
   parity suite on all three operating systems** to confirm equivalence.

> **Do not hand-write or synthesize CSV content.** Fabricated rows assert against themselves and
> produce **false** parity. Goldens must always come from the running baseline producer. When a
> producer changes, regenerate the golden **and** update the fixture expectations **in lockstep**.

## Byte-stability & staging (owned elsewhere)

Two pieces of plumbing make these comparisons stable and discoverable at runtime. **Neither is
configured in this folder** — they are described here only so the contract is understood
end-to-end:

- **EOL/encoding normalization is disabled** for these artifacts by the **root** `.gitattributes`
  rule `SourceCode/AgOpenGPS.Tests/Parity/** -text` (owned by the root agent — **not** configured
  here). This keeps the on-disk text identical to the committed text on every Windows/Linux/macOS
  checkout, so byte/numeric comparisons stay stable.
- **Artifacts are copied next to the test assembly** by the parent
  `SourceCode/AgOpenGPS.Tests/AgOpenGPS.Tests.csproj` rule
  `Parity\Golden\**\*` → `CopyToOutputDirectory=PreserveNewest` (owned by the parent agent —
  **not** modified here). This is what lets the fixture resolve them through
  `TestContext.CurrentContext.TestDirectory` on every OS, including `osx-arm64`.

## Provenance

- Producer subsystem: `SourceCode/GPS/Classes/`
  (`CGuidance.cs`, `CTrack.cs`, `CTrackMethods.cs`, `CAHRS.cs`, `CSection.cs`).
- Staging pattern modeled on `SourceCode/AgLibrary.Tests/Settings/` — `TestSettings.xml` is staged
  via `CopyToOutputDirectory` and round-trip-compared by `XmlSettingsHandlerTests.cs`.
- Float-tolerance style mirrors the existing `AgOpenGPS.Core.Tests` geometry tests
  (`Math.Abs(a - b) < 0.001`).
- Store-wide index: [`../README.md`](../README.md). Frozen guidance contract:
  [`docs/architecture.md`](../../../../../docs/architecture.md).

## Cross-platform notes

Per the cross-cutting parity risks in AAP §0.6.5:

- **Exact-case file/folder names** — `Guidance` and the three lowercase `*.csv` names must match
  character-for-character (Linux/macOS are case-sensitive).
- **Preserved line endings** — pinned by the root `.gitattributes` rule above, so a CRLF↔LF rewrite
  can never perturb a comparison.
- **Invariant numeric I/O** — all parsing uses `InvariantCulture` (period decimal separator), so a
  comma-decimal locale cannot corrupt a golden's values.
- **`Path.Combine` everywhere** — paths are always composed with `Path.Combine`, never a hard-coded
  `\` or `/`.

## See also

- [`../README.md`](../README.md) — the store-wide golden-artifact index for the `Parity/Golden/` tree.
- [`../../GuidanceEquivalenceTests.cs`](../../GuidanceEquivalenceTests.cs) — the consuming parity
  fixture that resolves and numerically compares these files.
- [`docs/architecture.md`](../../../../../docs/architecture.md) — the frozen guidance/steering
  contract (Stanley and Pure-Pursuit formulas, code locations).
- [`docs/settings.md`](../../../../../docs/settings.md) — the frozen steering-guard literals
  (`maxSteerAngle`, `maxAngularVelocity`, `wheelbase`).
- [`MIGRATION_DOCS/PARITY_REPORT.md`](../../../../../MIGRATION_DOCS/PARITY_REPORT.md) — open-risk
  tracking for any not-yet-captured or unverified guidance golden.
