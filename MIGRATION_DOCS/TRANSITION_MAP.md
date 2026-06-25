# Transition Map — AgOpenGPS net48/WinForms → net8.0/Avalonia Migration

<!-- [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/CHANGELOG.md -->

This document provides the old→new file-by-file mapping for the AgOpenGPS migration from
`.NET Framework 4.8` / Windows Forms to `.NET 8/9` / Avalonia UI (cross-platform: Windows,
macOS, Linux), covering all twelve projects in `SourceCode/AgOpenGPS.sln`. Each entry records
the original Windows-coupled file, its cross-platform replacement (if any), the disposition,
and the parity status. It is maintained incrementally as files are migrated.

## Legend

| Column | Description |
|--------|-------------|
| **Original** | Path of the source (pre-migration) file in the `net48`/WinForms baseline |
| **Replaced With** | Path(s) of the replacement file(s) in the migrated codebase, or `(removed)` when deleted with no direct replacement |
| **Disposition** | `Migrated` (minimal-change recompile / de-Windowsed) · `Reimplemented` (rebuilt on the Avalonia stack with 1:1 parity) · `Feature-gated` (Windows-only capability, graceful no-op elsewhere) · `Unchanged` · `Deleted` |
| **Parity Status** | `At parity` · `Superseded` · `Pending verification` · `Feature-gated (per-OS)` · `Deferred` · `N/A` (documentation/config, not a behavioral artifact) |
| **Notes** | Additional context |

---

## AgIO — UI Shell (`SourceCode/AgIO/Source/Forms/` → `SourceCode/AgIO/Source/Views/`)

All WinForms dialog files in `SourceCode/AgIO/Source/Forms/` are **Reimplemented** as Avalonia
views under `SourceCode/AgIO/Source/Views/`; the entire `Forms/` directory is eliminated as part
of the Avalonia migration. The WinForms code-behind (`.cs`), designer-generated layout
(`.Designer.cs` / `.designer.cs`), and resource (`.resx`) files are removed and their behavior is
preserved 1:1 in the corresponding Avalonia view and view-model.

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/AgIO/Source/Forms/FormISOBUS.cs` | `SourceCode/AgIO/Source/Views/FormISOBUS.axaml`<br/>`SourceCode/AgIO/Source/Views/FormISOBUS.axaml.cs` | Reimplemented | At parity | See FormISOBUS migration notes below. |
| `SourceCode/AgIO/Source/Forms/FormISOBUS.Designer.cs` | `SourceCode/AgIO/Source/Views/FormISOBUS.axaml` + `FormISOBUS.axaml.cs` | Reimplemented | At parity | `// [XPLAT]` WinForms designer-generated layout superseded by Avalonia XAML. Controls: CAN adapter selector, channel selector, open/close ISOBUS buttons, download link, receive log text box. Logic preserved in `FormISOBUSViewModel.cs`. |
| `SourceCode/AgIO/Source/Forms/FormISOBUS.resx` | `SourceCode/AgIO/Source/Views/FormISOBUS.axaml` | Reimplemented | Pending | WinForms designer resource container superseded by Avalonia XAML resources. |
| `SourceCode/AgIO/Source/Forms/FormAdvancedSettings.cs` | `SourceCode/AgIO/Source/Views/FormAdvancedSettings.axaml` + `FormAdvancedSettings.axaml.cs` | Reimplemented | Pending verification | Avalonia view preserves 3 display-preference checkboxes (`setDisplay_isAutoRunGPS_Out`, `setDisplay_StartMinimized`, `setDisplay_ShowOnWarning`) and save-on-close behaviour; `FormLoop mf` god-object reference replaced by constructor-injected `FormAdvancedSettingsViewModel`. |
| `SourceCode/AgIO/Source/Forms/FormAdvancedSettings.Designer.cs` | `SourceCode/AgIO/Source/Views/FormAdvancedSettings.axaml` | Reimplemented | Pending | `// [XPLAT]` WinForms designer-generated layout for the advanced-settings dialog; reimplemented in Avalonia XAML. |
| `SourceCode/AgIO/Source/Forms/FormAdvancedSettings.resx` | `SourceCode/AgIO/Source/Views/FormAdvancedSettings.axaml` | Reimplemented | Pending | WinForms `.resx` designer resource container. File contained only standard ResX schema boilerplate with no custom embedded resources (no icons, strings, or bitmaps). Resources migrate to Avalonia inline XAML resources and `avares://` image assets. `ResXFileCodeGenerator`/WinForms resx pipeline is not used in the Avalonia architecture. |
| `SourceCode/AgIO/Source/Forms/FormEthernet.cs` | `SourceCode/AgIO/Source/Views/FormEthernet.axaml` + `FormEthernet.axaml.cs` | Reimplemented | Pending | `// [XPLAT]` WinForms code-behind for the Ethernet/UDP loopback IP configuration dialog; reimplemented as an Avalonia view. `System.Windows.Forms` dependency purged per AAP §0.4.2. |
| `SourceCode/AgIO/Source/Forms/FormEthernet.designer.cs` | `SourceCode/AgIO/Source/Views/FormEthernet.axaml` + `FormEthernet.axaml.cs` | Reimplemented | At parity | WinForms designer-generated layout (IP address pickers for UDP loopback, UDP on/off toggle). Replaced by Avalonia XAML view. Case-sensitivity hazard resolved: lowercase `.designer.cs` removed; Avalonia replacement uses correct Pascal-case naming. `// [XPLAT]` See §0.6.5, G7. |
| `SourceCode/AgIO/Source/Forms/FormEthernet.resx` | `SourceCode/AgIO/Source/Views/FormEthernet.axaml` | Reimplemented | Pending | WinForms `.resx` designer resource superseded by Avalonia XAML resources. |
| `SourceCode/AgIO/Source/Forms/FormCommSetGPS.cs` | `SourceCode/AgIO/Source/Views/FormCommSetGPS.axaml` + `FormCommSetGPS.axaml.cs` | Reimplemented | Pending | `// [XPLAT]` WinForms code-behind for the GPS serial/comm-port configuration dialog; reimplemented as an Avalonia view. Serial-port enumeration abstracted via `IPlatformServices`. |
| `SourceCode/AgIO/Source/Forms/FormCommSetGPS.Designer.cs` | `SourceCode/AgIO/Source/Views/FormCommSetGPS.axaml` + `FormCommSetGPS.axaml.cs` | Reimplemented | Pending | WinForms designer-generated layout superseded by Avalonia XAML. |
| `SourceCode/AgIO/Source/Forms/FormCommSetGPS.resx` | `SourceCode/AgIO/Source/Views/FormCommSetGPS.axaml` | Reimplemented | Pending | WinForms `.resx` designer resource superseded by Avalonia XAML resources. |
| `SourceCode/AgIO/Source/Forms/FormEventViewer.cs` | `SourceCode/AgIO/Source/Views/FormEventViewer.axaml` + `FormEventViewer.axaml.cs` | Reimplemented | Pending verification | `// [XPLAT]` WinForms `public partial class FormEventViewer : Form` replaced by Avalonia view; `System.Windows.Forms` dependency purged per AAP §0.4.2; Avalonia view created by Views agent; log-file reading behavior (StreamReader loop + `Log.sbEvents` display) preserved 1:1. |
| `SourceCode/AgIO/Source/Forms/FormEventViewer.Designer.cs` | `SourceCode/AgIO/Source/Views/FormEventViewer.axaml`<br/>`SourceCode/AgIO/Source/Views/FormEventViewer.axaml.cs` | Reimplemented | Superseded | WinForms designer-generated layout superseded by Avalonia XAML. |
| `SourceCode/AgIO/Source/Forms/FormEventViewer.resx` | `SourceCode/AgIO/Source/Views/FormEventViewer.axaml` | Reimplemented | Pending verification | WinForms `.resx` designer resource superseded by Avalonia XAML resources. |
| `SourceCode/AgIO/Source/Forms/FormKeyboard.cs` | `SourceCode/AgIO/Source/Views/FormKeyboard.axaml` · `SourceCode/AgIO/Source/Views/FormKeyboard.axaml.cs` · `SourceCode/AgIO/Source/Views/FormKeyboardViewModel.cs` | Reimplemented | At parity | `// [XPLAT]` On-screen keyboard for touch text entry. WinForms `public partial class FormKeyboard : Form` purged per AAP §0.4.2. 1:1 parity with touch-friendly large controls preserved. Related: `AgIO/Controls/TextBoxExtensions.ShowKeyboard()` updated by Controls agent. |
| `SourceCode/AgIO/Source/Forms/FormKeyboard.Designer.cs` | `SourceCode/AgIO/Source/Views/FormKeyboard.axaml` / `FormKeyboard.axaml.cs` | Reimplemented | At parity | `// [XPLAT]` WinForms Designer-generated partial class; UI layout reimplemented in AXAML (replacement created by Views agent). |
| `SourceCode/AgIO/Source/Forms/FormKeyboard.resx` | `SourceCode/AgIO/Source/Views/FormKeyboard.axaml` | Reimplemented | Superseded | `// [XPLAT]` WinForms `.resx` resources migrated to Avalonia XAML view. `keyboard1.Locked` metadata no longer required in the Avalonia layout model. |
| `SourceCode/AgIO/Source/Forms/FormGPSData.cs` | `SourceCode/AgIO/Source/Views/FormGPSData.axaml`<br>`SourceCode/AgIO/Source/Views/FormGPSData.axaml.cs`<br>`SourceCode/AgIO/Source/Views/FormGPSDataViewModel.cs` | Reimplemented | Pending | Avalonia view wired to `MainWindowViewModel` via data-binding; live GPS/NMEA telemetry readouts (latitude, longitude, fix quality, satellites, HDOP, speed, roll, IMU data, heading, altitude, raw NMEA sentences VTG/GGA/HDT/AVR/PAOGI/HPD/PANDA/KSXT) bound to view-model properties populated by extracted NMEA/UDP services. `System.Windows.Forms` dependency removed. |
| `SourceCode/AgIO/Source/Forms/FormGPSData.Designer.cs` | `SourceCode/AgIO/Source/Views/FormGPSData.axaml`<br>`SourceCode/AgIO/Source/Views/FormGPSData.axaml.cs`<br>`SourceCode/AgIO/Source/Views/FormGPSDataViewModel.cs` | Reimplemented | Pending | WinForms designer-generated layout superseded by Avalonia XAML. |
| `SourceCode/AgIO/Source/Forms/FormGPSData.resx` | `SourceCode/AgIO/Source/Views/FormGPSData.axaml` | Reimplemented | N/A | WinForms designer resource (timer1.TrayLocation metadata only). Resources superseded by Avalonia XAML layout; no string resources to migrate. |

---

## AgIO — Classes (de-WinForms / cross-platform)

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/AgIO/Source/Classes/CGLM.cs` | `SourceCode/AgIO/Source/Classes/CGLM.cs` | Migrated | At parity | `// [XPLAT]` Removed `using System.Windows.Forms;`; glm/math utility is now framework-agnostic for net8.0. |
| `SourceCode/AgIO/Source/Classes/CRadioChannel.cs` | `SourceCode/AgIO/Source/Classes/CRadioChannel.cs` | Migrated | At parity | `// [XPLAT]` Provenance-tagged; no behavioral change, recompiled for net8.0+Avalonia. |
| `SourceCode/AgIO/Source/Classes/ListViewColumnSorterExt.cs` | `SourceCode/AgIO/Source/Classes/ListViewColumnSorterExt.cs` | Migrated | At parity | `// [XPLAT]` Removed `System.Windows.Forms` dependency; added portable `ListSortState` enum to replace `System.Windows.Forms.SortOrder`; comparer is now framework-agnostic and culture-invariant (`InvariantCulture`) for deterministic cross-OS ordering. Consumed by the Avalonia view-model. |

---

## AgIO — Build / Configuration

| Original | Replaced With | Disposition | Parity Status | Notes |
|---|---|---|---|---|
| `SourceCode/AgIO/Source/App.config` | `(removed)` | Deleted | N/A | `net48` `App.config` startup section removed; not used by SDK-style net8.0 builds. Breaks on case-sensitive filesystems / modern SDK builds per AAP §0.7 (G7). |

---

## FormISOBUS — Migration Notes

<!-- [XPLAT] migrated from net48/WinForms — see TRANSITION_MAP.md -->

**Original:** `SourceCode/AgIO/Source/Forms/FormISOBUS.cs`
**Replaced by:** `SourceCode/AgIO/Source/Views/FormISOBUS.axaml` + `FormISOBUS.axaml.cs`
**Disposition:** Reimplemented (1:1 parity)

Key changes in the Avalonia reimplementation:
- `public partial class FormISOBUS : Form` → Avalonia `Window` bound to a view-model
- `MessageBox.Show(...)` calls (×3) → Avalonia dialog or Core `IErrorPresenter`
- `Microsoft.Win32.Registry` (AOG-TaskController installation path) → `IPlatformServices` abstraction
- `Application.DoEvents()` (Windows threading) → async/await or Avalonia Dispatcher
- `InvokeRequired` / `Invoke(new Action(...))` → `Avalonia.Threading.Dispatcher.UIThread.InvokeAsync`
- `System.Windows.Forms` namespace → purged (AAP §0.4.2)

Behavior preserved:
- ISOBUS CAN adapter selection (PEAK-PCAN, InnoMaker-USB2CAN, Rusoku-TouCAN, SYS-TEC-USB2CAN) with channel counts
- AOG-TaskController process lifecycle (start, stop, graceful shutdown with 5-second timeout)
- Log output redirection (stdout + stderr) with 100,000-character cap
- Settings persistence (`isobus_canAdapterIndex`, `isobus_canChannelIndex`, `isobus_isOn`)
- Download link for AOG-TaskController GitHub repository

---

*This file is maintained as the single-source-of-truth mapping for the WinForms → Avalonia
migration. Entries are added as each file is processed. See also `CHANGELOG.md`,
`PARITY_REPORT.md`, `FEATURE_TRACEABILITY.md`, and `VALUE_SUMMARY.md`.*
