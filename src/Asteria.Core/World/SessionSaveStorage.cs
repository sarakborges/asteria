namespace Asteria.Core.World;

/// <summary>
/// Entire Core gameplay session, including all Spheres, is published as one
/// checksummed generation. No in-game load UI or catalog compatibility until
/// native session retirement and active Sphere publication are integrated.
/// </summary>
public static class SessionSaveStorage
{
    private const string Prefix = "session-";
    public const int RetainedGenerations =
        AtomicSaveGenerationStore.RetainedGenerations;

    public static ulong Publish(
        string directory, GameplaySessionSnapshot snapshot,
        BlockRegistry blocks, FluidRegistry fluids,
        DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ActiveSphere is null)
            throw new InvalidDataException(
                "A durable gameplay session requires its active Sphere.");
        return AtomicSaveGenerationStore.Publish(
            directory, Prefix, GameplaySessionFileCodec.MaximumBytes,
            stream => GameplaySessionFileCodec.Write(
                stream, snapshot, blocks, fluids, dyes, layers));
    }

    public static GameplaySessionSnapshot ReadLatest(
        string directory, BlockRegistry blocks, FluidRegistry fluids,
        DyeRegistry dyes, AttachedLayerRegistry layers) =>
        AtomicSaveGenerationStore.ReadLatest(
            directory, Prefix, GameplaySessionFileCodec.MaximumBytes,
            (stream, length) => GameplaySessionFileCodec.Read(
                stream, length, blocks, fluids, dyes, layers));

    /// <summary>
    /// Candidate selection also verifies full world restoration. A damaged
    /// latest generation is never selected just because its checksum passes.
    /// This does not activate a Godot session or make the world loadable in UI.
    /// </summary>
    public static (GameplaySessionSnapshot Snapshot, DimensionSessionStateStore States)
        RestoreLatest(
            string directory, DimensionRegistry dimensions,
            BlockRegistry blocks, FluidRegistry fluids,
            DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        return AtomicSaveGenerationStore.ReadLatest(
            directory, Prefix, GameplaySessionFileCodec.MaximumBytes,
            (stream, length) =>
            {
                var saved = GameplaySessionFileCodec.Read(
                    stream, length, blocks, fluids, dyes, layers);
                if (saved.ActiveSphere is null)
                    throw new InvalidDataException("Session has no active Sphere.");

                try
                {
                    var creation = new WorldCreationOptions(
                        saved.Name, saved.Spatial.WorldSeed,
                        saved.Player.GameMode, saved.TicksPerSecond,
                        saved.SpawnCreatures, saved.Spatial.Generation);
                    var restored = GameplaySessionSaveCodec.Restore(
                        creation, dimensions, saved, blocks, fluids, dyes, layers);
                    return (saved, restored);
                }
                catch (Exception error) when (error is ArgumentException or
                    KeyNotFoundException or InvalidOperationException)
                {
                    throw new InvalidDataException(
                        "Saved session cannot be restored using the active pack.", error);
                }
            });
    }

    public static string GenerationFileName(ulong generation) =>
        AtomicSaveGenerationStore.GenerationFileName(Prefix, generation);
}
