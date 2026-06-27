// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// FieldIoService is the cross-platform extraction of the AgOpenGPS field load / save / create / export
// ORCHESTRATOR. It was lifted (behavior FROZEN) out of the former WinForms god-object FormGPS partial class:
//   * SourceCode/GPS/Forms/SaveOpen.Designer.cs — FileOpenField() and the per-aspect File*Save/File*Create/
//     File*Load delegators, the KML exporters, the ISOXML v3/v4 exporters, the TryLoad/TryRun loader
//     wrappers, and the AgShare auto-download pre-step.
//   * SourceCode/GPS/Forms/Controls.Designer.cs — the field-save half of FileSaveEverythingBeforeClosingField()
//     (the FileSave* / Export* batch); the section-master/contour/AgShare-upload/ISOBUS/view-close half stays
//     with the other services and the view and is surfaced here as composition-root hooks.
//
// Decoupling notes (AAP §0.6.1 — state separation; §0.7.1 — parity):
//   * NO `mf`/FormGPS back-reference — every former direct FormGPS member access is a constructor-injected
//     collaborator, named IDENTICALLY to the original FormGPS member so the ported bodies are a verbatim copy
//     of the frozen orchestration; only view touches and the file-picker are altered, each tagged // [XPLAT].
//   * NO `using System.Windows.Forms;`, NO `using System.Drawing;`, NO `using AgOpenGPS.Forms;`. Every former
//     WinForms touch — OpenFileDialog, FormDialog.Show, TimedMessageBox, JobNew(), PanelsAndOGLSize()/SetButtons()/
//     SetZoom()/oglZoom.Refresh(), FixTramModeButton(), panelDrag.Visible — is replaced by an injected delegate
//     (a value-returning picker / a wired callback / a message sink) or a public event the Avalonia view subscribes
//     to. SetButtons()'s WinForms body is NOT ported; it is a pure view concern folded into OnFieldOpened.
//
// Behavior contract (AAP §0.2.2, §0.7.1 — FROZEN, parity-tested by FieldRoundTripTests / IsoXmlEquivalenceTests):
//   * THIS SERVICE IS A PURE ORCHESTRATOR. It does NOT reimplement any byte-level field-file format: every read
//     and write is delegated verbatim to the frozen AgOpenGPS.IO handlers (the format owners) so load→save remains
//     byte-identical. ISOXML export is delegated to AgOpenGPS.Protocols.ISOBUS.ISO11783_TaskFile.Export.
//   * KML is written here directly (it is an export-only text format, not delegated to IO/). All numeric output
//     uses CultureInfo.InvariantCulture so a comma-decimal locale on Linux/macOS cannot corrupt lat/lon.
//   * Path.Combine is used for every path (never a hard-coded '\\') for cross-OS correctness.
//   * The Sections.txt shoelace area triple-product and the FileOpenField load ORDER are copied verbatim.
//   * AgShare auto-download stays gated by AgShareEnabled && AgShareAutoLoad (disabled by default — security
//     posture frozen, AAP §0.7.2).
//
// Collaboration (AAP key insight): FieldIoService is the only extracted service that is fully INDEPENDENT of the
// other four at compile time — it orchestrates the frozen IO/ISOBUS format owners. The single cross-link is
// recalcFieldBounds (a delegate the composition root wires to RenderCoordinator.CalculateMinMax), injected so this
// service never takes a hard reference to the render layer.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using AgLibrary.Logging;
using AgOpenGPS.Core;                 // ApplicationCore, WorldGrid (shared Core runtime + grid model)
using AgOpenGPS.Core.AgShare;         // AgShareClient (injected; consumed by AgShareDownloader)
using AgOpenGPS.Core.Models;          // Wgs84, GeoCoord, GeoDir, LocalPlane, ApplicationModel
using AgOpenGPS.Core.Streamers;       // BingMapStreamer
using AgOpenGPS.Core.Translations;    // gStr (localized strings forwarded to the injected message sinks)
using AgOpenGPS.IO;                   // FieldPlaneFiles, SectionsFiles, ContourFiles, FlagsFiles, BoundaryFiles,
                                      // HeadlandFiles, HeadlinesFiles, TramFiles, RecPathFiles, TrackFiles, ElevationFiles
using AgOpenGPS.Protocols.ISOBUS;     // ISO11783_TaskFile
// NOTE: the domain collaborator types (CNMEA, CFieldData, CPatches, CContour, CFlag, CBoundary, CBoundaryList,
// CTram, CRecordedPath, CRecPathPt, CTrack, CTrk, CHeadLine, CHeadPath, CABLine, AgShareDownloader, RegistrySettings,
// vec2/vec3) and the `Properties.Settings` singleton live in the parent `AgOpenGPS` namespace. Because this file is
// declared in the nested `AgOpenGPS.Services` namespace, the C# enclosing-namespace lookup resolves them without an
// explicit `using AgOpenGPS;` (a directive Roslyn's IDE0005 would flag as redundant — and which would fail the
// Release TreatWarningsAsErrors build). This mirrors the sibling SectionService.cs / PgnDispatcher.cs.

namespace AgOpenGPS.Services
{
    /// <summary>
    /// [XPLAT] Cross-platform field load/save/create/export orchestrator, extracted (behavior FROZEN) from the
    /// WinForms FormGPS partial <c>SaveOpen.Designer.cs</c>. All former <c>mf</c> members are constructor-injected;
    /// no <c>System.Windows.Forms</c>, <c>System.Drawing</c>, or <c>AgOpenGPS.Forms</c> type is referenced.
    /// Byte-level field-file I/O is delegated to the frozen <see cref="AgOpenGPS.IO"/> handlers and ISOXML export to
    /// <see cref="ISO11783_TaskFile"/>; only the (export-only) KML text format is written directly here, always with
    /// <see cref="CultureInfo.InvariantCulture"/>. View touches are surfaced as injected delegates and public events.
    /// </summary>
    public class FieldIoService
    {
        // ============================================================================================
        //  Injected collaborators — named IDENTICALLY to the original FormGPS members so the extracted
        //  method bodies below are a verbatim copy of the frozen orchestration logic.
        // ============================================================================================

        /// <summary>GPS/NMEA + local-plane definition (was mf.pn).</summary>
        private readonly CNMEA pn;

        /// <summary>Field statistics: worked area, user distance, outer-boundary area, description (was mf.fd).</summary>
        private readonly CFieldData fd;

        /// <summary>Section coverage triangle strips; patch lists persisted to Sections.txt (was mf.triStrip).</summary>
        private readonly List<CPatches> triStrip;

        /// <summary>Contour strip/point lists persisted to Contour.txt (was mf.ct).</summary>
        private readonly CContour ct;

        /// <summary>Field flags persisted to Flags.txt and exported to KML (was mf.flagPts).</summary>
        private readonly List<CFlag> flagPts;

        /// <summary>Field boundaries; also the source for headland save and turn-line rebuild (was mf.bnd).</summary>
        private readonly CBoundary bnd;

