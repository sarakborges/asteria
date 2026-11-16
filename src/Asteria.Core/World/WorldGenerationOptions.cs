namespace Asteria.Core.World;

/// <summary>Immutable world-generation settings pinned at world creation.</summary>
public enum WorldGenerationMode : byte
{
    Normal,
    Flat,
    Void,
}

public sealed record WorldGenerationOptions
{
    public const int MinimumBiomeSizeTenths = 5;
    public const int MaximumBiomeSizeTenths = 50;

    public WorldGenerationOptions(
        WorldGenerationMode mode = WorldGenerationMode.Normal,
        string? spawnBiome = null,
        int biomeSizeTenths = 10,
        bool spawnStructures = true,
        bool singleBiome = false,
        bool spawnCaves = true,
        bool spawnOceans = true)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (biomeSizeTenths is < MinimumBiomeSizeTenths or > MaximumBiomeSizeTenths)
            throw new ArgumentOutOfRangeException(nameof(biomeSizeTenths));
        if (spawnBiome is not null)
            BiomeDefinition.ValidateId(spawnBiome);
        if (singleBiome && spawnBiome is null)
            throw new ArgumentException(
                "Single Biome requires a selected Spawn Biome.", nameof(spawnBiome));

        Mode = mode;
        SpawnBiome = spawnBiome;
        BiomeSizeTenths = biomeSizeTenths;
        SpawnStructures = spawnStructures;
        SingleBiome = singleBiome;
        SpawnCaves = spawnCaves;
        SpawnOceans = spawnOceans;
    }

    public WorldGenerationMode Mode { get; }
    public string? SpawnBiome { get; }
    public int BiomeSizeTenths { get; }
    public float BiomeSizeMultiplier => BiomeSizeTenths / 10f;
    public bool SpawnStructures { get; }
    public bool SingleBiome { get; }
    public bool SpawnCaves { get; }
    public bool SpawnOceans { get; }

    /// <summary>Biome IDs are Sphere-scoped. Keep the world flags while
    /// dropping an origin-Sphere spawn choice on unrelated Spheres.</summary>
    public WorldGenerationOptions ForSphere(DimensionDefinition dimension)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        if (SpawnBiome is null ||
            dimension.SurfaceBiomes.Contains(SpawnBiome, StringComparer.Ordinal))
            return this;

        return new WorldGenerationOptions(
            Mode, spawnBiome: null, BiomeSizeTenths, SpawnStructures,
            singleBiome: false, SpawnCaves, SpawnOceans);
    }
}
