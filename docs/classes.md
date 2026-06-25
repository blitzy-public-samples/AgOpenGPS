# Core Classes

## Business Logic Distribution

Following the cross-platform migration (.NET Framework 4.8 / Windows Forms → .NET 8/9 + Avalonia), the business logic is distributed across the portable `AgOpenGPS.Core` library and the GPS application project:

| Location | Purpose |
|----------|---------|
| `GPS/Classes/` | Main application logic, state management, guidance (recompiled cross-platform; behavior unchanged) |
| `AgOpenGPS.Core/Models/` | Reusable models, geo conversions, helpers |
| `AgOpenGPS.Core/ViewModels/` | MVVM view-models and commands (`ApplicationViewModel`, `RelayCommand` / `RelayCommand<T>`, base `ViewModel`) — the previously null-wired scaffold is now active and bound to the Avalonia views |
| `AgOpenGPS.Core/Presenters/` + `Interfaces/Presenters/` | Presenter contracts and implementations (`IApplicationPresenter`, `IErrorPresenter`, `IPanelPresenter`, concrete `ApplicationPresenter`) wired through the composition root `ApplicationCore` |
| `AgOpenGPS.Core/Platform/` | Cross-platform OS abstraction (`IPlatformServices`, `PlatformServicesFactory`) isolating per-OS capabilities behind one interface |
| `GPS/Services/` | Plain services extracted from the former `FormGPS` partial classes — scan-loop, PGN dispatch, section control, field I/O, and render coordination (behavior frozen) |

---

# GPS/Classes/ - Main Application Logic

These classes manage application state and core operations:

