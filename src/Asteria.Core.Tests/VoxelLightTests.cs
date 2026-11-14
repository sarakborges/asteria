using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VoxelLightTests
{
    [Fact]
    public void PacksSkyAndRgbBlockChannelsAtFourBitsEach()
    {
        var light = new VoxelLight(15, 12, 7, 3);

        Assert.Equal((byte)15, light.Sky);
        Assert.Equal((byte)12, light.Red);
        Assert.Equal((byte)7, light.Green);
        Assert.Equal((byte)3, light.Blue);
        Assert.Equal((byte)12, light.BlockPeak);
    }

    [Fact]
    public void AttenuationIsPerChannelAndSaturating()
    {
        var light = VoxelLight.Attenuate(
            new VoxelLight(15, 8, 2, 0),
            3);

        Assert.Equal((byte)12, light.Sky);
        Assert.Equal((byte)5, light.Red);
        Assert.Equal((byte)0, light.Green);
        Assert.Equal((byte)0, light.Blue);
    }
}
