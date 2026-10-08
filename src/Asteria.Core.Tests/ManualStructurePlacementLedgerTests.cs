using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ManualStructurePlacementLedgerTests
{
    private static ManualStructurePlacementFootprint At(
        int x, int y, int z, string reference = "asteria:root") =>
        new(reference, x, z, x, x + 2, y, y + 4, z, z + 2);

    [Fact]
    public void CommittedStructureBlocksAnotherOverlappingPlacement()
    {
        var ledger = new ManualStructurePlacementLedger();
        var initial = At(8, 64, 8);
        Assert.False(ledger.Overlaps(initial));
        ledger.RecordCommitted(initial);
        Assert.True(ledger.Overlaps(initial));
        Assert.True(ledger.Overlaps(At(10, 68, 10)));
        Assert.False(ledger.Overlaps(At(11, 64, 8)));
        Assert.False(ledger.Overlaps(At(8, 69, 8)));
        Assert.Single(ledger.Snapshot());
        Assert.Throws<InvalidOperationException>(() =>
            ledger.RecordCommitted(At(9, 64, 9)));
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void SpatialIndexSupportsNegativeWorldCoordinatesAndChunkEdges()
    {
        var ledger = new ManualStructurePlacementLedger();
        var left = At(-1, 35, -1);
        ledger.RecordCommitted(left);
        Assert.True(ledger.Overlaps(At(0, 35, 0)));
        Assert.False(ledger.Overlaps(At(3, 35, 0)));
        ledger.RecordCommitted(At(34, 35, -30));
        Assert.True(ledger.Overlaps(At(35, 37, -30)));
        Assert.Equal(2, ledger.Count);
    }

    [Fact]
    public void SnapshotIsDetachedFromTheMutableIndex()
    {
        var ledger = new ManualStructurePlacementLedger();
        ledger.RecordCommitted(At(1, 30, 1));
        var before = ledger.Snapshot();
        ledger.RecordCommitted(At(20, 30, 20));

        Assert.Single(before);
        Assert.Equal(2, ledger.Snapshot().Count);
    }

    [Fact]
    public void DifferentSpheresOwnIndependentPlacementHistories()
    {
        var dimensions = DimensionRegistry.FromJson(new[]
        {
            DimensionJson("asteria:alpha"),
            DimensionJson("asteria:beta"),
        });
        var sessions = new DimensionSessionStateStore(42UL, dimensions);
        var alpha = sessions.GetOrCreate(new DimensionId("asteria:alpha"));
        var beta = sessions.GetOrCreate(new DimensionId("asteria:beta"));
        var placed = At(8, 55, 8);
        alpha.ManualStructures.RecordCommitted(placed);

        Assert.True(alpha.ManualStructures.Overlaps(placed));
        Assert.False(beta.ManualStructures.Overlaps(placed));
        Assert.Same(alpha.ManualStructures,
            sessions.GetOrCreate(new DimensionId("asteria:alpha")).ManualStructures);
        Assert.Equal(1, alpha.ManualStructures.Count);
    }

    private static string DimensionJson(string id) =>
        $$"""
        {
          "id": "{{id}}",
          "surfaceBiomes": ["asteria:test"],
          "volumeBiomes": [],
          "undergroundBiomes": [],
          "seaLevel": 32,
          "gravity": 18,
          "spawn": {"x": 0, "z": 0},
          "environment": {
            "skyTop": "#000000",
            "skyBottom": "#ffffff",
            "ambient": 1,
            "fogColor": "#000000",
            "fogDensity": 0
          }
        }
        """;
}
