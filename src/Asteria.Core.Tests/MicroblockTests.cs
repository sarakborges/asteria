using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class MicroblockTests
{
    [Fact]
    public void EightCubedMaskSupportsThreeEditingResolutions()
    {
        var thick = MicroblockMask.Full.Edit(0, 0, 0, MicroblockResolution.Thick, occupied: false);
        var thin = MicroblockMask.Full.Edit(0, 0, 0, MicroblockResolution.Thin, occupied: false);
        var extraThin = MicroblockMask.Full.Edit(0, 0, 0, MicroblockResolution.ExtraThin, occupied: false);

        Assert.Equal(448, thick.OccupiedCount);
        Assert.Equal(504, thin.OccupiedCount);
        Assert.Equal(511, extraThin.OccupiedCount);
        Assert.False(thick.Contains(0, 0, 0));
        Assert.True(thick.Contains(4, 0, 0));
    }

    [Fact]
    public void PartialGeometryScalesMacroVoxelLightDampening()
    {
        var half = MicroblockMask.Empty;

        for (var z = 0; z < 4; z++)
        {
            for (var y = 0; y < MicroblockMask.Edge; y++)
            {
                for (var x = 0; x < MicroblockMask.Edge; x++)
                {
                    half = half.Edit(x, y, z, MicroblockResolution.ExtraThin, occupied: true);
                }
            }
        }

        Assert.Equal(256, half.OccupiedCount);
        Assert.Equal((byte)8, half.LightDampening(15));
    }

    [Fact]
    public void ChunkInternsRepeatedMicroblockMasks()
    {
        var chunk = new Chunk();
        var stone = new BlockRuntimeId(1);
        var mask = MicroblockMask.Full.Edit(0, 0, 0, MicroblockResolution.Thick, occupied: false);

        chunk.SetBlock(0, 0, 0, stone);
        chunk.SetBlock(1, 0, 0, stone);
        chunk.SetMicroblockMask(0, 0, 0, mask);
        chunk.SetMicroblockMask(1, 0, 0, mask);

        Assert.Equal(1, chunk.MicroblockMaskCount);
        Assert.Equal(mask, chunk.GetMicroblockMask(0, 0, 0));
        Assert.Equal(mask, chunk.GetMicroblockMask(1, 0, 0));
        Assert.True(chunk.GetCell(0, 0, 0).HasMicroblockGeometry);
    }
}
