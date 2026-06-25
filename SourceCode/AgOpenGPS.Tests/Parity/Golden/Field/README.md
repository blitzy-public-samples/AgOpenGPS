<!-- [XPLAT] new for the net48/WinForms → net8.0/Avalonia migration — see MIGRATION_DOCS/TRANSITION_MAP.md -->

# Parity Goldens — Field Files

This directory stores the **byte-exact GPS/IO field-file artifacts** captured from the **Windows/net48 baseline**, consumed by `FieldRoundTripTests.cs` in the parent `Parity/` folder. Each artifact is a frozen reference output of one production `AgOpenGPS.IO` field-file handler. The cross-platform (net8.0/Avalonia) build must reproduce every one of them **byte-for-byte** on `windows`, `ubuntu`, and `macos` (including `osx-arm64`), proving the field-file formats under `SourceCode/GPS/IO/` did not drift across the migration and that end-of-line, culture, and path-separator hazards never corrupt them.

## What this folder proves

For each artifact, `FieldRoundTripTests` performs a strict load → save → re-read round-trip:

1. **Load the golden** from this folder.
2. **Stage** it into a temporary field directory under the handler's *real on-disk file name* (see the table below).
3. Call the production `AgOpenGPS.IO` `Load(...)` then `Save(...)` for that handler.
4. **Read the re-saved file back** from the temp directory.
5. **Assert byte equality** via `File.ReadAllBytes` + `Is.EqualTo` — a byte comparison, not a text comparison.

These are **text artifacts**: header/marker lines plus comma-separated values formatted with **`InvariantCulture`** (period decimal separator). A comma-decimal locale (for example `de-DE` or `fr-FR` on Linux/macOS) must never change a single byte — that locale-corruption risk is exactly the cross-platform hazard this suite guards (AAP §0.6.5).

## Expected artifacts (exact names)

The `Golden file` column is **character-for-character exact** and **case-sensitive**. The table also records the producing handler under `SourceCode/GPS/IO/`, the **real on-disk field file name** the handler reads and writes, and the leading marker / first line so the capture step is unambiguous.

| Golden file | Producer (`SourceCode/GPS/IO/`) | On-disk field file | Leading marker / first line |
|---|---|---|---|
| `Boundary.txt`  | `BoundaryFiles.cs`   | `Boundary.txt`     | `$Boundary` |
| `Tracks.txt`    | `TrackFiles.cs`      | `TrackLines.txt` ‡ | `$TrackLines` (variant `$TwolTracks`) |
| `Sections.txt`  | `SectionFiles.cs`    | `Sections.txt`     | no `$`-header — count-prefixed `vec3` patch blocks (may be empty) |
| `Flags.txt`     | `FlagFiles.cs`       | `Flags.txt`        | `$Flags` |
| `Contour.txt`   | `ContourFiles.cs`    | `Contour.txt`      | `$Contour` |
| `Headland.txt`  | `HeadlandFiles.cs`   | `Headland.txt`     | `$Headland` |
| `Headlines.txt` | `HeadlinesFiles.cs`  | `Headlines.txt`    | `$HeadLines` |
| `Tram.txt`      | `TramFiles.cs`       | `Tram.txt`         | `$Tram` |
| `RecPath.txt`   | `RecPathFiles.cs`    | `RecPath.txt`      | `$RecPath` |
| `Elevation.txt` | `Elevation.cs` (`ElevationFiles`) | `Elevation.txt` | timestamp line, then `$FieldDir` / `$Offsets` / `StartFix` |
| `FieldPlane.txt`| `FieldPlaneFiles.cs` | `Field.txt` ‡      | timestamp line, then `$FieldDir` / `$Offsets` / `StartFix` |

‡ The golden file name differs from the handler's real on-disk field file name. When staging the golden for round-trip, the test writes it into the temp field directory under the **on-disk** name (`TrackLines.txt`, `Field.txt`), runs `Load`/`Save`, then byte-compares the re-saved file against the golden.

`FileIOUtils.cs` is a shared low-level helper (invariant double formatting via `FileIoUtils.FormatDouble`, vec3 block reads) used by the handlers above. It is **not** a per-artifact handler and has **no golden of its own**.

## How the tests consume these files

Each fixture resolves its golden with the exact form:

