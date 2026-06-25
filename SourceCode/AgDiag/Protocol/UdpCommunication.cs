// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AgDiag.Protocol
{
    public class UdpCommunication
    {
        private readonly Pgns _pgns;

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private int cntr;

        public UdpCommunication(Pgns pgns)
        {
            _pgns = pgns;
        }

        public event EventHandler<int> DefaultSendsUpdated;

        // [XPLAT] M3: surface UDP/loopback receive failures to the UI instead of silently swallowing
        // them. The deleted WinForms code showed a modal MessageBox; the cross-platform equivalent is an
        // event the Avalonia view (FormLoop) subscribes to and renders in a visible diagnostic label.
        // This is an event member on an existing class (mirroring DefaultSendsUpdated), not a new
        // architectural abstraction, so it stays within the AAP's new-surface limit (R7).
        public event EventHandler<string> ErrorOccurred;

        public void LoadLoopback()
        {
            var cancellationToken = _cancellationTokenSource.Token;
            Task.Factory.StartNew(() => ReceiveLoopAsync(cancellationToken), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void CloseLoopback()
        {
            _cancellationTokenSource.Cancel();
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (UdpClient udpClient = new UdpClient(17777))
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var result = await udpClient.ReceiveAsync().ConfigureAwait(false);

                        HandleMessage(result.Buffer);
                    }
                }
            }
            catch (Exception ex)
            {
                // [XPLAT] WinForms MessageBox replaced with a cross-platform, operator-visible diagnostic
                // surface: raise ErrorOccurred so FormLoop can show the failure in its UDP error label,
                // and keep Debug.WriteLine for trace logs. This prevents loopback/UDP receive failures
                // (e.g. port 17777 already bound) from being silently hidden from the operator.
                Debug.WriteLine($"UDP Error: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"UDP Error: {ex.Message}");
            }
        }

        private void HandleMessage(byte[] data)
        {
            if (data[0] == 0x80 && data[1] == 0x81)
            {
                switch (data[3])
                {
                    case 253:
                        {
                            _pgns.asModule.SetBytesFromMessage(data);
                            break;
                        }
                    case 254:
                        {
                            _pgns.asData.SetBytesFromMessage(data);
                            break;
                        }
                    case 252:
                        {
                            _pgns.asSet.SetBytesFromMessage(data);
                            break;
                        }
                    case 251:
                        {
                            _pgns.asConfig.SetBytesFromMessage(data);
                            break;
                        }
                    case 239:
                        {
                            _pgns.maData.SetBytesFromMessage(data);
                            break;
                        }

                    default:
                        {
                            DefaultSendsUpdated?.Invoke(this, data.Length);
                            break;
                        }
                }
            }
            else
            {
                cntr += data.Length;
                DefaultSendsUpdated?.Invoke(this, cntr);
            }
        }
    }
}
