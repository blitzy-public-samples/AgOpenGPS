using System;
using System.Text;

namespace AgOpenGPS.Ipc
{
    /// <summary>
    /// Builds the legacy byte PGN frames <c>[0x80, 0x81, 0x7F, PGN, Length, Data..., CRC]</c> that the
    /// AgIO <c>CommandService</c> forwards to the hardware firmware for each AgOpenGPS -> AgIO outbound
    /// command / configuration RPC. The firmware-facing byte contract is unchanged by the IPC migration;
    /// only the transport (UDP -> gRPC) changed. This logic was extracted out of
    /// <c>AgIO.CommandServiceImpl</c> (a WinExe that the net48 test project cannot reference) into the
    /// shared <c>AgOpenGPS.Ipc</c> contract assembly so the exact PGN byte / length byte / payload offsets
    /// and the additive-byte CRC can be unit-tested directly — the coverage gap that previously let the
    /// PGN 0x64 corrected-position byte-contract regression slip through.
    ///
    /// <para>
    /// Each <c>Build*</c> method returns the frame with the CRC byte left as zero; the caller applies
    /// <see cref="ApplyCrc(byte[])"/> immediately before forwarding to the hardware (this mirrors the
    /// original <c>Forward</c> flow, where the CRC was applied once, centrally). Byte layouts are copied
    /// verbatim from the legacy producers so parity is preserved bit-for-bit.
    /// </para>
    /// </summary>
    // IPC-REFACTOR: new shared helper introduced by the UDP->gRPC IPC migration; centralizes the hardware
    // byte-frame construction + additive CRC that AgIO's CommandService forwards, so the firmware-facing byte
    // contract lives in one testable place (fixes the PGN 0x64 corrected-position byte-contract regression).
    public static class CommandFrameBuilder
    {
        /// <summary>PGN 0xFE — AutoSteer data (speed, status, commanded angle, line distance, sections 1-16).</summary>
        public static byte[] BuildAutoSteerData(AutoSteerDataMsg request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xFE, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            frame[5] = (byte)(request.Speed & 0xFF);
            frame[6] = (byte)((request.Speed >> 8) & 0xFF);
            frame[7] = (byte)request.Status;
            frame[8] = (byte)(request.CommandedSteerAngle & 0xFF);
            frame[9] = (byte)((request.CommandedSteerAngle >> 8) & 0xFF);
            frame[10] = (byte)request.LineDistance;
            frame[11] = (byte)request.SectionControl18;
            frame[12] = (byte)request.SectionControl916;
            return frame;
        }

        /// <summary>PGN 0xFC — AutoSteer settings (Kp, PWM levels, counts/degree, WAS offset, Ackermann).</summary>
        public static byte[] BuildAutoSteerSettings(AutoSteerSettingsMsg request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xFC, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            frame[5] = (byte)request.GainProportionalKp;
            frame[6] = (byte)request.HighPwm;
            frame[7] = (byte)request.LowPwm;
            frame[8] = (byte)request.MinPwm;
            frame[9] = (byte)request.CountsPerDegree;
            frame[10] = (byte)(request.WasOffset & 0xFF);
            frame[11] = (byte)((request.WasOffset >> 8) & 0xFF);
            frame[12] = (byte)request.Ackerman;
            return frame;
        }

        /// <summary>PGN 0xFB — AutoSteer config.</summary>
        public static byte[] BuildAutoSteerConfig(AutoSteerConfigMsg request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xFB, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            frame[5] = (byte)request.Set0;
            frame[6] = (byte)request.MaxPulse;
            frame[7] = (byte)request.MinSpeed;
            frame[8] = (byte)request.AckermanFix;
            frame[9] = (byte)request.AngularVelocity;
            return frame;
        }

        /// <summary>PGN 0xEF — Machine data (U-turn, speed, hydraulic lift, tram, geo-stop, sections).</summary>
        public static byte[] BuildMachineData(MachineDataMsg request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xEF, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            frame[5] = (byte)request.UTurn;
            frame[6] = (byte)request.Speed;
            frame[7] = (byte)request.HydLift;
            frame[8] = (byte)request.Tram;
            frame[9] = (byte)request.GeoStop;
            frame[11] = (byte)request.SectionControl18;
            frame[12] = (byte)request.SectionControl916;
            return frame;
        }

        /// <summary>PGN 0xEE — Machine config (hydraulic raise/lower times, user bytes).</summary>
        public static byte[] BuildMachineConfig(MachineConfigMsg request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xEE, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            frame[5] = (byte)request.RaiseTime;
            frame[6] = (byte)request.LowerTime;
            frame[7] = (byte)request.EnableHyd;
            frame[8] = (byte)request.Set0;
            frame[9] = (byte)request.User1;
            frame[10] = (byte)request.User2;
            frame[11] = (byte)request.User3;
            frame[12] = (byte)request.User4;
            return frame;
        }

        /// <summary>PGN 0xEC — Relay config (pin map, up to 24 relays), 30-byte frame / 24 data bytes.</summary>
        public static byte[] BuildRelayConfig(RelayConfigMsg request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            byte[] frame = new byte[30];
            frame[0] = 0x80;
            frame[1] = 0x81;
            frame[2] = 0x7F;
            frame[3] = 0xEC;
            frame[4] = 24;
            for (int i = 0; i < request.PinConfig.Count && i < 24; i++)
            {
                frame[5 + i] = (byte)request.PinConfig[i];
            }
            return frame;
        }

