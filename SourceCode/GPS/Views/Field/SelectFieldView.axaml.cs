// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views.Field
{
    public partial class SelectFieldView : Window
    {
        public SelectFieldView()
        {
            InitializeComponent();
            btnCancel.Click += OnCancelClick;
        }

        // [XPLAT] kiosk-safe dismissal: the field-table VM exposes no cancel command;
        // closing leaves ActiveField unchanged (selection only commits via SelectFieldCommand).
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
