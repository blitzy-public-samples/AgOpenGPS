// [XPLAT] migrated from net48/WinForms FormPGN — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Collections.Generic;
using AgOpenGPS.Core.ViewModels;

namespace AgIO.Views
{
    /// <summary>
    /// Display-only view-model backing <c>FormPGN.axaml</c>, the static, read-only
    /// "PGN Guide" reference window. The WinForms original (<c>FormPGN</c>) carried no
    /// behaviour beyond an OK button that closed the dialog; its entire content was a
    /// two-column reference table — PGN identifier and description — authored as static
    /// labels in the designer and resx. This view-model reproduces that table as bindable
    /// data so the Avalonia view can render it without any code-behind logic, and it also
    /// surfaces the frozen PGN wire-protocol constants documented in
    /// <c>docs/pgn-protocol.md</c> for convenient on-screen reference.
    /// </summary>
    /// <remarks>
    /// The dialog owns no commands, no mutable state, and no result: the hosting
    /// <c>Window</c> closes itself from its own code-behind, mirroring the WinForms
    /// <c>btnSerialOK_Click</c> handler that called <c>Close()</c>. Every member exposed
    /// here is immutable reference data. The PGN identifiers and descriptions are
    /// reproduced verbatim from the WinForms <c>FormPGN</c> designer and resx, and the
    /// protocol constants are reproduced verbatim from <c>docs/pgn-protocol.md</c>; both
    /// are FROZEN parity/contract data and must not be altered during the migration.
    /// </remarks>
    public class FormPGNViewModel : ViewModel
    {
        // Reference table reproduced verbatim from the WinForms FormPGN designer (label2,
        // decimal PGN ids) and FormPGN.resx (label3.Text descriptions), order preserved.
        // Stored once as a shared, immutable backing array. The four logical groups
        // (AutoSteer, Machine, Subnet, Hello) are presented as a single flat ordered list;
        // any visual grouping or spacing is the responsibility of FormPGN.axaml.
        private static readonly PgnReferenceEntry[] _entries =
        {
            new PgnReferenceEntry(254, "Steer Data"),
            new PgnReferenceEntry(253, "From AutoSteer Module"),
            new PgnReferenceEntry(252, "Steer Settings"),
            new PgnReferenceEntry(251, "Steer Config"),
            new PgnReferenceEntry(250, "From AutoSteer Sensors"),
            new PgnReferenceEntry(239, "Machine Data"),
            new PgnReferenceEntry(238, "Machine Config"),
            new PgnReferenceEntry(237, "From Machine Module"),
            new PgnReferenceEntry(236, "Pin Config"),
            new PgnReferenceEntry(235, "Section Dimensions"),
            new PgnReferenceEntry(234, "Matt Switch Module"),
            new PgnReferenceEntry(229, "64 Section On Off"),
            new PgnReferenceEntry(203, "Subnet Scan Reply"),
            new PgnReferenceEntry(202, "Scan Request"),
            new PgnReferenceEntry(201, "Set Subnet"),
            new PgnReferenceEntry(200, "Hello Sent To All"),
            new PgnReferenceEntry(126, "Hello Steer Reply"),
            new PgnReferenceEntry(123, "Hello Machine Reply"),
            new PgnReferenceEntry(121, "Hello IMU Reply"),
            new PgnReferenceEntry(120, "Hello GPS Reply"),
        };

        // --------------------------------------------------------------------------------
        // FROZEN PGN wire-protocol contract — reproduced verbatim from docs/pgn-protocol.md.
        // These describe the byte framing shared by AgIO and AgOpenGPS over the UDP loopback
        // fabric and MUST NOT change: behavioural parity depends on byte-for-byte identical
        // frames, additive CRC, and the fixed loopback port pair.
        // --------------------------------------------------------------------------------

        /// <summary>Frame byte [0]: the standard AOG header (<c>0x80</c>).</summary>
        public const byte FrameHeaderByte0 = 0x80;

