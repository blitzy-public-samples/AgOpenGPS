// [XPLAT] migrated from net48/WinForms Forms/FormYes.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ModSim.Views
{
    /// <summary>
    /// Modal confirmation dialog (a single message line plus an "Ok" button), reimplemented as an
    /// Avalonia <see cref="Window"/> replacing the deleted WinForms <c>FormYes : Form</c>.
    /// </summary>
    /// <remarks>
    /// [XPLAT] Behaviour ported 1:1 from the WinForms original: the constructor assigns the message
    /// text and widens the window proportionally to the message length
    /// (<c>Width = message.Length * 15 + 180</c>), and clicking "Ok" closes the dialog with a
    /// <c>true</c> result — the cross-platform equivalent of the WinForms
    /// <c>btnSerialOK.DialogResult = DialogResult.OK</c>, observable via <c>ShowDialog&lt;bool&gt;</c>.
    /// </remarks>
    public partial class FormYesView : Window
    {
        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormYesView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog and displays <paramref name="messageStr"/>, mirroring the WinForms
        /// <c>FormYes(string messageStr)</c> constructor including its proportional width override.
        /// </summary>
        /// <param name="messageStr">The confirmation message to display.</param>
        public FormYesView(string messageStr)
            : this()
        {
            lblMessage2.Text = messageStr;

            // [XPLAT] preserve the original proportional sizing: Width = len * 15 + 180.
            int messWidth = messageStr?.Length ?? 0;
            Width = (messWidth * 15) + 180;
        }

        // [XPLAT] WinForms DialogResult.OK -> close the dialog returning true for ShowDialog<bool>.
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close(true);
        }
    }
}
