// [XPLAT] migrated from net48/WinForms (Forms/Field/FormFieldExisting.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the 'create field from existing field' picker (lists nearby fields with distance and area to copy from) — a 1:1 visual-parity reimplementation of the WinForms
    /// <c>FormFieldExisting</c> (FormFieldExisting.cs + FormFieldExisting.Designer.cs). Imperative dialog with NO DataContext,
    /// x:DataType or MVVM bindings; controls are addressed by x:Name. The .axaml declares no event
    /// handlers, so this code-behind is the thin Avalonia adapter required for XAML compilation.
    /// </summary>
    /// <remarks>
    /// Populating the list with per-field distance / area requires the live GPS position and local plane (mf.pn, mf.AppModel), and creating the field runs through the FormGPS field IO. That behaviour depends on the FormGPS god-object / services that are not projected
    /// at this checkpoint, so it is host-owned — see MIGRATION_DOCS/TRANSITION_MAP.md. No fabricated
    /// calls to not-yet-existing services are made here.
    /// </remarks>
    public partial class FormFieldExistingView : Window
    {
        public FormFieldExistingView()
        {
            InitializeComponent();
        }
    }
}
