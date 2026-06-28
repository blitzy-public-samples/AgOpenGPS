<!-- [XPLAT] new for the net48/WinForms → net8.0/Avalonia migration — see MIGRATION_DOCS/TRANSITION_MAP.md -->

# Parity Golden — ISOXML Exports (SEMANTIC compare)

This folder stores the **ISOXML (ISO 11783-10) export goldens** captured from the current **Windows/net48 baseline** build. They are consumed by **`IsoXmlEquivalenceTests.cs`** in the parent `Parity/` folder, which proves that ISOXML **V3/V4 import/export semantics did not drift** across the net48/WinForms → net8.0/Avalonia migration.

Unlike every other category in `Golden/` (which is byte-exact or numeric), the comparison here is **SEMANTIC, not byte-exact**: the fixture parses both the freshly produced XML and the golden XML and asserts that the element/attribute **values** are equivalent, deliberately ignoring insignificant whitespace and attribute ordering.

## Expected artifacts

Resolved by the fixture via `Path.Combine(TestContext.CurrentContext.TestDirectory, "Parity", "Golden", "IsoXml", "<file>")`:

| File | Description |
|---|---|
| `V3_TASKDATA.XML` | ISOXML **V3** export (`ISO11783_TaskData` VersionMajor = Version3, VersionMinor = Item3) |
| `V4_TASKDATA.XML` | ISOXML **V4** export (`ISO11783_TaskData` VersionMajor = Version4, VersionMinor = Item2) |

A versioned `TASKDATA.XML` pair is acceptable **as long as the fixture and these goldens agree** on the names.

> **These files are captured and committed.** `V3_TASKDATA.XML` and `V4_TASKDATA.XML` are present in this folder. `IsoXmlEquivalenceTests` resolves each through `LoadRequiredGoldenXml(...)`, which **FAILS** the test when a required golden is missing — the **enforced-golden** strategy — so CI enforces ISOXML parity on `windows` / `ubuntu` / `macos` (including `osx-arm64`). The one exception is `IsoXmlExport_DrivenFromDomainGraph_RequiresFormGpsGraph`, which stays intentionally **Ignored** because it needs the FormGPS-assembled object graph (the committed goldens prove the export instead). Residual cross-OS verification status is recorded in `MIGRATION_DOCS/PARITY_REPORT.md`.

## Comparison mode — SEMANTIC (not byte)

ISOXML serializers may legitimately differ in insignificant whitespace and attribute order, so a raw-byte compare would yield false negatives. The fixture therefore:

- Parses both the produced and golden XML (via `System.Xml.Linq` `XDocument` or the `Dev4Agriculture.ISO11783.ISOXML` object model), and
- Asserts equivalence of the meaningful element/attribute **values** — version markers, partfield designator, boundary/headland polygon structures, guidance line/point structures, and coordinates (numeric values within tolerance) —
- while **ignoring** insignificant whitespace and attribute ordering.

## Documented limits the fixture asserts

- **Task/field name ≤ 248 bytes** — the ISOXML designator length cap (ties to the PGN `0xF3` field-name maximum).
- **AB-line + Curve export cap** — the exporter emits guidance tracks **only** for `TrackMode.AB` and `TrackMode.Curve`; all other track modes are skipped.
- **V3 vs V4 markers** — the two versions are distinguished by the root `ISO11783_TaskData` `VersionMajor`/`VersionMinor` attributes: **V3** = Major `Version3` / Minor `Item3`; **V4** = Major `Version4` / Minor `Item2`. The underlying geometry (boundary/headland polygons, AB A/B endpoints, curve points) is identical between versions; only the version markers and the V4 `GuidanceGroup`/`GuidancePattern` wrapping differ.

## How the fixture resolves these files

Each artifact is resolved at runtime via `Path.Combine(TestContext.CurrentContext.TestDirectory, "Parity", "Golden", "IsoXml", "<file>")` (case-sensitive `IsoXml`). When a required golden is missing the corresponding test **FAILS** (via `LoadRequiredGoldenXml`/`LoadRequiredGoldenPath`) so CI enforces parity on every OS; the goldens are copied next to the test assembly by the `AgOpenGPS.Tests.csproj` `Parity\Golden\**\*` (`PreserveNewest`) rule and pinned byte-stable by `.gitattributes`.

## Capturing the goldens

Goldens are produced **once** from the **Windows/net48 baseline** build by running the ISOXML exporter (`SourceCode/GPS/Protocols/ISOBUS/ISO11783_TaskFile.cs` → `Export(..., Version.V3)` and `Export(..., Version.V4)`), writing the resulting `TASKDATA.XML` here as `V3_TASKDATA.XML` / `V4_TASKDATA.XML`, committing, then re-running the suite on `windows`/`ubuntu`/`macos` to confirm semantic equivalence. **Do not fabricate or hand-edit TASKDATA content** — invented content would produce false parity results. When the producer changes, the goldens and the fixture expectations are updated in lockstep.

## Byte-stability & staging (owned elsewhere — do not configure here)

- **EOL/encoding** normalization is disabled for these artifacts by the **ROOT** `.gitattributes` rule `SourceCode/AgOpenGPS.Tests/Parity/** -text` (owned by the root agent). Do **not** add a `.gitattributes` here (and it is irrelevant to a semantic compare anyway).
- **Output staging** is configured by the parent `SourceCode/AgOpenGPS.Tests/AgOpenGPS.Tests.csproj` (`<None Update="Parity\Golden\**\*"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>`), which copies these files next to the test assembly so `TestContext.CurrentContext.TestDirectory` resolves them on all OSes including `osx-arm64`. Do **not** modify any `.csproj` here.

## Provenance

- **Producer subsystem:** `SourceCode/GPS/Protocols/ISOBUS/` — `ISO11783_TaskFile.cs` (exporter; `enum Version { V3, V4 }`), `IsoXmlParserHelpers.cs`, `IsoXmlFieldImporter.cs`, `CurveCABTools.cs`.
- **Library:** `Dev4Agriculture.ISO11783.ISOXML` 0.23.1.1 (referenced by the parent `AgOpenGPS.Tests.csproj`).
- **Staging pattern:** modeled on `SourceCode/AgLibrary.Tests/Settings/` (`TestSettings.xml` staged via `CopyToOutputDirectory` and round-trip-consumed by `XmlSettingsHandlerTests.cs`).

## Cross-platform notes

- The folder name is exactly **`IsoXml`** (capital `I`, capital `X`, lowercase `ml`) — Linux/macOS are case-sensitive, so it must match the fixture's path exactly.
- All numeric attribute parsing/formatting uses `InvariantCulture` (period decimal separator) so a comma-decimal locale cannot change a comparison.
- Paths are always built with `Path.Combine`.

A `.gitkeep` sits beside this README so the folder is tracked in git before any real golden artifact exists.
