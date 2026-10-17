using StreamDeck.Configuration;
using StreamDeck.Shortcuts;
using System.Diagnostics.CodeAnalysis;

var tests = new (string Name, Action Run)[]
{
    ("missing config loads defaults", MissingConfigLoadsDefaults),
    ("configuration roundtrips shortcuts", ConfigurationRoundTripsShortcuts),
    ("appearance configuration validates and serializes", AppearanceConfigurationValidatesAndSerializes),
    ("invalid json and invalid ranges fail", InvalidFileAndConfigurationFail),
    ("null config sections fail with validation issues", NullConfigSectionsFailWithValidationIssues),
    ("shortcut validation is reused", ShortcutValidationIsReused),
    ("save replaces existing file atomically", SaveReplacesExistingFileAtomically),
    ("failed atomic replace leaves existing file intact", FailedAtomicReplaceLeavesExistingFileIntact),
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

static void MissingConfigLoadsDefaults()
{
    using var workspace = TestWorkspace.Create();
    var store = new AgentConfigurationStore(workspace.ConfigPath);

    var configuration = store.LoadOrDefault();

    AssertNotNull(configuration.Server, "server");
    AssertNotNull(configuration.Monitoring, "monitoring");
    AssertNotNull(configuration.Covers, "covers");
    AssertNotNull(configuration.Appearance, "appearance");
    AssertNotNull(configuration.Shortcuts, "shortcuts");
    AssertEqual(8765, configuration.Server.Port, "server port");
    AssertEqual(1000, configuration.Monitoring.IntervalMs, "monitoring interval");
    AssertEqual(140, configuration.Covers.Size, "cover size");
    AssertEqual(80, configuration.Covers.JpegQuality, "jpeg quality");
    AssertEqual("dark", configuration.Appearance.Theme, "appearance theme");
    AssertEqual("#7EAFE3", configuration.Appearance.Accent, "appearance accent");
    AssertEqual("#101216", configuration.Appearance.Background, "appearance background");
    AssertEqual("rounded", configuration.Appearance.ButtonStyle, "appearance button style");
    AssertEqual(70, configuration.Appearance.Brightness, "appearance brightness");
    AssertEqual(300, configuration.Appearance.DimAfterSec, "appearance dim timeout");
    AssertEqual(20, configuration.Appearance.DimBrightness, "appearance dim brightness");
    AssertEqual(1800, configuration.Appearance.ScreenOffAfterSec, "appearance screen-off timeout");
    AssertEqual(true, configuration.Appearance.WakeOnTouch, "appearance wake on touch");
    AssertEqual(0, configuration.Shortcuts.Count, "shortcuts count");
}

static void ConfigurationRoundTripsShortcuts()
{
    using var workspace = TestWorkspace.Create();
    var store = new AgentConfigurationStore(workspace.ConfigPath);
    var expected = new AgentConfiguration
    {
        Server = new ServerConfiguration { Port = 9000 },
        Monitoring = new MonitoringConfiguration { IntervalMs = 2_000 },
        Covers = new CoversConfiguration { Size = 160, JpegQuality = 72 },
        Shortcuts =
        [
            new ShortcutDefinition
            {
                Id = "docs",
                Name = "Docs",
                Icon = "browser",
                Action = new ShortcutActionDefinition
                {
                    Type = ShortcutActionTypes.OpenUrl,
                    Url = "https://example.com/docs"
                }
            }
        ]
    };

    store.Save(expected);
    var loaded = store.Load();

    AssertNotNull(loaded.Server, "server");
    AssertNotNull(loaded.Monitoring, "monitoring");
    AssertNotNull(loaded.Covers, "covers");
    AssertNotNull(loaded.Shortcuts, "shortcuts");
    AssertEqual(expected.Server!.Port, loaded.Server.Port, "server port");
    AssertEqual(expected.Monitoring!.IntervalMs, loaded.Monitoring.IntervalMs, "monitoring interval");
    AssertEqual(expected.Covers!.Size, loaded.Covers.Size, "cover size");
    AssertEqual(expected.Covers.JpegQuality, loaded.Covers.JpegQuality, "jpeg quality");
    AssertEqual("docs", loaded.Shortcuts[0].Id, "shortcut id");
    AssertEqual("https://example.com/docs", loaded.Shortcuts[0].Action.Url, "shortcut url");

    var json = File.ReadAllText(workspace.ConfigPath);
    AssertDoesNotContain("token", json, "json token");
    AssertDoesNotContain("secret", json, "json secret");
}

static void InvalidFileAndConfigurationFail()
{
    using var workspace = TestWorkspace.Create();
    File.WriteAllText(workspace.ConfigPath, "{ nope");
    var store = new AgentConfigurationStore(workspace.ConfigPath);

    AssertThrows<AgentConfigurationException>(() => store.Load(), "invalid json");

    var invalid = new AgentConfiguration
    {
        Server = new ServerConfiguration { Port = 70_000 },
        Monitoring = new MonitoringConfiguration { IntervalMs = 10 },
        Covers = new CoversConfiguration { Size = 10, JpegQuality = 101 }
    };

    var exception = AssertThrows<AgentConfigurationException>(() => store.Save(invalid), "invalid config");
    AssertContains("$.server.port", exception.Issues.Select(issue => issue.Path), "server issue");
    AssertContains("$.monitoring.intervalMs", exception.Issues.Select(issue => issue.Path), "monitoring issue");
    AssertContains("$.covers.size", exception.Issues.Select(issue => issue.Path), "cover size issue");
    AssertContains("$.covers.jpegQuality", exception.Issues.Select(issue => issue.Path), "jpeg issue");
}

static void AppearanceConfigurationValidatesAndSerializes()
{
    using var workspace = TestWorkspace.Create();
    var store = new AgentConfigurationStore(workspace.ConfigPath);
    var invalid = new AgentConfiguration
    {
        Appearance = new AppearanceConfiguration
        {
            Theme = "blue",
            Accent = "not-a-color",
            Background = "#12345Z",
            ButtonStyle = "square",
            PageOrder = ["media", "media", "pc", "info"],
            PageVisibility = ["media", "pc"],
            Brightness = 9,
            DimAfterSec = 86401,
            DimBrightness = 101,
            ScreenOffAfterSec = -1
        }
    };

    var exception = AssertThrows<AgentConfigurationException>(() => store.Save(invalid), "invalid appearance");
    var paths = exception.Issues.Select(issue => issue.Path).ToArray();
    AssertContains("$.appearance.theme", paths, "theme issue");
    AssertContains("$.appearance.accent", paths, "accent issue");
    AssertContains("$.appearance.background", paths, "background issue");
    AssertContains("$.appearance.buttonStyle", paths, "button style issue");
    AssertContains("$.appearance.pageOrder", paths, "page order issue");
    AssertContains("$.appearance.pageVisibility", paths, "page visibility issue");
    AssertContains("$.appearance.brightness", paths, "brightness issue");
    AssertContains("$.appearance.dimAfterSec", paths, "dim timeout issue");
    AssertContains("$.appearance.dimBrightness", paths, "dim brightness issue");
    AssertContains("$.appearance.screenOffAfterSec", paths, "screen-off issue");

    store.Save(new AgentConfiguration { Appearance = new AppearanceConfiguration { DimAfterSec = 86400, ScreenOffAfterSec = 86400 } });
    var json = File.ReadAllText(workspace.ConfigPath);
    AssertTrue(json.Contains("\"appearance\"", StringComparison.Ordinal), "appearance json");
    AssertTrue(json.Contains("\"dimAfterSec\": 86400", StringComparison.Ordinal), "dim timeout json");
    AssertTrue(json.Contains("\"screenOffAfterSec\": 86400", StringComparison.Ordinal), "screen-off json");
}

static void NullConfigSectionsFailWithValidationIssues()
{
    using var workspace = TestWorkspace.Create();
    File.WriteAllText(
        workspace.ConfigPath,
        """
        {
          "server": null,
          "monitoring": null,
          "covers": null,
          "appearance": null,
          "shortcuts": null
        }
        """);

    var store = new AgentConfigurationStore(workspace.ConfigPath);

    var exception = AssertThrows<AgentConfigurationException>(() => store.Load(), "null sections");
    AssertContains("$.server", exception.Issues.Select(issue => issue.Path), "server null issue");
    AssertContains("$.monitoring", exception.Issues.Select(issue => issue.Path), "monitoring null issue");
    AssertContains("$.covers", exception.Issues.Select(issue => issue.Path), "covers null issue");
    AssertContains("$.appearance", exception.Issues.Select(issue => issue.Path), "appearance null issue");
    AssertContains("$.shortcuts", exception.Issues.Select(issue => issue.Path), "shortcuts null issue");
}

static void ShortcutValidationIsReused()
{
    using var workspace = TestWorkspace.Create();
    var store = new AgentConfigurationStore(workspace.ConfigPath);
    var invalid = new AgentConfiguration
    {
        Shortcuts =
        [
            new ShortcutDefinition
            {
                Id = "bad shortcut id!",
                Name = "Bad",
                Icon = "terminal",
                Action = new ShortcutActionDefinition { Type = "unknown" }
            }
        ]
    };

    var exception = AssertThrows<AgentConfigurationException>(() => store.Save(invalid), "invalid shortcut");

    AssertContains("$.shortcuts[0].id", exception.Issues.Select(issue => issue.Path), "shortcut id issue");
    AssertContains("$.shortcuts[0].action.type", exception.Issues.Select(issue => issue.Path), "shortcut type issue");
}

static void SaveReplacesExistingFileAtomically()
{
    using var workspace = TestWorkspace.Create();
    var fileSystem = new RecordingFileSystem();
    var store = new AgentConfigurationStore(workspace.ConfigPath, fileSystem: fileSystem);
    File.WriteAllText(workspace.ConfigPath, AgentConfigurationJson.Serialize(AgentConfiguration.CreateDefault()));

    store.Save(new AgentConfiguration { Server = new ServerConfiguration { Port = 9010 } });

    AssertTrue(fileSystem.ReplaceWasCalled, "replace called");
    var loaded = store.Load();
    AssertNotNull(loaded.Server, "server");
    AssertEqual(9010, loaded.Server.Port, "replaced port");
    AssertEqual(0, Directory.GetFiles(workspace.Root, "*.tmp").Length, "temp files");
}

static void FailedAtomicReplaceLeavesExistingFileIntact()
{
    using var workspace = TestWorkspace.Create();
    var original = new AgentConfiguration { Server = new ServerConfiguration { Port = 8765 } };
    File.WriteAllText(workspace.ConfigPath, AgentConfigurationJson.Serialize(original));

    var fileSystem = new RecordingFileSystem { ThrowOnReplace = true };
    var store = new AgentConfigurationStore(workspace.ConfigPath, fileSystem: fileSystem);

    AssertThrows<IOException>(() => store.Save(new AgentConfiguration { Server = new ServerConfiguration { Port = 9999 } }), "replace failure");
    var loaded = store.Load();
    AssertNotNull(loaded.Server, "server");
    AssertEqual(8765, loaded.Server.Port, "original port");
    AssertEqual(0, Directory.GetFiles(workspace.Root, "*.tmp").Length, "temp files");
}

static void AssertEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }
}

