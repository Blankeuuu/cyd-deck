using System.Runtime.InteropServices;

namespace StreamDeck.WindowsAgent;

/// <summary>
/// Central product identity and metadata definitions for CYD-DECK Windows Agent.
/// </summary>
public static class AppIdentity
{
    public const string Name = "CYD-DECK";
    public const string ExecutableFileName = "CYD-DECK.exe";
    public const string LegacyExecutableFileName = "StreamDeck.WindowsAgent.exe";
    public const string AppUserModelId = "Blankeuuu.CYD-DECK";
    public const string StartupRegistryValueName = "CYD-DECK";
    public const string LegacyStartupRegistryValueName = "StreamDeckWindowsAgent";
    public const string Publisher = "Blankeuuu";
    public const string Description = "CYD-DECK Windows Agent";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    /// <summary>
    /// Configures the process-wide Application User Model ID (AUMID).
    /// Primary documentation requirement: must be called before presenting any UI,
    /// before creating the taskbar/tray icon, and before any window is created.
    /// </summary>
    /// <returns>True if S_OK (0) was returned; false otherwise.</returns>
    public static bool ConfigureProcessExplicitAppUserModelId()
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            {
                return false;
            }

            var hr = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            if (hr != 0)
            {
                AgentDiagnosticLog.Write($"SetCurrentProcessExplicitAppUserModelID returned HRESULT 0x{hr:X8}");
                return false;
            }

            AgentDiagnosticLog.Write($"Configured process explicit AUMID: {AppUserModelId}");
            return true;
        }
        catch (Exception ex)
        {
            AgentDiagnosticLog.Write($"SetCurrentProcessExplicitAppUserModelID failed: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }
}
