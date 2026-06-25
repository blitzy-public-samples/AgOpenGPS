// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Runtime.InteropServices;

namespace AgOpenGPS.Core.Platform
{
    /// <summary>
    /// Identifies the operating-system family that AgOpenGPS is running on.
    /// </summary>
    public enum PlatformKind
    {
        Unknown = 0,
        Windows,
        Linux,
        MacOS
    }

    /// <summary>
    /// Selects the correct <see cref="IPlatformServices"/> implementation at startup. The
    /// portable Core owns only the operating-system detection (via
    /// <see cref="RuntimeInformation"/>) and a per-OS registration slot; the concrete
    /// implementations live in the GPS project, which registers them during bootstrap. Core
    /// therefore never references GPS, avoiding a circular dependency.
    /// See MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </summary>
    public static class PlatformServicesFactory
    {
        private static Func<IPlatformServices> _windowsFactory;
        private static Func<IPlatformServices> _linuxFactory;
        private static Func<IPlatformServices> _macFactory;

        /// <summary>
        /// The operating-system family detected for the current process.
        /// </summary>
        public static PlatformKind CurrentPlatform
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return PlatformKind.Windows;
                }
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return PlatformKind.Linux;
                }
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return PlatformKind.MacOS;
                }
                return PlatformKind.Unknown;
            }
        }

        /// <summary>
        /// Registers the concrete <see cref="IPlatformServices"/> creators supplied by the host
        /// application. Each slot is optional so a single-OS build can register only the creator
        /// compiled for its target (the Windows implementation targets net8.0-windows). A null
        /// argument leaves the corresponding slot unchanged.
        /// </summary>
        public static void Register(
            Func<IPlatformServices> windowsFactory = null,
            Func<IPlatformServices> linuxFactory = null,
            Func<IPlatformServices> macFactory = null)
        {
            if (windowsFactory != null)
            {
                _windowsFactory = windowsFactory;
            }
            if (linuxFactory != null)
            {
                _linuxFactory = linuxFactory;
            }
            if (macFactory != null)
            {
                _macFactory = macFactory;
            }
        }

        /// <summary>
        /// Creates the <see cref="IPlatformServices"/> implementation registered for the current
        /// operating system. Throws <see cref="PlatformNotSupportedException"/> when the OS is
        /// unrecognised and <see cref="InvalidOperationException"/> when no implementation was
        /// registered for the current OS.
        /// </summary>
        public static IPlatformServices Create()
        {
            switch (CurrentPlatform)
            {
                case PlatformKind.Windows:
                    return Invoke(_windowsFactory, PlatformKind.Windows);
                case PlatformKind.Linux:
                    return Invoke(_linuxFactory, PlatformKind.Linux);
                case PlatformKind.MacOS:
                    return Invoke(_macFactory, PlatformKind.MacOS);
                default:
                    throw new PlatformNotSupportedException(
                        "AgOpenGPS does not recognise the current operating system.");
            }
        }

        private static IPlatformServices Invoke(Func<IPlatformServices> factory, PlatformKind kind)
        {
            if (factory == null)
            {
                throw new InvalidOperationException(
                    "No IPlatformServices implementation was registered for " + kind +
                    ". Call PlatformServicesFactory.Register(...) during application startup.");
            }

            return factory();
        }
    }
}
