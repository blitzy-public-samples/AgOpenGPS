// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Linq;
using System.Text;
using AgOpenGPS.Core;

namespace AgOpenGPS
{
    /// <summary>
    /// [XPLAT] Tri-state icon of the on-screen ISOBUS section-control button. Replaces the three former
    /// WinForms <c>btnIsobusSectionControl.Image</c> assignments (<c>Properties.Resources.IsobusSectionControl*</c>)
    /// that the deleted <c>FormGPS</c> god-object owned. The bound Avalonia view-model maps each state to the
    /// corresponding icon, so this domain class no longer references any WinForms/System.Drawing image type.
    /// The selection semantics are preserved exactly:
    ///   <list type="bullet">
    ///     <item><description><see cref="Idle"/> - Task Controller running but no implement connected (numberOfSections == 0).</description></item>
    ///     <item><description><see cref="On"/> - Task Controller running with an implement and section control enabled.</description></item>
    ///     <item><description><see cref="Off"/> - Task Controller running with an implement and section control disabled.</description></item>
    ///   </list>
    /// Mirrors the established Core <c>btnStates</c> button-state migration precedent (no new abstraction introduced).
    /// </summary>
    public enum IsobusSectionControlButtonState
    {
        /// <summary>TC running but no implement connected (numberOfSections == 0).</summary>
        Idle,

        /// <summary>TC running with an implement and section control enabled.</summary>
        On,

        /// <summary>TC running with an implement and section control disabled.</summary>
        Off
    }

    public class CISOBUS
    {
        // [XPLAT] Decoupled from the WinForms host-form god-object (the former `private readonly FormGPS mf;`).
        // Per AAP §0.6.1 every former direct FormGPS member access becomes a constructor-injected collaborator,
        // exactly mirroring the established CNMEA(ApplicationModel, ...) / CModuleComm(ApplicationModel, ...)
        // decoupling. No FormGPS reference remains, so this ISOBUS / machine-PGN builder compiles and runs
        // unchanged on Windows, Linux and macOS. The PGN wire contract is FROZEN byte-for-byte (see
        // RequestSectionControlEnabled / SendFieldName / SendProcessData below and docs/pgn-protocol.md):
        //   _appModel      - shared AgOpenGPS.Core runtime model. Supplies the field-job gate
        //                    (isJobStarted, was FormGPS.isJobStarted) and the active field folder name
        //                    (Fields.CurrentFieldName, was FormGPS.currentFieldDirectory) read by the
        //                    heartbeat-reconnect field-name push. Same state, same gating.
        //   _sendPgnToLoop - the UDP/PGN loopback transmit path (was the FormGPS `mf.SendPgnToLoop(byte[])`
        //                    call). The composition root wires this delegate to
        //                    AgOpenGPS.Services.PgnDispatcher.SendPgnToLoop — the SAME method that owns the
        //                    loopback socket — so the SAME byte[] frame is sent to the SAME AgIO peer
        //                    (127.255.255.255:17777) and the SAME trailing additive-checksum CRC is applied
        //                    there. A delegate (rather than a held PgnDispatcher reference) is used for two
        //                    reasons, neither of which changes a single wire byte: (1) it breaks the
        //                    PgnDispatcher<->CISOBUS construction cycle (PgnDispatcher's own constructor
        //                    requires this CISOBUS instance), exactly as PgnDispatcher breaks its own service
        //                    cycles with late-bound delegates (OnGpsFixReady / OnRemoteSwitchChanged); and
        //                    (2) it keeps this class free of a compile-time dependency on the transport type,
        //                    matching PgnDispatcher's own injected-delegate decoupling (Action<Action>
        //                    postToUi / Action<string> reportError). No new abstraction is introduced.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        private readonly ApplicationModel _appModel;
        private readonly Action<byte[]> _sendPgnToLoop;

        private DateTimeOffset timestamp;

        private bool sectionControlEnabled;
        private int numClients;
        private bool[] actualSectionStates;

        private int lastGuidanceLineDeviation;
        private DateTimeOffset guidanceLineDeviationTime;
        private int lastActualSpeed;
        private DateTimeOffset actualSpeedTime;
        private int lastTotalDistance;
        private DateTimeOffset totalDistanceTime;

