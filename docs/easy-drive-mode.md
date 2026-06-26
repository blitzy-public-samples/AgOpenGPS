# Easy Drive Mode

> **Migration note (Avalonia).** The UI references in this specification were updated for the cross-platform **.NET 8/9 + Avalonia** migration: the former Windows Forms shells (`FormGPS`, `FormJob`, `FormEasyDrive`, `FormQuickAB`) are now Avalonia views bound to Core view-models, and the form partial-class logic now lives in the extracted services (`FieldIoService`, `SectionService`). **The Easy Drive Mode feature itself is unchanged** — every workflow step, gating rule, and numeric value below is preserved exactly. See [`../MIGRATION_DOCS/TRANSITION_MAP.md`](../MIGRATION_DOCS/TRANSITION_MAP.md) for the full transition narrative.

## Concept

A quick-start mode that lets the user drive with GPS guidance without loading a field.
The user sets a working width and pivot distance, then creates AB lines or Curves.
Everything stays in memory - nothing is saved to disk.

## Requirements

### Entry
- New button in `JobView` (extra row in the Avalonia `Grid`)
- Button is **disabled** when a field is already open (`isJobStarted`)
- Opens `EasyDriveView` for configuration

### EasyDriveView (new Avalonia view, touch-friendly)
- **Touch-friendly numeric input** for working width (respects metric/imperial)
- **Touch-friendly numeric input** for pivot distance (default 100cm)
- Large touch-friendly Start and Cancel buttons
- Configures a Rigid Tool with 1 section, in-memory only

### Active Mode Behavior
- Global flag: `public bool isEasyDriveMode` on `ApplicationViewModel`
- Field label shows "Easy Drive" (or "Temporary")
- User can create AB lines/Curves via the guidance flyout panel
- Multiple lines/curves can be created in the same session
- Flyout panel is limited: no ABDraw (boundary-dependent features hidden)
- `QuickABView` opens normally, user chooses when

### Disabled Features (gated by `isEasyDriveMode`)
- Config screen (blocked)
- Load Vehicle/Tool profiles (blocked)
- Headland, Boundary, Tram tools (blocked)
- All other `JobView` buttons disabled when Easy Drive is active
- Steer setting changes are NOT saved

### Saves Blocked
- `FileSaveEverythingBeforeClosingField` skips all file saves
- The `QuickABView` add command skips `FileSaveTracks()`
- `Settings.Default.setF_CurrentDir` not updated
- No field directory created on disk

### Exit
- Only via `JobView`: click the same button (or Close)
- Confirmation dialog via the Avalonia dialog/confirmation presenter (yes/no question)
- Tool settings restored from `ToolSettings.Default` via `SectionSetPosition()` + `SectionCalcWidths()` (now run through `SectionService`)
- `isEasyDriveMode = false`, `JobClose()`

## Implementation

### New Files

| File | Purpose |
|------|---------|
| `Views/Field/EasyDriveView.axaml.cs` | Configuration view (width + pivot distance) |
| `Views/Field/EasyDriveView.axaml` | View layout — XAML markup (replaces the former designer layout) |
| `ViewModels/Field/EasyDriveViewModel.cs` | Configuration view-model (width + pivot distance, Start/Cancel commands) |

### Modified Files

| File | Change |
|------|--------|
| `ApplicationViewModel` | Add `isEasyDriveMode` and `isEasyDriveRequested` flags |
| `JobViewModel` | Add EasyDrive command, disable logic, exit confirmation |
| `JobView.axaml` | Grid 5->6 rows, new button + separator |
| Application controller / view-models | Route Easy Drive from the job-menu command, gate saves via `FieldIoService`, gate features |
| `QuickABViewModel` | Gate `FileSaveTracks()` call |
| `ApplicationViewModel` (current-field label) | Show "Easy Drive" in the bound current-field label (`CurrentFieldText`) |
| `SectionService` | Called programmatically for tool setup/restore |

### Step-by-Step Build Order

#### 1. Global flags (ApplicationViewModel)
```csharp
public bool isEasyDriveMode = false;
public bool isEasyDriveRequested = false;
```

#### 2. EasyDriveView (new Avalonia view)
Touch-friendly view (`EasyDriveView` + `EasyDriveViewModel`) with:
- Touch-friendly numeric input for working width
- Touch-friendly numeric input for pivot distance (default 1.0m)
- Large Start + Cancel buttons

On Start:
```
tool.numOfSections = 1
tool.isSectionsNotZones = true
tool.isToolRearFixed = true (rigid)
tool.hitchLength = [pivot distance input]
section[0].positionLeft = -width/2
section[0].positionRight = width/2
SectionCalcWidths()
currentFieldDirectory = "Temporary"
pn.DefineLocalPlane(AppModel.CurrentLatLon, false)
JobNew()
isEasyDriveMode = true
```
(The section setup — the `section[0]` positions and `SectionCalcWidths()` — now runs through `SectionService`; the values are unchanged.)

#### 3. JobView / JobViewModel changes
- `JobView.axaml`: Grid rows 5 -> 6, add the EasyDrive button + a separator
- On load/bind: `IsEasyDriveEnabled = !isJobStarted`
- When `isEasyDriveMode`: all buttons disabled except Close
- Close in Easy Drive: confirm via the dialog/confirmation presenter, skip saves, restore tool, reset flag
- EasyDrive command: set `isEasyDriveRequested = true`, then close the dialog with an OK result

#### 4. Job-menu routing (application controller / JobViewModel)
After `JobView` closes, before other dialog-result checks:
```csharp
if (isEasyDriveRequested)
{
    isEasyDriveRequested = false;
    var easyDrive = new EasyDriveView { DataContext = new EasyDriveViewModel(appState) };
    await easyDrive.ShowDialog(mainWindow);
}
```

#### 5. FileSaveEverythingBeforeClosingField gate (FieldIoService / application controller)
At top of method:
```csharp
if (isEasyDriveMode)
{
    Dispatcher.UIThread.Post(() => {
        IsRightPanelEnabled = false;
        FieldMenuButtonEnableDisable(false);   // equivalent enable/disable command
        JobClose();
        isEasyDriveMode = false;
        WindowTitle = "AgOpenGPS";              // window-title bound property
    });
    return;
}
```

#### 6. QuickABView save gate
In the `QuickABViewModel` add command: `if (!isEasyDriveMode) fieldIoService.FileSaveTracks();`

#### 7. Feature gates
Block with the Avalonia timed-message presenter + return when `isEasyDriveMode`:
- Config screen opening
- Load Vehicle/Tool profiles
- Headland, Boundary, Tram menu items

#### 8. Guidance flyout panel adjustment
When building the guidance flyout panel's visibility:
- Hide the ABDraw button (the corresponding control in the guidance flyout) when `isEasyDriveMode` (no boundary)
- Keep visible: PlusAB (create track), nudge, off, snap to pivot

#### 9. Label display
In `ApplicationViewModel`: when `isEasyDriveMode`, the bound current-field label shows "Easy Drive"

#### 10. Tool restore on exit
```csharp
SectionSetPosition();   // reload from ToolSettings.Default
SectionCalcWidths();    // recalculate
```
(Both now run through `SectionService`; behavior unchanged.)

## UI Design Notes

- All controls must be touch-friendly (large buttons, numeric input with on-screen keypad)
- `EasyDriveView` follows the shared Avalonia button style (large bold text, flat style, blue accent/borders)
- Minimum target size for touch: 75px height per button
- The `JobView` Easy Drive button should have a distinctive color to stand out
