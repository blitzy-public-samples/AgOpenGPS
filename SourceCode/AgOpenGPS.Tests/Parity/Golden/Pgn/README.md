<!-- [XPLAT] new for the net48/WinForms → net8.0/Avalonia migration — see MIGRATION_DOCS/TRANSITION_MAP.md -->
# PGN Frame Goldens (Parity)

This folder stores the frozen, **byte-exact** encoded PGN wire frames captured from the **Windows/net48 baseline**, consumed by `PgnFrameGoldenTests.cs` in the parent `Parity/` folder. The cross-platform `net8.0` build must reproduce each frame **byte-for-byte including the additive CRC** on the `windows`/`ubuntu`/`macos` CI matrix (including `osx-arm64`). PGN is the single most safety-critical contract in the system (autosteer + section-control commands), and the wire format is **frozen** per [`docs/pgn-protocol.md`](../../../../../docs/pgn-protocol.md).

This README is **documentation only**: it *describes* the expected frames so engineers can later capture the real `*.bin` goldens and so anyone reading the parity suite understands the contract. It contains **no code**, declares **no dependencies**, and neither adds nor re-declares any `.gitattributes` or `.csproj` (those are owned elsewhere — see [Byte-stability & staging](#byte-stability--staging-owned-elsewhere)).

## Expected artifacts

The following golden frames are the **required** proof artifacts for `PgnFrameGoldenTests.cs`. File names are **exact and case-sensitive** — Linux/macOS are case-sensitive, so a casing mismatch silently breaks resolution. The category folder name is exactly `Pgn` (capital `P`, lowercase `gn`).

| File | Bytes | PGN id | Description |
|------|-------|--------|-------------|
| `D0_latlon.bin` | 14 | `0xD0` (208) | Latitude/Longitude — 8 data bytes: encoded lat `int32` at indices `[5..8]`, encoded lon `int32` at `[9..12]` |
| `FE_autosteer.bin` | 14 | `0xFE` (254) | AutoSteer Data — speed Lo/Hi, status, steer-angle Lo/Hi, line distance, section bitmasks 1–8 / 9–16 |
| `EF_machine.bin` | 14 | `0xEF` (239) | Machine Data — uturn, speed, hyd-lift, tram, geo-stop, section bitmasks 1–8 / 9–16 |
| `E5_sections.bin` | 16 | `0xE5` (229) | Section Control (Extended) — 8 bitmask bytes at `[5..12]` (up to **64** sections), tool-left speed `[13]`, tool-right speed `[14]` |
| `D6_gps.bin` | 52 | `0xD6` (214) | GPS Position — lon/lat `double`, heading/speed/roll/altitude `float`, satellites/fix-quality/HDOP/age/IMU fields |

### Optional artifacts

These are produced **only if** the capture step emits them; the parity suite treats them as best-effort extras, not gating requirements.

| File (optional) | Bytes | PGN id | Description |
|------|-------|--------|-------------|
| `EB_dims.bin` | 38 | `0xEB` (235) | Section Dimensions — 16 width Lo/Hi pairs + section-count byte |
| `EC_relay.bin` | 29 | `0xEC` (236) | Relay Config — pin configuration for 24 relays |

> **Authoritative source of truth.** The **captured baseline artifact is the authoritative source of truth for exact byte content and length.** The sizes above follow the frozen contract in [`docs/pgn-protocol.md`](../../../../../docs/pgn-protocol.md). If a captured frame's length differs from the documented value for an **optional** frame, the **captured golden wins** — do not hand-edit it to match this table.

## Frame layout

Every PGN frame shares the same universal envelope:

```
[ 0x80 0x81 | 0x7F | <PGN> | <Len> | <Data …> | <CRC> ]
   header     src    id      len      payload    additive checksum
  byte 0,1   byte 2  byte 3  byte 4  byte 5..N-1   byte N (last)
```

- **Bytes 0–1** — the fixed header `0x80 0x81`.
- **Byte 2** — the source address `0x7F`.
- **Byte 3** — the PGN id (e.g. `0xFE`, `0xEF`, `0xE5`, `0xD0`, `0xD6`).
- **Byte 4** — the **data length**: the count of payload bytes **only** (it does not include the header, source, id, length, or CRC bytes).
- **Bytes 5..N-1** — the data payload.
- **Byte N (last)** — the additive CRC over the frame (see below).

## Additive CRC

The CRC is the **heart of the byte-equivalence guarantee** and must be reproduced exactly. It is a simple additive checksum:

```csharp
byte crc = 0;
for (int i = 2; i < frame.Length - 1; i++)   // sum from source byte (index 2) through the last data byte (index Length-2)
    crc += frame[i];
frame[frame.Length - 1] = crc;               // store in the final byte; byte arithmetic wraps mod 256
```

Explicitly:

- The CRC is summed over indices **`[2 .. len-2]` inclusive** — from the source byte (index 2) through the last data byte (index `Length-2`).
- The sum **wraps modulo 256** (it accumulates in a `byte`, so it naturally wraps at 256).
- It is stored in the **final byte** of the frame (`frame[frame.Length - 1]`).
- The header bytes `0x80 0x81` (indices 0–1) and the **CRC slot itself** are **excluded** from the sum.

This matches [`docs/pgn-protocol.md`](../../../../../docs/pgn-protocol.md), the `MakeCRC()` helpers in `SourceCode/GPS/Forms/PGN.Designer.cs`, and `PgnDispatcher.SendPgnToLoop(...)`.

## Loopback / network constants (frozen)

The parity suite asserts these as **values** — it does **not** open sockets:

- AOG binds/listens on loopback port **15555**.
- The AgIO endpoint is **`127.255.255.255:17777`**.
- The loopback subnet is **`127.x.x.x`**; the protocol is **UDP**.
- The receive throttle `udpWatchLimit = 70` ms (per-fix scan loop) is preserved across OS schedulers.

## How `PgnFrameGoldenTests.cs` uses these files

The fixture resolves each golden cross-OS with a case-sensitive `Pgn` segment:

```csharp
Path.Combine(TestContext.CurrentContext.TestDirectory, "Parity", "Golden", "Pgn", "<file>.bin")
```

Comparison is **byte-exact**: the fixture reads the bytes with `File.ReadAllBytes(...)` and asserts

```csharp
Assert.That(actualFrame, Is.EqualTo(goldenBytes)); // element-wise byte comparison
```

It **never** uses `File.ReadAllText` (which would risk end-of-line / encoding drift on the binary frames).

**Guarded-golden strategy.** Until a given `*.bin` exists on disk, the corresponding test calls `Assert.Ignore(...)` and self-reports **Ignored** — so `dotnet test` stays **green and discoverable** on all OSes while goldens are still being captured. The fixture's ignore message takes the form:

```
Golden artifact not yet captured: <path> — tracked as an open risk in MIGRATION_DOCS/PARITY_REPORT.md
```

**Ignored** is therefore the expected state for any not-yet-captured artifact; it becomes a hard byte-comparison the moment the `*.bin` is committed.

## Capturing the goldens

The `*.bin` frames are produced **once** from the **Windows/net48 baseline** build:

1. Drive the baseline PGN encoders / `CPGN_*` frame objects to build each frame (header + payload + the computed additive CRC).
2. Write the **raw bytes** to the matching `Pgn/<NAME>.bin` (e.g. `Pgn/FE_autosteer.bin`).
3. Commit the artifact.
4. Re-run the suite on **all three operating systems** to confirm byte equivalence.

When a frame's producer changes, **both** the golden **and** the fixture expectation must be updated **in lockstep**, and any unverified frame is listed as an open risk in [`MIGRATION_DOCS/PARITY_REPORT.md`](../../../../../MIGRATION_DOCS/PARITY_REPORT.md).

> **Never fabricate bytes.** This README only *describes* the expected frames; the real frames are captured from the running baseline. Inventing bytes would produce **false** parity results.

## Byte-stability & staging (owned elsewhere)

Two pieces of configuration make these binary goldens stable and discoverable at runtime. **Neither is configured in this folder** — they are described here only so the contract is understood end-to-end:

- **EOL/encoding normalization is disabled** for these artifacts by the **ROOT** `.gitattributes` rule `SourceCode/AgOpenGPS.Tests/Parity/** -text` (owned by the root agent — **not** configured here). This keeps byte comparisons stable across Windows/Linux/macOS checkouts.
- **Artifacts are copied next to the test assembly** by the parent `AgOpenGPS.Tests.csproj` via `Parity\Golden\**\*` → `CopyToOutputDirectory=PreserveNewest` (owned by the parent agent — **not** configured here). This is why `TestContext.CurrentContext.TestDirectory` resolves them at runtime on every OS, including `osx-arm64`.

## Provenance

The producer subsystems that emit these frames are:

- `SourceCode/GPS/Forms/PGN.Designer.cs` — the additive-CRC `MakeCRC()` helper plus the `CPGN_*` frame templates / byte offsets.
- `SourceCode/GPS/Services/PgnDispatcher.cs` — the UDP transport plus the relocated `CPGN_*` frame objects.
- `SourceCode/GPS/Classes/CISOBUS.cs` — the ISOBUS frame builders (e.g. PGN `0xF1`/`0xF2`/`0xF3`; field-name ≤ 248 bytes).

The staging pattern is modeled on `SourceCode/AgLibrary.Tests/Settings/` — `TestSettings.xml` is staged via `CopyToOutputDirectory` and round-trip-compared by `XmlSettingsHandlerTests.cs`.

## Cross-platform notes

Per AAP §0.6.5, the surrounding parity code honors these cross-platform rules:

- **Exact-case file names** — the `Pgn/` folder and every `*.bin` use exact casing (Linux/macOS are case-sensitive).
- **Preserved byte content / line endings** — pinned by the root `.gitattributes` rule noted above.
- **`InvariantCulture`** — all numeric I/O in the producers uses `InvariantCulture` so a comma-decimal locale never corrupts derived text.
- **`Path.Combine`** — paths are always built with `Path.Combine`, never hard-coded separators.

The `*.bin` frames are pure binary, so culture and EOL conventions do not affect their bytes directly; these rules govern the surrounding test and producer code that builds and resolves them.

## See also

- [`docs/pgn-protocol.md`](../../../../../docs/pgn-protocol.md) — the frozen PGN wire contract (frame structure, additive CRC, loopback ports).
- [`../README.md`](../README.md) — the store-wide golden-artifact index for the `Parity/Golden/` tree.
- [`../../PgnFrameGoldenTests.cs`](../../PgnFrameGoldenTests.cs) — the consuming parity fixture that resolves and byte-compares these files.
- [`MIGRATION_DOCS/PARITY_REPORT.md`](../../../../../MIGRATION_DOCS/PARITY_REPORT.md) — open-risk tracking for any not-yet-captured or unverified frame.
