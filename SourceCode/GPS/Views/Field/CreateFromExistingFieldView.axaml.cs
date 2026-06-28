// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views.Field
{
    public partial class CreateFromExistingFieldView : Window
    {
        public CreateFromExistingFieldView()
        {
            InitializeComponent();
            btnCancel.Click += OnCancelClick;
        }

        // [XPLAT] kiosk-safe dismissal: the create-from-existing VM exposes no cancel command;
        // closing leaves ActiveField unchanged (creation only commits via SelectFieldCommand).
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
