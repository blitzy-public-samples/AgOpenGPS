// [XPLAT] migrated from net48/WinForms FormISOBUS.cs + FormISOBUS.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using AgIO.Properties;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.ViewModels;
#if WINDOWS
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using Microsoft.Win32;
#endif

namespace AgIO.Views
{
    /// <summary>
    /// View-model backing the Avalonia ISOBUS dialog (the migrated WinForms <c>FormISOBUS</c>). It lets the
    /// operator pick a CAN adapter and channel and then launch or stop the external <c>AOG-TaskController</c>
    /// process, exactly as the original dialog did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>[XPLAT] Windows feature-gate (AAP §0.6.3).</b> This is the most Windows-coupled dialog in AgIO: the
    /// external task controller is a Windows executable whose install path is discovered through the Windows
    /// <b>Registry</b>, and it is launched/stopped through <see cref="System.Diagnostics.Process"/>. The
    /// Registry read and the process control therefore only exist when the <c>WINDOWS</c> compilation symbol is
    /// defined — i.e. on the <c>net8.0-windows</c> target head. On the plain <c>net8.0</c> head (Linux/macOS, and
    /// any non-Windows publish) that code is excluded entirely and the launch path degrades gracefully: it
    /// surfaces a friendly "only available on Windows" notice through <see cref="IErrorPresenter"/> and never
    /// starts anything. Start-up is never broken (AAP §0.7.2 graceful degradation).
    /// </para>
    /// <para>
    /// <b>Portable vs. gated.</b> The adapter/channel <i>selection</i> logic — the adapter list, the per-adapter
    /// channel counts, the "reset the channel when the adapter changes" rule, and the persistence of
    /// <c>isobus_canAdapterIndex</c>/<c>isobus_canChannelIndex</c> — is fully portable and runs on every OS. Only
    /// the Registry lookup and the process launch/stop are gated behind <c>#if WINDOWS</c>.
    /// </para>
    /// <para>
    /// <b>God-object removal.</b> The original form reached UI and helpers directly. This view-model takes no
    /// <c>FormLoop</c>/<c>mf</c> back-reference; the four informational/error <c>MessageBox.Show(...)</c> calls of
    /// the original are routed through the injected <see cref="IErrorPresenter"/> (the Core
    /// <see cref="IErrorPresenter.PresentTimedMessage(TimeSpan, string, string)"/> contract), and the dialog
    /// outcome is signalled to the hosting window through the <see cref="RequestClose"/> event.
    /// </para>
    /// <para>
    /// <b>Culture safety (AAP §0.6.5).</b> The CAN channel embedded in the launch arguments is formatted with
    /// <see cref="CultureInfo.InvariantCulture"/> so a locale whose number formatting differs can never change
    /// the command line handed to the task controller.
    /// </para>
    /// </remarks>
    public class FormISOBUSViewModel : ViewModel
    {
        // [XPLAT] The CAN adapters offered by the dialog, in display order. This is the portable replacement for
        // the WinForms cboxRadioAdapter.Items list and is identical to the original four entries.
        private static readonly string[] AdapterNamesSource =
        {
            "PEAK-PCAN",
            "InnoMaker-USB2CAN",
            "Rusoku-TouCAN",
            "SYS-TEC-USB2CAN",
        };

        // [XPLAT] How many CAN channels each adapter exposes. Ported verbatim from the WinForms
        // UpdateChannelSelection() dictionary; drives the 1..N channel list shown for the selected adapter.
        private static readonly Dictionary<string, int> AdapterChannelCounts = new Dictionary<string, int>
        {
            { "PEAK-PCAN", 16 },
            { "InnoMaker-USB2CAN", 2 },
            { "Rusoku-TouCAN", 16 },
            { "SYS-TEC-USB2CAN", 2 },
        };

        // [XPLAT] Presenter used to surface the original four MessageBox.Show notifications. Stored (never
        // re-assigned) and always invoked null-conditionally so a null presenter can never fault the dialog,
        // mirroring the optional-presenter pattern used by NtripService/SerialCommService.
        private readonly IErrorPresenter _errorPresenter;

