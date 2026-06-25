// [XPLAT] migrated from net48/WinForms Forms/Controls.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// NMEA-sentence generation half of the ModSim simulator. Ported verbatim from the WinForms
// FormSim "GPS Simulator" region: the position dead-reckoning maths, the NMEA XOR checksum, and the
// per-sentence builders. Every numeric conversion that ends up on the wire keeps its explicit
// CultureInfo.InvariantCulture argument so the emitted sentences are byte-identical on any host
// locale (a Windows/Linux/macOS comma-decimal locale would otherwise corrupt the protocol text).
using System;
using System.Globalization;
using System.Text;

namespace ModSim.Views
{
    public partial class MainSimView
    {
        // The eight individual NMEA sentence buffers plus the aggregate send buffer.
        private readonly StringBuilder sbOGI = new StringBuilder();
        private readonly StringBuilder sbNDA = new StringBuilder();

        private readonly StringBuilder sbHDT = new StringBuilder();
        private readonly StringBuilder sbRMC = new StringBuilder();

        private readonly StringBuilder sbGGA = new StringBuilder();
        private readonly StringBuilder sbVTG = new StringBuilder();
        private readonly StringBuilder sbAVR = new StringBuilder();
        private readonly StringBuilder sbKSXT = new StringBuilder();

        // The entire string to send out.
        private readonly StringBuilder sbSendText = new StringBuilder();

        // The checksum of an NMEA line.
        private string sumStr = "";

        /// <summary>
        /// Dead-reckons a new latitude/longitude from a start point, bearing and distance, then
        /// derives the NMEA degree-minute fields and N/S, E/W hemisphere characters. Ported verbatim.
        /// </summary>
        public void CalculateNewPostionFromBearingDistance(double lat, double lng, double bearing, double distance)
        {
            double R = distance / 6371.0; // Earth Radius in Km

            double lat2 = Math.Asin((Math.Sin(lat) * Math.Cos(R)) + (Math.Cos(lat) * Math.Sin(R) * Math.Cos(bearing)));
            double lon2 = lng + Math.Atan2(Math.Sin(bearing) * Math.Sin(R) * Math.Cos(lat), Math.Cos(R) - (Math.Sin(lat) * Math.Sin(lat2)));

            latitude = ToDegrees * lat2;
            longitude = ToDegrees * lon2;

            // convert to DMS from Degrees
            latMinu = latitude;
            longMinu = longitude;

            latDeg = (int)latitude;
            longDeg = (int)longitude;

            latMinu -= latDeg;
            longMinu -= longDeg;

            latMinu = Math.Round(latMinu * 60.0, 7);
            longMinu = Math.Round(longMinu * 60.0, 7);

            latDeg *= 100.0;
            longDeg *= 100.0;

            latNMEA = latMinu + latDeg;
            longNMEA = longMinu + longDeg;

            if (latitude >= 0) NS = 'N';
            else NS = 'S';
            if (longitude >= 0) EW = 'E';
            else EW = 'W';
        }

        /// <summary>
        /// Calculates the NMEA checksum (XOR of every character between '$' and '*') and stores it
        /// as a two-digit upper-case hex string in <see cref="sumStr"/>. Ported verbatim.
        /// </summary>
        public void CalculateChecksum(string Sentence)
        {
            int sum = 0, inx;
            char[] sentence_chars = Sentence.ToCharArray();
            char tmp;
            // All character xor:ed results in the trailing hex checksum
            // The checksum calc starts after '$' and ends before '*'
            for (inx = 1; ; inx++)
            {
                tmp = sentence_chars[inx];
                // Indicates end of data and start of checksum
                if (tmp == '*')
                    break;
                sum ^= tmp;    // Build checksum
            }
            // Calculated checksum converted to a 2 digit hex string
            sumStr = string.Format(CultureInfo.InvariantCulture, "{0:X2}", sum);
        }

        private void BuildGGA()
        {
            sbGGA.Clear();
            sbGGA.Append("$GPGGA,");
            sbGGA.Append(TimeNow);
            sbGGA.Append(Math.Abs(latNMEA).ToString("0000.0000000", CultureInfo.InvariantCulture)).Append(',').Append(NS).Append(',');
            sbGGA.Append(Math.Abs(longNMEA).ToString("00000.0000000", CultureInfo.InvariantCulture)).Append(',').Append(EW).Append(',');
            sbGGA.Append(fixQuality.ToString(CultureInfo.InvariantCulture)).Append(',').Append(sats.ToString(CultureInfo.InvariantCulture)).Append(',').Append(HDOP.ToString(CultureInfo.InvariantCulture)).Append(',').Append("1000");
            sbGGA.Append(",M,46.9,M,37.1,,*");

            CalculateChecksum(sbGGA.ToString());
            sbGGA.Append(sumStr);
            sbGGA.Append("\r\n");
        }

