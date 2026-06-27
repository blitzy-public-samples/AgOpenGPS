<!-- [XPLAT] new for the net48/WinForms → net8.0/Avalonia migration — see MIGRATION_DOCS/TRANSITION_MAP.md -->
# Settings Parity Goldens

This folder holds the frozen, **byte-exact** settings XML goldens captured from the **Windows/net48 baseline**, consumed by [`SettingsRoundTripTests.cs`](../../SettingsRoundTripTests.cs) in the parent `Parity/` folder. The cross-platform `net8.0`/Avalonia build must reproduce each golden **byte-for-byte** on the `windows` / `ubuntu` / `macos` CI matrix (including `osx-arm64`). The migration de-Windows-ifies the settings *backing* — [`docs/settings.md`](../../../../../docs/settings.md) records that the application no longer depends on the Windows Registry for its configuration root, replacing the Registry / `%AppData%` source with cross-platform paths resolved by `AgOpenGPS.Core.Platform.IPlatformServices` — **while the settings XML schema and the `CSettingsMigration` legacy→split round-trip are frozen EXACTLY**. These goldens are the proof that the freeze holds.

This README is **documentation only**: it *describes* the expected goldens so engineers can later capture the real `*.xml` files and so anyone reading the parity suite understands the contract. It contains **no code**, declares **no dependencies**, fabricates **no golden content**, and neither adds nor re-declares any `.gitattributes` or `.csproj` (those are owned elsewhere — see [Byte-stability & staging](#byte-stability--staging-owned-elsewhere)).

## Expected artifacts

The following goldens are the proof artifacts for `SettingsRoundTripTests.cs`. File names are **exact and case-sensitive** — Linux/macOS filesystems are case-sensitive (AAP §0.6.5), so any casing mismatch silently breaks `Path.Combine` resolution in the fixture. The category folder name is exactly `Settings` (capital `S`).

| File | Serialized type element | Represents |
|---|---|---|
| `Vehicle.xml` | `<AgOpenGPS.Properties.VehicleSettings>` | Split vehicle profile (dimensions, steer/IMU/GPS/Arduino settings) |
| `Tool.xml` | `<AgOpenGPS.Properties.ToolSettings>` | Split tool/implement profile (hitch/section geometry, widths, relays) |
| `Environment.xml` | `<AgOpenGPS.Properties.Settings>` | Split environment/general settings (UI, display, feature flags, UDP timing) |
| `Legacy.xml` | `<AgOpenGPS.Properties.SettingsLegacy>` | Legacy **single-file** representation, the input for the `CSettingsMigration` legacy→split round-trip |

The fixture resolves each golden next to the test assembly with a **case-sensitive** `Settings` segment:

```
Path.Combine(TestContext.CurrentContext.TestDirectory, "Parity", "Golden", "Settings", "<file>.xml")
```

**Producer subsystem.** These XML files are produced by the settings serializers under `SourceCode/GPS/Properties/` — `VehicleSettings.cs`, `ToolSettings.cs`, `Settings.cs`, `SettingsLegacy.cs`, and `RegistrySettings.cs` (which now sources its directory roots from `IPlatformServices` rather than the Registry) — together with the migration driver `AgOpenGPS.CSettingsMigration` in `SourceCode/GPS/Classes/CSettingsMigration.cs`, which performs the legacy→split conversion (`MigrateVehicle` / `MigrateTool` / `MigrateEnvironment`) and, on Windows, a **one-time** Registry read (`MigrateLegacyRegistrySettings()`).

## Frozen schema shape

The settings XML schema is **frozen** by the migration and mirrors the proven shape exercised by [`SourceCode/AgLibrary.Tests/Settings/TestSettings.xml`](../../../../AgLibrary.Tests/Settings/). The structure is:

```
configuration / userSettings / <Namespace.Type> / setting[@name][@serializeAs] / value
```

The snippet below is **illustrative only — _not_ a golden to commit** (real goldens are captured from the baseline, never hand-written):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <userSettings>
    <AgOpenGPS.Properties.VehicleSettings>
      <setting name="setVehicle_maxSteerAngle" serializeAs="String"><value>30</value></setting>
      <setting name="setVehicle_maxAngularVelocity" serializeAs="String"><value>0.64</value></setting>
      <setting name="setVehicle_wheelbase" serializeAs="String"><value>3.3</value></setting>
      <!-- … scalars use serializeAs="String"; collections/objects use serializeAs="Xml" … -->
    </AgOpenGPS.Properties.VehicleSettings>
  </userSettings>
</configuration>
```

The serialization rules are precise:

- **Scalars** — string, enum, bool, int, double, color, point, size — use `serializeAs="String"`, with the value text inside a single `<value>` element.
- **Collections / objects** use `serializeAs="Xml"`, with a nested element under `<value>` (for example an `ArrayOf…` element for a list, or the object's own type element for a complex member).

**Decimal / culture rule (critical).** Doubles serialize with a **period** (`.`) decimal separator under `InvariantCulture` — for example `setVehicle_maxAngularVelocity` → `0.64` (never `0,64`), `setVehicle_maxSteerAngle` → `30`, and `setVehicle_wheelbase` → `3.3`. A comma-decimal locale (for example `de-DE`) must **not** change any golden's bytes; this is exactly why the fixture forces `InvariantCulture` and why a raw byte comparison is safe across operating systems.

## How the test consumes these

`SettingsRoundTripTests.cs` performs a **byte-exact** round-trip. For each golden it loads the bytes, drives the *real* production serializer — `VehicleSettings` / `ToolSettings` / `Settings` via `AgLibrary.Settings.XmlSettingsHandler`, and the `CSettingsMigration` legacy→split path for `Legacy.xml` — re-saves, and asserts equality on the raw bytes:

```csharp
byte[] goldenBytes  = File.ReadAllBytes(goldenPath);
byte[] savedBytes   = File.ReadAllBytes(regeneratedPath);
Assert.That(savedBytes, Is.EqualTo(goldenBytes)); // element-wise byte comparison
```

It uses `File.ReadAllBytes` (never `File.ReadAllText`) so end-of-line / encoding drift can never mask a regression.

**Guarded-golden strategy.** Until a given `*.xml` exists on disk, the corresponding test calls `Assert.Ignore(...)` and self-reports as **Ignored** (not **Failed**) — so `dotnet test` stays **green and discoverable** on every OS (including `osx-arm64`) while the real goldens are still being captured. The fixture's ignore message takes the form:

```
Golden artifact not yet captured: <path> — tracked as an open risk in MIGRATION_DOCS/PARITY_REPORT.md
```

**Ignored** is therefore the expected state for any not-yet-captured golden; each gap is tracked as an open risk in [`MIGRATION_DOCS/PARITY_REPORT.md`](../../../../../MIGRATION_DOCS/PARITY_REPORT.md), and a test flips from Ignored to an enforced byte assertion automatically the moment its golden is committed.

## Capturing the goldens

The `*.xml` goldens are produced **once**, from the **Windows/net48 baseline** build:

1. Serialize real settings through the baseline producers in `SourceCode/GPS/Properties/` (and drive `CSettingsMigration` to emit `Legacy.xml`, the legacy single-file input for the migration round-trip).
2. Write the **raw bytes** to the matching `Parity/Golden/Settings/<file>.xml` (`Vehicle.xml`, `Tool.xml`, `Environment.xml`, `Legacy.xml`).
3. **Commit** the artifact.
4. **Re-run the suite on all three operating systems** to confirm byte-equivalence from the cross-platform build.

When a producer changes, the golden **and** the fixture expectation must be updated **in lockstep** so the comparison keeps reflecting real baseline behavior.

> **Never fabricate goldens.** Do not invent or hand-write any `Vehicle.xml` / `Tool.xml` / `Environment.xml` / `Legacy.xml` content here or anywhere. A fabricated artifact would assert against itself and produce a **false** parity result. Goldens must always come from the running baseline producer listed above.

## Byte-stability & staging (owned elsewhere)

Two pieces of plumbing make these comparisons stable and discoverable at runtime. **Neither is configured in this folder** — they are documented here only so the contract is understood end-to-end, and must **not** be re-declared or overridden here:

- **EOL/encoding normalization is disabled** for these artifacts by the **ROOT** `.gitattributes` rule `SourceCode/AgOpenGPS.Tests/Parity/** -text`, so the bytes on disk equal the committed bytes on every Windows/Linux/macOS checkout and a CRLF↔LF rewrite can never perturb a byte comparison. **Owned by the root agent; not configured here.**
- **Artifacts are copied next to the test assembly** by the parent `AgOpenGPS.Tests.csproj` via `Parity\Golden\**\*` → `CopyToOutputDirectory=PreserveNewest`, which is what lets the fixture resolve them through `TestContext.CurrentContext.TestDirectory` on all OSes, including `osx-arm64`. **Owned by the parent agent; not configured here.**

## Provenance

This store is modeled on the proven golden-staging pattern in `SourceCode/AgLibrary.Tests/Settings/`, where `TestSettings.xml` is staged next to the test assembly via `CopyToOutputDirectory` and round-trip-compared by [`XmlSettingsHandlerTests.cs`](../../../../AgLibrary.Tests/Settings/XmlSettingsHandlerTests.cs). The settings parity suite generalizes that load → save compare and **upgrades it from `File.ReadAllText` to `File.ReadAllBytes`**, so EOL / encoding drift cannot hide a regression.

## Cross-platform notes

Per the cross-cutting parity risks in AAP §0.6.5, the following invariants keep a golden identical across operating systems:

- **Exact-case file names.** The `Settings/` folder and every `*.xml` use exact casing; Linux/macOS are case-sensitive, so a casing mismatch silently breaks resolution.
- **Preserved line endings.** EOL conversion is disabled via the root `.gitattributes` rule noted above, so a CRLF↔LF rewrite can never perturb a byte comparison.
- **Invariant numeric I/O.** All numeric serialization and parsing uses `InvariantCulture` (period decimal separator), so a comma-decimal locale cannot corrupt a golden's bytes (`0.64`, never `0,64`).
- **`Path.Combine` everywhere.** Paths are always composed with `Path.Combine` / `Path.DirectorySeparatorChar`, never a hard-coded `\` or `/`.

## See also

- [`../../SettingsRoundTripTests.cs`](../../SettingsRoundTripTests.cs) — the consuming parity fixture that resolves and byte-compares these files.
- [`../README.md`](../README.md) — the store-wide golden-artifact index for the `Parity/Golden/` tree.
- [`docs/settings.md`](../../../../../docs/settings.md) — the settings model and the (now one-time) Registry coupling that the migration replaced; the XML schema is frozen.
- [`MIGRATION_DOCS/PARITY_REPORT.md`](../../../../../MIGRATION_DOCS/PARITY_REPORT.md) — open-risk tracking for any not-yet-captured or unverified settings golden.
