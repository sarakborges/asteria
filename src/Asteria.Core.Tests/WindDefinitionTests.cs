using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WindDefinitionTests
{
    [Fact]
    public void NormalizesWindDirectionAndPreservesStrength()
    {
        var wind = new DimensionWindDefinition(3, 4, 0.45f);
        Assert.Equal(0.27f, wind.Velocity.X, 3);
        Assert.Equal(0.36f, wind.Velocity.Y, 3);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 0, -1)]
    [InlineData(float.PositiveInfinity, 1, 1)]
    public void RejectsInvalidWind(float x, float z, float strength) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DimensionWindDefinition(x, z, strength));

    [Fact]
    public void BlockWindSwayIsExplicitAuthoredData()
    {
        var block = BlockDefinitionJson.Parse(
            """{"id":"asteria:test_plant","windSway":true}""");
        Assert.True(block.WindSway);
        Assert.False(new BlockDefinition("asteria:test_stone").WindSway);
    }
}
