# Transition Map — AgOpenGPS net48/WinForms → net8.0/Avalonia Migration

<!-- [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/CHANGELOG.md -->

This document provides the old→new file-by-file mapping for the AgOpenGPS migration from
`.NET Framework 4.8` / Windows Forms to `.NET 8/9` / Avalonia UI (cross-platform: Windows,
macOS, Linux), covering all twelve projects in `SourceCode/AgOpenGPS.sln`. Each entry records
the original Windows-coupled file, its cross-platform replacement (if any), the disposition,
and the parity status. It is maintained incrementally as files are migrated.

> **Accuracy contract.** This map records only what is **actually present in the repository at
> the current checkpoint**. A row may claim a replacement file exists only when that file is on
> disk and compiles. Work that is intentionally scheduled for a later checkpoint is recorded with
> the **`Deferred`** parity status and a `(deferred — see notes)` replacement marker — never as
> "At parity". This contract was introduced to correct earlier rows that recorded views/services
> as complete before they existed.

## Legend

| Column | Description |
|--------|-------------|
| **Original** | Path of the source (pre-migration) file in the `net48`/WinForms baseline |
| **Replaced With** | Path(s) of the replacement file(s) that **exist in the repository now**, `(deferred — see notes)` when the WinForms file is removed but its Avalonia replacement is scheduled for a later checkpoint, or `(removed)` when deleted with no replacement |
| **Disposition** | `Migrated` (minimal-change recompile / de-Windowsed) · `Reimplemented` (rebuilt on the Avalonia stack with 1:1 parity) · `Extracted` (non-visual logic lifted into an injectable service) · `Feature-gated` (Windows-only capability, graceful no-op elsewhere) · `Unchanged` · `Deleted` |
| **Parity Status** | `At parity` (replacement exists and is behavior-verified) · `Pending verification` (replacement exists, parity test pending) · `Deferred` (replacement scheduled for a later checkpoint; WinForms code removed) · `N/A` (documentation/config, not a behavioral artifact) |
| **Notes** | Additional context |

> **Checkpoint legend.** This map is authored across multiple checkpoints (CP). The current
> checkpoint is **CP4 — GPS WinForms Dialog Catalog Part 2 Retired (Settings / Guidance / Inputs /
> Profiles / Field Part 2 + Charting)**. CP4 removes the **remaining** GPS WinForms dialogs — the
> second batch — completing the retirement of the entire legacy GPS Windows Forms UI catalog:
> **128 files** in total across `SourceCode/GPS/Forms/{Field,Guidance,Inputs,Profiles,Settings}/`,
> covering **47 dialog/config groups** (11 Field Part 2, 12 Guidance, 2 Inputs, 4 Profiles, 18
> Settings/Config/Charting). With CP4, `SourceCode/GPS/Forms/` is **absent** and
> `Forms/Settings/FormButtonsRightPanel.*` was the **last legacy WinForms dialog** deleted. Their
> Avalonia Views/ViewModels (and the six `Config*.Designer.cs` config panels, and the three
> charting forms' cross-platform chart replacement) are **Deferred** to later GPS checkpoints
> (CP4–CP9) and are recorded in the CP4 sections below with named target files and frozen-behavior
> contracts — **never** as "At parity" (`SourceCode/GPS/Views/` and `SourceCode/GPS/Services/` do
> not exist yet). The Avalonia GL viewport and the GPS `csproj`→Avalonia conversion remain
> later-checkpoint work (CP9) per the AAP's one-solution, multi-checkpoint structure. CP1–CP3
> retired the runtime baseline, the AgIO hub, the GPS `FormGPS` shell + real-time partials, and the
> first dialog batch (root dialogs + Config controls + Pickers + Field Part 1); those sections are
> retained below for historical traceability.
>
> **GPS buildability (CP4 — unchanged from CP3).** Because the runtime moniker was flipped to
> `net8.0` in CP1 while the GPS UI is re-platformed later, the **GPS project remains intentionally
> non-buildable until its Avalonia `csproj` conversion completes** (CP9). CP4 deletes only dialog
> sources and updates documentation + the GPS `csproj` charting/metadata; it does **not** alter the
> `UseWindowsForms`/TFM blocker, so the GPS build still fails at SDK target-resolution
> (NETSDK1100/NETSDK1136) **before any source is compiled** — identically to CP3. See *GPS — Build
> Sequencing & Retained-Reference Inventory* below and `CHANGELOG.md`. AgIO, AgOpenGPS.Core,
> AgLibrary, and the test projects build and pass.

---

## AgIO — Comms-Hub Services (non-visual logic extracted from `FormLoop` partials)

The non-visual AgIO transport/parse logic that lived in `FormLoop` partial classes is **Extracted**
into plain, constructor-injectable service classes under `SourceCode/AgIO/Source/Services/`. These
services **exist in the repository and compile** as of CP2. They preserve the frozen PGN/socket/NMEA
contracts byte-for-byte (header `0x80 0x81 0x7F`; PGN `0xD6` source byte `0x7C` preserved as in the
baseline; additive-checksum CRC; loopback ports `15555`/`17777`; module discovery `255.255.255.255:8888`
bind `IPAddress.Any:9999`) per AAP §0.2.2/§0.7.1.

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/AgIO/Source/Forms/UDP.designer.cs` | `SourceCode/AgIO/Source/Services/UdpLoopbackService.cs` | Extracted | At parity | `// [XPLAT]` Non-visual UDP/loopback logic plus helper types `CTraffic`/`CScanReply`, lifted into the injectable `UdpLoopbackService`. **Frozen contract preserved**: `helloFromAgIO = {0x80,0x81,0x7F,200,3,56,0,0,0x47}`; send to AgOpenGPS loopback `127.0.0.1:15555`; bind `IPAddress.Loopback:17777`; module-scan broadcast `255.255.255.255:8888`; bind `IPAddress.Any:9999`; `SetSocketOption Broadcast`; async `BeginReceiveFrom`/`EndReceiveFrom`/`BeginSendTo` with no added latency. `0x80 0x81` header validation retained. UI-thread `BeginInvoke`/`MethodInvoker` replaced by status **events** (no `System.Windows.Forms`). Lowercase `.designer.cs` case-hazard resolved (AAP §0.6.5/G7). |
| `SourceCode/AgIO/Source/Forms/SerialComm.Designer.cs` | `SourceCode/AgIO/Source/Services/SerialCommService.cs` | Extracted | At parity | `// [XPLAT]` Serial transport for six port roles (GPS, GPS2, RTCM, IMU, SteerModule, MachineModule): `System.IO.Ports.SerialPort` open/close/read/write, a shared `ProcessModuleBytes()` PGN frame parse loop (`0x80 0x81` header + additive-CRC `CK_A += byteList[j]` for `j=2..length`), and `SendSteerModulePort`/`SendMachineModulePort`. Incoming GPS/IMU bytes feed `NmeaService.rawBuffer`. Port-name enumeration abstracted behind a `GetAvailablePortNames()` seam (`SerialPort.GetPortNames()` today; `IPlatformServices` later) covering `COMx` vs `/dev/ttyUSB*`,`/dev/ttyACM*`,`/dev/cu.*`. Numeric parsing uses `InvariantCulture` (AAP §0.6.5). Status surfaced via `PortStatusChanged` event. `MessageBox.Show` removed. |
| `SourceCode/AgIO/Source/Forms/NTRIPComm.Designer.cs` | `SourceCode/AgIO/Source/Services/NtripService.cs` | Extracted | At parity | `// [XPLAT]` NTRIP TCP client: caster connect, Base64 auth, GGA send, RTCM receive + forward to modules, source-table fetch. WinForms timers → `Avalonia.Threading.DispatcherTimer`. DNS resolution kept via an explicit `ResolveCasterIP()` helper. Status/log surfaced via `StatusChanged` + `MessageRequested` events. `System.Windows.Forms` purged. |
| `SourceCode/AgIO/Source/Forms/NMEA.Designer.cs` | `SourceCode/AgIO/Source/Services/NmeaService.cs` | Extracted | At parity | `// [XPLAT]` NMEA parse/build (GGA, VTG, HDT, AVR/PTNL, HPD, PAOGI, PANDA, KSXT, RMC, TRA, PSTI/032/035/036) and PGN `0xD6` forwarding to AgOpenGPS via UDP loopback. **Hardened in CP2**: the UDP transport is now a **required** constructor dependency (`readonly NmeaService(UdpLoopbackService udp)`, fail-fast `ArgumentNullException`) — no nullable `Udp` property, no silent traffic drop, no nullable-member dereference in arguments. **Per-sentence field-count guards** added before every parser call (CWE-20). `ValidateChecksum()` logs a sanitized reason (exception type + sentence id + length), never `ex.ToString()`. PGN `0xD6` frame bytes preserved byte-for-byte incl. source byte `0x7C` (see CP2 note in `NmeaService.cs`). All numeric parse/format uses `InvariantCulture`. |
| `SourceCode/AgIO/Source/Forms/Controls.Designer.cs` | `SourceCode/AgIO/Source/Views/MainWindow.axaml.cs` (composition root) + `Services/*` | Extracted | At parity | `// [XPLAT]` Non-visual `FormLoop` coordination partial (timer wiring, `TimedMessageBox` helper, dialog ownership). Coordination role moves into the `MainWindow` composition root (which instantiates and wires `UdpLoopbackService`/`NmeaService`/`SerialCommService`/`NtripService` and runs the scan loop). `TimedMessageBox` prompts → Avalonia `FormTimedMessageView`/`FormYesView`. `System.Windows.Forms` purged. |

---

## AgIO — UI Shell & Composition Root (`FormLoop` → Avalonia `MainWindow` + `App`)

The AgIO main shell is **Reimplemented** on Avalonia and **exists in the repository** as of CP2. There
is **no separate `MainWindowViewModel.cs`**: per AAP §0.3.2 the `MainWindow` code-behind is the
comms-hub composition root (mirroring how the WinForms `FormLoop` owned its collaborators and loop
timer), instantiating and wiring the four transport services and the one-second scan loop.

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/AgIO/Source/Forms/FormLoop.cs` | `SourceCode/AgIO/Source/Views/MainWindow.axaml`<br/>`SourceCode/AgIO/Source/Views/MainWindow.axaml.cs`<br/>`SourceCode/AgIO/Source/App.axaml`<br/>`SourceCode/AgIO/Source/App.axaml.cs` | Reimplemented | Pending verification | `// [XPLAT]` AgIO main application shell (~803L). Startup coordination, periodic timers (1 s / 2 s / 10 s / 3 min), module hello-alarm status (GPS/Steer/Machine/IMU), profile/reconnect/shutdown. `MainWindow.axaml.cs` is the composition root: `new UdpLoopbackService()` → `new NmeaService(_udp)` → `new SerialCommService()` → `new NtripService()`; it also issues `SendHelloToModules()`. Win32 `user32.dll` P/Invoke → Avalonia `Window.Activate()`/`WindowState`/`Topmost`. Status colors → bound to `App.axaml` status brushes. Two-program model preserved (AgIO auto-started by AgOpenGPS over UDP loopback). `System.Windows.Forms` purged. |
| `SourceCode/AgIO/Source/Forms/FormLoop.Designer.cs` | `SourceCode/AgIO/Source/Views/MainWindow.axaml` | Reimplemented | Pending verification | `// [XPLAT]` WinForms designer-generated layout (~1261L) for the AgIO comm-hub UI — module status buttons, NTRIP byte-activity label, lat/lon readouts, watchdog label, menu/toolbar. Reimplemented as Avalonia XAML; status `BackColor` assignments become bindings to `App.axaml` status brushes. AgIO is light-theme only (no day/night). |
| `SourceCode/AgIO/Source/Forms/FormLoop.resx` | `SourceCode/AgIO/Source/Views/MainWindow.axaml` | Reimplemented | N/A | `// [XPLAT]` WinForms `.resx` for the main window (button-icon bitmaps from `AgIO.Properties.Resources`, timer tray metadata, ResX boilerplate). The current `MainWindow.axaml` uses colored `Border` status indicators rather than embedded button-icon bitmaps; `ResXFileCodeGenerator` pipeline not used in the Avalonia architecture. Icon assets migrate to `avares://` at the UI-polish checkpoint. |

---

## AgIO — Touch Dialogs (Reimplemented on Avalonia in CP2)

These five dialogs are **Reimplemented** and **exist in the repository** as of CP2. On-disk view files
use the `Form<Name>View.axaml` naming convention (paired `.axaml.cs` code-behind + `Form<Name>ViewModel.cs`).
Callers are reached through the migrated extension helpers (see *AgIO — Controls / Extensions*).

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/AgIO/Source/Forms/FormYes.cs`<br/>`FormYes.designer.cs`<br/>`FormYes.resx` | `SourceCode/AgIO/Source/Views/FormYesView.axaml`<br/>`FormYesView.axaml.cs`<br/>`FormYesViewModel.cs` | Reimplemented | At parity | `// [XPLAT]` Yes/No confirmation dialog (message + OK/Cancel, optional Cancel visibility). Constructor params (`messageStr`, `showCancel`) preserved in the view-model; `AcceptButton`/`CancelButton` semantics replicated via Avalonia default/`IsCancel` buttons; `Close(true)`/`Close(false)` result. Lowercase `.designer.cs` case-hazard resolved (G7). |
| `SourceCode/AgIO/Source/Forms/FormTimedMessage.cs`<br/>`FormTimedMessage.Designer.cs`<br/>`FormtimedMessage.resx` | `SourceCode/AgIO/Source/Views/FormTimedMessageView.axaml`<br/>`FormTimedMessageView.axaml.cs`<br/>`FormTimedMessageViewModel.cs` | Reimplemented | At parity | `// [XPLAT]` Auto-dismissing timed popup (title + message + timeout). WinForms `Timer` → `Avalonia.Threading.DispatcherTimer` auto-dismiss. The `FormtimedMessage.resx` lowercase-`t` case-hazard (mismatching `FormTimedMessage.cs`) is resolved by deletion (AAP §0.6.5/G7). Serves as the Avalonia replacement target for transient `MessageBox.Show` prompts. |
| `SourceCode/AgIO/Source/Forms/FormKeyboard.cs`<br/>`FormKeyboard.Designer.cs`<br/>`FormKeyboard.resx` | `SourceCode/AgIO/Source/Views/FormKeyboardView.axaml`<br/>`FormKeyboardView.axaml.cs`<br/>`FormKeyboardViewModel.cs` | Reimplemented | At parity | `// [XPLAT]` On-screen keyboard for touch text entry, hosting the shared `Keypad.Keyboard` Avalonia `UserControl`; OK/Cancel close. Invoked via the migrated `TextBoxExtensions.ShowKeyboard()` (async `ShowDialog`). Touch-friendly large controls preserved 1:1. |
| `SourceCode/AgIO/Source/Forms/FormNumeric.cs`<br/>`FormNumeric.Designer.cs`<br/>`FormNumeric.resx` | `SourceCode/AgIO/Source/Views/FormNumericView.axaml`<br/>`FormNumericView.axaml.cs`<br/>`FormNumericViewModel.cs` | Reimplemented | At parity | `// [XPLAT]` On-screen numeric keypad for touch entry. **Increment/decrement repeat-button behavior is implemented and tested in CP2**: `FormNumericViewModel.Increment()`/`Decrement()` clamp to `Min`/`Max`, step ±1, parse with `InvariantCulture`, and clear the error/`"Error"` state (matching WinForms `BtnDistanceUp_MouseDown`/`BtnDistanceDn_MouseDown`); `FormNumericView.axaml` wires them to `RepeatButton`s. Bounds, decimal/sign entry, backspace/clear, and out-of-range error highlighting preserved. Verified by the CP2 ad-hoc parity test (increment/decrement/clamp/empty/`Error`/`TryGetResult`). Invoked via the migrated `NumericUpDownExtensions.ShowKeypad()`. |
| `SourceCode/AgIO/Source/Forms/FormPGN.cs`<br/>`FormPGN.designer.cs`<br/>`FormPGN.resx` | `SourceCode/AgIO/Source/Views/FormPGNView.axaml`<br/>`FormPGNView.axaml.cs`<br/>`FormPGNViewModel.cs` | Reimplemented | At parity | `// [XPLAT]` Read-only PGN Guide reference dialog; PGN number/description content migrated to the view-model. **Protocol-doc reconciliation (CP2 finding):** the PGN guide presents the generic AgIO source byte `0x7F` per `docs/pgn-protocol.md`; this is documented as distinct from the **NMEA PGN `0xD6`** frame, which intentionally preserves the baseline source byte `0x7C` (see the explanatory note in `NmeaService.cs` and `FormPGNViewModel.cs`). Lowercase `.designer.cs` case-hazard resolved (G7). |

---

## AgIO — Complex Configuration Dialogs (WinForms removed; Avalonia replacement Deferred)

The following WinForms configuration/diagnostic dialogs were **removed** in CP2 so the AgIO project
could be fully de-Windows-ified (the project no longer sets `UseWindowsForms`/`ImportWindowsDesktopTargets`,
so the original `System.Windows.Forms` code can no longer compile and must not be retained). Their
Avalonia replacements are **Deferred** to a later AgIO UI checkpoint. This is recorded honestly as
`Deferred` — **not** "At parity". These dialogs have **zero retained references** in the CP2 codebase
(verified: AgIO compiles clean in Debug and Release), and their underlying capabilities remain
available because their settings persist via the cross-platform settings store and are exercised by the
extracted services; only the **configuration UI surface** is temporarily absent.

| Original (all `.cs` + `.Designer`/`.designer.cs` + `.resx`) | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/AgIO/Source/Forms/FormAdvancedSettings.*` | `(deferred — see notes)` | Deleted | Deferred | Display-preference dialog (`setDisplay_isAutoRunGPS_Out`, `setDisplay_StartMinimized`, `setDisplay_ShowOnWarning`). Settings persist; Avalonia view scheduled for the AgIO UI checkpoint. |
| `SourceCode/AgIO/Source/Forms/FormCommSetGPS.*` | `(deferred — see notes)` | Deleted | Deferred | GPS serial/comm-port configuration dialog. Serial transport itself is live in `SerialCommService`; only the config UI is deferred. Port enumeration will bind to `IPlatformServices`. |
| `SourceCode/AgIO/Source/Forms/FormEthernet.*` | `(deferred — see notes)` | Deleted | Deferred | Ethernet/UDP loopback IP configuration dialog. UDP loopback transport is live in `UdpLoopbackService`; only the config UI is deferred. Lowercase `.designer.cs` case-hazard eliminated by removal. |
| `SourceCode/AgIO/Source/Forms/FormEventViewer.*` | `(deferred — see notes)` | Deleted | Deferred | Event/log viewer (StreamReader loop + `Log.sbEvents`). Logging persists; viewer UI deferred. |
| `SourceCode/AgIO/Source/Forms/FormGPSData.*` | `(deferred — see notes)` | Deleted | Deferred | Live GPS/NMEA telemetry readout. `NmeaService` produces the telemetry; the read-only display view is deferred. |
| `SourceCode/AgIO/Source/Forms/FormISOBUS.*` | `(deferred — see notes)` | Deleted | Deferred | ISOBUS CAN-adapter + AOG-TaskController lifecycle dialog. `Microsoft.Win32.Registry` path lookup will route through `IPlatformServices`; `Application.DoEvents()`/`InvokeRequired` → async/Dispatcher. Deferred to the UI checkpoint. |
| `SourceCode/AgIO/Source/Forms/FormNtrip.*` | `(deferred — see notes)` | Deleted | Deferred | NTRIP caster **configuration** dialog. NTRIP client behavior is live in `NtripService`; only the config UI (caster URL/credentials/GGA interval/source table) is deferred. `CheckIPValid`/source-table fetch live in the service. |
| `SourceCode/AgIO/Source/Forms/FormProfiles.*` | `(deferred — see notes)` | Deleted | Deferred | Hardware profile management (create/choose/save/save-as). Profiles persist via settings; management UI deferred. Lowercase `.designer.cs` case-hazard eliminated. |
| `SourceCode/AgIO/Source/Forms/FormRadio.*` | `(deferred — see notes)` | Deleted | Deferred | Radio/telemetry module configuration (scan/open/close, channel CRUD, AT commands). Serial transport live in `SerialCommService`; config UI deferred. Works with `CRadioChannel` (migrated, unchanged). |
| `SourceCode/AgIO/Source/Forms/FormRadioChannel.*` | `(deferred — see notes)` | Deleted | Deferred | Radio-channel editor (Id/Name/Frequency/Lat/Lon). Deferred with its parent `FormRadio`. |
| `SourceCode/AgIO/Source/Forms/FormSerialMonitor.*` | `(deferred — see notes)` | Deleted | Deferred | Serial-traffic monitor. `SerialPort.GetPortNames()` → `IPlatformServices` at the UI checkpoint; monitor UI deferred. Lowercase `.designer.cs` case-hazard eliminated. |
| `SourceCode/AgIO/Source/Forms/FormSerialPass.*` | `(deferred — see notes)` | Deleted | Deferred | Serial pass-through configuration (`setPass_isOn`, `setNTRIP_sendToSerial/UDP/UDPPort`, `setPort_*`). Settings persist; config UI deferred. Lowercase `.designer.cs` case-hazard eliminated. |
| `SourceCode/AgIO/Source/Forms/FormSource.*` | `(deferred — see notes)` | Deleted | Deferred | NTRIP source-table/nearest-station selection. **Parity note for the future view:** `double.TryParse` must use `InvariantCulture` (AAP §0.6.5) and the Haversine `GetDistance` must be reproduced unchanged. Deferred. |
| `SourceCode/AgIO/Source/Forms/FormUDP.*` | `(deferred — see notes)` | Deleted | Deferred | UDP module-scan/subnet configuration dialog. The frozen module-discovery contract (`255.255.255.255:8888`, bind `IPAddress.Any:9999`, scan PGN) is live in `UdpLoopbackService`; only the config UI is deferred. |
| `SourceCode/AgIO/Source/Forms/FormUDPMonitor.*` | `(deferred — see notes)` | Deleted | Deferred | UDP/loopback traffic monitor (capture toggle, GPS/NTRIP log filters, save to `zAgIO_UDP_log.txt`). The byte-level UDP traffic is live in `UdpLoopbackService`; the observer UI is deferred. Lowercase `.designer.cs` case-hazard eliminated. |

---

## AgIO — Controls / Extensions

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/AgIO/Source/Controls/TextBoxExtensions.cs` | `SourceCode/AgIO/Source/Controls/TextBoxExtensions.cs` (rewritten) | Migrated | At parity | `// [XPLAT]` `ShowKeyboard(...)` no longer instantiates the deleted WinForms `FormKeyboard`; it now launches the Avalonia `FormKeyboardView` via async `ShowDialog` and writes the result back to the bound text property. |
| `SourceCode/AgIO/Source/Controls/NumericUpDownExtensions.cs` | `SourceCode/AgIO/Source/Controls/NumericUpDownExtensions.cs` (rewritten) | Migrated | At parity | `// [XPLAT]` `ShowKeypad(...)` no longer instantiates the deleted WinForms `FormNumeric`; it now launches the Avalonia `FormNumericView` via async `ShowDialog`, honoring min/max and returning the entered value. |

---

## AgIO — Classes (de-WinForms / cross-platform)

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/AgIO/Source/Classes/CGLM.cs` | `SourceCode/AgIO/Source/Classes/CGLM.cs` | Migrated | At parity | `// [XPLAT]` Removed `using System.Windows.Forms;`; math/`glm` utility is framework-agnostic for net8.0. The pre-existing lower-cased type name `glm` (mirroring the C++ OpenGL-Mathematics library) triggers analyzer `CS8981` under net8.0; because `Release` sets `TreatWarningsAsErrors`, a tightly-scoped `#pragma warning disable/restore CS8981` wraps the class (renaming the type was rejected as broad behavior-frozen call-site churn). Behavior unchanged. |
| `SourceCode/AgIO/Source/Classes/CRadioChannel.cs` | `SourceCode/AgIO/Source/Classes/CRadioChannel.cs` | Migrated | At parity | `// [XPLAT]` Provenance-tagged; no behavioral change, recompiled for net8.0+Avalonia. |
| `SourceCode/AgIO/Source/Classes/ListViewColumnSorterExt.cs` | `SourceCode/AgIO/Source/Classes/ListViewColumnSorterExt.cs` | Migrated | At parity | `// [XPLAT]` Removed `System.Windows.Forms` dependency; added a portable `ListSortState` enum replacing `System.Windows.Forms.SortOrder`; comparer is framework-agnostic and culture-invariant (`InvariantCulture`) for deterministic cross-OS ordering. |

---

## AgIO — Build / Configuration / Entry Point

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/AgIO/Source/AgIO.csproj` | `SourceCode/AgIO/Source/AgIO.csproj` | Migrated | At parity | `// [XPLAT]` `WinExe`+WinForms → `Exe` + Avalonia 11.3.18 stack (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `Avalonia.Diagnostics` (Debug)) + `System.IO.Ports` 9.0.0. Removed `UseWindowsForms`/`ImportWindowsDesktopTargets`, `WindowsBase`/`System.Configuration` refs, `System.Memory`/`System.Resources.Extensions`, stale `Compile Update` entries (`Controls.Designer.cs`, `NMEA.Designer.cs`, `SerialComm.Designer.cs`, `UDP.Designer.cs`) and the `Resources.resx` `EmbeddedResource`. Added `RuntimeIdentifiers` `win-x64;linux-x64;osx-x64;osx-arm64` and `ProjectReference`s to `AgLibrary`, `Keypad`, and `AgOpenGPS.Core`. |
| `SourceCode/AgIO/Source/Program.cs` | `SourceCode/AgIO/Source/Program.cs` | Migrated | At parity | `// [XPLAT]` `[STAThread]` + WinForms `Application.Run(new FormLoop())` → Avalonia `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`. Single-instance preserved with the **same named mutex GUID** `{8F6F0AC4-B9A1-55fd-A8CF-72F04E6BDE8F}`; `RegistrySettings.Load()` + culture setup (`CurrentCulture`/`CurrentUICulture` + `DefaultThreadCurrentCulture`/`DefaultThreadCurrentUICulture` for cross-OS determinism) preserved. `Restart()` made cross-platform (`ReleaseMutex` → `Process.Start(MainModule.FileName)` → `desktop.Shutdown()`). |
| `SourceCode/AgIO/Source/Properties/RegistrySettings.cs` | `SourceCode/AgIO/Source/Properties/RegistrySettings.cs` | Migrated | At parity | `// [XPLAT]` Windows-Registry backing replaced with a cross-platform XML store at `<ApplicationData>/AgOpenGPS/registry.xml` (`Environment.SpecialFolder.ApplicationData`). Settings **schema/keys preserved**; only the backing store and path source change (AAP §0.5 G5). Enables AgIO to build/run on Linux/macOS. |
| `SourceCode/AgIO/Source/App.config` | `(removed)` | Deleted | N/A | `net48` startup section removed; unused by SDK-style net8.0 builds and a case-sensitive-filesystem hazard (AAP §0.7/G7). |
| `SourceCode/AgIO/Source/Properties/Resources.resx`<br/>`SourceCode/AgIO/Source/Properties/Resources.Designer.cs` | `(deferred — see notes)` | Deleted | Deferred | `// [XPLAT]` The WinForms strongly-typed image-resource pair was removed to unblock the de-Windows-ified build (the generated designer pulled `System.Drawing.Bitmap`/WinForms resource types). The button-icon bitmaps are recoverable from baseline `f549b00f` and will be re-introduced as Avalonia `avares://` assets at the AgIO UI-polish checkpoint. The current `MainWindow.axaml` uses colored `Border` status indicators and does not depend on these generated accessors. |

---

## GPS — Rendering Host Retirement (AAP G3)

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/WinForms/GeoViewport.cs` | `SourceCode/GPS/Controls/AvaloniaGeoViewport.cs` *(deferred — CP9)* | Reimplemented | Deferred | `// [XPLAT]` The WinForms `OpenTK.GLControl`-hosted GL viewport (`GeoViewport : GeoViewportBase`, overriding `MakeCurrent`/`ViewportSize`/`EndPaint`+`SwapBuffers`) is **retired** in CP2. Its cross-platform replacement, `AvaloniaGeoViewport : GeoViewportBase` over Avalonia `OpenGlControlBase`, is scheduled for **CP9 (Rendering re-host)** per AAP §0.3.1/§0.6.2. The Core `GeoViewportBase` abstraction (`AgOpenGPS.Core/Drawing/`) is **unchanged**, so only the host adapter changes — the DrawLib/`GLW` drawing code is insulated from the host swap. The dominant open feasibility risk (immediate-mode GL vs GLES/ANGLE; `glReadPixels` back-buffer scan) is tracked in `PARITY_REPORT.md`. Retained GPS WinForms forms that still reference the old viewport are migrated at the GPS UI checkpoints (CP3/CP4) before final removal becomes build-breaking. |
| `SourceCode/AgOpenGPS.Core/Drawing/GeoViewportBase.cs` | `SourceCode/AgOpenGPS.Core/Drawing/GeoViewportBase.cs` | Unchanged | At parity | Reference abstraction; not modified by the migration. Both the WinForms host (removed) and the future Avalonia host (CP9) implement it. |

---

## GPS — Low-Level / Behavior-Frozen (CP2)

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Classes/CSmartWAS.cs` | `SourceCode/GPS/Classes/CSmartWAS.cs` | Migrated | At parity | `// [XPLAT]` Constructor migrated from `CSmartWAS(FormGPS)` to `CSmartWAS(ApplicationModel)`; `AddSample()` reads gating state (`isBtnAutoSteerOn`, `avgSpeed`, `guidanceLineDistanceOff`) from the shared Core `ApplicationModel`. **CP2 data-flow fix:** the live state is now synchronized into `ApplicationModel` immediately before each `AddSample()` call (see `Position.designer.cs` row) so sample collection is no longer disabled by default `false`/`0` values. Sampling thresholds (`MIN_SPEED_KMH`, `MAX_ANGLE_DEG`, `MAX_DIST_OFF_MM`, `MAX_SAMPLES`, `MIN_SAMPLES`) unchanged; verified by a CP2 ad-hoc parity test compiling the real `CSmartWAS.cs` (7/7). |
| `SourceCode/AgOpenGPS.Core/Models/ApplicationModel.cs` | `SourceCode/AgOpenGPS.Core/Models/ApplicationModel.cs` | Migrated | At parity | `// [XPLAT]` Added Smart WAS gating fields (`isBtnAutoSteerOn`, `avgSpeed`, `guidanceLineDistanceOff`) consumed by `CSmartWAS`. Written by the live-state sync in `Position.designer.cs`. |
| `SourceCode/GPS/Forms/Position.designer.cs` | `SourceCode/GPS/Services/PositionService.cs` `(deferred — CP9)` | Extracted | Deferred | `// [XPLAT]` **Corrected at CP3: this file was DELETED in CP3.** It was previously recorded here as a `Migrated`, behavior-verified self-mapping (a non-deferred parity status), documenting the **CP2** three-field Smart WAS live-state sync (`bool`/`double`/`short` written into `ApplicationModel` immediately before `CSmartWAS.AddSample(...)`); that row is now **stale and superseded** because the file no longer exists. The `UpdateFixPosition()` scan loop **and** that CP2 Smart WAS sync are **Extracted** to the deferred `PositionService` — the sync **must be re-established there** or `CSmartWAS` sampling regresses to default `false`/`0` state. The authoritative CP3 mapping (with the full frozen-behavior contract) is the `Position.designer.cs` row under *GPS — FormGPS Partial Extraction Sources (CP3)* below. The lowercase `.designer.cs` case-hazard (G7) is resolved by the deletion. |
| `SourceCode/GPS/ResourcesBrands/BrandImages.resx` | `SourceCode/GPS/ResourcesBrands/BrandImages.resx` (restored) | Unchanged | At parity | `// [XPLAT]` **CP2 build-integrity fix:** the resource was restored byte-for-byte from baseline `f549b00f` (18,373 bytes) because its consumers remain in place this checkpoint — `SourceCode/GPS/AgOpenGPS.csproj` still embeds it and `SourceCode/GPS/Classes/Brands.cs` still calls the generated `BrandImages.*` accessors (48 accessors, all backed by resx entries; all 48 referenced PNGs present under `Brands/{Articulated,Brand,Harvester,Tractor}`). The GPS brand-image → Avalonia `avares://` asset migration is **deferred** to the GPS UI checkpoint (CP3/CP4/CP9), when `Brands.cs` and the csproj metadata are migrated together. |

---

## GPS — Controls

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Controls/NudlessNumericUpDownExtensions.cs` | `SourceCode/Keypad/NumKeypad.axaml.cs` *(GPS host view deferred — CP3/CP4)* | Deleted | Deferred | `// [XPLAT]` WinForms static extension (`ShowKeypad(this NudlessNumericUpDown, Form)`) that highlighted the control and opened the WinForms `FormNumeric`. Both `NudlessNumericUpDown` and WinForms `FormNumeric` cease to exist after Avalonia re-platforming. The reusable Avalonia numeric keypad primitive `SourceCode/Keypad/NumKeypad.axaml.cs` **exists**; the GPS-side numeric-entry host view (`GPS/Views/Inputs/...`) is **deferred** to the GPS UI checkpoint (the `GPS/Views` tree does not exist yet). References in retained GPS WinForms forms are migrated by the GPS view checkpoints. |
| `SourceCode/GPS/Controls/TextBoxExtensions.cs` | *(GPS host view deferred — CP3/CP4)* | Deleted | Deferred | `// [XPLAT]` WinForms text-entry helper removed; GPS Avalonia text-entry surfaces are introduced at the GPS UI checkpoint. |
| `SourceCode/GPS/Controls/DraggableControlExtension.cs` | `(removed)` | Deleted | N/A | `// [XPLAT]` WinForms draggable-control helper; no Avalonia equivalent required (Avalonia layout handles this natively). |

---

## GPS — WinForms Shell Retirement (CP3)

The GPS main kiosk shell and its non-visual composition partials were **removed** in CP3. Their
Avalonia replacement — `App.axaml(.cs)` plus `Views/MainView.axaml(.cs)` bound to the Core
view-models, with domain logic in extracted Services — is **Deferred** to the GPS UI checkpoints
(CP4–CP9). `SourceCode/GPS/Views/` does **not** exist yet, so these rows are recorded `Deferred`,
never "At parity" (accuracy contract). The `FormGPS` ("`mf`") god-object back-reference is replaced
by constructor-injected `ApplicationModel`/services + view-models (AAP §0.3.2 MVVM).

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Forms/FormGPS.cs` | `SourceCode/GPS/Views/MainView.axaml.cs` + `SourceCode/GPS/App.axaml.cs` `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Main shell/coordination god-object. Replaced by `MainView` view-model + a thin application controller; the `mf` back-reference held by GPS classes/dialogs becomes injected `ApplicationModel`/services. `Program.cs` `Application.Run(new FormGPS())` → Avalonia bootstrap (see Build Sequencing section). |
| `SourceCode/GPS/Forms/FormGPS.Designer.cs` | `SourceCode/GPS/Views/MainView.axaml` `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` Kiosk layout: central OpenGL field viewport + side/bottom command panels + live status readouts → Avalonia XAML, 1:1 parity (AAP §0.3.3). |
| `SourceCode/GPS/Forms/FormGPS.resx` | `SourceCode/GPS/Views/MainView.axaml` + `App.axaml` theme `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` Shell palette/day-night + embedded bitmaps → Avalonia Fluent theme + custom styles; image assets migrate to `avares://` at the UI-polish checkpoint. |
| `SourceCode/GPS/Forms/GUI.Designer.cs` | `SourceCode/GPS/Views/MainView.axaml(.cs)` composition `(deferred — CP4)` | Extracted | Deferred | `// [XPLAT]` Panel/text/settings/day-night/viewport shell logic → `MainView` composition + view-model bindings. Non-domain (UI/render) state stays in the view. |
| `SourceCode/GPS/Forms/Controls.Designer.cs` | `SourceCode/GPS/Views/MainView.axaml(.cs)` composition `(deferred — CP4)` | Extracted | Deferred | `// [XPLAT]` Command-button/menu/event-handler wiring → `MainView` + `RelayCommand` commands on the Core view-models. |

---

## GPS — FormGPS Partial Extraction Sources (CP3)

The real-time domain pipeline that lived inside `FormGPS` partial classes was **removed** in CP3; it
is **Extracted** into plain, constructor-injectable `SourceCode/GPS/Services/*` classes (AAP §0.6.1,
Extract Class / Move Method). Those service files **do not exist yet** (`SourceCode/GPS/Services/` is
absent) — each row is recorded `Deferred` with the **frozen-behavior contract** the destination
service must reproduce and the **parity test** that verifies it. State separation: domain state
(`pn.fix`, guidance lines, coverage, boundary) → services/Core; render/UI state (camera, GL matrices,
panel toggles) → the Avalonia view + `RenderCoordinator` (AAP §0.6.1).

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Forms/Position.designer.cs` | `SourceCode/GPS/Services/PositionService.cs` `(deferred — CP9)` | Extracted | Deferred | `// [XPLAT]` **Frozen:** `UpdateFixPosition()` scan loop; CAHRS heading/roll fusion; WGS84→local-plane conversion; boundary/contour/recorded-path capture; autosteer-safe state; 1000 ms RTK-recovery debounce (`RTK_RECOVER_DEBOUNCE_MS`); `CalculateSectionLookAhead`. **Plus the CP2 three-field Smart WAS live-state sync** (`bool`/`double`/`short` into `ApplicationModel` before `CSmartWAS.AddSample(...)`) — must be re-established here. Parity: `GuidanceEquivalenceTests` (tolerance `Is.LessThan(0.001)`). This is the authoritative row; the stale CP2 "GPS — Low-Level" row was corrected to point here. |
| `SourceCode/GPS/Forms/UDPComm.Designer.cs` | `SourceCode/GPS/Services/PgnDispatcher.cs` `(deferred — CP9)` | Extracted | Deferred | `// [XPLAT]` **Frozen:** async `System.Net.Sockets` UDP receive; inbound header validation `data[0]==0x80 && data[1]==0x81`; additive CRC `CK_A += data[j]` for `j=2..Length` with reject-on-mismatch; the **70 ms `udpWatchLimit` throttle**; loopback bind `127.0.0.1:15555` / peer `:17777`; outbound CRC writer `crc += byteData[i]`. No added receive→fuse→steer→section latency. Parity: `PgnFrameGoldenTests` + receive→fuse→steer latency timing. |
| `SourceCode/GPS/Forms/PGN.Designer.cs` | `SourceCode/GPS/Services/PgnDispatcher.cs` `(deferred — CP9)` | Extracted | Deferred | `// [XPLAT]` **Frozen:** the `CPGN_*` frame classes (`0xD0`/`0xFE`/`0xFD`/`0xFC`/`0xFB`…) with header `0x80 0x81 0x7F`, fixed length bytes, and CRC trailer; lat/lon `0x7FFFFFFF` angle encodings. Byte-for-byte (`docs/pgn-protocol.md`). Parity: `PgnFrameGoldenTests`. |
| `SourceCode/GPS/Forms/Sections.Designer.cs` | `SourceCode/GPS/Services/SectionService.cs` `(deferred — CP9)` | Extracted | Deferred | `// [XPLAT]` **Frozen:** section master manual/auto logic, zone handling, 1–16 unique / up to 64 same-width sections via PGN `0xE5`, machine byte PGN `0xEF`, and `isJobStarted` gating (AAP §0.2.2). |
| `SourceCode/GPS/Forms/SaveOpen.Designer.cs` | `SourceCode/GPS/Services/FieldIoService.cs` `(deferred — CP9)` | Extracted | Deferred | `// [XPLAT]` **Frozen:** field/ISOXML/KML/AgShare load/save/export; `Path.Combine(RegistrySettings.fieldsDirectory, …)`; `Directory.Exists`/`File.Exists` validation; `isJobStarted` critical/optional load gate; `TryLoadFromAgShareAsync`; all numeric I/O via `InvariantCulture` (AAP §0.6.5). Parity: `FieldRoundTripTests`, `IsoXmlEquivalenceTests`. |
| `SourceCode/GPS/Forms/OpenGL.Designer.cs` | `SourceCode/GPS/Services/RenderCoordinator.cs` + `SourceCode/GPS/Controls/AvaloniaGeoViewport.cs` `(deferred — CP9)` | Extracted | Deferred | `// [XPLAT]` **Frozen:** projection/frustum (`frustum[24]`, `CalcFrustum`) clipping-plane culling; back-buffer `glReadPixels` section/lookahead pixel scan; overlays. Host swap goes through `AvaloniaGeoViewport : GeoViewportBase` over `OpenGlControlBase` (Core DrawLib/`GLW` unchanged). The immediate-mode-GL-vs-GLES feasibility risk and the `glReadPixels` scan are tracked in `PARITY_REPORT.md` (AAP §0.6.2). |

---

## GPS — Security & Safety Control Destinations (CP3)

Per AAP §0.7.1 (R1/R2) and the CP3 review's security findings, every safety/security control removed
by the CP3 deletions is mapped to its destination here so it cannot be silently lost during the later
reimplementation, together with the golden/parity test that must verify it. All destinations are
`Deferred` until the corresponding Service/View exists (CP4–CP9).

| Control (deleted source) | Destination | Frozen behavior to preserve | Parity test |
|---|---|---|---|
| PGN inbound header + additive CRC (`UDPComm.Designer.cs` L35–47) | `PgnDispatcher` | `0x80 0x81` header; `CK_A` additive checksum; reject frame on CRC mismatch | `PgnFrameGoldenTests` |
| Loopback receive throttle (`UDPComm.Designer.cs` `udpWatchLimit=70`) | `PgnDispatcher` / `PositionService` | ≤70 ms per-fix; no added receive→fuse→steer→section latency | receive→fuse→steer latency test |
| Loopback endpoints (`UDPComm.Designer.cs`) | `PgnDispatcher` | bind `127.0.0.1:15555`; peer `:17777` | `PgnFrameGoldenTests` |
| PGN frame defs + CRC writers (`PGN.Designer.cs` L15–40, L226, L319) | `PgnDispatcher` | `CPGN_*` byte arrays + CRC trailer byte-for-byte | `PgnFrameGoldenTests` |
| Field path/file validation + job gate (`SaveOpen.Designer.cs` L32–69, L96–142, L978) | `FieldIoService` | `Path.Combine` + `Directory`/`File.Exists`; `isJobStarted` critical/optional load | `FieldRoundTripTests` |
| Filename regex sanitization (`FormInputDialog.cs` L32 `glm.fileRegex`) | input-dialog ViewModel | strip invalid filename characters before use | input-validation test |
| Hotkey character whitelist (`Form_Keys.cs` L46 `[^0-9a-zA-Z]`) | hotkey ViewModel | accept only alphanumeric shortcut characters | hotkey-validation test |
| AgShare download job-state gate (`FormAgShareDownloader.cs` L139 `isJobStarted`) | AgShare downloader View / `AgShareClient` | block cloud download while a field job is open | AgShare gating test |
| AgShare upload / duplicate / credentials (`FormAgShareUploader.cs` L43–59, L254–471) | AgShare uploader View / `AgShareClient` | `DuplicateNameChoice` Cancel/Overwrite; credential handling; `AgShareEnabled=false` default | AgShare gating test |
| Section-control safety (`Sections.Designer.cs`) | `SectionService` | `isJobStarted` gating; `0xE5`/`0xEF` section/machine semantics | section-state test |

---

## GPS — Root Dialogs (CP3)

The first batch of GPS root dialogs was **removed**; Avalonia View/ViewModel replacements are
**Deferred** (`GPS/Views/` absent). Dialogs that retained callers still reference are flagged
*(retained caller)* — those callers are migrated together with the View (see Build Sequencing
section). The G7 case-fix files (`FormYes.designer.cs`, `FormtimedMessage.resx`) are removed here.

| Original (`.cs` + `.Designer`/`.designer.cs` [+ `.resx`]) | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Forms/FormDialog.*` | `SourceCode/GPS/Views/FormDialogView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Shared alert/confirm (`Show`/`ShowQuestion`, `DialogSeverity`). **(retained callers after CP4)** `Program.cs`, `Properties/RegistrySettings.cs`. The dialog callers `Profiles/FormLoadProfile.cs` and `Profiles/FormLoadVehicleTool.cs` were **deleted in CP4** (resolved by deletion; replacements `FormLoadProfileView`/`FormLoadVehicleToolView` in *GPS — Profile Dialogs (CP4)*). |
| `SourceCode/GPS/Forms/FormAgShareSettings.*` | `SourceCode/GPS/Views/FormAgShareSettingsView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` Server/API-key settings, connection test, clipboard/paste. `AgShareEnabled=false` default preserved. |
| `SourceCode/GPS/Forms/FormEventViewer.*` | `SourceCode/GPS/Views/FormEventViewerView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` Event/log read + refresh. |
| `SourceCode/GPS/Forms/FormGPSData.*` | `SourceCode/GPS/Views/FormGPSDataView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` Live GPS telemetry readout; was `FormGPS`-coupled → binds to `ApplicationModel`. |
| `SourceCode/GPS/Forms/FormHelp.*` | `SourceCode/GPS/Views/FormHelpView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` External help/update links. |
| `SourceCode/GPS/Forms/FormInputDialog.*` (`.cs` + `.Designer.cs`, no `.resx`) | `SourceCode/GPS/Views/FormInputDialogView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` **Security:** filename regex sanitization (`glm.fileRegex`). **(caller resolved in CP4)** the former caller `Profiles/FormLoadVehicleTool.ShowInput` was **deleted in CP4**; now hosted by `FormLoadVehicleToolView` (*GPS — Profile Dialogs (CP4)*). |
| `SourceCode/GPS/Forms/FormPan.*` | `SourceCode/GPS/Views/FormPanView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` Camera pan; was `FormGPS`-coupled. |
| `SourceCode/GPS/Forms/FormSaving.*` | `SourceCode/GPS/Views/FormSavingView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` Saving step/progress status. |
| `SourceCode/GPS/Forms/FormShiftPos.*` | `SourceCode/GPS/Views/FormShiftPosView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` GPS drift/offset; was `FormGPS`-coupled. |
| `SourceCode/GPS/Forms/FormTermsAndConditions.*` | `SourceCode/GPS/Views/FormTermsAndConditionsView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` Terms acceptance flow + external license/help links + terms-text resource. |
| `SourceCode/GPS/Forms/FormTimedMessage.*` (`.cs` + `.Designer.cs`, no `.resx`) | `SourceCode/GPS/Views/FormTimedMessageView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Auto-dismissing timed popup. **(caller resolved in CP4)** the former caller `Profiles/FormNewProfile.cs` (`new FormTimedMessage(...)`) was **deleted in CP4**; now hosted by `FormNewProfileView` (*GPS — Profile Dialogs (CP4)*). |
| `SourceCode/GPS/Forms/FormWebCam.*` | `SourceCode/GPS/Views/FormWebCamView` **or feature-gated** `(deferred — CP4)` | Feature-gated | Deferred | `// [XPLAT]` Optional webcam (F-045); `isWebCamOn=false` default; Accord/DirectShow is Windows-only — gate off-Windows (AAP §0.6.3). |
| `SourceCode/GPS/Forms/FormYes.cs` + `FormYes.designer.cs` + `FormYes.resx` | `SourceCode/GPS/Views/FormYesView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` Yes/No confirmation. **G7:** lowercase `.designer.cs` case-hazard resolved by deletion. |
| `SourceCode/GPS/Forms/Form_Keys.*` | `SourceCode/GPS/Views/FormKeysView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` **Security:** hotkey character whitelist (`[^0-9a-zA-Z]`); shortcut settings update. |
| `SourceCode/GPS/Forms/FormtimedMessage.resx` (stray, old-cased) | `(removed)` | Deleted | N/A | `// [XPLAT]` **G7:** mis-cased `.resx` (vs `FormTimedMessage.cs`) eliminated; no separate replacement (covered by `FormTimedMessageView`). |

---

## GPS — Config Controls (CP3)

| Original (`.cs` + `.Designer.cs` + `.resx`) | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Forms/Config/ConfigSummaryControl.*` | `SourceCode/GPS/Views/Config/ConfigSummaryView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Config-summary `UserControl` → Avalonia `UserControl`. **(reference resolved in CP4)** the former declaring/instantiating file `Forms/Settings/FormConfig.Designer.cs` was **deleted in CP4**; this control is now composed by `FormConfigView` (see *GPS — Settings Dialogs (CP4)*). |
| `SourceCode/GPS/Forms/Config/ConfigVehicleControl.*` | `SourceCode/GPS/Views/Config/ConfigVehicleView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Vehicle-config `UserControl` → Avalonia `UserControl`. **(reference resolved in CP4)** `Forms/Settings/FormConfig.Designer.cs` deleted in CP4; now composed by `FormConfigView`. |

---

## GPS — Pickers (CP3)

| Original (`.cs` + `.Designer.cs` + `.resx`) | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Forms/Pickers/FormColorPicker.*` | Avalonia `ColorPicker` hosted in `SourceCode/GPS/Views/Pickers/` `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Replaces the `MechanikaDesign.WinForms.UI.ColorPicker` dialog (AAP §0.5). **(callers resolved in CP4)** former callers `Forms/Settings/FormColor.cs`, `FormColorSection.cs` were **deleted in CP4**; the `ColorPicker` is now hosted by `FormColorView`/`FormColorSectionView` (see *GPS — Settings Dialogs (CP4)*). |
| `SourceCode/GPS/Forms/Pickers/FormDrivePicker.*` | `SourceCode/GPS/Views/Pickers/FormDrivePickerView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Field selection by distance/path. **(caller resolved in CP4)** former caller `Forms/Field/FormJob.cs` deleted in CP4; now hosted by `FormJobView` (see *GPS — Field Dialogs — Part 2 (CP4)*). |
| `SourceCode/GPS/Forms/Pickers/FormFilePicker.*` | `SourceCode/GPS/Views/Pickers/FormFilePickerView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` **Security:** field list + area calculation + `InvariantCulture` file parsing. **(caller resolved in CP4)** former caller `Forms/Field/FormJob.cs` deleted in CP4; now hosted by `FormJobView`. |
| `SourceCode/GPS/Forms/Pickers/FormRecordPicker.*` | `SourceCode/GPS/Views/Pickers/FormRecordPickerView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` Recorded-path file copy/load/delete. |

---

## GPS — Field Dialogs — Part 1 (CP3)

| Original (`.cs` + `.Designer.cs` + `.resx`) | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Forms/Field/FormAgShareDownloader.*` | `SourceCode/GPS/Views/Field/FormAgShareDownloaderView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Cloud field preview/download; **job-state gate** (`if (gps.isJobStarted)`). **(caller resolved in CP4)** former caller `Forms/Field/FormJob.cs` deleted in CP4; now hosted by `FormJobView` (*GPS — Field Dialogs — Part 2 (CP4)*). |
| `SourceCode/GPS/Forms/Field/FormAgShareUploader.*` | `SourceCode/GPS/Views/Field/FormAgShareUploaderView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Cloud upload; `DuplicateNameChoice` Cancel/Overwrite; boundary/field-file collection; credential flow. **(caller resolved in CP4)** former caller `Forms/Field/FormJob.cs` (`new FormAgShareUploader(mf.agShareClient)`) deleted in CP4; now hosted by `FormJobView`. |
| `SourceCode/GPS/Forms/Field/FormBndTool.*` | `SourceCode/GPS/Views/Field/FormBndToolView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` Boundary smoothing/reduction/build tool. |
| `SourceCode/GPS/Forms/Field/FormBoundary.*` | `SourceCode/GPS/Views/Field/FormBoundaryView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` Boundary add/delete/import + KML file logic. |
| `SourceCode/GPS/Forms/Field/FormBoundaryPlayer.*` | `SourceCode/GPS/Views/Field/FormBoundaryPlayerView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` Boundary record/playback + section-on gate. |
| `SourceCode/GPS/Forms/Field/FormBuildBoundaryFromTracks.*` | `SourceCode/GPS/Views/Field/FormBuildBoundaryFromTracksView.axaml(.cs)` + ViewModel `(deferred — CP4)` | Reimplemented | Deferred | `// [XPLAT]` Track selection + OpenGL preview + save validation + boundary generation. |
| `SourceCode/GPS/Forms/Field/FormMap.*` | cross-platform map control **or feature-gated** in `SourceCode/GPS/Views/Field/` `(deferred — CP4)` | Feature-gated | Deferred | `// [XPLAT]` GMap-based boundary/map editing (F-021); online imagery optional, tile cache (SQLite) is cross-platform; field still renders without it (AAP §0.6.3). |

> **Note (CP4):** `FormSaveOrNot.*` was deleted in **CP4** (Field Part 2), not CP3; its direct
> replacement row has been **consolidated** into *GPS — Dialog Catalog Part 2 (CP4) → Field Dialogs
> Part 2* below so that all eleven CP4 Field dialogs are recorded together and correctly attributed.

---

## GPS — Dialog Catalog Part 2 (CP4)

The **second and final** batch of GPS WinForms dialogs was **removed** in CP4, completing the
retirement of the entire legacy GPS Windows Forms UI catalog (**128 files / 47 dialog-config
groups** under `SourceCode/GPS/Forms/{Field,Guidance,Inputs,Profiles,Settings}/`). Avalonia
View/ViewModel replacements are **Deferred** — `SourceCode/GPS/Views/` and `SourceCode/GPS/Services/`
do **not** exist yet, so every row below is recorded `Deferred` with its named target file and
frozen-behavior contract, **never** "At parity" (accuracy contract). The `FormGPS` (`mf`) god-object
back-reference held by these dialogs becomes constructor-injected `ApplicationModel`/services +
view-models (AAP §0.3.2 MVVM). All destination views consume the existing `GPS/App.axaml` Fluent
theme and the `Aog*` day/night brush resources (see *UI / Design / Accessibility Parity* below).

> **Retained-caller reconciliation (CP4).** Several CP3 rows above annotated `(retained caller)`
> targets that were themselves deleted in CP4 — `Forms/Field/FormJob.cs`,
> `Forms/Settings/FormColor.cs`, `Forms/Settings/FormColorSection.cs`,
> `Forms/Settings/FormConfig.Designer.cs`, `Forms/Profiles/FormLoadProfile.cs`,
> `Forms/Profiles/FormLoadVehicleTool.cs`, and `Forms/Profiles/FormNewProfile.cs`. Those callers no
> longer exist; their dangling references to deleted CP3 types are therefore **resolved by deletion**
> at CP4, and each now has its own direct replacement row in this catalog. The *Retained references
> to deleted CP3 types* table further below has been updated to reflect this.

### GPS — Field Dialogs — Part 2 (CP4)

| Original (`.cs` + `.Designer.cs` [+ `.resx`]) | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Forms/Field/FormCopyTracks.*` | `SourceCode/GPS/Views/Field/FormCopyTracksView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Copy/duplicate guidance tracks between fields (source-field list, multi-select, copy to current field). **Frozen:** track-file enumeration under the field directory and `InvariantCulture` path/area parsing; overwrite handling preserved. |
| `SourceCode/GPS/Forms/Field/FormEasyDrive.*` (`.cs` + `.Designer.cs`, no `.resx`) | `SourceCode/GPS/Views/Field/FormEasyDriveView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` On-screen "easy drive" steering/joystick overlay (manual nudge of the simulated/auto path). Was `FormGPS`-coupled → binds to `ApplicationModel`/`PositionService`. No `.resx` in the CP4 diff. |
| `SourceCode/GPS/Forms/Field/FormEnterFlag.*` | `SourceCode/GPS/Views/Field/FormEnterFlagView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Flag add/import/save. **Frozen safety/validation:** `FlagsFiles.DeduplicateFlags(mf.flagPts)` dedup; lat/lon line-format guard on import raising `"Invalid line: {line}"` and aborting; success/error dialogs; file read/write error handling. `double` parse via `InvariantCulture` for file I/O (AAP §0.6.5). |
| `SourceCode/GPS/Forms/Field/FormFieldData.*` | `SourceCode/GPS/Views/Field/FormFieldDataView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Read-only field statistics readout (area worked/remaining, boundary area, overlap). Binds to `CFieldData`/`ApplicationModel`; numeric formatting via `InvariantCulture`. |
| `SourceCode/GPS/Forms/Field/FormFieldDir.*` | `SourceCode/GPS/Views/Field/FormFieldDirView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Field-directory chooser/rename. **Frozen:** `Directory.Exists` guards, filename sanitization, and cross-platform path handling (`Path.Combine`, no hard-coded `\`). |
| `SourceCode/GPS/Forms/Field/FormFieldExisting.*` | `SourceCode/GPS/Views/Field/FormFieldExistingView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Open an existing field (list + select + open). **Frozen:** field-list enumeration, `File.Exists` guard, and the `isJobStarted` close-gate before switching fields. |
| `SourceCode/GPS/Forms/Field/FormFieldISOXML.*` | `SourceCode/GPS/Views/Field/FormFieldISOXMLView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` ISOXML field import/export dialog. **Frozen contract (behavior-frozen, AAP §0.2.2):** ISOXML V3/V4 semantics, name ≤248 bytes, AB+Curve export limit; delegates to the (deferred) `FieldIoService`. Parity: `IsoXmlEquivalenceTests`. |
| `SourceCode/GPS/Forms/Field/FormFieldKML.*` | `SourceCode/GPS/Views/Field/FormFieldKMLView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` KML boundary import. **Frozen:** KML parse + `InvariantCulture` lat/lon parsing; `File.Exists` guard. |
| `SourceCode/GPS/Forms/Field/FormFlags.*` | `SourceCode/GPS/Views/Field/FormFlagsView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Flag list/edit/delete/goto. **Frozen:** flag-collection CRUD against `mf.flagPts`, dedup on save, and delete-all confirmation. |
| `SourceCode/GPS/Forms/Field/FormJob.*` | `SourceCode/GPS/Views/Field/FormJobView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` New / resume / drive-to / AgShare field-job entry hub. **Frozen:** `isJobStarted` gating; hosts the (deferred) `FormFilePickerView`/`FormDrivePickerView`/`FormAgShareDownloaderView`/`FormAgShareUploaderView` (the CP3 Pickers/Field-Part-1 rows that named `FormJob.cs` as their `(retained caller)` — that caller is now this Avalonia view). |
| `SourceCode/GPS/Forms/Field/FormSaveOrNot.*` | `SourceCode/GPS/Views/Field/FormSaveOrNotView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` **Consolidated from the former CP3-Field-Part-1 placement.** Save-or-not confirmation (exit to desktop / shutdown / cancel) with auto-countdown timers. **Frozen:** timer-driven auto-exit (`countExit`=4 / `countShutdown`=5), `setWindow_isShutdownComputer` setting, `DialogResult.OK`/`Yes`/`Ignore` → Avalonia close-result semantics, and the `AgShareEnabled` snapshot trigger on job-started. |

### GPS — Guidance Dialogs (CP4)

| Original (`.cs` + `.Designer`/`.designer.cs` + `.resx`) | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Forms/Guidance/FormABDraw.*` | `SourceCode/GPS/Views/Guidance/FormABDrawView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` AB-line draw-on-field tool (point capture + OpenGL preview). **Frozen geometry:** AB/curve point capture, `glm.Distance`-based densification, and exact track-array writes; render preview rehosts on `AvaloniaGeoViewport` (CP9). Parity: `GuidanceEquivalenceTests`. |
| `SourceCode/GPS/Forms/Guidance/FormBuildTracks.*` | `SourceCode/GPS/Views/Guidance/FormBuildTracksView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Build/manage the track set (list, add, delete, reorder, set reference). **Frozen:** track CRUD against `CTrack`; behavior frozen. |
| `SourceCode/GPS/Forms/Guidance/FormGrid.*` | `SourceCode/GPS/Views/Guidance/FormGridView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Grid/origin guidance configuration. **Frozen:** grid origin + spacing, `InvariantCulture` numeric entry. |
| `SourceCode/GPS/Forms/Guidance/FormHeadAche.*` | `SourceCode/GPS/Views/Guidance/FormHeadAcheView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Headland/clip-line builder. **Frozen safety/validation:** start-point ≠ end-point guard raising `"Line Error" / "Start Point = End Point"` and aborting; clip-line geometry (`glm.Distance` densification, slice loops) preserved exactly. Parity: `GuidanceEquivalenceTests`. |
| `SourceCode/GPS/Forms/Guidance/FormHeadLine.*` | `SourceCode/GPS/Views/Guidance/FormHeadLineView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Headland-line builder/mover. **Frozen safety/validation:** `distance == 0 && curve` guard raising `"Distance Error" / "Distance Set to 0, Nothing to Move"` and aborting; point-distance-too-big guard; headland geometry frozen. Parity: `GuidanceEquivalenceTests`. |
| `SourceCode/GPS/Forms/Guidance/FormNudge.*` (`.cs` + lowercase `.designer.cs` + `.resx`) | `SourceCode/GPS/Views/Guidance/FormNudgeView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Nudge the active guidance line (step left/right, snap). **Frozen:** nudge-distance step + snap value applied to `guidanceLineDistanceOff`. **G7:** the lowercase `.designer.cs` case-hazard is resolved by deletion. |
| `SourceCode/GPS/Forms/Guidance/FormQuickAB.*` | `SourceCode/GPS/Views/Guidance/FormQuickABView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Quick AB-line creation (set A, set B, create). **Frozen:** AB heading computation and track creation. |
| `SourceCode/GPS/Forms/Guidance/FormRecordName.*` | `SourceCode/GPS/Views/Guidance/FormRecordNameView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Recorded-path naming. **Frozen:** filename sanitization (`glm.fileRegex`) and duplicate handling before save. |
| `SourceCode/GPS/Forms/Guidance/FormRefNudge.*` (`.cs` + lowercase `.designer.cs` + `.resx`) | `SourceCode/GPS/Views/Guidance/FormRefNudgeView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Reference-line nudge (move the reference AB/curve). **Frozen:** reference-offset step. **G7:** lowercase `.designer.cs` case-hazard resolved by deletion. |
| `SourceCode/GPS/Forms/Guidance/FormSmoothAB.*` | `SourceCode/GPS/Views/Guidance/FormSmoothABView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` AB-curve smoothing. **Frozen:** smoothing window/iteration math on the curve point list; behavior frozen. Parity: `GuidanceEquivalenceTests`. |
| `SourceCode/GPS/Forms/Guidance/FormTram.*` | `SourceCode/GPS/Views/Guidance/FormTramView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Tramline UI (pattern, passes, on/off). **Frozen:** tram pattern + pass-count applied to `CTram`. |
| `SourceCode/GPS/Forms/Guidance/FormTramLine.*` | `SourceCode/GPS/Views/Guidance/FormTramLineView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Tramline geometry configuration. **Frozen safety/validation:** track-spacing/width validation and tram offset; invalid-spacing guard preserved. |

### GPS — Input Dialogs (CP4)

| Original (`.cs` + `.Designer.cs` + `.resx`) | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Forms/Inputs/FormKeyboard.*` | `SourceCode/GPS/Views/Inputs/FormKeyboardView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)`, hosting `SourceCode/Keypad/Keyboard.axaml` | Reimplemented | Deferred | `// [XPLAT]` GPS on-screen keyboard for touch text entry, hosting the shared Avalonia `Keypad.Keyboard` `UserControl` (already migrated). Distinct from the AgIO `FormKeyboardView` (the AgIO keyboard rows do **not** cover this GPS dialog). Touch-friendly large keys preserved 1:1; OK/Cancel close-result. |
| `SourceCode/GPS/Forms/Inputs/FormNumeric.*` | `SourceCode/GPS/Views/Inputs/FormNumericView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)`, hosting `SourceCode/Keypad/NumKeypad.axaml` | Reimplemented | Deferred | `// [XPLAT]` GPS on-screen numeric keypad. **Frozen safety/validation:** `readonly` `min`/`max`; out-of-range highlighting (value `< min` → min label red, `> max` → max label red) with clamp; decimal/sign/backspace/clear entry. The reusable `Keypad.NumKeypad` primitive already exists; this GPS host view is deferred. File/protocol numeric I/O uses `InvariantCulture` (AAP §0.6.5). |

### GPS — Profile Dialogs (CP4)

| Original (`.cs` + `.Designer.cs` + `.resx`) | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Forms/Profiles/FormConvertProfiles.*` | `SourceCode/GPS/Views/Profiles/FormConvertProfilesView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Convert legacy profiles → split Vehicle/Tool profiles. **Frozen safety/validation:** filename sanitization via `glm.fileRegex`; **duplicate-name block** (`File.Exists` → `"…Already Exists"` error, abort); convert-enable gate = `hasSelection && vehicleValid && toolValid && atLeastOne`. Round-trip must honor the frozen split settings XML schema (AAP §0.2.2). |
| `SourceCode/GPS/Forms/Profiles/FormLoadProfile.*` | `SourceCode/GPS/Views/Profiles/FormLoadProfileView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Load a saved profile. **Frozen:** selection-count guard; user-facing warnings routed through the (deferred) `FormDialogView` (this view is the former `(retained caller)` for `FormDialog` recorded in the CP3 Root-Dialogs row). |
| `SourceCode/GPS/Forms/Profiles/FormLoadVehicleTool.*` | `SourceCode/GPS/Views/Profiles/FormLoadVehicleToolView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Load vehicle + tool profiles. **Frozen safety/validation:** filename sanitization; **job-state guard** (block while `isJobStarted`); duplicate handling; `ShowInput` filename prompt routed through the (deferred) `FormDialogView`/`FormInputDialogView` (former `(retained caller)`). |
| `SourceCode/GPS/Forms/Profiles/FormNewProfile.*` | `SourceCode/GPS/Views/Profiles/FormNewProfileView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Create a new profile. **Frozen safety/validation:** `InvalidFileRegex` (strips Windows-invalid filename chars `< > : " / \ ? *` and the pipe) **and** `glm.fileRegex` sanitization; **`isJobStarted` close-gate** (timed `"close field first"` message); `File.Exists` → overwrite confirmation; `Directory.Exists(environmentDirectory)` guard. The timed message uses the (deferred) `FormTimedMessageView` (former `(retained caller)`). |

### GPS — Settings Dialogs (CP4)

| Original (`.cs` + `.Designer.cs` + `.resx`) | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Forms/Settings/FormAllSettings.*` | `SourceCode/GPS/Views/Settings/FormAllSettingsView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Searchable all-settings grid (read/reset every persisted setting). **Frozen:** settings enumeration + reset; bound to the cross-platform settings store (schema frozen, AAP §0.2.2). |
| `SourceCode/GPS/Forms/Settings/FormButtonsRightPanel.*` | `SourceCode/GPS/Views/Settings/FormButtonsRightPanelView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` **Last legacy WinForms dialog deleted (CP4 final).** Right-panel button picker/arranger. **Frozen safety/validation:** minimum button-count guard — `if (mf.buttonOrder.Count < 2)` → `"Button Error" / "Not Enough Buttons Added"`, log, and abort save; `setDisplay_buttonOrder` persistence (default `"0,1,2,3,4,5,6,7"`); `ScreenHelper.IsOnScreen` bounds restore. |
| `SourceCode/GPS/Forms/Settings/FormColor.*` | Avalonia `ColorPicker` hosted in `SourceCode/GPS/Views/Settings/FormColorView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Day/night + field/section color settings (replaces the `MechanikaDesign.WinForms.UI.ColorPicker` dialog). **Frozen:** 16 custom colors parsed with `int.Parse(…, InvariantCulture)` + ARGB packing; the product default ARGB values are already mapped to the `App.axaml` `Aog*` color/brush keys. |
| `SourceCode/GPS/Forms/Settings/FormColorSection.*` | `SourceCode/GPS/Views/Settings/FormColorSectionView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Per-section color assignment. **Frozen:** section-color list + `InvariantCulture` ARGB parse; hosts the Avalonia `ColorPicker`. |
| `SourceCode/GPS/Forms/Settings/FormConfig.*` | `SourceCode/GPS/Views/Settings/FormConfigView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` The settings/config **shell** that hosted the six `Config*` tab panels and the `ConfigSummaryControl`/`ConfigVehicleControl` user controls. Reimplemented as the Avalonia config shell composing the `Config*View` panels (see *Config Controls — Part 2* below) + the CP3 `ConfigSummaryView`/`ConfigVehicleView`. Resolves the CP3 `(retained reference)` notes for those controls (the referencing `FormConfig.Designer.cs` is now deleted). |
| `SourceCode/GPS/Forms/Settings/FormCorrection.*` | `SourceCode/GPS/Views/Settings/FormCorrectionView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` GPS correction-source / heading-fusion settings. **Frozen:** correction-source selection + thresholds; any live readout chart element shares the cross-platform charting work (see *Charting Dialogs* below). |
| `SourceCode/GPS/Forms/Settings/FormSimCoords.*` | `SourceCode/GPS/Views/Settings/FormSimCoordsView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Simulator start-coordinate settings (lat/lon entry for `CSim`). **Frozen:** `InvariantCulture` lat/lon parse; feeds the in-app simulator (cross-OS integration without hardware, AAP §0.6.4). |
| `SourceCode/GPS/Forms/Settings/FormSteer.*` | `SourceCode/GPS/Views/Settings/FormSteerView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Steering settings. **Frozen safety bounds (AAP §0.2.2):** `setVehicle_maxSteerAngle` capped (`maxSteerAngle=30°`), `maxAngularVelocity=0.64°/s`, `setAS_minSteerPWM`/`maxSteerPWM`, `maxPulseCounts`, min/max steer speed, snap distance (cm↔inch ×2.54), WAS offset; writes the steer-config PGN (`p_252`) byte-for-byte. Parity: `GuidanceEquivalenceTests` + `PgnFrameGoldenTests`. |
| `SourceCode/GPS/Forms/Settings/FormSteerWiz.*` | `SourceCode/GPS/Views/Settings/FormSteerWizView.axaml(.cs)` + ViewModel `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Steering calibration wizard (multi-step). **Frozen safety bounds:** same steering limits as `FormSteer` plus WAS-offset/zero calibration and `maxPulseCounts` sensor calibration; steer-config PGN (`p_252`) writes preserved. Parity: `GuidanceEquivalenceTests` + `PgnFrameGoldenTests`. |

### GPS — Config Controls — Part 2 (CP4)

These six `Config*.Designer.cs` files were **partial-class designer panels of `FormConfig`** (the config
tab pages, `DependentUpon FormConfig.cs` in the GPS `csproj`). They are reimplemented as Avalonia config
panels composed by `FormConfigView` (above). The matching stale `<Compile Update>` `csproj` entries are
removed in CP4 (see `CHANGELOG.md` Build/CI).

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Forms/Settings/ConfigData.Designer.cs` | `SourceCode/GPS/Views/Config/ConfigDataView.axaml(.cs)` `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Config "Data/Sources" panel → Avalonia `UserControl` hosted in `FormConfigView`. |
| `SourceCode/GPS/Forms/Settings/ConfigHelp.Designer.cs` | `SourceCode/GPS/Views/Config/ConfigHelpView.axaml(.cs)` `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Config "Help" panel → Avalonia `UserControl`. |
| `SourceCode/GPS/Forms/Settings/ConfigMenu.Designer.cs` | `SourceCode/GPS/Views/Config/ConfigMenuView.axaml(.cs)` `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Config menu/navigation panel → Avalonia `UserControl`. |
| `SourceCode/GPS/Forms/Settings/ConfigModule.Designer.cs` | `SourceCode/GPS/Views/Config/ConfigModuleView.axaml(.cs)` `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Config "Module" (hardware module) panel → Avalonia `UserControl`. |
| `SourceCode/GPS/Forms/Settings/ConfigTool.Designer.cs` | `SourceCode/GPS/Views/Config/ConfigToolView.axaml(.cs)` `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Config "Tool/Implement" panel → Avalonia `UserControl`; tool-width/section settings (frozen). |
| `SourceCode/GPS/Forms/Settings/ConfigVehicle.Designer.cs` | `SourceCode/GPS/Views/Config/ConfigVehicleView.axaml(.cs)` `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Config "Vehicle" panel → Avalonia `UserControl`; vehicle dimensions/antenna offsets (frozen). |

### GPS — Charting Dialogs (CP4)

The three live-graph dialogs used the **Windows-only** `System.Windows.Forms.DataVisualization`
charting control. They are reimplemented with a **cross-platform** charting surface — a custom Avalonia
drawing control (or a selected cross-platform chart package) — preserving series, axes, zoom/autoscale,
and the retained rolling-data buffer behavior (AAP §0.5.1, R10). The Windows-only
`System.Windows.Forms.DataVisualization` `<Reference>` is **removed** from `SourceCode/GPS/AgOpenGPS.csproj`
in CP4 (no consumer remains after these deletions); see `CHANGELOG.md` Build/CI.

| Original (`.cs` + `.Designer.cs` + `.resx`) | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Forms/Settings/FormGraphSteer.*` | `SourceCode/GPS/Views/Settings/FormGraphSteerView.axaml(.cs)` (custom Avalonia chart) `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Live steer-angle graph (set-vs-actual steer angle, PWM). **Frozen:** series definitions, axis ranges/labels, autoscale/zoom, and the rolling sample buffer; replaces `DataVisualization.Chart` with cross-platform drawing. Data source unchanged (steer telemetry). |
| `SourceCode/GPS/Forms/Settings/FormGraphXTE.*` | `SourceCode/GPS/Views/Settings/FormGraphXTEView.axaml(.cs)` (custom Avalonia chart) `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Live cross-track-error (XTE) graph. **Frozen:** XTE series, axis scaling, autoscale/zoom, rolling buffer; cross-platform chart replacement. |
| `SourceCode/GPS/Forms/Settings/FormGraphHeading.*` | `SourceCode/GPS/Views/Settings/FormGraphHeadingView.axaml(.cs)` (custom Avalonia chart) `(deferred — CP4/CP9)` | Reimplemented | Deferred | `// [XPLAT]` Live heading/IMU graph (heading sources, fusion). **Frozen:** heading series, axis scaling, autoscale/zoom, rolling buffer; cross-platform chart replacement. |

---

## GPS — Security & Safety Control Destinations (CP4)

Per AAP §0.7.1 (R1) and the CP4 review's safety findings, every validation/safety control removed by the
CP4 deletions is mapped here to its destination so it cannot be silently lost during the later Avalonia
reimplementation, together with the golden/parity test that must verify it. All destinations are
`Deferred` until the corresponding View/ViewModel exists (CP4–CP9).

| Control (deleted source) | Destination | Frozen behavior to preserve | Parity test |
|---|---|---|---|
| Minimum button-count (`Settings/FormButtonsRightPanel.cs` `buttonOrder.Count < 2`) | `FormButtonsRightPanelView` ViewModel | Block save + show `"Not Enough Buttons Added"` when fewer than 2 buttons added; persist `setDisplay_buttonOrder` | button-picker validation test |
| Numeric min/max/clamp (`Inputs/FormNumeric.cs`) | `FormNumericView` ViewModel | `value < min`/`value > max` highlight + clamp; decimal/sign/backspace/clear; `InvariantCulture` for file/protocol I/O | numeric-entry validation test |
| Steering safety bounds (`Settings/FormSteer.cs`, `FormSteerWiz.cs`) | `FormSteerView` / `FormSteerWizView` ViewModels | `maxSteerAngle=30°`, `maxAngularVelocity=0.64°/s`, min/max steer PWM, `maxPulseCounts`, min/max speed, snap distance; steer-config PGN `p_252` byte-for-byte | `GuidanceEquivalenceTests` + `PgnFrameGoldenTests` |
| Profile filename sanitization + duplicate + job-state (`Profiles/FormNewProfile.cs`, `FormConvertProfiles.cs`, `FormLoadVehicleTool.cs`) | Profile ViewModels | `InvalidFileRegex` (`< > : " / \ ? *` + pipe) and `glm.fileRegex` strip; `File.Exists` → overwrite/duplicate block; `isJobStarted` close-gate; `Directory.Exists` guard | profile-validation test |
| Guidance invalid-line / distance guards (`Guidance/FormHeadAche.cs`, `FormHeadLine.cs`, `FormTramLine.cs`) | Guidance ViewModels | `"Start Point = End Point"` abort; `distance == 0 && curve` → `"Nothing to Move"` abort; point-distance-too-big guard; tram spacing validation | guidance-validation test + `GuidanceEquivalenceTests` |
| Flag-entry format guard (`Field/FormEnterFlag.cs`) | `FormEnterFlagView` ViewModel | `FlagsFiles.DeduplicateFlags`; `"Invalid line: {line}"` lat/lon format guard on import + abort; file read/write error handling | flag-import validation test |
| Color parse (`Settings/FormColor.cs`, `FormColorSection.cs`) | `FormColorView` / `FormColorSectionView` ViewModels | 16 custom colors `int.Parse(…, InvariantCulture)` + ARGB packing; defaults match `App.axaml` `Aog*` keys | settings/color round-trip test |
| ISOXML import/export semantics (`Field/FormFieldISOXML.cs`) | `FormFieldISOXMLView` ViewModel → `FieldIoService` | ISOXML V3/V4 semantics; name ≤248 bytes; AB+Curve export limit (behavior-frozen, AAP §0.2.2) | `IsoXmlEquivalenceTests` |
| Field/job load gating (`Field/FormFieldExisting.cs`, `FormJob.cs`) | `FormFieldExistingView` / `FormJobView` ViewModels | `isJobStarted` close-gate; `File.Exists`/`Directory.Exists` validation; `InvariantCulture` path/area parse | `FieldRoundTripTests` |

---

## GPS — Dialog Catalog Part 2 UI / Design / Accessibility Parity (CP4)

The CP4 deletions removed the WinForms *visual* surface of the Settings/Guidance/Inputs/Profiles/Field
dialogs. Because their Avalonia replacements are **Deferred** (`SourceCode/GPS/Views/` absent), this
section records the **design / interaction parity contract** each replacement View must meet, so visual
and behavioral fidelity is tracked rather than silently lost. The acceptance bar is **1:1 parity with the
current Windows Forms UI** (AAP §0.3.3); no Figma or design-system/token library was supplied, so the
currently-rendered UI is the sole visual reference.

- **Design-system / theme resources.** Every replacement View consumes the existing
  `SourceCode/GPS/App.axaml` `FluentTheme` base plus the day/night `Aog*` theme resources via
  `{DynamicResource}` — `AogFrameBrush` (panels/chrome), `AogTextBrush` (status/panel text),
  `AogAccentBrush` (primary/section teal accent `#FF1B97A0`), `AogFieldBrush` (viewport chrome). Day/night
  is driven by `Application.Current.RequestedThemeVariant` (Light=day, Dark=night), reproducing
  `FormGPS.SwapDayNightMode()`. The Inter font is registered via `.WithInterFont()`. No hard-coded colors
  — all fixed values map to the `Aog*` keys or live user `Settings`.
- **Keyboard & focus.** Logical tab order matching the WinForms layout; `Enter` = default/accept button,
  `Esc` = cancel (`IsCancel`); on-screen `Keypad.Keyboard`/`Keypad.NumKeypad` for touch text/number entry
  (GPS `FormKeyboardView`/`FormNumericView`); hotkey character whitelist preserved where applicable.
- **Responsive & touch.** Kiosk-style large, touch-friendly controls preserved 1:1 (button sizes, spacing,
  min window/dialog dimensions); dialogs remain usable at the touch breakpoints the WinForms originals
  targeted; `ScreenHelper.IsOnScreen` bounds-restore behavior reproduced.
- **Interactive states.** `disabled` / `hover` / `pressed(active)` / `focus` visual treatments via the
  Fluent control theme plus the `Aog*` accent; out-of-range numeric fields show the red error highlight
  (FormNumeric parity); convert/save buttons enable only when validation passes.
- **Accessibility.** `AutomationProperties.Name`/labeled inputs for screen-reader parity, visible focus
  indicators, and contrast targets — noting that where the current product's colors and Avalonia Fluent
  defaults differ, **parity to the current UI (AAP §0.3.3) takes precedence** since no Figma was supplied.

---

## GPS — Build Sequencing & Retained-Reference Inventory (CP3)

**Honest non-buildable status (F8).** The runtime moniker was flipped to `net8.0` in CP1
(`Directory.Build.props`), but the GPS project still declares `UseWindowsForms=true` /
`ImportWindowsDesktopTargets=true` and references WinForms-coupled packages
(`OpenTK.GLControl`, `GMap.NET.WinForms`, `MechanikaDesign.WinForms.UI.ColorPicker`,
`System.Windows.Forms.DataVisualization`). Consequently:

- `dotnet build SourceCode/GPS/AgOpenGPS.csproj` on Linux fails with **NETSDK1100** (Windows
  targeting requires `EnableWindowsTargeting=true` off-Windows); adding that flag then fails with
  **NETSDK1136** (Windows Forms/WPF requires a `-windows` target framework). The failure occurs at
  the **SDK target-resolution stage, before any source is compiled**.
- Therefore the retained references to deleted CP3 types (below) and the `FormGPS`-coupled GPS
  classes are **not yet reachable as compiler errors**; they surface only after the WinForms/TFM
  blocker is removed during the GPS `csproj`→Avalonia conversion.
- **Resolution checkpoints (CP4–CP9):** convert `AgOpenGPS.csproj` to the Avalonia stack (remove
  `UseWindowsForms`/`ImportWindowsDesktopTargets`, add Avalonia packages + RIDs, drop
  `System.Memory`/`System.ValueTuple` polyfills, replace GMap/ColorPicker/GLControl); rewrite
  `Program.cs` to the Avalonia bootstrap + `IPlatformServices` single-instance; create the `Views/`
  and `Services/` trees; and migrate the retained callers + GPS classes off `FormGPS`. The GPS
  project becomes buildable again once that conversion completes. AgIO, AgOpenGPS.Core, AgLibrary,
  and the three test projects build and pass throughout CP3.

**Project-metadata cleanup (F7).** In **CP3**, `SourceCode/GPS/AgOpenGPS.csproj` had its eight stale
`<Compile Update>` entries for the deleted partials (`Controls`, `GUI`, `Position`, `SaveOpen`,
`OpenGL`, `PGN`, `Sections`, `UDPComm` `.Designer.cs`, all `DependentUpon` the deleted `FormGPS.cs`)
**removed**; at that point the `Forms\Settings\Config*.Designer.cs` entries were still kept because
`FormConfig.cs` was retained. **CP4 update:** because CP4 deletes `FormConfig.cs` **and** all six
`Forms/Settings/Config*.Designer.cs` partial-designer files, their now-orphaned
`<Compile Update … DependentUpon="FormConfig.cs">` entries are **removed** from `AgOpenGPS.csproj`
in CP4, and the Windows-only `System.Windows.Forms.DataVisualization` `<Reference>` is **removed**
alongside the charting-dialog deletions (no consumer remains; R10) — see `CHANGELOG.md` Build/CI. The
`Resources.Designer.cs` / `BrandImages.Designer.cs` resource metadata remain preserved.

### Retained references to deleted CP3 types (F4) — resolved as callers migrate

> **CP4 reconciliation.** Six of the eight callers below were themselves **deleted in CP4** (the
> Profiles, `FormJob`, `FormColor`/`FormColorSection`, and `FormConfig.Designer.cs` dialogs); their
> dangling references to deleted CP3 types are therefore **resolved by deletion**, and each now has a
> direct replacement row in the *GPS — Dialog Catalog Part 2 (CP4)* section above. Only the two
> **non-dialog** callers (`Program.cs`, `RegistrySettings.cs`) genuinely remain retained after CP4.

**(a) Still genuinely retained after CP4** (file still exists; reference resolves when the caller migrates):

| Retained file (line) | Deleted type referenced | Resolution |
|---|---|---|
| `SourceCode/GPS/Program.cs` L32, L36–39 | `FormGPS`, `FormDialog` | Rewrite to Avalonia bootstrap; `FormDialog.Show` → Avalonia dialog (CP4/CP9). |
| `SourceCode/GPS/Properties/RegistrySettings.cs` L216 | `FormDialog` | Route user-facing warning through the Avalonia dialog service (CP4/CP9). |

**(b) Resolved by deletion in CP4** (caller deleted; replacement tracked in the CP4 catalog above):

| Former retained caller (deleted in CP4) | Deleted CP3 type it referenced | Replacement row (CP4 catalog) |
|---|---|---|
| `SourceCode/GPS/Forms/Profiles/FormLoadProfile.cs` | `FormDialog` | *Profile Dialogs* → `FormLoadProfileView` (+ deferred `FormDialogView`). |
| `SourceCode/GPS/Forms/Profiles/FormLoadVehicleTool.cs` | `FormDialog`, `FormInputDialog` | *Profile Dialogs* → `FormLoadVehicleToolView` (+ deferred `FormDialogView`/`FormInputDialogView`). |
| `SourceCode/GPS/Forms/Profiles/FormNewProfile.cs` | `FormTimedMessage` | *Profile Dialogs* → `FormNewProfileView` (+ deferred `FormTimedMessageView`). |
| `SourceCode/GPS/Forms/Field/FormJob.cs` | `FormFilePicker`, `FormDrivePicker`, `FormAgShareDownloader`, `FormAgShareUploader` | *Field Dialogs — Part 2* → `FormJobView` (hosts the deferred Picker/AgShare Views). |
| `SourceCode/GPS/Forms/Settings/FormColor.cs`, `FormColorSection.cs` | `FormColorPicker` | *Settings Dialogs* → `FormColorView` / `FormColorSectionView` (Avalonia `ColorPicker`). |
| `SourceCode/GPS/Forms/Settings/FormConfig.Designer.cs` | `ConfigSummaryControl`, `ConfigVehicleControl` | *Settings Dialogs* → `FormConfigView` (composes `ConfigSummaryView`/`ConfigVehicleView`). |

### GPS `Classes/*` still coupled to `FormGPS` (the `mf` god-object) (F5)

These 19 retained algorithm classes take a `FormGPS` constructor parameter / hold an `mf`
back-reference. The decoupling (constructor/service injection of `ApplicationModel` + services,
exactly as `CSmartWAS(FormGPS)` → `CSmartWAS(ApplicationModel)` was done in CP2) is performed as the
shell is replaced (CP4–CP9), with behavior frozen and parity-tested:

`AgShare/AgShareUploader.cs`, `CABCurve.cs`, `CABLine.cs`, `CBoundary.cs`, `CContour.cs`,
`CFieldData.cs`, `CGuidance.cs`, `CISOBUS.cs`, `CModuleComm.cs`, `CNMEA.cs`, `CPatches.cs`,
`CRecordedPath.cs`, `CSection.cs`, `CSim.cs`, `CTool.cs`, `CTrack.cs`, `CTram.cs`, `CVehicle.cs`,
`CYouTurn.cs`. (`CSmartWAS.cs` was already decoupled in CP2 and is `At parity` above.)

---

*This file is maintained as the single-source-of-truth mapping for the WinForms → Avalonia
migration. Entries record only files that exist in the repository at the current checkpoint;
later-checkpoint work is marked `Deferred`. See also `CHANGELOG.md`, `PARITY_REPORT.md`,
`FEATURE_TRACEABILITY.md`, and `VALUE_SUMMARY.md`.*
