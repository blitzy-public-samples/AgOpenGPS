// [XPLAT] migrated from net48/WinForms (Forms/Field/FormFieldExisting.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views.Field
{
    /// <summary>
    /// [XPLAT] Code-behind for the "create field from an existing field" dialog, bound to
    /// <see cref="AgOpenGPS.Core.ViewModels.CreateFromExistingFieldViewModel"/>. The .axaml declares no
    /// event handlers, so this code-behind is the thin Avalonia adapter required for XAML compilation;
    /// the dialog is driven by data bindings to the Core view-model.
    /// </summary>
    /// <remarks>
    /// No fabricated calls to not-yet-projected FormGPS field IO are made here — cloning an existing
    /// field is owned by the bound view-model. See MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </remarks>
    public partial class CreateFromExistingFieldView : Window
    {
        public CreateFromExistingFieldView()
        {
            InitializeComponent();

            // [XPLAT] Cancel is intentionally NOT a view-model command (parity with the WinForms
            // FormFieldExisting Cancel button / DialogResult.Cancel). Its Click is wired imperatively
            // to Close() the dialog (returns the default/null result = "no field chosen"); the
            // OK/Create path commits and closes through the bound view-model + SelectFieldPanelPresenter.
            // Combined with IsCancel="True" in the .axaml, this also restores the Escape-key dismissal
            // the WinForms CancelButton provided.
            btnCancel.Click += (_, _) => Close();
        }
    }
}
