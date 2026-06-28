// [XPLAT] migrated from net48/WinForms FormRadioChannel.cs + FormRadioChannel.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using System.Windows.Input;
using AgOpenGPS.Core.ViewModels;

namespace AgIO.Views
{
    /// <summary>
    /// View-model backing <c>FormRadioChannelView.axaml</c>, the small editor dialog that adds or
    /// edits a single <see cref="CRadioChannel"/> record (Id / Name / Frequency / Location). It
    /// replaces the WinForms <c>FormRadioChannel</c> and is opened by <c>FormRadio</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Dialog result (god-object removal).</b> The original dialog took a direct
    /// <c>FormLoop</c> reference (<c>mf</c>) and was driven by WinForms <c>DialogResult</c>. Both
    /// couplings are removed. The edited <see cref="CRadioChannel"/> is returned to <c>FormRadio</c>
    /// through the <see cref="RequestClose"/> event — the hosting window closes the dialog with that
    /// value via <c>ShowDialog&lt;CRadioChannel&gt;</c> — and a cancellation is signalled by the
    /// separate <see cref="RequestCancel"/> event.
    /// </para>
    /// <para>
    /// <b>Stay-open-on-invalid parity.</b> The WinForms OK handler set
    /// <c>DialogResult = DialogResult.None</c> to keep the form open whenever validation failed.
    /// That behaviour is preserved exactly: <see cref="OkCommand"/> raises
    /// <see cref="RequestTimedMessage"/> and returns WITHOUT raising <see cref="RequestClose"/>, so
    /// the dialog stays open until the operator supplies valid input.
    /// </para>
    /// <para>
    /// <b>Culture safety.</b> The identifier is parsed with <see cref="CultureInfo.InvariantCulture"/>
    /// (and <see cref="NumberStyles.Integer"/>), matching the original and guaranteeing that a locale
    /// whose number formatting differs can never change which strings are accepted (AAP §0.6.5).
    /// </para>
    /// <para>
    /// <b>On-screen keyboard.</b> The original gated the touch keyboard on the <c>FormLoop</c>
    /// <c>isKeyboardOn</c> flag, which AgIO hard-coded to <c>true</c>. The equivalent
    /// <see cref="IsKeyboardOn"/> flag is exposed here (defaulting to <c>true</c> for parity) so the
    /// view can decide whether to open the keyboard before tapping a field. Actually presenting the
    /// keyboard is a view concern handled by the <c>Controls/TextBoxExtensions.ShowKeyboardAsync</c>
    /// helper; this view-model never opens a window itself.
    /// </para>
    /// </remarks>
    public class FormRadioChannelViewModel : ViewModel
    {
        // [XPLAT] The Location string the dialog was opened with. The WinForms OK handler only
        // overwrote Channel.Location when BOTH latitude and longitude were supplied; otherwise it
        // left the previous value untouched. Retaining the original here reproduces that exactly.
        private readonly string _originalLocation;

        private string _idText;
        private string _name;
        private string _frequency;
        private string _latText;
        private string _lonText;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormRadioChannelViewModel"/> class for the
        /// supplied channel, seeding the editable fields exactly as the WinForms
        /// <c>FormRadioChannel_Load</c> handler did.
        /// </summary>
        /// <param name="channel">
        /// The channel to edit. A new/empty record (the "add" case) starts with a zero id and blank
        /// fields. A <c>null</c> argument is treated as a new empty channel so the editor can never
        /// fault on construction (nullable reference types are disabled project-wide).
        /// </param>
        public FormRadioChannelViewModel(CRadioChannel channel)
        {
            // Guard a null channel without a nullable annotation (nullable refs disabled project-wide).
            if (channel == null)
            {
                channel = new CRadioChannel();
            }

            // Remember the channel's original Location so OnOk can preserve it when the operator does
            // not supply both latitude and longitude (the WinForms Location-retention behaviour).
            _originalLocation = channel.Location;

            // Seed the editable fields (mirrors FormRadioChannel_Load). The id is formatted with the
            // invariant culture so it round-trips with the invariant-culture parse performed in OnOk.
            _idText = channel.Id.ToString(CultureInfo.InvariantCulture);
            _name = channel.Name ?? string.Empty;
            _frequency = channel.Frequency ?? string.Empty;

            // Split the stored "lat lon" Location into its two fields, guarding a missing/short value
            // exactly as the original (which required at least two space-separated parts).
            _latText = string.Empty;
            _lonText = string.Empty;
            if (!string.IsNullOrEmpty(channel.Location))
            {
                string[] locationArray = channel.Location.Split(' ');
                if (locationArray.Length >= 2)
                {
                    _latText = locationArray[0];
                    _lonText = locationArray[1];
                }
            }

            OkCommand = new RelayCommand(OnOk);
            CancelCommand = new RelayCommand(OnCancel);
        }

