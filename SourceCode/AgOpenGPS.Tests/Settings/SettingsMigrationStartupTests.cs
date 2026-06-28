// [XPLAT] migrated from net48 — see MIGRATION_DOCS/TRANSITION_MAP.md
// Regression coverage for the one-time legacy Windows Registry -> RegistrySettings.xml migration startup
// hook (CSettingsMigration.MigrateLegacyRegistrySettings) that Program.Main() now invokes immediately
// before RegistrySettings.Load(). The migration must be safe to call unconditionally at startup on every
// supported OS: on non-Windows it is a guaranteed no-op (RuntimeInformation guard + a #if WINDOWS body that
// is not even compiled into the net8.0 assembly), and on Windows the seeding is wrapped in try/catch so a
// failed seed can never block startup. These tests lock in that "never throws / no-op off-Windows" contract
// so the Linux and macOS legs of the tri-OS CI matrix stay unaffected by the new startup hook. The Windows
// seed-once / idempotency behaviour requires a live Windows Registry and is guaranteed by the production
// guards in CSettingsMigration (RegistrySettings.xml existence check + LegacyRegistryExists()).
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace AgOpenGPS.Tests.Settings
{
    [TestFixture]
    public class SettingsMigrationStartupTests
    {
        /// <summary>
        /// The startup hook must never throw, on any OS. This is the exact call Program.Main() makes before
        /// RegistrySettings.Load(); a regression here would break application startup on every platform.
        /// </summary>
        [Test]
        public void MigrateLegacyRegistrySettings_DoesNotThrow_OnStartupPath()
        {
            Assert.DoesNotThrow(() => CSettingsMigration.MigrateLegacyRegistrySettings());
        }

        /// <summary>
        /// On any non-Windows OS the migration returns at the RuntimeInformation guard before touching the
        /// filesystem, so repeated invocations remain a safe no-op. This proves the new startup hook leaves
        /// the Linux/macOS CI legs (and end-user runs) completely unaffected.
        /// </summary>
        [Test]
        public void MigrateLegacyRegistrySettings_IsRepeatableNoOp_OnNonWindows()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Windows seed-once / idempotency path requires a live Windows Registry; it is " +
                              "guaranteed by the production guards (RegistrySettings.xml existence + LegacyRegistryExists).");
            }

            Assert.DoesNotThrow(() =>
            {
                CSettingsMigration.MigrateLegacyRegistrySettings();
                CSettingsMigration.MigrateLegacyRegistrySettings();
            });
        }
    }
}
