using System;
using System.Runtime.InteropServices;

using Microsoft.Win32;

using static Dalamud.api;

namespace MidiBard.Util;

public static class WineDetector
{
    private static readonly Lazy<bool> _isRunningUnderWine = new(DetectWine);
    private static readonly Lazy<bool> _isLinuxEnvironment = new(DetectLinux);
    private static readonly Lazy<string> _wineVersion = new(DetectWineVersion);

    public static bool IsRunningUnderWine => _isRunningUnderWine.Value;
    public static bool IsLinuxEnvironment => _isLinuxEnvironment.Value;
    public static string WineVersion => _wineVersion.Value;

    private static bool DetectWine()
    {
        try
        {
            // Method 1: Check Wine registry key
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Wine"))
            {
                if (key != null)
                {
                    PluginLog.Information("Wine detected via registry key");
                    return true;
                }
            }

            // Method 2: Check environment variables
            var winePrefix = Environment.GetEnvironmentVariable("WINEPREFIX");
            var wine = Environment.GetEnvironmentVariable("WINE");

            if (!string.IsNullOrEmpty(winePrefix) || !string.IsNullOrEmpty(wine))
            {
                PluginLog.Information($"Wine detected via environment variables - WINEPREFIX: {winePrefix}, WINE: {wine}");
                return true;
            }

            // Method 3: Check process hierarchy (Wine processes often have wine in the name)
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var processName = currentProcess.ProcessName.ToLowerInvariant();

            if (processName.Contains("wine"))
            {
                PluginLog.Information("Wine detected via process name");
                return true;
            }

            PluginLog.Debug("Wine detection completed - not running under Wine");
            return false;
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "Exception during Wine detection, assuming not Wine");
            return false;
        }
    }

    private static bool DetectLinux()
    {
        try
        {
            var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            PluginLog.Debug($"Linux environment detection: {isLinux}");
            return isLinux;
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "Exception during Linux detection, assuming not Linux");
            return false;
        }
    }

    private static string DetectWineVersion()
    {
        try
        {
            if (!IsRunningUnderWine) return null;

            // Try to get Wine version from registry
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Wine"))
            {
                if (key != null)
                {
                    var version = key.GetValue("Version")?.ToString();
                    if (!string.IsNullOrEmpty(version))
                    {
                        PluginLog.Information($"Wine version detected: {version}");
                        return version;
                    }
                }
            }

            // Try environment variable
            var wineVersion = Environment.GetEnvironmentVariable("WINE_VERSION");
            if (!string.IsNullOrEmpty(wineVersion))
            {
                PluginLog.Information($"Wine version from environment: {wineVersion}");
                return wineVersion;
            }

            return "unknown";
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "Exception during Wine version detection");
            return "unknown";
        }
    }
}