        /// <summary>Frame byte [1]: the PGN header (<c>0x81</c>).</summary>
        public const byte FrameHeaderByte1 = 0x81;

        /// <summary>Frame byte [2]: the source address (<c>0x7F</c>).</summary>
        public const byte FrameSourceAddress = 0x7F;

        /// <summary>The AgOpenGPS (AOG) UDP listen port on the loopback subnet.</summary>
        public const int AogLoopbackPort = 15555;

        /// <summary>The AgIO UDP endpoint port on the loopback subnet.</summary>
        public const int AgIoLoopbackPort = 17777;

        /// <summary>PGN identifier <c>0xD6</c> (214) — GPS Position Data.</summary>
        public const byte GpsPositionDataPgn = 0xD6;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormPGNViewModel"/> class. The
        /// reference window carries no state, so the constructor takes no arguments; it
        /// simply binds the immutable display data that the view renders.
        /// </summary>
        public FormPGNViewModel()
        {
            WindowTitle = "PGN Guide";
            PgnColumnHeader = "PGN";
            DescriptionColumnHeader = "Description";
            PgnReferenceEntries = _entries;
            FrameStructureDescription = "[0]=0x80 (AOG header), [1]=0x81 (PGN header), [2]=0x7F (source addr), [3]=PGN id, [4]=data length, [5..]=payload, [last]=CRC";
            CrcDescription = "CRC = additive sum of bytes index 2..(len-2)";
        }

        /// <summary>
        /// Gets the window title shown in the dialog chrome ("PGN Guide"), reproduced from
        /// the WinForms <c>FormPGN.Text</c>.
        /// </summary>
        public string WindowTitle { get; }

        /// <summary>
        /// Gets the heading for the identifier column ("PGN"), reproduced from the WinForms
        /// <c>lblMessage2</c> label.
        /// </summary>
        public string PgnColumnHeader { get; }

        /// <summary>
        /// Gets the heading for the description column ("Description"), reproduced from the
        /// WinForms <c>label1</c> label.
        /// </summary>
        public string DescriptionColumnHeader { get; }

        /// <summary>
        /// Gets the read-only PGN reference table: the ordered (identifier, description)
        /// rows displayed by the guide. Reproduced verbatim from the WinForms <c>FormPGN</c>
        /// designer and resx, and frozen for migration parity.
        /// </summary>
        public IReadOnlyList<PgnReferenceEntry> PgnReferenceEntries { get; }

        /// <summary>
        /// Gets a human-readable summary of the PGN frame layout, reproduced verbatim from
        /// <c>docs/pgn-protocol.md</c>.
        /// </summary>
        public string FrameStructureDescription { get; }

        /// <summary>
        /// Gets a human-readable description of the CRC checksum rule, reproduced verbatim
        /// from <c>docs/pgn-protocol.md</c>.
        /// </summary>
        public string CrcDescription { get; }

        /// <summary>
        /// An immutable row of the PGN reference table: a single PGN identifier paired with
        /// its human-readable description. Reproduced verbatim from the WinForms
        /// <c>FormPGN</c> reference content.
        /// </summary>
        public sealed class PgnReferenceEntry
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="PgnReferenceEntry"/> class.
            /// </summary>
            /// <param name="pgnId">
            /// The PGN identifier carried in frame byte [3]; a value in the 0–255 byte range.
            /// </param>
            /// <param name="description">The human-readable description of the message.</param>
            public PgnReferenceEntry(byte pgnId, string description)
            {
                PgnId = pgnId;
                Description = description;
            }

            /// <summary>
            /// Gets the PGN identifier (the value carried in frame byte [3]).
            /// </summary>
            public byte PgnId { get; }

            /// <summary>
            /// Gets the human-readable description of the PGN message.
            /// </summary>
            public string Description { get; }
        }
    }
}
