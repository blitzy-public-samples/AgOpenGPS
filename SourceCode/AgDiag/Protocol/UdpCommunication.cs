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
        // them. The deleted WinForms code showed a modal error dialog; the cross-platform equivalent is an
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

                        // [XPLAT] QA F4-C5: process each datagram inside its own try/catch so a single
                        // malformed frame can never tear down the receive loop. The migrated async/while loop
                        // sat inside ONE outer try/catch, so any exception from HandleMessage (e.g. an
                        // IndexOutOfRange on a truncated frame) propagated out, disposed the UdpClient via the
                        // using-block, and silently stopped the passive monitor until restart. A per-iteration
                        // catch restores the per-callback resilience the production AgIO.UdpLoopbackService has
                        // (it re-arms its receive before processing), letting AgDiag log the bad frame and keep
                        // receiving. The OUTER catch is retained for genuine socket-level failures (e.g. the
                        // 17777 bind failing). — see TRANSITION_MAP.md
                        try
                        {
                            HandleMessage(result.Buffer);
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"UDP malformed-frame ignored: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // [XPLAT] WinForms error dialog replaced with a cross-platform, operator-visible diagnostic
                // surface: raise ErrorOccurred so FormLoop can show the failure in its UDP error label,
                // and use Trace.WriteLine for trace logs. This prevents loopback/UDP receive failures
                // (e.g. port 17777 already bound) from being silently hidden from the operator.
                Trace.WriteLine($"UDP Error: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"UDP Error: {ex.Message}");
            }
        }

        private void HandleMessage(byte[] data)
        {
            // [XPLAT] QA F4-C5: guard against malformed/truncated datagrams before indexing. The dispatch
            // reads data[0], data[1] and data[3], and every PGN handler copies a payload starting at data[5],
            // so a valid AgOpenGPS PGN is at least 5 bytes (header 0x80 0x81, source, pgn id, length). A
            // shorter frame — including an empty 0-byte datagram — is not a decodable PGN, so it is ignored
            // rather than allowed to throw IndexOutOfRange. This pairs with the per-iteration catch in
            // ReceiveLoopAsync so the passive monitor degrades gracefully. — see TRANSITION_MAP.md
            if (data == null || data.Length < 5)
            {
                return;
            }

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
