// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// PgnDispatcher is the cross-platform extraction of the AgOpenGPS UDP transport and PGN
// (Parameter Group Number) message layer. It was lifted verbatim (behavior FROZEN) out of the
// former WinForms god-object FormGPS partial classes:
//   * SourceCode/GPS/Forms/UDPComm.Designer.cs — the loopback UDP socket, the async
//     receive/decode loop, the per-fix 70 ms throttle, the additive-checksum CRC, and the send path.
//   * SourceCode/GPS/Forms/PGN.Designer.cs      — the nested CPGN_* message templates and the
//     shared p_* instances that the steer/machine/section paths populate before sending.
//
// The on-wire contract is BYTE-FOR-BYTE preserved (docs/pgn-protocol.md L9–L53): header
// 0x80 0x81 0x7F, single-byte PGN id, single-byte length, payload, trailing additive-checksum CRC;
// loopback bind on 127.0.0.1:15555 and the AgIO peer on 127.255.255.255:17777. Verified by the
// Parity golden suite (PgnFrameGoldenTests).
//
// Decoupling notes (AAP §0.6.1): every former direct FormGPS member access becomes a
// constructor-injected collaborator (_ahrs/_mc/_pn/_isobus/_trk/_appModel). The WinForms
// BeginInvoke UI-thread marshal becomes an injected Action<Action> postToUi delegate (the
// composition root passes Avalonia.Threading.Dispatcher.UIThread.Post). Window-lifecycle code
// (WndProc/OnShown/etc.) is intentionally NOT ported — it belongs to the Avalonia view. The
// PgnDispatcher↔PositionService and PgnDispatcher↔SectionService runtime cycles are resolved by
// the composition root through late-bound delegates/events (OnGpsFixReady / OnRemoteSwitchChanged),
// introducing no new interface (AAP constraint: limit new architectural surface).
using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using AgLibrary.Logging;
using AgOpenGPS.Core;          // ApplicationModel (shared Core runtime state) — matches the CNMEA migration template
using AgOpenGPS.Core.Models;   // Wgs84 / GeoCoord (the WGS84→local-plane conversion types)

namespace AgOpenGPS.Services
{
    // =================================================================================================
    // PGN message templates — relocated VERBATIM from FormGPS's nested classes in PGN.Designer.cs.
    // Every byte array, index field, and constructor body is part of the frozen wire contract and must
    // not drift. They live as top-level classes in AgOpenGPS.Services so PgnDispatcher, the sibling
    // services (PositionService/SectionService), and the Parity golden tests can all reference them.
    // =================================================================================================

    /// <summary>
    /// PGN 0xD0 (208) — Latitude/Longitude, 8 bytes as a modified integer-encoded float
    /// (lat = encodedAngle / (0x7FFFFFFF / 90.0); lon = encodedAngle / (0x7FFFFFFF / 180.0)).
    /// </summary>
    public class CPGN_D0
    {
        /// <summary>
        ///  Latitude Longitude 8 bytes as modified float
        ///  double lat = (encodedAngle / (0x7FFFFFFF / 90.0));
        ///  double lon = (encodedAngle / (0x7FFFFFFF / 180.0));
        /// </summary>
        public byte[] latLong = new byte[] { 0x80, 0x81, 0x7F, 0xD0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };

        public void LoadLatitudeLongitude(double lat, double lon)
        {
            int encodedAngle = (int)(lat * (0x7FFFFFFF / 90.0));
            //double angle = (encodedAngle / (0x7FFFFFFF / 90.0));

            byte[] lat6 = BitConverter.GetBytes(encodedAngle);
            Array.Copy(lat6, 0, latLong, 5, 4);

            encodedAngle = (int)(lon * (0x7FFFFFFF / 180.0));
            //double angle = (encodedAngle / (0x7FFFFFFF / 180.0));

            lat6 = BitConverter.GetBytes(encodedAngle);
            Array.Copy(lat6, 0, latLong, 9, 4);
        }
    }

    /// <summary>PGN 0xFE (254) — AutoSteerData (speed, status, steer angle, line distance, section bytes).</summary>
    public class CPGN_FE
    {
        /// <summary>
        /// 8 bytes
        /// </summary>
        public byte[] pgn = new byte[] { 0x80, 0x81, 0x7f, 0xFE, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
        public int speedLo = 5;
        public int speedHi = 6;
        public int status = 7;
        public int steerAngleLo = 8;
        public int steerAngleHi = 9;
        public int lineDistance = 10;
        public int sc1to8 = 11;
        public int sc9to16 = 12;

        public void Reset()
        {
        }
    }

    /// <summary>PGN 0xFD (253) — From steer module (actual angle, heading, roll, switch status, pwm).</summary>
    public class CPGN_FD
    {
        /// <summary>
        /// From steer module
        /// </summary>
        public byte[] pgn = new byte[] { 0x80, 0x81, 0x7f, 0xFD, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
        public int actualLo = 5;
        public int actualHi = 6;
        public int headLo = 7;
        public int headHi = 8;
        public int rollLo = 9;
        public int rollHi = 10;
        public int switchStatus = 11;
        public int pwm = 12;

        public void Reset()
        {
        }
    }

