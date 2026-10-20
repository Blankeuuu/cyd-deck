using System.Diagnostics;

namespace StreamDeck.WindowsAgent.Updates;

public sealed class UpdateInstallerLauncher
{
    private readonly Func<ProcessStartInfo, Process?> processLauncher;
    private readonly Action applicationExiter;

    public UpdateInstallerLauncher(
        Func<ProcessStartInfo, Process?>? processLauncher = null,
        Action? applicationExiter = null)
    {
        this.processLauncher = processLauncher ?? (psi => Process.Start(psi));
        this.applicationExiter = applicationExiter ?? (() => Application.Exit());
    }

    public static string BuildInstallerArguments(bool silent, bool restart = true)
    {
        if (silent)
        {
            return "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART";
        }

        return "/NORESTART";
    }

    public bool TryLaunchInstaller(
        string installerPath,
        out string? errorMessage,
        bool silent = false,
        bool exitCurrentProcess = true)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(installerPath))
        {
            errorMessage = "Ścieżka do instalatora jest pusta.";
            return false;
        }

        if (!File.Exists(installerPath))
        {
            errorMessage = $"Plik instalatora nie istnieje: {installerPath}";
            return false;
        }

        var fileName = Path.GetFileName(installerPath);
        if (!UpdateSecurityValidator.IsAgentInstallerAsset(fileName))
        {
            errorMessage = "Plik nie jest zaufanym instalatorem CYD-DECK (nie znajduje się na liście dozwolonych).";
            return false;
        }

        try
        {
            using (var stream = File.OpenRead(installerPath))
            {
                var header = new byte[2];
                var read = stream.Read(header, 0, 2);
                if (!UpdateSecurityValidator.ValidateInstallerMagicAndSize(header[..read], stream.Length, out var safeError))
                {
                    errorMessage = safeError;
                    return false;
                }
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                Arguments = BuildInstallerArguments(silent)
            };

            var process = processLauncher(startInfo);
            if (process is null)
            {
                errorMessage = "Nie udało się uruchomić procesu instalatora.";
                return false;
            }

            if (exitCurrentProcess)
            {
                applicationExiter();
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Błąd podczas uruchamiania instalatora: {ex.Message}";
            return false;
        }
    }
}