        /// <summary>Tramline outer/inner/line geometry persisted to Tram.txt (was mf.tram).</summary>
        private readonly CTram tram;

        /// <summary>Recorded drive path persisted to RecPath.txt (was mf.recPath).</summary>
        private readonly CRecordedPath recPath;

        /// <summary>Guidance tracks (AB/curve) persisted to TrackLines.txt and exported to KML/ISOXML (was mf.trk).</summary>
        private readonly CTrack trk;

        /// <summary>Headland guidance lines persisted to Headlines.txt (was mf.hdl — type <c>CHeadLine</c>).</summary>
        private readonly CHeadLine hdl;

        /// <summary>AB-line geometry; supplies <c>abLength</c> for the KML AB-line extents (was mf.ABLine).</summary>
        private readonly CABLine ABLine;

        /// <summary>World grid hosting the loaded Bing background tile map (was mf.worldGrid).</summary>
        private readonly WorldGrid worldGrid;

        /// <summary>Elevation grid text accumulated by the scan loop; flushed to Elevation.txt (was mf.sbGrid).</summary>
        private readonly StringBuilder sbGrid;

        /// <summary>AgShare cloud client consumed by <see cref="AgShareDownloader"/> (was mf.agShareClient).</summary>
        private readonly AgShareClient agShareClient;

        /// <summary>
        /// Shared AgOpenGPS.Core runtime hub. Source of <c>CurrentLatLon</c> (new-field start fix), <c>LocalPlane</c>
        /// (geo↔WGS84 conversion for KML/ISOXML) and <c>isJobStarted</c> (the field-job gate). The SAME instance the
        /// extracted scan-loop/section services and the Avalonia view-models read and write (was mf.AppCore).
        /// </summary>
        private readonly ApplicationCore appCore;

        // ---- Injected delegates (replace value-returning / wired WinForms calls) ---------------------

        /// <summary>[XPLAT] File picker — replaces the WinForms <c>OpenFileDialog</c>. Receives the fields root
        /// directory and returns the chosen <c>Field.txt</c> path, or the literal <c>"Cancel"</c> when dismissed.</summary>
        private readonly Func<string, string> pickFieldFile;

        /// <summary>[XPLAT] Domain+UI new-field reset — replaces <c>JobNew()</c>; the composition root performs the
        /// domain reset (and the view its part).</summary>
        private readonly Action onJobNew;

        /// <summary>[XPLAT] Recomputes field min/max bounds after boundaries load — wired by the composition root to
        /// <c>RenderCoordinator.CalculateMinMax</c> so this service keeps no hard reference to the render layer.</summary>
        private readonly Action recalcFieldBounds;

        /// <summary>[XPLAT] Field-job gate — wired to <c>() =&gt; appCore.AppModel.isJobStarted</c>. Surfaced as the
        /// private <see cref="isJobStarted"/> property so the ported bodies stay verbatim.</summary>
        private readonly Func<bool> isJobStartedFunc;

        /// <summary>[XPLAT] Error/warning sink — replaces <c>FormDialog.Show(title, message, severity)</c>. The third
        /// argument is <c>true</c> for an error (was <c>DialogSeverity.Error</c>) and <c>false</c> for a warning
        /// (was <c>DialogSeverity.Warning</c>).</summary>
        private readonly Action<string, string, bool> reportFileProblem;

        /// <summary>[XPLAT] Transient/timed message sink — replaces <c>TimedMessageBox(timeoutMs, title, message)</c>.</summary>
        private readonly Action<int, string, string> showTimedMessage;

        /// <summary>[XPLAT] Optional pre-save hook for <see cref="FileSaveEverythingBeforeClosingField"/>: the
        /// section-master/contour stop, mapping-off, Easy-Drive restore and AgShare upload START are owned by other
        /// services and the view; the composition root attaches them here. May be <c>null</c>.</summary>
        private readonly Action onBeforeCloseField;

        /// <summary>[XPLAT] Optional post-save hook for <see cref="FileSaveEverythingBeforeClosingField"/>: the
        /// ISOBUS field-name reset and the WinForms close (JobClose/panel disable) are view/cross-service concerns;
        /// the composition root attaches them here. May be <c>null</c>.</summary>
        private readonly Action onAfterCloseField;

        // ---- Public events (replace WinForms view refreshes; the Avalonia view subscribes) -----------

        /// <summary>[XPLAT] Raised once at the end of <see cref="FileOpenField"/> — replaces the WinForms
        /// <c>PanelsAndOGLSize(); SetButtons(); SetZoom(); oglZoom.Refresh();</c> final-refresh block.</summary>
        public event Action OnFieldOpened;

        /// <summary>[XPLAT] Raised after tram data loads — replaces the WinForms <c>FixTramModeButton()</c> call.</summary>
        public event Action OnTramModeChanged;

        /// <summary>[XPLAT] Raised after a recorded path loads — replaces the WinForms <c>panelDrag.Visible = …</c>
        /// touch; the argument is <c>recPath.recList.Count &gt; 0</c>.</summary>
        public event Action<bool> OnRecPathLoaded;

        // ============================================================================================
        //  Persisted-buffer state (ported verbatim from SaveOpen.Designer.cs)
        // ============================================================================================

        /// <summary>Holds pending section patches to persist (was FormGPS.patchSaveList).</summary>
        public List<List<vec3>> patchSaveList = new List<List<vec3>>();

        /// <summary>Holds pending contour patches to persist (was FormGPS.contourSaveList).</summary>
        public List<List<vec3>> contourSaveList = new List<List<vec3>>();

        /// <summary>
        /// [XPLAT] The active field's directory name (folder under the fields root), formerly the FormGPS
        /// <c>currentFieldDirectory</c> member. Mutable state set by <see cref="FileOpenField"/> and read by every
        /// save/create/export method.
        /// </summary>
        public string currentFieldDirectory { get; set; } = string.Empty;

        // ---- Wrapper properties keeping the ported bodies verbatim -----------------------------------

        /// <summary>[XPLAT] Field-job gate, evaluated through the injected delegate so call sites read as the
        /// original <c>isJobStarted</c>.</summary>
        private bool isJobStarted => isJobStartedFunc();

        /// <summary>[XPLAT] Shared application model, surfaced as <c>AppModel</c> exactly as FormGPS exposed it
        /// (<c>AppModel =&gt; AppCore.AppModel</c>) so the ported bodies read unchanged.</summary>
        private ApplicationModel AppModel => appCore.AppModel;

