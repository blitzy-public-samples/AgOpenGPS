// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// NMEA-sentence generation + GPS-simulator heartbeat half of the ModSim simulator. Ported
// verbatim from the WinForms FormSim "GPS Simulator" region (Forms/Controls.Designer.cs,
// lines 93-488): the GPS-sim state fields, the 100 ms simulator tick, the position
// dead-reckoning maths, the NMEA XOR checksum, and the eight per-sentence builders.
//
// This is the SAME partial class MainSimView as MainSimView.axaml.cs (the Window base + ctor +
// UI handlers + DispatcherTimer wiring) and MainSimView.Udp.cs (sockets + PGN parsing); the
// three partials freely cross-reference each other's members. The sentences emitted here are
// behaviour-frozen wire data (NMEA over UDP 8888 -> 9999): every numeric that ends up on the
// wire keeps its explicit CultureInfo.InvariantCulture so the bytes stay identical on any host
// locale (a comma-decimal locale would otherwise corrupt the protocol text), while the on-screen
// display labels deliberately remain culture-aware.
using System;
using System.Globalization;
using System.Text;

namespace ModSim.Views
{
    public partial class MainSimView
    {
        #region GPS Simulator

        private string TimeNow = "";

        //Our two new nmea strings
        private readonly StringBuilder sbOGI = new StringBuilder();
        private readonly StringBuilder sbNDA = new StringBuilder();

        private readonly StringBuilder sbHDT = new StringBuilder();
        private readonly StringBuilder sbRMC = new StringBuilder();

        private readonly StringBuilder sbGGA = new StringBuilder();
        private readonly StringBuilder sbVTG = new StringBuilder();
        private readonly StringBuilder sbAVR = new StringBuilder();
        private readonly StringBuilder sbKSXT = new StringBuilder();

        //The entire string to send out
        private readonly StringBuilder sbSendText = new StringBuilder();

        //GPS related properties
        private readonly int fixQuality = 8, sats = 12;

        private readonly double HDOP = 0.9;
        public double altitude = 300;
        private char EW = 'W';
        private char NS = 'N';

        public double latitude, longitude;

        private double latDeg, latMinu, longDeg, longMinu, latNMEA, longNMEA;
        public double speed = 0.6, headingTrue, stepDistance = 0.05, steerAngle;
        private double degrees, roll = 0;

        private int rollIMU = 0, headingIMU = 0;//, pitchIMU = 0;

        private const double ToRadians = 0.01745329251994329576923690768489, ToDegrees = 57.295779513082325225835265587528;

        //The checksum of an NMEA line
        private string sumStr = "";

