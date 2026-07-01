# Schema Versioning Policy

This document governs when and how the `schema_version` field of the `PgnEnvelope`
message is incremented for the AgIO ↔ AgOpenGPS inter-process (IPC) contract. The
canonical proto3 schema is defined in [`agopengps_ipc.proto`](agopengps_ipc.proto); this
policy is its human-facing governance companion.

The legacy IPC transport used a custom binary frame —
`[0x80, 0x81, 0x7F, PGN, Length, Data..., CRC]` — that carried **no version field of
any kind**; the receiver dispatched directly off the raw PGN byte (`data[3]`). The migration
to a typed, versioned proto3 contract remedies that gap: every `PgnEnvelope` now carries an
explicit `schema_version`, giving both sides a single, unambiguous handle for evolving the
contract safely.

## Initial Value

The initial value of `schema_version` is **`1`**.

This value mirrors `IpcConstants.SchemaVersion = 1`, defined in
[`SourceCode/AgOpenGPS.Ipc/IpcConstants.cs`](../../SourceCode/AgOpenGPS.Ipc/IpcConstants.cs).
Producers stamp this constant onto **every** `PgnEnvelope` they emit. The two constants
— the `schema_version` governed here and `IpcConstants.SchemaVersion` in code —
**MUST be kept in lock-step**: whenever one changes, the other changes in the same commit.

## When to Increment `schema_version`

Increment `schema_version` **only** on a *breaking* change to the wire contract. Additive,
backward-compatible changes — which proto3 tolerates natively — do **not** require a bump.

| Change | Breaking? | Bump `schema_version`? |
|--------|-----------|------------------------|
| Add a new field with a fresh, unused tag | No | No |
| Add a new `oneof payload` arm with a new tag | No | No |
| Add a new RPC to a service | No | No |
| Add a new standalone message type | No | No |
| Remove a field from an existing message | Yes | **Yes** |
| Rename a field | Yes | **Yes** |
| Change a field's wire type | Yes | **Yes** |
| Renumber an existing field or `oneof` arm | Yes | **Yes** |
| Remove, renumber, or repurpose a `oneof` arm | Yes | **Yes** |

### Breaking changes (increment)

A change is breaking — and therefore requires an increment — if it is any of the
following:

- **Removing a field** from an existing message.
- **Renaming a field.** proto3 wire compatibility keys on the field *number*, not its name,
  so the encoded bytes are unaffected — but the generated C#/JSON member name changes,
  which breaks any consumer that references it. Treat every rename as breaking.
- **Changing a field's wire type** (for example `uint32` → `string`, or
  `sint32` → `int32`, where zig-zag encoding differs from a plain varint).
- **Renumbering an existing field or `oneof` arm** (changing its tag).
- **Removing or renumbering a `oneof` arm**, or repurposing an existing tag for a different
  message.

### Additive changes (do NOT increment)

The following are backward-compatible and require **no** version bump:

- Adding a new optional scalar or message field with a fresh, previously-unused tag.
- Adding a new `oneof payload` arm with a new tag (for example, a 24th PGN message added at
  tag `25`).
- Adding a new RPC to `TelemetryService` or `CommandService`.
- Adding a new standalone message type.

**Rationale:** proto3 readers silently ignore fields they do not recognize and substitute the
type's default value for any field that is absent. Additive changes are therefore
wire-compatible in both directions — old and new peers interoperate — without a
`schema_version` bump.

## Producer / Consumer Contract

- **Producers** build every envelope by stamping the current constant:

  ```csharp
  var envelope = new PgnEnvelope
  {
      SchemaVersion = IpcConstants.SchemaVersion,
      GpsPosition   = msg, // any oneof payload arm
  };
  ```

- **Consumers** dispatch on the strongly-typed payload discriminator:

  ```csharp
  switch (envelope.PayloadCase)
  {
      case PgnEnvelope.PayloadOneofCase.GpsPosition:
          // handle GpsPosition
          break;
      // ... other payload cases ...
  }
  ```

  A consumer **MAY** additionally branch on `envelope.SchemaVersion` in a future revision to
  support multiple schema generations side by side. To stay forward-compatible, a consumer
  **MUST** tolerate payload cases it does not recognize — treat an unknown `PayloadCase`
  as a no-op (ignore it) rather than faulting. This mirrors proto3's own tolerance of unknown
  fields and lets a newer producer talk to an older consumer without breakage.

## Change Procedure

When you need to evolve the schema:

1. **Prefer an additive change.** Add new fields, `oneof` arms, RPCs, or messages using
   fresh, previously-unused tags. This needs **no** `schema_version` bump.
2. **If a breaking change is unavoidable,** increment the `PgnEnvelope.schema_version` default
   expectation **and** bump `IpcConstants.SchemaVersion` in
   `SourceCode/AgOpenGPS.Ipc/IpcConstants.cs` **in the same commit**, so the code and this
   policy never drift apart.
3. **Never reuse a retired field number or `oneof` tag.** Once a tag has been used it is
   permanently spent; assign the next free number instead, and declare the old one with proto
   `reserved` to enforce this.
4. **Regenerate and rebuild.** Regenerate the `AgOpenGPS.Ipc` types from the updated `.proto`,
   then rebuild all five consuming programs (AgIO, AgOpenGPS, AgDiag, GPS_Out, ModSim) and the
   test suite to confirm everything still compiles and passes.

## Related Files

- [`agopengps_ipc.proto`](agopengps_ipc.proto) — the canonical proto3 schema
  (`PgnEnvelope`, the 23 PGN messages, `TelemetryService`, and `CommandService`).
- [`SourceCode/AgOpenGPS.Ipc/IpcConstants.cs`](../../SourceCode/AgOpenGPS.Ipc/IpcConstants.cs)
  — defines `IpcConstants.SchemaVersion`, the code-side mirror of the value governed here.
