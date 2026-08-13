namespace DKay.GameServerDock.Infrastructure.Processes;

public static class ConsoleOutputPolicy
{
    private const string Cs2MissingConsoleInputBuffer =
        "CTextConsoleWin::GetLine: !GetNumberOfConsoleInputEvents";

    public static bool ShouldRecord(string line)
    {
        if (line.Trim().Equals(Cs2MissingConsoleInputBuffer, StringComparison.Ordinal))
        {
            return false;
        }

        // Source2 probes CounterStrikeSharp's managed assemblies as if they were native libraries
        // after CounterStrikeSharp has already loaded successfully. One probe per framework DLL
        // produces hundreds of identical access-violation lines without describing a server fault.
        // Preserve native Metamod failures and all other loader output.
        return !(line.StartsWith("Could not PreloadLibrary ", StringComparison.Ordinal) &&
                 line.Contains("addons\\counterstrikesharp\\", StringComparison.OrdinalIgnoreCase) &&
                 line.Contains(".dll - Access violation", StringComparison.OrdinalIgnoreCase));
    }
}