    /// <summary>
    /// PGN 0xFC (252) — AutoSteer Settings. Constructor seeds the payload from the persisted
    /// VehicleSettings (gains/PWM/counts-per-degree/WAS-offset/ackerman). Behavior FROZEN.
    /// </summary>
    public class CPGN_FC
    {
        /// <summary>
        /// PGN - 252 - FC gainProportional=5 HighPWM=6  LowPWM = 7 MinPWM = 8 
        /// CountsPerDegree = 9 wasOffsetHi = 10 wasOffsetLo = 11 
        /// </summary>
        public byte[] pgn = new byte[] { 0x80, 0x81, 0x7f, 0xFC, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
        public int gainProportional = 5;
        public int highPWM = 6;
        public int lowPWM = 7;
        public int minPWM = 8;
        public int countsPerDegree = 9;
        public int wasOffsetLo = 10;
        public int wasOffsetHi = 11;
        public int ackerman = 12;

        public CPGN_FC()
        {
            pgn[gainProportional] = Properties.VehicleSettings.Default.setAS_Kp;
            pgn[highPWM] = Properties.VehicleSettings.Default.setAS_highSteerPWM;
            pgn[lowPWM] = Properties.VehicleSettings.Default.setAS_lowSteerPWM;
            pgn[minPWM] = Properties.VehicleSettings.Default.setAS_minSteerPWM;
            pgn[countsPerDegree] = Properties.VehicleSettings.Default.setAS_countsPerDegree;
            pgn[wasOffsetHi] = unchecked((byte)(Properties.VehicleSettings.Default.setAS_wasOffset >> 8));
            pgn[wasOffsetLo] = unchecked((byte)(Properties.VehicleSettings.Default.setAS_wasOffset));
            pgn[ackerman] = Properties.VehicleSettings.Default.setAS_ackerman;
        }

        public void Reset()
        {
        }
    }

    /// <summary>PGN 0xFB (251) — Autosteer board config (set0/maxPulse/minSpeed/set1/angVel); ctor zeros them.</summary>
    public class CPGN_FB
    {
        /// <summary>
        /// 
        /// PGN - 251 - FB 
        /// set0=5 maxPulse = 6 minSpeed = 7 ackermanFix = 8
        /// </summary>
        public byte[] pgn = new byte[] { 0x80, 0x81, 0x7f, 0xFB, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
        public int set0 = 5;
        public int maxPulse = 6;
        public int minSpeed = 7;
        public int set1 = 8;
        public int angVel = 9;
        //public int  = 10;
        //public int  = 11;
        //public int  = 12;

        public CPGN_FB()
        {
            pgn[set0] = 0;
            pgn[maxPulse] = 0;
            pgn[minSpeed] = 0;
            pgn[set1] = 0;
            pgn[angVel] = 0;
        }

        public void Reset()
        {
        }
    }

    /// <summary>PGN 0xEF (239) — Machine Data (uturn/speed/hydLift/tram/geoStop + section bytes).</summary>
    public class CPGN_EF
    {
        /// <summary>
        /// PGN - 239 - EF 
        /// uturn=5  tree=6  hydLift = 8 
        /// </summary>
        public byte[] pgn = new byte[] { 0x80, 0x81, 0x7f, 0xEF, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
        public int uturn = 5;
        public int speed = 6;
        public int hydLift = 7;
        public int tram = 8;
        public int geoStop = 9; //out of bounds etc
        //public int  = 10;
        public int sc1to8 = 11;
        public int sc9to16 = 12;

        public CPGN_EF()
        {
        }

        public void Reset()
        {
        }
    }

    /// <summary>PGN 0xE5 (229) — Section symmetric (10-byte payload: section bytes 1..64 + tool L/R speeds).</summary>
    public class CPGN_E5
    {
        /// <summary>
        /// PGN - 229 - E5 
        /// </summary>
        public byte[] pgn = new byte[] { 0x80, 0x81, 0x7f, 0xE5, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
        public int sc1to8 = 5;
        public int sc9to16 = 6;
        public int sc17to24 = 7;
        public int sc25to32 = 8;
        public int sc33to40 = 9;
        public int sc41to48 = 10;
        public int sc49to56 = 11;
        public int sc57to64 = 12;
        public int toolLSpeed = 13;
        public int toolRSpeed = 14;

        public CPGN_E5()
        {
        }

        public void Reset()
        {
        }
    }

    /// <summary>
    /// PGN 0xEE (238) — Machine config (raise/lower times, hyd enable, user bytes). Constructor seeds
    /// from ToolSettings; MakeCRC() applies the additive checksum over bytes [2 .. len-2]. Behavior FROZEN.
    /// </summary>
    public class CPGN_EE
    {
        /// <summary>
        /// PGN - 238 - EE 
        /// raiseTime=5  lowerTime=6   enableHyd= 7 set0 = 8
        /// </summary>
        public byte[] pgn = new byte[] { 0x80, 0x81, 0x7f, 0xEE, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
        public int raiseTime = 5;
        public int lowerTime = 6;
        public int enableHyd = 7;
        public int set0 = 8;
        public int user1 = 9;
        public int user2 = 10;
        public int user3 = 11;
        public int user4 = 12;

        // PGN  - 127.239 0x7FEF
        int crc = 0;

        public CPGN_EE()
        {
            pgn[raiseTime] = Properties.ToolSettings.Default.setArdMac_hydRaiseTime;
            pgn[lowerTime] = Properties.ToolSettings.Default.setArdMac_hydLowerTime;
            pgn[enableHyd] = Properties.ToolSettings.Default.setArdMac_isHydEnabled;
            pgn[set0] = Properties.ToolSettings.Default.setArdMac_setting0;

            pgn[user1] = Properties.ToolSettings.Default.setArdMac_user1;
            pgn[user2] = Properties.ToolSettings.Default.setArdMac_user2;
            pgn[user3] = Properties.ToolSettings.Default.setArdMac_user3;
            pgn[user4] = Properties.ToolSettings.Default.setArdMac_user4;
        }

        public void MakeCRC()
        {
            crc = 0;
            for (int i = 2; i < pgn.Length - 1; i++)
            {
                crc += pgn[i];
            }
            pgn[pgn.Length - 1] = (byte)crc;
        }

        public void Reset()
        {
        }
    }

