// [XPLAT] migrated from net48/WinForms (Forms/Guidance/FormQuickAB.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the Quick-AB track-creation dialog — a 1:1 visual-parity
    /// reimplementation of the WinForms <c>FormQuickAB</c> (FormQuickAB.cs +
    /// FormQuickAB.Designer.cs). The dialog is a multi-panel wizard (Choose → AB-line / A+ / Curve →
    /// Name) for creating a new guidance track.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings); controls are addressed by
    /// x:Name. The .axaml declares no event handlers, so this code-behind is the thin Avalonia
    /// adapter required for XAML compilation, plus the self-contained dialog-dismissal behaviour.
    /// <para>
    /// The cancel buttons on each panel (<c>btnCancelChoose</c>, <c>btnCancel_ABLine</c>,
    /// <c>btnCancel_APlus</c>, <c>btnCancel_Curve</c>) are wired to <see cref="Window.Close()"/>,
    /// reproducing the dismissal half of the original cancel handlers. The track-creation flow itself
    /// — recording the A/B points, building the curve, computing the heading, appending the new
    /// <c>CTrk</c> to <c>mf.trk.gArr</c> and the associated <c>mf.curve</c> / <c>mf.ABLine</c> state
    /// — operates on the FormGPS god-object and is host-owned at this checkpoint (the host can hook
    /// <see cref="Window.Closing"/> for the pre-close state cleanup the original performed). No
    /// fabricated calls to not-yet-projected services are made here — see
    /// MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </para>
    /// </remarks>
    public partial class FormQuickABView : Window
    {
        public FormQuickABView()
        {
            InitializeComponent();

            // [XPLAT] Per-panel cancel buttons — wire the self-contained dismissal (the original also
            // cleared mf.curve/ABLine working state first; that cleanup is host-owned).
            btnCancelChoose.Click += OnCloseClick;
            btnCancel_ABLine.Click += OnCloseClick;
            btnCancel_APlus.Click += OnCloseClick;
            btnCancel_Curve.Click += OnCloseClick;
        }

        // [XPLAT] Shared cancel handler: dismiss the wizard.
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
