using System;
// IPC-REFACTOR: removed unused using System.Diagnostics; (byte-parse path gone).
using AgOpenGPS.Ipc; // IPC-REFACTOR: consume generated CorrectedPositionMsg from the AgOpenGPS.Ipc contract.

namespace GPS_Out.PGNs
{
    public class PGN100
    {
        // data from AOG
        // corrected position
        // 0        header Hi       128 0x80
        // 1        header Lo       129 0x81
        // 2        source          127 0x7F
        // 3        AGIO PGN        100 0x64
        // 4        length          16
        // 5-12     longitude       double
        // 13-20    latitude        double
        // 21       CRC

        // corrected position alternate
        // 0        header Hi       128 0x80
        // 1        header Lo       129 0x81
        // 2        source          127 0x7F
        // 3        AGIO PGN        100 0x64
        // 4        length          24
        // 5-12     longitude       double
        // 13-20    latitude        double
        // 21-28    Fix2Fix         double
        // 29       CRC

        private const byte HeaderCount = 5;
        private double cFix2Fix;
        private double cLatitude;
        private double cLongitude;
        private frmStart mf;
        private DateTime ReceiveTime;
        private bool ExtendedPGN = false;

        public PGN100(frmStart CalledFrom)
        {
            mf = CalledFrom;
            cFix2Fix = 1000;    // invalid data flag
        }

        public double Fix2FixHeading
        {
            get
            {
                if (Connected() && Properties.Settings.Default.UseRollCorrected && ExtendedPGN)
                {
                    return cFix2Fix;
                }
                else
                {
                    return 1000;
                }
            }
        }

        public double Latitude
        {
            get
            {
                if (Connected())
                {
                    return cLatitude;
                }
                else
                {
                    return 0;
                }
            }
        }

        public double Longitude
        {
            get
            {
                if (Connected())
                {
                    return cLongitude;
                }
                else
                {
                    return 0;
                }
            }
        }

        public bool Connected()
        {
            return (DateTime.Now - ReceiveTime).TotalSeconds < 4;
        }

        // IPC-REFACTOR: byte parse replaced by typed CorrectedPositionMsg ingest; ReceiveTime stamped on message arrival.
        public void ParseMessage(CorrectedPositionMsg msg)
        {
            try
            {
                // IPC-REFACTOR: CRC check removed — HTTP/2 frame integrity provides equivalent byte-level guarantees.
                cLongitude = msg.Longitude;
                cLatitude = msg.Latitude;
                cFix2Fix = msg.Fix2FixHeading;
                ExtendedPGN = (msg.Fix2FixHeading != 1000);
                ReceiveTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                mf.Tls.WriteErrorLog("PGN100/ParseMessage: " + ex.ToString());
            }
        }
    }
}