        /// <summary>
        /// Gets or sets the text of the identifier field (the former <c>tbId.Text</c>). It is
        /// validated and parsed with <see cref="CultureInfo.InvariantCulture"/> by
        /// <see cref="OkCommand"/>.
        /// </summary>
        public string IdText
        {
            get { return _idText; }
            set
            {
                if (value != _idText)
                {
                    _idText = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the channel name (the former <c>tbName.Text</c>). Must be non-empty for
        /// <see cref="OkCommand"/> to accept the edit.
        /// </summary>
        public string Name
        {
            get { return _name; }
            set
            {
                if (value != _name)
                {
                    _name = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the channel frequency (the former <c>tbFrequency.Text</c>). Must be non-empty
        /// for <see cref="OkCommand"/> to accept the edit.
        /// </summary>
        public string Frequency
        {
            get { return _frequency; }
            set
            {
                if (value != _frequency)
                {
                    _frequency = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the latitude field (the former <c>tbLat.Text</c>). It is combined with
        /// <see cref="LonText"/> into the channel's <c>Location</c> only when both are supplied.
        /// </summary>
        public string LatText
        {
            get { return _latText; }
            set
            {
                if (value != _latText)
                {
                    _latText = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the longitude field (the former <c>tbLon.Text</c>). It is combined with
        /// <see cref="LatText"/> into the channel's <c>Location</c> only when both are supplied.
        /// </summary>
        public string LonText
        {
            get { return _lonText; }
            set
            {
                if (value != _lonText)
                {
                    _lonText = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the touch on-screen keyboard is offered when the
        /// operator taps a text field. Defaults to <c>true</c>, matching the AgIO <c>FormLoop</c>
        /// original which hard-coded <c>isKeyboardOn = true</c>. The view reads this flag before
        /// invoking the keyboard helper; the view-model itself never opens a window.
        /// </summary>
        public bool IsKeyboardOn { get; set; } = true;

        /// <summary>
        /// Gets the command that validates the edits and, when they are valid, accepts the dialog by
        /// raising <see cref="RequestClose"/> with the composed <see cref="CRadioChannel"/>. On
        /// invalid input it raises <see cref="RequestTimedMessage"/> and leaves the dialog open
        /// (the former <c>DialogResult.None</c> behaviour). Bound to the OK button.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the command that dismisses the dialog without saving by raising
        /// <see cref="RequestCancel"/>. Bound to the Cancel button.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Raised when the operator confirms a valid edit. The argument carries the composed
        /// <see cref="CRadioChannel"/>; the hosting window closes the dialog with this value (via
        /// <c>ShowDialog&lt;CRadioChannel&gt;</c>) and <c>FormRadio</c> stores the returned record.
        /// Initialized to a no-op delegate so it is always safe to invoke.
        /// </summary>
        public event Action<CRadioChannel> RequestClose = delegate { };

        /// <summary>
        /// Raised when the operator cancels the dialog; the hosting window closes without a result.
        /// Modelled as a distinct event (rather than a nullable channel argument) because nullable
        /// reference types are disabled project-wide. Initialized to a no-op delegate so it is always
        /// safe to invoke.
        /// </summary>
        public event Action RequestCancel = delegate { };

        /// <summary>
        /// Raised to ask the view to show a transient, self-dismissing notification — the MVVM
        /// replacement for the WinForms <c>FormLoop.TimedMessageBox(milliseconds, title, message)</c>
        /// call. The arguments are the display duration in milliseconds, the title, and the message
        /// body. Initialized to a no-op delegate so it is always safe to invoke.
        /// </summary>
        public event Action<int, string, string> RequestTimedMessage = delegate { };

        /// <summary>
        /// Validates the current edits and either accepts the dialog or reports the first problem,
        /// reproducing the WinForms <c>btnSerialOK_Click</c> handler one-for-one.
        /// </summary>
        /// <remarks>
        /// The three guards (the identifier parses as an integer, the name is non-empty, and the
        /// frequency is non-empty) run in the original order, and each raises
        /// <see cref="RequestTimedMessage"/> with the original title and message and a 2000&#160;ms
        /// duration, then returns WITHOUT raising <see cref="RequestClose"/> — keeping the dialog open
        /// exactly as the former <c>DialogResult.None</c> did. When all guards pass, a new
        /// <see cref="CRadioChannel"/> is composed; its <c>Location</c> starts from the value the
        /// dialog was opened with and is overwritten only when BOTH latitude and longitude are
        /// present, matching the original.
        /// </remarks>
        private void OnOk()
        {
            // Guard 1: the identifier must parse as an integer. InvariantCulture + NumberStyles.Integer
            // freezes which strings are accepted regardless of the operating-system locale (AAP §0.6.5).
            if (!int.TryParse(IdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int channelId))
            {
                RequestTimedMessage(2000, "Invalid Id", $"Id '{IdText}' is not a valid number");
                return;
            }

            // Guard 2: the name must be supplied.
            if (string.IsNullOrEmpty(Name))
            {
                RequestTimedMessage(2000, "Invalid Name", "Name is not filled in");
                return;
            }

            // Guard 3: the frequency must be supplied.
            if (string.IsNullOrEmpty(Frequency))
            {
                RequestTimedMessage(2000, "Invalid Frequency", "Frequency is not filled in");
                return;
            }

            // All guards passed: compose the edited channel. Location defaults to the value the dialog
            // opened with and is replaced only when both coordinates are present (WinForms parity).
            CRadioChannel channel = new CRadioChannel
            {
                Id = channelId,
                Name = Name,
                Frequency = Frequency,
                Location = _originalLocation
            };

            if (!string.IsNullOrEmpty(LatText) && !string.IsNullOrEmpty(LonText))
            {
                channel.Location = LatText + " " + LonText;
            }

            RequestClose(channel);
        }

        /// <summary>
        /// Dismisses the dialog without saving (the former Cancel button) by raising
        /// <see cref="RequestCancel"/>.
        /// </summary>
        private void OnCancel()
        {
            RequestCancel();
        }
    }
}
