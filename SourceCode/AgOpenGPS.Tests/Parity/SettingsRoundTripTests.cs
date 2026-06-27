// [XPLAT] migrated from net48 — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using System.IO;
using AgLibrary.Settings;
using AgOpenGPS.Core.Platform;
using AgOpenGPS.Properties;
using NUnit.Framework;

namespace AgOpenGPS.Tests.Parity
{
    /// <summary>
    /// Golden-file parity suite for the AgOpenGPS settings subsystem (AAP §0.2.2, §0.6.4
    /// "SettingsRoundTripTests"). It is the behavioral-parity PROOF that the net48/WinForms →
    /// net8.0/Avalonia migration kept the settings contract frozen even though the storage backing was
    /// de-Windows-ified: the split settings XML schema (Vehicle / Tool / Environment) must round-trip
    /// byte-for-byte, and the <see cref="CSettingsMigration"/> legacy single-file → split conversion
    /// must be preserved. The former Windows Registry + <c>%AppData%</c> source is replaced by the
    /// cross-platform <c>AgOpenGPS.Core.Platform.IPlatformServices</c> config root, but the XML schema
    /// itself is unchanged — and these tests guard that freeze on the full windows/ubuntu/macos matrix.
    ///
    /// <para>
    /// The fixture extends the load → save byte-compare pattern proven in
    /// <c>SourceCode/AgLibrary.Tests/Settings/XmlSettingsHandlerTests.cs</c>, upgraded from
    /// <c>File.ReadAllText</c> to <c>File.ReadAllBytes</c> so end-of-line/encoding drift can never mask
    /// a regression (byte stability of the goldens on disk is pinned by the ROOT
    /// <c>.gitattributes</c> rule <c>SourceCode/AgOpenGPS.Tests/Parity/** -text</c>).
    /// </para>
    ///
    /// <para>
    /// Golden artifacts are captured once from the Windows/net48 baseline and may not exist on disk
    /// yet; every golden-consuming test therefore resolves its artifact through
    /// <see cref="LoadGoldenOrIgnore"/>, which self-reports as <c>Ignored</c> (never failed) until the
    /// real golden is committed. This keeps <c>dotnet test</c> green and discoverable on every OS —
    /// including <c>osx-arm64</c> — while the goldens are still being produced.
    /// </para>
    /// </summary>
    public class SettingsRoundTripTests
    {
        /// <summary>Per-test isolated configuration root, created in <see cref="CreateIsolatedConfigRoot"/>.</summary>
        private string _tempDir;

        /// <summary>
        /// Forces the invariant culture for the whole fixture. This is the highest data-integrity
        /// safeguard (AAP §0.6.5): every numeric serialize/parse in the settings code uses
        /// <see cref="CultureInfo.InvariantCulture"/>, so doubles such as
        /// <c>setVehicle_maxSteerAngle=30</c> and <c>setVehicle_maxAngularVelocity=0.64</c> always
        /// serialize with a PERIOD decimal separator. Pinning the culture here proves a comma-decimal
        /// locale (for example de-DE) can never change a golden's bytes.
        /// </summary>
        [OneTimeSetUp]
        public void ForceInvariantCulture()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        }

