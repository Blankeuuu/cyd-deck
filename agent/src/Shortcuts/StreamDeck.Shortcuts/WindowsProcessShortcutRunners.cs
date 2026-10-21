using System.Diagnostics;

namespace StreamDeck.Shortcuts;

public sealed class WindowsProcessApplicationLauncher : IApplicationLauncher
{
    public Task LaunchAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = CreateStartInfo(executablePath, arguments);
        Process.Start(startInfo);
        return Task.CompletedTask;
    }

    public static ProcessStartInfo CreateStartInfo(string executablePath, IReadOnlyList<string> arguments)
    {
        return CreateStartInfoCore(NormalizeConfiguredPath(executablePath), arguments);
    }

    private static ProcessStartInfo CreateStartInfoCore(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static string NormalizeConfiguredPath(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Trim();
        }

        return trimmed;
    }
}

public sealed class WindowsShellUrlOpener : IUrlOpener
{
    public Task OpenAsync(Uri url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Process.Start(new ProcessStartInfo
        {
            FileName = url.AbsoluteUri,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }
}

public sealed class WindowsProcessScriptRunner : IScriptRunner
{
    private readonly string powerShellPath;

    public WindowsProcessScriptRunner()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe"))
    {
    }

    public WindowsProcessScriptRunner(string powerShellPath)
    {
        if (string.IsNullOrWhiteSpace(powerShellPath) || !Path.IsPathFullyQualified(powerShellPath))
        {
            throw new ArgumentException("PowerShell host path must be fully qualified.", nameof(powerShellPath));
        }

        this.powerShellPath = powerShellPath;
    }

    public Task RunScriptAsync(string scriptPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ValidateConfiguredPath(scriptPath, nameof(scriptPath));
        if (scriptPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || scriptPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            static string QuoteBatchValue(string value)
            {
                if (value.IndexOfAny(['"', '%', '\r', '\n', '\0']) >= 0)
                    throw new ArgumentException("Batch path/arguments contain unsupported expansion characters.");
                return "\"" + value + "\"";
            }
            var command = string.Join(" ", new[] { scriptPath }.Concat(arguments).Select(QuoteBatchValue));
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                Arguments = "/d /s /v:off /c \"" + command + "\"",
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(scriptPath)!
            };
            return WaitForScriptAsync(start, cancellationToken);
        }
        return StartAsync(scriptPath, arguments, cancellationToken);
    }

    public Task RunPowerShellAsync(string scriptPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ValidateConfiguredPath(scriptPath, nameof(scriptPath));
        if (!scriptPath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("PowerShell script path must point to a .ps1 file.", nameof(scriptPath));
        }

        var fixedArguments = new[] { "-NoProfile", "-File", scriptPath }
            .Concat(arguments)
            .ToArray();

        return StartAsync(powerShellPath, fixedArguments, cancellationToken);
    }

    private static Task StartAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return WaitForScriptAsync(startInfo, cancellationToken);
    }

    private static async Task WaitForScriptAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Script process was not started.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidOperationException($"Script failed (exit code {process.ExitCode}).");
    }

    private static void ValidateConfiguredPath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Configured script path must be fully qualified.", parameterName);
        }
        if (!File.Exists(path)) throw new FileNotFoundException("Configured script is unavailable.");
    }
}
