using System;
using System.Text;
// IPC-REFACTOR: typed outbound message builders for CommandService (replaces raw UDP byte frames).
using AgOpenGPS.Ipc;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        //Latitude
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

        //AutoSteerData
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


        //AutoSteer Settings
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

        //Autosteer Board Config
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

        //Machine Data
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

        //Machine Config
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

        //Relay Config
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

                pgn[pin0] = (byte)int.Parse(words[0]);
                pgn[pin1] = (byte)int.Parse(words[1]);
                pgn[pin2] = (byte)int.Parse(words[2]);
                pgn[pin3] = (byte)int.Parse(words[3]);
                pgn[pin4] = (byte)int.Parse(words[4]);
                pgn[pin5] = (byte)int.Parse(words[5]);
                pgn[pin6] = (byte)int.Parse(words[6]);
                pgn[pin7] = (byte)int.Parse(words[7]);
                pgn[pin8] = (byte)int.Parse(words[8]);
                pgn[pin9] = (byte)int.Parse(words[9]);

                pgn[pin10] = (byte)int.Parse(words[10]);
                pgn[pin11] = (byte)int.Parse(words[11]);
                pgn[pin12] = (byte)int.Parse(words[12]);
                pgn[pin13] = (byte)int.Parse(words[13]);
                pgn[pin14] = (byte)int.Parse(words[14]);
                pgn[pin15] = (byte)int.Parse(words[15]);
                pgn[pin16] = (byte)int.Parse(words[16]);
                pgn[pin17] = (byte)int.Parse(words[17]);
                pgn[pin18] = (byte)int.Parse(words[18]);
                pgn[pin19] = (byte)int.Parse(words[19]);

                pgn[pin20] = (byte)int.Parse(words[20]);
                pgn[pin21] = (byte)int.Parse(words[21]);
                pgn[pin22] = (byte)int.Parse(words[22]);
                pgn[pin23] = (byte)int.Parse(words[23]);

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

        //pgn instances

        /// <summary>
        /// autoSteerData - FE - 254 - 
        /// </summary>
        public CPGN_FE p_254 = new CPGN_FE();

        /// <summary>
        /// autoSteerSettings PGN - 252 - FC
        /// </summary>
        public CPGN_FC p_252 = new CPGN_FC();

        /// <summary>
        /// autoSteerConfig PGN - 251 - FB
        /// </summary>
        public CPGN_FB p_251 = new CPGN_FB();

        /// <summary>
        /// machineData PGN - 239 - EF
        /// </summary>
        public CPGN_EF p_239 = new CPGN_EF();

        /// <summary>
        /// machineConfig PGN - 238 - EE
        /// </summary>
        public CPGN_EE p_238 = new CPGN_EE();

        /// <summary>
        /// relayConfig PGN - 236 - EC
        /// </summary>
        public CPGN_EC p_236 = new CPGN_EC();

        /// <summary>
        /// Section dimensions PGN - 235 - EB
        /// </summary>
        public CPGN_EB p_235 = new CPGN_EB();

        /// <summary>
        /// Section dimensions PGN - 228 - E4
        /// </summary>
        public CPGN_E4 p_228 = new CPGN_E4();

        /// <summary>
        /// Section Symmetric PGN - 229 - EB
        /// </summary>
        public CPGN_E5 p_229 = new CPGN_E5();

        /// <summary>
        /// LatitudeLongitude - D0 - 
        /// </summary>
        //public CPGN_D0 p_208 = new CPGN_D0();

        // IPC-REFACTOR: byte[] -> typed CommandService message builders; consumed by SendPgnToLoop hub in UDPComm.Designer.cs.
        // Each builder reads the fully-populated legacy frame `d` (the same array formerly handed to the UDP send path)
        // at the documented byte offsets and returns the strongly-typed AgOpenGPS.Ipc message. Byte offsets and scaling
        // are the contract source of truth in docs/pgn-protocol.md; the // PGN 0xNN comments preserve byte-table traceability.
        // Nested + private keeps these helpers off the public surface (existing CPGN_* public API is unchanged).
        private static class OutboundPgn
        {
            // PGN 0xFE (254) - AutoSteer Data, 14 bytes. speedLo@5/speedHi@6, status@7,
            // steerAngleLo@8/steerAngleHi@9 (signed Int16), lineDistance@10 (offset-encoded 0..255, unsigned),
            // section control 1-8 @11, 9-16 @12.
            internal static AutoSteerDataMsg BuildAutoSteerData(byte[] d)
            {
                return new AutoSteerDataMsg
                {
                    Speed = (uint)(d[5] | (d[6] << 8)),
                    Status = d[7],
                    CommandedSteerAngle = (int)(short)(d[8] | (d[9] << 8)),
                    LineDistance = d[10],
                    SectionControl18 = d[11],
                    SectionControl916 = d[12]
                };
            }

            // PGN 0xFC (252) - AutoSteer Settings, 14 bytes. wasOffset is lo@10/hi@11 combined.
            internal static AutoSteerSettingsMsg BuildAutoSteerSettings(byte[] d)
            {
                return new AutoSteerSettingsMsg
                {
                    GainProportionalKp = d[5],
                    HighPwm = d[6],
                    LowPwm = d[7],
                    MinPwm = d[8],
                    CountsPerDegree = d[9],
                    WasOffset = (uint)(d[10] | (d[11] << 8)),
                    Ackerman = d[12]
                };
            }

            // PGN 0xFB (251) - AutoSteer Config, 14 bytes.
            internal static AutoSteerConfigMsg BuildAutoSteerConfig(byte[] d)
            {
                return new AutoSteerConfigMsg
                {
                    Set0 = d[5],
                    MaxPulse = d[6],
                    MinSpeed = d[7],
                    AckermanFix = d[8],
                    AngularVelocity = d[9]
                };
            }

            // PGN 0xEF (239) - Machine Data, 14 bytes. Byte 10 is reserved (not mapped).
            internal static MachineDataMsg BuildMachineData(byte[] d)
            {
                return new MachineDataMsg
                {
                    UTurn = d[5],
                    Speed = d[6],
                    HydLift = d[7],
                    Tram = d[8],
                    GeoStop = d[9],
                    SectionControl18 = d[11],
                    SectionControl916 = d[12]
                };
            }

            // PGN 0xEE (238) - Machine Config, 14 bytes.
            internal static MachineConfigMsg BuildMachineConfig(byte[] d)
            {
                return new MachineConfigMsg
                {
                    RaiseTime = d[5],
                    LowerTime = d[6],
                    EnableHyd = d[7],
                    Set0 = d[8],
                    User1 = d[9],
                    User2 = d[10],
                    User3 = d[11],
                    User4 = d[12]
                };
            }

            // PGN 0xEC (236) - Relay Config, 29 bytes. 24 relay pin values @5..28 (order preserved).
            internal static RelayConfigMsg BuildRelayConfig(byte[] d)
            {
                RelayConfigMsg m = new RelayConfigMsg();
                for (int i = 0; i < 24; i++)
                {
                    m.PinConfig.Add(d[5 + i]);
                }
                return m;
            }

            // PGN 0xEB (235) - Section Dimensions, 38 bytes. 16 lo/hi width pairs @5..36, numSections@37.
            internal static SectionDimensionsMsg BuildSectionDimensions(byte[] d)
            {
                SectionDimensionsMsg m = new SectionDimensionsMsg
                {
                    NumSections = d[37]
                };
                for (int i = 0; i < 16; i++)
                {
                    m.SectionWidths.Add((uint)(d[5 + (i * 2)] | (d[6 + (i * 2)] << 8)));
                }
                return m;
            }

            // PGN 0xE5 (229) - Section Control (Extended), 16 bytes. 8 bitmask bytes @5..12 = up to 64 sections.
            internal static ExtendedSectionControlMsg BuildExtendedSectionControl(byte[] d)
            {
                return new ExtendedSectionControlMsg
                {
                    Sections = (ulong)d[5] | ((ulong)d[6] << 8) | ((ulong)d[7] << 16) | ((ulong)d[8] << 24) | ((ulong)d[9] << 32) | ((ulong)d[10] << 40) | ((ulong)d[11] << 48) | ((ulong)d[12] << 56),
                    ToolLeftSpeed = d[13],
                    ToolRightSpeed = d[14]
                };
            }

            // PGN 0xE4 (228) - Rate Control, 14 bytes.
            internal static RateControlMsg BuildRateControl(byte[] d)
            {
                return new RateControlMsg
                {
                    Rate0 = d[5],
                    Rate1 = d[6],
                    Rate2 = d[7]
                };
            }

            // PGN 0xF1 (241) - Section Control Enable Request (AOG -> TC), 7 bytes.
            internal static SectionControlEnableMsg BuildSectionControlEnable(byte[] d)
            {
                return new SectionControlEnableMsg
                {
                    Enabled = d[5] != 0
                };
            }

            // PGN 0xF2 (242) - Process Data (AOG -> TC), 12 bytes. identifier@5-6, value@7-10 (Int32).
            internal static ProcessDataMsg BuildProcessData(byte[] d)
            {
                return new ProcessDataMsg
                {
                    Identifier = (uint)(d[5] | (d[6] << 8)),
                    Value = d[7] | (d[8] << 8) | (d[9] << 16) | (d[10] << 24)
                };
            }

            // PGN 0xF3 (243) - Field Name (AOG -> TC), variable length. len@4, UTF-8 name @5.., empty = field closed, max 248.
            internal static FieldNameMsg BuildFieldName(byte[] d)
            {
                int available = Math.Max(0, d.Length - 5);
                int len = Math.Min(d[4], available);
                return new FieldNameMsg
                {
                    Name = len > 0 ? Encoding.UTF8.GetString(d, 5, len) : string.Empty
                };
            }

            // PGN 0xD0 (208) - Latitude/Longitude, 14 bytes. Encoded Int32 lat@5..8, lon@9..12.
            internal static LatLonMsg BuildLatLon(byte[] d)
            {
                return new LatLonMsg
                {
                    LatitudeEncoded = d[5] | (d[6] << 8) | (d[7] << 16) | (d[8] << 24),
                    LongitudeEncoded = d[9] | (d[10] << 8) | (d[11] << 16) | (d[12] << 24)
                };
            }

            // PGN 0x64 (100) - Corrected Position (AOG -> AgIO), byte[30]. double longitude@5, latitude@13, fix2fixHeading@21.
            // Built by Position.designer.cs via BitConverter.GetBytes(double); read back symmetrically here.
            // Sentinel Fix2FixHeading == 1000 marks an invalid heading; it is passed through unchanged for the consumer to interpret.
            internal static CorrectedPositionMsg BuildCorrectedPosition(byte[] d)
            {
                return new CorrectedPositionMsg
                {
                    Longitude = BitConverter.ToDouble(d, 5),
                    Latitude = BitConverter.ToDouble(d, 13),
                    Fix2FixHeading = BitConverter.ToDouble(d, 21)
                };
            }
        }
    }
}
