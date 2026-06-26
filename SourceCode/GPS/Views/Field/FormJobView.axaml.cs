// [XPLAT] migrated from net48/WinForms (Forms/Field/FormJob.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the field / job menu (open, new, resume, drive-in, from-existing / KML / ISO-XML, AgShare, close) — a 1:1 visual-parity reimplementation of the WinForms
    /// <c>FormJob</c> (FormJob.cs + FormJob.Designer.cs). Imperative dialog with NO DataContext,
    /// x:DataType or MVVM bindings; controls are addressed by x:Name. The .axaml declares no event
    /// handlers, so this code-behind is the thin Avalonia adapter required for XAML compilation.
    /// </summary>
    /// <remarks>
    /// Each command (open / new / resume / close a field, import from KML / ISO-XML / existing, AgShare operations) is a FormGPS field-lifecycle action. That behaviour depends on the FormGPS god-object / services that are not projected
    /// at this checkpoint, so it is host-owned — see MIGRATION_DOCS/TRANSITION_MAP.md. No fabricated
    /// calls to not-yet-existing services are made here.
    /// </remarks>
    public partial class FormJobView : Window
    {
        public FormJobView()
        {
            InitializeComponent();
        }
    }
}