        /// <summary>
        /// Constructs the field-I/O orchestrator with its injected collaborators and view-decoupling delegates. No
        /// <c>FormGPS</c> reference is taken; the composition root wires the shared domain instances (so this service,
        /// the extracted scan-loop/section services and the Avalonia view-models all operate over the SAME state) and
        /// supplies the delegates that stand in for the former WinForms calls.
        /// </summary>
        /// <param name="pn">GPS/NMEA + local-plane (was mf.pn).</param>
        /// <param name="fd">Field statistics (was mf.fd).</param>
        /// <param name="triStrip">Section coverage triangle strips (was mf.triStrip).</param>
        /// <param name="ct">Contour strips/points (was mf.ct).</param>
        /// <param name="flagPts">Field flags (was mf.flagPts).</param>
        /// <param name="bnd">Field boundaries (was mf.bnd).</param>
        /// <param name="tram">Tramline geometry (was mf.tram).</param>
        /// <param name="recPath">Recorded path (was mf.recPath).</param>
        /// <param name="trk">Guidance tracks (was mf.trk).</param>
        /// <param name="hdl">Headland guidance lines (was mf.hdl).</param>
        /// <param name="ABLine">AB-line geometry; supplies abLength for KML (was mf.ABLine).</param>
        /// <param name="worldGrid">World grid hosting the Bing background map (was mf.worldGrid).</param>
        /// <param name="sbGrid">Elevation grid text accumulator (was mf.sbGrid).</param>
        /// <param name="appCore">Shared Core runtime hub: CurrentLatLon, LocalPlane, isJobStarted (was mf.AppCore).</param>
        /// <param name="agShareClient">AgShare cloud client (was mf.agShareClient).</param>
        /// <param name="pickFieldFile">[XPLAT] Field.txt picker (was OpenFileDialog); returns the path or "Cancel".</param>
        /// <param name="onJobNew">[XPLAT] New-field reset (was JobNew()).</param>
        /// <param name="recalcFieldBounds">[XPLAT] Field-bounds recompute (was CalculateMinMax(); wired to RenderCoordinator).</param>
        /// <param name="isJobStarted">[XPLAT] Field-job gate (wired to () =&gt; appCore.AppModel.isJobStarted).</param>
        /// <param name="reportFileProblem">[XPLAT] Error/warning sink (title, message, isError) — was FormDialog.Show.</param>
        /// <param name="showTimedMessage">[XPLAT] Timed message sink (timeoutMs, title, message) — was TimedMessageBox.</param>
        /// <param name="onBeforeCloseField">[XPLAT] Optional pre-save hook for close orchestration (may be null).</param>
        /// <param name="onAfterCloseField">[XPLAT] Optional post-save hook for close orchestration (may be null).</param>
        public FieldIoService(
            CNMEA pn,
            CFieldData fd,
            List<CPatches> triStrip,
            CContour ct,
            List<CFlag> flagPts,
            CBoundary bnd,
            CTram tram,
            CRecordedPath recPath,
            CTrack trk,
            CHeadLine hdl,
            CABLine ABLine,
            WorldGrid worldGrid,
            StringBuilder sbGrid,
            ApplicationCore appCore,
            AgShareClient agShareClient,
            Func<string, string> pickFieldFile,
            Action onJobNew,
            Action recalcFieldBounds,
            Func<bool> isJobStarted,
            Action<string, string, bool> reportFileProblem,
            Action<int, string, string> showTimedMessage,
            Action onBeforeCloseField = null,
            Action onAfterCloseField = null)
        {
            this.pn = pn ?? throw new ArgumentNullException(nameof(pn));
            this.fd = fd ?? throw new ArgumentNullException(nameof(fd));
            this.triStrip = triStrip ?? throw new ArgumentNullException(nameof(triStrip));
            this.ct = ct ?? throw new ArgumentNullException(nameof(ct));
            this.flagPts = flagPts ?? throw new ArgumentNullException(nameof(flagPts));
            this.bnd = bnd ?? throw new ArgumentNullException(nameof(bnd));
            this.tram = tram ?? throw new ArgumentNullException(nameof(tram));
            this.recPath = recPath ?? throw new ArgumentNullException(nameof(recPath));
            this.trk = trk ?? throw new ArgumentNullException(nameof(trk));
            this.hdl = hdl ?? throw new ArgumentNullException(nameof(hdl));
            this.ABLine = ABLine ?? throw new ArgumentNullException(nameof(ABLine));
            this.worldGrid = worldGrid ?? throw new ArgumentNullException(nameof(worldGrid));
            this.sbGrid = sbGrid ?? throw new ArgumentNullException(nameof(sbGrid));
            this.appCore = appCore ?? throw new ArgumentNullException(nameof(appCore));
            this.agShareClient = agShareClient ?? throw new ArgumentNullException(nameof(agShareClient));
            this.pickFieldFile = pickFieldFile ?? throw new ArgumentNullException(nameof(pickFieldFile));
            this.onJobNew = onJobNew ?? throw new ArgumentNullException(nameof(onJobNew));
            this.recalcFieldBounds = recalcFieldBounds ?? throw new ArgumentNullException(nameof(recalcFieldBounds));
            this.isJobStartedFunc = isJobStarted ?? throw new ArgumentNullException(nameof(isJobStarted));
            this.reportFileProblem = reportFileProblem ?? throw new ArgumentNullException(nameof(reportFileProblem));
            this.showTimedMessage = showTimedMessage ?? throw new ArgumentNullException(nameof(showTimedMessage));
            // Optional hooks — may legitimately be null (a minimal host or a parity test can omit them).
            this.onBeforeCloseField = onBeforeCloseField;
            this.onAfterCloseField = onAfterCloseField;
        }

