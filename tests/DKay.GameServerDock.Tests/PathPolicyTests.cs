using DKay.GameServerDock.Application.Services;

namespace DKay.GameServerDock.Tests;

public sealed class PathPolicyTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dkay-dock-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolves_files_below_the_server_root()
    {
        var policy = new PathPolicy(_root);
        var result = policy.ResolveChildPath(_root, Path.Combine("cfg", "server.cfg"));

        Assert.StartsWith(Path.GetFullPath(_root), result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("cfg/../../outside.txt")]
    public void Rejects_path_traversal(string path)
    {
        var policy = new PathPolicy(_root);

        Assert.Throws<InvalidOperationException>(() => policy.ResolveChildPath(_root, path));
    }

    [Fact]
    public void Generates_a_safe_server_directory()
    {
        var policy = new PathPolicy(_root);

        var result = policy.ResolveServerDirectory("Friends Survival!", Guid.Parse("d65f250f-cd61-4c49-8c84-0c3a565ef896"));

        Assert.Contains("friends-survival", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validates_a_managed_server_directory()
    {
        var policy = new PathPolicy(_root);
        var directory = Path.Combine(_root, "managed-server");

        Assert.Equal(Path.GetFullPath(directory), policy.ValidateServerDirectory(directory));
    }

    [Fact]
    public void Rejects_deleting_the_server_root_or_an_outside_directory()
    {
        var policy = new PathPolicy(_root);

        Assert.Throws<InvalidOperationException>(() => policy.ValidateServerDirectory(_root));
        Assert.Throws<InvalidOperationException>(() => policy.ValidateServerDirectory(Path.GetTempPath()));
    }
}
