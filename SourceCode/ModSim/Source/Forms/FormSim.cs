using ModSim.Properties;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

namespace ModSim
{
    public partial class FormSim : Form
    {
        public FormSim()
        {
            InitializeComponent();
        }

        //First run
        private void FormSim_Load(object sender, EventArgs e)
        {
            cboxGGA.Checked = Settings.Default.isGGA;
            cboxVTG.Checked = Settings.Default.isVTG;
            cboxAVR.Checked = Settings.Default.isAVR;
            cboxHDT.Checked = Settings.Default.isHDT;
            cboxRMC.Checked = Settings.Default.isRMC;
            cboxOGI.Checked = Settings.Default.isOGI;
            cboxNDA.Checked = Settings.Default.isNDA;
            cboxKSXT.Checked = Settings.Default.isKSXT;

            latitude = Settings.Default.setGPS_SimLatitude;
            nudLat.Value = (decimal)Settings.Default.setGPS_SimLatitude;
            longitude = Settings.Default.setGPS_SimLongitude;
            nudLon.Value = (decimal)Settings.Default.setGPS_SimLongitude;

            lblIPSet1.Text = Properties.Settings.Default.etIP_SubnetOne.ToString();
            lblIPSet2.Text = Properties.Settings.Default.etIP_SubnetTwo.ToString();
            lblIPSet3.Text = Properties.Settings.Default.etIP_SubnetThree.ToString();

            lblScanReply.Text = "No";

            // IPC-REFACTOR: LoadUDPNetwork now bootstraps the gRPC client (channel + StreamTelemetry subscription) instead of a UDP socket.
            LoadUDPNetwork();
        }

        private void FormSim_FormClosing(object sender, FormClosingEventArgs e)
        {
            //save settings before exit
            Settings.Default.isGGA = cboxGGA.Checked;
            Settings.Default.isVTG = cboxVTG.Checked;
            Settings.Default.isAVR = cboxAVR.Checked;
            Settings.Default.isHDT = cboxHDT.Checked;
            Settings.Default.isRMC = cboxRMC.Checked;
            Settings.Default.isOGI = cboxOGI.Checked;
            Settings.Default.isNDA = cboxNDA.Checked;
            Settings.Default.isKSXT = cboxKSXT.Checked;

            Settings.Default.Save();

            // IPC-REFACTOR: UDP socket teardown (UDPSocket.Shutdown/Close) replaced by gRPC client teardown.
            // Cancel the StreamTelemetry subscription (stops the shared IpcTelemetrySubscriber loop), then dispose
            // the token source and the channel. _cts and _channel are declared in the UDP.designer.cs partial
            // (same FormSim class).
            _cts?.Cancel();
            _cts?.Dispose();
            _channel?.Dispose();
        }

        private void lblIP_Click(object sender, EventArgs e)
        {
            lblIP.Text = "";
            foreach (IPAddress IPA in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (IPA.AddressFamily == AddressFamily.InterNetwork)
                {
                    _ = IPA.ToString();
                    lblIP.Text += IPA.ToString() + "\r\n";
                }
            }
        }
    }
}