        // Returns field directory; creates it when ensureExists is true.
        private string GetFieldDir(bool ensureExists = false)
        {
            var dir = Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory);
            if (ensureExists && !string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        // Open a field with required precheck and per-file loaders.
        public async Task FileOpenField(string openType)
        {
            if (isJobStarted)
            {
                _ = FileSaveEverythingBeforeClosingField();
            }

            // Resolve Field.txt path
            string fileAndDirectory = "Cancel";
            if (!string.IsNullOrEmpty(openType) && openType.Contains("Field.txt"))
            {
                fileAndDirectory = openType;
                openType = "Load";
            }

            switch (openType)
            {
                case "Resume":
                    fileAndDirectory = Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory, "Field.txt");
                    if (!File.Exists(fileAndDirectory)) fileAndDirectory = "Cancel";
                    break;

                case "Open":
                    // [XPLAT] WinForms OpenFileDialog (Filter "Field files (Field.txt)|Field.txt", InitialDirectory =
                    // fields root) → injected picker returning the chosen path or the literal "Cancel".
                    fileAndDirectory = pickFieldFile(RegistrySettings.fieldsDirectory);
                    break;
            }

            if (fileAndDirectory == "Cancel") return;

            // If AgShare is active and this field has a cloud ID, fetch the latest version first
            await TryLoadFromAgShareAsync(fileAndDirectory);

            // Set current field directory
            currentFieldDirectory = new DirectoryInfo(Path.GetDirectoryName(fileAndDirectory)).Name;
            var dir = GetFieldDir(false);

            // --- Load all field data ---
            if (!TryLoad("Field.txt", LoadCriticality.Required, () => FieldPlaneFiles.LoadOrigin(dir), out Wgs84 origin))
            {
                return;
            }
            pn.DefineLocalPlane(origin, true);

            // [XPLAT] JobNew() reset the domain + WinForms UI; the domain reset stays (composition root), the UI part
            // is the view's. Wired via the injected onJobNew delegate.
            onJobNew();

            // --- Tracks ---
            FileLoadTracks();

            // --- Sections into triStrip + area ---
            if (TryLoad("Sections.txt", LoadCriticality.Optional, () => SectionsFiles.Load(dir), out var sections))
            {
                fd.workedAreaTotal = 0;
                fd.distanceUser = 0;
                if (triStrip != null && triStrip.Count > 0 && triStrip[0] != null)
                {
                    triStrip[0].patchList = new List<List<vec3>>();
                    foreach (var patch in sections)
                    {
                        triStrip[0].triangleList = new List<vec3>(patch);
                        triStrip[0].patchList.Add(triStrip[0].triangleList);

                        int verts = patch.Count - 2;
                        if (verts >= 2)
                        {
                            for (int j = 1; j < verts; j++)
                            {
                                // FROZEN shoelace triple-product (parity-tested) — copied verbatim.
                                double temp = patch[j].easting * (patch[j + 1].northing - patch[j + 2].northing)
                                            + patch[j + 1].easting * (patch[j + 2].northing - patch[j].northing)
                                            + patch[j + 2].easting * (patch[j].northing - patch[j + 1].northing);
                                fd.workedAreaTotal += Math.Abs(temp * 0.5);
                            }
                        }
                    }
                }
            }

            // --- Contour ---
            if (TryLoad("Contour.txt", LoadCriticality.Optional, () => ContourFiles.Load(dir), out var contours))
            {
                ct.stripList.Clear();
                foreach (var patch in contours)
                {
                    ct.ptList = new List<vec3>(patch);
                    ct.stripList.Add(ct.ptList);
                }
            }

            // --- Flags ---
            if (TryLoad("Flags.txt", LoadCriticality.Optional, () => FlagsFiles.Load(dir), out var flags))
            {
                flagPts.Clear();
                flagPts.AddRange(flags);
            }

            // --- Boundaries ---
            if (TryLoad("Boundary.txt", LoadCriticality.Optional, () => BoundaryFiles.Load(dir), out var boundaries))
            {
                bnd.bndList.Clear();
                bnd.bndList.AddRange(boundaries);
                // [XPLAT] CalculateMinMax() lives in RenderCoordinator (render/bounds helper); injected as a delegate
                // by the composition root so this service keeps no hard reference to the render layer.
                recalcFieldBounds();
                bnd.BuildTurnLines();
            }

            // --- Headlands ---
            TryRun("Headland.txt", LoadCriticality.Optional, () => HeadlandFiles.AttachLoad(dir, boundaries));

            // --- Tram ---
            if (TryLoad("Tram.txt", LoadCriticality.Optional, () => TramFiles.Load(dir), out var tramData))
            {
                tram.tramBndOuterArr.Clear();
                tram.tramBndOuterArr.AddRange(tramData.Outer);
                tram.tramBndInnerArr.Clear();
                tram.tramBndInnerArr.AddRange(tramData.Inner);
                tram.tramList.Clear();
                tram.tramList.AddRange(tramData.Lines);
                // [XPLAT] was FixTramModeButton() — a WinForms button-visual refresh; raised as an event.
                OnTramModeChanged?.Invoke();
            }

            // --- RecPath ---
            if (TryLoad("RecPath.txt", LoadCriticality.Optional, () => RecPathFiles.Load(dir), out var recPathList))
            {
                recPath.recList.Clear();
                recPath.recList.AddRange(recPathList);
            }

            // ---BingMap ---
            DirectoryInfo fieldDirectoryInfo = new DirectoryInfo(dir);
            BingMapStreamer bingMapStreamer = new BingMapStreamer();
            worldGrid.BingMap = bingMapStreamer.TryRead(fieldDirectoryInfo);

            // optional — Elevation.txt is probed for presence/integrity only; the loaded data is discarded
            // ([XPLAT] was `out var elevation` (unused) in net48; a discard avoids an unused-local analyzer warning).
            TryLoad("Elevation.txt", LoadCriticality.Optional, () => ElevationFiles.Load(dir), out _);

            // --- Final UI refresh ---
            // [XPLAT] was PanelsAndOGLSize(); SetButtons(); SetZoom(); oglZoom.Refresh(); — all WinForms view work,
            // collapsed into a single event the Avalonia view subscribes to.
            OnFieldOpened?.Invoke();
        }

        // Save HeadLines.
        public void FileSaveHeadLines()
        {
            HeadlinesFiles.Save(GetFieldDir(true), hdl.tracksArr);
        }

        // Load HeadLines (no message if missing).
        public void FileLoadHeadLines()
        {
            var dir = GetFieldDir();
            List<CHeadPath> headlines;
            if (!TryLoad("Headlines.txt", LoadCriticality.Optional, () => HeadlinesFiles.Load(dir), out headlines))
            {
                headlines = new List<CHeadPath>();
            }

            hdl.tracksArr?.Clear();
            hdl.tracksArr.AddRange(headlines);
            hdl.idx = -1;
        }

        // Save tracks
        public void FileSaveTracks()
        {
            TrackFiles.Save(GetFieldDir(true), trk.gArr);
        }

        // Load tracks
        public void FileLoadTracks()
        {
            var dir = GetFieldDir();

            List<CTrk> tracks;
            if (!TryLoad("TrackLines.txt", LoadCriticality.Optional, () => TrackFiles.Load(dir), out tracks))
            {
                tracks = new List<CTrk>();
            }

            trk.gArr?.Clear();
            trk.gArr.AddRange(tracks);
            trk.idx = -1;
        }

        // Create Field.txt for a new field session.
        public void FileCreateField()
        {
            if (!isJobStarted)
            {
                // [XPLAT] was FormDialog.Show(gStr.gsFieldNotOpen, gStr.gsCreateNewField, DialogSeverity.Error).
                reportFileProblem(gStr.gsFieldNotOpen, gStr.gsCreateNewField, true);
                return;
            }

            var dir = GetFieldDir(true);
            var startFix = new Wgs84(AppModel.CurrentLatLon.Latitude, AppModel.CurrentLatLon.Longitude);
            FieldPlaneFiles.Save(dir, DateTime.Now, startFix);
        }

        public void FileCreateElevation()
        {
            var dir = GetFieldDir(true);
            var startFix = new Wgs84(AppModel.CurrentLatLon.Latitude, AppModel.CurrentLatLon.Longitude);
            ElevationFiles.CreateHeader(dir, DateTime.Now, startFix);
        }

        public void FileSaveElevation()
        {
            var dir = GetFieldDir(true);
            ElevationFiles.Append(dir, sbGrid.ToString());
            sbGrid.Clear();
        }

        // Append pending sections.
        public void FileSaveSections()
        {
            if (patchSaveList.Count > 0)
            {
                SectionsFiles.Append(GetFieldDir(true), patchSaveList);
                patchSaveList.Clear();
            }
        }

