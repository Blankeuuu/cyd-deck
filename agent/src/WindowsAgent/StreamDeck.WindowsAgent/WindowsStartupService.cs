using Microsoft.Win32;

namespace StreamDeck.WindowsAgent;

public sealed class WindowsStartupService
{
    public const string CurrentValueName = AppIdentity.StartupRegistryValueName;
    public const string LegacyValueName = AppIdentity.LegacyStartupRegistryValueName;
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly AgentOptions options;
    private readonly IRunKeyAccessor runKey;
    private readonly string currentDirectory;

    public WindowsStartupService(AgentOptions options)
        : this(options, new RegistryRunKeyAccessor(), null)
    {
    }

    internal WindowsStartupService(AgentOptions options, IRunKeyAccessor runKey, string? currentExecutablePathOverride = null)
    {
        this.options = options;
        this.runKey = runKey;
        this.currentDirectory = ResolveCurrentDirectory(currentExecutablePathOverride);
    }

    public bool IsEnabled()
    {
        var currentValue = runKey.GetValue(CurrentValueName);
        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            if (string.Equals(currentValue, GetCommand(), StringComparison.Ordinal))
            {
                return true;
            }

            if (TryParseCommandLine(currentValue, out var currentExePath, out _) &&
                IsSameInstallExecutable(currentExePath))
            {
                return true;
            }
        }

        var legacyValue = runKey.GetValue(LegacyValueName);
        if (!string.IsNullOrWhiteSpace(legacyValue))
        {
            if (TryParseCommandLine(legacyValue, out var legacyExePath, out _) &&
                IsSameInstallExecutable(legacyExePath))
            {
                return true;
            }
        }

        return false;
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            runKey.SetValue(CurrentValueName, GetCommand());
            runKey.DeleteValue(LegacyValueName);
        }
        else
        {
            runKey.DeleteValue(CurrentValueName);
            runKey.DeleteValue(LegacyValueName);
        }
    }

    public bool MigrateLegacyStartupIfPresent()
    {
        var existingCurrent = runKey.GetValue(CurrentValueName);
        if (!string.IsNullOrWhiteSpace(existingCurrent))
        {
            return false;
        }

        var legacyValue = runKey.GetValue(LegacyValueName);
        if (string.IsNullOrWhiteSpace(legacyValue))
        {
            return false;
        }

        if (!TryParseCommandLine(legacyValue, out var legacyExePath, out var legacyArguments))
        {
            return false;
        }

        if (!IsSameInstallExecutable(legacyExePath))
        {
            return false;
        }

        var targetExePath = GetTargetExecutablePath();
        var migratedCommand = string.IsNullOrWhiteSpace(legacyArguments)
            ? $"\"{targetExePath}\""
            : $"\"{targetExePath}\" {legacyArguments}";

        runKey.SetValue(CurrentValueName, migratedCommand);
        runKey.DeleteValue(LegacyValueName);
        AgentDiagnosticLog.Write($"Migrated autostart registry entry from '{LegacyValueName}' to '{CurrentValueName}'");
        return true;
    }

    public string GetCommand()
    {
        var processPath = GetTargetExecutablePath();
        var command = $"\"{processPath}\"";
        if (options.MockMode)
        {
            command += " --mock";
        }
        return $"{command} --minimized";
    }

    internal string GetTargetExecutablePath()
    {
        if (string.IsNullOrWhiteSpace(currentDirectory))
        {
            return AppIdentity.ExecutableFileName;
        }

        return Path.Combine(currentDirectory, AppIdentity.ExecutableFileName);
    }

    internal static bool TryParseCommandLine(string? commandLine, out string executablePath, out string arguments)
    {
        executablePath = string.Empty;
        arguments = string.Empty;

        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return false;
        }

        var trimmed = commandLine.Trim();
        if (trimmed.StartsWith('"'))
        {
            var closingQuoteIndex = trimmed.IndexOf('"', 1);
            if (closingQuoteIndex <= 1)
            {
                return false;
            }

            executablePath = trimmed[1..closingQuoteIndex].Trim();
            arguments = trimmed[(closingQuoteIndex + 1)..].Trim();
            return !string.IsNullOrWhiteSpace(executablePath);
        }

        var searchStart = 0;
        while (searchStart < trimmed.Length)
        {
            var exeIndex = trimmed.IndexOf(".exe", searchStart, StringComparison.OrdinalIgnoreCase);
            if (exeIndex < 0)
            {
                break;
            }

            var endOfExe = exeIndex + 4;
            if (endOfExe == trimmed.Length || char.IsWhiteSpace(trimmed[endOfExe]))
            {
                executablePath = trimmed[..endOfExe].Trim();
                arguments = trimmed[endOfExe..].Trim();
                return !string.IsNullOrWhiteSpace(executablePath);
            }

            searchStart = endOfExe;
        }

        for (var i = 0; i < trimmed.Length; i++)
        {
            if (char.IsWhiteSpace(trimmed[i]))
            {
                executablePath = trimmed[..i].Trim();
                arguments = trimmed[(i + 1)..].Trim();
                return !string.IsNullOrWhiteSpace(executablePath);
            }
        }

        executablePath = trimmed;
        return !string.IsNullOrWhiteSpace(executablePath);
    }

    private bool IsSameInstallExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        string fileName;
        string? directoryName;
        try
        {
            if (!Path.IsPathRooted(executablePath))
            {
                return false;
            }

            fileName = Path.GetFileName(executablePath);
            directoryName = Path.GetDirectoryName(executablePath);
        }
        catch
        {
            return false;
        }

        var isKnownExeName =
            string.Equals(fileName, AppIdentity.ExecutableFileName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, AppIdentity.LegacyExecutableFileName, StringComparison.OrdinalIgnoreCase);

        if (!isKnownExeName)
        {
            return false;
        }

        var normalizedCandidateDir = NormalizeDirectoryPath(directoryName);
        var normalizedCurrentDir = NormalizeDirectoryPath(currentDirectory);

        if (string.IsNullOrEmpty(normalizedCandidateDir) || string.IsNullOrEmpty(normalizedCurrentDir))
        {
            return false;
        }

        return string.Equals(normalizedCandidateDir, normalizedCurrentDir, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveCurrentDirectory(string? executablePathOverride)
    {
        if (!string.IsNullOrWhiteSpace(executablePathOverride))
        {
            if (executablePathOverride.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(executablePathOverride);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    return NormalizeDirectoryPath(dir) ?? dir;
                }
            }

            return NormalizeDirectoryPath(executablePathOverride) ?? executablePathOverride;
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            var processDir = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrWhiteSpace(processDir))
            {
                return NormalizeDirectoryPath(processDir) ?? processDir;
            }
        }

        if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
        {
            return NormalizeDirectoryPath(AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
        }

        return string.Empty;
    }

    private static string? NormalizeDirectoryPath(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(directoryPath.Trim());
            return Path.TrimEndingDirectorySeparator(fullPath);
        }
        catch
        {
            return null;
        }
    }

    internal interface IRunKeyAccessor
    {
        string? GetValue(string name);
        void SetValue(string name, string value);
        void DeleteValue(string name);
    }

    private sealed class RegistryRunKeyAccessor : IRunKeyAccessor
    {
        public string? GetValue(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(name) as string;
        }

        public void SetValue(string name, string value)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(name, value, RegistryValueKind.String);
        }

        public void DeleteValue(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
    }
}
