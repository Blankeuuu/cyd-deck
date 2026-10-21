using Xunit;

namespace StreamDeck.Shortcuts.Tests;

public sealed class WindowsScriptExecutionTests
{
    [Fact]
    public async Task PowerShellScriptWithSpacedArgumentsWritesProof()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "CYD PS żółć " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var script = Path.Combine(folder, "test script.ps1");
        var output = Path.Combine(folder, "proof.txt");
        try {
            await File.WriteAllTextAsync(script, "param([string]$Value,[string]$Output)\nif ($Value -ne 'argument with spaces') { exit 9 }\n[IO.File]::WriteAllText($Output, 'executed')\n", TestContext.Current.CancellationToken);
            await new WindowsProcessScriptRunner().RunPowerShellAsync(script, ["argument with spaces", output], TestContext.Current.CancellationToken);
            Assert.Equal("executed", await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
        } finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task MissingScriptFailsBeforeLaunchingInterpreter()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cmd");
        await Assert.ThrowsAsync<FileNotFoundException>(() => new WindowsProcessScriptRunner().RunScriptAsync(missing, [], TestContext.Current.CancellationToken));
    }
    [Theory]
    [InlineData(".bat")]
    [InlineData(".cmd")]
    public async Task BatchScriptWithUnicodePathAndSpacedArgumentActuallyRuns(string extension)
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "CYD test żółć " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var script = Path.Combine(folder, "test script" + extension);
        var output = Path.Combine(folder, "proof.txt");
        try
        {
            await File.WriteAllTextAsync(script, "@echo off\r\nif not \"%~1\"==\"argument with spaces\" exit /b 9\r\necho executed>\"%~2\"\r\nexit /b 0\r\n", TestContext.Current.CancellationToken);
            await new WindowsProcessScriptRunner().RunScriptAsync(script, ["argument with spaces", output], TestContext.Current.CancellationToken);
            Assert.Equal("executed", (await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken)).Trim());
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task PowerShellFailureIsNotReportedAsSuccessfulLaunch()
    {
        if (!OperatingSystem.IsWindows()) return;
        var script = Path.Combine(Path.GetTempPath(), "cyd-test-" + Guid.NewGuid().ToString("N") + ".ps1");
        try
        {
            await File.WriteAllTextAsync(script, "exit 7", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new WindowsProcessScriptRunner().RunPowerShellAsync(script, [], TestContext.Current.CancellationToken));
        }
        finally { File.Delete(script); }
    }
}
