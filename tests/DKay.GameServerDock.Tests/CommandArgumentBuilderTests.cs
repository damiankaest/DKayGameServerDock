using DKay.GameServerDock.Application.Services;

namespace DKay.GameServerDock.Tests;

public sealed class CommandArgumentBuilderTests
{
    [Fact]
    public void Keeps_each_argument_separate_instead_of_building_a_shell_string()
    {
        var arguments = new CommandArgumentBuilder()
            .AddPair("+force_install_dir", @"C:\Game Servers\CS2")
            .AddPair("+app_update", "730")
            .Build();

        Assert.Equal(["+force_install_dir", @"C:\Game Servers\CS2", "+app_update", "730"], arguments);
    }

    [Theory]
    [InlineData("hello\nquit")]
    [InlineData("hello\rquit")]
    public void Rejects_control_characters(string input)
    {
        Assert.Throws<ArgumentException>(() => new CommandArgumentBuilder().Add(input));
    }
}

