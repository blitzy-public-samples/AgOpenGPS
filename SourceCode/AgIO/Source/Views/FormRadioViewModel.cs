// [XPLAT] migrated from net48/WinForms FormRadio.cs + FormRadio.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Windows.Input;
using AgLibrary.Logging;
using AgOpenGPS.Core.ViewModels;
using AgIO.Services;
using AgIO.Properties;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] View-model backing <c>FormRadio.axaml</c>, the AgIO "Radio" configuration dialog that
    /// configures a serial radio used as an RTK correction source and manages the operator's list of
    /// radio channels. It replaces the deleted WinForms <c>FormRadio : Form</c>
    /// (<c>SourceCode/AgIO/Source/Forms/FormRadio.cs</c> + <c>FormRadio.Designer.cs</c>), preserving the
    /// dialog's behaviour exactly (AAP §0.2.2 / §0.7.1) while removing every Windows-Forms and
    /// god-object coupling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>God-object removal.</b> The original took a direct <c>FormLoop mf</c> back-reference and
    /// reached through it for the shared radio serial port (<c>mf.spRadio</c>), the current position
    /// (<c>mf.latitude</c>/<c>mf.longitude</c>), the NTRIP reconfigure (<c>mf.ConfigureNTRIP()</c>),
    /// the timed-message popup (<c>mf.TimedMessageBox</c>) and the on-screen-keyboard flag. Every one of
    /// those is replaced: the comm collaborators are reached through the injected
    /// <see cref="NmeaService"/>, <see cref="SerialCommService"/> and <see cref="NtripService"/> (the
    /// extracted AgIO comm services that the AgIO composition root owns), and the popup / dialog-launch /
    /// window-close concerns become intent <b>events</b> the hosting Avalonia view binds.
    /// </para>
    /// <para>
    /// <b>Shared radio port.</b> The radio serial port is the single <see cref="SerialPort"/> owned by
    /// <see cref="NtripService"/> (<c>_ntrip.spRadio</c>) — the very port the NTRIP/serial-pass
    /// correction path and the serial-pass dialog also use. Opening, closing and writing to it here goes
    /// through that shared instance, exactly as the WinForms dialog manipulated <c>mf.spRadio</c>, so the
    /// radio-versus-NTRIP relationship is honoured by the one shared port plus the mutual-exclusion guard
    /// in <see cref="OkCommand"/>.
    /// </para>
    /// <para>
    /// <b>NTRIP mutual exclusion (frozen).</b> When the operator confirms with the radio enabled while
    /// NTRIP is also enabled, NTRIP is switched off and a timed message is raised — identical to the
    /// original, down to the message text. Confirming with the radio on but no channel selected keeps the
    /// dialog open (the former <c>DialogResult.None</c>), surfaced as a timed message with no
    /// <see cref="RequestClose"/>.
    /// </para>
    /// <para>
    /// <b>Serial framing frozen / culture safe.</b> Only serial-port-<i>name</i> enumeration is abstracted
    /// (via <see cref="SerialCommService.GetAvailablePortNames"/>, which routes to
    /// <c>IPlatformServices.GetSerialPortNames()</c>); the wire framing is untouched. The baud rate is
    /// parsed with <see cref="CultureInfo.InvariantCulture"/> and the radio set-frequency command string
    /// <c>"SL&amp;F={frequency}"</c> is preserved verbatim (AAP §0.6.5).
    /// </para>
    /// <para>
    /// <b>Channel editing round-trip.</b> Adding or editing a channel opens the
    /// <c>FormRadioChannel</c> editor (which returns a <see cref="CRadioChannel"/>). Because a view-model
    /// must not open a window, this VM raises <see cref="RequestEditChannel"/> with the channel to edit;
    /// the hosting view opens the editor, awaits its <c>ShowDialog&lt;CRadioChannel&gt;</c> result and
    /// calls <see cref="ApplyEditedChannel"/> with it (a <c>null</c> result means the editor was
    /// cancelled).
    /// </para>
    /// </remarks>
    public class FormRadioViewModel : ViewModel
    {
        // [XPLAT] The extracted AgIO comm services that replace the former FormLoop (mf) back-reference.
        // These are the same individual services the AgIO composition root (MainWindow) constructs and
        // owns; injecting them directly (rather than an aggregate facade) matches every sibling AgIO
        // view-model and the live service graph. _nmea supplies the parsed position
        // (latitude/longitude); _serial supplies the cross-platform serial port-name enumeration
        // (GetAvailablePortNames); _ntrip owns the shared radio serial port (spRadio) and the NTRIP
        // reconfigure (ConfigureNTRIP) plus participates in the radio-versus-NTRIP mutual exclusion.
        private readonly NmeaService _nmea;
        private readonly SerialCommService _serial;
        private readonly NtripService _ntrip;

        // [XPLAT] Operator position (decimal degrees) snapshotted at construction from the NMEA service,
        // reproducing the WinForms "_currentLat = mf.latitude; _currentLon = mf.longitude". Used by
        // GetChannelDistance to compute each channel's great-circle distance for display, exactly as the
        // original AddChannelToListView did.
        private readonly double _currentLat;
        private readonly double _currentLon;

        // [XPLAT] Tracks which channel an edit round-trip targets so ApplyEditedChannel can distinguish
        // "edit in place" (non-null — replace this instance) from "add new" (null — append). Mirrors the
        // WinForms split between btnEditChannel_Click (updated the selected channel) and
        // btnAddChannel_Click (appended a new one).
        private CRadioChannel _editingChannel;

        private CRadioChannel _selectedChannel;
        private string _selectedPort;
        private string _selectedBaud;
        private bool _isRadioOn;
        private bool _isRadioPortOpen;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormRadioViewModel"/> class, reproducing the
        /// combined work of the WinForms <c>FormRadio</c> constructor and its <c>FormRadio_Load</c>
        /// handler: it snapshots the current position, loads the persisted channel list, enumerates the
        /// available serial ports and seeds the port / baud / radio-on selections from settings.
        /// </summary>
        /// <param name="nmea">
        /// The NMEA service supplying the parsed operator position (<c>latitude</c>/<c>longitude</c>)
        /// snapshotted for channel-distance display. Must not be <c>null</c>.
        /// </param>
        /// <param name="serial">
        /// The serial-comm service supplying cross-platform serial port-name enumeration
        /// (<see cref="SerialCommService.GetAvailablePortNames"/>). Must not be <c>null</c>.
        /// </param>
        /// <param name="ntrip">
        /// The NTRIP service that owns the shared radio serial port (<c>spRadio</c>) and the NTRIP
        /// reconfigure (<c>ConfigureNTRIP</c>), and that participates in the radio-versus-NTRIP mutual
        /// exclusion. Must not be <c>null</c>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="nmea"/>, <paramref name="serial"/> or <paramref name="ntrip"/> is
        /// <c>null</c>.
        /// </exception>
        public FormRadioViewModel(NmeaService nmea, SerialCommService serial, NtripService ntrip)
        {
            // Fail fast on an incomplete composition rather than NullReferenceException-ing later when a
            // command reaches through a service (nullable reference types are disabled project-wide, so
            // the guards are explicit rather than annotation-driven).
            _nmea = nmea ?? throw new ArgumentNullException(nameof(nmea));
            _serial = serial ?? throw new ArgumentNullException(nameof(serial));
            _ntrip = ntrip ?? throw new ArgumentNullException(nameof(ntrip));

            // [XPLAT] mf.latitude / mf.longitude -> _nmea.latitude / longitude (public double fields).
            _currentLat = _nmea.latitude;
            _currentLon = _nmea.longitude;

            // [XPLAT] Load the persisted radio channels (Settings.Default.setRadio_Channels). The setting
            // is initialised non-null, but the original guarded a null list, so the ?? keeps that parity.
            Channels = new ObservableCollection<CRadioChannel>();
            List<CRadioChannel> storedChannels = Settings.Default.setRadio_Channels ?? new List<CRadioChannel>();
            foreach (CRadioChannel channel in storedChannels)
            {
                Channels.Add(channel);
            }

            // [XPLAT] Enumerate serial ports through the abstracted seam (COMx on Windows, /dev/ttyUSB*,
            // /dev/ttyACM* on Linux, /dev/cu.* on macOS) instead of the Windows-leaning static
            // SerialPort.GetPortNames(). Never returns null; guarded for an unwired serial service.
            AvailablePorts = new ObservableCollection<string>();
            foreach (string portName in _serial.GetAvailablePortNames())
            {
                AvailablePorts.Add(portName);
            }

            // [XPLAT] Seed the current port / baud / radio-on selections (FormRadio_Load read the same
            // three settings into cboxRadioPort / cboxBaud / cboxIsRadioOn).
            _selectedPort = Settings.Default.setPort_portNameRadio;
            _selectedBaud = Settings.Default.setPort_baudRateRadio;
            _isRadioOn = Settings.Default.setRadio_isOn;

            // [XPLAT] Pre-select the channel whose frequency matches the persisted radio channel, exactly
            // as FormRadio_Load selected the matching lvChannels row (compared on Frequency).
            string persistedChannelFrequency = Settings.Default.setPort_radioChannel;
            foreach (CRadioChannel channel in Channels)
            {
                if (channel.Frequency == persistedChannelFrequency)
                {
                    _selectedChannel = channel;
                    break;
                }
            }

            // [XPLAT] Reflect the live state of the shared radio port (it may already be open if the NTRIP
            // radio/serial-pass path opened it), mirroring the original SetButtonState() check
            // (mf.spRadio != null && mf.spRadio.IsOpen).
            _isRadioPortOpen = _ntrip.spRadio != null && _ntrip.spRadio.IsOpen;

            // Commands (each maps 1:1 to a WinForms button handler).
            OkCommand = new RelayCommand(OnOk);
            CancelCommand = new RelayCommand(OnCancel);
            OpenRadioCommand = new RelayCommand(OnOpenRadio);
            CloseRadioCommand = new RelayCommand(OnCloseRadio);
            AddChannelCommand = new RelayCommand(OnAddChannel);
            EditChannelCommand = new RelayCommand(OnEditChannel);
            DeleteChannelCommand = new RelayCommand(OnDeleteChannel);
        }

        /// <summary>
        /// Gets the radio channels bound to the channel list in the view (an Avalonia
        /// <c>ListBox</c>/<c>DataGrid</c> replacing the WinForms <c>lvChannels</c> <c>ListView</c>).
        /// Persisted to <c>Settings.Default.setRadio_Channels</c> when the dialog is confirmed.
        /// </summary>
        public ObservableCollection<CRadioChannel> Channels { get; }

        /// <summary>
        /// Gets the names of the available serial ports, enumerated through the cross-platform serial
        /// seam (<see cref="SerialCommService.GetAvailablePortNames"/>). Bound to the port selector that
        /// replaces the WinForms <c>cboxRadioPort</c> combo box.
        /// </summary>
        public ObservableCollection<string> AvailablePorts { get; }

        /// <summary>
        /// Gets or sets the channel currently selected in the list (the former <c>lvChannels</c>
        /// selection). A <c>null</c> value means no channel is selected, which the OK validation rejects
        /// while the radio is enabled.
        /// </summary>
        public CRadioChannel SelectedChannel
        {
            get { return _selectedChannel; }
            set
            {
                if (value != _selectedChannel)
                {
                    _selectedChannel = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected serial port name (the former <c>cboxRadioPort.Text</c>). Persisted
        /// to <c>Settings.Default.setPort_portNameRadio</c> on confirm.
        /// </summary>
        public string SelectedPort
        {
            get { return _selectedPort; }
            set
            {
                if (value != _selectedPort)
                {
                    _selectedPort = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected baud rate as text (the former <c>cboxBaud.Text</c>). Parsed with
        /// <see cref="CultureInfo.InvariantCulture"/> when the radio port is opened, and persisted to
        /// <c>Settings.Default.setPort_baudRateRadio</c> on confirm.
        /// </summary>
        public string SelectedBaud
        {
            get { return _selectedBaud; }
            set
            {
                if (value != _selectedBaud)
                {
                    _selectedBaud = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the radio correction source is enabled (the former
        /// <c>cboxIsRadioOn.Checked</c>). When enabled on confirm it disables NTRIP (mutual exclusion).
        /// </summary>
        public bool IsRadioOn
        {
            get { return _isRadioOn; }
            set
            {
                if (value != _isRadioOn)
                {
                    _isRadioOn = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the shared radio serial port is currently open. Drives
        /// the open/close/set-channel button enablement the WinForms <c>SetButtonState</c> handled, so the
        /// view can enable "Close" while the port is open and "Open" while it is closed.
        /// </summary>
        public bool IsRadioPortOpen
        {
            get { return _isRadioPortOpen; }
            set
            {
                if (value != _isRadioPortOpen)
                {
                    _isRadioPortOpen = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the command that confirms the dialog (the former <c>btnRadioOK</c>): it validates, persists
        /// the radio settings and channel list, applies the NTRIP mutual-exclusion guard, reconfigures
        /// NTRIP and closes the dialog. On invalid input it leaves the dialog open.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the command that dismisses the dialog without saving (the former <c>btnRadioCancel</c>).
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Gets the command that opens the shared radio serial port and sets it to the selected channel's
        /// frequency (the former <c>btnOpenSerial</c> combined with <c>btnSetChannel</c>).
        /// </summary>
        public ICommand OpenRadioCommand { get; }

        /// <summary>
        /// Gets the command that closes the shared radio serial port (the former <c>btnCloseSerial</c>).
        /// </summary>
        public ICommand CloseRadioCommand { get; }

        /// <summary>
        /// Gets the command that begins adding a new channel (the former <c>btnAddChannel</c>): it composes
        /// a new channel with the next free id and raises <see cref="RequestEditChannel"/> to open the
        /// editor.
        /// </summary>
        public ICommand AddChannelCommand { get; }

        /// <summary>
        /// Gets the command that begins editing the selected channel (the former <c>btnEditChannel</c>): it
        /// raises <see cref="RequestEditChannel"/> with a copy of the selection so a cancelled edit leaves
        /// the original untouched.
        /// </summary>
        public ICommand EditChannelCommand { get; }

        /// <summary>
        /// Gets the command that deletes the selected channel from <see cref="Channels"/> (the former
        /// <c>btnDeleteChannel</c>).
        /// </summary>
        public ICommand DeleteChannelCommand { get; }

        /// <summary>
        /// Raised when the dialog should close (parameterless — unlike a picker, this dialog persists its
        /// result directly to settings rather than returning a value). The hosting Avalonia window
        /// subscribes and closes the dialog. Initialized to a no-op delegate so it is always safe to invoke.
        /// </summary>
        public event Action RequestClose = delegate { };

        /// <summary>
        /// Raised to ask the view to show a transient, self-dismissing notification — the MVVM replacement
        /// for the WinForms <c>FormLoop.TimedMessageBox(milliseconds, title, message)</c>. The arguments are
        /// the display duration in milliseconds, the title and the message body. Initialized to a no-op
        /// delegate so it is always safe to invoke.
        /// </summary>
        public event Action<int, string, string> RequestTimedMessage = delegate { };

        /// <summary>
        /// Raised to ask the view to open the <c>FormRadioChannel</c> add/edit editor for the supplied
        /// channel. Because a view-model must not open a window, the hosting view opens the editor, awaits
        /// its <c>ShowDialog&lt;CRadioChannel&gt;</c> result and calls <see cref="ApplyEditedChannel"/> with
        /// it. Initialized to a no-op delegate so it is always safe to invoke.
        /// </summary>
        public event Action<CRadioChannel> RequestEditChannel = delegate { };

        /// <summary>
        /// Applies the result of a <c>FormRadioChannel</c> add/edit round-trip raised by
        /// <see cref="RequestEditChannel"/>. Reproduces the WinForms split between
        /// <c>btnAddChannel_Click</c> (append) and <c>btnEditChannel_Click</c> (update the selected
        /// channel in place): when an edit was in progress the edited channel is replaced at its existing
        /// position; otherwise the new channel is appended. A <c>null</c> <paramref name="result"/> means
        /// the editor was cancelled and is a no-op (the former <c>DialogResult</c> not being OK).
        /// </summary>
        /// <param name="result">
        /// The channel returned by the editor, or <c>null</c> when the editor was cancelled.
        /// </param>
        public void ApplyEditedChannel(CRadioChannel result)
        {
            // Cancelled editor: nothing to apply (parity with the original "if DialogResult.OK" gate).
            if (result == null)
            {
                _editingChannel = null;
                return;
            }

            // Edit-in-place: replace the edited instance at its current position so list ordering is
            // preserved and the binding refreshes (CRadioChannel is not observable, so a property mutation
            // alone would not notify the list — replacing the item raises the collection-changed event).
            if (_editingChannel != null && Channels.Contains(_editingChannel))
            {
                int index = Channels.IndexOf(_editingChannel);
                Channels[index] = result;
            }
            else
            {
                // Add: append the new channel (former btnAddChannel_Click).
                Channels.Add(result);
            }

            // Keep the edited/added channel selected, matching the original which left the row selected.
            SelectedChannel = result;
            _editingChannel = null;
        }

        /// <summary>
        /// Computes the display distance (in kilometres, formatted <c>"N2"</c>) from the operator's current
        /// position to the supplied channel's stored location, or <c>"-"</c> when the channel has no usable
        /// location or the current position is unknown. Reproduces the WinForms
        /// <c>AddChannelToListView</c> distance column exactly.
        /// </summary>
        /// <param name="channel">The channel whose distance to display.</param>
        /// <returns>The formatted distance string, or <c>"-"</c> when it cannot be computed.</returns>
        /// <remarks>
        /// [XPLAT] The great-circle (haversine) math is inlined here — bit-identical to the
        /// <c>glm.DistanceLonLat</c> the WinForms dialog called (mean Earth radius 6371&#160;km) — so the
        /// view-model depends only on its injected collaborators and not on the unrelated <c>glm</c> helper.
        /// Both the location parse and the distance format use <see cref="CultureInfo.InvariantCulture"/> so
        /// a comma-decimal locale can never misread the stored coordinates or alter the rendered text
        /// (AAP §0.6.5).
        /// </remarks>
        public string GetChannelDistance(CRadioChannel channel)
        {
            string distance = "-";

            if (channel != null && !string.IsNullOrEmpty(channel.Location) && _currentLat > 0 && _currentLon > 0)
            {
                string[] locationArray = channel.Location.Split(' ');

                if (locationArray.Length >= 2)
                {
                    if (double.TryParse(locationArray[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) &&
                        double.TryParse(locationArray[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                    {
                        distance = DistanceLonLatKm(lon, lat, _currentLon, _currentLat)
                            .ToString("N2", CultureInfo.InvariantCulture);
                    }
                }
            }

            return distance;
        }

        // [XPLAT] Behaviour-frozen great-circle distance in kilometres, inlined from glm.DistanceLonLat so
        // the distance column is bit-identical to the WinForms build without taking a dependency on the
        // glm helper. Identical formula and constants: mean Earth radius 6371 km and the same
        // degrees->radians factor.
        private static double DistanceLonLatKm(double lon1, double lat1, double lon2, double lat2)
        {
            const int earthMeanRadiusKm = 6371;
            const double toRadians = 0.01745329251994329576923690768489;

            double dLon = (lon2 - lon1) * toRadians;
            double dLat = (lat2 - lat1) * toRadians;

            double a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
                + Math.Cos(lat1 * toRadians) * Math.Cos(lat2 * toRadians) * (Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
            double angle = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return angle * earthMeanRadiusKm;
        }

        /// <summary>
        /// Confirms the dialog (the former <c>btnRadioOK_Click</c>): validates the radio-on/channel
        /// requirement, persists the radio settings and channel list, applies the NTRIP mutual-exclusion
        /// guard, then closes the dialog and reconfigures NTRIP. Invalid input keeps the dialog open.
        /// </summary>
        private void OnOk()
        {
            // [XPLAT] Radio enabled but no channel selected -> timed message and STAY OPEN (the former
            // DialogResult.None), preserved verbatim including the message text.
            if (IsRadioOn && SelectedChannel == null)
            {
                RequestTimedMessage(2000, "No channel", "Radio is set to on. But no channel is selected");
                return;
            }

            // Persist the selected channel frequency only when a channel is selected (parity with the
            // original "if lvChannels.SelectedItems.Count > 0").
            if (SelectedChannel != null)
            {
                Settings.Default.setPort_radioChannel = SelectedChannel.Frequency;
            }

            Settings.Default.setPort_portNameRadio = SelectedPort;
            Settings.Default.setPort_baudRateRadio = SelectedBaud;
            Settings.Default.setRadio_isOn = IsRadioOn;

            // [XPLAT] NTRIP mutual exclusion (frozen): enabling the radio disables NTRIP. The timed-message
            // text — including the original "diabling" spelling — is preserved verbatim.
            if (Settings.Default.setRadio_isOn && Settings.Default.setNTRIP_isOn)
            {
                RequestTimedMessage(2000, "NTRIP also enabled", "NTRIP is also enabled, diabling it");
                Settings.Default.setNTRIP_isOn = false;
            }

            // Persist the channel list and save (the original assigned the backing list then Save()).
            Settings.Default.setRadio_Channels = Channels.ToList();
            Settings.Default.Save();

            // [XPLAT] Source order preserved: close the dialog, then reconfigure NTRIP. mf.ConfigureNTRIP()
            // is the NtripService.ConfigureNTRIP() method on the injected NTRIP service.
            RequestClose();
            _ntrip.ConfigureNTRIP();
        }

        /// <summary>
        /// Dismisses the dialog without saving (the former <c>btnRadioCancel</c>).
        /// </summary>
        private void OnCancel()
        {
            RequestClose();
        }

        /// <summary>
        /// Opens the shared radio serial port and, on success, sets it to the selected channel's frequency
        /// (the former <c>btnOpenSerial_Click</c> combined with <c>btnSetChannel_Click</c>).
        /// </summary>
        private void OnOpenRadio()
        {
            // The injected NTRIP service owns the shared radio serial port (formerly mf.spRadio).
            NtripService ntrip = _ntrip;

            // Close any previously open shared port first (parity with btnOpenSerial_Click head).
            if (ntrip.spRadio != null && ntrip.spRadio.IsOpen)
            {
                ntrip.spRadio.Close();
                ntrip.spRadio.Dispose();
                ntrip.spRadio = null;
            }

            // [XPLAT] Serial framing frozen: same SerialPort construction and "\r\n" newline as the
            // original; baud parsed with InvariantCulture so a comma-decimal locale cannot misread it.
            ntrip.spRadio = new SerialPort(SelectedPort, int.Parse(SelectedBaud, CultureInfo.InvariantCulture))
            {
                NewLine = "\r\n"
            };

            try
            {
                ntrip.spRadio.Open();
                IsRadioPortOpen = true;

                // [XPLAT] Set the radio frequency on open. The "SL&F={frequency}" command string is
                // preserved verbatim; Frequency is a string, so no numeric formatting is applied.
                if (SelectedChannel != null)
                {
                    ntrip.spRadio.WriteLine($"SL&F={SelectedChannel.Frequency}");
                }
            }
            catch (Exception ex)
            {
                // Parity with the original catch: timed message + log; the port is not considered open.
                RequestTimedMessage(3000, "Error opening port", ex.Message);
                Log.EventWriter("Catch - > Error opening Radio port" + ex.ToString());
                IsRadioPortOpen = false;
            }
        }

        /// <summary>
        /// Closes the shared radio serial port (the former <c>btnCloseSerial_Click</c>).
        /// </summary>
        private void OnCloseRadio()
        {
            NtripService ntrip = _ntrip;
            if (ntrip.spRadio != null && ntrip.spRadio.IsOpen)
            {
                ntrip.spRadio.Close();
                ntrip.spRadio.Dispose();
                ntrip.spRadio = null;
            }

            IsRadioPortOpen = false;
        }

        /// <summary>
        /// Begins adding a new channel (the former <c>btnAddChannel_Click</c>): composes a new channel with
        /// the next free id and raises <see cref="RequestEditChannel"/> so the view opens the editor.
        /// </summary>
        private void OnAddChannel()
        {
            // Adding (not editing) — clear the edit target so ApplyEditedChannel appends the result.
            _editingChannel = null;

            // Assign the next free id (parity with btnAddChannel_Click: maxChannelId + 1).
            int maxChannelId = Channels.Count > 0 ? Channels.Max(c => c.Id) : 0;
            CRadioChannel channel = new CRadioChannel
            {
                Id = maxChannelId + 1
            };

            RequestEditChannel(channel);
        }

        /// <summary>
        /// Begins editing the selected channel (the former <c>btnEditChannel_Click</c>): raises
        /// <see cref="RequestEditChannel"/> with a copy of the selection so a cancelled edit leaves the
        /// original untouched, matching the WinForms editor which only wrote back on OK.
        /// </summary>
        private void OnEditChannel()
        {
            // The original required exactly one selected row before editing.
            if (SelectedChannel == null)
            {
                return;
            }

            _editingChannel = SelectedChannel;

            // Pass a copy: the WinForms editor edited a separate Channel object and only copied the values
            // back into the selected channel on OK, so a cancel never mutated the original.
            CRadioChannel copy = new CRadioChannel
            {
                Id = SelectedChannel.Id,
                Name = SelectedChannel.Name,
                Frequency = SelectedChannel.Frequency,
                Location = SelectedChannel.Location
            };

            RequestEditChannel(copy);
        }

        /// <summary>
        /// Deletes the selected channel from <see cref="Channels"/> (the former
        /// <c>btnDeleteChannel_Click</c>).
        /// </summary>
        private void OnDeleteChannel()
        {
            if (SelectedChannel != null)
            {
                Channels.Remove(SelectedChannel);
                SelectedChannel = null;
            }
        }
    }
}
