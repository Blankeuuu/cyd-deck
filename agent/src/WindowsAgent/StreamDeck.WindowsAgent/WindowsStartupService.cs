using Microsoft.Win32;

namespace StreamDeck.WindowsAgent;

public sealed class WindowsStartupService(AgentOptions options)
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StreamDeckWindowsAgent";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && string.Equals(value, GetCommand(), StringComparison.Ordinal);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            key.SetValue(ValueName, GetCommand(), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private string GetCommand()
    {
        var command = $"\"{Environment.ProcessPath}\"";
        return options.MockMode ? $"{command} --mock" : command;
    }
}
