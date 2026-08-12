using DKay.GameServerDock.Application.Abstractions;

namespace DKay.GameServerDock.Application.Services;

public sealed class PathPolicy(string serversRoot) : IPathPolicy
{
    private readonly string _serversRoot = Path.GetFullPath(serversRoot);

    public string ResolveServerDirectory(string serverName, Guid serverId)
    {
        var slug = new string(serverName.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');

        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "server";
        }

        return ResolveChildPath(_serversRoot, $"{slug}-{serverId:N}");
    }

    public string ResolveChildPath(string serverRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Absolute paths are not allowed.");
        }

        var canonicalRoot = Path.GetFullPath(serverRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(canonicalRoot, relativePath));

        if (!candidate.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The requested path is outside the server directory.");
        }

        return candidate;
    }
}