        /// <summary>PGN 0xEB — Section dimensions (widths, up to 16 sections), 39-byte frame / 33 data bytes.</summary>
        public static byte[] BuildSectionDimensions(SectionDimensionsMsg request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            byte[] frame = new byte[39];
            frame[0] = 0x80;
            frame[1] = 0x81;
            frame[2] = 0x7F;
            frame[3] = 0xEB;
            frame[4] = 33;
            for (int i = 0; i < request.SectionWidths.Count && i < 16; i++)
            {
                uint width = request.SectionWidths[i];
                frame[5 + (i * 2)] = (byte)(width & 0xFF);
                frame[6 + (i * 2)] = (byte)((width >> 8) & 0xFF);
            }
            frame[37] = (byte)request.NumSections;
            return frame;
        }

        /// <summary>PGN 0xE5 — Extended section control (up to 64 sections + tool L/R speed), 16-byte frame / 10 data bytes.</summary>
        public static byte[] BuildExtendedSectionControl(ExtendedSectionControlMsg request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xE5, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            byte[] sections = BitConverter.GetBytes(request.Sections);
            Array.Copy(sections, 0, frame, 5, 8);
            frame[13] = (byte)request.ToolLeftSpeed;
            frame[14] = (byte)request.ToolRightSpeed;
            return frame;
        }

        /// <summary>PGN 0xE4 — Rate control.</summary>
        public static byte[] BuildRateControl(RateControlMsg request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xE4, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            frame[5] = (byte)request.Rate0;
            frame[6] = (byte)request.Rate1;
            frame[7] = (byte)request.Rate2;
            return frame;
        }

        /// <summary>PGN 0xF1 — Section-control enable request, 7-byte frame / 1 data byte.</summary>
        public static byte[] BuildSectionControlEnable(SectionControlEnableMsg request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xF1, 1, 0, 0 };
            frame[5] = (byte)(request.Enabled ? 1 : 0);
            return frame;
        }

        /// <summary>PGN 0xF2 — ISOBUS process data (identifier + 4-byte value), 12-byte frame / 6 data bytes.</summary>
        public static byte[] BuildProcessData(ProcessDataMsg request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xF2, 6, 0, 0, 0, 0, 0, 0, 0 };
            frame[5] = (byte)(request.Identifier & 0xFF);
            frame[6] = (byte)((request.Identifier >> 8) & 0xFF);
            byte[] value = BitConverter.GetBytes(request.Value);
            Array.Copy(value, 0, frame, 7, 4);
            return frame;
        }

        /// <summary>PGN 0xF3 — ISOBUS field name (UTF-8, clamped to 248 bytes; empty = field closed).</summary>
        public static byte[] BuildFieldName(FieldNameMsg request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            byte[] nameBytes = Encoding.UTF8.GetBytes(request.Name);
            if (nameBytes.Length > 248)
            {
                byte[] clamped = new byte[248];
                Array.Copy(nameBytes, clamped, 248);
                nameBytes = clamped;
            }
            byte[] frame = new byte[5 + nameBytes.Length + 1];
            frame[0] = 0x80;
            frame[1] = 0x81;
            frame[2] = 0x7F;
            frame[3] = 0xF3;
            frame[4] = (byte)nameBytes.Length;
            Array.Copy(nameBytes, 0, frame, 5, nameBytes.Length);
            return frame;
        }

        /// <summary>PGN 0xD0 — Encoded lat/lon (two 4-byte encoded values).</summary>
        public static byte[] BuildLatLon(LatLonMsg request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xD0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            byte[] latitude = BitConverter.GetBytes(request.LatitudeEncoded);
            Array.Copy(latitude, 0, frame, 5, 4);
            byte[] longitude = BitConverter.GetBytes(request.LongitudeEncoded);
            Array.Copy(longitude, 0, frame, 9, 4);
            return frame;
        }

        /// <summary>
        /// PGN 0x64 — Corrected position (AOG -> AgIO). ALWAYS a 30-byte frame carrying 24 data bytes:
        /// longitude (double) at offset 5-12, latitude (double) at offset 13-20, and fix2fix heading
        /// (double) at offset 21-28. The legacy producer (GPS <c>Position.designer.cs</c>) always wrote
        /// all three doubles — including the sentinel heading value 1000 that marks an invalid heading —
        /// so the length byte (24) and the heading bytes are part of the firmware-facing byte contract and
        /// must NOT be dropped when the heading equals the sentinel. Emitting a shorter (16-data-byte) frame
        /// for the sentinel case, as an earlier revision did, changed the byte contract and was the PGN 0x64
        /// regression this builder fixes.
        /// </summary>
        public static byte[] BuildCorrectedPosition(CorrectedPositionMsg request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            byte[] frame = new byte[30];
            frame[0] = 0x80;
            frame[1] = 0x81;
            frame[2] = 0x7F;
            frame[3] = 0x64;
            frame[4] = 24;
            byte[] longitude = BitConverter.GetBytes(request.Longitude);
            Array.Copy(longitude, 0, frame, 5, 8);
            byte[] latitude = BitConverter.GetBytes(request.Latitude);
            Array.Copy(latitude, 0, frame, 13, 8);
            // Always write the fix2fix heading at offset 21-28, including the sentinel 1000 (invalid heading).
            byte[] heading = BitConverter.GetBytes(request.Fix2FixHeading);
            Array.Copy(heading, 0, frame, 21, 8);
            return frame;
        }

        /// <summary>
        /// Computes the legacy additive-byte CRC over bytes 2..N-2 and stores it in the final byte
        /// (index N-1). The hardware firmware still requires this CRC after the IPC transport migration.
        /// </summary>
        /// <param name="frame">The frame whose final byte receives the additive CRC.</param>
        public static void ApplyCrc(byte[] frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            byte crc = 0;
            for (int i = 2; i < frame.Length - 1; i++)
            {
                crc += frame[i];
            }
            frame[frame.Length - 1] = crc;
        }
    }
}
