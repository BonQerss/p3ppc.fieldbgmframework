using p3ppc.fieldbgmframework.Configuration;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using System.Diagnostics;

namespace p3ppc.fieldbgmframework;

internal static class Utils
{
    private static ILogger _logger = null!;
    private static Config _config = null!;
    private static IStartupScanner _startupScanner = null!;

    internal static nint BaseAddress { get; private set; }

    internal static bool Initialise(ILogger logger, Config config, IModLoader modLoader)
    {
        _logger = logger;
        _config = config;
        using var thisProcess = Process.GetCurrentProcess();
        BaseAddress = thisProcess.MainModule!.BaseAddress;

        var startupScannerController = modLoader.GetController<IStartupScanner>();
        if (startupScannerController == null || !startupScannerController.TryGetTarget(out var startupScanner))
        {
            LogError("Unable to get controller for Reloaded SigScan Library.");
            return false;
        }

        _startupScanner = startupScanner;
        return true;
    }

    internal static void UpdateConfig(Config config) => _config = config;

    internal static void Log(string message) => _logger.WriteLine($"[Field BGM Framework] {message}");

    internal static void LogDebug(string message)
    {
        if (_config.DebugEnabled)
        {
            Log(message);
        }
    }

    internal static void LogError(string message, Exception e) => _logger.WriteLine($"[Field BGM Framework] {message}: {e.Message}", System.Drawing.Color.Red);

    internal static void LogError(string message) => _logger.WriteLine($"[Field BGM Framework] {message}", System.Drawing.Color.Red);

    internal static void SigScan(string pattern, string name, Action<nint> action)
    {
        _startupScanner.AddMainModuleScan(pattern, result =>
        {
            if (!result.Found)
            {
                LogError($"Unable to find {name}, so custom field BGM won't work.");
                return;
            }

            LogDebug($"Found {name} at 0x{result.Offset + BaseAddress:X}");
            action(result.Offset + BaseAddress);
        });
    }

    internal static unsafe nuint GetGlobalAddress(nint ptrAddress) => (nuint)(*(int*)ptrAddress + ptrAddress + 4);
}