        // [XPLAT] WinForms simTimer_Tick -> OnSimTimerTick, driven by the Avalonia DispatcherTimer wired
        // in MainSimView.axaml.cs (simTimer.Tick += OnSimTimerTick). TrackBar.Value (int) became
        // Slider.Value (double), so every slider read is cast back to (int) to preserve the original
        // integer-step maths; the single programmatic slider write is bracketed by _suppressSliderEvents
        // (declared in the axaml.cs partial) so it does not re-enter the ValueChanged handlers.
        private void OnSimTimerTick(object sender, EventArgs e)
        {
            stepDistance = (int)tbarSpeed.Value * 0.027777777777 * (0.1);

            if (guidanceStatus == 0)
                steerAngle = (int)tbarSteerAngleWAS.Value * 0.01;
            else
            {
                steerAngle = steerAngleSetPoint;
                _suppressSliderEvents = true;
                tbarSteerAngleWAS.Value = (int)(steerAngleSetPoint);
                _suppressSliderEvents = false;
                steerAngleActual = steerAngle;
                lblWAS.Text = "Steer: " + (steerAngleActual).ToString("N2") + "°";
            }

            double temp = (stepDistance * Math.Tan(steerAngle * 0.02) / 2.5);
            headingTrue += temp;

            if (headingTrue > (2.0 * Math.PI)) headingTrue -= (2.0 * Math.PI);
            if (headingTrue < 0) headingTrue += (2.0 * Math.PI);

            degrees = ToDegrees * headingTrue;

            headingIMU = (int)(degrees * 10);

            lblHeading.Text = (headingTrue * 57.29577951308).ToString("N2") + '°';

            CalculateNewPostionFromBearingDistance(ToRadians * latitude, ToRadians * longitude, headingTrue, stepDistance / 1000.0);

            lblCurrentLon.Text = longitude.ToString("N7");
            lblCurrentLat.Text = latitude.ToString("N7");

            //calc the speed
            speed = Math.Round(1.944 * stepDistance * 1.0 / (0.1), 1);

            TimeNow = DateTime.UtcNow.ToString("HHmmss.fff,", CultureInfo.InvariantCulture);

            if (cboxVTG.IsChecked == true)
            {
                BuildVTG();
                sbSendText.Append(sbVTG.ToString());
                SendUDPMessage(sbVTG.ToString());
            }
            if (cboxAVR.IsChecked == true)
            {
                BuildAVR();
                sbSendText.Append(sbAVR.ToString());
                SendUDPMessage(sbAVR.ToString());
            }
            if (cboxHDT.IsChecked == true)
            {
                BuildHDT();
                sbSendText.Append(sbHDT.ToString());
                SendUDPMessage(sbHDT.ToString());
            }
            if (cboxGGA.IsChecked == true)
            {
                BuildGGA();
                sbSendText.Append(sbGGA.ToString());
                SendUDPMessage(sbGGA.ToString());
            }
            if (cboxRMC.IsChecked == true)
            {
                BuildRMC();
                sbSendText.Append(sbRMC.ToString());
                SendUDPMessage(sbRMC.ToString());
            }
            if (cboxOGI.IsChecked == true)
            {
                BuildOGI();
                sbSendText.Append(sbOGI.ToString());
                SendUDPMessage(sbOGI.ToString());
            }
            if (cboxNDA.IsChecked == true)
            {
                BuildNDA();
                sbSendText.Append(sbNDA.ToString());
                SendUDPMessage(sbNDA.ToString());
            }
            if (cboxKSXT.IsChecked == true)
            {
                BuildKSXT();
                sbSendText.Append(sbKSXT.ToString());
                SendUDPMessage(sbKSXT.ToString());
            }

            sbSendText.Clear();
        }

        /// <summary>
        /// Dead-reckons a new latitude/longitude from a start point, bearing and distance, then
        /// derives the NMEA degree-minute fields and the N/S, E/W hemisphere characters. Ported verbatim.
        /// </summary>
        public void CalculateNewPostionFromBearingDistance(double lat, double lng, double bearing, double distance)
        {
            double R = distance / 6371.0; // Earth Radius in Km

            double lat2 = Math.Asin((Math.Sin(lat) * Math.Cos(R)) + (Math.Cos(lat) * Math.Sin(R) * Math.Cos(bearing)));
            double lon2 = lng + Math.Atan2(Math.Sin(bearing) * Math.Sin(R) * Math.Cos(lat), Math.Cos(R) - (Math.Sin(lat) * Math.Sin(lat2)));

            latitude = ToDegrees * lat2;
            longitude = ToDegrees * lon2;

            //convert to DMS from Degrees
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

        //calculate the NMEA checksum to stuff at the end
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
            // Calculated checksum converted to a 2 digit hex string. [XPLAT] the source emitted this with
            // String.Format("{0:X2}", sum); the InvariantCulture overload is byte-identical for the "X2"
            // hex format and keeps the Release build (TreatWarningsAsErrors) warning-clean.
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

            // [XPLAT] AVR culture fix (AAP §0.6.5): the ONLY wire numeric missing InvariantCulture in source
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
                .Append((headingIMU).ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append((rollIMU).ToString(CultureInfo.InvariantCulture)).Append(",32,298").Append("*");

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

            //sbKSXT.Append(sumStr);
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

        #endregion
    }
}
