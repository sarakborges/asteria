namespace Asteria.Core.World;

/// <summary>
/// Immutable validated world-creation request. Rules become mutable inside
/// DimensionSessionStateStore, not on this creation draft.
/// </summary>
public sealed class WorldCreationOptions
{
    public const string DefaultName = "New World";

    public WorldCreationOptions(
        string name,
        ulong seed,
        PlayerGameMode gameMode = PlayerGameMode.Survival,
        uint ticksPerSecond = WorldGameRules.DefaultTicksPerSecond,
        bool spawnCreatures = true)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "World name must not be empty.",
                nameof(name));
        }

        var normalized = name.Trim();
        if (normalized.Length > 200 ||
            normalized is "." or ".." ||
            normalized.EndsWith(".", StringComparison.Ordinal) ||
            normalized.IndexOfAny(
                ['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0 ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                "World name is not a valid single directory component.",
                nameof(name));
        }

        var stem = normalized.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 &&
             (stem.StartsWith("COM", StringComparison.Ordinal) ||
              stem.StartsWith("LPT", StringComparison.Ordinal)) &&
             (stem[3] is (>= '1' and <= '9') or '¹' or '²' or '³')))
        {
            throw new ArgumentException(
                "World name is reserved.",
                nameof(name));
        }

        if (!Enum.IsDefined(gameMode))
        {
            throw new ArgumentOutOfRangeException(nameof(gameMode));
        }

        if (ticksPerSecond == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
        }

        Name = normalized;
        Seed = seed;
        GameMode = gameMode;
        TicksPerSecond = ticksPerSecond;
        SpawnCreatures = spawnCreatures;
    }

    public string Name { get; }
    public ulong Seed { get; }
    public PlayerGameMode GameMode { get; }
    public uint TicksPerSecond { get; }
    public bool SpawnCreatures { get; }
}