    /// <summary>
    /// PGN 0xEC (236) — Relay config. Constructor parses the 24-entry comma-separated pin map from
    /// ToolSettings.setRelay_pinConfig. [XPLAT] int.Parse uses CultureInfo.InvariantCulture so the
    /// pin map round-trips identically under any OS locale (the net48 original used the default parse).
    /// </summary>
    public class CPGN_EC
    {
        /// <summary>
        /// PGN - 236 - EC
        /// Pin conifg 1 to 20
        /// </summary>
        public byte[] pgn = new byte[] { 0x80, 0x81, 0x7f, 0xEC, 24,
                                    0, 0, 0, 0, 0, 0, 0, 0,
                                    0, 0, 0, 0, 0, 0, 0, 0,
                                    0, 0, 0, 0, 0, 0, 0, 0, 0xCC };

        //where in the pgn is which pin
        public int pin0 = 5;
        public int pin1 = 6;
        public int pin2 = 7;
        public int pin3 = 8;
        public int pin4 = 9;
        public int pin5 = 10;
        public int pin6 = 11;
        public int pin7 = 12;
        public int pin8 = 13;
        public int pin9 = 14;

        public int pin10 = 15;
        public int pin11 = 16;
        public int pin12 = 17;
        public int pin13 = 18;
        public int pin14 = 19;
        public int pin15 = 20;
        public int pin16 = 21;

        public int pin17 = 22;
        public int pin18 = 23;
        public int pin19 = 24;
        public int pin20 = 25;
        public int pin21 = 26;
        public int pin22 = 27;
        public int pin23 = 28;

        // PGN  - 127.237 0x7FED
        int crc = 0;

        public CPGN_EC()
        {
            string[] words;

            words = Properties.ToolSettings.Default.setRelay_pinConfig.Split(',');

            // [XPLAT] InvariantCulture pins the integer parse to the wire-contract semantics regardless
            // of the host locale (the net48 original relied on the thread's default culture).
            pgn[pin0] = (byte)int.Parse(words[0], CultureInfo.InvariantCulture);
            pgn[pin1] = (byte)int.Parse(words[1], CultureInfo.InvariantCulture);
            pgn[pin2] = (byte)int.Parse(words[2], CultureInfo.InvariantCulture);
            pgn[pin3] = (byte)int.Parse(words[3], CultureInfo.InvariantCulture);
            pgn[pin4] = (byte)int.Parse(words[4], CultureInfo.InvariantCulture);
            pgn[pin5] = (byte)int.Parse(words[5], CultureInfo.InvariantCulture);
            pgn[pin6] = (byte)int.Parse(words[6], CultureInfo.InvariantCulture);
            pgn[pin7] = (byte)int.Parse(words[7], CultureInfo.InvariantCulture);
            pgn[pin8] = (byte)int.Parse(words[8], CultureInfo.InvariantCulture);
            pgn[pin9] = (byte)int.Parse(words[9], CultureInfo.InvariantCulture);

            pgn[pin10] = (byte)int.Parse(words[10], CultureInfo.InvariantCulture);
            pgn[pin11] = (byte)int.Parse(words[11], CultureInfo.InvariantCulture);
            pgn[pin12] = (byte)int.Parse(words[12], CultureInfo.InvariantCulture);
            pgn[pin13] = (byte)int.Parse(words[13], CultureInfo.InvariantCulture);
            pgn[pin14] = (byte)int.Parse(words[14], CultureInfo.InvariantCulture);
            pgn[pin15] = (byte)int.Parse(words[15], CultureInfo.InvariantCulture);
            pgn[pin16] = (byte)int.Parse(words[16], CultureInfo.InvariantCulture);
            pgn[pin17] = (byte)int.Parse(words[17], CultureInfo.InvariantCulture);
            pgn[pin18] = (byte)int.Parse(words[18], CultureInfo.InvariantCulture);
            pgn[pin19] = (byte)int.Parse(words[19], CultureInfo.InvariantCulture);

            pgn[pin20] = (byte)int.Parse(words[20], CultureInfo.InvariantCulture);
            pgn[pin21] = (byte)int.Parse(words[21], CultureInfo.InvariantCulture);
            pgn[pin22] = (byte)int.Parse(words[22], CultureInfo.InvariantCulture);
            pgn[pin23] = (byte)int.Parse(words[23], CultureInfo.InvariantCulture);

        }

        public void MakeCRC()
        {
            crc = 0;
            for (int i = 2; i < pgn.Length - 1; i++)
            {
                crc += pgn[i];
            }
            pgn[pgn.Length - 1] = (byte)crc;
        }

        public void Reset()
        {
        }
    }

    /// <summary>PGN 0xEB (235) — Section dimensions (16 sections, Lo/Hi each) + numSections; ctor zeros all.</summary>
    public class CPGN_EB
    {
        /// <summary>
        /// PGN - 235 - EB
        /// Section dimensions
        /// </summary>
        public byte[] pgn = new byte[] { 0x80, 0x81, 0x7f, 0xEB, 33,
                                    0, 0, 0, 0, 0, 0, 0, 0,
                                    0, 0, 0, 0, 0, 0, 0, 0,
                                    0, 0, 0, 0, 0, 0, 0, 0,
                                    0, 0, 0, 0, 0, 0, 0, 0,
                                    0, 0xCC };

        //where in the pgn is which pin
        public int sec0Lo = 5;
        public int sec1Lo = 7;
        public int sec2Lo = 9;
        public int sec3Lo = 11;
        public int sec4Lo = 13;
        public int sec5Lo = 15;
        public int sec6Lo = 17;
        public int sec7Lo = 19;
        public int sec8Lo = 21;
        public int sec9Lo = 23;
        public int sec10Lo = 25;
        public int sec11Lo = 27;
        public int sec12Lo = 29;
        public int sec13Lo = 31;
        public int sec14Lo = 33;
        public int sec15Lo = 35;

