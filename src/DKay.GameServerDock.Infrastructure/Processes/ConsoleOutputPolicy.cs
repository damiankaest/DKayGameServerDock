namespace DKay.GameServerDock.Infrastructure.Processes;

public static class ConsoleOutputPolicy
{
    private const string Cs2MissingConsoleInputBuffer =
        "CTextConsoleWin::GetLine: !GetNumberOfConsoleInputEvents";

    public static bool ShouldRecord(string line) =>
        !line.Trim().Equals(Cs2MissingConsoleInputBuffer, StringComparison.Ordinal);
}