> **Cross-platform note:** Every `C`-prefixed class below is recompiled unchanged on .NET 8/9 — its **behavior, methods, and outputs are frozen** by the migration. The only structural change is how collaborators are obtained: the former global `FormGPS` (`mf`) back-reference has been replaced by **constructor-injected** collaborators and Core view-models (see [Dependency Injection](#dependency-injection-replaces-the-formgps-reference) below).

## CVehicle

**File:** `Classes/CVehicle.cs`

**Purpose:** Vehicle configuration and steering parameters

**Key Properties:**
```csharp
// Dead zone settings
public int deadZoneHeading, deadZoneDelay;
public bool isInDeadZone;

// Speed limits
public double slowSpeedCutoff;
public double functionSpeedLimit;
public double maxSteerSpeed, minSteerSpeed;

// Steering gains
public double stanleyDistanceErrorGain;
public double stanleyHeadingErrorGain;
public double stanleyIntegralGainAB;
public double purePursuitIntegralGain;

// Lookahead parameters
public double goalPointLookAheadHold;
public double goalPointLookAheadMult;
public double goalPointAcquireFactor;

// Steering state
public bool isInFreeDriveMode;
public double driveFreeSteerAngle;
public double modeXTE, modeTime;
```

**Key Methods:**
- Constructor loads settings from VehicleSettings and ToolSettings
- `UpdateGenreals()` - Updates calculated steering parameters

---

## CTool

**File:** `Classes/CTool.cs`

**Purpose:** Tool configuration and section management

**Key Properties:**
```csharp
// Dimensions
public double width, halfWidth, overlap;
public double offset;
public double hitchLength;

// Trailing tool settings
public bool isToolTrailing, isToolTBT;
public bool isToolRearFixed, isToolFrontFixed;
public double trailingHitchLength;
public double tankTrailingHitchLength;

// Lookahead settings
public double lookAheadOnSetting;
public double lookAheadOffSetting;
public double turnOffDelay;

// Section configuration
public int numOfSections;
public int minCoverage;
public bool isSectionsNotZones;
public bool isSectionOffWhenOut;

// Zone support
public int zones;
public int[] zoneRanges;

// Colors
public Color[] secColors;
```

**Key Methods:**
- `SetCurrentToolConfig()` - Loads tool settings from ToolSettings
- `ResetSectionPositions()` - Resets section positions
- `UpdateSectionPositions()` - Updates section positions based on width

---

## CSection

**File:** `Classes/CSection.cs`

**Purpose:** Individual section control state

**Key Properties:**
```csharp
// Section state
public bool isSectionOn;
public bool isSectionRequiredOn;
public bool sectionOnRequest, sectionOffRequest;

// Timers
public int sectionOnTimer, sectionOffTimer;
public int mappingOnTimer, mappingOffTimer;

// Position (meters from center, left is negative)
public double positionLeft = -4;
public double positionRight = 4;
public double sectionWidth;

// World space points
public vec2 leftPoint, rightPoint;
public vec2 lastLeftPoint, lastRightPoint;

// Location state
public bool isInBoundary;
public bool isInHeadlandArea;
public bool isLookOnInHeadland;

// Button state
public btnStates sectionBtnState;
```

**Key Methods:**
- `SetSectionColors()` - Sets section display colors
- `DrawSection()` - Renders section in OpenGL

---

## CGuidance

**File:** `Classes/CGuidance.cs`

**Purpose:** Steering angle calculation using Stanley or Pure Pursuit algorithms

**Key Properties:**
```csharp
// Distance errors
public double distanceFromCurrentLineSteer;
public double distanceFromCurrentLinePivot;
public double steerAngleGu;

// Heading error
public double steerHeadingError;
public double distSteerError, lastDistSteerError;
public double derivativeDistError;

// Integral
public double inty;

// Side hill compensation
public double sideHillCompFactor;
```

**Key Methods:**
- `DoSteerAngleCalc()` - Stanley steering calculation
- `PurePursuitSteerCalc()` - Pure Pursuit steering calculation
- `CalculateIntegral()` - Integral term calculation

**Stanley Algorithm:**
```csharp
// Cross-track error
double XTEc = Math.Atan((distanceFromCurrentLineSteer * stanleyDistanceErrorGain) / speed);

// Heading error
steerHeadingError *= stanleyHeadingErrorGain;

// Combined
steerAngleGu = (XTEc + steerHeadingError) * -1.0;
```

---

## CAHRS

**File:** `Classes/CAHRS.cs`

**Purpose:** IMU data and GPS/IMU fusion

**Key Properties:**
```csharp
// Raw IMU data
public double imuHeading = 99999;
public double imuRoll = 0;
public double imuPitch = 0;
public double imuYawRate = 0;

// Roll zero offset
public double rollZero;
public double rollFilter;

// Settings
public bool isAutoSteerAuto;
public bool isRollInvert;
public bool isDualAsIMU;
public bool isReverseOn;

// Fusion parameters
public double forwardComp;
public double reverseComp;
public double fusionWeight;

// Auto-switch
public bool autoSwitchDualFixOn;
public double autoSwitchDualFixSpeed;
```

**Key Methods:**
- `ResetAHRS()` - Resets AHRS values
- `SetHeading()` - Sets fused heading from GPS/IMU

---

## CABLine

**File:** `Classes/CABLine.cs`

**Purpose:** AB line guidance calculations

**Key Properties:**
```csharp
// AB line points
public vec3 currentLinePtA;
public vec3 currentLinePtB;
public double abHeading, abLength;

// Distance from line
public double distanceFromCurrentLinePivot;
public double distanceFromRefLine;

// Pure pursuit
public vec2 goalPointAB;
public vec2 radiusPointAB;
public double ppRadiusAB;
public double steerAngleAB;

// State
public bool isABValid;
public bool isMakingABLine;
public bool isHeadingSameWay;

// Design mode
public vec2 desPtA, desPtB;
public double desHeading;
public string desName;
```

**Key Methods:**
- `CreateCurrentABLine()` - Creates AB line from A and B points
- `GetCurrentABLine()` - Returns current AB line offset by path count
- `ResetABLines()` - Clears all AB lines
- `SnapABLine()` - Snaps to existing AB line

---

## CContour

**File:** `Classes/CContour.cs`

**Purpose:** Contour guidance calculations

**Key Properties:**
```csharp
// Contour points
public List<vec3> ptList;

// Current contour
public vec2 goalPointCT;
public vec2 radiusPointCT;
public double steerAngleCT;

// State
public bool isContourValid;
public bool isContourOn;
public int currentIndex;
```

**Key Methods:**
- `BuildContour()` - Builds contour from recorded points
- `GetCurrentContour()` - Returns current contour point
- `SmoothContour()` - Applies smoothing to contour

---

## CTrack

**File:** `Classes/CTrack.cs`

**Purpose:** Current guidance track state (combines AB Line, Contour, etc.)

**Key Properties:**
```csharp
// Track type
public enum TrackType { None, ABLine, Curve, Contour }

// Current track state
public TrackType trackType;
public double distanceFromCurrentLine;
public double steerAngle;

// Guidance line
public vec2 goalPoint;
public vec2 radiusPoint;

// Mode
public int mode;
public bool isLocked;
```

**Key Methods:**
- `UpdateTrack()` - Updates track calculations
- `GetGoalPoint()` - Returns goal point for guidance
- `GetSteerAngle()` - Returns calculated steer angle

---

## CYouTurn

**File:** `Classes/CYouTurn.cs`

**Purpose:** Automatic U-turn generation at headlands

**Key Properties:**
```csharp
// U-turn state
public bool isYouTurnTriggered;
public bool isTurnLeft;
public bool isYouTurnBtnOn;

// U-turn parameters
public double youTurnRadius;
public int youTurnStartOffset;
public int uTurnStyle;
public int rowSkipsWidth;
public int uTurnSmoothing;

// Generated path
public List<vec3> ytList;
public vec2 goalPointYT;
public vec2 radiusPointYT;
public double steerAngleYT;

// Turn validation
public bool isTurnCreationTooClose;
public bool isTurnCreationNotCrossingError;
public bool isOutOfBounds;
```

**Key Methods:**
- `BuildYouTurnList()` - Generates U-turn path points
- `CalculateYouTurn()` - Calculates U-turn steering
- `ResetYouTurn()` - Resets U-turn state

**U-Turn Styles:**
- 0: Basic U-turn
- 1: Wide U-turn
- 2: Y-turn

---

## CModuleComm

**File:** `Classes/CModuleComm.cs`

**Purpose:** Communication with steering modules

**Key Properties:**
```csharp
// Safety
public bool isOutOfBounds;

// Section switch data (PGN 234)
public byte[] ss = new byte[9];
public int swHeader, swMain, swNumSections;

// Module feedback
public int pwmDisplay = 0;
public double actualSteerAngleDegrees = 0;
public int actualSteerAngleChart = 0;
public int sensorData = -1;

// Work switch
public bool isWorkSwitchActiveLow;
public bool isRemoteWorkSystemOn;
public bool workSwitchHigh, steerSwitchHigh;
```

**Key Methods:**
- `CheckWorkAndSteerSwitch()` - Handles work/steer switch input

---

## CISOBUS

**File:** `Classes/CISOBUS.cs`

**Purpose:** ISOBUS section control communication

**Key Properties:**
```csharp
// Section control
private bool sectionControlEnabled;
private bool[] actualSectionStates;

// Process data
private int lastGuidanceLineDeviation;
private int lastActualSpeed;
private int lastTotalDistance;
```

**Key Methods:**
- `RequestSectionControlEnabled(bool)` - Request section control
- `SetGuidanceLineDeviation(int)` - Send guidance deviation
- `SetActualSpeed(int)` - Send vehicle speed
- `SetTotalDistance(int)` - Send total distance
- `DeserializeHeartbeat(byte[])` - Parse ISOBUS heartbeat
- `IsAlive()` - Check if ISOBUS connected (1s timeout)

---

## CBoundary / CBoundaryList

**File:** `Classes/CBoundary.cs`, `Classes/CBoundaryList.cs`

**Purpose:** Boundary (geofence) definition and checking

**Key Properties:**
```csharp
// Boundary points
public List<vec3> ptList;

// Area
public double area;

// State
public bool isBndBeingMade;
public bool isBndBeingViewed;
```

**Key Methods:**
- `IsPointInsideBoundary(vec2)` - Checks if point is inside
- `CalculateArea()` - Calculates boundary area
- `SmoothBoundary()` - Applies Douglas-Peucker smoothing

---

## CFence

**File:** `Classes/CFence.cs`

**Purpose:** Geofence logic for stopping guidance outside boundary

**Key Properties:**
```csharp
// Fence state
public bool isAlright
public bool isOnInZone;

// Trigger points
public vec2 turnLineStart, turnLineEnd;
```

**Key Methods:**
- `FenceCheck()` - Checks if vehicle is in boundary
- `TurnOnDisableSections()` - Turns off sections when out
- `TurnOnEnableSections()` - Turns on sections when in

---

## CHead

**File:** `Classes/CHead.cs`

**Purpose:** Headland detection and section control

**Key Properties:**
```csharp
// Headland state
public bool isInHeadland;

// Headland distance
public double distanceFromHeadland;
```

**Key Methods:**
- `HeadlandCheck()` - Checks if vehicle is in headland area

---

## CNMEA

**File:** `Classes/CNMEA.cs`

**Purpose:** NMEA sentence parsing

**Key Methods:**
- `ParseGPGGA()` - Parse GPGGA sentence
- `ParseGPRMC()` - Parse GPRMC sentence
- `ParseGPVTG()` - Parse GPVTG sentence
- `ParseGPCHC()` - Parse GPCHC (ComNav) sentence

---

## CSim

**File:** `Classes/CSim.cs`

**Purpose:** Simulation mode for testing

**Key Properties:**
```csharp
public double simLatitude;
public double simLongitude;
public double simHeading;
public double simSpeed;
```

**Key Methods:**
- `UpdateSimulation()` - Updates simulated position

---

## CFieldData

**File:** `Classes/CFieldData.cs`

**Purpose:** Field save/load operations

**Key Methods:**
- `SaveField()` - Saves field to file
- `LoadField()` - Loads field from file
- `CreateFieldFile()` - Creates new field file

---

## CTram

**File:** `Classes/CTram.cs`

**Purpose:** Tramline management

**Key Properties:**
```csharp
public List<vec3> tramList;
public double tramWidth;
public int passes;
```

**Key Methods:**
- `BuildTram()` - Generates tramlines from boundary
- `DrawTram()` - Renders tramlines

---

## CFlag

**File:** `Classes/CFlag.cs`

**Purpose:** Flag/waypoint management

**Key Properties:**
```csharp
public vec2 position;
public string notes;
public int ID;
```

---

## CRecordedPath

**File:** `Classes/CRecordedPath.cs`

**Purpose:** Recorded path storage

**Key Properties:**
```csharp
public List<vec3> path;
public string name;
```

---

## CSettingsMigration

**File:** `Classes/CSettingsMigration.cs`

**Purpose:** Migrates old single-file settings to new split format

**Key Methods:**
- `MigrateVehicle()` - Migrates vehicle settings
- `MigrateTool()` - Migrates tool settings
- `MigrateEnvironment()` - Migrates environment settings

---

# AgOpenGPS.Core/Models/ - Shared Models & Conversions

## Geo Models (Models/Base/)

### GeoCoord
**Purpose:** Geographic coordinate with northing/easting

**Key Properties:**
```csharp
public double Northing { get; set; }
public double Easting { get; set; }
```

### Wgs84
**Purpose:** WGS84 latitude/longitude coordinate

**Key Properties:**
```csharp
public double Latitude { get; set; }
public double Longitude { get; set; }
```

### LocalPlane
**Purpose:** Local plane projection for calculations

**Key Methods:**
- `ConvertWgs84ToGeoCoord()` - Converts WGS84 to local easting/northing
- `ConvertGeoCoordToWgs84()` - Converts local to WGS84

### Units
**Purpose:** Unit conversion utilities

**Key Methods:**
- `m2in()` - Meters to inches
- `in2m()` - Inches to meters
- `kmh2mph()` - km/h to mph

## Field Models (Models/Field/)

| Class | Purpose |
|-------|---------|
| `Field` | Field definition with name, directory |
| `Boundary` | Boundary data with point list |
| `TramLines` | Tramline storage |
| `Contour` | Contour data |
| `RecordedPath` | Recorded track data |
| `WorkedArea` | Worked area polygon |
| `Flag` | Flag/waypoint |

## Guidance Models (Models/Guidance/)

| Class | Purpose |
|-------|---------|
| `DubinsPathSelector` | Dubins path algorithm for smooth turns |
| `DubinsPathConstraints` | Path constraints (min radius, etc.) |

---

# Naming Convention

- Classes in GPS/: `C` prefix (e.g., `CSection`)
- Views (Avalonia): `*.axaml` markup + matching code-behind (e.g., `MainView.axaml` / `MainView.axaml.cs`)
- View-models: `*ViewModel` suffix (e.g., `ApplicationViewModel`)
- Services (extracted): `*Service` suffix (e.g., `PositionService`)
- Core models: No prefix (e.g., `GeoCoord`)

> The legacy WinForms shells that used the `Form` prefix (e.g., `FormGPS`, `FormLoop`) have been reimplemented as Avalonia views following the naming above.

---

# Common Patterns

## Dependency Injection (replaces the FormGPS reference)

Collaborators are **constructor-injected** into the extracted `GPS/Services/` classes and bound through the Core view-models (`ApplicationViewModel`). This eliminates the former `FormGPS` god-object: classes no longer reach back to a single global form for shared state.

```csharp
// Collaborators are supplied explicitly at construction
public PositionService(CVehicle vehicle, CTool tool, CAHRS ahrs, CTrack trk, /* position-navigation */ ...)
```

The same collaborators that were previously reached through the main form are now received directly:

| Collaborator | Type | Role |
|--------------|------|------|
| `vehicle` | `CVehicle` | Vehicle configuration and steering parameters |
| `tool` | `CTool` | Tool configuration and section management |
| `ahrs` | `CAHRS` | IMU data and GPS/IMU fusion |
| `trk` | `CTrack` | Current guidance track state |
| `pn` | Position/Navigation data | GPS fix and local-plane position |

> **Historical context:** the legacy net48/WinForms build held `private readonly FormGPS mf;` in most classes and accessed shared state via `mf.vehicle`, `mf.tool`, `mf.ahrs`, `mf.trk`, and `mf.pn`. That back-reference has been **replaced** by the constructor injection shown above and by data binding from the Avalonia view-models.

## Static Settings Access

Settings are accessed via static `Default` property:

```csharp
Properties.VehicleSettings.Default.setVehicle_wheelbase = 3.3;
Properties.ToolSettings.Default.setVehicle_toolWidth = 4.0;
Properties.Settings.Default.setMenu_isMetric = true;
```

## From FormGPS Partial Classes to Services

The migration **decomposes** the former `FormGPS` partial classes (`FormGPS.cs`, `UDPComm.Designer.cs`, `PGN.Designer.cs`, and the other `*.Designer.cs` partials) into a thin Avalonia UI shell plus plain, injectable services. **Behavior is frozen** — only the *location and ownership* of the logic changed:

| Former partial | New owner | Responsibility |
|----------------|-----------|----------------|
| `FormGPS.cs` (UI shell) | `Views/MainView.axaml` (+ `.axaml.cs`) and `App.axaml` (+ `.axaml.cs`), bound to `ApplicationViewModel` | Main kiosk window and application bootstrap |
| `Position.designer.cs` | `Services/PositionService.cs` | Per-fix scan loop (GPS-fix processing, heading/roll fusion, WGS84→local-plane conversion) |
| `UDPComm.Designer.cs` + `PGN.Designer.cs` | `Services/PgnDispatcher.cs` | UDP receive loops and PGN encode/decode/CRC |
| `Sections.Designer.cs` | `Services/SectionService.cs` | Section/zone control and machine-byte logic |
| `SaveOpen.Designer.cs` | `Services/FieldIoService.cs` | Field load/save/export |
| `OpenGL.Designer.cs` | `Services/RenderCoordinator.cs` (+ `Controls/AvaloniaGeoViewport.cs` for GL hosting) | Projection/frustum/back-buffer scan and overlays |

The extracted services take explicit inputs and produce explicit outputs (no global form back-reference), and each is parity-tested against the WinForms baseline so the real-time loop timing and guidance outputs remain identical.

## Cross-Platform Services

Operating-system-specific capabilities are isolated behind a single interface, `IPlatformServices`, defined in the portable `AgOpenGPS.Core/Platform/` assembly. A `PlatformServicesFactory` selects the concrete implementation at startup via `RuntimeInformation.IsOSPlatform`:

- `WindowsPlatformServices` — Windows-only paths and capabilities (compiled under `net8.0-windows`).
- `LinuxPlatformServices` — Linux paths and best-effort capabilities.
- `MacPlatformServices` — macOS paths and capabilities (some features gracefully gated).

The interface abstracts the application-data/config root, monitor brightness, serial-port name enumeration, and the single-instance guard — replacing the former Windows Registry/`%AppData%` source, WMI brightness, `COMx`-only enumeration, and named-Mutex single-instance mechanisms. Application and UI code depend only on the interface (Dependency Inversion), and unavailable capabilities degrade to a no-op rather than breaking startup.

## Related Files

- [Architecture](architecture.md) - System architecture and component relationships
- [Settings](settings.md) - Settings system and properties
- [PGN Protocol](pgn-protocol.md) - Communication protocol specification
- [Transition Map](../MIGRATION_DOCS/TRANSITION_MAP.md) - Old→new file-by-file migration mapping
