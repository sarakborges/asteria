using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldCreationSeedTests
{
    [Theory]
    [InlineData("0", 0UL)]
    [InlineData("42", 42UL)]
    [InlineData("18446744073709551615", ulong.MaxValue)]
    [InlineData(" 00042 ", 42UL)]
    public void ValidDecimalSeedRoundTripsExactly(
        string input,
        ulong expected)
    {
        Assert.True(
            WorldCreationSeed.TryParse(
                input,
                out var actual));
        Assert.Equal(
            expected,
            actual);
        Assert.True(
            WorldCreationSeed.TryParse(
                WorldCreationSeed.Format(
                    actual),
                out var roundTrip));
        Assert.Equal(
            expected,
            roundTrip);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.2")]
    [InlineData("0x10")]
    [InlineData("18446744073709551616")]
    [InlineData("other")]
    public void InvalidInputCannotCreateWorld(
        string input)
    {
        Assert.False(
            WorldCreationSeed.TryParse(
                input,
                out _));
    }

    [Fact]
    public void RandomSeedCanBeSerializedWithoutPrecisionLoss()
    {
        var generated =
            WorldCreationSeed.GenerateRandom();

        Assert.True(
            WorldCreationSeed.TryParse(
                WorldCreationSeed.Format(
                    generated),
                out var restored));
        Assert.Equal(
            generated,
            restored);
    }
}