        public int sec0Hi = 6;
        public int sec1Hi = 8;
        public int sec2Hi = 10;
        public int sec3Hi = 12;
        public int sec4Hi = 14;
        public int sec5Hi = 16;
        public int sec6Hi = 18;
        public int sec7Hi = 20;
        public int sec8Hi = 22;
        public int sec9Hi = 24;
        public int sec10Hi = 26;
        public int sec11Hi = 28;
        public int sec12Hi = 30;
        public int sec13Hi = 32;
        public int sec14Hi = 34;
        public int sec15Hi = 36;

        public int numSections = 37;

        public CPGN_EB()
        {
            pgn[sec0Lo] = 0;
            pgn[sec1Lo] = 0;
            pgn[sec2Lo] = 0;
            pgn[sec3Lo] = 0;
            pgn[sec4Lo] = 0;
            pgn[sec5Lo] = 0;
            pgn[sec6Lo] = 0;
            pgn[sec7Lo] = 0;
            pgn[sec8Lo] = 0;
            pgn[sec9Lo] = 0;
            pgn[sec10Lo] = 0;
            pgn[sec11Lo] = 0;
            pgn[sec12Lo] = 0;
            pgn[sec13Lo] = 0;
            pgn[sec14Lo] = 0;
            pgn[sec15Lo] = 0;

            pgn[sec0Hi] = 0;
            pgn[sec1Hi] = 0;
            pgn[sec2Hi] = 0;
            pgn[sec3Hi] = 0;
            pgn[sec4Hi] = 0;
            pgn[sec5Hi] = 0;
            pgn[sec6Hi] = 0;
            pgn[sec7Hi] = 0;
            pgn[sec8Hi] = 0;
            pgn[sec9Hi] = 0;
            pgn[sec10Hi] = 0;
            pgn[sec11Hi] = 0;
            pgn[sec12Hi] = 0;
            pgn[sec13Hi] = 0;
            pgn[sec14Hi] = 0;
            pgn[sec15Hi] = 0;

            pgn[numSections] = 0;
        }

        public void Reset()
        {
        }
    }

    /// <summary>PGN 0xE4 (228) — rate bytes (rate0/rate1/rate2).</summary>
    public class CPGN_E4
    {
        /// <summary>
        /// 8 bytes
        /// </summary>
        public byte[] pgn = new byte[] { 0x80, 0x81, 0x7f, 0xE4, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
        public int rate0 = 5;
        public int rate1 = 6;
        public int rate2 = 7;
        //public int  = 6;
        //public int = 7;
        //public int gleLo = 8;
        //public int gleHi = 9;
        //public int tance = 10;
        //public int = 11;
        //public int  = 12;
    }

    /// <summary>
    /// [XPLAT] Cross-platform UDP transport + PGN message dispatcher, extracted (behavior FROZEN) from the
    /// WinForms FormGPS partials <c>UDPComm.Designer.cs</c> and <c>PGN.Designer.cs</c>. It owns the loopback
    /// socket, the per-fix throttle, the additive-checksum CRC, and the shared <c>p_*</c> PGN instances; it is
    /// the only component that <see cref="SendPgnToLoop(byte[])"/> sends. Sibling services populate the shared
    /// instances (<see cref="p_254"/> by PositionService; <see cref="p_239"/>/<see cref="p_229"/> by
    /// SectionService) and the 0xD6 receive path drives PositionService via <see cref="OnGpsFixReady"/>.
    /// All former FormGPS (<c>mf</c>) members are constructor-injected; no WinForms type is referenced.
    /// </summary>
    public class PgnDispatcher
    {
        // ---- Injected collaborators (former FormGPS `mf.*` access — AAP §0.6.1 state separation) ----
        private readonly CAHRS _ahrs;
        private readonly CModuleComm _mc;
        private readonly CNMEA _pn;
        private readonly CISOBUS _isobus;
        private readonly CTrack _trk;
        private readonly ApplicationModel _appModel;

        // [XPLAT] UI-thread marshal: replaces WinForms BeginInvoke((MethodInvoker)(...)). The composition
        // root supplies `a => Avalonia.Threading.Dispatcher.UIThread.Post(a)`; this file never references Avalonia.
        private readonly Action<Action> _postToUi;

        // [XPLAT] Error sink: replaces the WinForms FormDialog.Show(...) used on transport failures.
        private readonly Action<string> _reportError;

        // ---- App socket (loopback UDP) ----------------------------------------------------------------
        private Socket loopBackSocket;

        // [XPLAT] Endpoints of modules. Declared as EndPoint (not IPEndPoint) because endPointLoopBack is
        // passed by `ref` to BeginReceiveFrom/EndReceiveFrom, whose signature is `ref EndPoint`. The frozen
        // wire targets are unchanged: AgIO peer 127.255.255.255:17777, loopback receive on 127.0.0.1:15555.
        private EndPoint epAgIO = new IPEndPoint(IPAddress.Parse("127.255.255.255"), 17777);
        private EndPoint endPointLoopBack = new IPEndPoint(IPAddress.Loopback, 0);

        // Data stream (1024-byte receive buffer is part of the frozen transport).
        private byte[] loopBuffer = new byte[1024];

        // ---- Throttle / status counters (ported verbatim from UDPComm.Designer.cs) --------------------
        /// <summary>Count of GPS (0xD6) sentences dropped by the <see cref="udpWatchLimit"/> throttle.</summary>
        public int missedSentenceCount = 0;

        /// <summary>Per-fix throttle floor in milliseconds (frozen at 70 ms). 0xD6 frames arriving sooner are dropped.</summary>
        public int udpWatchLimit = 70;

        /// <summary>
        /// [XPLAT] Receive-pipeline "pink steer dot" watchdog counter (was a FormGPS field). Reset to 0 when a
        /// steer-module return frame (0xFD/253) arrives; incremented elsewhere by the periodic timer and read
        /// read-only by the steer-wizard telemetry. Kept on the dispatcher because the agent-prompt resets it
        /// without a collaborator prefix and no injected collaborator owns it.
        /// </summary>
        public int steerModuleConnectedCounter = 0;