        // [XPLAT] View-model-bindable projection of the on-screen ISOBUS section-control button, replacing the
        // former direct WinForms writes to FormGPS.btnIsobusSectionControl (.Image x3, .Visible x1). The bound
        // Avalonia view-model reads these properties and refreshes on SectionControlButtonChanged: it maps the
        // tri-state to the button icon and binds the button's visibility to IsSectionControlButtonVisible. This
        // mirrors the PgnDispatcher event-based UI-projection pattern (e.g. event Action<string> OnHardwareMessage,
        // which "was the WinForms lblHardwareMessage label"). The idle/on/off and visible/hidden semantics are
        // unchanged. See MIGRATION_DOCS/TRANSITION_MAP.md.

        /// <summary>
        /// [XPLAT] Current tri-state icon the bound view-model renders for the ISOBUS section-control button
        /// (was the three <c>mf.btnIsobusSectionControl.Image</c> assignments). Defaults to
        /// <see cref="IsobusSectionControlButtonState.Idle"/>.
        /// </summary>
        public IsobusSectionControlButtonState SectionControlButtonState { get; private set; }
            = IsobusSectionControlButtonState.Idle;

        /// <summary>
        /// [XPLAT] Whether the bound view-model shows the ISOBUS section-control button — true while the Task
        /// Controller heartbeat is alive (was <c>mf.btnIsobusSectionControl.Visible</c>).
        /// </summary>
        public bool IsSectionControlButtonVisible { get; private set; }

        /// <summary>
        /// [XPLAT] Raised whenever <see cref="SectionControlButtonState"/> or
        /// <see cref="IsSectionControlButtonVisible"/> changes, so the bound view-model can refresh the button.
        /// </summary>
        public event Action SectionControlButtonChanged;

        /// <summary>
        /// [XPLAT] Constructs the ISOBUS / machine-PGN builder with its injected collaborators instead of the
        /// WinForms host form (was <c>CISOBUS(FormGPS _f)</c>).
        /// </summary>
        /// <param name="appModel">
        /// Shared AgOpenGPS.Core runtime model — supplies the field-job gate (<c>isJobStarted</c>) and the
        /// active field folder name (<c>Fields.CurrentFieldName</c>) previously read from <c>FormGPS</c>.
        /// </param>
        /// <param name="sendPgnToLoop">
        /// The UDP/PGN loopback transmit delegate (was <c>mf.SendPgnToLoop</c>). The composition root wires it to
        /// <c>AgOpenGPS.Services.PgnDispatcher.SendPgnToLoop</c>; the same byte[] frame is sent to the same AgIO
        /// peer (127.255.255.255:17777) with the same trailing additive-checksum CRC applied there.
        /// </param>
        public CISOBUS(ApplicationModel appModel, Action<byte[]> sendPgnToLoop)
        {
            // constructor — inject the shared model + the loopback transmit path (no FormGPS back-reference)
            _appModel = appModel ?? throw new ArgumentNullException(nameof(appModel));
            _sendPgnToLoop = sendPgnToLoop ?? throw new ArgumentNullException(nameof(sendPgnToLoop));
        }

        public bool IsSectionOn(int section)
        {
            // If no section states available, don't override - return false to let AOG control sections
            if (actualSectionStates == null || actualSectionStates.Length == 0)
            {
                return false;
            }
            if (section < actualSectionStates.Length)
            {
                return actualSectionStates[section];
            }
            return false;
        }

        public void RequestSectionControlEnabled(bool enabled)
        {
            // Send the request
            byte[] data = new byte[7];
            data[0] = 0x80; // standard AIO header
            data[1] = 0x81; // PGN header
            data[2] = 0x7F; // SRC address
            data[3] = 0xF1; // PGN
            data[4] = 1; // Length
            data[5] = (byte)(enabled ? 0x01 : 0x00); // Section control enabled request
            // [XPLAT] was mf.SendPgnToLoop(data) — same frame, same loopback peer (17777); CRC applied by the dispatcher
            _sendPgnToLoop(data);
        }

