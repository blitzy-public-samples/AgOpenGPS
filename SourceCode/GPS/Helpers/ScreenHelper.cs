// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia;
using Avalonia.Controls;

namespace AgOpenGPS.Helpers
{
    /// <summary>
    /// Helper utilities for determining whether a window (or an arbitrary rectangle) is visible across the
    /// connected monitors. Used by dialogs to recover a window whose persisted position has since moved
    /// off-screen (for example, a monitor that was disconnected or reconfigured).
    /// </summary>
    /// <remarks>
    /// [XPLAT] Cross-platform reimplementation of the former WinForms screen-bounds check; see
    /// MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </remarks>
    public static class ScreenHelper
    {
        /// <summary>
        /// Determines if a rectangle (in physical screen pixels) is fully visible across the combined
        /// working areas of all connected monitors.
        /// </summary>
        /// <remarks>
        /// [XPLAT] Reimplemented on Avalonia's per-window screen API. The former WinForms version relied on
        /// the Windows-only static <c>Screen.AllScreens</c> collection and the GDI+ rectangle type, neither
        /// of which exists on cross-platform .NET. Avalonia exposes screens per top-level via
        /// <c>Window.Screens</c> rather than through a static collection, so the screens instance is
        /// passed in by the caller. The pixel-area semantics are preserved exactly:
        /// the rectangle is "on screen" only when the summed visible area of its intersection with every
        /// monitor's working area is at least its own total area (comparison kept as in the original),
        /// which also correctly accepts a rectangle that legitimately spans two adjacent monitors.
        /// </remarks>
        /// <param name="rectangle">The rectangle to test, in physical screen pixel coordinates.</param>
        /// <param name="screens">
        /// The screens collection from the owning window/top-level (e.g., <c>window.Screens</c>).
        /// </param>
        /// <returns><see langword="true"/> if the rectangle is fully shown across the monitors.</returns>
        public static bool IsOnScreen(PixelRect rectangle, Screens screens)
        {
            // The total area the rectangle must have covered to count as fully visible.
            // A long accumulator is exact for integer pixel counts and avoids int overflow across very
            // large multi-monitor desktops; the original used double, and the comparison result is
            // unchanged because pixel areas are whole numbers.
            long totalPixels = (long)rectangle.Width * rectangle.Height;
            if (totalPixels <= 0)
            {
                // Degenerate / zero-size rectangle (e.g., a window whose frame is not yet realized).
                // Treat it as on-screen so an unmeasured window is never needlessly repositioned.
                return true;
            }

            if (screens?.All == null || screens.All.Count == 0)
            {
                // Screen topology is unavailable (transient null during startup/teardown). Assume the
                // rectangle is visible rather than block a window over a momentary null collection.
                return true;
            }

            long visiblePixels = 0;
            foreach (var screen in screens.All)
            {
                // Intersect the rectangle with each monitor's WORKING area (which excludes the taskbar /
                // dock), preserving the original behavior that used Screen.WorkingArea rather than Bounds.
                PixelRect intersection = rectangle.Intersect(screen.WorkingArea);

                // A non-overlapping PixelRect.Intersect yields an empty (zero-size) rectangle; the "> 0"
                // guards are equivalent to the original "!= 0" test for every real on/off-screen case.
                if (intersection.Width > 0 && intersection.Height > 0)
                {
                    // Tally the visible pixels; summing across monitors lets a window that legitimately
                    // straddles two adjacent screens still be considered fully visible.
                    visiblePixels += (long)intersection.Width * intersection.Height;
                }
            }

            // Fully visible only when the combined visible area covers the rectangle's whole area.
            return visiblePixels >= totalPixels;
        }

        /// <summary>
        /// Convenience overload that tests whether the given window's frame is fully visible across all
        /// monitors, so callers can simply write <c>ScreenHelper.IsOnScreen(this)</c>.
        /// </summary>
        /// <remarks>
        /// [XPLAT] Replaces the former WinForms call pattern <c>ScreenHelper.IsOnScreen(Bounds)</c>.
        /// <c>Window.FrameSize</c> (device-independent pixels, including the window border and title bar)
        /// is the closest analogue to the old <c>Form.Bounds</c>; it is converted to physical pixels with
        /// <c>Window.RenderScaling</c>. When the frame size is not yet available (window not realized) it
        /// falls back to <c>Window.ClientSize</c>.
        /// </remarks>
        /// <param name="window">The window whose on-screen visibility is being tested.</param>
        /// <returns><see langword="true"/> if the window's frame is fully shown across the monitors.</returns>
        public static bool IsOnScreen(Window window)
        {
            if (window == null)
            {
                return true;
            }

            Screens screens = window.Screens;
            if (screens == null)
            {
                return true;
            }

            // Window.Position is the top-left corner in physical screen pixels. FrameSize (in DIPs,
            // including the border/title) best matches the former WinForms Form.Bounds; fall back to
            // ClientSize when the frame has not been measured yet.
            Size sizeDip = window.FrameSize ?? window.ClientSize;
            PixelSize sizePx = PixelSize.FromSize(sizeDip, window.RenderScaling);
            PixelRect rect = new PixelRect(window.Position, sizePx);

            return IsOnScreen(rect, screens);
        }
    }
}