static void AssertTrue(bool value, string name)
{
    if (!value)
    {
        throw new InvalidOperationException($"{name}: expected true");
    }
}

static void AssertNotNull<T>([NotNull] T? value, string name)
{
    if (value is null)
    {
        throw new InvalidOperationException($"{name}: expected non-null");
    }
}

static void AssertContains<T>(T expected, IEnumerable<T> actual, string name)
{
    if (!actual.Contains(expected))
    {
        throw new InvalidOperationException($"{name}: expected collection to contain {expected}");
    }
}

static void AssertDoesNotContain(string unexpected, string actual, string name)
{
    if (actual.Contains(unexpected, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{name}: expected content not to contain {unexpected}");
    }
}

static TException AssertThrows<TException>(Action action, string name)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}");
}

sealed class TestWorkspace : IDisposable
{
    private TestWorkspace(string root)
    {
        Root = root;
        ConfigPath = Path.Combine(root, AgentConfigurationStore.FileName);
    }

    public string Root { get; }

    public string ConfigPath { get; }

    public static TestWorkspace Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "streamdeck-configuration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new TestWorkspace(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

sealed class RecordingFileSystem : IConfigurationFileSystem
{
    private readonly ConfigurationFileSystem _inner = new();

    public bool ReplaceWasCalled { get; private set; }

    public bool ThrowOnReplace { get; init; }

    public bool FileExists(string path)
    {
        return _inner.FileExists(path);
    }

    public void CreateDirectory(string path)
    {
        _inner.CreateDirectory(path);
    }

    public string ReadAllText(string path)
    {
        return _inner.ReadAllText(path);
    }

    public void WriteAllText(string path, string contents)
    {
        _inner.WriteAllText(path, contents);
    }

    public void ReplaceFile(string sourceFileName, string destinationFileName, string? destinationBackupFileName)
    {
        ReplaceWasCalled = true;
        if (ThrowOnReplace)
        {
            throw new IOException("replace failed");
        }

        _inner.ReplaceFile(sourceFileName, destinationFileName, destinationBackupFileName);
    }

    public void MoveFile(string sourceFileName, string destinationFileName, bool overwrite)
    {
        _inner.MoveFile(sourceFileName, destinationFileName, overwrite);
    }

    public void DeleteFile(string path)
    {
        _inner.DeleteFile(path);
    }
}