        /// <summary>
        /// Send the active field folder name to TC (PGN 0xF3).
        /// Call with empty string when field is closed.
        /// </summary>
        public void SendFieldName(string fieldName)
        {
            byte[] nameBytes = string.IsNullOrEmpty(fieldName)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(fieldName);

            // Cap at 248 bytes to stay within PGN length byte range
            if (nameBytes.Length > 248) nameBytes = nameBytes.Take(248).ToArray();

            byte[] message = new byte[6 + nameBytes.Length];
            message[0] = 0x80; // standard AgIO header
            message[1] = 0x81;
            message[2] = 0x7F; // SRC address
            message[3] = 0xF3; // PGN: field name
            message[4] = (byte)nameBytes.Length; // 0 = field closed
            Array.Copy(nameBytes, 0, message, 5, nameBytes.Length);
            // [XPLAT] was mf.SendPgnToLoop(message) — same frame, same loopback peer (17777); CRC applied by the dispatcher
            _sendPgnToLoop(message);
        }

        private void SendProcessData(ushort identifier, int data)
        {
            byte[] dataBytes = BitConverter.GetBytes(data);
            byte[] message = new byte[12];
            message[0] = 0x80; // standard AIO header
            message[1] = 0x81; // PGN header
            message[2] = 0x7F; // SRC address
            message[3] = 0xF2; // PGN
            message[4] = 6; // Length
            message[5] = (byte)(identifier & 0xFF);
            message[6] = (byte)(identifier >> 8);
            message[7] = dataBytes[0];
            message[8] = dataBytes[1];
            message[9] = dataBytes[2];
            message[10] = dataBytes[3];
            // [XPLAT] was mf.SendPgnToLoop(message) — same frame, same loopback peer (17777); CRC applied by the dispatcher
            _sendPgnToLoop(message);
        }

        public void SetGuidanceLineDeviation(int deviation)
        {
            if (deviation == lastGuidanceLineDeviation)
            {
                return;
            }
            if (DateTimeOffset.Now - guidanceLineDeviationTime < TimeSpan.FromMilliseconds(100))
            {
                return;
            }
            lastGuidanceLineDeviation = deviation;
            guidanceLineDeviationTime = DateTimeOffset.Now;
            SendProcessData(513, deviation);
        }

        public void SetActualSpeed(int speed)
        {
            if (speed == lastActualSpeed)
            {
                return;
            }
            if (DateTimeOffset.Now - actualSpeedTime < TimeSpan.FromMilliseconds(100))
            {
                return;
            }
            lastActualSpeed = speed;
            actualSpeedTime = DateTimeOffset.Now;
            SendProcessData(397, speed);
        }

        public void SetTotalDistance(int distance)
        {
            if (distance == lastTotalDistance)
            {
                return;
            }
            if (DateTimeOffset.Now - totalDistanceTime < TimeSpan.FromMilliseconds(100))
            {
                return;
            }
            lastTotalDistance = distance;
            totalDistanceTime = DateTimeOffset.Now;
            SendProcessData(597, distance);
        }

        public bool SectionControlEnabled
        {
            get => sectionControlEnabled;
            private set
            {
                if (sectionControlEnabled == value)
                    return;

                // Changed, act accordingly
                sectionControlEnabled = value;
                UpdateButtonImage();
            }
        }

        /// <summary>
        /// Number of clients connected to the Task Controller (0-7)
        /// </summary>
        public int NumClients => numClients;

        /// <summary>
        /// Number of sections reported by TC (0 = no section control capability)
        /// </summary>
        private int numberOfSections;

        private void UpdateButtonImage()
        {
            // 3 states:
            // - Idle: TC running but no implement connected (numberOfSections == 0)
            // - On: TC running with implement, section control enabled
            // - Off: TC running with implement, section control disabled
            // [XPLAT] was three mf.btnIsobusSectionControl.Image = Properties.Resources.IsobusSectionControl*
            // assignments; now sets the view-model-bindable tri-state and notifies the bound view (which maps
            // the state to the icon). The Idle/On/Off selection logic is unchanged.
            if (numberOfSections == 0)
            {
                SetSectionControlButtonState(IsobusSectionControlButtonState.Idle);
            }
            else if (sectionControlEnabled)
            {
                SetSectionControlButtonState(IsobusSectionControlButtonState.On);
            }
            else
            {
                SetSectionControlButtonState(IsobusSectionControlButtonState.Off);
            }
        }