        private readonly Stopwatch udpWatch = new Stopwatch();

        // ---- Shared PGN instances (owned here; populated by sibling services before sending) ----------
        /// <summary>autoSteerData PGN 0xFE (254) — populated by PositionService.</summary>
        public CPGN_FE p_254 { get; } = new CPGN_FE();

        /// <summary>autoSteerSettings PGN 0xFC (252).</summary>
        public CPGN_FC p_252 { get; } = new CPGN_FC();

        /// <summary>autoSteerConfig PGN 0xFB (251).</summary>
        public CPGN_FB p_251 { get; } = new CPGN_FB();

        /// <summary>machineData PGN 0xEF (239) — populated by SectionService.BuildMachineByte.</summary>
        public CPGN_EF p_239 { get; } = new CPGN_EF();

        /// <summary>machineConfig PGN 0xEE (238).</summary>
        public CPGN_EE p_238 { get; } = new CPGN_EE();

        /// <summary>relayConfig PGN 0xEC (236).</summary>
        public CPGN_EC p_236 { get; } = new CPGN_EC();

        /// <summary>section dimensions PGN 0xEB (235).</summary>
        public CPGN_EB p_235 { get; } = new CPGN_EB();

        /// <summary>rate PGN 0xE4 (228).</summary>
        public CPGN_E4 p_228 { get; } = new CPGN_E4();

        /// <summary>section symmetric PGN 0xE5 (229) — populated by SectionService.BuildMachineByte.</summary>
        public CPGN_E5 p_229 { get; } = new CPGN_E5();

        // LatitudeLongitude PGN 0xD0 (208) — commented out in the net48 source; kept optional.
        //public CPGN_D0 p_208 { get; } = new CPGN_D0();

        // ---- Late-bound collaboration hooks (composition root wires these after all services exist) ----
        /// <summary>
        /// [XPLAT] Settable hook invoked on the receive thread after a real GPS fix (0xD6) is decoded; the
        /// composition root assigns <c>positionService.UpdateFixPosition</c>. A settable delegate (not an event)
        /// breaks the PgnDispatcher↔PositionService construction cycle without introducing a new interface.
        /// </summary>
        public Action OnGpsFixReady;

        /// <summary>
        /// [XPLAT] Settable hook invoked when remote switches (0xEA/234) change; the composition root assigns
        /// <c>sectionService.DoRemoteSwitches</c> (was the direct FormGPS.DoRemoteSwitches() call).
        /// </summary>
        public Action OnRemoteSwitchChanged;

        /// <summary>[XPLAT] Hardware/display message (0xDD/221) — was the WinForms lblHardwareMessage label.</summary>
        public event Action<string> OnHardwareMessage;

        /// <summary>[XPLAT] Cycle guidance lines forward (0xDE/222) — was btnCycleLines.PerformClick().</summary>
        public event Action OnCycleLines;

        /// <summary>[XPLAT] Cycle guidance lines backward (0xDE/222) — was btnCycleLinesBk.PerformClick().</summary>
        public event Action OnCycleLinesBk;

        /// <summary>[XPLAT] Transport error notification — was a WinForms FormDialog.Show(...) on load/send failure.</summary>
        public event Action<string> OnError;

        /// <summary>
        /// Constructs the dispatcher with its injected collaborators. The PositionService/SectionService
        /// collaboration is wired afterwards via <see cref="OnGpsFixReady"/> / <see cref="OnRemoteSwitchChanged"/>
        /// to avoid a constructor cycle (AAP §0.6.1).
        /// </summary>
        /// <param name="ahrs">AHRS sensor state (was mf.ahrs).</param>
        /// <param name="mc">Module-comm state (was mf.mc).</param>
        /// <param name="pn">NMEA fix/heading/speed state (was mf.pn).</param>
        /// <param name="isobus">ISOBUS heartbeat handler (was mf.isobus).</param>
        /// <param name="trk">Track/guidance-line collection for remote nudge (was mf.trk).</param>
        /// <param name="appModel">Shared AgOpenGPS.Core model — current lat/lon, local plane, sentence counter (was mf.AppModel).</param>
        /// <param name="postToUi">UI-thread dispatch delegate (was WinForms BeginInvoke).</param>
        /// <param name="reportError">Error sink (was WinForms FormDialog.Show).</param>
        public PgnDispatcher(
            CAHRS ahrs,
            CModuleComm mc,
            CNMEA pn,
            CISOBUS isobus,
            CTrack trk,
            ApplicationModel appModel,
            Action<Action> postToUi,
            Action<string> reportError)
        {
            _ahrs = ahrs ?? throw new ArgumentNullException(nameof(ahrs));
            _mc = mc ?? throw new ArgumentNullException(nameof(mc));
            _pn = pn ?? throw new ArgumentNullException(nameof(pn));
            _isobus = isobus ?? throw new ArgumentNullException(nameof(isobus));
            _trk = trk ?? throw new ArgumentNullException(nameof(trk));
            _appModel = appModel ?? throw new ArgumentNullException(nameof(appModel));
            _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));
            _reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        }

        // [XPLAT] Single error path: forwards to the injected sink AND any OnError subscribers.
        private void ReportError(string message)
        {
            _reportError?.Invoke(message);
            OnError?.Invoke(message);
        }

