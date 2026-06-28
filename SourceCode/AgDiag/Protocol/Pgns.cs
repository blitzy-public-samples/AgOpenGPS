// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;

namespace AgDiag.Protocol
{
    public abstract class PgnBase
    {
        protected byte[] Bytes { get; set; }

        protected byte GetByte(int byteIndex)
        {
            return Bytes[byteIndex];
        }

        protected int GetInt(int lowByteIndex, int highByteIndex)
        {
            return (Bytes[highByteIndex] << 8) | Bytes[lowByteIndex];
        }

        protected bool IsBitOn(int byteIndex, int bit)
        {
            return (Bytes[byteIndex] & (1 << bit)) != 0;
        }

        public void SetBytesFromMessage(byte[] data)
        {
            // [XPLAT] QA F4-C5: clamp the payload copy length so a malformed datagram cannot throw and tear
            // down the AgDiag receive loop. The original net48 idiom copied `data.Length - 5` bytes blindly,
            // which (a) underflows to a negative count on a short (<5-byte) frame and (b) overruns the fixed
            // `Bytes` buffer on an oversized frame — both raise ArgumentException. Clamping to the smaller of
            // the incoming frame and the destination buffer (minus the 5-byte header) and guarding `> 0`
            // preserves the exact happy-path copy (a valid frame has data.Length == Bytes.Length, so the
            // count is unchanged) while turning malformed input into a safe no-op. — see TRANSITION_MAP.md
            int count = Math.Min(data.Length, Bytes.Length) - 5;
            if (count > 0)
            {
                Buffer.BlockCopy(data, 5, Bytes, 5, count);
            }
        }

        public string ToHexString()
        {
            return BitConverter.ToString(Bytes);
        }
    }

    public class Pgns
    {
        //AutoSteerData
        public class CPGN_FE : PgnBase
        {
            private const int speedLo = 5;
            private const int speedHi = 6;
            private const int status = 7;
            private const int steerAngleLo = 8;
            private const int steerAngleHi = 9;
            private const int sc1to8 = 11;

            /// <summary>
            /// autoSteerData FE 254 speedHi=5 speedLo=6  status = 7 free = 8;
            /// steerAngleHi = 9 steerAngleLo = 10 tramControl = 11 sc1to8 = 12 sc9to16 = 13;
            /// </summary>
            public CPGN_FE()
            {
                Bytes = new byte[] { 0x80, 0x81, 0x7f, 0xFE, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
            }

            public int Speed => GetInt(speedLo, speedHi);
            public byte Status => GetByte(status);
            public int SteerAngle => GetInt(steerAngleLo, steerAngleHi);

            public bool IsSectionOn(int section)
            {
                if (section < 1 || section > 8) throw new ArgumentOutOfRangeException(nameof(section));

                return IsBitOn(sc1to8, section - 1);
            }
        }

        //From steer module
        public class CPGN_FD : PgnBase
        {
            private const int actualLo = 5;
            private const int actualHi = 6;
            private const int headLo = 7;
            private const int headHi = 8;
            private const int rollLo = 9;
            private const int rollHi = 10;
            private const int switchStatus = 11;
            private const int pwm = 12;

            public CPGN_FD()
            {
                Bytes = new byte[] { 0x80, 0x81, 0x7f, 0xFD, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
            }

            public int ActualSteerAngle => GetInt(actualLo, actualHi);
            public int Heading => GetInt(headLo, headHi);
            public int Roll => GetInt(rollLo, rollHi);
            public byte PWM => GetByte(pwm);
            public bool IsWorkSwitchOn => IsBitOn(switchStatus, 0);
            public bool IsSteerSwitchOn => IsBitOn(switchStatus, 1);
        }

        //AutoSteer Settings
        public class CPGN_FC : PgnBase
        {
            private const int gainProportional = 5;
            private const int highPWM = 6;
            private const int lowPWM = 7;
            private const int minPWM = 8;
            private const int countsPerDegree = 9;
            private const int wasOffsetLo = 10;
            private const int wasOffsetHi = 11;
            private const int ackerman = 12;

            /// <summary>
            /// PGN - 252 - FC gainProportional=5 HighPWM=6  LowPWM = 7 MinPWM = 8 
            /// CountsPerDegree = 9 wasOffsetHi = 10 wasOffsetLo = 11 
            /// </summary>
            public CPGN_FC()
            {
                Bytes = new byte[] { 0x80, 0x81, 0x7f, 0xFC, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
            }

            public byte GainProportional => GetByte(gainProportional);
            public byte HighPWM => GetByte(highPWM);
            public byte LowPWM => GetByte(lowPWM);
            public byte MinPWM => GetByte(minPWM);
            public byte CountsPerDegree => GetByte(countsPerDegree);
            public int SteerOffset => GetInt(wasOffsetLo, wasOffsetHi);
            public byte Ackerman => GetByte(ackerman);
        }

        //Autosteer Board Config
        public class CPGN_FB : PgnBase
        {
            private const int set0 = 5;
            private const int maxPulse = 6;
            private const int minSpeed = 7;

            /// <summary>
            /// PGN - 251 - FB 
            /// set0=5 maxPulse = 6 minSpeed = 7 ackermanFix = 8
            /// </summary>
            public CPGN_FB()
            {
                Bytes = new byte[] { 0x80, 0x81, 0x7f, 0xFB, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
            }

            public byte Set0 => GetByte(set0);
            public byte MaxPulse => GetByte(maxPulse);
            public byte MinSpeed => GetByte(minSpeed);
        }

        //Machine Data
        public class CPGN_EF : PgnBase
        {
            private const int speed = 6;

            /// <summary>
            /// PGN - 239 - EF 
            /// uturn=5  tree=6  hydLift = 8 
            /// </summary>
            public CPGN_EF()
            {
                Bytes = new byte[] { 0x80, 0x81, 0x7f, 0xEF, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
            }

            public byte Speed => GetByte(speed);
        }

        //Machine Config
        public class CPGN_EE : PgnBase
        {
            public int raiseTime = 5;
            public int lowerTime = 6;
            public int enableHyd = 7;
            public int set0 = 8;

            /// <summary>
            /// PGN - 238 - EE 
            /// raiseTime=5  lowerTime=6   enableHyd= 7 set0 = 8
            /// </summary>
            public CPGN_EE()
            {
                Bytes = new byte[] { 0x80, 0x81, 0x7f, 0xEE, 3, 0, 0, 0, 0xCC };
            }
        }

        //pgn instances

        /// <summary>
        /// autoSteerData - FE - 254 - 
        /// </summary>
        public CPGN_FE asData = new CPGN_FE();

        /// <summary>
        /// autoSteerReturn - FD - 253 - 
        /// </summary>
        public CPGN_FD asModule = new CPGN_FD();

        /// <summary>
        /// autoSteerSettings PGN - 252 - FC
        /// </summary>
        public CPGN_FC asSet = new CPGN_FC();

        /// <summary>
        /// autoSteerConfig PGN - 251 - FB
        /// </summary>
        public CPGN_FB asConfig = new CPGN_FB();

        /// <summary>
        /// machineData PGN - 239 - EF
        /// </summary>
        public CPGN_EF maData = new CPGN_EF();

        /// <summary>
        /// machineConfig PGN - 238 - EE
        /// </summary>
        public CPGN_EE maConfig = new CPGN_EE();
    }
}
