namespace Asteria.Core.World;

/// <summary>
/// Legacy spatial-only generations. They do not register with WorldSaveCatalog
/// and remain incompatible with playable session saves.
/// </summary>
public static class SpatialSaveStorage
{
    private const string Prefix = "sphere-chunks-";

    public const int RetainedGenerations =
        AtomicSaveGenerationStore.RetainedGenerations;

    public static ulong Publish(string directory, DimensionChunkSaveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return AtomicSaveGenerationStore.Publish(
            directory, Prefix, DimensionChunkFileCodec.MaximumPayloadBytes,
            target => DimensionChunkFileCodec.Write(target, snapshot));
    }

    public static DimensionChunkSaveSnapshot ReadLatest(string directory) =>
        AtomicSaveGenerationStore.ReadLatest(
            directory, Prefix, DimensionChunkFileCodec.MaximumPayloadBytes,
            (stream, length) => DimensionChunkFileCodec.Read(stream, length));

    public static string GenerationFileName(ulong generation) =>
        AtomicSaveGenerationStore.GenerationFileName(Prefix, generation);
}