        // Create empty Sections.txt.
        public void FileCreateSections()
        {
            SectionsFiles.CreateEmpty(GetFieldDir(true));
        }

        // Create Boundary.txt header.
        public void FileCreateBoundary()
        {
            var dir = GetFieldDir(true);
            BoundaryFiles.CreateEmpty(dir);
        }

        // Create Flags.txt header and zero count.
        public void FileCreateFlags()
        {
            FlagsFiles.Save(GetFieldDir(true), new List<CFlag>(0));
        }

        // Create Contour.txt with header.
        public void FileCreateContour()
        {
            ContourFiles.CreateFile(GetFieldDir(true));
        }

        // Append pending contour patches.
        public void FileSaveContour()
        {
            if (contourSaveList.Count > 0)
            {
                ContourFiles.Append(GetFieldDir(true), contourSaveList);
                contourSaveList.Clear();
            }
        }

        // Save boundaries.
        public void FileSaveBoundary()
        {
            BoundaryFiles.Save(GetFieldDir(true), bnd.bndList);
        }

        // Save tram data.
        public void FileSaveTram()
        {
            TramFiles.Save(GetFieldDir(true), tram.tramBndOuterArr, tram.tramBndInnerArr, tram.tramList);
        }

        // Save headland(s).
        public void FileSaveHeadland()
        {
            HeadlandFiles.Save(GetFieldDir(true), bnd.bndList);
        }

        // Create RecPath header + zero count.
        public void FileCreateRecPath()
        {
            var dir = GetFieldDir(true);
            RecPathFiles.CreateEmpty(dir);
        }

        // Save recorded path.
        public void FileSaveRecPath(string name = "RecPath.Txt")
        {
            RecPathFiles.Save(GetFieldDir(true), recPath.recList, name);
        }

        // Load RecPath.txt (message if missing).
        public void FileLoadRecPath()
        {
            var dir = GetFieldDir();

            List<CRecPathPt> rec;
            if (!TryLoad("RecPath.txt", LoadCriticality.Optional, () => RecPathFiles.Load(dir), out rec))
            {
                rec = new List<CRecPathPt>();
            }

            recPath.recList.Clear();
            recPath.recList.AddRange(rec);
            // [XPLAT] was panelDrag.Visible = recPath.recList.Count > 0; — a WinForms control toggle, raised as an event.
            OnRecPathLoaded?.Invoke(recPath.recList.Count > 0);
        }

        // Save flags.
        public void FileSaveFlags()
        {
            FlagsFiles.Save(GetFieldDir(true), flagPts);
        }

        // Export one flag to KML using WGS84 from LocalPlane.
        public void FileSaveSingleFlagKML2(int flagNumber)
        {
            Wgs84 latLon = AppModel.LocalPlane.ConvertGeoCoordToWgs84(flagPts[flagNumber - 1].GeoCoord);

            string directoryName = Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory);
            if ((directoryName.Length > 0) && (!Directory.Exists(directoryName)))
            {
                Directory.CreateDirectory(directoryName);
            }

            string myFileName = "Flag.kml";
            using (StreamWriter writer = new StreamWriter(Path.Combine(directoryName, myFileName)))
            {
                writer.WriteLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
                writer.WriteLine(@"<kml xmlns=""http://www.opengis.net/kml/2.2"">");
                writer.WriteLine(@"<Document>");
                writer.WriteLine(@"  <Placemark>");
                writer.WriteLine(@"<Style> <IconStyle>");
                if (flagPts[flagNumber - 1].color == 0)
                {
                    writer.WriteLine(@"<color>ff4400ff</color>");
                }
                if (flagPts[flagNumber - 1].color == 1)
                {
                    writer.WriteLine(@"<color>ff44ff00</color>");
                }
                if (flagPts[flagNumber - 1].color == 2)
                {
                    writer.WriteLine(@"<color>ff44ffff</color>");
                }
                writer.WriteLine(@"</IconStyle> </Style>");
                writer.WriteLine(@" <name> " + flagNumber.ToString(CultureInfo.InvariantCulture) + @"</name>");
                writer.WriteLine(@"<Point><coordinates> "
                    + latLon.Longitude.ToString(CultureInfo.InvariantCulture) + ","
                    + latLon.Latitude.ToString(CultureInfo.InvariantCulture) + ",0"
                    + @"</coordinates> </Point> ");
                writer.WriteLine(@"  </Placemark>");
                writer.WriteLine(@"</Document>");
                writer.WriteLine(@"</kml>");
            }
        }

        // Export one flag to KML using stored WGS84.
        public void FileSaveSingleFlagKML(int flagNumber)
        {
            string directoryName = Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory);
            if ((directoryName.Length > 0) && (!Directory.Exists(directoryName)))
            {
                Directory.CreateDirectory(directoryName);
            }

