// [XPLAT] migrated from net48/WinForms (Forms/Field/FormAgShareDownloader.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the AgShare field-download browser (lists fields available from the AgShare account for download into the local fields folder) — a 1:1 visual-parity reimplementation of the WinForms
    /// <c>FormAgShareDownloader</c> (FormAgShareDownloader.cs + FormAgShareDownloader.Designer.cs). Imperative dialog with NO DataContext,
    /// x:DataType or MVVM bindings; controls are addressed by x:Name. The .axaml declares no event
    /// handlers, so this code-behind is the thin Avalonia adapter required for XAML compilation.
    /// </summary>
    /// <remarks>
    /// Listing remote fields and downloading them run through the AgShare network client and the local field IO. That behaviour depends on the FormGPS god-object / services that are not projected
    /// at this checkpoint, so it is host-owned — see MIGRATION_DOCS/TRANSITION_MAP.md. No fabricated
    /// calls to not-yet-existing services are made here.
    /// </remarks>
    public partial class FormAgShareDownloaderView : Window
    {
        public FormAgShareDownloaderView()
        {
            InitializeComponent();
        }
    }
}
