using System.Text.RegularExpressions;

namespace Asteria.Core.World;

/// <summary>
/// Immutable validated world-creation request. Rules become mutable inside
/// DimensionSessionStateStore, not on this creation draft.
/// </summary>
public sealed class WorldCreationOptions
{
    public const string DefaultName = "New World";
    public const int MaximumNameLength = 200;

    private static readonly Regex CopyPrefix = new(
        @"^copy(?:\\s+(\\d+))?\\s+of\\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Creates a concise name for a recreated world, incrementing an existing
    /// copy prefix instead of nesting another "Copy of" prefix.
    /// </summary>
    public static string CreateCopyName(string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        var baseName = sourceName.Trim();
        var copyCount = 0;
        Match match;
        while ((match = CopyPrefix.Match(baseName)).Success)
        {
            copyCount = match.Groups[1].Success
                ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                : copyCount + 1;
            baseName = baseName[match.Length..].TrimStart();
        }

        var prefix = copyCount == 0 ? "Copy of " : $"Copy {copyCount + 1} of ";
        return prefix + baseName;
    }

    public WorldCreationOptions(
        string name,
        ulong seed,
        PlayerGameMode gameMode = PlayerGameMode.Survival,
        uint ticksPerSecond = WorldGameRules.DefaultTicksPerSecond,
        bool spawnCreatures = true,
        WorldGenerationOptions? generation = null,
        bool keepInventory = true,
        CreatureAttackMode creatureAttackMode = CreatureAttackMode.Default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "World name must not be empty.",
                nameof(name));
        }

        var normalized = name.Trim();
        if (normalized.Length > MaximumNameLength ||
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

        if (!Enum.IsDefined(creatureAttackMode))
            throw new ArgumentOutOfRangeException(nameof(creatureAttackMode));

        if (ticksPerSecond == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
        }

        Name = normalized;
        Seed = seed;
        GameMode = gameMode;
        TicksPerSecond = ticksPerSecond;
        SpawnCreatures = spawnCreatures;
        KeepInventory = keepInventory;
        AttackMode = creatureAttackMode;
        Generation = generation ?? new WorldGenerationOptions();
    }

    public string Name { get; }
    public ulong Seed { get; }
    public PlayerGameMode GameMode { get; }
    public uint TicksPerSecond { get; }
    public bool SpawnCreatures { get; }
    public bool KeepInventory { get; }
    public CreatureAttackMode AttackMode { get; }
    public WorldGenerationOptions Generation { get; }
}