            string myFileName = "Flag.kml";
            using (StreamWriter writer = new StreamWriter(Path.Combine(directoryName, myFileName)))
            {
                writer.WriteLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
                writer.WriteLine(@"<kml xmlns=""http://www.opengis.net/kml/2.2"">");
                writer.WriteLine(@"<Document>");
                writer.WriteLine(@"  <Placemark>");
                writer.WriteLine(@"<Style> <IconStyle>");
                if (flagPts[flagNumber - 1].color == 0)
                {
                    writer.WriteLine(@"<color>ff4400ff</color>");
                }
                if (flagPts[flagNumber - 1].color == 1)
                {
                    writer.WriteLine(@"<color>ff44ff00</color>");
                }
                if (flagPts[flagNumber - 1].color == 2)
                {
                    writer.WriteLine(@"<color>ff44ffff</color>");
                }
                writer.WriteLine(@"</IconStyle> </Style>");
                writer.WriteLine(@" <name> " + flagNumber.ToString(CultureInfo.InvariantCulture) + @"</name>");
                writer.WriteLine(@"<Point><coordinates> " +
                                flagPts[flagNumber - 1].longitude.ToString(CultureInfo.InvariantCulture) + "," + flagPts[flagNumber - 1].latitude.ToString(CultureInfo.InvariantCulture) + ",0" +
                                @"</coordinates> </Point> ");
                writer.WriteLine(@"  </Placemark>");
                writer.WriteLine(@"</Document>");
                writer.WriteLine(@"</kml>");
            }
        }

        // Export current position to KML.
        public void FileMakeKMLFromCurrentPosition(Wgs84 currentLatLon)
        {
            string directoryName = Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory);
            if ((directoryName.Length > 0) && (!Directory.Exists(directoryName)))
            {
                Directory.CreateDirectory(directoryName);
            }

            using (StreamWriter writer = new StreamWriter(Path.Combine(directoryName, "CurrentPosition.kml")))
            {
                writer.WriteLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
                writer.WriteLine(@"<kml xmlns=""http://www.opengis.net/kml/2.2"">");
                writer.WriteLine(@"<Document>");
                writer.WriteLine(@"  <Placemark>");
                writer.WriteLine(@"<Style> <IconStyle>");
                writer.WriteLine(@"<color>ff4400ff</color>");
                writer.WriteLine(@"</IconStyle> </Style>");
                writer.WriteLine(@" <name> Your Current Position </name>");
                writer.WriteLine(@"<Point><coordinates> "
                    + currentLatLon.Longitude.ToString(CultureInfo.InvariantCulture) + ","
                    + currentLatLon.Latitude.ToString(CultureInfo.InvariantCulture) + ",0"
                    + @"</coordinates> </Point> ");
                writer.WriteLine(@"  </Placemark>");
                writer.WriteLine(@"</Document>");
                writer.WriteLine(@"</kml>");
            }
        }

        // Export full field to KML.
        public void ExportFieldAs_KML()
        {
            string directoryName = Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory);
            if ((directoryName.Length > 0) && (!Directory.Exists(directoryName)))
            {
                Directory.CreateDirectory(directoryName);
            }

            string myFileName = "Field.kml";
            XmlTextWriter kml = new XmlTextWriter(Path.Combine(directoryName, myFileName), Encoding.UTF8)
            {
                Formatting = Formatting.Indented,
                Indentation = 3
            };

            kml.WriteStartDocument();
            kml.WriteStartElement("kml", "http://www.opengis.net/kml/2.2");
            kml.WriteStartElement("Document");

            // Description
            kml.WriteStartElement("Folder");
            kml.WriteElementString("name", "Field Stats");
            kml.WriteElementString("description", fd.GetDescription());
            kml.WriteEndElement();

            // Boundaries
            kml.WriteStartElement("Folder");
            kml.WriteElementString("name", "Boundaries");

            for (int i = 0; i < bnd.bndList.Count; i++)
            {
                kml.WriteStartElement("Placemark");
                if (i == 0)
                {
                    kml.WriteElementString("name", currentFieldDirectory);
                }

                kml.WriteStartElement("Style");
                kml.WriteStartElement("LineStyle");
                if (i == 0)
                {
                    kml.WriteElementString("color", "ffdd00dd");
                }
                else
                {
                    kml.WriteElementString("color", "ff4d3ffd");
                }
                kml.WriteElementString("width", "4");
                kml.WriteEndElement();

                kml.WriteStartElement("PolyStyle");
                if (i == 0)
                {
                    kml.WriteElementString("color", "407f3f55");
                }
                else
                {
                    kml.WriteElementString("color", "703f38f1");
                }
                kml.WriteEndElement();
                kml.WriteEndElement();

                kml.WriteStartElement("Polygon");
                kml.WriteElementString("tessellate", "1");
                kml.WriteStartElement("outerBoundaryIs");
                kml.WriteStartElement("LinearRing");

                kml.WriteStartElement("coordinates");
                string bndPts = "";
                if (bnd.bndList[i].fenceLine.Count > 3)
                {
                    bndPts = GetBoundaryPointsLatLon(i);
                }
                kml.WriteRaw(bndPts);
                kml.WriteEndElement();

                kml.WriteEndElement();
                kml.WriteEndElement();
                kml.WriteEndElement();
                kml.WriteEndElement();
            }

            kml.WriteEndElement(); // Boundaries

            // AB lines
            kml.WriteStartElement("Folder");
            kml.WriteElementString("name", "AB_Lines");
            kml.WriteElementString("visibility", "0");

            string linePts = "";

            foreach (CTrk track in trk.gArr)
            {
                kml.WriteStartElement("Placemark");
                kml.WriteElementString("visibility", "0");
                kml.WriteElementString("name", track.name);
                kml.WriteStartElement("Style");
                kml.WriteStartElement("LineStyle");
                kml.WriteElementString("color", "ff0000ff");
                kml.WriteElementString("width", "2");
                kml.WriteEndElement();
                kml.WriteEndElement();

                kml.WriteStartElement("LineString");
                kml.WriteElementString("tessellate", "1");
                kml.WriteStartElement("coordinates");

                GeoCoord pointA = track.ptA.ToGeoCoord();
                GeoDir heading = new GeoDir(track.heading);
                linePts = GetGeoCoordToWgs84_KML(pointA - ABLine.abLength * heading);
                linePts += GetGeoCoordToWgs84_KML(pointA + ABLine.abLength * heading);
                kml.WriteRaw(linePts);

                kml.WriteEndElement();
                kml.WriteEndElement();
                kml.WriteEndElement();
            }
            kml.WriteEndElement(); // AB_Lines

            // Curve lines
            kml.WriteStartElement("Folder");
            kml.WriteElementString("name", "Curve_Lines");
            kml.WriteElementString("visibility", "0");

            for (int i = 0; i < trk.gArr.Count; i++)
            {
                linePts = "";
                kml.WriteStartElement("Placemark");
                kml.WriteElementString("visibility", "0");
                kml.WriteElementString("name", trk.gArr[i].name);

                kml.WriteStartElement("Style");
                kml.WriteStartElement("LineStyle");
                kml.WriteElementString("color", "ff6699ff");
                kml.WriteElementString("width", "2");
                kml.WriteEndElement();
                kml.WriteEndElement();

                kml.WriteStartElement("LineString");
                kml.WriteElementString("tessellate", "1");
                kml.WriteStartElement("coordinates");

                foreach (vec3 v3 in trk.gArr[i].curvePts)
                {
                    linePts += GetGeoCoordToWgs84_KML(v3.ToGeoCoord());
                }
                kml.WriteRaw(linePts);

                kml.WriteEndElement();
                kml.WriteEndElement();

                kml.WriteEndElement();
            }
            kml.WriteEndElement(); // Curve_Lines

            // Recorded path
            kml.WriteStartElement("Folder");
            kml.WriteElementString("name", "Recorded Path");
            kml.WriteElementString("visibility", "1");

            linePts = "";
            kml.WriteStartElement("Placemark");
            kml.WriteElementString("visibility", "1");
            kml.WriteElementString("name", "Path 1");

            kml.WriteStartElement("Style");
            kml.WriteStartElement("LineStyle");
            kml.WriteElementString("color", "ff44ffff");
            kml.WriteElementString("width", "2");
            kml.WriteEndElement();
            kml.WriteEndElement();

            kml.WriteStartElement("LineString");
            kml.WriteElementString("tessellate", "1");
            kml.WriteStartElement("coordinates");

            for (int j = 0; j < recPath.recList.Count; j++)
            {
                linePts += GetGeoCoordToWgs84_KML(recPath.recList[j].AsGeoCoord);
            }
            kml.WriteRaw(linePts);

            kml.WriteEndElement();
            kml.WriteEndElement();

            kml.WriteEndElement(); // Placemark
            kml.WriteEndElement(); // Folder

            // Flags
            kml.WriteStartElement("Folder");
            kml.WriteElementString("name", "Flags");

            for (int i = 0; i < flagPts.Count; i++)
            {
                kml.WriteStartElement("Placemark");
                kml.WriteElementString("name", "Flag_" + i.ToString());

                kml.WriteStartElement("Style");
                kml.WriteStartElement("IconStyle");
                if (flagPts[i].color == 0)
                {
                    kml.WriteElementString("color", "ff4400ff");
                }
                if (flagPts[i].color == 1)
                {
                    kml.WriteElementString("color", "ff44ff00");
                }
                if (flagPts[i].color == 2)
                {
                    kml.WriteElementString("color", "ff44ffff");
                }
                kml.WriteEndElement();
                kml.WriteEndElement();

                kml.WriteElementString("name", ((i + 1).ToString() + " " + flagPts[i].notes));
                kml.WriteStartElement("Point");
                kml.WriteElementString("coordinates", flagPts[i].longitude.ToString(CultureInfo.InvariantCulture) +
                    "," + flagPts[i].latitude.ToString(CultureInfo.InvariantCulture) + ",0");
                kml.WriteEndElement();
                kml.WriteEndElement();
            }
            kml.WriteEndElement(); // Flags

            // Sections
            kml.WriteStartElement("Folder");
            kml.WriteElementString("name", "Sections");

            string secPts = "";
            int cntr = 0;

            for (int j = 0; j < triStrip.Count; j++)
            {
                int patches = triStrip[j].patchList.Count;

                if (patches > 0)
                {
                    foreach (var triList in triStrip[j].patchList)
                    {
                        if (triList.Count > 0)
                        {
                            kml.WriteStartElement("Placemark");
                            kml.WriteElementString("name", "Sections_" + cntr.ToString());
                            cntr++;

                            string collor = "F0" + ((byte)(triList[0].heading)).ToString("X2") +
                                ((byte)(triList[0].northing)).ToString("X2") + ((byte)(triList[0].easting)).ToString("X2");

                            kml.WriteStartElement("Style");
                            kml.WriteStartElement("LineStyle");
                            kml.WriteElementString("color", collor);
                            kml.WriteEndElement();

                            kml.WriteStartElement("PolyStyle");
                            kml.WriteElementString("color", collor);
                            kml.WriteEndElement();
                            kml.WriteEndElement();

                            kml.WriteStartElement("Polygon");
                            kml.WriteElementString("tessellate", "1");
                            kml.WriteStartElement("outerBoundaryIs");
                            kml.WriteStartElement("LinearRing");

                            kml.WriteStartElement("coordinates");
                            secPts = "";
                            for (int i = 1; i < triList.Count; i += 2)
                            {
                                secPts += GetGeoCoordToWgs84_KML(triList[i].ToGeoCoord());
                            }
                            for (int i = triList.Count - 1; i > 1; i -= 2)
                            {
                                secPts += GetGeoCoordToWgs84_KML(triList[i].ToGeoCoord());
                            }
                            secPts += GetGeoCoordToWgs84_KML(triList[1].ToGeoCoord());

                            kml.WriteRaw(secPts);
                            kml.WriteEndElement();

                            kml.WriteEndElement();
                            kml.WriteEndElement();
                            kml.WriteEndElement();

                            kml.WriteEndElement();
                        }
                    }
                }
            }
            kml.WriteEndElement(); // Sections

            // End document
            kml.WriteEndElement();
            kml.WriteEndElement();

            kml.WriteEndDocument();
            kml.Flush();
            kml.Close();
        }

        // Helper to build lat/lon list for one boundary.
        public string GetBoundaryPointsLatLon(int bndNum)
        {
            StringBuilder sb = new StringBuilder();

            foreach (vec3 v3 in bnd.bndList[bndNum].fenceLine)
            {
                sb.Append(GetGeoCoordToWgs84_KML(v3.ToGeoCoord()));
            }
            return sb.ToString();
        }

        // Regenerates an overview KML for all fields that already produced Field.kml.
        // [XPLAT] was `private` in net48 with no in-repo caller; widened to public so the Avalonia composition root
        // can invoke it (and to avoid an unused-private-member analyzer warning under the Release build).
        public void FileUpdateAllFieldsKML()
        {
            string directoryName = RegistrySettings.fieldsDirectory;
            if ((directoryName.Length > 0) && (!Directory.Exists(directoryName)))
            {
                return;
            }

            string myFileName = "AllFields.kml";
            XmlTextWriter kml = new XmlTextWriter(Path.Combine(directoryName, myFileName), Encoding.UTF8)
            {
                Formatting = Formatting.Indented,
                Indentation = 3
            };

            kml.WriteStartDocument();
            kml.WriteStartElement("kml", "http://www.opengis.net/kml/2.2");
            kml.WriteStartElement("Document");

            foreach (string dir in Directory.EnumerateDirectories(directoryName).OrderBy(d => new DirectoryInfo(d).Name).ToArray())
            {
                if (!File.Exists(Path.Combine(dir, "Field.kml")))
                {
                    continue;
                }

                string name = Path.GetFileName(dir);
                kml.WriteStartElement("Folder");
                kml.WriteElementString("name", name);

                var lines = File.ReadAllLines(Path.Combine(dir, "Field.kml"));
                LinkedList<string> linebuffer = new LinkedList<string>();
                for (int i = 3; i < lines.Length - 2; i++)
                {
                    linebuffer.AddLast(lines[i]);
                    if (linebuffer.Count > 2)
                    {
                        kml.WriteRaw("   ");
                        kml.WriteRaw(Environment.NewLine);
                        kml.WriteRaw(linebuffer.First.Value);
                        linebuffer.RemoveFirst();
                    }
                }
                kml.WriteRaw("   ");
                kml.WriteRaw(Environment.NewLine);
                kml.WriteRaw(linebuffer.First.Value);
                linebuffer.RemoveFirst();
                kml.WriteRaw("   ");
                kml.WriteRaw(Environment.NewLine);
                kml.WriteRaw(linebuffer.First.Value);
                kml.WriteRaw(Environment.NewLine);

                kml.WriteEndElement(); // Folder
                kml.WriteComment("End of " + name);
            }

            kml.WriteEndElement(); // Document
            kml.WriteEndElement(); // kml
            kml.WriteEndDocument();
            kml.Flush();
            kml.Close();
        }

        // Formats a GeoCoord as "lon,lat,0 ".
        private string GetGeoCoordToWgs84_KML(GeoCoord geoCoord)
        {
            Wgs84 latLon = AppModel.LocalPlane.ConvertGeoCoordToWgs84(geoCoord);
            return latLon.Longitude.ToString("N7", CultureInfo.InvariantCulture) + ',' +
                   latLon.Latitude.ToString("N7", CultureInfo.InvariantCulture) + ",0 ";
        }

        // Export ISOXML v3.
        public void ExportFieldAs_ISOXMLv3()
        {
            string directoryName = Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory, "zISOXML", "v3");
            if ((directoryName.Length > 0) && (!Directory.Exists(directoryName)))
            {
                Directory.CreateDirectory(directoryName);
            }

            try
            {
                // ISOXML semantics (name ≤ 248 bytes, AB+Curve export limit) are owned by ISO11783_TaskFile.
                ISO11783_TaskFile.Export(
                    directoryName,
                    currentFieldDirectory,
                    (int)(fd.areaOuterBoundary),
                    bnd.bndList,
                    AppModel.LocalPlane,
                    trk,
                    ISO11783_TaskFile.Version.V3);
            }
            catch (Exception e)
            {
                // [XPLAT] was FormDialog.Show("ISOXML Exception ", e.ToString(), DialogSeverity.Error) + Log.EventWriter.
                reportFileProblem("ISOXML Exception ", e.ToString(), true);
                Log.EventWriter("Export field as ISOXML Exception" + e);
            }
        }

        // Export ISOXML v4.
        public void ExportFieldAs_ISOXMLv4()
        {
            string directoryName = Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory, "zISOXML", "v4");
            if ((directoryName.Length > 0) && (!Directory.Exists(directoryName)))
            {
                Directory.CreateDirectory(directoryName);
            }

            try
            {
                ISO11783_TaskFile.Export(
                    directoryName,
                    currentFieldDirectory,
                    (int)(fd.areaOuterBoundary),
                    bnd.bndList,
                    AppModel.LocalPlane,
                    trk,
                    ISO11783_TaskFile.Version.V4);
            }
            catch (Exception e)
            {
                Log.EventWriter("Export Field as ISOXML: " + e.Message);
            }
        }

        // Criticality flag for loader calls.
        private enum LoadCriticality
        {
            Required,
            Optional
        }

        // Runs a loader with return value; logs + user message on failure.
        private bool TryLoad<T>(string fileLabel, LoadCriticality criticality, Func<T> loader, out T result)
        {
            try
            {
                result = loader();
                return true;
            }
            catch (Exception)
            {
                Log.EventWriter($"[Load:{fileLabel}] failed");
                if (criticality == LoadCriticality.Required)
                {
                    // [XPLAT] was FormDialog.Show(gStr.gsFieldFileIsCorrupt, …, DialogSeverity.Error).
                    reportFileProblem(gStr.gsFieldFileIsCorrupt, $"{fileLabel} is required and could not be loaded.", true);
                }
                else
                {
                    // [XPLAT] was FormDialog.Show("Optional file problem", …, DialogSeverity.Warning).
                    reportFileProblem("Optional file problem", $"{fileLabel} is missing or corrupt but Field is Loaded", false);
                }
                result = default(T);
                return false;
            }
        }

        // Downloads the latest cloud version of a field if AgShare is enabled and agshare.txt exists.
        // Overwrites local files so FileOpenField loads the cloud version. Falls back silently on failure.
        private async Task TryLoadFromAgShareAsync(string fieldFilePath)
        {
            // AgShare is disabled by default — security posture frozen (AAP §0.7.2). Both gates must be on.
            if (!Properties.Settings.Default.AgShareEnabled) return;
            if (!Properties.Settings.Default.AgShareAutoLoad) return;

            string fieldDir = Path.GetDirectoryName(fieldFilePath);
            string agshareFile = Path.Combine(fieldDir, "agshare.txt");

            if (!File.Exists(agshareFile)) return;

            string idText = File.ReadAllText(agshareFile).Trim();
            if (!Guid.TryParse(idText, out Guid fieldId)) return;

            var downloader = new AgShareDownloader(agShareClient);
            bool success = await downloader.DownloadAndSaveAsync(fieldId);

            if (success)
                // [XPLAT] was TimedMessageBox(3000, gStr.gsAgShareFieldLoaded, Path.GetFileName(fieldDir)).
                showTimedMessage(3000, gStr.gsAgShareFieldLoaded, Path.GetFileName(fieldDir));
            else
                // [XPLAT] was FormDialog.Show("AgShare", gStr.gsAgShareLoadFailed, DialogSeverity.Warning).
                reportFileProblem("AgShare", gStr.gsAgShareLoadFailed, false);
        }

        // Runs an action (no return); logs + user message on failure.
        private bool TryRun(string fileLabel, LoadCriticality criticality, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                Log.EventWriter($"[Load:{fileLabel}] failed: {ex}");
                if (criticality == LoadCriticality.Required)
                {
                    // [XPLAT] was FormDialog.Show(gStr.gsFieldFileIsCorrupt, …, DialogSeverity.Error).
                    reportFileProblem(gStr.gsFieldFileIsCorrupt, $"{fileLabel} is required and could not be processed.", true);
                }
                else
                {
                    // [XPLAT] was FormDialog.Show("Optional file problem", …, DialogSeverity.Warning).
                    reportFileProblem("Optional file problem", $"{fileLabel} is missing or corrupt; it will be recreated on save.", false);
                }
                return false;
            }
        }

        // Saves the full field (boundary, sections, contour, tracks, KML, ISOXML v4) before the field is closed.
        // [XPLAT] Ported from the field-save half of the net48 Controls.Designer.cs FileSaveEverythingBeforeClosingField:
        // the FileSave*/Export* batch (each delegated to a frozen IO handler / exporter) is reproduced verbatim with its
        // per-operation try/catch + Log.EventWriter. The cross-cutting halves — stopping the contour line, turning the
        // section masters and mapping off, the Easy-Drive profile restore, the AgShare UPLOAD, the ISOBUS field-name
        // reset, and the WinForms close (JobClose/panel disable/title) — are owned by the other services and the view;
        // they are surfaced as the optional onBeforeCloseField / onAfterCloseField hooks the composition root attaches,
        // so this orchestrator stays independent of those services.
        public async Task FileSaveEverythingBeforeClosingField()
        {
            onBeforeCloseField?.Invoke();

            // Save field data with individual exception handling for each operation (frozen behavior).
            await Task.Run(() =>
            {
                try { FileSaveBoundary(); }
                catch (Exception ex) { Log.EventWriter($"CRITICAL: Boundary save failed: {ex}"); throw; }

                try { FileSaveSections(); }
                catch (Exception ex) { Log.EventWriter($"CRITICAL: Sections save failed: {ex}"); throw; }

                try { FileSaveContour(); }
                catch (Exception ex) { Log.EventWriter($"CRITICAL: Contour save failed: {ex}"); throw; }

                try { FileSaveTracks(); }
                catch (Exception ex) { Log.EventWriter($"CRITICAL: Tracks save failed: {ex}"); throw; }

                try { ExportFieldAs_KML(); }
                catch (Exception ex) { Log.EventWriter($"WARNING: KML export failed: {ex}"); }

                //ExportFieldAs_ISOXMLv3(); NOTE: This is very very slow, commented out until we have a field exporter

                try { ExportFieldAs_ISOXMLv4(); }
                catch (Exception ex) { Log.EventWriter($"WARNING: ISOXML export failed: {ex}"); }
            });

            Log.EventWriter("** Field closed **   " + currentFieldDirectory + "   " +
                DateTime.Now.ToString("f", CultureInfo.InvariantCulture));

            onAfterCloseField?.Invoke();
        }




    }
}
