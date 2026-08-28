using DKay.GameServerDock.Application.Abstractions;

namespace DKay.GameServerDock.Infrastructure.Games;

public sealed class ExistingCs2InstallationValidator : IExistingCs2InstallationValidator
{
    public string Validate(string installDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        var normalizedInput = installDirectory.Trim().Trim('"');
        if (!Path.IsPathRooted(normalizedInput))
        {
            throw new InvalidOperationException(
                "Bitte gib den absoluten Pfad des vorhandenen CS2-Serverordners auf dem Host an.");
        }

        var directory = Path.GetFullPath(normalizedInput)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Der Serverordner wurde nicht gefunden: '{directory}'.");
        }

        var executable = OperatingSystem.IsWindows()
            ? Path.Combine(directory, "game", "bin", "win64", "cs2.exe")
            : Path.Combine(directory, "game", "bin", "linuxsteamrt64", "cs2");
        if (!File.Exists(executable))
        {
            throw new InvalidOperationException(
                $"Der Ordner ist keine vollständige CS2-Serverinstallation. Erwartete Datei: '{executable}'.");
        }

        return directory;
    }
}