        /// <summary>
        /// Starts the loopback UDP server: binds the receive socket to 127.0.0.1:15555 and begins the
        /// asynchronous receive loop. Behavior FROZEN from UDPComm.Designer.cs StartLoopbackServer().
        /// </summary>
        public void StartLoopbackServer()
        {
            try
            {
                // Initialise the socket
                loopBackSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                loopBackSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                loopBackSocket.Bind(new IPEndPoint(IPAddress.Loopback, 15555));
                loopBackSocket.BeginReceiveFrom(loopBuffer, 0, loopBuffer.Length, SocketFlags.None,
                    ref endPointLoopBack, new AsyncCallback(ReceiveAppData), null);

                // [XPLAT] nmea limiter — the net48 original started udpWatch once at FormGPS init
                // (FormGPS.cs:534). Start it here as the receive pipeline goes live so the first 0xD6 fix
                // sees ElapsedMilliseconds >= udpWatchLimit and is processed (the throttle is otherwise
                // self-restarting via Reset()/Start() inside the 0xD6 case).
                udpWatch.Start();

                Log.EventWriter("UDP Loopback network started: " + IPAddress.Loopback.ToString() + ":" + "15555");
            }
            catch (Exception ex)
            {
                // [XPLAT] was FormDialog.Show("UDP Server", ...): replaced by the injected error sink / OnError.
                ReportError("UDP Server Load Error: " + ex.Message);
                Log.EventWriter("Catch -> Load UDP Loopback Error: " + ex.ToString());
            }
        }

        /// <summary>
        /// Async receive completion: copies the datagram, re-arms the receive immediately, then marshals
        /// the decode onto the UI thread. Behavior FROZEN from UDPComm.Designer.cs ReceiveAppData().
        /// </summary>
        private void ReceiveAppData(IAsyncResult asyncResult)
        {
            try
            {
                // Receive all data
                int msgLen = loopBackSocket.EndReceiveFrom(asyncResult, ref endPointLoopBack);

                byte[] localMsg = new byte[msgLen];
                Array.Copy(loopBuffer, localMsg, msgLen);

                // Listen for more connections again...
                loopBackSocket.BeginReceiveFrom(loopBuffer, 0, loopBuffer.Length, SocketFlags.None,
                    ref endPointLoopBack, new AsyncCallback(ReceiveAppData), null);

                // [XPLAT] was BeginInvoke((MethodInvoker)(() => ReceiveFromAgIO(localMsg)));
                _postToUi(() => ReceiveFromAgIO(localMsg));
            }
            catch (Exception)
            {
                // [XPLAT] preserved as source — swallow (the net48 original had a commented-out MessageBox).
            }
        }

        /// <summary>
        /// Computes the trailing additive-checksum CRC over bytes [2 .. len-2] and asynchronously sends the
        /// frame to the AgIO peer. PUBLIC — sibling services call this. Behavior FROZEN (CRC is the
        /// authoritative additive checksum from UDPComm.Designer.cs SendPgnToLoop()).
        /// </summary>
        /// <param name="byteData">The full PGN frame (header + payload + a trailing CRC placeholder byte).</param>
        public void SendPgnToLoop(byte[] byteData)
        {
            if (loopBackSocket != null && byteData.Length > 2)
            {
                try
                {
                    int crc = 0;
                    for (int i = 2; i + 1 < byteData.Length; i++)
                    {
                        crc += byteData[i];
                    }
                    byteData[byteData.Length - 1] = (byte)crc;

                    loopBackSocket.BeginSendTo(byteData, 0, byteData.Length, SocketFlags.None,
                        epAgIO, new AsyncCallback(SendAsyncLoopData), null);
                }
                catch (Exception ex)
                {
                    // [XPLAT] route to the injected error sink (was a commented-out MessageBox in net48).
                    ReportError("UDP Send Error: " + ex.Message);
                }
            }
        }

        /// <summary>Async send completion. Behavior FROZEN from UDPComm.Designer.cs SendAsyncLoopData().</summary>
        public void SendAsyncLoopData(IAsyncResult asyncResult)
        {
            try
            {
                // [XPLAT] EndSendTo is the documented pairing for BeginSendTo on .NET 8 (the net48 source used
                // EndSend); behavior-neutral for the wire output, as the frame is already queued by BeginSendTo.
                loopBackSocket.EndSendTo(asyncResult);
            }
            catch (Exception)
            {
                // [XPLAT] preserved as source — swallow.
            }
        }

