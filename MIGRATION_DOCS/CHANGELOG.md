# Changelog

<!-- [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md -->

This file is the **narrative spine** of the AgOpenGPS migration from **.NET Framework 4.8 / Windows
Forms** (Windows-only) to **.NET 8.0 / Avalonia** (cross-platform: `win-x64`, `linux-x64`, `osx-x64`,
`osx-arm64`), preserving 100% functional parity across all twelve projects in
`SourceCode/AgOpenGPS.sln`. It is a **reverse-chronological**, **evidence-backed** log of every
meaningful change, **grouped by migration area**.

It is one of five **MIGRATION_DOCS** deliverables, all **on disk**: its companion
**`TRANSITION_MAP.md`** (the file-by-file old→new mapping with each artifact's disposition and parity
status); **`PARITY_REPORT.md`** (the proof that behavior did not change — PGN byte-equivalence,
field-file round-trip, ISOXML V3/V4 equivalence, settings XML round-trip, guidance/steering output
equivalence — plus the open risks); **`FEATURE_TRACEABILITY.md`** (the per-feature F-001…F-045
cross-platform disposition checklist); and **`VALUE_SUMMARY.md`** (the non-technical brief).

The format loosely follows the [Keep a Changelog](https://keepachangelog.com/) conventions, **adapted
to migration areas**: because the entire refactor is executed in a single phase within the one
solution, there is a single `[Unreleased]` entry whose subsections are the migration **areas** (rather
than semantic-version releases), each using `Added` / `Changed` / `Removed` groups.

> **Provenance.** All migrated code carries an inline
> `// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md` comment (XML/markup
> files use the `<!-- [XPLAT] … -->` form). This file and `TRANSITION_MAP.md` are the authoritative
> record; commit messages alone are not sufficient provenance (AAP §0.7.2).

> **Status & buildability note (final state).** The migrated suite has **converged**. The full
> `AgOpenGPS.sln` — all twelve projects, including the `GPS` application — builds clean in **Debug and
> Release** (`TreatWarningsAsErrors` + `MSBuildTreatWarningsAsErrors`) for both `net8.0` and
> `net8.0-windows`, with **0 errors and 0 warnings** — including **0 `AVLN3001`** Avalonia-XAML
> runtime-loader notices. The eight `GPS` views that previously lacked a public parameterless
> constructor now carry the standard Avalonia dual-constructor pattern, so the runtime/XAML loader can
> reach every `avares://` view; and the Release gate now promotes Avalonia/MSBuild-task warnings (not
> just Roslyn `CS####`) to errors, so the zero-warning acceptance bar is actually enforced in Release
> and on the tri-OS CI matrix (see the **Build/CI** area, **F1-001**).
> The local Linux test run is **133 passed / 1 skipped / 0 failed** across all three assemblies
> (`AgOpenGPS.Core.Tests` 33, `AgLibrary.Tests` 3, `AgOpenGPS.Tests` 97 — the latter raised from 79 by the
> QA F5 production-invoking parity additions, plus a further +4 by the QA final_alt guidance composition-root tests), and **all five golden-file
> parity suites — PGN, Guidance, ISOXML, Settings, and Field — are authored, committed, and enforcing**
> (each loader fails, not skips, when a required golden is absent). There is **no remaining
> `<Compile Remove>` / `<AvaloniaXaml Remove>` gating** in any project; the `FormGPS`→services decoupling
> is complete (the only residual `FormGPS` tokens are `// [XPLAT]` provenance comments). The honest
> residuals carried in `PARITY_REPORT.md` are: **(1) tri-OS CI execution** on GitHub-hosted runners (the
> matrices are on disk; execution is the external-evidence step); **(2) real-GPU desktop-GL
> confirmation (Open Risk #1)** — the `RequestDesktopGlProfile` hook is wired, the GLES/ANGLE runtime
> fail-safe enforced, and the production GL path is now **re-verified PASS end-to-end under software GL**
> (`OnOpenGlInit` fires, OpenTK binds, desktop-GL `4.5 (Compatibility Profile)`, `GL.ReadPixels` = `NoError`),
> leaving only per-OS **real-GPU** confirmation as the external-evidence residual (criteria enumerated in
> Open Risk #1); and **(3)** the single skipped `FormGPS`-graph-dependent ISOXML **export** driver
> (the ISOXML import/round-trip contract is enforced). _Open Risk #8 (guidance peer-wiring) was closed by
> this pass via `GuidanceComposition.WireGuidanceReferences` + `GuidanceCompositionTests`._

---

## [Unreleased] — net48/WinForms → net8.0/Avalonia cross-platform migration

_Single-phase migration within the one solution; converged at the final code-review remediation pass._

> **[XPLAT] QA Checkpoint final_alt remediation — final end-to-end delivery gate (9 findings: 0
> Critical, 1 Major, 8 Minor; all resolved + verified).** Static gate after this pass: the full
> `AgOpenGPS.sln` (all twelve projects, `net8.0` + `net8.0-windows`) builds **0 errors / 0 warnings**
> under `TreatWarningsAsErrors` + `MSBuildTreatWarningsAsErrors` (including **0 `AVLN3001`**); the three
> test assemblies report **133 passed / 1 skipped / 0 failed** (`AgOpenGPS.Core.Tests` 33,
> `AgLibrary.Tests` 3, `AgOpenGPS.Tests` 97 — the +4 over the prior 129/93 is the new guidance
> composition-root suite), culture-invariant on a `de-DE` re-run. Changes by area:
> >
> > - **Guidance composition (C2 / Open Risk #8) — Minor:** added `GPS/Services/GuidanceComposition.cs`
> >   (`WireGuidanceReferences`) as the single source of truth for the seven late guidance peer-wirings
> >   (`CABLine`/`CABCurve`/`CContour`/`CTrack`/`CYouTurn`/`CRecordedPath`/`CGuidance`); `App.axaml.cs`
> >   now constructs the live `CGuidance` and calls it, and `AgOpenGPS.Tests/Parity/ParityGraphFixture`
> >   calls the same helper. New `GuidanceCompositionTests` assert every late-wired peer is non-null.
> >   _Open Risk #8 → RESOLVED._
> > - **Steer wizard (F-020) — Minor:** `FormSteerView`'s Wizard button now routes through an injected
> >   real-adapter launcher (`App.axaml.cs` `OpenSteerConfig` passes `openSteerWizard`); the inert
> >   Null-adapter `FormSteerWizView()` path is confined to the designer/previewer ctor. Verified at
> >   runtime on Avalonia.Headless — the production button invokes the real launcher.
> > - **Settings (G5 / G7) — Minor:** `AgLibrary/Settings/XmlSettingsHandler` parses floating-point
> >   settings with strict `TryParse(NumberStyles.Float, InvariantCulture)` and fails the load on an
> >   invalid numeric, so a corrupt comma-decimal `0,64` no longer silently coerces to `64` under a
> >   comma-decimal locale (verified under `de-DE`).
> > - **Platform services (G4) — Minor:** AgIO macOS single-instance now P/Invokes `flock(2)` (parity
> >   with GPS) instead of relying on `FileShare.None` alone.
> > - **Accessibility (G2) — Minor:** 19 image-only `Button`/`ToggleButton` controls across
> >   `FormInputDialogView` / `FormDialogView` / `FormABDrawView` gained `AutomationProperties.Name` +
> >   `ToolTip.Tip` (verified live via Avalonia.Headless).
> > - **Build / CI (G6) — Minor:** `build.yml` + `release.yml` build/test steps now pin
> >   `-c ${{ env.BUILD_CONFIGURATION }}` (Release) so the CI gate matches the analyzer/warning gate.
> > - **Docs (G8) — Minor:** test-count and `AVLN3001` figures reconciled to the final 0-warning state;
> >   the loopback *address* docs (`docs/pgn-protocol.md`, `docs/architecture.md`, this file) now
> >   distinguish the legacy directed-broadcast `127.255.255.255` baseline from the migrated explicit
> >   unicast `127.0.0.1` (frozen ports **15555 / 17777** unchanged).
> > - **Rendering (G3 / Open Risk #1) — Major:** the production `AvaloniaGeoViewport` GL path was
> >   **re-verified PASS end-to-end under software GL** (`xvfb` + Mesa `llvmpipe`). A single harness binary
> >   captured both states: with Avalonia's default `{"llvmpipe"}` GLX blacklist active, `OnOpenGlInit` does
> >   not fire (`IsContextAudited=False`; framework logs `Renderer 'llvmpipe …' is blacklisted by 'llvmpipe'`)
> >   — reproducing the QA observation and proving the limitation is **Avalonia's policy, not an AgOpenGPS
> >   defect**; with the blacklist emptied in the **test host only**, the **real** `OnOpenGlInit` fires, OpenTK
> >   3.3.3 binds (`IsContextAudited=True`), the context audits as desktop GL **`4.5 (Compatibility Profile)`,
> >   `IsRenderingGated=False`** (non-GLES — immediate-mode `GLW` supported), and `GL.ReadPixels` returns
> >   `NoError`. An independent `ctypes` GLX off-screen-pbuffer probe corroborates a desktop-GL `4.5` context
> >   is obtainable from the same Mesa stack. **Real-GPU per-OS confirmation remains the only residual**, now
> >   with precise enumerated acceptance criteria recorded in `PARITY_REPORT.md` Open Risk #1.

> **[XPLAT] QA Checkpoint F10 remediation — End-to-end MVVM wiring, OpenGL re-host & cross-layer flow (2
> findings: 0 Critical, 1 Major, 0 Minor, 1 Info; all resolved + runtime-verified on Linux).** F10 confirmed
> every cross-layer seam PASS (UI→VM→Service→PGN→UDP→AgIO→fuse→UI), G2 MVVM activation FULL, G4 platform
> services FULL, and all behavior-frozen contracts FULL (129 passed / 0 failed / 1 skipped regression+parity).
> The single Major was an **OpenGL re-host (G3) binding defect**; the Info was a stale test-header comment.
> Changes by area:
> >
> > - **Rendering — Major (G3): OpenTK↔Avalonia GL binding threw on every platform, blanking the field
> >   viewport.** In `SourceCode/GPS/Controls/AvaloniaGeoViewport.cs`, `EnsureGlBindings`'s
> >   `GetCurrentContextDelegate` returned `ContextHandle.Zero`. OpenTK 3.3.3's
> >   `GraphicsContext(ContextHandle, GetAddressDelegate, GetCurrentContextDelegate)` constructor, given a Zero
> >   handle, **adopts the delegate's return as the context handle** and throws `GraphicsContextMissingException`
> >   when it is also Zero — so the bind aborted, `HandleOpenGlInit` fail-safed (`_glInitialized = false`)
> >   before any render, and the central guidance viewport rendered nothing on Windows/Linux/macOS alike
> >   (delegate-return-driven ⇒ environment-independent; no crash — graceful degradation held). **Fix:** the
> >   delegate now returns a stable non-zero process-lifetime currency token
> >   (`AvaloniaCurrentContextToken = new ContextHandle(new IntPtr(1))`), semantically correct because Avalonia
> >   guarantees a context is current for the whole `OnOpenGlInit`/`OnOpenGlRender`/`OnOpenGlDeinit` window (the
> >   only time the bind runs). A sentinel was chosen over a per-OS
> >   `glXGetCurrentContext`/`wglGetCurrentContext`/`CGLGetCurrentContext` query precisely because the latter
> >   returns null under an EGL/ANGLE context and would re-throw; the GLES-vs-desktop decision stays with the
> >   existing `AuditGlContext` feature-gate. _Verified at two runtime levels:_ (A) a standalone
> >   OpenTK-constructor toggle (`ContextHandle.Zero` → `GraphicsContextMissingException` vs. non-zero token →
> >   constructor does not throw + `LoadAll()` succeeds — environment-independent); and (B, gold standard) the
> >   **real production `AvaloniaGeoViewport`** hosted in an Avalonia window under Xvfb + Mesa completing
> >   `OnOpenGlInit` end-to-end — production log `"…OpenTK 3.3.3 GL entry points bound to Avalonia GL context."`,
> >   context audited as **desktop GL 4.5 Compatibility / not-GLES / not-gated**, `_glInitialized = true`, and an
> >   immediate-mode `GLW`-style triangle rendered through the `_renderAction` seam with `glReadPixels` reading
> >   back the full framebuffer (center `(255,217,0)`, corners `(69,115,51)`)
> >   (`blitzy/screenshots/f10_levelb_real_viewport_readback.png`, with the QA capstone
> >   `blitzy/screenshots/f10_gl_immediate_mode_readback.png`). The Level-B host cleared Avalonia 11.3.18's
> >   default GLX `{"llvmpipe"}` blacklist in the **test host only** so the container's software Mesa GL was
> >   accepted; no product code is affected.
> >   `PARITY_REPORT.md` Open Risk #1 updated to record this defect + resolution (**RESOLVED (c)**) as DISTINCT
> >   from the GLES/ANGLE context-type risk. _At parity._
> > - **Tests/docs — Info: stale `<Compile Remove>` claim in a parity-test header.**
> >   `SourceCode/AgOpenGPS.Tests/Parity/PgnFrameGoldenTests.cs` asserted that `Services/PgnDispatcher.cs` was
> >   compile-gated out of `AgOpenGPS.csproj` and that `AgOpenGPS.Services` was therefore unreferenceable. That
> >   gating was removed earlier in the migration (no `<Compile Remove>` at HEAD; `AgOpenGPS.Tests` references
> >   the GPS project; `PgnDispatcher` compiles into the assembly). Corrected the header to reflect HEAD while
> >   preserving the still-valid rationale that the DI/`mf`-coupled `PgnDispatcher` (8-arg ctor) is not cleanly
> >   constructible in isolation, so the pure-algorithm + golden-byte proof remains the chosen approach (test
> >   behavior unchanged; suite stays green). _Documentation hygiene._

> **[XPLAT] QA Checkpoint F5 remediation — Guidance / Steering / Section-control parity certification (13
> findings: 0 Critical, 6 Major, 4 Minor, 3 Info; all resolved + runtime-verified on Linux).** The F5
> checkpoint confirmed there is **no behavioral regression and no safety-clamp violation** — every guidance
> output, safety guard, and section-control semantic is **byte-identical to the net48 baseline `860eb9fd`**
> by source-diff (logic moved for decoupling; outputs frozen). The FAIL was driven entirely by **the parity
> suite's inability to *certify* that parity**: it reproduced documented formulas in-test rather than
> invoking production code (M1), its goldens were formula-derived (M6), several checkpoint-required algorithms
> had **zero committed coverage** (Dubins M2, six algorithms M5, `isJobStarted` gate M4, CAHRS fusion M3),
> plus minor CI-runtime (m1) and stale-documentation (m2, m3, m4) issues. This pass converts the suite into a
> **production-invoking** regression gate. The **only production-code change** is the behavior-preserving
> CAHRS fusion seam (M3); all other changes are in the **test project, golden fixtures, CI, and docs**. Each
> fix was runtime-verified by invoking the **real** production methods on net8.0/Linux. Changes by group:
> >
> > - **M3 (Major) — CAHRS heading/roll fusion was untestable in isolation.** The IMU+GPS fusion lived as an
> >   inline block inside the `FormGPS`-coupled `PositionService.UpdateFixPosition`. Extracted the **verbatim**
> >   fusion arithmetic into a pure static `CAHRS.FuseImuGpsHeading(imuHeadingRad, gpsHeading, fusionWeight,
> >   isReverseWithIMU, ref imuGPS_Offset)`; `PositionService` now delegates to it (byte-identical behavior).
> >   This is the sole production touch and is behavior-preserving. _Verified:_ production-invoking fusion test
> >   (production-captured golden, `Within(0.001)`) + full-suite regression (zero drift). _At parity._
> > - **M1 + M6 (Major) — suite was formula-self-referential with formula-derived goldens.** Added
> >   `SourceCode/AgOpenGPS.Tests/Parity/ParityGraphFixture.cs`, which builds the **real** guidance/section
> >   graph in the live `App.axaml.cs` composition order (and wires the cyclic peers via
> >   `SetGuidanceReferences`). Rewrote `GuidanceEquivalenceTests.cs` to invoke the **real** (private, via a
> >   controlled reflection seam) `CGuidance.DoSteerAngleCalc` (Stanley), the **real** `CABLine.GetCurrentABLine`
> >   (Pure Pursuit `steerAngleAB`), the **real** clamp (over-range → ±`vehicle.maxSteerAngle`), and the
> >   production `glm.toDegrees` tie. Recaptured `stanley.csv` / `purepursuit.csv` **from the production
> >   methods** (accepted golden source: production ≡ net48 by source-diff). _Verified:_ 8/8 production-invoking
> >   tests PASS. _At parity._
> > - **M1 + M6 + M4 (Major) — section control + `isJobStarted` gate.** Added `SectionControlParityTests.cs`
> >   invoking the **real** `SectionService.BuildMachineByte` (both 1–16 unique-width and ≤64 same-width modes,
> >   asserting the exact PGN `0xFE`/`0xEF`/`0xE5` bytes) and the **real** `DoRemoteSwitches` proving the
> >   `isJobStarted` gate blocks section activation outside an active job (off → none on; on → activation).
> >   Golden `sections_machinebyte.csv` production-captured; legacy `sections.csv` retained as a labelled
> >   secondary mask-edge cross-check. _Verified:_ 3/3 PASS. _At parity._
> > - **M2 + M5 (Major) — Dubins + six uncovered algorithms.** Added `GuidanceAlgorithmCoverageTests.cs`
> >   invoking the **real** `CDubins.GenerateDubins` (straight ≈30.0 m, lateral, U-turn ≈53.25 m, plus a
> >   determinism re-run), `CSmartWAS` (250-sample Mean/Median/StdDev/RecommendedOffset), `CContour`
> >   (`DistanceFromContourLine`), `CABCurve` (`BuildNewOffsetList`), `CRecordedPath`
> >   (`StartDrivingRecordedPath`), and `CYouTurn` (`DistanceFromYouTurnLine`), against production-captured
> >   goldens (`algorithms.csv`). _Verified:_ 6/6 PASS. _At parity._
> > - **m1 (Minor) — net8.0 tests could fail to launch on a 9.0-only CI runtime.** Added
> >   `<RollForward>Major</RollForward>` to the three test projects and pinned explicit `dotnet-version`
> >   (`8.0.x` + `9.0.x`) on `setup-dotnet` in `build.yml` / `release.yml` (defense-in-depth). _Verified:_ the
> >   full suite now launches and passes on this **9.0-only** container with `DOTNET_ROLL_FORWARD` unset.
> > - **m2 + m3 + m4 + i3 (Minor/Info) — stale/contradictory test documentation.** Rewrote the
> >   `GuidanceEquivalenceTests.cs` header (the CP6 `<Compile Remove>` gating was removed at CP9; in-test
> >   formula reproduction replaced by production invocation), reconciled the golden `README.md` with reality
> >   (production-captured goldens), documented that the `0.64°/s` angular-velocity limiter is **inactive in
> >   both** net48 and migrated code (nominal guard, m4), and noted the production Pure-Pursuit `Math.Atan`
> >   goal-point form vs. the documented `atan2` (mathematically equivalent, i3).
> >
> > _Net result:_ production code invoked by the committed parity suite went from **1 of 17 paths
> > (`glm.toDegrees`)** to the full Stanley / Pure-Pursuit / clamp / Dubins / CSmartWAS / Contour / ABCurve /
> > RecordedPath / YouTurn / section-assembly / `isJobStarted` set. Local Linux: **17 production-invoking
> > parity tests green** (8 Guidance + 3 Section + 6 Algorithm-coverage), full `AgOpenGPS.Tests` **93 passed /
> > 1 skipped / 0 failed** (the skip is the out-of-scope F6/F7 ISOXML export driver), culture-invariant on
> > re-run under `de_DE.UTF-8`.

> **[XPLAT] QA Checkpoint F4 remediation — UDP loopback fabric / two-program model / simulators (5
> findings: 2 Critical, 2 Major, 1 Minor; all resolved + runtime-verified on Linux).** This pass closes
> the F4 integration checkpoint, which found the cross-platform two-program spine non-functional on
> Linux even though every comm service reproduced its WinForms behavior at the source level. Each fix
> was verified by driving the **real** production code (via `AssemblyLoadContext` reflection harnesses
> over actual UDP loopback, and a live headless AgIO launch), not just by re-building. Ports
> **15555/17777** and the PGN frame/CRC remain byte-frozen; no new architectural surface was added
> (plain CLR event + `DispatcherTimer` + ordered path probe). Changes by group, each detailed under its
> migration area below:
>
> - **F4-C1 (Critical) — Loopback fabric did not deliver on Linux.** Both programs sent to the 127/8
>   **directed broadcast** `127.255.255.255` while binding receivers to the **specific** address
>   `127.0.0.1`. Windows delivers a subnet-directed broadcast to a specifically-bound socket; Linux and
>   macOS do not — so the frozen WinForms idiom silently failed cross-platform. The loopback peer is now
>   resolved to the **unicast loopback host `IPAddress.Loopback`** on both sides
>   (`AgIO/Source/Services/UdpLoopbackService.cs` `epAgOpen` → `127.0.0.1:15555`;
>   `GPS/Services/PgnDispatcher.cs` `epAgIO` → `127.0.0.1:17777`). The `eth_loop` settings schema and the
>   frozen ports are preserved; the module subnet-broadcast endpoint (port 8888, real LAN hardware) is
>   **untouched**. _Verified:_ real-service harness — unicast/loopback delivery lossless both directions
>   (`epAgOpen=127.0.0.1:15555`, `epAgIO=127.0.0.1:17777`), with the original broadcast→specific-bind
>   still dropped as the control. _At parity (adapted for cross-platform)._
> - **F4-C2 (Critical) — AgIO crashed on startup before binding its socket.** ~22 AgIO Avalonia views
>   carried a hand-written `private void InitializeComponent() => AvaloniaXamlLoader.Load(this);` that
>   **shadowed** the source-generated `InitializeComponent(bool)` overload, so `x:Name` fields (e.g.
>   `lblIP`) were never assigned → `NullReferenceException` on first access (`MainWindow.axaml.cs:196`),
>   before `LoadLoopback()`. The manual method (and its now-orphaned `using Avalonia.Markup.Xaml;`) was
>   removed from every Load-only view so the generator wires the named controls; the four genuinely
>   logic-bearing keyboard/numeric views (which use `FindControl` into locals, referencing no generated
>   fields) were left as-is. _Verified:_ live `xvfb-run dotnet AgIO.dll` — process stays alive, empty
>   error log, `127.0.0.1:17777` bound. _At parity._
> - **F4-C3 (Major) — GPS could not locate AgIO in the shipped layout.** `Program.StartAgIO()` probed
>   only **beside** the GPS executable, but `release.yml` publishes GPS → `publish/${rid}/AgOpenGPS/` and
>   AgIO → `publish/${rid}/AgIO/` as **siblings**, so AgIO was never found and never auto-started on any
>   published OS. `StartAgIO()` now probes an **ordered candidate list** — beside-GPS
>   (`AppContext.BaseDirectory`) first, then the published sibling `../AgIO/` — launching the first that
>   exists, preserving the existing single-instance guard and graceful "Can't Find AgIO" degradation.
>   _Verified:_ reflection harness drove the real `Program.StartAgIO()` (graceful, no spurious spawn) and
>   the real candidate resolver across published-sibling (resolves), co-located (resolves), and missing
>   (−1) layouts. _At parity (packaging adapted)._
> - **F4-C4 (Major) — In-app simulator (CSim) produced no fix stream.** The Avalonia composition root
>   built `CSim` + `CNMEA` but dropped the three implicit links the WinForms shell had: it never called
>   `sim.SetNmea(pn)` (→ `NullReferenceException` at `_pn.vtgSpeed`), never subscribed the scan loop to
>   `CSim.FixGenerated`, and had no periodic `DoSimTick` driver; the real-GPS hook
>   `PgnDispatcher.OnGpsFixReady` was wired **render-only**. `App.axaml.cs` now injects the fix sink
>   (`sim.SetNmea(pn)`), subscribes `sim.FixGenerated += position.UpdateFixPosition`, and drives a 93 ms
>   `DispatcherTimer` whose `Tick` is a behavior-frozen copy of `FormGPS.timerSim_Tick`; a single
>   `Action<bool>` keeps `CSim.IsActive` + the timer + `MainView.IsSimulatorActive` in lock-step from both
>   startup and the operator toggle; and `MainView` `OnGpsFixReady` is now a composite
>   (`UpdateFixPosition()` then `RequestRender()`) so both the simulator and real-GPS paths drive the
>   receive→fuse→steer→section loop. _Verified:_ real-`CSim` harness — unwired `DoSimTick` reproduced the
>   `NullReferenceException`; after wiring, 5 ticks fired `FixGenerated` 5 times and produced a real fix.
>   _At parity._
> - **F4-C5 (Minor) — One malformed UDP frame killed the AgDiag receive loop.** `ReceiveLoopAsync` ran
>   the `while` loop inside a single outer `try/catch`, so any exception from `HandleMessage` exited the
>   loop and disposed the socket; `HandleMessage` indexed `data[0..3]` with no length guard, and
>   `Pgns.SetBytesFromMessage` copied `data.Length - 5` bytes (underflow on short frames, overflow on
>   oversized). Added a **per-iteration `try/catch`** (log + continue — matching the production AgIO
>   hub's per-callback resilience), a `data.Length >= 5` guard, and a clamped copy length
>   (`Math.Min(data.Length, Bytes.Length) - 5`, guarded `> 0`); the happy-path copy is byte-identical.
>   _Verified:_ harness — valid PGN 253 decoded (Heading=10000); 0-byte/short/oversized frames did not
>   raise `ErrorOccurred` nor kill the loop; a subsequent valid PGN 253 was still decoded (Heading=20000).
>   _At parity (graceful-degradation hardening, AAP §0.7.2)._
>
> _Static gate after this pass: AgDiag, AgIO (`net8.0`), GPS (`net8.0` + `net8.0-windows`), ModSim,
> GPS_Out all build **0 W / 0 E** under `TreatWarningsAsErrors`; tests **115 passed / 1 skipped / 0
> failed** (Core 33, AgLibrary 3, AgOpenGPS.Tests 79). Fabric binds remain loopback-only (`127.0.0.1`,
> no stray `0.0.0.0`)._

> **[XPLAT] Final code-review remediation pass — 31 findings resolved.** This pass closes the final
> checkpoint review (10 Critical, 13 Major, 6 Minor, 2 Info) and reconciles all five MIGRATION_DOCS with
> the integrated on-disk state. Changes by group, each detailed under its migration area below:
>
> - **Rendering bootstrap (G3).** `BuildAvaloniaApp()` in both `GPS/Program.cs` and `AgIO/Source/Program.cs`
>   now chains **`AvaloniaGeoViewport.RequestDesktopGlProfile(...)`**, so the renderer requests a
>   desktop-GL compatibility profile ahead of ANGLE/EGL; the GLES/ANGLE runtime fail-safe in the adapter
>   remains as defense-in-depth. On-hardware per-OS confirmation is **Open Risk #1**.
> - **Two-program model (R4 / F-003).** `GPS` now **auto-starts AgIO** on launch (gated on
>   `setDisplay_isAutoStartAgIO` and a non-empty vehicle profile) and **auto-stops** it on shutdown
>   (gated on `setDisplay_isAutoOffAgIO`), via new `Program.StartAgIO()` / `Program.StopAgIO()` using safe
>   `Process.Start` / termination — preserving the original FormGPS behavior over UDP loopback (no
>   project reference between the two programs).
> - **Field lifecycle (G2/G5 / APP-2).** `FieldIoService` is now constructed with real **before/after
>   field-close callbacks** (section-master/contour stop, mapping-off, job close, panel-disable, title
>   parity) instead of `null`.
> - **Platform-service instance (G4 / APP-1).** The `App` composition root **reuses** the single
>   process-level `IPlatformServices` published as `Program.PlatformServices` instead of creating a
>   second `PlatformServicesFactory.Create()` instance.
> - **MainView command wiring + section enablement (G2 / R3 / MV-1..3).** Every operator
>   button/toggle/hotkey in the `MainView` kiosk shell is wired to a real command through a new
>   `ShellCommands` delegate bundle assembled in the GPS composition root; the manual section buttons are
>   bound to live `SectionService` availability (`PerformSectionClick`/`PerformZoneClick` made public)
>   rather than hard-disabled; a `ShellCommandsCoverageTests` suite proves the command map covers the
>   original FormGPS actions.
> - **Dialog navigation + steer wizard (G2 / MVC-1..3).** All migrated Field/Guidance/Settings dialogs
>   are reachable from the shell command/menu graph; the steer-wizard workflow is migrated and wired
>   (real `ISteerWiz*` adapters) — the "not available yet" placeholder is removed.
> - **Security hardening (R11).** NTRIP mountpoint CR/LF/control-char validation; UDP minimum-length
>   guards before header/PGN indexing; NTRIP source-table field-count validation; `ProcessStartInfo.ArgumentList`
>   for `open`/`xdg-open`; hardened `XDocument.Load` (`DtdProcessing = Prohibit`, `XmlResolver = null`).
> - **Field byte parity (R2/R6 / C2).** The 11 field golden artifacts are captured via the production IO
>   serializers under `InvariantCulture`, committed, and **enforced** (the loader fails when a golden is
>   absent — proven by a negative test); `.gitattributes` pins them byte-stable.
> - **Dependency audit (SEC-7).** `dotnet list package --vulnerable` was run against the live NuGet
>   advisory DB across all twelve projects (top-level + transitive): **zero vulnerable packages**.
> - **Documentation (G8 / R14 / DOC-1,2).** All five MIGRATION_DOCS reconciled with the final on-disk
>   state and the honest open residuals.

> **[XPLAT] Build integration — source gating removed; real-time pipeline integrated.** The interim
> Checkpoint-6 source-gating block was **removed** from `SourceCode/GPS/AgOpenGPS.csproj` — there are now
> **zero** `<Compile Remove>` / `<AvaloniaXaml Remove>` / `<AvaloniaResource Remove>` entries. All
> previously-gated files (the 16 guidance/domain classes, `ISO11783_TaskFile`, `SectionsVisual`, the 11
> lockstep-gated Field/Guidance/Pickers views, the five extracted real-time services `PgnDispatcher` /
> `SectionService` / `RenderCoordinator` / `PositionService` / `FieldIoService`, and the
> `AvaloniaGeoViewport` GL-host adapter) compile in the **normal GPS build** on both `net8.0` and
> `net8.0-windows`, in Debug **and** Release, with 0 errors and 0 new warnings. The blocker uncovered
> when un-gating was a namespace collision in four guidance views (`namespace AgOpenGPS.Views` made the
> unqualified `Settings.Default` bind to the sibling `AgOpenGPS.Views.Settings` namespace); it was fixed
> by qualifying all sites to `Properties.Settings.Default`, matching the established convention. The full
> `AgOpenGPS.sln` builds clean and `AgOpenGPS.Tests` compiles and runs (it references GPS).

---

### Runtime/TFM

#### Changed

- **Target framework `net48` → `net8.0`** via the shared `SourceCode/Directory.Build.props`
  `<TargetFramework>` element (line 5, immediately below the `[XPLAT]` provenance comment at line 4;
  `net48` occupied line 4 in the baseline). This single baseline is inherited by 11 of the 12 projects.
  The analyzer settings `EnableNETAnalyzers` and `EnforceCodeStyleInBuild`, and the `Release`-only
  `TreatWarningsAsErrors`, are retained. _At parity._
- **`GPS` and `AgIO` application projects** target the cross-platform stack with per-OS RIDs
  `win-x64;linux-x64;osx-x64;osx-arm64`; Windows-only code paths (WMI brightness, Registry
  migration-read) are isolated under a `net8.0-windows` target so they never reach Linux/macOS builds.
  Both projects build cleanly for both targets in Debug and Release. _At parity (local)._
- **`SourceCode/Updater/AgOpenGPS.Updater.csproj`** — the only project that pinned its own framework —
  had its explicit `net48` flipped to `net8.0` separately. _At parity (local)._
- **`.NET SDK 9.0.300` toolchain retained** (`global.json`, `rollForward: latestFeature`); the
  migration changes target frameworks, not the SDK pin. _At parity._

#### Removed

- **`net48` BCL polyfill packages `System.Memory 4.6.0` and `System.ValueTuple 4.6.1`** — in-box on
  `net8.0`. Removed from `AgOpenGPS.Core`, the `GPS` `csproj`, and the test projects. _At parity (local)._
- **`SourceCode/AgIO/Source/App.config`** `net48` startup section — unused by SDK-style `net8.0` builds
  and a case-sensitive-filesystem hazard (no GPS `App.config` exists). _At parity._

### UI shell

#### Added

- **Avalonia 11.3.18 UI stack** (the AAP-recommended 11.3.x line): `Avalonia`, `Avalonia.Desktop`,
  `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `Avalonia.Controls.ColorPicker`, and
  `Avalonia.Diagnostics` (developer-only, `Debug`-conditioned). On disk for `GPS`, `AgIO`, `ModSim`,
  `AgDiag`, `GPS_Out`, `Keypad`, and `Updater`.
- **`SourceCode/GPS/App.axaml(.cs)`** — a new Avalonia `Application` with the Fluent theme plus a
  day/night palette derived from the `FormGPS` colors, and the GPS composition root that assembles the
  `ShellCommands` bundle, registers the per-OS platform services, reuses `Program.PlatformServices`, and
  wires the field-close lifecycle and AgIO auto-start/stop.

#### Changed

- **Windows Forms surface reimplemented as Avalonia views** (~88 forms total): `FormGPS` + ~67 GPS
  dialogs → `SourceCode/GPS/Views/**`; `FormLoop` + ~21 AgIO dialogs → `SourceCode/AgIO/Source/Views/**`;
  the `Keypad` `GenericKeypad` / `NumKeypad` / `Keyboard` user controls → Avalonia `UserControl`s. All
  60 GPS `.axaml` dialog/shell views (including `MainView` and the custom chart controls) each have a
  matching `.axaml.cs` code-behind — declared handlers implemented, `AutomationProperties.Name`
  accessibility labels added on interactive controls, day/night theme tokens applied — and **all compile
  into the normal GPS build** on both TFMs. _At parity (local)._
- **MainView kiosk shell fully wired.** Every operator button, toggle, and hotkey is bound to a real
  command via the `ShellCommands` delegate bundle; manual section/zone buttons are bound to live
  `SectionService` availability rather than hard-disabled; every migrated dialog is reachable from the
  shell command/menu graph; the steer-wizard workflow is migrated and wired. _At parity (local)._
- **Core MVVM/Presenter scaffold wired to views.** The existing-but-null-wired `RelayCommand`,
  `IPanelPresenter`, and `IErrorPresenter` in `AgOpenGPS.Core` are now bound to Avalonia views via data
  binding, replacing the legacy `FormGPS` constructing `ApplicationCore(dir, null, null)`.

_Note:_ this is a **1:1 parity reimplementation** — no redesign and no new screens; the visual reference
is the current Windows Forms UI (no Figma was supplied, AAP §0.3.3).

### Rendering

#### Added

- **`SourceCode/GPS/Controls/AvaloniaGeoViewport.cs`** — a `: GeoViewportBase` adapter over Avalonia's
  `OpenGlControlBase`, overriding `OnOpenGlInit` / `OnOpenGlRender` / `OnOpenGlDeinit` and binding the
  kept OpenTK 3.3.3 GL bindings to Avalonia's GL context via `GlInterface.GetProcAddress`. Provides the
  GLES/ANGLE runtime fail-safe and the `RequestDesktopGlProfile` hook. On disk and compiled into the GPS
  build. _At parity (local); on-hardware GL = Open Risk #1._
- **`SourceCode/GPS/Services/RenderCoordinator.cs`** — projection/frustum/back-buffer scan/overlays
  extracted from `OpenGL.Designer.cs`; three `GL.ReadPixels` paths preserved (section/lookahead scan,
  zoom overlap, flag pick). On disk and compiled. _At parity (local); on-hardware GL = Open Risk #1._

#### Changed

- **`BuildAvaloniaApp()` requests a desktop-GL profile.** Both `GPS/Program.cs` and
  `AgIO/Source/Program.cs` chain `AvaloniaGeoViewport.RequestDesktopGlProfile(...)` so a desktop-GL
  compatibility context is requested ahead of ANGLE/EGL.
- The three legacy GL surfaces **`oglMain` / `oglZoom` / `oglBack`** map to one or more
  `OpenGlControlBase` instances (or one control with offscreen framebuffers), where **`oglBack` is the
  offscreen buffer used for the section/lookahead `glReadPixels` pixel scan**.

#### Removed

- **`OpenTK.GLControl 3.3.3`** WinForms GL host — replaced by Avalonia's `OpenGlControlBase` (ships in
  `Avalonia.OpenGL`, no separate package). The package reference is dropped from the GPS `csproj`.
  _At parity (local)._

_Unchanged:_ **`OpenTK 3.3.3`** math/bindings and the `AgOpenGPS.Core/Drawing` `GeoViewportBase` + `GLW`
DrawLib are kept as-is — the renderer is insulated from the host swap by the existing abstraction.

> **Dominant feasibility risk (Open Risk #1 in `PARITY_REPORT.md`).** Avalonia's GL context is frequently
> OpenGL ES / ANGLE, while the Core DrawLib/`GLW` uses immediate-mode legacy OpenGL with a `glReadPixels`
> back-buffer scan. This is mitigated on disk by the `RequestDesktopGlProfile` hook (now wired in both
> composition roots) and the adapter's GLES/ANGLE runtime fail-safe (gates the immediate-mode path to a
> harmless cleared surface so it can never crash-loop). The residual is **on-hardware, per-OS
> confirmation** that a desktop-GL context is obtained and the back-buffer scan renders correctly.

### Platform services

#### Added

- **`SourceCode/AgOpenGPS.Core/Platform/IPlatformServices.cs`** — a single new abstraction isolating
  every OS-specific call: application-data/config root, monitor brightness get/set, serial-port-name
  enumeration, and single-instance acquisition. On disk and compiling within the portable Core.
- **`SourceCode/AgOpenGPS.Core/Platform/PlatformServicesFactory.cs`** — selects the concrete
  implementation at startup via `RuntimeInformation.IsOSPlatform` using **delegate registration** (no
  `Core → app` dependency). **All four bootstraps** (GPS `Program.cs` + `App.axaml.cs`, AgIO `Program.cs`
  + `App.axaml.cs`) call `Register(...)` then `Create()`; GPS publishes the single process-level instance
  as `Program.PlatformServices`, which the App composition root reuses. _At parity (local)._
- **`WindowsPlatformServices.cs`** (WMI brightness + named-mutex single-instance + Registry
  migration-read, under `net8.0-windows`), **`LinuxPlatformServices.cs`** (sysfs `/sys/class/backlight`
  best-effort brightness; `~/.config/AgOpenGPS` config root; lockfile + advisory lock), and
  **`MacPlatformServices.cs`** (`~/Library/Application Support/AgOpenGPS` config root; brightness no-op;
  lockfile + `flock`) — in both GPS `Platform/` and AgIO `Services/`. _At parity (local)._

_Note:_ per the migration rules, new architectural surface area is limited to `IPlatformServices` plus
the OpenGL host adapter — no unrelated abstractions were introduced.

### Mapping

#### Changed

- **`System.Windows.Forms.DataVisualization` steering/heading charts → custom Avalonia chart controls**,
  preserving series, axes, zoom/autoscale, and the rolling data buffer (`FormGraphHeading` /
  `FormGraphSteer` / `FormGraphXTE` / `FormCorrection` → `FormGraphXTEView`, `HeadingChartControl`, and
  the `FormGraph*View` / `FormCorrectionView` code-behind). The Windows-only `<Reference>` is removed from
  the GPS `csproj`. On disk and compiling. _At parity (local)._

#### Removed

- **`GMap.NET.WinForms 2.1.7`** (online background imagery) — **feature-gated** (F-021); the SQLite tile
  cache stays cross-platform and the field still renders without imagery. Package removed.
  _Feature-gated (per-OS)._
- **`MechanikaDesign.WinForms.UI.ColorPicker 2.0.0`** → replaced with Avalonia's `ColorSpectrum` /
  `ColorSlider` in `FormColorPickerView`. _At parity (local)._
- **`Accord.Imaging 3.8.0` + `Accord.Video.DirectShow 3.8.0`** webcam capture — **feature-gated**
  off-Windows (F-045; DirectShow is Windows-only and abandoned; default `isWebCamOn=false`).
  _Feature-gated (per-OS)._

### Serial

#### Changed

- **Serial communication keeps `System.IO.Ports`**, now as the **cross-platform** NuGet package
  `System.IO.Ports 9.0.0` (Windows/Linux/macOS), so serial behavior is preserved; only port-**name**
  enumeration is abstracted behind `IPlatformServices` (`COMx` vs `/dev/ttyUSB*`, `/dev/ttyACM*`,
  `/dev/cu.*`). Applies to AgIO `SerialComm` and `GPS_Out` (F-026 / F-038; the 4-second NMEA timeout is
  preserved). On disk in AgIO's `SerialCommService` and the GPS serial path. _At parity (local)._

### Settings

#### Changed

- **Settings backing swapped from the Windows Registry** (`docs/settings.md` L26-28: _"All settings are
  stored in Windows Registry (not .config files)"_) **and `%AppData%`** to an `IPlatformServices`
  cross-platform config root — Windows `%AppData%\AgOpenGPS`, Linux `~/.config/AgOpenGPS`, macOS
  `~/Library/Application Support/AgOpenGPS`. Files:
  `GPS/Properties/{RegistrySettings,VehicleSettings,ToolSettings,Settings,SettingsLegacy}.cs` and
  `AgIO/Source/Properties/{RegistrySettings,Settings}.cs`. `RegistrySettings.cs` no longer makes live
  `Registry.CurrentUser` calls; the one-time Windows migration-read is confined to `WindowsPlatformServices`.
  Verified by `SettingsRoundTripTests`. _At parity (local) (F-036)._
- **`GPS/Classes/CSettingsMigration.cs`** performs a one-time Registry read on Windows (wired into
  `GPS/Program.cs` immediately before `RegistrySettings.Load()`, Windows-gated and idempotent); the
  legacy→split round-trip is preserved exactly, with regression tests. _At parity (local)._
- **AgIO `XDocument.Load` hardened** with `DtdProcessing = Prohibit` + `XmlResolver = null` (defense in
  depth for the local app-data store). _At parity (local)._

_Unchanged / frozen:_ the split settings XML schema — Vehicle `VehicleProfiles/{name}.xml`, Tool
`ToolProfiles/{name}.xml`, Environment `Environment/environment.xml` (`docs/settings.md` L32-36) — is
preserved and verified by `SettingsRoundTripTests` (F-036).

### Build/CI

#### Changed

- **[XPLAT] F1-001 — `GPS` now builds zero-warning in Release; the warnings-as-errors gate hardened.**
  The final build/packaging QA checkpoint found the `GPS` project emitting **16 `AVLN3001`** Avalonia
  XAML-compiler warnings in a clean Release rebuild (8 unique views × the `net8.0` + `net8.0-windows`
  legs), violating the "all projects build with zero warnings" acceptance bar (AAP §0.7). Root cause:
  eight views declared only a parameterized (DI) constructor, so Avalonia's compiled-XAML runtime
  loader (`AvaloniaXamlLoader`) could not reach the `avares://` resource (it needs a **public
  parameterless** constructor). Resolved two ways:
  - **Constructors (the fix).** Added the standard Avalonia **dual-constructor** pattern to each of the
    eight views — a public parameterless ctor that calls `InitializeComponent()`, with the existing
    constructor chaining to it via `: this()` (so `InitializeComponent()` still runs exactly once and
    all dependency wiring is unchanged). This matches the convention already used by 39 sibling views
    (e.g. `FormDialogView`, `FormBoundaryView`, `FormColorView`). Views fixed:
    `Views/Field/FormEnterFlagView`, `Views/Field/FormFlagsView`, `Views/FormAgShareSettingsView`,
    `Views/FormInputDialogView` (public parameterless + the existing **private** parity ctor, mirroring
    `FormDialogView`), `Views/Inputs/FormKeyboard`, `Views/Inputs/FormNumeric`,
    `Views/Settings/FormConfigView`, `Views/Settings/FormCorrectionView`. No behavior change (UI-shell
    constructors only; no behavior-frozen contract touched).
  - **Gate hardening.** `SourceCode/Directory.Build.props` Release config now also sets
    **`<MSBuildTreatWarningsAsErrors>true</MSBuildTreatWarningsAsErrors>`** alongside the existing
    `TreatWarningsAsErrors`. Plain `TreatWarningsAsErrors` only escalates Roslyn `CS####` warnings, so
    Avalonia **MSBuild-task** warnings such as `AVLN3001` previously passed the Release gate silently;
    the new property promotes them to errors, so this class of regression now **fails** the Release
    build and the tri-OS CI `dotnet build` step. Verified: full-solution clean Release rebuild =
    *"Build succeeded. 0 Warning(s). 0 Error(s)."* (was 16 warnings); `linux-x64` self-contained
    publish of all six executables succeeds warning-free; tests remain 115 passed / 1 skipped / 0
    failed. _At parity._
- **`.github/workflows/build.yml`** from a single `windows-latest` runner to a `strategy.matrix` over
  windows-latest / ubuntu-latest / macos-latest (the macOS leg covering `osx-x64` + `osx-arm64`) with
  `fail-fast: false` and a per-OS artifact `AgOpenGPS-${{ matrix.os }}`. `<EnableWindowsTargeting>true</EnableWindowsTargeting>`
  added to `Directory.Build.props` so non-Windows legs can restore/build the `net8.0-windows` TFM; the
  solution-level publish replaced with the per-project / per-RID / self-contained strategy (a
  solution-level RID publish fails `NETSDK1129`); least-privilege `permissions:` blocks added. Action
  pins kept: `actions/checkout@v5`, `actions/setup-dotnet@v4`, `gittools/actions/gitversion/setup@v3.2.0`
  (`versionSpec 5.12.x`), `gittools/actions/gitversion/execute@v3.2.0`, `actions/upload-artifact@v4`. On
  disk. _At parity (local); CI execution on GitHub-hosted runners pending (external evidence)._
- **`.github/workflows/release.yml`** to four per-RID self-contained publish legs (windows→`win-x64`,
  ubuntu→`linux-x64`, macos→`osx-x64`, macos→`osx-arm64`); the PowerShell `Compress-Archive` step is
  replaced with cross-platform archiving guarded by `if: runner.os`; per-RID asset names embed the
  version + RID; the license files are bundled (root `LICENSE` Apache 2.0, `GPS/License.txt` GPLv3,
  `Updater/License.txt` GPLv3); the `softprops/action-gh-release@v2` draft/prerelease logic is retained.
  On disk. _At parity (local); CI execution pending._
- **`SourceCode/.editorconfig`** `*.Designer.cs` analyzer glob is the capital-`D` form and lists
  `Position` (and `Config*`, `Controls`, `GUI`, `OpenGL`, `PGN`, `SaveOpen`, `Sections`, `UDPComm`,
  `NMEA`, `NTRIPComm`, `SerialComm`, `UDP`), carrying the `[XPLAT]` provenance note. _At parity._

#### Added

- **Dependency vulnerability audit (SEC-7 / AAP §0.5).** `dotnet list package --vulnerable`
  (`--include-transitive`) was run against the **live NuGet advisory database** (`api.nuget.org`
  reachable, HTTP 200) across all twelve projects and both TFMs: **zero vulnerable packages** (21
  distinct top-level references; 56 resolved including transitive for GPS `net8.0`). The pinned on-disk
  versions are the Avalonia stack at **11.3.18**, `Dev4Agriculture.ISO11783.ISOXML 0.23.1.1`,
  `OpenTK 3.3.3`, `Newtonsoft.Json 13.0.4`, `SkiaSharp 2.88.9`, `SourceGear.sqlite3 3.50.3`,
  `System.Data.SQLite 2.0.1`, `System.IO.Ports 9.0.0`, `System.Management 9.0.0`,
  `Microsoft.Win32.Registry 5.0.0`, `System.Configuration.ConfigurationManager 9.0.0`,
  `System.Resources.Extensions 9.0.0`, and the test toolchain `Microsoft.NET.Test.Sdk 17.12.0`,
  `NUnit 4.3.2`, `NUnit3TestAdapter 4.6.0`, `NUnit.Analyzers 4.6.0`. Recorded in `PARITY_REPORT.md`.
  _At parity (local); re-run as a standing CI gate._

### Logic decoupling

#### Changed

- **Scan-loop / guidance / section / communication logic lifted out of the `FormGPS` / `FormLoop`
  `.Designer.cs` partials into plain, constructor-injectable services** (behavior frozen):
  `PositionService.cs` (← `Position.designer.cs`), `PgnDispatcher.cs`
  (← `UDPComm.Designer.cs` + `PGN.Designer.cs`), `SectionService.cs` (← `Sections.Designer.cs`),
  `FieldIoService.cs` (← `SaveOpen.Designer.cs`), `RenderCoordinator.cs` (← `OpenGL.Designer.cs`), and
  the AgIO `Services/*` (← `{NMEA,NTRIPComm,SerialComm,UDP}.Designer.cs`). **All five GPS services and the
  four AgIO services (`NmeaService`, `NtripService`, `SerialCommService`, `UdpLoopbackService`) are on
  disk and compiling**, covered by golden-file parity suites green on local Linux. `SectionService`
  exposes `PerformSectionClick`/`PerformZoneClick` + section/zone state readers for the kiosk shell;
  `FieldIoService` exposes before/after field-close callbacks wired from the composition root.
  _At parity (local)._

_Note:_ this eliminates the `mf` / `FormGPS` god-object back-reference via constructor injection; the
≤70 ms `udpWatchLimit` throttle and the receive→fuse→steer→section path are preserved with no added
latency.

### Core & algorithms

#### Changed

- **`SourceCode/AgOpenGPS.Core/**/*.cs` recompiled for `net8.0`; WPF types purged** — `CommandManager`
  in `ViewModels/RelayCommand.cs`, `Visibility` in `FieldTableViewModel.cs`, and the `Media3D` `using`
  in `Models/Camera.cs`. The Core builds clean and its 33 tests pass. _At parity._
- **`SourceCode/GPS/Classes/**/*.cs` recompiled, behavior frozen** — Stanley
  `CGuidance.DoSteerAngleCalc()` and Pure Pursuit `CTrackMethods.GoalPoint()`
  (`atan2(2·wheelbase·sin(error), lookahead)`); the safety guards `maxSteerAngle = 30°` and
  `maxAngularVelocity = 0.64°/s` (`docs/settings.md` L54-55) are unchanged. **All classes now compile in
  the normal GPS build** — the `FormGPS` / `mf` back-reference is decoupled into injected
  `ApplicationModel` / services / delegates (`CSmartWAS(FormGPS)` → `CSmartWAS(ApplicationModel)`; the
  only residual `FormGPS` tokens are `// [XPLAT]` provenance comments). Verified by
  `GuidanceEquivalenceTests`. _At parity (local). Residual: the live `SetGuidanceReferences` peer-wiring
  is a pre-existing composition gap — Open Risk #8._

#### Removed

- **`PresentationCore` (Core), `WindowsBase` (AgIO), and `System.Windows.Forms` (Updater) GAC
  references** — removed. _At parity (local)._
- **`System.Management`** moved from a GAC reference to the NuGet package under `net8.0-windows`,
  confined to `WindowsPlatformServices`. _At parity (local)._

### Tests & parity

#### Added

- **Five golden-file parity suites authored, committed, and enforcing** under
  `SourceCode/AgOpenGPS.Tests/Parity/`: `PgnFrameGoldenTests`, `FieldRoundTripTests`,
  `IsoXmlEquivalenceTests`, `SettingsRoundTripTests`, `GuidanceEquivalenceTests` (modeled on
  `AgLibrary.Tests/Settings/XmlSettingsHandlerTests.cs`). The `Parity/Golden/**` fixtures are committed,
  including the **11 enforcing `Field/*.txt` goldens** (Boundary, Tracks, Sections, Flags, Contour,
  Headland, Headlines, Tram, RecPath, Elevation, FieldPlane) captured via the production IO serializers
  under `InvariantCulture`. Each loader checks `File.Exists` and **fails** (not Ignores) when a required
  golden is absent — proven by a negative test. `.gitattributes` pins the fixtures byte-stable (`-text`).
  **Green on local Linux**; tri-OS CI is the residual. _At parity (local)._

- **[XPLAT] F5 — production-invoking guidance/section parity (M1, M2, M4, M5, M6).**
  `SourceCode/AgOpenGPS.Tests/Parity/ParityGraphFixture.cs` builds the **real** guidance/section graph in
  the live `App.axaml.cs` composition order and wires the cyclic peers (`SetGuidanceReferences`). Two new
  suites — **`SectionControlParityTests`** (real `SectionService.BuildMachineByte` 1–16/≤64 modes + the
  `isJobStarted` gate via real `DoRemoteSwitches`) and **`GuidanceAlgorithmCoverageTests`** (real
  `CDubins.GenerateDubins` + determinism, `CSmartWAS`, `CContour`, `CABCurve`, `CRecordedPath`, `CYouTurn`)
  — join the rewritten **`GuidanceEquivalenceTests`** (now invoking the real `CGuidance.DoSteerAngleCalc`,
  `CABLine.GetCurrentABLine`, and clamp). New production-captured goldens `Guidance/sections_machinebyte.csv`
  and `Guidance/algorithms.csv` (legacy `sections.csv` kept as a labelled secondary cross-check). **17/17
  production-invoking parity tests green on local Linux.** _At parity (local)._

- **[XPLAT] F5 — CAHRS fusion seam test (M3).** A production-invoking test exercises the new pure static
  `CAHRS.FuseImuGpsHeading` (the extracted, behavior-preserving IMU+GPS fusion) against a production-captured
  golden. _At parity (local)._

#### Changed

- **`AgOpenGPS.Tests.csproj`** drops `<PlatformTarget>x64</PlatformTarget>` (invalid for `osx-arm64`)
  and `System.Memory`, and adds an `AgOpenGPS.Core` `ProjectReference` plus
  `Dev4Agriculture.ISO11783.ISOXML 0.23.1.1`. The test toolchain is kept: `NUnit 4.3.2`,
  `Microsoft.NET.Test.Sdk 17.12.0`, `NUnit3TestAdapter 4.6.0`, `NUnit.Analyzers 4.6.0`. _At parity (local)._

- **[XPLAT] F5 — CI runtime roll-forward (m1).** Added `<RollForward>Major</RollForward>` to
  `AgOpenGPS.Tests.csproj`, `AgOpenGPS.Core.Tests.csproj`, and `AgLibrary.Tests.csproj`, and pinned explicit
  `dotnet-version` (`8.0.x` + `9.0.x`) on the `setup-dotnet` step in `.github/workflows/build.yml` and
  `release.yml`, so the **net8.0** test assemblies launch reliably even on a runner image that resolves only
  the 9.0 runtime. Verified by running the full suite with `DOTNET_ROLL_FORWARD` unset on a 9.0-only
  container. _At parity._

- **[XPLAT] F5 — test documentation reconciled (m2, m3, m4, i3).** The `GuidanceEquivalenceTests.cs` header
  and the `Parity/Golden/Guidance/README.md` were rewritten to describe production invocation and
  production-captured goldens (the CP6 `<Compile Remove>` gating was removed at CP9), the inactive `0.64°/s`
  angular-velocity limiter (nominal in both net48 and migrated) is documented, and the production
  Pure-Pursuit `Math.Atan` goal-point form (vs. the documented `atan2`, mathematically equivalent) is noted.

#### Removed

- **`SourceCode/AgOpenGPS.Tests/SampleTest.cs`** placeholder — deleted from disk; its role is superseded
  by the golden-file parity suites above. _Removed._

_Known limitation:_ one test — `IsoXmlExport_DrivenFromDomainGraph_*` — is skipped because it requires a
full `FormGPS` domain graph; the ISOXML **import / round-trip** contract is otherwise enforced by
`IsoXmlEquivalenceTests`. Recorded in `PARITY_REPORT.md`.

### Documentation

#### Added

- **`MIGRATION_DOCS/` deliverable set — all five on disk:** `CHANGELOG.md` (this file),
  `TRANSITION_MAP.md`, `PARITY_REPORT.md`, `FEATURE_TRACEABILITY.md`, and `VALUE_SUMMARY.md`. Each is
  reconciled with the final integrated on-disk state and carries the honest open residuals (tri-OS CI
  execution, on-hardware GL, latent guidance peer-wiring, the ISOXML export-driver limitation).

#### Changed

- **`README.md`** rewritten with the cross-platform posture ("AgOpenGPS is now cross-platform … runs
  natively on Windows, macOS, and Linux") and per-OS build/run/publish instructions (multi-target apps
  pair the framework to the RID, e.g. `-f net8.0-windows` for `win-x64`, `-f net8.0` elsewhere);
  **`docs/**/*.md`** references updated for the cross-platform stack. **`docs/pgn-protocol.md` is
  retained unchanged** as the frozen protocol contract (header `0x80 0x81 0x7F`, additive-checksum CRC,
  loopback ports 15555/17777). _At parity._

### Cross-platform correctness (case/culture/paths)

#### Changed

- **Case-fix renames** `Position.designer.cs` → `Position.Designer.cs`,
  `FormYes.designer.cs` → `FormYes.Designer.cs`, and
  `FormtimedMessage.resx` → `FormTimedMessage.resx` (the same `FormYes` / `FormtimedMessage` case fixes
  also apply under AgIO and ModSim). The GPS hazards are resolved by the retirement of the legacy
  `Forms/` tree, and the `.editorconfig` analyzer glob already uses the capital-`D` form; the AgIO/ModSim
  originals were removed and reimplemented as the Avalonia `FormYesView` / `FormTimedMessageView`.
  _At parity._
- **Numeric file/protocol I/O audited for `InvariantCulture`** (so a comma-decimal locale on Linux/macOS
  cannot corrupt field files, settings, ISOXML, or PGN-derived text); hard-coded `\` replaced with
  `Path.Combine` / `Path.DirectorySeparatorChar`; **`.gitattributes`** `-text` rules added for the
  byte-stable parity fixtures (`SourceCode/AgLibrary.Tests/Settings/TestSettings.xml`,
  `SourceCode/AgOpenGPS.Tests/Parity/**`, `**/*.txt`, `**/*.xml`). _At parity (local)._

### Licensing

_Unchanged:_ `/LICENSE` (Apache 2.0) and the GPLv3 `SourceCode/GPS/License.txt` and
`SourceCode/Updater/License.txt` are **retained, not rewritten**. The per-program license artifacts are
preserved across the migration (Apache 2.0 at the repository root; GPLv3 for the GPS and Updater
projects). _At parity._

---

_This changelog is the narrative spine of the migration, reconciled with the final integrated on-disk
state. See also `TRANSITION_MAP.md` (file-by-file old→new mapping and per-row parity status),
`PARITY_REPORT.md` (behavioral-parity proof and open risks — including the GL-context /`glReadPixels`
Open Risk #1 and the latent guidance peer-wiring Open Risk #8), `FEATURE_TRACEABILITY.md` (per-feature
F-001…F-045 cross-platform disposition), and `VALUE_SUMMARY.md` (executive brief) — all on disk._
