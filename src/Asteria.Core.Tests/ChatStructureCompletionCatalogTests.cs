using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChatStructureCompletionCatalogTests
{
    [Fact]
    public void PlaceIncludesAllReferencesButLocateOnlyOffersGeneratedLocatableRoots()
    {
        var structures = new StructureRegistry(
        [
            Structure("asteria:ready"),
            Structure("asteria:locked", locatable: false),
            Structure("asteria:unplaced"),
            Structure("asteria:group_02", groupId: "group"),
            Structure("asteria:group_01", groupId: "group"),
            Structure("asteria:hidden_02", groupId: "hidden"),
            Structure("asteria:hidden_01", groupId: "hidden", locatable: false),
        ]);
        var sets = new StructureSetRegistry(
        [
            Set("asteria:active_set"),
            Set("asteria:locked_set", locatable: false),
        ]);

        var generated = new HashSet<string>(StringComparer.Ordinal)
        {
            "asteria:ready",
            "asteria:group",
            "asteria:hidden",
            "asteria:active_set",
            "asteria:locked_set",
        };
        var catalog = ChatStructureCompletionCatalog.Create(
            structures, sets, generated.Contains);

        Assert.Equal(
        [
            "asteria:active_set", "asteria:group", "asteria:hidden",
            "asteria:locked", "asteria:locked_set", "asteria:ready",
            "asteria:unplaced",
        ], catalog.Structures);
        Assert.Equal(
        [
            "asteria:active_set", "asteria:group", "asteria:ready",
        ], catalog.LocatableStructures);
        Assert.All(catalog.LocatableStructures, id =>
            Assert.Contains(id, catalog.Structures));
    }

    [Fact]
    public void NoActiveWorldDoesNotPublishMisleadingLocateResults()
    {
        var structures = new StructureRegistry([Structure("asteria:ready")]);
        var catalog = ChatStructureCompletionCatalog.Create(
            structures, StructureSetRegistry.Empty, null);

        Assert.Equal(["asteria:ready"], catalog.Structures);
        Assert.Empty(catalog.LocatableStructures);
    }

    [Fact]
    public void CandidateLimitsAndSortingAreDeterministic()
    {
        var definitions = Enumerable.Range(0, 520)
            .Reverse()
            .Select(i => Structure($"asteria:root_{i:D4}"));
        var structures = new StructureRegistry(definitions);
        var catalog = ChatStructureCompletionCatalog.Create(
            structures, StructureSetRegistry.Empty, _ => true);

        Assert.Equal(ChatStructureCompletionCatalog.MaximumCandidates,
            catalog.Structures.Count);
        Assert.Equal(catalog.Structures, catalog.LocatableStructures);
        Assert.Equal("asteria:root_0000", catalog.Structures[0]);
        Assert.Equal("asteria:root_0511", catalog.Structures[^1]);
    }

    private static StructureDefinition Structure(
        string id, string? groupId = null, bool locatable = true) =>
        new(id, rotation: false, anchor: default,
            voxels:
            [
                new StructureVoxelDefinition(
                    0, 0, 0, "asteria:stone", BlockOrientation.Y),
            ],
            groupId: groupId, locatable: locatable);

    private static StructureSetDefinition Set(string id, bool locatable = true) =>
        new(id,
        [
            new StructureSetElementDefinition("root", "asteria:ready"),
        ], locatable: locatable);
}
