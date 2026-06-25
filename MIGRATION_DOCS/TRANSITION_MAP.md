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
> checkpoint is **CP2 — AgIO Comms-Hub Re-platform + GPS Low-Level + WinForms GL Host Retired**.
> The GPS WinForms UI surface (FormGPS + ~67 dialogs), the Avalonia GL viewport, and the AgIO
> complex configuration dialogs are explicitly later-checkpoint work (CP3/CP4/CP9) per the AAP's
> one-solution, multi-checkpoint structure.

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
| `SourceCode/GPS/Forms/Position.designer.cs` | `SourceCode/GPS/Forms/Position.designer.cs` | Migrated | At parity | `// [XPLAT]` Added the three-field live-state sync into `AppModel` immediately before `smartWAS.AddSample(...)` (type-exact: `bool`/`double`/`short`). This restores Smart WAS sample-collection parity that the constructor migration had silently disabled. (File also subject to the `Position.designer.cs` → `Position.Designer.cs` case-normalization rename tracked under cross-platform case fixes.) |
| `SourceCode/GPS/ResourcesBrands/BrandImages.resx` | `SourceCode/GPS/ResourcesBrands/BrandImages.resx` (restored) | Unchanged | At parity | `// [XPLAT]` **CP2 build-integrity fix:** the resource was restored byte-for-byte from baseline `f549b00f` (18,373 bytes) because its consumers remain in place this checkpoint — `SourceCode/GPS/AgOpenGPS.csproj` still embeds it and `SourceCode/GPS/Classes/Brands.cs` still calls the generated `BrandImages.*` accessors (48 accessors, all backed by resx entries; all 48 referenced PNGs present under `Brands/{Articulated,Brand,Harvester,Tractor}`). The GPS brand-image → Avalonia `avares://` asset migration is **deferred** to the GPS UI checkpoint (CP3/CP4/CP9), when `Brands.cs` and the csproj metadata are migrated together. |

---

## GPS — Controls

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/GPS/Controls/NudlessNumericUpDownExtensions.cs` | `SourceCode/Keypad/NumKeypad.axaml.cs` *(GPS host view deferred — CP3/CP4)* | Deleted | Deferred | `// [XPLAT]` WinForms static extension (`ShowKeypad(this NudlessNumericUpDown, Form)`) that highlighted the control and opened the WinForms `FormNumeric`. Both `NudlessNumericUpDown` and WinForms `FormNumeric` cease to exist after Avalonia re-platforming. The reusable Avalonia numeric keypad primitive `SourceCode/Keypad/NumKeypad.axaml.cs` **exists**; the GPS-side numeric-entry host view (`GPS/Views/Inputs/...`) is **deferred** to the GPS UI checkpoint (the `GPS/Views` tree does not exist yet). References in retained GPS WinForms forms are migrated by the GPS view checkpoints. |
| `SourceCode/GPS/Controls/TextBoxExtensions.cs` | *(GPS host view deferred — CP3/CP4)* | Deleted | Deferred | `// [XPLAT]` WinForms text-entry helper removed; GPS Avalonia text-entry surfaces are introduced at the GPS UI checkpoint. |
| `SourceCode/GPS/Controls/DraggableControlExtension.cs` | `(removed)` | Deleted | N/A | `// [XPLAT]` WinForms draggable-control helper; no Avalonia equivalent required (Avalonia layout handles this natively). |

---

*This file is maintained as the single-source-of-truth mapping for the WinForms → Avalonia
migration. Entries record only files that exist in the repository at the current checkpoint;
later-checkpoint work is marked `Deferred`. See also `CHANGELOG.md`, `PARITY_REPORT.md`,
`FEATURE_TRACEABILITY.md`, and `VALUE_SUMMARY.md`.*