        // [XPLAT] Channel numbers (1..N) available for the currently selected adapter; bound by the channel
        // combo box. Populated by UpdateChannelSelection(); replaces the WinForms cboxRadioChannel.Items.
        private readonly ObservableCollection<int> _channelOptions = new ObservableCollection<int>();

        // [XPLAT] Whether the external AOG-TaskController is installed. On Windows this is the one-time result of
        // the Registry lookup; off Windows it is always false. Read by the visibility/command helpers below.
        private readonly bool _isInstalled;

        // [XPLAT] Backing command instances kept strongly typed so their CanExecute state can be refreshed when
        // the task-controller process starts or stops (see RefreshTaskControllerState). Exposed as ICommand.
        private readonly RelayCommand _openIsobusCommand;
        private readonly RelayCommand _closeIsobusCommand;

        private int _selectedAdapterIndex;
        private int _selectedChannelIndex;
        private bool _isIsobusOn;

        // [XPLAT] Process output log shown in the dialog (the former textBoxRcv). Field-initialized so the plain
        // net8.0 build, where nothing ever writes to it, neither warns (CS0649) nor renders null text.
        private string _logText = string.Empty;

#if WINDOWS
        // [XPLAT] The launched AOG-TaskController process. WINDOWS-only: the external task controller is a Windows
        // executable discovered through the Registry. Plain field (nullable reference types are disabled
        // project-wide) — assigned and null-checked without a '?' annotation, matching the WinForms original.
        private Process aogTaskControllerProcess;

        // [XPLAT] Upper bound on the retained process-log length, ported from the WinForms MaxLogLength. Only the
        // Windows build streams process output, so the constant lives inside the gate to stay "used" on net8.0.
        private const int MaxLogLength = 100000;
#endif

        /// <summary>
        /// The public download page for the external task controller, shown when it is not installed. Exposed so
        /// the view can render it as a hyperlink; opening a browser is a view concern (kept out of the
        /// view-model so no <see cref="System.Diagnostics.Process"/> dependency leaks onto the non-Windows head).
        /// Value preserved from the WinForms <c>linkDownloadIsobus</c> navigation target.
        /// </summary>
        public const string DownloadUrl = "https://www.github.com/GwnDaan/AOG-TaskController";

        /// <summary>
        /// Initializes a new instance of the <see cref="FormISOBUSViewModel"/> class, seeding the adapter and
        /// channel selection from saved settings exactly as the WinForms constructor did
        /// (<c>cboxRadioAdapter.SelectedIndex = isobus_canAdapterIndex</c> /
        /// <c>cboxRadioChannel.SelectedIndex = isobus_canChannelIndex</c>).
        /// </summary>
        /// <param name="errorPresenter">
        /// Presenter through which the dialog surfaces transient informational/error messages (the migrated
        /// replacement for <c>MessageBox.Show</c>). May be <see langword="null"/>; every use is
        /// null-conditional so a missing presenter never faults the dialog.
        /// </param>
        public FormISOBUSViewModel(IErrorPresenter errorPresenter)
        {
            _errorPresenter = errorPresenter;

            // [XPLAT] Runtime capability flag. The external task controller can only be launched on Windows, so
            // the view binds the Open/Close buttons' availability to this. RuntimeInformation is evaluated on
            // both target heads (so its using is never unnecessary); on the net8.0 head the gated launch code is
            // additionally absent, so the dialog degrades to the "Windows only" notice regardless.
            IsSupported = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            // [XPLAT] Resolve the install state once. GetInstallationPath() is a Windows-only Registry read; off
            // Windows the feature is unavailable by definition. Assigned on BOTH heads so the readonly field is
            // definitely-assigned and never triggers CS0649 on net8.0.
#if WINDOWS
            _isInstalled = !string.IsNullOrEmpty(GetInstallationPath());
#else
            _isInstalled = false;
#endif

            _isIsobusOn = Settings.Default.isobus_isOn;

            // [XPLAT] Seed the adapter index from settings, clamped into range so an out-of-range/corrupt saved
            // value can never throw (WinForms ComboBox.SelectedIndex would have thrown) — it falls back to the
            // first adapter. The channel list and channel index are then derived by UpdateChannelSelection().
            int savedAdapter = Settings.Default.isobus_canAdapterIndex;
            _selectedAdapterIndex = (savedAdapter >= 0 && savedAdapter < AdapterNamesSource.Length)
                ? savedAdapter
                : 0;

            // Populate ChannelOptions for the seeded adapter and select the saved channel (the constructor case
            // always preserves the saved channel because the seeded adapter equals the saved adapter).
            UpdateChannelSelection();

            _openIsobusCommand = new RelayCommand(StartAogTaskController, () => CanOpenTaskController);
            _closeIsobusCommand = new RelayCommand(OnCloseIsobus, () => CanCloseTaskController);
            OkCommand = new RelayCommand(OnOk);
        }

