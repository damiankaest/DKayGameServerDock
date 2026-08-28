using DKay.GameServerDock.Infrastructure.Games;

namespace DKay.GameServerDock.Tests;

public sealed class ExistingCs2InstallationValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dkay-existing-cs2-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Accepts_an_existing_cs2_installation_at_an_arbitrary_absolute_path()
    {
        var executable = OperatingSystem.IsWindows()
            ? Path.Combine(_root, "game", "bin", "win64", "cs2.exe")
            : Path.Combine(_root, "game", "bin", "linuxsteamrt64", "cs2");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, string.Empty);

        var result = new ExistingCs2InstallationValidator().Validate(_root);

        Assert.Equal(
            Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            result);
    }

    [Fact]
    public void Rejects_a_directory_without_the_platform_cs2_executable()
    {
        Directory.CreateDirectory(_root);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ExistingCs2InstallationValidator().Validate(_root));

        Assert.Contains("CS2", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_relative_paths()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ExistingCs2InstallationValidator().Validate("relative-cs2-server"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