`Path.Combine(TestContext.CurrentContext.TestDirectory, "Parity", "Golden", "Field", "<file>.txt")`

Never hard-code `/` or `\`; always build paths with `Path.Combine`. Paths are **case-sensitive** on Linux and macOS, so a single wrong letter silently breaks resolution (AAP §0.6.5).

**Guarded-golden strategy.** Until an artifact exists on disk, the corresponding `[Test]` calls `Assert.Ignore(...)` and self-reports **Ignored** (not Failed). This keeps `dotnet test` **green and discoverable** on every OS (`windows` / `ubuntu` / `macos`, including `osx-arm64`) while goldens are still being captured. Each not-yet-captured artifact is tracked as an open risk in `MIGRATION_DOCS/PARITY_REPORT.md`.

This Field suite **extends the proven round-trip byte-compare pattern** from `SourceCode/AgLibrary.Tests/Settings/XmlSettingsHandlerTests.cs` (which round-trips `TestSettings.xml`), upgraded from `File.ReadAllText` to **`File.ReadAllBytes`** so the comparison is byte-exact rather than text-equivalent.

## Capturing the goldens

Goldens are produced **once**, from the **Windows/net48 baseline** build:

1. Run the baseline `SourceCode/GPS/IO/` serializers for a representative field.
2. Write each output to `Golden/Field/<name>.txt`, using the **golden names** from the table above — remembering the two ‡ renames (`TrackLines.txt` → `Tracks.txt`, `Field.txt` → `FieldPlane.txt`).
3. Commit the captured goldens.
4. Re-run the parity suite on all three operating systems to confirm byte equivalence.

**Do not hand-write or fabricate golden content.** Fabricated bytes would produce *false* parity results. When an artifact is added — or its producer format changes — **both** the golden and any fixture expectations must be updated **in lockstep**.

## Byte-stability & staging (owned elsewhere)

- **End-of-line / encoding normalization is disabled** for these artifacts by the **root** `.gitattributes` rule `SourceCode/AgOpenGPS.Tests/Parity/** -text`, which keeps byte comparisons stable across Windows/Linux/macOS checkouts (the repository root otherwise sets `* text=auto`). This is **owned by the root agent; it is not configured here.**
- **Artifacts are copied next to the test assembly** by the parent `AgOpenGPS.Tests.csproj` (`Parity\Golden\**\*` → `CopyToOutputDirectory=PreserveNewest`), so they resolve through `TestContext.CurrentContext.TestDirectory` on all OSes including `osx-arm64`. This is **owned by the parent agent; it is not configured here.**
- The `.gitkeep` sibling in this folder keeps `Field/` tracked by git before any golden exists.

## Provenance

- **Producer subsystem:** `SourceCode/GPS/IO/` — `BoundaryFiles.cs`, `TrackFiles.cs`, `SectionFiles.cs`, `FlagFiles.cs`, `ContourFiles.cs`, `HeadlandFiles.cs`, `HeadlinesFiles.cs`, `TramFiles.cs`, `RecPathFiles.cs`, `Elevation.cs` (`ElevationFiles`), and `FieldPlaneFiles.cs`, plus the shared `FileIOUtils.cs` helper.
- **Staging pattern** modeled on `SourceCode/AgLibrary.Tests/Settings/`, where `TestSettings.xml` is staged via `CopyToOutputDirectory` and round-trip-compared by `XmlSettingsHandlerTests.cs`.

## Cross-platform notes

These are the AAP §0.6.5 cross-cutting parity hazards this store defends against:

- **Exact-case file names** — Linux and macOS are case-sensitive, so the golden names above must match the fixture letter-for-letter.
- **Preserved line endings** — guaranteed by the root `.gitattributes` `-text` rule, so the committed bytes equal the on-disk bytes on every OS.
- **`InvariantCulture` everywhere** — all numeric I/O uses a period decimal separator, so a comma-decimal locale cannot corrupt a golden.
- **`Path.Combine` everywhere** — no hard-coded separators, so the same code resolves on `\` and `/` filesystems.

See also the parent store index [`../README.md`](../README.md), the open-risk tracker `MIGRATION_DOCS/PARITY_REPORT.md`, and the field-file format owner `SourceCode/GPS/IO/`.
