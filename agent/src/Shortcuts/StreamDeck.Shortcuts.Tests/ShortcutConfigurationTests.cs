using StreamDeck.Shortcuts;
using Xunit;

namespace StreamDeck.Shortcuts.Tests;

public sealed class ShortcutConfigurationTests
{
    [Fact]
    public void ValidFixtureCreatesDeviceConfigurationWithoutExecutionDetails()
    {
        var catalog = LoadValidCatalog();

        var deviceConfiguration = catalog.DeviceConfiguration;

        Assert.Equal(6, deviceConfiguration.Shortcuts.Count);
        Assert.Contains(deviceConfiguration.Shortcuts, shortcut => shortcut.Id == "screenshot" && shortcut.Icon == "camera" && shortcut.DisplayMode == ShortcutDisplayModes.Label);

        var json = ShortcutJson.SerializeDeviceConfiguration(deviceConfiguration);
        Assert.DoesNotContain("scriptPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("executablePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Tools", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://example.com/docs", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actionType", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidFixtureReportsDuplicatesMalformedValuesAndDangerousValues()
    {
        var configuration = LoadConfiguration("invalid-shortcuts.json");
        var validator = new ShortcutConfigurationValidator();

        var issues = validator.Validate(configuration);

        Assert.Contains(issues, issue => issue.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Path.EndsWith(".id", StringComparison.Ordinal) && issue.Message.Contains("stable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Path.Contains(".keys", StringComparison.Ordinal) && issue.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Path.Contains(".url", StringComparison.Ordinal) && issue.Message.Contains("http", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Path.Contains(".scriptPath", StringComparison.Ordinal) && issue.Message.Contains("local path", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("""{"shortcuts":null}""", "$.shortcuts")]
    [InlineData("""{"shortcuts":[null]}""", "$.shortcuts[0]")]
    [InlineData("""{"shortcuts":[{"id":"missing-action","name":"Missing Action","icon":"broken","action":null}]}""", "$.shortcuts[0].action")]
    [InlineData("""{"shortcuts":[{"id":"bad-hotkey","name":"Bad Hotkey","icon":"keyboard","action":{"type":"hotkey","keys":null}}]}""", "$.shortcuts[0].action.keys")]
    [InlineData("""{"shortcuts":[{"id":"bad-script","name":"Bad Script","icon":"script","action":{"type":"script","scriptPath":"C:\\Tools\\cleanup.cmd","arguments":null}}]}""", "$.shortcuts[0].action.arguments")]
    [InlineData("""{"shortcuts":[{"id":"bad-script-arg","name":"Bad Script Arg","icon":"script","action":{"type":"script","scriptPath":"C:\\Tools\\cleanup.cmd","arguments":[null]}}]}""", "$.shortcuts[0].action.arguments[0]")]
    public void MalformedNullValuesReturnValidationIssuesInsteadOfThrowing(string json, string expectedPath)
    {
        var configuration = ShortcutJson.DeserializeConfiguration(json);
        var validator = new ShortcutConfigurationValidator();

        var issues = validator.Validate(configuration);

        Assert.Contains(issues, issue => issue.Path == expectedPath);
    }

    [Theory]
    [InlineData("C:\\Program Files\\Example App\\Example.exe")]
    [InlineData("\"C:\\Program Files\\Example App\\Example.exe\"")]
    [InlineData("notepad.exe")]
    public void LaunchApplicationAcceptsFullQuotedFullAndBareExecutablePaths(string executablePath)
    {
        var configuration = CreateLaunchConfiguration(executablePath);
        var validator = new ShortcutConfigurationValidator();

        var issues = validator.Validate(configuration);

        Assert.DoesNotContain(issues, issue => issue.Path.EndsWith(".executablePath", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("..\\Tools\\app.exe")]
    [InlineData("folder\\app.exe")]
    [InlineData("https://example.com/app.exe")]
    [InlineData("\\\\fileserver\\share\\app.exe")]
    public void LaunchApplicationRejectsRelativeOrRemoteExecutablePaths(string executablePath)
    {
        var configuration = CreateLaunchConfiguration(executablePath);
        var validator = new ShortcutConfigurationValidator();

        var issues = validator.Validate(configuration);

        Assert.Contains(issues, issue => issue.Path.EndsWith(".executablePath", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\\\\fileserver\\share\\cleanup.cmd")]
    [InlineData("\\\\fileserver\\share\\build-report.ps1")]
    public void ScriptActionsRejectUncNetworkPaths(string scriptPath)
    {
        var configuration = new ShortcutConfiguration
        {
            Shortcuts =
            [
                new ShortcutDefinition
                {
                    Id = "network-script",
                    Name = "Network Script",
                    Icon = "script",
                    Action = new ShortcutActionDefinition
                    {
                        Type = scriptPath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
                            ? ShortcutActionTypes.PowerShell
                            : ShortcutActionTypes.Script,
                        ScriptPath = scriptPath,
                        Arguments = []
                    }
                }
            ]
        };
        var validator = new ShortcutConfigurationValidator();

        var issues = validator.Validate(configuration);

        Assert.Contains(issues, issue => issue.Path.EndsWith(".scriptPath", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Example App\\Example.exe\"", "C:\\Program Files\\Example App\\Example.exe")]
    [InlineData("notepad.exe", "notepad.exe")]
    public void WindowsApplicationLauncherNormalizesConfiguredExecutablePath(string executablePath, string expectedFileName)
    {
        var startInfo = WindowsProcessApplicationLauncher.CreateStartInfo(executablePath, ["--flag"]);

        Assert.Equal(expectedFileName, startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(["--flag"], startInfo.ArgumentList);
    }

    [Fact]
    public async Task UnknownShortcutIdDoesNotCallAnyExecutor()
    {
        var executor = CreateExecutor(LoadValidCatalog(), out var calls);

        var result = await executor.ExecuteAsync(new ShortcutExecuteRequest("run-anything-from-lan"), TestContext.Current.CancellationToken);

        Assert.Equal(ShortcutExecutionStatus.UnknownShortcut, result.Status);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task ExecuteUsesOnlyLocallyConfiguredActionForRequestedId()
    {
        var executor = CreateExecutor(LoadValidCatalog(), out var calls);

        var result = await executor.ExecuteAsync(new ShortcutExecuteRequest("build-report"), TestContext.Current.CancellationToken);

        Assert.Equal(ShortcutExecutionStatus.Executed, result.Status);
        Assert.Equal(["powershell:C:\\Tools\\build-report.ps1:daily"], calls);
    }

    [Fact]
    public async Task EachSupportedActionTypeIsDispatched()
    {
        var executor = CreateExecutor(LoadValidCatalog(), out var calls);

        await executor.ExecuteAsync(new ShortcutExecuteRequest("discord"), TestContext.Current.CancellationToken);
        await executor.ExecuteAsync(new ShortcutExecuteRequest("docs"), TestContext.Current.CancellationToken);
        await executor.ExecuteAsync(new ShortcutExecuteRequest("screenshot"), TestContext.Current.CancellationToken);
        await executor.ExecuteAsync(new ShortcutExecuteRequest("cleanup"), TestContext.Current.CancellationToken);
        await executor.ExecuteAsync(new ShortcutExecuteRequest("custom-mute"), TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "launch:C:\\Program Files\\Discord\\Discord.exe:",
                "url:https://example.com/docs",
                "hotkey:WIN+SHIFT+S",
                "script:C:\\Tools\\cleanup.cmd:--quiet",
                "adapter:audio:toggle-mic"
            ],
            calls);
    }

    private static ShortcutCatalog LoadValidCatalog()
    {
        return ShortcutCatalog.Create(LoadConfiguration("valid-shortcuts.json"));
    }

    private static ShortcutConfiguration LoadConfiguration(string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        return ShortcutJson.DeserializeConfiguration(File.ReadAllText(path));
    }

    private static ShortcutConfiguration CreateLaunchConfiguration(string executablePath)
    {
        return new ShortcutConfiguration
        {
            Shortcuts =
            [
                new ShortcutDefinition
                {
                    Id = "launch-app",
                    Name = "Launch App",
                    Icon = "app",
                    Action = new ShortcutActionDefinition
                    {
                        Type = ShortcutActionTypes.LaunchApplication,
                        ExecutablePath = executablePath,
                        Arguments = []
                    }
                }
            ]
        };
    }

    private static ShortcutExecutor CreateExecutor(ShortcutCatalog catalog, out List<string> calls)
    {
        calls = [];
        return new ShortcutExecutor(
            catalog,
            new RecordingApplicationLauncher(calls),
            new RecordingUrlOpener(calls),
            new RecordingHotkeySender(calls),
            new RecordingScriptRunner(calls),
            new RecordingPredefinedAdapter(calls));
    }

    private sealed class RecordingApplicationLauncher(List<string> calls) : IApplicationLauncher
    {
        public Task LaunchAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            calls.Add($"launch:{executablePath}:{string.Join(' ', arguments)}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUrlOpener(List<string> calls) : IUrlOpener
    {
        public Task OpenAsync(Uri url, CancellationToken cancellationToken)
        {
            calls.Add($"url:{url.AbsoluteUri.TrimEnd('/')}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHotkeySender(List<string> calls) : IHotkeySender
    {
        public Task SendAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken)
        {
            calls.Add($"hotkey:{string.Join('+', keys)}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingScriptRunner(List<string> calls) : IScriptRunner
    {
        public Task RunScriptAsync(string scriptPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            calls.Add($"script:{scriptPath}:{string.Join(' ', arguments)}");
            return Task.CompletedTask;
        }

        public Task RunPowerShellAsync(string scriptPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            calls.Add($"powershell:{scriptPath}:{string.Join(' ', arguments)}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPredefinedAdapter(List<string> calls) : IPredefinedShortcutAdapter
    {
        public Task ExecuteAsync(string adapterKey, CancellationToken cancellationToken)
        {
            calls.Add($"adapter:{adapterKey}");
            return Task.CompletedTask;
        }
    }
}
