namespace Asteria.Core.World;

/// <summary>
/// World-scoped mutable rules, shared across all Sphere sessions.
/// </summary>
public sealed class WorldGameRules
{
    public const uint DefaultTicksPerSecond = 40;

    public WorldGameRules(
        uint ticksPerSecond = DefaultTicksPerSecond,
        bool spawnCreatures = true)
    {
        SetTicksPerSecond(ticksPerSecond);
        SpawnCreatures = spawnCreatures;
    }

    public uint TicksPerSecond { get; private set; }

    public bool SpawnCreatures { get; private set; }

    public bool SetTicksPerSecond(uint value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Tick rate must be greater than zero.");
        }

        if (TicksPerSecond == value)
        {
            return false;
        }

        TicksPerSecond = value;
        return true;
    }

    public bool SetSpawnCreatures(bool value)
    {
        if (SpawnCreatures == value)
        {
            return false;
        }

        SpawnCreatures = value;
        return true;
    }
}