        public bool IsAlive()
        {
            // Button visible = TC is running (heartbeat received within 1 second)
            // Button image indicates implement status (idle/on/off)
            bool isAlive = (timestamp != default &&
                           DateTimeOffset.Now - timestamp < TimeSpan.FromSeconds(1));

            // [XPLAT] was mf.btnIsobusSectionControl.Visible = isAlive; now drives the view-model-bindable
            // visibility flag (the bound view binds its visibility to this). Same heartbeat semantics.
            SetSectionControlButtonVisible(isAlive);

            return isAlive;
        }

        /// <summary>
        /// Check if TC has active clients with section control capability
        /// </summary>
        public bool HasActiveClients => numberOfSections > 0;

        // [XPLAT] Button-state mutators that update the view-model-bindable projection and raise
        // SectionControlButtonChanged only on an actual change. The replaced WinForms .Image / .Visible setters
        // were idempotent, so notifying only on transitions preserves the rendered result while avoiding
        // redundant refreshes; the public properties always reflect the latest computed state regardless.
        private void SetSectionControlButtonState(IsobusSectionControlButtonState state)
        {
            if (SectionControlButtonState == state)
                return;
            SectionControlButtonState = state;
            SectionControlButtonChanged?.Invoke();
        }

        private void SetSectionControlButtonVisible(bool visible)
        {
            if (IsSectionControlButtonVisible == visible)
                return;
            IsSectionControlButtonVisible = visible;
            SectionControlButtonChanged?.Invoke();
        }

        private static bool ReadBit(byte data, int bitIndex)
        {
            return (data & (1 << bitIndex)) != 0;
        }

        public bool DeserializeHeartbeat(byte[] data)
        {
            if (data.Length < 2)
            {
                // Make sure we can read at least the first bitmask and the number of sections
                return false;
            }

            // Detect reconnect: TC was dead (no heartbeat for >1s) and is now alive again
            bool wasAlive = timestamp != default && DateTimeOffset.Now - timestamp < TimeSpan.FromSeconds(1);
            if (!wasAlive)
                // [XPLAT] was mf.isJobStarted ? mf.currentFieldDirectory : string.Empty — same job-started gating;
                // the field folder name now comes from the shared Core ApplicationModel field service.
                SendFieldName(_appModel.isJobStarted ? _appModel.Fields.CurrentFieldName : string.Empty);

            // Extract fields from byte 0 (backward compatible):
            // Bit 0: Section control enabled (existing)
            // Bits 1-3: Number of clients (0-7) - may be 0 for older TC versions
            // Bits 4-7: Reserved
            bool newSectionControlEnabled = ReadBit(data[0], 0);
            int newNumClients = (data[0] >> 1) & 0x07;
            int newNumberOfSections = data[1];

            // Store client count for informational purposes (backward compatible: old TCs send 0)
            numClients = newNumClients;

            // If no sections, don't expect section states - show idle state
            // This handles: no clients, clients without sections, or old TCs that don't support section control
            if (newNumberOfSections == 0)
            {
                numberOfSections = 0;
                sectionControlEnabled = newSectionControlEnabled;
                actualSectionStates = null;
                timestamp = DateTimeOffset.Now;
                UpdateButtonImage();
                return true;
            }

            // Validate we have enough data for section states
            if (data.Length != 2 + (newNumberOfSections + 7) / 8)
            {
                // Make sure we have enough data to read all the section states
                return false;
            }

            // Has sections - proceed with section control (backward compatible with old TCs)
            this.numberOfSections = newNumberOfSections;
            this.SectionControlEnabled = newSectionControlEnabled;
            this.actualSectionStates = Enumerable.Range(0, newNumberOfSections)
                .Select(i => ReadBit(data[2 + (i / 8)], i % 8)) // Section states starts at the 2nd byte
                .ToArray();

            timestamp = DateTimeOffset.Now;
            return true;
        }
    }
}
