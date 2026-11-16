using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerChatCommandProcessorTests
{
    [Fact]
    public void CommandCatalogMatchesMineCloneRebuildExactly()
    {
        Assert.Equal(
            new[] { "/spawn", "/place", "/locate", "/warp", "/kill", "/modify" },
            PlayerChatCommandProcessor.SupportedCommands);
    }

    [Theory]
    [InlineData("hello", ChatCommandKind.Say, "hello")]
    [InlineData("/spawn asteria:slime", ChatCommandKind.Spawn, "asteria:slime")]
    [InlineData("/place structure asteria:ruin 2", ChatCommandKind.Place, "asteria:ruin")]
    [InlineData("/locate biome asteria:plains", ChatCommandKind.LocateBiome, "asteria:plains")]
    [InlineData("/locate structure asteria:ruin 1", ChatCommandKind.LocateStructure, "asteria:ruin")]
    [InlineData("/kill", ChatCommandKind.Kill, null)]
    [InlineData("/modify add NO_AI", ChatCommandKind.Modify, "NO_AI")]
    [InlineData("/whatever", ChatCommandKind.Unknown, "/whatever")]
    [InlineData("/help", ChatCommandKind.Unknown, "/help")]
    [InlineData("/position", ChatCommandKind.Unknown, "/position")]
    [InlineData("/time", ChatCommandKind.Unknown, "/time")]
    public void ParsesAuthoredGrammar(string input, ChatCommandKind kind, string? argument)
    {
        var parsed = PlayerChatCommandProcessor.Parse(input);
        Assert.Equal(kind, parsed.Kind);
        Assert.Equal(argument, parsed.Argument);
    }

    [Fact]
    public void WarpUsesXZYAndOptionalDimension()
    {
        var parsed = PlayerChatCommandProcessor.Parse(
            "/warp 10 -20 30 asteria:umbral");
        Assert.Equal(ChatCommandKind.Warp, parsed.Kind);
        Assert.Equal(10, parsed.X);
        Assert.Equal(-20, parsed.Z);
        Assert.Equal(30, parsed.Y);
        Assert.Equal("asteria:umbral", parsed.Option);
    }

    [Theory]
    [InlineData("/spawn")]
    [InlineData("/spawn one two three")]
    [InlineData("/place structure id 0")]
    [InlineData("/place something")]
    [InlineData("/locate structure")]
    [InlineData("/locate biome id 2")]
    [InlineData("/warp 10 20")]
    [InlineData("/warp x 20 30")]
    [InlineData("/kill other")]
    [InlineData("/modify remove NO_AI ignored")]
    [InlineData("/modify inspect NO_AI")]
    public void InvalidArgumentsYieldUsage(string line)
    {
        var parsed = PlayerChatCommandProcessor.Parse(line);
        Assert.Equal(ChatCommandKind.Usage, parsed.Kind);
        Assert.StartsWith("/", parsed.Argument);
    }

    [Theory]
    [InlineData("/modify edit NO_AI frozen", "edit", "frozen")]
    [InlineData("/modify remove NO_AI", "remove", null)]
    [InlineData("/modify add NO_AI", "add", null)]
    public void ModifyRetainsActionAndOptionalValue(
        string line, string action, string? value)
    {
        var parsed = PlayerChatCommandProcessor.Parse(line);
        Assert.Equal(ChatCommandKind.Modify, parsed.Kind);
        Assert.Equal(action, parsed.Option);
        Assert.Equal(value, parsed.Value);
    }
}
