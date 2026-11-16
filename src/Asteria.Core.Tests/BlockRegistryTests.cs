using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockRegistryTests
{
    [Fact]
    public void RuntimeIdsAreCompactAndAirIsReserved()
    {
        var registry = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:dirt"),
        ]);

        Assert.Equal(BlockRuntimeId.Air, registry.GetId("asteria:air"));
        Assert.Equal((ushort)1, registry.GetId("asteria:stone").Value);
        Assert.Equal((ushort)2, registry.GetId("asteria:dirt").Value);
        Assert.False(registry.GetDefinition(BlockRuntimeId.Air).IsCollidable);
        Assert.Equal(2, registry.AuthoredCount);
    }

    [Fact]
    public void DuplicateNamespacedIdsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:stone"),
        ]));
    }
}