        /// <summary>
        /// Decodes an inbound PGN frame from AgIO and dispatches it to the injected collaborators. The header
        /// validation (0x80 0x81), length check, and additive checksum (CK_A) verification are FROZEN exactly
        /// as UDPComm.Designer.cs ReceiveFromAgIO(); every former `mf.*` access is now an injected collaborator.
        /// </summary>
        private void ReceiveFromAgIO(byte[] data)
        {
            if (data.Length > 4 && data[0] == 0x80 && data[1] == 0x81)
            {
                int Length = Math.Max((data[4]) + 5, 5);

                // [XPLAT] CP9 F2 (CWE-20): EXACT envelope validation. The net48 source accepted any
                // `data.Length > Length` (the declared frame plus arbitrary trailing bytes): a truncated frame
                // that declared a large payload was rejected, but an oversized/padded frame was processed. We now
                // require the datagram to be EXACTLY the declared size — header(5) + declared payload(data[4]) +
                // one trailing CRC byte = Length + 1 — so both truncated AND oversized/padded frames are rejected
                // before any fixed-offset read. ReceiveAppData sizes localMsg to the exact datagram length, so a
                // conformant AgIO frame always satisfies this; well-formed behavior is unchanged and only malformed
                // frames are dropped. Rejection is silent (no throw, no log-flood) exactly as the source CRC path.
                if (data.Length != Length + 1)
                {
                    return;
                }

                // Additive checksum (CK_A) over bytes [2 .. Length-1], compared to the trailing CRC at data[Length].
                // data.Length == Length + 1 guarantees data[Length] (the last byte) exists for this compare.
                byte CK_A = 0;
                for (int j = 2; j < Length; j++)
                {
                    CK_A += data[j];
                }

                if (data[Length] != (byte)CK_A)
                {
                    return;
                }

                switch (data[3])
                {
                    case 0xD6:
                        {
                            // [XPLAT] CP9 F2: per-PGN read-safety guard. This case reads fixed offsets up to
                            // BitConverter.ToInt16(data, 54) (bytes 54-55), so the frame must be at least 56 bytes.
                            // The exact envelope above guarantees data.Length == data[4] + 6, but a frame that
                            // declared a short payload (small data[4]) with a valid additive CRC would still reach
                            // these reads; this guard rejects it before any out-of-bounds access. A conformant 0xD6
                            // GPS frame is 57 bytes (data[4] = 0x33 = 51), so well-formed behavior is unchanged.
                            if (data.Length < 56)
                                break;

                            if (udpWatch.ElapsedMilliseconds < udpWatchLimit)
                            {
                                missedSentenceCount++;
                                return;
                            }
                            udpWatch.Reset();
                            udpWatch.Start();

                            double Lon = BitConverter.ToDouble(data, 5);
                            double Lat = BitConverter.ToDouble(data, 13);

                            if (Lon != double.MaxValue && Lat != double.MaxValue)
                            {
                                // [XPLAT] The net48 `if (timerSim.Enabled) DisableSim();` is intentionally NOT
                                // ported here: no simulator collaborator is injected into the transport
                                // dispatcher, and disabling the simulator on a real fix is the PositionService's
                                // responsibility (it owns UpdateFixPosition, invoked below via OnGpsFixReady).

                                _appModel.CurrentLatLon = new Wgs84(Lat, Lon);

                                GeoCoord fixCoord = _appModel.LocalPlane.ConvertWgs84ToGeoCoord(_appModel.CurrentLatLon);
                                _pn.fix.northing = fixCoord.Northing;
                                _pn.fix.easting = fixCoord.Easting;

                                //From dual antenna heading sentences
                                float temp = BitConverter.ToSingle(data, 21);
                                if (temp != float.MaxValue)
                                {
                                    _pn.headingTrueDual = temp + _pn.headingTrueDualOffset;
                                    if (_pn.headingTrueDual >= 360) _pn.headingTrueDual -= 360;
                                    else if (_pn.headingTrueDual < 0) _pn.headingTrueDual += 360;

                                    if (_ahrs.isDualAsIMU) _ahrs.imuHeading = _pn.headingTrueDual;
                                }

                                //from single antenna sentences (VTG,RMC)
                                _pn.headingTrue = BitConverter.ToSingle(data, 25);

                                //always save the speed.
                                temp = BitConverter.ToSingle(data, 29);
                                if (temp != float.MaxValue)
                                {
                                    _pn.vtgSpeed = temp;
                                }

                                //roll in degrees
                                temp = BitConverter.ToSingle(data, 33);
                                if (temp != float.MaxValue)
                                {
                                    if (_ahrs.isRollInvert) temp *= -1;
                                    _ahrs.imuRoll = temp - _ahrs.rollZero;
                                }
                                if (temp == float.MinValue)
                                    _ahrs.imuRoll = 0;

                                //altitude in meters
                                temp = BitConverter.ToSingle(data, 37);
                                if (temp != float.MaxValue)
                                    _pn.altitude = temp;

                                ushort sats = BitConverter.ToUInt16(data, 41);
                                if (sats != ushort.MaxValue)
                                    _pn.satellitesTracked = sats;

                                byte fix = data[43];
                                if (fix != byte.MaxValue)
                                    _pn.fixQuality = fix;

                                ushort hdop = BitConverter.ToUInt16(data, 44);
                                if (hdop != ushort.MaxValue)
                                    _pn.hdop = hdop * 0.01;

                                ushort age = BitConverter.ToUInt16(data, 46);
                                if (age != ushort.MaxValue)
                                    _pn.age = age * 0.01;

                                ushort imuHead = BitConverter.ToUInt16(data, 48);
                                if (imuHead != ushort.MaxValue)
                                {
                                    _ahrs.imuHeading = imuHead;
                                    _ahrs.imuHeading *= 0.1;
                                }

                                short imuRol = BitConverter.ToInt16(data, 50);
                                if (imuRol != short.MaxValue)
                                {
                                    double rollK = imuRol;
                                    if (_ahrs.isRollInvert) rollK *= -0.1;
                                    else rollK *= 0.1;
                                    rollK -= _ahrs.rollZero;
                                    _ahrs.imuRoll = _ahrs.imuRoll * _ahrs.rollFilter + rollK * (1 - _ahrs.rollFilter);
                                }

                                short imuPich = BitConverter.ToInt16(data, 52);
                                if (imuPich != short.MaxValue)
                                {
                                    _ahrs.imuPitch = imuPich;
                                }

                                short imuYaw = BitConverter.ToInt16(data, 54);
                                if (imuYaw != short.MaxValue)
                                {
                                    _ahrs.imuYawRate = imuYaw;
                                }

                                _appModel.sentenceCounter = 0;

                                // [XPLAT] was the direct UpdateFixPosition() call; now drives PositionService.
                                OnGpsFixReady?.Invoke();
                            }
                        }
                        break;

                    case 0xD3: //external IMU
                        {
                            if (data.Length != 14)
                                break;
                            if (_ahrs.imuRoll > 25 || _ahrs.imuRoll < -25) _ahrs.imuRoll = 0;
                            //Heading
                            _ahrs.imuHeading = (Int16)((data[6] << 8) + data[5]);
                            _ahrs.imuHeading *= 0.1;

                            //Roll
                            double rollK = (Int16)((data[8] << 8) + data[7]);

                            if (_ahrs.isRollInvert) rollK *= -0.1;
                            else rollK *= 0.1;
                            rollK -= _ahrs.rollZero;
                            _ahrs.imuRoll = _ahrs.imuRoll * _ahrs.rollFilter + rollK * (1 - _ahrs.rollFilter);

                            //Angular velocity
                            _ahrs.angVel = (Int16)((data[10] << 8) + data[9]);
                            _ahrs.angVel /= -2;

                            break;
                        }
                    case 0xD4: //imu disconnect pgn
                        {
                            // [XPLAT] CP9 F2: read-safety guard — this case reads data[5], so require >= 6 bytes
                            // before the access (defense-in-depth atop the exact envelope; conformant frames pass).
                            if (data.Length < 6)
                                break;
                            if (data[5] == 1)
                            {
                                _ahrs.imuHeading = 99999;

                                _ahrs.imuRoll = 88888;

                                _ahrs.angVel = 0;
                            }
                            break;
                        }
                    case 253: //return from autosteer module
                        {
                            //Steer angle actual
                            if (data.Length != 14)
                                break;
                            _mc.actualSteerAngleChart = (Int16)((data[6] << 8) + data[5]);
                            _mc.actualSteerAngleDegrees = (double)_mc.actualSteerAngleChart * 0.01;

                            //Heading
                            double head253 = (Int16)((data[8] << 8) + data[7]);
                            if (head253 != 9999)
                            {
                                _ahrs.imuHeading = head253 * 0.1;
                            }

                            //Roll
                            double rollK = (Int16)((data[10] << 8) + data[9]);
                            if (rollK != 8888)
                            {
                                if (_ahrs.isRollInvert) rollK *= -0.1;
                                else rollK *= 0.1;
                                rollK -= _ahrs.rollZero;
                                _ahrs.imuRoll = _ahrs.imuRoll * _ahrs.rollFilter + rollK * (1 - _ahrs.rollFilter);
                            }
                            //else ahrs.imuRoll = 88888;

                            //switch status
                            _mc.workSwitchHigh = (data[11] & 1) == 1;
                            _mc.steerSwitchHigh = (data[11] & 2) == 2;

                            //the pink steer dot reset
                            steerModuleConnectedCounter = 0;

                            //Actual PWM
                            _mc.pwmDisplay = data[12];

                            break;
                        }

                    case 0xF0: // ISOBUS heartbeat
                        {
                            int length = data[4];
                            // [XPLAT] CP9 F2: declared-payload guard — the copy reads bytes [5 .. 5+length-1], so
                            // require the buffer to actually contain them before Array.Copy. The exact envelope
                            // already implies this for conformant frames; the explicit local bound hardens against
                            // any future envelope change and documents the read range.
                            if (data.Length < 5 + length)
                                break;
                            byte[] pgnData = new byte[length];
                            Array.Copy(data, 5, pgnData, 0, length);
                            _isobus.DeserializeHeartbeat(pgnData);
                            break;
                        }

                    case 250:
                        {
                            if (data.Length != 14)
                                break;
                            _mc.sensorData = data[5];
                            break;
                        }

                    case 221: // DD
                        {
                            //{ 0x80, 0x81, 0x7f, 221, number bytes, seconds to display, mystery byte, 98,99,100,101, CRC };
                            if (data.Length < 9) break;

                            // [XPLAT] CP9 F2: bound the UTF-8 slice explicitly. The message length is data[4] - 2;
                            // reject a frame whose declared message would be negative or run past the buffer BEFORE
                            // calling GetString (which would otherwise throw ArgumentOutOfRangeException). The exact
                            // envelope + the >= 9 guard already imply a valid slice for conformant frames, so the
                            // decoded bytes — and thus well-formed behavior — are unchanged.
                            int hwMsgLen = data[4] - 2;
                            if (hwMsgLen < 0 || 7 + hwMsgLen > data.Length) break;

                            // [XPLAT] was the WinForms lblHardwareMessage label (text/visibility/color set inline).
                            // Decode the UTF-8 message exactly as the net48 source and raise it; the Avalonia view
                            // subscriber owns display/visibility/coloring/duration (formerly data[5]/data[6]).
                            string hardwareMessage = System.Text.Encoding.UTF8.GetString(data, 7, hwMsgLen);
                            Log.EventWriter(hardwareMessage);
                            OnHardwareMessage?.Invoke(hardwareMessage);
                            break;
                        }
                    case 222: // 0xDE
                        {
                            //{ 0x80, 0x81, 0x7f, 222, number bytes, mask, command CRC };
                            // [XPLAT] CP9 F2: off-by-one fix — this case reads BOTH data[5] (mask) and data[6]
                            // (command), so the frame must be at least 7 bytes. The net48 source guarded
                            // `data.Length < 6`, which still permitted a 6-byte frame to read data[6] out of
                            // bounds. A conformant 0xDE frame carries mask + command (data[4] >= 2 -> length >= 8),
                            // so well-formed behavior is unchanged.
                            if (data.Length < 7) break;
                            if (((data[5] & 1) == 1)) //mask bit #0 set and command bit #0 nudge line to the 0 = left 1 = right
                            {
                                double dist = Properties.ToolSettings.Default.setAS_snapDistance * 0.01;
                                if ((data[6] & 1) != 1) { _trk.NudgeTrack(-dist); }
                                if ((data[6] & 1) == 1) { _trk.NudgeTrack(dist); }
                            }
                            if (((data[5] & 2) == 2)) //mask bit #1 set and command bit #0 cycle line to the 0 = left 1 = right
                            {
                                // [XPLAT] was btnCycleLines.PerformClick() / btnCycleLinesBk.PerformClick().
                                if ((data[6] & 1) != 1) { OnCycleLines?.Invoke(); }
                                if ((data[6] & 1) == 1) { OnCycleLinesBk?.Invoke(); }
                            }

                            break;
                        }


                    #region Remote Switches
                    case 234://MTZ8302 Feb 2020
                        {
                            //Steer angle actual
                            if (data.Length != 14)
                                break;

                            Buffer.BlockCopy(data, 5, _mc.ss, 1, 8);

                            // [XPLAT] was the direct DoRemoteSwitches() call; now drives SectionService.
                            OnRemoteSwitchChanged?.Invoke();

                            break;
                        }
                        #endregion
                }
            }
        }
    }
}
