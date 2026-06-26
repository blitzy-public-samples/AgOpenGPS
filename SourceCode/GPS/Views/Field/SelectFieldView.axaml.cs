// [XPLAT] migrated from net48/WinForms (Forms/Field/FormFieldDir.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views.Field
{
    /// <summary>
    /// [XPLAT] Code-behind for the "select existing field" picker, bound to
    /// <see cref="AgOpenGPS.Core.ViewModels.SelectFieldViewModel"/>. The .axaml declares no event
    /// handlers, so this code-behind is the thin Avalonia adapter required for XAML compilation; the
    /// view's controls are driven by data bindings to the Core view-model.
    /// </summary>
    /// <remarks>
    /// No fabricated calls to not-yet-projected FormGPS services are made here — field enumeration and
    /// selection are owned by the bound view-model. See MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </remarks>
    public partial class SelectFieldView : Window
    {
        public SelectFieldView()
        {
            InitializeComponent();
        }
    }
}
