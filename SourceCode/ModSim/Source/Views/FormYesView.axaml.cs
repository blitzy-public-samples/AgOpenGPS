using Avalonia.Controls;
using Avalonia.Interactivity;

// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
namespace ModSim.Views;

/// <summary>
/// Modal confirmation dialog — a single bold message line above one large "Ok" button —
/// reimplemented as an Avalonia <see cref="Window"/> that replaces the deleted WinForms
/// <c>FormYes : Form</c> of the standalone ModSim simulator.
/// </summary>
/// <remarks>
/// Behaviour is ported 1:1 from the WinForms original:
/// <list type="bullet">
///   <item>
///     The constructor assigns the caller's text to <c>lblMessage2</c> and widens the window
///     proportionally to the message length (<c>Width = messageStr.Length * 15 + 180</c>),
///     reproducing the WinForms <c>FormYes(string)</c> sizing verbatim.
///   </item>
///   <item>
///     Clicking "Ok" closes the dialog with a <see langword="true"/> result — the cross-platform
///     equivalent of the WinForms <c>btnSerialOK.DialogResult = DialogResult.OK</c> — observable
///     through <c>ShowDialog&lt;bool&gt;(owner)</c>.
///   </item>
/// </list>
/// STANDALONE: ModSim owns this dialog outright; it references no other project (no shared core
/// library) and uses no MVVM or data binding. The controls declared in <c>FormYesView.axaml</c>
/// (<c>lblMessage2</c>, <c>btnSerialOK</c>) are addressed imperatively by their generated names.
/// </remarks>
public partial class FormYesView : Window
{
    /// <summary>
    /// Parameterless constructor required by Avalonia's compiled-XAML runtime loader so the
    /// <c>avares://ModSim/Views/FormYesView.axaml</c> resource remains reachable (otherwise the
    /// build emits warning <c>AVLN3001</c>, which the Release configuration treats as an error).
    /// Production code constructs the dialog via <see cref="FormYesView(string)"/>.
    /// </summary>
    public FormYesView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the dialog and displays <paramref name="messageStr"/>, mirroring the WinForms
    /// <c>FormYes(string messageStr)</c> constructor — including its proportional width override.
    /// </summary>
    /// <param name="messageStr">The confirmation message to display in the dialog.</param>
    public FormYesView(string messageStr)
    {
        // [XPLAT] InitializeComponent is emitted by the Avalonia XAML source generator from
        // FormYesView.axaml; it also creates the typed lblMessage2 / btnSerialOK fields.
        InitializeComponent();

        lblMessage2.Text = messageStr;
        Width = messageStr.Length * 15 + 180;   // [XPLAT] verbatim from WinForms FormYes(string)
    }

    /// <summary>
    /// Handles the "Ok" button click (wired via <c>Click="OnOkClick"</c> in the XAML) and closes
    /// the dialog returning <see langword="true"/> — the cross-platform replacement for the WinForms
    /// <c>DialogResult.OK</c> that <c>ShowDialog&lt;bool&gt;</c> surfaces to the caller.
    /// </summary>
    /// <param name="sender">The event source (the "Ok" button); unused.</param>
    /// <param name="e">The routed-event payload; unused.</param>
    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
