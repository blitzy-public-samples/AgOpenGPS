// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Real (non-Null) collaborator adapter for the boundary dialogs (FormBoundaryView / FormBndToolView /
// FormBoundaryPlayerView). Those dialogs declare a local IBoundaryFieldData seam (FormBoundaryPlayerView
// .axaml.cs) standing in for the two FormGPS calls the WinForms boundary editor made after committing a
// boundary set:
//   mf.fd.UpdateFieldBoundaryGUIAreas();  // refresh the boundary-area read-outs
//   mf.CalculateMinMax();                 // recompute the field world extents
// In the migrated build those two responsibilities live on different objects — the boundary-area read-out
// recompute on CFieldData, the world-extent recompute on RenderCoordinator — so this adapter binds the
// single interface onto both. Behaviour is frozen: each member delegates verbatim to the original target.

using System;
using AgOpenGPS.Services; // RenderCoordinator

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Binds <see cref="IBoundaryFieldData"/> onto the live composition-root objects, replacing the
    /// inert Null default so the boundary dialogs recompute areas/extents exactly as the WinForms editor did.
    /// </summary>
    internal sealed class BoundaryFieldDataAdapter : IBoundaryFieldData
    {
        private readonly CFieldData _fd;
        private readonly RenderCoordinator _render;

        public BoundaryFieldDataAdapter(CFieldData fd, RenderCoordinator render)
        {
            _fd = fd ?? throw new ArgumentNullException(nameof(fd));
            _render = render ?? throw new ArgumentNullException(nameof(render));
        }

        /// <summary>[XPLAT] was <c>mf.fd.UpdateFieldBoundaryGUIAreas()</c>.</summary>
        public void UpdateFieldBoundaryGUIAreas() => _fd.UpdateFieldBoundaryGUIAreas();

        /// <summary>[XPLAT] was <c>mf.CalculateMinMax()</c>.</summary>
        public void CalculateMinMax() => _render.CalculateMinMax();
    }
}