        /// <summary>
        /// Gets the CAN adapters offered by the dialog, in display order. Bound by the adapter combo box; the
        /// portable replacement for the WinForms <c>cboxRadioAdapter.Items</c>.
        /// </summary>
        public IReadOnlyList<string> AdapterNames => AdapterNamesSource;

        /// <summary>
        /// Gets the CAN channel numbers (1..N) available for the currently selected adapter. Bound by the
        /// channel combo box and recomputed whenever <see cref="SelectedAdapterIndex"/> changes.
        /// </summary>
        public ObservableCollection<int> ChannelOptions => _channelOptions;

        /// <summary>
        /// Gets or sets the zero-based index of the selected CAN adapter (the former
        /// <c>cboxRadioAdapter.SelectedIndex</c>). Changing it rebuilds <see cref="ChannelOptions"/>, resets the
        /// channel when the adapter actually changed (and keeps the saved channel when it did not), and persists
        /// <c>isobus_canAdapterIndex</c> — exactly reproducing the WinForms
        /// <c>cboxRadioAdapter_SelectedIndexChanged</c> ordering (update channels first, then save the index).
        /// </summary>
        public int SelectedAdapterIndex
        {
            get { return _selectedAdapterIndex; }
            set
            {
                if (value != _selectedAdapterIndex)
                {
                    _selectedAdapterIndex = value;
                    NotifyPropertyChanged();

                    // Rebuild the channel list and (re)select a channel BEFORE saving the new adapter index, so
                    // UpdateChannelSelection() still sees the previous saved adapter and can detect the change.
                    UpdateChannelSelection();

                    Settings.Default.isobus_canAdapterIndex = _selectedAdapterIndex;

                    // The adapter panel/enabled state does not change with the selection, but the selected
                    // adapter name (used in the launch arguments) does — refresh dependent bindings.
                    NotifyPropertyChanged(nameof(SelectedAdapterName));
                }
            }
        }

