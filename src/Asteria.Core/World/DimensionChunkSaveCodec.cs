namespace Asteria.Core.World;

/// <summary>Complete spatial snapshot of one initialized Sphere.</summary>
public sealed class SavedSphereChunks
{
    public SavedSphereChunks(DimensionId dimension, VoxelWorldSaveSnapshot chunks)
    {
        if (string.IsNullOrWhiteSpace(dimension.Value))
            throw new ArgumentException("A saved Sphere needs a valid dimension ID.",
                nameof(dimension));
        Dimension = dimension;
        Chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    }

    public DimensionId Dimension { get; }
    public VoxelWorldSaveSnapshot Chunks { get; }
}

/// <summary>
/// The complete list of initialized Sphere chunk snapshots, tied to the
/// immutable world-generation identity. Does not save player/entities yet.
/// </summary>
public sealed class DimensionChunkSaveSnapshot
{
    private readonly IReadOnlyList<SavedSphereChunks> _spheres;

    public DimensionChunkSaveSnapshot(
        ulong worldSeed,
        WorldGenerationOptions generation,
        IEnumerable<SavedSphereChunks> spheres)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(spheres);
        var ordered = spheres.Take(257).ToArray();
        if (ordered.Length > 256 || ordered.Any(sphere => sphere is null))
            throw new InvalidDataException("Invalid number of saved Spheres.");

        Array.Sort(ordered, (a, b) => StringComparer.Ordinal.Compare(
            a.Dimension.Value, b.Dimension.Value));
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1].Dimension == ordered[index].Dimension)
                throw new InvalidDataException(
                    $"Duplicate saved Sphere: {ordered[index].Dimension}");
        }

        WorldSeed = worldSeed;
        Generation = generation;
        _spheres = Array.AsReadOnly(ordered);
    }

    public ulong WorldSeed { get; }
    public WorldGenerationOptions Generation { get; }
    public IReadOnlyList<SavedSphereChunks> Spheres => _spheres;
}

/// <summary>
/// Captures the existing dimension states, never touching uncreated Spheres.
/// Decode happens before a new DimensionSessionStateStore is returned, so an
/// invalid chunk or dimension cannot partially replace an active game.
/// </summary>
public static class DimensionChunkSaveCodec
{
    public static DimensionChunkSaveSnapshot Capture(
        DimensionSessionStateStore store,
        BlockRegistry blocks,
        FluidRegistry fluids,
        DyeRegistry dyes,
        AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(store);

        var spheres = store.CreatedDimensions().Select(state =>
            new SavedSphereChunks(
                state.Dimension.Id,
                VoxelWorldSaveCodec.Capture(
                    state.World, blocks, fluids, dyes, layers)));
        return new DimensionChunkSaveSnapshot(
            store.WorldSeed, store.Generation, spheres);
    }

    public static DimensionSessionStateStore Restore(
        WorldCreationOptions creation,
        DimensionRegistry dimensions,
        DimensionChunkSaveSnapshot snapshot,
        BlockRegistry blocks,
        FluidRegistry fluids,
        DyeRegistry dyes,
        AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(creation);
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (creation.Seed != snapshot.WorldSeed ||
            creation.Generation != snapshot.Generation)
            throw new InvalidDataException(
                "Saved Sphere chunks do not match the world's seed and generation settings.");
        if (snapshot.Spheres.Count > dimensions.Count)
            throw new InvalidDataException(
                "Save contains more Spheres than are defined by the selected pack.");

        // Restore into isolated worlds first. Nothing is published until
        // every authored Sphere ID and chunk payload has been validated.
        var prepared = snapshot.Spheres.Select(saved =>
        {
            _ = dimensions.Get(saved.Dimension);
            return (
                saved.Dimension,
                World: VoxelWorldSaveCodec.Restore(
                    saved.Chunks, blocks, fluids, dyes, layers));
        }).ToArray();

        var restored = new DimensionSessionStateStore(creation, dimensions);
        foreach (var (dimension, world) in prepared)
            restored.AddRestoredDimension(dimension, world);
        return restored;
    }
}
