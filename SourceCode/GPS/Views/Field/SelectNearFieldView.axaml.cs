// [XPLAT] migrated from net48/WinForms (Forms/Field/FormFieldExisting.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views.Field
{
    /// <summary>
    /// [XPLAT] Code-behind for the "select near field" picker, bound to
    /// <see cref="AgOpenGPS.Core.ViewModels.SelectNearFieldViewModel"/>. The .axaml declares no event
    /// handlers, so this code-behind is the thin Avalonia adapter required for XAML compilation; the
    /// nearby-field list (<c>lstFields</c>) is populated through bindings on the Core view-model.
    /// </summary>
    /// <remarks>
    /// No fabricated calls to not-yet-projected FormGPS services are made here — distance/area ranking
    /// is owned by the bound view-model. See MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </remarks>
    public partial class SelectNearFieldView : Window
    {
        public SelectNearFieldView()
        {
            InitializeComponent();
        }
    }
}