        private void BuildVTG()
        {
            sbVTG.Clear();
            sbVTG.Append("$GPVTG,");
            sbVTG.Append(degrees.ToString("N5", CultureInfo.InvariantCulture));
            sbVTG.Append(",T,034.4,M,");
            sbVTG.Append(speed.ToString(CultureInfo.InvariantCulture));
            sbVTG.Append(",N,");
            sbVTG.Append((speed * 1.852).ToString(CultureInfo.InvariantCulture));
            sbVTG.Append(",K*");

            CalculateChecksum(sbVTG.ToString());
            sbVTG.Append(sumStr);
            sbVTG.Append("\r\n");
        }

        private void BuildHDT()
        {
            sbHDT.Clear();
            sbHDT.Append("$GNHDT,");
            sbHDT.Append(degrees.ToString("N5", CultureInfo.InvariantCulture));
            sbHDT.Append(",T*");

            CalculateChecksum(sbHDT.ToString());
            sbHDT.Append(sumStr);
            sbHDT.Append("\r\n");
        }

        private void BuildAVR()
        {
            sbAVR.Clear();
            sbAVR.Append("$PTNL,AVR,");
            sbAVR.Append(TimeNow);
            sbAVR.Append(degrees.ToString("N5", CultureInfo.InvariantCulture)); //field 2

            sbAVR.Append(",Yaw,-2.1,Tilt,"); //field 3,4,5

            sbAVR.Append(roll.ToString(CultureInfo.InvariantCulture) + ",Roll,"); //field 6,7

            sbAVR.Append("444.232,3,1.2,17*"); //field 8 thru 12

            CalculateChecksum(sbAVR.ToString());
            sbAVR.Append(sumStr);
            sbAVR.Append("\r\n");
        }

        private void BuildOGI()
        {
            sbOGI.Clear();
            sbOGI.Append("$PAOGI,");

            sbOGI.Append(TimeNow);
            sbOGI.Append(Math.Abs(latNMEA).ToString("0000.0000000", CultureInfo.InvariantCulture)).Append(',').Append(NS).Append(',');
            sbOGI.Append(Math.Abs(longNMEA).ToString("0000.0000000", CultureInfo.InvariantCulture)).Append(',').Append(EW).Append(',');

            sbOGI.Append(fixQuality.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sats.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(HDOP.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append("1000,3.2,")                                                                    //10
                .Append(speed.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(degrees.ToString("N5", CultureInfo.InvariantCulture)).Append(',')
                .Append(roll.ToString(CultureInfo.InvariantCulture)).Append(",0.12,359.9,T*");

            CalculateChecksum(sbOGI.ToString());
            sbOGI.Append(sumStr);
            sbOGI.Append("\r\n");
        }

        private void BuildNDA()
        {
            sbNDA.Clear();
            sbNDA.Append("$PANDA,");

            sbNDA.Append(TimeNow);
            sbNDA.Append(Math.Abs(latNMEA).ToString("0000.0000000", CultureInfo.InvariantCulture)).Append(',').Append(NS).Append(',');
            sbNDA.Append(Math.Abs(longNMEA).ToString("0000.0000000", CultureInfo.InvariantCulture)).Append(',').Append(EW).Append(',');

            sbNDA.Append(fixQuality.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sats.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(HDOP.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append("1000,3.2,")                                                                    //10
                .Append(speed.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(headingIMU.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(rollIMU.ToString(CultureInfo.InvariantCulture)).Append(",32,298").Append("*");

            CalculateChecksum(sbNDA.ToString());
            sbNDA.Append(sumStr);
            sbNDA.Append("\r\n");
        }

        private void BuildKSXT()
        {
            sbKSXT.Clear();
            sbKSXT.Append("$KSXT,"); //1

            sbKSXT.Append(TimeNow);
            sbKSXT.Append(longitude.ToString("0000.0000000", CultureInfo.InvariantCulture)).Append(',');
            sbKSXT.Append(latitude.ToString("0000.0000000", CultureInfo.InvariantCulture)).Append(',');

            sbKSXT.Append(altitude.ToString(CultureInfo.InvariantCulture)).Append(',') //altitude
                .Append(degrees.ToString("N5", CultureInfo.InvariantCulture)) //true heading
                .Append(",22,35,") // Pitch, SpeedAngle
                .Append(speed.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(roll.ToString(CultureInfo.InvariantCulture)).Append(",3,3,13,-1075,-98,-8,,,,37,13,,")
                .Append("*3FCF0C9B");

            sbKSXT.Append("\r\n");
        }

        private void BuildRMC()
        {
            sbRMC.Clear();
            sbRMC.Append("$GPRMC,")
            .Append(TimeNow).Append("A,")
            .Append(Math.Abs(latNMEA).ToString("0000.0000000", CultureInfo.InvariantCulture)).Append(',').Append(NS).Append(',')
            .Append(Math.Abs(longNMEA).ToString("0000.0000000", CultureInfo.InvariantCulture)).Append(',').Append(EW).Append(',')
            .Append(speed.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(degrees.ToString("N5", CultureInfo.InvariantCulture))
            .Append(",230394,359.9*");

            CalculateChecksum(sbRMC.ToString());
            sbRMC.Append(sumStr);
            sbRMC.Append("\r\n");
        }
    }
}
