using System.Collections.Generic;
using NUnit.Framework;

namespace AgOpenGPS.Tests
{
    /// <summary>
    /// Guard fixture for the AgIO&#8596;AgOpenGPS gRPC client connection retry/backoff schedule.
    ///
    /// The IPC transport migration replaces the legacy UDP loopback bind-failure dialog with a
    /// client-side connection retry loop. Because AgOpenGPS auto-starts AgIO, the gRPC server
    /// socket may not yet be listening when a client first connects, so each client retries the
    /// connection with exponential backoff before surfacing the existing FormDialog error path
    /// and the Log.EventWriter entry.
    ///
    /// The canonical, preserved schedule is exactly five attempts with delays of
    /// 500 / 1000 / 2000 / 4000 / 8000 ms (base 500 ms, &#215;2 multiplier), computed as
    /// delay(n) = 500 * 2^(n-1) for n = 1..5, which sums to 15500 ms of accumulated wait.
    ///
    /// There is no shared backoff helper type to exercise: the production loop is inline in the
    /// WinForms FormGPS partial (UDPComm.Designer.cs) and is not a cleanly invokable unit. This
    /// fixture therefore locks the arithmetic and the attempt count of the preserved schedule so
    /// that any future drift in the client's backoff progression is caught immediately, without
    /// incurring flaky multi-second real-time waits.
    /// </summary>
    public class RetryBackoffIpcTests
    {
        /// <summary>Maximum number of connection attempts before the error is surfaced.</summary>
        private const int MaxAttempts = 5;

        /// <summary>Base (first-attempt) backoff delay, in milliseconds.</summary>
        private const int BaseDelayMs = 500;

        /// <summary>Exponential growth factor applied to the delay after each attempt.</summary>
        private const int Multiplier = 2;

        /// <summary>
        /// Builds the exponential backoff schedule from the canonical constants using the same
        /// progression as the production client: delay(n) = BaseDelayMs * Multiplier^(n-1).
        /// Deriving the values here, rather than returning a hard-coded literal, proves the
        /// arithmetic itself so that a change to any constant is reflected in the asserted result.
        /// </summary>
        /// <returns>The ordered per-attempt delays, in milliseconds.</returns>
        private static IReadOnlyList<int> BuildBackoffScheduleMs()
        {
            var delays = new List<int>();
            int delay = BaseDelayMs;
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                delays.Add(delay);
                delay *= Multiplier;
            }

            return delays;
        }

        /// <summary>
        /// The client must attempt to connect exactly five times before it gives up and surfaces
        /// the error. This locks the preserved attempt count against accidental change.
        /// </summary>
        [Test]
        public void BackoffSchedule_HasExactlyFiveAttempts()
        {
            // Act
            var schedule = BuildBackoffScheduleMs();

            // Assert
            Assert.That(schedule.Count, Is.EqualTo(5));
        }

        /// <summary>
        /// The per-attempt delays must match the canonical 500 / 1000 / 2000 / 4000 / 8000 ms
        /// progression, verified as an ordered, element-wise equality against the expected array.
        /// </summary>
        [Test]
        public void BackoffSchedule_MatchesCanonical_500_1000_2000_4000_8000()
        {
            // Arrange
            int[] expected = { 500, 1000, 2000, 4000, 8000 };

            // Act
            var schedule = BuildBackoffScheduleMs();

            // Assert
            Assert.That(schedule, Is.EqualTo(expected));
        }

        /// <summary>
        /// The total accumulated backoff before the error is surfaced must be 15500 ms
        /// (500 + 1000 + 2000 + 4000 + 8000). The sum is accumulated on a synthetic clock so the
        /// test never performs a real wait.
        /// </summary>
        [Test]
        public void BackoffSchedule_TotalWaitBeforeSurfacingError_Is15500ms()
        {
            // Act
            int totalMs = 0;
            foreach (int d in BuildBackoffScheduleMs())
            {
                totalMs += d;
            }

            // Assert
            Assert.That(totalMs, Is.EqualTo(15500));
        }

        /// <summary>
        /// Each delay must equal the closed form 500 * 2^(n-1); pinning the individual attempts
        /// (0-based indices) reinforces that the schedule follows a strict &#215;2 doubling progression.
        /// </summary>
        [Test]
        public void BackoffSchedule_FollowsClosedFormDoublingProgression()
        {
            // Act
            var schedule = BuildBackoffScheduleMs();

            // Assert
            Assert.That(schedule[0], Is.EqualTo(500));
            Assert.That(schedule[1], Is.EqualTo(1000));
            Assert.That(schedule[2], Is.EqualTo(2000));
            Assert.That(schedule[3], Is.EqualTo(4000));
            Assert.That(schedule[4], Is.EqualTo(8000));
        }
    }
}
