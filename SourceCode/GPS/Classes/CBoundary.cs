// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Collections.Generic;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Interfaces;

namespace AgOpenGPS
{
    public partial class CBoundary
    {
        // [XPLAT] Decoupled from the WinForms FormGPS host god-object (the former
        // `private readonly FormGPS mf;` + `CBoundary(FormGPS _f)`). This root file of the
        // `partial class CBoundary` is the decoupling HUB for the sibling partials CFence.cs,
        // CHead.cs and CTurn.cs, which previously reached back into the WinForms shell through `mf`.
        // The collaborators those partials consume are now constructor-injected (or, for the cyclic
        // guidance peers, wired post-construct via SetABLine/SetYouTurn/SetFieldData) and used
        // live-by-reference, mirroring the established CVehicle(ApplicationModel, ...) /
        // CTool(ApplicationModel, ...) decoupling. No FormGPS reference remains, so the
        // boundary/headland/turn logic is portable across Windows, Linux and macOS. The boundary
        // area/point math, the headland geometry, the isDriveThru/headland gating and the field-file
        // round-trip are FROZEN — outputs are unchanged. Field-by-field decoupling map (was mf.X,
        // consumed by the named sibling partials):
        //   appModel       - shared AgOpenGPS.Core runtime model. Supplies the per-fix scan-loop state
        //                    the partials read live-by-reference (was mf.avgSpeed, and the GPS-layer
        //                    fix state mf.pivotAxlePos / mf.toolPivotPos / mf.isReverse /
        //                    mf.isHeadlandDistanceOn / mf.p_239); the fix/position pipeline writes it,
        //                    so values stay identical per fix (NOT recomputed or copied here).
        //   section        - per-section state array (was mf.section; CFence/CHead): edge points and
        //                    the headland-area / look-on flags.
        //   tool           - implement/tool config (was mf.tool; CFence/CHead): numOfSections, the
        //                    look-ahead distances, rpWidth and the headland side flags.
        //   vehicle        - vehicle config (was mf.vehicle; CHead): hydraulic-lift enable.
        //   mc             - module-comm state (was mf.mc; CFence): isOutOfBounds.
        //   sounds         - cross-platform audio service (was mf.sounds; CHead): hyd-lift / headland cues.
        //   ABLine         - AB-line guidance (was mf.ABLine; CFence/CTurn): draw line width. Cyclic peer.
        //   yt             - youturn (was mf.yt; CTurn): uturnDistanceFromBoundary. Cyclic peer.
        //   fd             - field data (was mf.fd; CTurn): UpdateFieldBoundaryGUIAreas(). Cyclic peer.
        //   errorPresenter - cross-platform UI message sink (was mf.TimedMessageBox; CTurn) routed
        //                    through IErrorPresenter.PresentTimedMessage.
        //   (mf.bnd, a self-reference to this CBoundary, is replaced by `this` inside the partials.)
        // The cyclic guidance peers (ABLine / yt / fd) form mutual references with CBoundary
        // (CBoundary<->CYouTurn<->CABLine and CBoundary<->CFieldData), so they are wired AFTER
        // construction via the SetX methods below — introducing no DI container and no new abstraction
        // (AAP §0.7.1). See MIGRATION_DOCS/TRANSITION_MAP.md.
        private readonly ApplicationModel appModel;
        private readonly CSection[] section;
        private readonly CTool tool;
        private readonly CModuleComm mc;
        private readonly CSound sounds;
        private readonly CVehicle vehicle;
        private readonly IErrorPresenter errorPresenter;

        // [XPLAT] Cyclic guidance collaborators wired post-construct (see SetABLine / SetYouTurn /
        // SetFieldData). Non-readonly because they are assigned after the constructor runs, exactly
        // like CVehicle's late-wired _bnd / _tool.
        private CABLine ABLine;
        private CYouTurn yt;
        private CFieldData fd;

        public List<CBoundaryList> bndList = new List<CBoundaryList>();

        //constructor
        // [XPLAT] Replaces CBoundary(FormGPS _f). The non-cyclic collaborators and the shared
        // ApplicationModel + IErrorPresenter are injected here; the cyclic guidance peers are wired
        // afterwards via SetABLine/SetYouTurn/SetFieldData. The initialization body is unchanged.
        public CBoundary(
            ApplicationModel appModel,
            CSection[] section,
            CTool tool,
            CModuleComm mc,
            CSound sounds,
            CVehicle vehicle,
            IErrorPresenter errorPresenter)
        {
            this.appModel = appModel;
            this.section = section;
            this.tool = tool;
            this.mc = mc;
            this.sounds = sounds;
            this.vehicle = vehicle;
            this.errorPresenter = errorPresenter;

            turnSelected = 0;
            isHeadlandOn = false;
            isSectionControlledByHeadland = Properties.ToolSettings.Default.setHeadland_isSectionControlled;
        }

        // [XPLAT] Post-construct wiring for the cyclic AB-line guidance peer (was mf.ABLine).
        public void SetABLine(CABLine ABLine)
        {
            this.ABLine = ABLine;
        }

        // [XPLAT] Post-construct wiring for the cyclic youturn peer (was mf.yt).
        public void SetYouTurn(CYouTurn yt)
        {
            this.yt = yt;
        }

        // [XPLAT] Post-construct wiring for the cyclic field-data peer (was mf.fd).
        public void SetFieldData(CFieldData fd)
        {
            this.fd = fd;
        }
    }
}
