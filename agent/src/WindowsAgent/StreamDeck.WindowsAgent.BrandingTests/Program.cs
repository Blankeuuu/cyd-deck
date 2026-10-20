using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using StreamDeck.WindowsAgent;
using StreamDeck.WindowsAgent.Tray;

namespace StreamDeck.WindowsAgent.BrandingTests;

internal static class Program
{
    private static int testsRun;
    private static int testsPassed;

    private static void Main(string[] args)
    {
        Console.WriteLine("=== CYD-DECK Windows Agent Branding & Identity Tests ===");

        var repoRoot = ResolveRepoRoot();
        var assetsDir = Path.Combine(repoRoot, "agent", "assets");

        if (args != null && args.Any(a => a.Equals("--generate", StringComparison.OrdinalIgnoreCase) || a.Equals("--generate-icons", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("Generating icon assets (cyd-deck.png and cyd-deck.ico)...");
            IconGenerator.GenerateAll(assetsDir);
            Console.WriteLine("Icons generated successfully.");
        }

        TestAppIdentityConstants();
        TestAumidConfiguration();
        TestWindowsStartupServiceAutostartAndMigration();
        TestOfficialIconAssetsAndAppIconFactory();
        TestInstallerScriptConsistency();
        TestBuildAndPublishScriptsConsistency();
        TestAssemblyAndPeMetadata();

        Console.WriteLine($"\n=== All {testsPassed}/{testsRun} Branding Tests PASSED ===");
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        testsRun++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"FAILED: {message}. Expected: '{expected}', Actual: '{actual}'");
        }
        testsPassed++;
        Console.WriteLine($"  [PASS] {message}");
    }

    private static void AssertTrue(bool condition, string message)
    {
        testsRun++;
        if (!condition)
        {
            throw new InvalidOperationException($"FAILED: {message}");
        }
        testsPassed++;
        Console.WriteLine($"  [PASS] {message}");
    }

    private static void TestAppIdentityConstants()
    {
        Console.WriteLine("\n-- Testing AppIdentity Constants --");
        AssertEqual("CYD-DECK", AppIdentity.Name, "AppIdentity.Name is CYD-DECK");
        AssertEqual("CYD-DECK.exe", AppIdentity.ExecutableFileName, "AppIdentity.ExecutableFileName is CYD-DECK.exe");
        AssertEqual("StreamDeck.WindowsAgent.exe", AppIdentity.LegacyExecutableFileName, "AppIdentity.LegacyExecutableFileName is StreamDeck.WindowsAgent.exe");
        AssertEqual("Blankeuuu.CYD-DECK", AppIdentity.AppUserModelId, "AppIdentity.AppUserModelId is Blankeuuu.CYD-DECK");
        AssertEqual("CYD-DECK", AppIdentity.StartupRegistryValueName, "AppIdentity.StartupRegistryValueName is CYD-DECK");
        AssertEqual("StreamDeckWindowsAgent", AppIdentity.LegacyStartupRegistryValueName, "AppIdentity.LegacyStartupRegistryValueName is StreamDeckWindowsAgent");
        AssertEqual("Blankeuuu", AppIdentity.Publisher, "AppIdentity.Publisher is Blankeuuu");
        AssertEqual("CYD-DECK Windows Agent", AppIdentity.Description, "AppIdentity.Description is CYD-DECK Windows Agent");
    }

    private static void TestAumidConfiguration()
    {
        Console.WriteLine("\n-- Testing AUMID Configuration P/Invoke Safety --");
        var result = AppIdentity.ConfigureProcessExplicitAppUserModelId();
        AssertTrue(result || !result, "ConfigureProcessExplicitAppUserModelId executes gracefully without exception");
    }

    private sealed class InMemoryRunKeyAccessor : WindowsStartupService.IRunKeyAccessor
    {
        public readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase);

        public string? GetValue(string name)
        {
            return Values.TryGetValue(name, out var val) ? val : null;
        }

        public void SetValue(string name, string value)
        {
            Values[name] = value;
        }

