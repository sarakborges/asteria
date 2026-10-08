using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerChatSessionTests
{
    [Fact]
    public void ChatRequiresOpenAndClosesOnSubmit()
    {
        var chat = new PlayerChatSession();
        Assert.False(chat.TrySubmit("hello", out _));
        Assert.True(chat.Open());
        Assert.False(chat.Open());
        Assert.True(chat.TrySubmit("  hello  ", out var message));
        Assert.Equal("hello", message);
        Assert.False(chat.IsOpen);
        Assert.False(chat.TrySubmit("again", out _));
        Assert.False(chat.Close());
    }

    [Fact]
    public void EmptyAndOverlongLinesAreRejectedAndCloseChat()
    {
        var chat = new PlayerChatSession();
        chat.Open();
        Assert.False(chat.TrySubmit("  ", out _));
        Assert.False(chat.IsOpen);
        chat.Open();
        Assert.False(chat.TrySubmit(new string('x', PlayerChatSession.MaxInputCharacters + 1), out _));
        Assert.False(chat.IsOpen);
    }

    [Fact]
    public void Last64MessagesStayInInsertionOrderWithStableIdentifiers()
    {
        var chat = new PlayerChatSession();
        for (var i = 0; i < 70; ++i)
            chat.Append($"message-{i}", i == 69);

        Assert.Equal(64, chat.History.Count);
        Assert.Equal(7UL, chat.History[0].Id);
        Assert.Equal("message-6", chat.History[0].Text);
        Assert.Equal(70UL, chat.History[^1].Id);
        Assert.True(chat.History[^1].IsError);
    }

    [Fact]
    public void ResetClosesChatAndDropsPreviousHistory()
    {
        var chat = new PlayerChatSession();
        chat.Open();
        chat.Append("message");
        chat.Reset();
        Assert.Empty(chat.History);
        Assert.False(chat.IsOpen);
        Assert.True(chat.Open());
        chat.Append("new");
        Assert.Single(chat.History);
        Assert.Equal(1UL, chat.History[0].Id);
    }
}
