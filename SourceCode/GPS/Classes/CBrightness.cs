// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using AgOpenGPS.Core.Platform;

//Class written and inspired by Andy
public class CWindowsSettingsBrightnessController
{
    // [XPLAT] The Windows-only WMI brightness body that previously lived in this file has been
    // relocated to Platform/WindowsPlatformServices.cs (compiled under net8.0-windows). This class
    // is now a thin, cross-platform brightness *policy* shim: it routes the get/set primitives
    // through the IPlatformServices abstraction while preserving the original application-level
    // policy exactly — the ±10 step, the [10,100] clamp, the isWmiMonitor gate on every method, and
    // the -1 "no controllable display" graceful-degradation contract. The class name and global
    // namespace are kept unchanged to avoid rippling changes through callers.
    private readonly IPlatformServices _platformServices;

    public bool isWmiMonitor;

    // [XPLAT] Preferred constructor: the host (FormGPS/Avalonia shell) injects the application's
    // IPlatformServices so brightness routing uses the per-OS implementation selected at startup.
    public CWindowsSettingsBrightnessController(bool isOn, IPlatformServices platformServices)
    {
        _platformServices = platformServices ?? throw new ArgumentNullException(nameof(platformServices));

        if (isOn)
        {
            if (Get() == -1)
                isWmiMonitor = false;
            else isWmiMonitor = true;
        }
        else
            isWmiMonitor = false;
    }

    // [XPLAT] Back-compat constructor preserving the original (bool isOn) public surface so existing
    // call sites are unaffected; resolves IPlatformServices from the factory when a caller cannot
    // inject it directly (per the platform-services migration guidance).
    public CWindowsSettingsBrightnessController(bool isOn)
        : this(isOn, PlatformServicesFactory.Create())
    {
    }

    // [XPLAT] WMI body removed; delegates to the platform abstraction. Returns the current brightness
    // (0-100) or -1 when there is no controllable display, identical to the former WMI behavior.
    private int Get()
    {
        return _platformServices.GetBrightness();
    }

    // [XPLAT] WMI body removed; delegates to the platform abstraction. No-op when brightness control
    // is unavailable on the current display or operating system, identical to the former WMI behavior.
    private void Set(int brightness)
    {
        _platformServices.SetBrightness(brightness);
    }

    public void BrightnessIncrease()
    {
        if (isWmiMonitor) Set(Math.Min(100, Get() + 10));
    }

    public void BrightnessDecrease()
    {
        if (isWmiMonitor) Set(Math.Max(10, Get() - 10));
    }

    public int GetBrightness()
    {
        if (isWmiMonitor) return Get();
        else return -1;
    }

    public void SetBrightness(int bright)
    {
        if (isWmiMonitor) Set(bright);
    }
}