        public void DeleteValue(string name)
        {
            Values.Remove(name);
        }
    }

    private static void TestWindowsStartupServiceAutostartAndMigration()
    {
        Console.WriteLine("\n-- Testing WindowsStartupService Autostart & Migration (Isolated In-Memory) --");

        const string installDir = @"C:\Users\Test\AppData\Local\Programs\CYD-DECK";
        const string currentExe = @"C:\Users\Test\AppData\Local\Programs\CYD-DECK\CYD-DECK.exe";
        const string legacyExeInSameDir = @"C:\Users\Test\AppData\Local\Programs\CYD-DECK\StreamDeck.WindowsAgent.exe";
        const string otherInstallLegacyExe = @"C:\OtherInstall\CYD-DECK\StreamDeck.WindowsAgent.exe";
        const string otherInstallNewExe = @"C:\Worktree\bin\Debug\CYD-DECK.exe";

        var options = new AgentOptions(MockMode: true);
        var fakeRegistry = new InMemoryRunKeyAccessor();
        var service = new WindowsStartupService(options, fakeRegistry, currentExe);

        // 1. Brak legacy i brak nowego wpisu -> autostart wyłączony, migracja zwraca false i nie włącza autostartu
        AssertTrue(!service.IsEnabled(), "Service is disabled when registry has no autostart entries");
        AssertTrue(!service.MigrateLegacyStartupIfPresent(), "MigrateLegacyStartupIfPresent returns false when no legacy entry exists");
        AssertEqual((string?)null, fakeRegistry.GetValue("CYD-DECK"), "MigrateLegacyStartupIfPresent does not enable autostart when no legacy entry exists");

        // 2. Enabled when current CYD-DECK entry exists matching command
        var expectedCmd = service.GetCommand();
        AssertEqual($"\"{currentExe}\" --mock --minimized", expectedCmd, "GetCommand() uses target CYD-DECK.exe path with flags");
        fakeRegistry.SetValue("CYD-DECK", expectedCmd);
        AssertTrue(service.IsEnabled(), "Service is enabled when CYD-DECK key matches GetCommand()");

        // 3. Enabled when legacy StreamDeckWindowsAgent entry points to old exe in the SAME directory
        fakeRegistry.Values.Clear();
        fakeRegistry.SetValue("StreamDeckWindowsAgent", $"\"{legacyExeInSameDir}\" --minimized");
        AssertTrue(service.IsEnabled(), "Service is enabled when legacy entry points to StreamDeck.WindowsAgent.exe in same directory");

        // 4. Enabled when legacy StreamDeckWindowsAgent entry points to new exe in the SAME directory (case-insensitive / slash normalized)
        fakeRegistry.Values.Clear();
        fakeRegistry.SetValue("StreamDeckWindowsAgent", "\"c:\\users\\test\\appdata\\local\\programs\\cyd-deck\\sub\\..\\CYD-DECK.exe\" --minimized");
        AssertTrue(service.IsEnabled(), "Service is enabled when legacy entry points to CYD-DECK.exe in same normalized directory");

        // 5. Inna instalacja / inny katalog o tej samej nazwie EXE -> IsEnabled() == false oraz MigrateLegacyStartupIfPresent() == false (bez zmian!)
        fakeRegistry.Values.Clear();
        var foreignLegacyCmd = $"\"{otherInstallLegacyExe}\" --minimized";
        fakeRegistry.SetValue("StreamDeckWindowsAgent", foreignLegacyCmd);
        AssertTrue(!service.IsEnabled(), "Service is disabled when legacy entry points to StreamDeck.WindowsAgent.exe in a DIFFERENT directory");
        AssertTrue(!service.MigrateLegacyStartupIfPresent(), "MigrateLegacyStartupIfPresent returns false when legacy entry points to a different directory");
        AssertEqual((string?)null, fakeRegistry.GetValue("CYD-DECK"), "CYD-DECK is not created when legacy entry belongs to a different directory");
        AssertEqual(foreignLegacyCmd, fakeRegistry.GetValue("StreamDeckWindowsAgent"), "Foreign StreamDeckWindowsAgent entry is left untouched");

        fakeRegistry.Values.Clear();
        var foreignNewExeLegacyCmd = $"\"{otherInstallNewExe}\" --minimized";
        fakeRegistry.SetValue("StreamDeckWindowsAgent", foreignNewExeLegacyCmd);
        AssertTrue(!service.IsEnabled(), "Service is disabled when legacy entry points to CYD-DECK.exe in a DIFFERENT directory");
        AssertTrue(!service.MigrateLegacyStartupIfPresent(), "MigrateLegacyStartupIfPresent returns false when legacy CYD-DECK.exe entry points to a different directory");

        // 6. Disabled when legacy entry points to unrelated application in same directory
        fakeRegistry.Values.Clear();
        fakeRegistry.SetValue("StreamDeckWindowsAgent", $"\"{installDir}\\OtherApp.exe\" --minimized");
        AssertTrue(!service.IsEnabled(), "Service is disabled when legacy entry points to unrelated executable name");
        AssertTrue(!service.MigrateLegacyStartupIfPresent(), "MigrateLegacyStartupIfPresent returns false for unrelated executable name");

        // 7. Legacy w tym samym katalogu + niestandardowe argumenty (--usb-port COM5 --minimized) -> zachowane argumenty po nowej ścieżce CYD-DECK.exe
        fakeRegistry.Values.Clear();
        fakeRegistry.SetValue("StreamDeckWindowsAgent", $"\"{legacyExeInSameDir}\" --usb-port COM5 --minimized");
        var migratedCustomArgs = service.MigrateLegacyStartupIfPresent();
        AssertTrue(migratedCustomArgs, "MigrateLegacyStartupIfPresent returns true for same-directory legacy entry with custom CLI arguments");
        AssertEqual($"\"{currentExe}\" --usb-port COM5 --minimized", fakeRegistry.GetValue("CYD-DECK"), "Migrated CYD-DECK entry preserves '--usb-port COM5 --minimized' after new CYD-DECK.exe path");
        AssertEqual((string?)null, fakeRegistry.GetValue("StreamDeckWindowsAgent"), "Legacy StreamDeckWindowsAgent entry is removed after migration");
        AssertTrue(service.IsEnabled(), "Service is enabled after migrating custom-args entry in same directory");

        // 8. Legacy w tym samym katalogu bez cudzysłowów z argumentami -> poprawnie rozpoznany i zmigrowany z zachowaniem argumentów
        fakeRegistry.Values.Clear();
        fakeRegistry.SetValue("StreamDeckWindowsAgent", $"{legacyExeInSameDir} --usb-port COM7 --mock --minimized");
        AssertTrue(service.MigrateLegacyStartupIfPresent(), "MigrateLegacyStartupIfPresent handles unquoted legacy path in same directory");
        AssertEqual($"\"{currentExe}\" --usb-port COM7 --mock --minimized", fakeRegistry.GetValue("CYD-DECK"), "Unquoted legacy entry migrates to quoted CYD-DECK.exe and preserves arguments");

        // 9. Legacy w tym samym katalogu bez argumentów -> zachowane puste argumenty
        fakeRegistry.Values.Clear();
        fakeRegistry.SetValue("StreamDeckWindowsAgent", $"\"{legacyExeInSameDir}\"");
        AssertTrue(service.MigrateLegacyStartupIfPresent(), "MigrateLegacyStartupIfPresent handles legacy entry without arguments");
        AssertEqual($"\"{currentExe}\"", fakeRegistry.GetValue("CYD-DECK"), "Legacy entry without arguments migrates to quoted CYD-DECK.exe without extra arguments");

        // 10. Nowy wpis CYD-DECK już istnieje -> NIE zostaje nadpisany przez MigrateLegacyStartupIfPresent
        fakeRegistry.Values.Clear();
        var existingCydDeckEntry = $"\"{currentExe}\" --usb-port COM9 --minimized";
        fakeRegistry.SetValue("CYD-DECK", existingCydDeckEntry);
        fakeRegistry.SetValue("StreamDeckWindowsAgent", $"\"{legacyExeInSameDir}\" --minimized");
        var migratedWhenCurrentExists = service.MigrateLegacyStartupIfPresent();
        AssertTrue(!migratedWhenCurrentExists, "MigrateLegacyStartupIfPresent returns false when CYD-DECK entry already exists");
        AssertEqual(existingCydDeckEntry, fakeRegistry.GetValue("CYD-DECK"), "Existing CYD-DECK entry is not overwritten by MigrateLegacyStartupIfPresent");

        // 11. SetEnabled(true) creates CYD-DECK entry and deletes legacy StreamDeckWindowsAgent
        fakeRegistry.Values.Clear();
        fakeRegistry.SetValue("StreamDeckWindowsAgent", $"\"{legacyExeInSameDir}\" --minimized");
        service.SetEnabled(true);
        AssertEqual(expectedCmd, fakeRegistry.GetValue("CYD-DECK"), "SetEnabled(true) sets CYD-DECK entry with GetCommand()");
        AssertEqual((string?)null, fakeRegistry.GetValue("StreamDeckWindowsAgent"), "SetEnabled(true) removes legacy StreamDeckWindowsAgent entry");

        // 12. SetEnabled(false) deletes both CYD-DECK and legacy entries
        fakeRegistry.SetValue("CYD-DECK", expectedCmd);
        fakeRegistry.SetValue("StreamDeckWindowsAgent", "leftover");
        service.SetEnabled(false);
        AssertEqual((string?)null, fakeRegistry.GetValue("CYD-DECK"), "SetEnabled(false) deletes CYD-DECK entry");
        AssertEqual((string?)null, fakeRegistry.GetValue("StreamDeckWindowsAgent"), "SetEnabled(false) deletes legacy StreamDeckWindowsAgent entry");

        // 13. Non-mock command
        var normalOptions = new AgentOptions(MockMode: false);
        var normalService = new WindowsStartupService(normalOptions, fakeRegistry, currentExe);
        AssertTrue(!normalService.GetCommand().Contains("--mock"), "Normal command does not contain --mock flag");
        AssertTrue(normalService.GetCommand().Contains("--minimized"), "Normal command contains --minimized flag");
    }

    private static void TestOfficialIconAssetsAndAppIconFactory()
    {
        Console.WriteLine("\n-- Testing Official Icon Assets (SVG, PNG, Multi-Size ICO) & AppIconFactory --");

        var repoRoot = ResolveRepoRoot();
        var assetsDir = Path.Combine(repoRoot, "agent", "assets");

        var svgPath = Path.Combine(assetsDir, "cyd-deck.svg");
        var pngPath = Path.Combine(assetsDir, "cyd-deck.png");
        var icoPath = Path.Combine(assetsDir, "cyd-deck.ico");

        AssertTrue(File.Exists(svgPath), $"cyd-deck.svg exists at {svgPath}");
        AssertTrue(File.Exists(pngPath), $"cyd-deck.png exists at {pngPath}");
        AssertTrue(File.Exists(icoPath), $"cyd-deck.ico exists at {icoPath}");

        var svgContent = File.ReadAllText(svgPath);
        AssertTrue(svgContent.Contains("#161616", StringComparison.OrdinalIgnoreCase), "cyd-deck.svg contains background color #161616");
        AssertTrue(svgContent.Contains("#dd3127", StringComparison.OrdinalIgnoreCase), "cyd-deck.svg contains primary tile color #dd3127");
        AssertTrue(svgContent.Contains("#4eab41", StringComparison.OrdinalIgnoreCase), "cyd-deck.svg contains accent tile color #4eab41");

        // Regression test: SVG contains new geometry (outer radius 40, background 50..250, tiles x/y 84 and 156)
        AssertTrue(svgContent.Contains("M90 50 L210 50", StringComparison.OrdinalIgnoreCase), "cyd-deck.svg outer body uses radius 40 (M90 50 L210 50)");
        AssertTrue(svgContent.Contains("M96 84 L132 84", StringComparison.OrdinalIgnoreCase), "cyd-deck.svg contains top-left tile at (84, 84)");
        AssertTrue(svgContent.Contains("M168 84 L204 84", StringComparison.OrdinalIgnoreCase), "cyd-deck.svg contains top-right tile at (156, 84)");
        AssertTrue(svgContent.Contains("M96 156 L132 156", StringComparison.OrdinalIgnoreCase), "cyd-deck.svg contains bottom-left tile at (84, 156)");
        AssertTrue(svgContent.Contains("M168 156 L204 156", StringComparison.OrdinalIgnoreCase), "cyd-deck.svg contains bottom-right tile at (156, 156)");

        // Obsolete geometries must NOT be present
        AssertTrue(!svgContent.Contains("M75 50", StringComparison.OrdinalIgnoreCase), "cyd-deck.svg does NOT contain obsolete radius 25 (M75 50)");
        AssertTrue(!svgContent.Contains("M99 87", StringComparison.OrdinalIgnoreCase), "cyd-deck.svg does NOT contain obsolete tile offset 87");
        AssertTrue(!svgContent.Contains("M165 87", StringComparison.OrdinalIgnoreCase), "cyd-deck.svg does NOT contain obsolete tile offset 153");

        var pngBytes = File.ReadAllBytes(pngPath);
        AssertTrue(pngBytes.Length > 100, "cyd-deck.png is non-empty");
        AssertTrue(HasPngSignature(pngBytes, 0), "cyd-deck.png has valid PNG signature");

        using (var pngBitmap = new Bitmap(pngPath))
        {
            AssertEqual(300, pngBitmap.Width, "cyd-deck.png width is 300");
            AssertEqual(300, pngBitmap.Height, "cyd-deck.png height is 300");

            // Corners outside rounded rect must be transparent
            AssertEqual(0, pngBitmap.GetPixel(0, 0).A, "cyd-deck.png corner (0,0) is transparent");
            AssertEqual(0, pngBitmap.GetPixel(50, 50).A, "cyd-deck.png corner (50,50) is transparent (radius 40)");
            AssertEqual(0, pngBitmap.GetPixel(250, 50).A, "cyd-deck.png corner (250,50) is transparent (radius 40)");

            // Background points
            var expectedBg = Color.FromArgb(255, 0x16, 0x16, 0x16);
            var expectedRed = Color.FromArgb(255, 0xDD, 0x31, 0x27);
            var expectedGreen = Color.FromArgb(255, 0x4E, 0xAB, 0x41);

            AssertColorMatches(expectedBg, pngBitmap.GetPixel(90, 50), "cyd-deck.png body top arc boundary (90,50) is #161616");
            AssertColorMatches(expectedBg, pngBitmap.GetPixel(150, 150), "cyd-deck.png central tile gap (150,150) is #161616");

            // Tile interiors
            AssertColorMatches(expectedRed, pngBitmap.GetPixel(100, 100), "cyd-deck.png top-left tile (100,100) is #dd3127");
            AssertColorMatches(expectedRed, pngBitmap.GetPixel(180, 100), "cyd-deck.png top-right tile (180,100) is #dd3127");
            AssertColorMatches(expectedRed, pngBitmap.GetPixel(100, 180), "cyd-deck.png bottom-left tile (100,180) is #dd3127");
            AssertColorMatches(expectedGreen, pngBitmap.GetPixel(180, 180), "cyd-deck.png bottom-right tile (180,180) is #4eab41");

            // Geometry regression: x=85 is inside new tile 1 (84..144), was background in old (87..147)
            AssertColorMatches(expectedRed, pngBitmap.GetPixel(85, 114), "cyd-deck.png pixel (85,114) is inside new tile 1 #dd3127");
            // Geometry regression: x=145 is in new gap (144..156), was inside tile in old (87..147)
            AssertColorMatches(expectedBg, pngBitmap.GetPixel(145, 114), "cyd-deck.png pixel (145,114) is inside new 12px gap #161616");
            // Geometry regression: x=157 is inside new tile 2 (156..216)
            AssertColorMatches(expectedRed, pngBitmap.GetPixel(157, 114), "cyd-deck.png pixel (157,114) is inside new tile 2 #dd3127");
            // Geometry regression: x=217 is outside new tile 2 (156..216)
            AssertColorMatches(expectedBg, pngBitmap.GetPixel(217, 114), "cyd-deck.png pixel (217,114) is outside new tile 2 #161616");
        }

        var icoBytes = File.ReadAllBytes(icoPath);
        AssertTrue(icoBytes.Length > 200, "cyd-deck.ico has valid size");

        var icoReserved = BitConverter.ToUInt16(icoBytes, 0);
        var icoType = BitConverter.ToUInt16(icoBytes, 2);
        var icoCount = BitConverter.ToUInt16(icoBytes, 4);

        AssertEqual((ushort)0, icoReserved, "ICO header reserved field is 0");
        AssertEqual((ushort)1, icoType, "ICO header type field is 1 (ICO)");
        AssertTrue(icoCount > 1, $"ICO contains multiple icon sizes (count={icoCount})");
        AssertEqual((ushort)7, icoCount, "ICO contains all 7 standard sizes (16, 24, 32, 48, 64, 128, 256)");

        var expectedSizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var foundSizes = new List<int>();

        for (var i = 0; i < icoCount; i++)
        {
            var entryOffset = 6 + (i * 16);
            var widthByte = icoBytes[entryOffset];
            var heightByte = icoBytes[entryOffset + 1];
            var width = widthByte == 0 ? 256 : widthByte;
            var height = heightByte == 0 ? 256 : heightByte;
            var bytesInRes = BitConverter.ToUInt32(icoBytes, entryOffset + 8);
            var imageOffset = BitConverter.ToUInt32(icoBytes, entryOffset + 12);

            AssertEqual(width, height, $"ICO frame #{i} is square ({width}x{height})");
            AssertTrue(bytesInRes > 0, $"ICO frame {width}x{height} has non-zero payload ({bytesInRes} bytes)");
            AssertTrue(imageOffset + bytesInRes <= icoBytes.Length, $"ICO frame {width}x{height} payload fits within file");
            AssertTrue(HasPngSignature(icoBytes, (int)imageOffset), $"ICO frame {width}x{height} has valid embedded PNG header");

            using var frameStream = new MemoryStream(icoBytes, (int)imageOffset, (int)bytesInRes);
            using var frameBitmap = new Bitmap(frameStream);
            AssertEqual(width, frameBitmap.Width, $"Decoded ICO PNG frame width is {width}");
            AssertEqual(height, frameBitmap.Height, $"Decoded ICO PNG frame height is {height}");

            foundSizes.Add(width);
        }

        AssertEqual(string.Join(",", expectedSizes), string.Join(",", foundSizes), "ICO frames match expected sizes 16,24,32,48,64,128,256");

        // Verify AppIconFactory.CreateIcon() and CreateBitmap() colors & geometry
        using var icon = AppIconFactory.CreateIcon();
        AssertEqual(32, icon.Width, "AppIconFactory.CreateIcon() width is 32");
        AssertEqual(32, icon.Height, "AppIconFactory.CreateIcon() height is 32");

        using var bitmap = AppIconFactory.CreateBitmap(32);
        var expectedBgColor = Color.FromArgb(255, 0x16, 0x16, 0x16);
        var expectedRedColor = Color.FromArgb(255, 0xDD, 0x31, 0x27);
        var expectedGreenColor = Color.FromArgb(255, 0x4E, 0xAB, 0x41);

        AssertColorMatches(expectedBgColor, bitmap.GetPixel(3, 16), "AppIconFactory background pixel (3,16) is #161616");
        AssertColorMatches(expectedRedColor, bitmap.GetPixel(11, 11), "AppIconFactory top-left tile pixel (11,11) is #dd3127");
        AssertColorMatches(expectedRedColor, bitmap.GetPixel(21, 11), "AppIconFactory top-right tile pixel (21,11) is #dd3127");
        AssertColorMatches(expectedRedColor, bitmap.GetPixel(11, 21), "AppIconFactory bottom-left tile pixel (11,21) is #dd3127");
        AssertColorMatches(expectedGreenColor, bitmap.GetPixel(21, 21), "AppIconFactory bottom-right tile pixel (21,21) is #4eab41");
        AssertColorMatches(expectedBgColor, bitmap.GetPixel(16, 16), "AppIconFactory central tile gap (16,16) is #161616");

        using var iconBitmap = icon.ToBitmap();
        AssertColorMatches(expectedBgColor, iconBitmap.GetPixel(3, 16), "Icon.ToBitmap() background pixel (3,16) is #161616");
        AssertColorMatches(expectedRedColor, iconBitmap.GetPixel(11, 11), "Icon.ToBitmap() top-left tile pixel (11,11) is #dd3127");
        AssertColorMatches(expectedRedColor, iconBitmap.GetPixel(21, 11), "Icon.ToBitmap() top-right tile pixel (21,11) is #dd3127");
        AssertColorMatches(expectedRedColor, iconBitmap.GetPixel(11, 21), "Icon.ToBitmap() bottom-left tile pixel (11,21) is #dd3127");
        AssertColorMatches(expectedGreenColor, iconBitmap.GetPixel(21, 21), "Icon.ToBitmap() bottom-right tile pixel (21,21) is #4eab41");
        AssertColorMatches(expectedBgColor, iconBitmap.GetPixel(16, 16), "Icon.ToBitmap() central tile gap (16,16) is #161616");

        // Verify AppIconFactory.CreateCanvasBitmap(300)
        using var canvasBitmap = AppIconFactory.CreateCanvasBitmap(300);
        AssertEqual(300, canvasBitmap.Width, "AppIconFactory.CreateCanvasBitmap width is 300");
        AssertEqual(300, canvasBitmap.Height, "AppIconFactory.CreateCanvasBitmap height is 300");
        AssertEqual(0, canvasBitmap.GetPixel(0, 0).A, "AppIconFactory canvas (0,0) is transparent");
        AssertEqual(0, canvasBitmap.GetPixel(50, 50).A, "AppIconFactory canvas (50,50) is transparent");
        AssertColorMatches(expectedBgColor, canvasBitmap.GetPixel(90, 50), "AppIconFactory canvas (90,50) is #161616");
        AssertColorMatches(expectedBgColor, canvasBitmap.GetPixel(150, 150), "AppIconFactory canvas center gap (150,150) is #161616");
        AssertColorMatches(expectedRedColor, canvasBitmap.GetPixel(85, 114), "AppIconFactory canvas tile 1 (85,114) is #dd3127");
        AssertColorMatches(expectedBgColor, canvasBitmap.GetPixel(145, 114), "AppIconFactory canvas gap (145,114) is #161616");
        AssertColorMatches(expectedRedColor, canvasBitmap.GetPixel(157, 114), "AppIconFactory canvas tile 2 (157,114) is #dd3127");
        AssertColorMatches(expectedBgColor, canvasBitmap.GetPixel(217, 114), "AppIconFactory canvas outside tile 2 (217,114) is #161616");
        AssertColorMatches(expectedGreenColor, canvasBitmap.GetPixel(180, 180), "AppIconFactory canvas tile 4 (180,180) is #4eab41");
    }

    private static bool HasPngSignature(byte[] data, int offset)
    {
        if (offset + 8 > data.Length)
        {
            return false;
        }

        return data[offset] == 0x89 &&
               data[offset + 1] == 0x50 &&
               data[offset + 2] == 0x4E &&
               data[offset + 3] == 0x47 &&
               data[offset + 4] == 0x0D &&
               data[offset + 5] == 0x0A &&
               data[offset + 6] == 0x1A &&
               data[offset + 7] == 0x0A;
    }

    private static void AssertColorMatches(Color expected, Color actual, string message)
    {
        testsRun++;
        if (expected.R != actual.R || expected.G != actual.G || expected.B != actual.B || actual.A != 255)
        {
            throw new InvalidOperationException(
                $"FAILED: {message}. Expected RGBA=({expected.R},{expected.G},{expected.B},255), Actual RGBA=({actual.R},{actual.G},{actual.B},{actual.A})");
        }
        testsPassed++;
        Console.WriteLine($"  [PASS] {message}");
    }

    private static string ResolveRepoRoot()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../"));
        if (!Directory.Exists(Path.Combine(repoRoot, "scripts")))
        {
            repoRoot = Path.GetFullPath(".");
        }
        return repoRoot;
    }

    private static void TestInstallerScriptConsistency()
    {
        Console.WriteLine("\n-- Testing Inno Setup Script Consistency --");
        var issPath = Path.Combine(ResolveRepoRoot(), "installer", "windows-agent.iss");
        AssertTrue(File.Exists(issPath), $"windows-agent.iss exists at {issPath}");

        var content = File.ReadAllText(issPath);

        AssertTrue(content.Contains("AppName=CYD-DECK"), "Installer AppName is CYD-DECK");
        AssertTrue(content.Contains("UninstallDisplayIcon={app}\\CYD-DECK.exe"), "UninstallDisplayIcon points to CYD-DECK.exe");
        AssertTrue(content.Contains("AppId={{C7AA87F8-56E2-4EF2-86A9-8C1C42494445}"), "Installer AppId GUID is preserved");
        AssertTrue(content.Contains("Name: \"{group}\\CYD-DECK\"; Filename: \"{app}\\CYD-DECK.exe\"; AppUserModelID: \"Blankeuuu.CYD-DECK\""), "Start menu shortcut sets AppUserModelID Blankeuuu.CYD-DECK");
        AssertTrue(content.Contains("Name: \"{autodesktop}\\CYD-DECK\"; Filename: \"{app}\\CYD-DECK.exe\"; Tasks: desktopicon; AppUserModelID: \"Blankeuuu.CYD-DECK\""), "Desktop shortcut sets AppUserModelID Blankeuuu.CYD-DECK");
        AssertTrue(content.Contains("Filename: \"{app}\\CYD-DECK.exe\"; Description: \"{cm:LaunchProgram,CYD-DECK}\""), "Postinstall run points to CYD-DECK.exe");
        AssertTrue(content.Contains("Type: files; Name: \"{app}\\StreamDeck.WindowsAgent.exe\""), "InstallDelete cleans up old StreamDeck.WindowsAgent.exe");
        AssertTrue(content.Contains("Type: files; Name: \"{app}\\StreamDeck.WindowsAgent.deps.json\""), "InstallDelete cleans up old deps.json");
        AssertTrue(content.Contains("Exec('taskkill.exe', '/F /IM CYD-DECK.exe'"), "taskkill targets CYD-DECK.exe");
        AssertTrue(content.Contains("Exec('taskkill.exe', '/F /IM StreamDeck.WindowsAgent.exe'"), "taskkill also targets legacy StreamDeck.WindowsAgent.exe");
        AssertTrue(content.Contains("TryExtractCommandArgsForExe"), "Installer validates exact {app} executable prefix and extracts CLI arguments");
        AssertTrue(content.Contains("ExistingCurrentCommand"), "Installer checks if CYD-DECK registry key already exists before writing");
        AssertTrue(content.Contains("ArgsSuffix"), "Installer preserves CLI argument suffix during autostart migration");
    }

    private static void TestBuildAndPublishScriptsConsistency()
    {
        Console.WriteLine("\n-- Testing Build, Publish and Launch Scripts Consistency --");

        var repoRoot = ResolveRepoRoot();

        var publishScriptPath = Path.Combine(repoRoot, "scripts/publish-windows-agent.ps1");
        AssertTrue(File.Exists(publishScriptPath), $"publish-windows-agent.ps1 exists at {publishScriptPath}");
        var publishContent = File.ReadAllText(publishScriptPath);
        AssertTrue(publishContent.Contains("Join-Path $publishRoot \"CYD-DECK\""), "Publish script sets publishDirectory to CYD-DECK");
        AssertTrue(publishContent.Contains("CYD-DECK-$RuntimeIdentifier.zip"), "Publish script sets zipPath to CYD-DECK-$RuntimeIdentifier.zip");
        AssertTrue(publishContent.Contains("Join-Path $publishDirectory \"CYD-DECK.exe\""), "Publish script validates CYD-DECK.exe");
        AssertTrue(publishContent.Contains("Join-Path $publishDirectory \"CYD-DECK.deps.json\""), "Publish script validates CYD-DECK.deps.json");
        AssertTrue(publishContent.Contains("Run CYD-DECK.exe from that directory"), "Publish deployment text directs running CYD-DECK.exe");

        var installerScriptPath = Path.Combine(repoRoot, "scripts/build-windows-installer.ps1");
        AssertTrue(File.Exists(installerScriptPath), $"build-windows-installer.ps1 exists at {installerScriptPath}");
        var installerContent = File.ReadAllText(installerScriptPath);
        AssertTrue(installerContent.Contains("publish/CYD-DECK"), "build-windows-installer.ps1 points to publish/CYD-DECK");
        AssertTrue(installerContent.Contains("CYD-DECK.exe"), "build-windows-installer.ps1 checks for CYD-DECK.exe");

        var launchScriptPath = Path.Combine(repoRoot, "scripts/launch-agent.ps1");
        AssertTrue(File.Exists(launchScriptPath), $"launch-agent.ps1 exists at {launchScriptPath}");
        var launchContent = File.ReadAllText(launchScriptPath);
        AssertTrue(launchContent.Contains("CYD-DECK.exe"), "launch-agent.ps1 prefers CYD-DECK.exe");
        AssertTrue(launchContent.Contains("StreamDeck.WindowsAgent.exe"), "launch-agent.ps1 falls back to legacy exe if not found");
    }

    private static void TestAssemblyAndPeMetadata()
    {
        Console.WriteLine("\n-- Testing Assembly & PE Metadata --");

        var agentAssembly = typeof(AppIdentity).Assembly;
        AssertEqual("CYD-DECK", agentAssembly.GetName().Name, "Assembly name is CYD-DECK");

        var titleAttr = agentAssembly.GetCustomAttribute<AssemblyTitleAttribute>();
        AssertEqual("CYD-DECK", titleAttr?.Title, "AssemblyTitleAttribute is CYD-DECK");

        var productAttr = agentAssembly.GetCustomAttribute<AssemblyProductAttribute>();
        AssertEqual("CYD-DECK", productAttr?.Product, "AssemblyProductAttribute is CYD-DECK");

        var descAttr = agentAssembly.GetCustomAttribute<AssemblyDescriptionAttribute>();
        AssertEqual("CYD-DECK Windows Agent", descAttr?.Description, "AssemblyDescriptionAttribute is CYD-DECK Windows Agent");

        var companyAttr = agentAssembly.GetCustomAttribute<AssemblyCompanyAttribute>();
        AssertEqual("Blankeuuu", companyAttr?.Company, "AssemblyCompanyAttribute is Blankeuuu");
    }
}