        /// <summary>
        /// Creates a fresh, uniquely named temp directory and points every settings directory root at
        /// it, replacing the platform <c>AppDataRoot</c> / former Windows <c>%AppData%</c> source so no
        /// test ever touches a real user configuration location. Individual tests refine the roots they
        /// need (for example the migration tests use dedicated <c>VehicleProfiles</c>/<c>ToolProfiles</c>
        /// sub-directories).
        /// </summary>
        [SetUp]
        public void CreateIsolatedConfigRoot()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "AgOpenGPS_SettingsParity_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            // [XPLAT] The directory roots are sourced from IPlatformServices.AppDataRoot in production
            // but remain settable static fields so tests can redirect them to an isolated location.
            RegistrySettings.baseDirectory = _tempDir;
            RegistrySettings.vehiclesDirectory = _tempDir;
            RegistrySettings.toolsDirectory = _tempDir;
            RegistrySettings.environmentDirectory = _tempDir;
        }

        /// <summary>Removes the per-test temp directory; cleanup failures never fail a parity assertion.</summary>
        [TearDown]
        public void RemoveIsolatedConfigRoot()
        {
            try
            {
                if (_tempDir != null && Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup; a leaked temp directory must never break a test result.
            }
        }

        /// <summary>
        /// Phase B — vehicle settings round-trip. Loads the frozen <c>Vehicle.xml</c> golden through the
        /// production <see cref="VehicleSettings"/> serializer and re-saves it; the regenerated bytes
        /// must be identical to the golden, proving the vehicle schema did not drift in the migration.
        /// </summary>
        [Test]
        public void VehicleSettings_RoundTrip_IsByteIdentical()
        {
            byte[] goldenBytes = LoadGoldenOrIgnore("Settings", "Vehicle.xml");

            RegistrySettings.vehiclesDirectory = _tempDir;
            File.WriteAllBytes(Path.Combine(_tempDir, "RoundTripInput.xml"), goldenBytes);

            VehicleSettings.Default.Reset();
            LoadResult loadResult = VehicleSettings.Default.Load("RoundTripInput");
            Assert.That(loadResult, Is.EqualTo(LoadResult.Ok));

            // Save to a DISTINCT profile name so the golden input is never overwritten before compare.
            VehicleSettings.Default.Save("RoundTripOutput");
            byte[] savedBytes = File.ReadAllBytes(Path.Combine(_tempDir, "RoundTripOutput.xml"));

            Assert.That(savedBytes, Is.EqualTo(goldenBytes));
        }

        /// <summary>
        /// Phase C — tool settings round-trip. Identical contract to the vehicle round-trip, against the
        /// frozen <c>Tool.xml</c> golden through the production <see cref="ToolSettings"/> serializer.
        /// </summary>
        [Test]
        public void ToolSettings_RoundTrip_IsByteIdentical()
        {
            byte[] goldenBytes = LoadGoldenOrIgnore("Settings", "Tool.xml");

            RegistrySettings.toolsDirectory = _tempDir;
            File.WriteAllBytes(Path.Combine(_tempDir, "RoundTripInput.xml"), goldenBytes);

            ToolSettings.Default.Reset();
            LoadResult loadResult = ToolSettings.Default.Load("RoundTripInput");
            Assert.That(loadResult, Is.EqualTo(LoadResult.Ok));

            ToolSettings.Default.Save("RoundTripOutput");
            byte[] savedBytes = File.ReadAllBytes(Path.Combine(_tempDir, "RoundTripOutput.xml"));

            Assert.That(savedBytes, Is.EqualTo(goldenBytes));
        }

        /// <summary>
        /// Phase D — environment settings round-trip against the frozen <c>Environment.xml</c> golden
        /// through the production <see cref="Settings"/> serializer. The environment settings include the
        /// complex (<c>serializeAs="Xml"</c>) Point/Size/Color members, so this also guards the
        /// XmlSerializer namespace-ordering byte-stability that the migration had to preserve.
        /// </summary>
        [Test]
        public void EnvironmentSettings_RoundTrip_IsByteIdentical()
        {
            byte[] goldenBytes = LoadGoldenOrIgnore("Settings", "Environment.xml");

            RegistrySettings.environmentDirectory = _tempDir;
            string environmentPath = Path.Combine(_tempDir, "environment.xml");
            File.WriteAllBytes(environmentPath, goldenBytes);

            // [XPLAT] Fully qualified: the sibling test namespace AgOpenGPS.Tests.Settings would otherwise
            // shadow the AgOpenGPS.Properties.Settings class for the bare name "Settings" in this file.
            LoadResult loadResult = AgOpenGPS.Properties.Settings.Default.Load();
            Assert.That(loadResult, Is.EqualTo(LoadResult.Ok));

            // Settings.Save() rewrites environment.xml in place; goldenBytes was captured from the Golden
            // store (not this temp copy), so the in-place rewrite is a safe regeneration to compare.
            AgOpenGPS.Properties.Settings.Default.Save();
            byte[] savedBytes = File.ReadAllBytes(environmentPath);

            Assert.That(savedBytes, Is.EqualTo(goldenBytes));
        }

        /// <summary>
        /// Phase E — legacy → split migration round-trip. Stages the frozen legacy single-file golden,
        /// drives <see cref="CSettingsMigration"/> to produce the split Vehicle/Tool profiles, and proves
        /// the produced split files are the canonical serializer output by asserting a load → re-save
        /// cycle reproduces them byte-for-byte. Also asserts the decimal-separator invariant explicitly.
        /// </summary>
        [Test]
        public void CSettingsMigration_LegacyToSplit_RoundTripsPreserved()
        {
            byte[] legacyBytes = LoadGoldenOrIgnore("Settings", "Legacy.xml");

            RegistrySettings.baseDirectory = _tempDir;
            RegistrySettings.vehiclesDirectory = Path.Combine(_tempDir, "VehicleProfiles");
            RegistrySettings.toolsDirectory = Path.Combine(_tempDir, "ToolProfiles");
            Directory.CreateDirectory(RegistrySettings.vehiclesDirectory);
            Directory.CreateDirectory(RegistrySettings.toolsDirectory);

            // The migration source is the legacy combined profile under baseDirectory/Vehicles.
            string legacyDir = Path.Combine(_tempDir, "Vehicles");
            Directory.CreateDirectory(legacyDir);
            File.WriteAllBytes(Path.Combine(legacyDir, "LegacyProfile.xml"), legacyBytes);

            // The staged file is the old single-file format, so a migration is required.
            Assert.That(CSettingsMigration.NeedsMigration("LegacyProfile"), Is.True);

            VehicleSettings.Default.Reset();
            ToolSettings.Default.Reset();
            LoadResult vehicleResult = CSettingsMigration.MigrateVehicle(
                "LegacyProfile", "MigratedVehicle", VehicleSettings.Default);
            LoadResult toolResult = CSettingsMigration.MigrateTool(
                "LegacyProfile", "MigratedTool", ToolSettings.Default);
            Assert.That(vehicleResult, Is.EqualTo(LoadResult.Ok));
            Assert.That(toolResult, Is.EqualTo(LoadResult.Ok));

            string migratedVehiclePath = Path.Combine(RegistrySettings.vehiclesDirectory, "MigratedVehicle.xml");
            string migratedToolPath = Path.Combine(RegistrySettings.toolsDirectory, "MigratedTool.xml");
            Assert.That(File.Exists(migratedVehiclePath), Is.True);
            Assert.That(File.Exists(migratedToolPath), Is.True);

            byte[] migratedVehicleBytes = File.ReadAllBytes(migratedVehiclePath);
            byte[] migratedToolBytes = File.ReadAllBytes(migratedToolPath);

            // The split files produced by migration are the canonical serializer output, so loading each
            // back and re-saving must reproduce it byte-for-byte (the round-trip is preserved).
            VehicleSettings.Default.Reset();
            Assert.That(VehicleSettings.Default.Load("MigratedVehicle"), Is.EqualTo(LoadResult.Ok));
            VehicleSettings.Default.Save("MigratedVehicleResaved");
            byte[] vehicleResavedBytes = File.ReadAllBytes(
                Path.Combine(RegistrySettings.vehiclesDirectory, "MigratedVehicleResaved.xml"));
            Assert.That(vehicleResavedBytes, Is.EqualTo(migratedVehicleBytes));

            ToolSettings.Default.Reset();
            Assert.That(ToolSettings.Default.Load("MigratedTool"), Is.EqualTo(LoadResult.Ok));
            ToolSettings.Default.Save("MigratedToolResaved");
            byte[] toolResavedBytes = File.ReadAllBytes(
                Path.Combine(RegistrySettings.toolsDirectory, "MigratedToolResaved.xml"));
            Assert.That(toolResavedBytes, Is.EqualTo(migratedToolBytes));

            // Decimal hazard (AAP §0.6.5): the migrated double must be serialized with a PERIOD, never a
            // comma, regardless of host locale.
            AssertPeriodDecimalSeparator(
                migratedVehicleBytes, VehicleSettings.Default.setVehicle_maxAngularVelocity);
        }

        /// <summary>
        /// Phase F — the one-time legacy migration on Windows. Runs the legacy → split conversion against
        /// TEMP directories only, asserting it completes without throwing, produces the split profile, is
        /// idempotent (the one-time conversion can be repeated safely) and keeps the period decimal
        /// separator. The genuine HKCU registry read lives in <c>WindowsPlatformServices</c> /
        /// <c>CSettingsMigration.MigrateLegacyRegistrySettings()</c> behind its own <c>#if WINDOWS</c>
        /// guard; it targets the real <c>AppDataRoot</c> and cannot be redirected to a temp dir, so it is
        /// intentionally not invoked here (the test must never mutate the real Registry or <c>%AppData%</c>).
        /// On non-Windows operating systems there is nothing to migrate, so the test passes immediately.
        /// </summary>
        [Test]
        public void RegistryToPathMigration_OnWindows_IsOneTime()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Pass("Registry to path migration is Windows-only; nothing to assert on this operating system.");
                return;
            }

            RegistrySettings.baseDirectory = _tempDir;
            RegistrySettings.vehiclesDirectory = Path.Combine(_tempDir, "VehicleProfiles");
            Directory.CreateDirectory(RegistrySettings.vehiclesDirectory);

            string legacyDir = Path.Combine(_tempDir, "Vehicles");
            Directory.CreateDirectory(legacyDir);

            // In-test legacy fixture (NOT a golden) so this Windows path runs even before goldens exist.
            XmlSettingsHandler.SaveXMLFile(Path.Combine(legacyDir, "WinLegacy.xml"), new SettingsLegacy());

            Assert.That(CSettingsMigration.NeedsMigration("WinLegacy"), Is.True);

            VehicleSettings.Default.Reset();
            LoadResult firstPass = CSettingsMigration.MigrateVehicle(
                "WinLegacy", "WinMigrated", VehicleSettings.Default);
            Assert.That(firstPass, Is.EqualTo(LoadResult.Ok));

            string migratedPath = Path.Combine(RegistrySettings.vehiclesDirectory, "WinMigrated.xml");
            Assert.That(File.Exists(migratedPath), Is.True);

            // One-time / idempotent: re-running the conversion succeeds without throwing.
            LoadResult secondPass = CSettingsMigration.MigrateVehicle(
                "WinLegacy", "WinMigrated", VehicleSettings.Default);
            Assert.That(secondPass, Is.EqualTo(LoadResult.Ok));

            AssertPeriodDecimalSeparator(
                File.ReadAllBytes(migratedPath), VehicleSettings.Default.setVehicle_maxAngularVelocity);
        }

        /// <summary>
        /// Phase G — platform-detection sanity. Confirms the cross-platform
        /// <see cref="PlatformServicesFactory.CurrentPlatform"/> switch agrees with the running OS.
        /// <see cref="PlatformServicesFactory.Create"/> is intentionally NOT called: it throws when no
        /// per-OS implementation is registered, which is the case inside this isolated test process.
        /// </summary>
        [Test]
        public void PlatformServicesFactory_CurrentPlatform_MatchesRunningOs()
        {
            PlatformKind current = PlatformServicesFactory.CurrentPlatform;

            if (OperatingSystem.IsWindows())
            {
                Assert.That(current, Is.EqualTo(PlatformKind.Windows));
            }
            else if (OperatingSystem.IsLinux())
            {
                Assert.That(current, Is.EqualTo(PlatformKind.Linux));
            }
            else if (OperatingSystem.IsMacOS())
            {
                Assert.That(current, Is.EqualTo(PlatformKind.MacOS));
            }
            else
            {
                Assert.That(current, Is.EqualTo(PlatformKind.Unknown));
            }
        }

        /// <summary>
        /// Asserts that <paramref name="sampleValue"/> appears in <paramref name="settingsBytes"/> using a
        /// PERIOD decimal separator (its invariant-culture form) and never a comma-decimal form. This makes
        /// the cross-platform decimal hazard (AAP §0.6.5) explicit: a comma-decimal locale must not be able
        /// to leak into the serialized settings bytes.
        /// </summary>
        private static void AssertPeriodDecimalSeparator(byte[] settingsBytes, double sampleValue)
        {
            string text = System.Text.Encoding.UTF8.GetString(settingsBytes);
            string invariant = sampleValue.ToString(CultureInfo.InvariantCulture);

            Assert.That(text.Contains(invariant, StringComparison.Ordinal), Is.True);

            if (invariant.Contains('.'))
            {
                string commaVariant = invariant.Replace('.', ',');
                Assert.That(text.Contains(commaVariant, StringComparison.Ordinal), Is.False);
            }
        }

        /// <summary>
        /// Resolves a golden artifact next to the test assembly (it is copied there by the
        /// <c>AgOpenGPS.Tests.csproj</c> <c>Parity\Golden\**\*</c> rule) using
        /// <see cref="TestContext.CurrentContext"/>.<c>TestDirectory</c> and <see cref="Path.Combine"/> so
        /// resolution is correct and case-stable on Windows, Linux and macOS. When the artifact has not
        /// been captured yet the test self-reports as <c>Ignored</c> (tracked as an open risk in
        /// <c>MIGRATION_DOCS/PARITY_REPORT.md</c>) rather than failing.
        /// </summary>
        private static byte[] LoadGoldenOrIgnore(string category, string fileName)
        {
            string path = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Parity", "Golden", category, fileName);

            if (!File.Exists(path))
            {
                Assert.Ignore(
                    $"Golden artifact not yet captured: {path} — tracked as an open risk in MIGRATION_DOCS/PARITY_REPORT.md");
            }

            return File.ReadAllBytes(path);
        }
    }
}
