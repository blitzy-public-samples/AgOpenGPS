<!-- [XPLAT] new for the net48/WinForms → net8.0/Avalonia migration — see MIGRATION_DOCS/TRANSITION_MAP.md -->
# Parity Golden-Artifact Store

This directory stores the **frozen baseline artifacts** consumed by the five parity fixtures in the parent [`Parity/`](../) folder (`PgnFrameGoldenTests`, `FieldRoundTripTests`, `IsoXmlEquivalenceTests`, `SettingsRoundTripTests`, `GuidanceEquivalenceTests`). Each artifact is captured **once** from the current **Windows/net48 baseline** build, and the cross-platform (net8.0/Avalonia) build must reproduce it **byte-for-byte — or _semantically_, for ISOXML —** on the full `windows` / `ubuntu` / `macos` CI matrix, including `osx-arm64`. This README is the single human-readable index for the store: it explains what lives here, how the fixtures consume it, how the goldens are captured, and which cross-cutting rules keep the comparisons stable. It contains **documentation only** — no goldens are hand-written here.

## Layout

```
Golden/
├── Pgn/        encoded PGN frames (*.bin) — byte-exact incl. additive CRC
├── Field/      GPS/IO field files (*.txt) — byte-exact load→save round-trip
├── IsoXml/     ISOXML exports (TASKDATA.XML) — SEMANTIC compare (not byte)
├── Settings/   settings goldens (*.xml) — byte-exact round-trip
└── Guidance/   fix-sequence inputs + expected outputs (*.csv) — numeric compare
```

The five category subfolders are named **exactly** `Pgn`, `Field`, `IsoXml`, `Settings`, and `Guidance`. Note the casing of `IsoXml` (capital `I`, capital `X`, lowercase `ml`): Linux and macOS filesystems are case-sensitive, so any casing drift silently breaks golden resolution.

## Expected artifacts

The table below summarizes which fixture owns each category and how it compares. The per-category lists that follow give the **exact** golden file names each fixture resolves.

| Category   | Consuming fixture              | Comparison mode                                                        |
|------------|--------------------------------|------------------------------------------------------------------------|
| `Pgn`      | `PgnFrameGoldenTests.cs`       | **byte-exact** (`File.ReadAllBytes` + `Is.EqualTo`)                     |
| `Field`    | `FieldRoundTripTests.cs`       | **byte-exact** (load → save round-trip)                                |
| `IsoXml`   | `IsoXmlEquivalenceTests.cs`    | **semantic** (parsed XML, *not* raw bytes)                             |
| `Settings` | `SettingsRoundTripTests.cs`    | **byte-exact** (round-trip)                                            |
| `Guidance` | `GuidanceEquivalenceTests.cs`  | **numeric** (`Within(0.001)` for floats; exact for section bitmasks)   |

### `Pgn/` — consumed by `PgnFrameGoldenTests.cs` · byte-exact

Encoded PGN frames, one `*.bin` per message id:

- `D0_latlon.bin` — 14 bytes
- `FE_autosteer.bin` — 14 bytes
- `EF_machine.bin` — 14 bytes
- `E5_sections.bin` — 16 bytes
- `D6_gps.bin` — 52 bytes
- `EB_dims.bin` — 38 bytes *(optional)*
- `EC_relay.bin` — 29 bytes *(optional)*

Each frame follows the frozen on-wire layout `[0x80 0x81 0x7F <PGN> <Len> <Data…> <CRC>]`, where the trailing byte is an **additive CRC** computed over bytes `[2 .. len-2]` (source-address byte through the last data byte). The protocol is loopback-only over ports **15555** (AOG listen) and **17777** (AgIO endpoint). This is a behavior-frozen contract — see [`docs/pgn-protocol.md`](../../../../docs/pgn-protocol.md).

### `Field/` — consumed by `FieldRoundTripTests.cs` · byte-exact

`GPS/IO/` field files, compared after a load → save round-trip:

- `Boundary.txt`
- `Tracks.txt`
- `Sections.txt`
- `Flags.txt`
- `Contour.txt`
- `Headland.txt`
- `Headlines.txt`
- `Tram.txt`
- `RecPath.txt`
- `Elevation.txt`
- `FieldPlane.txt`

### `IsoXml/` — consumed by `IsoXmlEquivalenceTests.cs` · semantic

ISOXML exports, compared as **parsed XML** rather than raw bytes (attribute order, whitespace, and encoding declarations may legitimately differ while remaining semantically equivalent):

- `V3_TASKDATA.XML`
- `V4_TASKDATA.XML`

A versioned `TASKDATA.XML` pair may be used instead, one per ISOXML version. Documented export limits that the goldens must respect: element/object **name ≤ 248 bytes**, and the **AB + Curve export cap**.

### `Settings/` — consumed by `SettingsRoundTripTests.cs` · byte-exact

Settings goldens, compared after a serialize → load → re-serialize round-trip:

- `Vehicle.xml`
- `Tool.xml`
- `Environment.xml`
- `Legacy.xml` — the legacy single-file input used to exercise the `CSettingsMigration` round-trip.

