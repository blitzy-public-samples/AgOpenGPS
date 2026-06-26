// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the field Flag viewer / editor.
//
// 1:1 behavioural-parity reimplementation of the WinForms Forms/Field/FormFlags
// (FormFlags.cs + FormFlags.Designer.cs). A MODELESS viewer/editor for the flags placed on the
// field map: it shows the currently-selected flag's Latitude / Longitude / Easting / Northing /
// Heading / ID and its free-text notes, lets the operator cycle the selection North (next) /
// South (previous), edit the notes, delete the selected flag, and displays the live distance from
// the GPS pivot to that flag (refreshed once a second, reproducing the WinForms timer1
// Interval = 1000 ms). It is shown with Show(owner) (modeless) — including from FormEnterFlagView.
//
// FRAMEWORK CONVERSIONS (each tagged // [XPLAT] below):
//   * System.Windows.Forms.Form           -> Avalonia.Controls.Window
//   * RepeatButton.MouseDown (north/south) -> RepeatButton.Click (the partner markup declares
//                                             btnNorth/btnSouth as Avalonia RepeatButtons, which
//                                             raise Click repeatedly while held — the same
//                                             hold-to-repeat behaviour as the WinForms RepeatButton)
//   * System.Windows.Forms.Timer           -> Avalonia.Threading.DispatcherTimer
//   * Form.Load                            -> Window.OnOpened (the codebase-standard Load analogue;
//                                             see Inputs/FormKeyboard + Settings/FormGraphXTEView)
//   * TextBox.Leave                        -> TextBox.LostFocus
//   * TextBox.KeyPress (KeyPressEventArgs) -> TextBox.KeyDown (KeyEventArgs)
//   * TextBox.Click + ShowKeyboard(this)   -> PointerPressed + modal FormKeyboard (Views/Inputs)
//   * every numeric .ToString()            -> .ToString(CultureInfo.InvariantCulture) — a §0.6.5
//                                             cross-cutting data-integrity requirement (the WinForms
//                                             original omitted culture, which corrupts on a
//                                             comma-decimal locale)
//
// DEPENDENCY INJECTION (replaces the WinForms "mf" FormGPS god-object — NO FormGPS reference):
//   The WinForms form reached through a "private readonly FormGPS mf" back-reference for the flag
//   collection (mf.flagPts), the shared 1-based selection index (mf.flagNumberPicked), the live GPS
//   fix (mf.pn), the unit flag (mf.isMetric), the on-screen-keyboard flag (mf.isKeyboardOn) and two
//   operations (mf.FileSaveFlags / mf.DeleteSelectedFlag). Per the AAP guidance/scan-loop decoupling
//   (§0.6.1, §0.3.2) this view takes NO FormGPS reference; the composition root injects a small
//   IFlagsViewContext exposing exactly those members. The flag collection and the selection index
//   are SHARED LIVE with the OpenGL render loop, so every read and write goes THROUGH the injected
//   context (never a private copy) — mutating context.FlagNumberPicked here is immediately visible
//   to the renderer, exactly as writing mf.flagNumberPicked was.
//
// No WinForms, no System.Drawing, no OpenTK, no GMap, no ColorPicker types are referenced.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AgOpenGPS.Core.Translations;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] The live state and operations the <see cref="FormFlagsView"/> reads from and writes
    /// back to, replacing the WinForms form's direct reads/writes of the FormGPS "mf" god-object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The composition root (the post-migration owner that the OpenGL render loop also reads — e.g. a
    /// flags/field-state service) implements this interface over the <b>live, shared</b> flag state.
    /// <see cref="FlagPts"/> must return the same <see cref="List{T}"/> instance the renderer draws,
    /// and <see cref="FlagNumberPicked"/> must read and write the same selection field the renderer
    /// honours, so the operator's North/South cycling and deletions performed in this dialog are seen
    /// immediately on the map. Defining the seam here (rather than importing a not-yet-existing
    /// service type) keeps the view decoupled from FormGPS and independently compilable.
    /// </para>
    /// </remarks>
    public interface IFlagsViewContext
    {
        /// <summary>
        /// The live, shared flag collection (WinForms <c>mf.flagPts</c>). Returns the same list
        /// instance the render loop draws — not a copy.
        /// </summary>
        List<CFlag> FlagPts { get; }

        /// <summary>
        /// The shared 1-based index of the selected flag (WinForms <c>mf.flagNumberPicked</c>);
        /// <c>0</c> means "none selected". Read/write: writes must be visible to the render loop, so
        /// the implementation backs this with the same field the renderer reads.
        /// </summary>
        int FlagNumberPicked { get; set; }

        /// <summary>
        /// The live GPS state (WinForms <c>mf.pn</c>); only <see cref="CNMEA.fix"/> is read here, to
        /// compute the live pivot-to-flag distance each timer tick.
        /// </summary>
        CNMEA Pn { get; }

        /// <summary>Whether distances are shown in metric units (WinForms <c>mf.isMetric</c>).</summary>
        bool IsMetric { get; }

        /// <summary>
        /// Whether the on-screen keyboard is enabled (WinForms <c>mf.isKeyboardOn</c>); when set,
        /// tapping the notes field opens the modal <see cref="FormKeyboard"/> rather than editing inline.
        /// </summary>
        bool IsKeyboardOn { get; }

        /// <summary>Persist the flag collection to disk (WinForms <c>mf.FileSaveFlags()</c>).</summary>
        void FileSaveFlags();

        /// <summary>
        /// Delete the currently-selected flag from the shared collection and renumber the remaining
        /// flags (WinForms <c>mf.DeleteSelectedFlag()</c>).
        /// </summary>
        void DeleteSelectedFlag();
    }

    /// <summary>
    /// [XPLAT] Code-behind for the field Flag viewer / editor — the behaviour half of
    /// <c>FormFlagsView.axaml</c>. Faithful Avalonia reimplementation of the WinForms
    /// <c>FormFlags</c>; an imperative dialog (no view-model, no data binding) that addresses every
    /// control by its original WinForms <c>x:Name</c> and wires every handler in the constructor.
    /// </summary>
    public partial class FormFlagsView : Window
    {
        /// <summary>
        /// The live, shared flag state / operations (replaces the WinForms <c>mf</c> FormGPS
        /// back-reference). All flag reads and writes go through this so they remain visible to the
        /// OpenGL render loop.
        /// </summary>
        private readonly IFlagsViewContext _context;

        /// <summary>
        /// [XPLAT] Replaces the WinForms <c>timer1</c> (System.Windows.Forms.Timer, Interval = 1000 ms,
        /// Enabled in the designer). Drives the once-a-second live distance read-out. Started in
        /// <see cref="OnOpened"/> and stopped in <see cref="OnClosed"/> so it only runs while the
        /// window is open.
        /// </summary>
        private readonly DispatcherTimer _timer;

        /// <summary>
        /// Initializes the dialog over the supplied live flag context. Parity with the WinForms
        /// <c>FormFlags(Form callingForm)</c> constructor, except the FormGPS "mf" reference is
        /// replaced by the injected <see cref="IFlagsViewContext"/>; there is intentionally no
        /// parameterless constructor (the view cannot function without the live flag state).
        /// </summary>
        /// <param name="context">The live, shared flag state and operations (must not be null).</param>
        public FormFlagsView(IFlagsViewContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));

            InitializeComponent();

            // [XPLAT] WinForms ctor: this.Text = gStr.gsFlags; labelDistanceToFlag.Text = gStr.gsDistanceToFlag.
            // The .axaml carries only the design-time caption / label; the localised runtime strings are
            // assigned here.
            Title = gStr.gsFlags;
            labelDistanceToFlag.Text = gStr.gsDistanceToFlag;

            // [XPLAT] The markup wires no handlers (only x:Name); attach them here by name. btnNorth /
            // btnSouth are RepeatButtons (Click fires repeatedly while held — the hold-to-repeat parity
            // of the WinForms RepeatButton MouseDown handlers).
            btnNorth.Click += BtnNorth_Click;
            btnSouth.Click += BtnSouth_Click;
            btnExit.Click += BtnExit_Click;
            btnDeleteFlag.Click += BtnDeleteFlag_Click;

            // [XPLAT] WinForms tboxFlagNotes Leave / KeyPress / Click -> LostFocus / KeyDown / PointerPressed.
            tboxFlagNotes.LostFocus += TboxFlagNotes_LostFocus;
            tboxFlagNotes.KeyDown += TboxFlagNotes_KeyDown;
            tboxFlagNotes.PointerPressed += TboxFlagNotes_PointerPressed;

            // [XPLAT] WinForms timer1 (Interval = 1000 ms). Constructed here; armed in OnOpened.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _timer.Tick += Timer_Tick;
        }

        /// <summary>
        /// [XPLAT] WinForms <c>FormFlags_Load</c>. Avalonia raises <c>Opened</c> once the window is
        /// shown (the cross-platform analogue of the WinForms Load event), so the load-time work lives
        /// here: refresh the labels and arm the distance timer. The WinForms off-screen reposition
        /// (ScreenHelper) is dropped in favour of the markup's <c>WindowStartupLocation="CenterOwner"</c>.
        /// </summary>
        /// <param name="e">The event data passed to the base implementation.</param>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            UpdateLabels();
            _timer.Start();
        }

        /// <summary>
        /// [XPLAT] Stop the distance timer when the window closes. The WinForms form disabled the timer
        /// in btnExit and relied on form disposal otherwise; a DispatcherTimer is not auto-disposed, so
        /// this is the authoritative cleanup for every close path (btnExit, delete-to-empty, or the user
        /// closing the window), following the repo's standard <see cref="Window.OnClosed"/> timer-cleanup
        /// convention.
        /// </summary>
        /// <param name="e">The event data passed to the base implementation.</param>
        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            base.OnClosed(e);
        }

        /// <summary>
        /// [XPLAT] Port of the WinForms <c>UpdateLabels()</c>: write the selected flag's fields into the
        /// read-out labels. Every numeric value is formatted with <see cref="CultureInfo.InvariantCulture"/>
        /// (the original omitted culture — a §0.6.5 data-integrity requirement).
        /// </summary>
        private void UpdateLabels()
        {
            // [XPLAT] Guard the degenerate (no-flag / none-selected) state. The WinForms original clamped
            // only the upper bound and would index flagPts[-1] when the collection is empty or nothing is
            // selected; every real caller keeps FlagNumberPicked >= 1 when flags exist, so this guard is
            // observably identical for all valid states and merely avoids an index crash in the empty state.
            if (_context.FlagPts.Count == 0 || _context.FlagNumberPicked < 1)
            {
                return;
            }

            // WinForms: if (flagNumberPicked > flagPts.Count) flagNumberPicked = flagPts.Count;
            if (_context.FlagNumberPicked > _context.FlagPts.Count)
            {
                _context.FlagNumberPicked = _context.FlagPts.Count;
            }

            CFlag f = _context.FlagPts[_context.FlagNumberPicked - 1];
            lblLatStart.Text = f.latitude.ToString(CultureInfo.InvariantCulture);
            lblLonStart.Text = f.longitude.ToString(CultureInfo.InvariantCulture);
            lblEasting.Text = f.easting.ToString("N2", CultureInfo.InvariantCulture);
            lblNorthing.Text = f.northing.ToString("N2", CultureInfo.InvariantCulture);
            lblHeading.Text = glm.toDegrees(f.heading).ToString("N2", CultureInfo.InvariantCulture);
            lblFlagSelected.Text = f.ID.ToString(CultureInfo.InvariantCulture);
            tboxFlagNotes.Text = f.notes;
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnNorth_MouseDown</c>: advance the selection to the next flag, wrapping
        /// from the last back to the first.
        /// </summary>
        /// <param name="sender">The north RepeatButton (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void BtnNorth_Click(object sender, RoutedEventArgs e)
        {
            _context.FlagNumberPicked++;
            if (_context.FlagNumberPicked > _context.FlagPts.Count)
            {
                _context.FlagNumberPicked = 1;
            }
            UpdateLabels();
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnSouth_MouseDown</c>: move the selection to the previous flag, wrapping
        /// from the first back to the last.
        /// </summary>
        /// <param name="sender">The south RepeatButton (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void BtnSouth_Click(object sender, RoutedEventArgs e)
        {
            _context.FlagNumberPicked--;
            if (_context.FlagNumberPicked < 1)
            {
                _context.FlagNumberPicked = _context.FlagPts.Count;
            }
            UpdateLabels();
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnExit_Click</c>: stop the timer, clear the selection, persist the flags
        /// and close.
        /// </summary>
        /// <param name="sender">The exit button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            _context.FlagNumberPicked = 0;
            _context.FileSaveFlags();
            Close();
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnDeleteFlag_Click</c>: delete the selected flag; if none remain, persist
        /// and close; otherwise keep the selection in range and refresh.
        /// </summary>
        /// <param name="sender">The delete button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void BtnDeleteFlag_Click(object sender, RoutedEventArgs e)
        {
            int flag = _context.FlagNumberPicked;
            if (_context.FlagPts.Count > 0)
            {
                _context.DeleteSelectedFlag();
            }
            if (_context.FlagPts.Count == 0)
            {
                _context.FileSaveFlags();
                Close();
                return;
            }

            // WinForms: if (flag > flagPts.Count) flagNumberPicked = flagPts.Count; else flagNumberPicked = flag;
            _context.FlagNumberPicked = (flag > _context.FlagPts.Count) ? _context.FlagPts.Count : flag;
            UpdateLabels();
        }

        /// <summary>
        /// [XPLAT] WinForms <c>tboxFlagNotes_Leave</c>: write the edited notes back to the selected flag
        /// (so the change is visible to the renderer and persisted on save).
        /// </summary>
        /// <param name="sender">The notes text box (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void TboxFlagNotes_LostFocus(object sender, RoutedEventArgs e)
        {
            // WinForms guarded only on flagNumberPicked > 0; the extra count check keeps the index safe if
            // the selection went stale, with no behavioural difference when the selection is valid.
            if (_context.FlagNumberPicked > 0 && _context.FlagPts.Count >= _context.FlagNumberPicked)
            {
                _context.FlagPts[_context.FlagNumberPicked - 1].notes = tboxFlagNotes.Text ?? string.Empty;
            }
        }

        /// <summary>
        /// [XPLAT] WinForms <c>tboxFlagNotes_KeyPress</c> (suppressed the carriage return). In Avalonia,
        /// handle <see cref="Key.Enter"/> on KeyDown and mark it handled so Enter does not insert a
        /// newline into the (multi-line) notes box.
        /// </summary>
        /// <param name="sender">The notes text box (unused).</param>
        /// <param name="e">The key event; <see cref="RoutedEventArgs.Handled"/> is set for Enter.</param>
        private void TboxFlagNotes_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// [XPLAT] WinForms <c>tboxFlagNotes_Click</c>: when the on-screen keyboard is enabled, open the
        /// modal <see cref="FormKeyboard"/> seeded with the current notes instead of editing inline; on
        /// accept, store the result and write it back to the selected flag (mirroring the WinForms Leave
        /// path), then move focus to the exit button (as the WinForms handler did). <c>async void</c> is
        /// the idiomatic Avalonia event-handler shape for awaiting a modal dialog.
        /// </summary>
        /// <param name="sender">The notes text box (unused).</param>
        /// <param name="e">The pointer event data (unused).</param>
        private async void TboxFlagNotes_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (!_context.IsKeyboardOn)
            {
                return;
            }

            // [XPLAT] ((TextBox)sender).ShowKeyboard(this) -> modal FormKeyboard. ShowDialog<string> returns
            // the entered string, or null when cancelled (FormKeyboard closes with Close(null)). string
            // (not string?) keeps this warning-clean under the project's disabled nullable context.
            FormKeyboard keyboard = new FormKeyboard(tboxFlagNotes.Text ?? string.Empty);
            string result = await keyboard.ShowDialog<string>(this);
            if (result != null)
            {
                tboxFlagNotes.Text = result;

                // Persist to the selected flag's notes, as the WinForms Leave handler did.
                if (_context.FlagNumberPicked > 0 && _context.FlagPts.Count >= _context.FlagNumberPicked)
                {
                    _context.FlagPts[_context.FlagNumberPicked - 1].notes = result;
                }
            }

            btnExit.Focus();
        }

        /// <summary>
        /// [XPLAT] Port of the WinForms <c>timer1_Tick</c> (once a second): when flags exist, normalize
        /// the selection (0 or out-of-range -> last flag) and update the live pivot-to-flag distance
        /// read-out, then refresh the field labels.
        /// </summary>
        /// <param name="sender">The dispatcher timer (unused).</param>
        /// <param name="e">The tick event data (unused).</param>
        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_context.FlagPts.Count > 0)
            {
                if (_context.FlagNumberPicked == 0)
                {
                    _context.FlagNumberPicked = _context.FlagPts.Count;
                }
                if (_context.FlagNumberPicked > _context.FlagPts.Count)
                {
                    _context.FlagNumberPicked = _context.FlagPts.Count;
                }

                CFlag f = _context.FlagPts[_context.FlagNumberPicked - 1];
                double distance = glm.Distance(_context.Pn.fix, f.easting, f.northing);

                // [XPLAT] Format with InvariantCulture (per §0.6.5). NOTE: the original imperial branch also
                // appends " m" (not " ft") — reproduced verbatim, including that quirk.
                if (_context.IsMetric)
                {
                    lblDistanceToFlag.Text = distance.ToString("N2", CultureInfo.InvariantCulture) + " m";
                }
                else
                {
                    lblDistanceToFlag.Text = (distance * glm.m2ft).ToString("N2", CultureInfo.InvariantCulture) + " m";
                }

                UpdateLabels();
            }
        }
    }
}
