namespace DKay.GameServerDock.Application.Services;

public sealed class CommandArgumentBuilder
{
    private readonly List<string> _arguments = [];

    public CommandArgumentBuilder Add(string argument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(argument);
        if (argument.Contains('\0') || argument.Contains('\r') || argument.Contains('\n'))
        {
            throw new ArgumentException("Command arguments cannot contain control characters.", nameof(argument));
        }

        _arguments.Add(argument);
        return this;
    }

    public CommandArgumentBuilder AddPair(string name, string value) => Add(name).Add(value);

    public IReadOnlyList<string> Build() => _arguments.ToArray();
}