The settings schema shape is `configuration/userSettings/<Namespace.Type>/setting@name@serializeAs/value`. Doubles serialize with a **period** (`.`) decimal separator under `InvariantCulture`, so the bytes are stable regardless of the host locale.

### `Guidance/` — consumed by `GuidanceEquivalenceTests.cs` · numeric

Fix-sequence inputs together with their expected outputs, as CSV:

- `stanley.csv`
- `purepursuit.csv`
- `sections.csv`

Floating-point columns (e.g. steer angle) are compared with a tolerance of `Within(0.001)`; section-state bitmask columns are compared exactly. CSV cells use **period** decimals and are parsed with `InvariantCulture`.

## How the tests resolve these files

Every fixture resolves a golden relative to the test assembly's output directory, using a path of the form:

```
Path.Combine(TestContext.CurrentContext.TestDirectory, "Parity", "Golden", "<Category>", "<file>")
```

`<Category>` is one of the **case-sensitive** names `Pgn` / `Field` / `IsoXml` / `Settings` / `Guidance` exactly as spelled above. Building the path with `Path.Combine` (never a hard-coded separator) keeps resolution correct on Windows, Linux, and macOS alike.

**Guarded-golden strategy.** Until a given artifact actually exists on disk, the corresponding test calls `Assert.Ignore(...)` and self-reports as **Ignored** rather than failing. This keeps `dotnet test` **green and discoverable** on every OS while the real goldens are still being captured. "Ignored" is therefore the **expected state** for any not-yet-captured artifact, and each such gap is tracked as an open risk in [`MIGRATION_DOCS/PARITY_REPORT.md`](../../../../MIGRATION_DOCS/PARITY_REPORT.md). Once an artifact is committed, its test flips from Ignored to an enforced byte/semantic/numeric assertion automatically.

## Capturing the goldens

Goldens are produced **once**, from the **Windows/net48 baseline** build, by running the baseline encoders/serializers and writing their output to the matching `Golden/<Category>/<file>`:

- **PGN frames** — `GPS/Forms/PGN.Designer.cs` + `GPS/Services/PgnDispatcher.cs`
- **Field files** — `GPS/IO/`
- **ISOXML exports** — `GPS/Protocols/ISOBUS/`
- **Settings** — `GPS/Properties/` + `CSettingsMigration`
- **Guidance outputs** — `GPS/Classes/`

After writing the bytes, **commit** the artifact and **re-run the suite on all three operating systems** to confirm byte/semantic equivalence from the cross-platform build.

> **Do not fabricate goldens.** Never hand-write or synthesize `*.bin` / `*.txt` / `*.xml` / `*.csv` content here. A fabricated artifact would assert against itself and produce a **false** parity result. Goldens must always come from the running baseline producer listed above.

When an artifact is added, or whenever its producer changes, **both** the golden file **and** any fixture expectation must be updated **in lockstep** so that the comparison continues to reflect real baseline behavior.

## Byte-stability & staging (owned elsewhere)

Two pieces of plumbing make these comparisons stable and are configured **outside this folder** — they are documented here for orientation only and must **not** be re-declared in this directory:

- **EOL/encoding normalization is disabled** for these artifacts by the **root** `.gitattributes` rule `SourceCode/AgOpenGPS.Tests/Parity/**  -text`, so the bytes on disk equal the committed bytes on every Windows/Linux/macOS checkout and byte comparisons stay stable. **Owned by the root agent; not configured here.**
- **Artifacts are copied next to the test assembly** by the parent `AgOpenGPS.Tests.csproj` rule `Parity\Golden\**\*` → `CopyToOutputDirectory=PreserveNewest`, which is what lets the fixtures resolve them through `TestContext.CurrentContext.TestDirectory` on all OSes, including `osx-arm64`. **Owned by the parent agent; not configured here.**

## Provenance

This store is modeled on the existing golden-staging pattern in `SourceCode/AgLibrary.Tests/Settings/`, where `TestSettings.xml` is staged next to the test assembly via `CopyToOutputDirectory` and round-trip-compared by `XmlSettingsHandlerTests.cs`. The parity suite generalizes that proven load/save byte-compare approach across the five contract categories above.

## Cross-platform notes

Per the cross-cutting parity risks in AAP §0.6.5, the following invariants keep a golden identical across operating systems:

- **Exact-case file names.** Folder and file names must match character-for-character; Linux/macOS are case-sensitive (this is why `IsoXml` must keep its exact casing).
- **Preserved line endings.** EOL conversion is disabled via the root `.gitattributes` rule above, so a CRLF↔LF rewrite can never perturb a byte comparison.
- **Invariant numeric I/O.** All numeric serialization and parsing uses `InvariantCulture` (period decimal separator), so a comma-decimal locale cannot corrupt a golden's bytes or values.
- **`Path.Combine` everywhere.** Paths are always composed with `Path.Combine` / `Path.DirectorySeparatorChar`, never a hard-coded `\` or `/`.
