using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerChatCommandProcessorTests
{
    [Theory]
    [InlineData("/help", "chat.local.help", false)]
    [InlineData("/position", "chat.local.position", false)]
    [InlineData("/time", "chat.local.time", false)]
    [InlineData("/unknown", "chat.local.unknown", true)]
    [InlineData("/time other", "chat.local.unknown", true)]
    public void CommandsPublishLocalizedFeedback(string command, string expectedKey, bool error)
    {
        var chat = new PlayerChatSession();
        PlayerChatCommandProcessor.Execute(chat, command, "X: 1", "Day 1");
        var line = Assert.Single(chat.History);
        Assert.Equal(expectedKey, line.LocalizationKey);
        Assert.Equal(error, line.IsError);
    }

    [Fact]
    public void OrdinaryTextIsLocalOnlyAndNeverInterpretedAsCommand()
    {
        var chat = new PlayerChatSession();
        PlayerChatCommandProcessor.Execute(chat, "hi everyone", "", "");
        var line = Assert.Single(chat.History);
        Assert.Equal("Player: hi everyone", line.Text);
        Assert.Null(line.LocalizationKey);
    }
}
