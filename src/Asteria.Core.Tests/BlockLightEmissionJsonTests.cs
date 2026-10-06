using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockLightEmissionJsonTests
{
    [Fact]
    public void ScalarEmissionRemainsWhite()
    {
        var block = BlockDefinitionJson.Parse("""
            {
              "id": "asteria:white_light",
              "lightEmission": 12
            }
            """);

        Assert.Equal(new BlockLightEmission(12, 12, 12), block.LightEmission);
    }

    [Fact]
    public void RgbEmissionPreservesAuthoredColor()
    {
        var block = BlockDefinitionJson.Parse("""
            {
              "id": "asteria:colored_light",
              "lightEmission": {
                "red": 15,
                "green": 4,
                "blue": 9
              }
            }
            """);

        Assert.Equal(new BlockLightEmission(15, 4, 9), block.LightEmission);
    }
}
