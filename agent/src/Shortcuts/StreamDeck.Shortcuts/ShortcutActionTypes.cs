namespace StreamDeck.Shortcuts;

public static class ShortcutActionTypes
{
    public const string LaunchApplication = "launch";
    public const string OpenUrl = "url";
    public const string Hotkey = "hotkey";
    public const string Script = "script";
    public const string PowerShell = "powershell";
    public const string PredefinedAdapter = "predefined";

    public static bool IsKnown(string? actionType)
    {
        return actionType is LaunchApplication
            or OpenUrl
            or Hotkey
            or Script
            or PowerShell
            or PredefinedAdapter;
    }
}