        /// <summary>
        /// Gets or sets the zero-based index of the selected CAN channel (the former
        /// <c>cboxRadioChannel.SelectedIndex</c>). Changing it persists <c>isobus_canChannelIndex</c>, matching
        /// the WinForms <c>cboxRadioChannel_SelectedIndexChanged</c> handler.
        /// </summary>
        public int SelectedChannelIndex
        {
            get { return _selectedChannelIndex; }
            set
            {
                if (value != _selectedChannelIndex)
                {
                    _selectedChannelIndex = value;
                    NotifyPropertyChanged();
                    Settings.Default.isobus_canChannelIndex = _selectedChannelIndex;
                    NotifyPropertyChanged(nameof(SelectedChannelNumber));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the ISOBUS task controller is currently flagged as on. Backed by the
        /// persisted <c>isobus_isOn</c> setting; set to <see langword="true"/> on a successful Windows launch and
        /// to <see langword="false"/> when the operator closes the connection (the latter on every OS).
        /// </summary>
        public bool IsIsobusOn
        {
            get { return _isIsobusOn; }
            private set
            {
                if (value != _isIsobusOn)
                {
                    _isIsobusOn = value;
                    Settings.Default.isobus_isOn = _isIsobusOn;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether launching the external task controller is supported on the current
        /// platform (true only on Windows). The view binds the Open/Close buttons' enabled state to this so they
        /// are disabled on Linux/macOS.
        /// </summary>
        public bool IsSupported { get; }

        /// <summary>
        /// Gets a value indicating whether the external <c>AOG-TaskController</c> is installed. On Windows this
        /// is the one-time result of the Registry lookup; off Windows it is always <see langword="false"/>.
        /// Drives whether the adapter panel or the download prompt is shown (the former WinForms
        /// <c>flowLayoutCANAdapter</c>/<c>flowLayoutDownloadIsobus</c> visibility split).
        /// </summary>
        public bool IsInstalled => _isInstalled;

        /// <summary>
        /// Gets a value indicating whether the task-controller process is currently running. Always
        /// <see langword="false"/> off Windows (no process is ever started there).
        /// </summary>
        public bool IsRunning
        {
            get
            {
#if WINDOWS
                return aogTaskControllerProcess != null && !aogTaskControllerProcess.HasExited;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Gets the display name of the currently selected adapter, or an empty string when no valid adapter is
        /// selected. This is the value embedded in the <c>--can_adapter=</c> launch argument (the former
        /// <c>cboxRadioAdapter.SelectedItem</c>).
        /// </summary>
        public string SelectedAdapterName
        {
            get
            {
                if (_selectedAdapterIndex >= 0 && _selectedAdapterIndex < AdapterNamesSource.Length)
                {
                    return AdapterNamesSource[_selectedAdapterIndex];
                }
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets the CAN channel number currently selected (the value, not the index), or <c>0</c> when no
        /// channel is available. This is the value embedded in the <c>--can_channel=</c> launch argument (the
        /// former <c>cboxRadioChannel.SelectedItem</c>, which held the 1-based channel number).
        /// </summary>
        public int SelectedChannelNumber
        {
            get
            {
                if (_selectedChannelIndex >= 0 && _selectedChannelIndex < _channelOptions.Count)
                {
                    return _channelOptions[_selectedChannelIndex];
                }
                return 0;
            }
        }

        /// <summary>
        /// Gets the accumulated process output log shown in the dialog (the former read-only <c>textBoxRcv</c>).
        /// Populated only on Windows, where the task-controller process streams its standard output/error; on
        /// other platforms it stays empty.
        /// </summary>
        public string LogText => _logText;

        /// <summary>
        /// Gets a value indicating whether the "Open" (start) action is currently available: the platform must
        /// support it, the task controller must be installed, and it must not already be running. Bound to the
        /// Open button (and used as its command's <c>CanExecute</c>).
        /// </summary>
        public bool CanOpenTaskController => IsSupported && IsInstalled && !IsRunning;

        /// <summary>
        /// Gets a value indicating whether the "Close" (stop) action is currently available: the platform must
        /// support it and the process must be running. Bound to the Close button (and used as its command's
        /// <c>CanExecute</c>).
        /// </summary>
        public bool CanCloseTaskController => IsSupported && IsRunning;

        /// <summary>
        /// Gets a value indicating whether the adapter/channel selection is editable. It mirrors the WinForms
        /// rule that disabled the combo boxes while the process was running (and hid them entirely when the task
        /// controller was not installed).
        /// </summary>
        public bool IsAdapterSelectionEnabled => IsSupported && IsInstalled && !IsRunning;

        /// <summary>
        /// Gets a value indicating whether the download prompt should be shown — i.e. the platform supports the
        /// feature but the task controller is not installed (the former <c>flowLayoutDownloadIsobus</c>
        /// visibility). On non-Windows platforms the view shows the "Windows only" notice instead, so this is
        /// <see langword="false"/> there.
        /// </summary>
        public bool ShowDownloadLink => IsSupported && !IsInstalled;

        /// <summary>
        /// Gets a value indicating whether the adapter/channel selection panel should be shown — i.e. the task
        /// controller is installed (the former <c>flowLayoutCANAdapter</c> visibility).
        /// </summary>
        public bool ShowAdapterPanel => IsSupported && IsInstalled;

        /// <summary>
        /// Gets a value indicating whether the process-output log should be shown — i.e. the task controller is
        /// installed (the former <c>textBoxRcv</c> visibility).
        /// </summary>
        public bool ShowLog => IsSupported && IsInstalled;

        /// <summary>
        /// Gets the command that starts the external task controller (the former <c>btnOpenIsobus</c>). On
        /// Windows it performs the Registry lookup and process launch; on other platforms it reports that the
        /// feature is Windows-only and starts nothing.
        /// </summary>
        public ICommand OpenIsobusCommand => _openIsobusCommand;

        /// <summary>
        /// Gets the command that stops the external task controller and clears the on-flag (the former
        /// <c>btnCloseIsobus</c>).
        /// </summary>
        public ICommand CloseIsobusCommand => _closeIsobusCommand;

        /// <summary>
        /// Gets the command that confirms the dialog: it persists the adapter/channel indices, saves settings,
        /// and requests the hosting window to close (the former <c>btnIsobusOK</c>, which hid the form).
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Raised to ask the hosting window to close the dialog. Initialized to a no-op delegate so it is always
        /// safe to invoke (nullable reference types are disabled project-wide).
        /// </summary>
        public event Action RequestClose = delegate { };

        /// <summary>
        /// Rebuilds <see cref="ChannelOptions"/> for the currently selected adapter and (re)selects a channel,
        /// reproducing the WinForms <c>UpdateChannelSelection()</c> exactly: the channel list is the 1..N range
        /// for the adapter, and the channel index is restored from settings only when the displayed adapter still
        /// equals the saved adapter — otherwise it resets to the first channel.
        /// </summary>
        private void UpdateChannelSelection()
        {
            string adapter = SelectedAdapterName;

            _channelOptions.Clear();
            if (AdapterChannelCounts.TryGetValue(adapter, out int channelCount))
            {
                for (int channel = 1; channel <= channelCount; channel++)
                {
                    _channelOptions.Add(channel);
                }
            }

            // [XPLAT] "Set the channel to the saved item only if the adapter is the same" — the comparison reads
            // the still-unchanged saved adapter index (SelectedAdapterIndex's setter saves AFTER calling this),
            // matching the WinForms ordering. A different adapter resets the channel to the first entry.
            int desiredChannelIndex;
            if (Settings.Default.isobus_canAdapterIndex == _selectedAdapterIndex)
            {
                desiredChannelIndex = Settings.Default.isobus_canChannelIndex;
            }
            else
            {
                desiredChannelIndex = 0;
            }

            // Clamp into the available range so a stale/corrupt saved channel can never select out of bounds.
            if (_channelOptions.Count == 0)
            {
                desiredChannelIndex = -1;
            }
            else if (desiredChannelIndex < 0 || desiredChannelIndex >= _channelOptions.Count)
            {
                desiredChannelIndex = 0;
            }

            SelectedChannelIndex = desiredChannelIndex;
        }

        /// <summary>
        /// Confirms the dialog: persists the current adapter/channel indices, writes settings to disk, and asks
        /// the hosting window to close. The indices are also persisted live as the operator changes them (as in
        /// the original); re-writing them here keeps the OK path self-contained before the explicit save.
        /// </summary>
        private void OnOk()
        {
            Settings.Default.isobus_canAdapterIndex = _selectedAdapterIndex;
            Settings.Default.isobus_canChannelIndex = _selectedChannelIndex;
            Settings.Default.Save();
            RequestClose();
        }

        /// <summary>
        /// Stops the external task controller (when supported) and clears the on-flag — the migrated
        /// <c>btnCloseIsobus_Click</c>. Clearing <c>isobus_isOn</c> is portable and happens on every OS; the
        /// process stop only exists on Windows.
        /// </summary>
        private void OnCloseIsobus()
        {
            IsIsobusOn = false;
#if WINDOWS
            StopAogTaskControllerProcess();
#endif
        }

        /// <summary>
        /// Starts the external <c>AOG-TaskController</c> (the migrated <c>btnOpenIsobus_Click</c> →
        /// <c>StartAogTaskController</c>). On Windows it discovers the install path from the Registry, stops any
        /// stray instance, launches the process with the selected adapter/channel, streams its output into
        /// <see cref="LogText"/>, and flags <c>isobus_isOn</c>. On any other platform the feature does not exist,
        /// so it reports that it is Windows-only and starts nothing — never breaking the dialog.
        /// </summary>
        private void StartAogTaskController()
        {
#if WINDOWS
            // [XPLAT] Belt-and-suspenders runtime guard alongside the compile-time gate. On the net8.0-windows
            // head this is effectively always true, but it documents intent and is harmless.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                PresentWindowsOnlyNotice();
                return;
            }

            ClearLog();
            try
            {
                string path = GetInstallationPath();
                if (string.IsNullOrEmpty(path))
                {
                    _errorPresenter?.PresentTimedMessage(
                        TimeSpan.FromSeconds(4),
                        "ISOBUS",
                        "AOG-TaskController is not installed??");
                    return;
                }

                // Stop any other running instances before launching a fresh one (parity with the original).
                Process[] processes = Process.GetProcessesByName("AOG-TaskController");
                foreach (Process process in processes)
                {
                    process.Kill();
                }

                path += @"\bin\AOG-TaskController.exe";

                // [XPLAT] The channel number is formatted with InvariantCulture so the command line handed to the
                // task controller never varies with the operating-system locale (AAP §0.6.5). The argument string
                // (including the double space before --log2file) is preserved verbatim from the original.
                string channelArgument = SelectedChannelNumber.ToString(CultureInfo.InvariantCulture);
                string arguments =
                    $"--can_adapter={SelectedAdapterName} --can_channel={channelArgument} --log_level=debug  --log2file";

                aogTaskControllerProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    },
                    EnableRaisingEvents = true,
                };

                aogTaskControllerProcess.OutputDataReceived += AogTaskControllerProcess_OutputDataReceived;
                aogTaskControllerProcess.ErrorDataReceived += AogTaskControllerProcess_OutputDataReceived;
                aogTaskControllerProcess.Exited += AogTaskControllerProcess_Exited;
                aogTaskControllerProcess.Start();
                aogTaskControllerProcess.BeginOutputReadLine();
                // [XPLAT] Also drain stderr: RedirectStandardError is enabled, so the error stream must be read
                // or the child can block on a full buffer. The WinForms original subscribed the handler but never
                // began the error read — reading it here is a safe robustness fix with no observable parity drift.
                aogTaskControllerProcess.BeginErrorReadLine();

                IsIsobusOn = true;
                RefreshTaskControllerState();
            }
            catch (Exception ex)
            {
                _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(4), "ISOBUS", ex.Message);
            }
#else
            // [XPLAT] Non-Windows graceful degradation: the external task controller is a Windows executable
            // located via the Registry, so there is nothing to start here. Inform the operator and return.
            PresentWindowsOnlyNotice();
#endif
        }

        /// <summary>
        /// Surfaces the friendly "Windows only" notice through the injected <see cref="IErrorPresenter"/>. Used
        /// by the non-Windows launch fallback (and the Windows runtime guard), so it is compiled on every target.
        /// </summary>
        private void PresentWindowsOnlyNotice()
        {
            _errorPresenter?.PresentTimedMessage(
                TimeSpan.FromSeconds(4),
                "ISOBUS",
                "ISOBUS Task Controller is only available on Windows.");
        }

#if WINDOWS
        /// <summary>
        /// [XPLAT] Reads the <c>AOG-TaskController</c> install path from the Windows Registry
        /// (<c>HKLM\SOFTWARE\AOG-TaskController</c>), checking the 64-bit then the 32-bit view and returning the
        /// key's default value, or <see langword="null"/> when it is absent. Windows-only; compiled solely under
        /// the <c>net8.0-windows</c> target.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static string GetInstallationPath()
        {
            const string subKeyPath = @"SOFTWARE\AOG-TaskController";

            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey key = baseKey.OpenSubKey(subKeyPath))
                {
                    if (key != null)
                    {
                        // Guard the default value before ToString() so a present-but-valueless key cannot fault
                        // (the WinForms original called .ToString() unconditionally).
                        object value = key.GetValue("");
                        if (value != null)
                        {
                            return value.ToString();
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// [XPLAT] Stops the running task-controller process: requests a graceful close, waits up to five
        /// seconds, then force-kills it if necessary (the migrated <c>StopAogTaskControllerProcess</c>). The
        /// WinForms busy-wait with <c>Application.DoEvents()</c> is replaced by
        /// <see cref="Process.WaitForExit(int)"/>, which is cleaner and carries no UI dependency. Windows-only.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void StopAogTaskControllerProcess()
        {
            try
            {
                if (aogTaskControllerProcess == null || aogTaskControllerProcess.HasExited)
                {
                    aogTaskControllerProcess = null;
                    RefreshTaskControllerState();
                    return;
                }

                aogTaskControllerProcess.CloseMainWindow();
                if (!aogTaskControllerProcess.WaitForExit(5000))
                {
                    // Force close if it did not exit gracefully within the timeout.
                    aogTaskControllerProcess.Kill();
                }

                aogTaskControllerProcess.Close();
                aogTaskControllerProcess = null;
                AppendLog(">>> AOG-TaskController.exe stopped");
                RefreshTaskControllerState();
            }
            catch (Exception ex)
            {
                _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(4), "ISOBUS", ex.Message);
            }
        }

        /// <summary>
        /// [XPLAT] Handles process exit (raised on a background thread); marshals the UI-state refresh onto the
        /// Avalonia UI thread — the equivalent of the WinForms <c>InvokeRequired</c>/<c>Invoke</c> pattern used by
        /// the original <c>UpdateComponentVisibility</c>.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void AogTaskControllerProcess_Exited(object sender, EventArgs e)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                RefreshTaskControllerState();
            }
            else
            {
                Dispatcher.UIThread.Post(RefreshTaskControllerState);
            }
        }

        /// <summary>
        /// [XPLAT] Receives a line of the process's standard output or error (background thread) and appends it to
        /// the log, ignoring empty lines — the migrated <c>AogTaskControllerProcess_OutputDataReceived</c>.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void AogTaskControllerProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendLog(e.Data);
            }
        }

        /// <summary>
        /// [XPLAT] Appends a line to <see cref="LogText"/>, marshaling onto the Avalonia UI thread when invoked
        /// from a background thread — the migrated <c>AppendLog</c> <c>InvokeRequired</c>/<c>Invoke</c> pattern.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void AppendLog(string message)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                AppendLogCore(message);
            }
            else
            {
                Dispatcher.UIThread.Post(() => AppendLogCore(message));
            }
        }

        /// <summary>
        /// [XPLAT] Appends <paramref name="message"/> to the log on the UI thread and trims the oldest content
        /// once it exceeds <c>MaxLogLength</c>, preserving the WinForms log-trim behaviour.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void AppendLogCore(string message)
        {
            string updated = _logText + message + Environment.NewLine;

            if (updated.Length > MaxLogLength)
            {
                // Keep the most recent MaxLogLength characters (drop the oldest), bounding memory use.
                updated = updated.Substring(updated.Length - MaxLogLength);
            }

            _logText = updated;
            NotifyPropertyChanged(nameof(LogText));
        }

        /// <summary>
        /// [XPLAT] Clears the process log (the migrated <c>textBoxRcv.Clear()</c> at the start of a launch).
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void ClearLog()
        {
            _logText = string.Empty;
            NotifyPropertyChanged(nameof(LogText));
        }

        /// <summary>
        /// [XPLAT] Refreshes every binding that depends on the install/running state and re-evaluates the
        /// Open/Close commands' <c>CanExecute</c>. Must run on the UI thread (callers marshal as needed). This
        /// replaces the per-control mutations of the WinForms <c>UpdateComponentVisibility</c>.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void RefreshTaskControllerState()
        {
            NotifyAllPropertiesChanged();
            _openIsobusCommand.RaiseCanExecuteChanged();
            _closeIsobusCommand.RaiseCanExecuteChanged();
        }
#endif
    }
}
