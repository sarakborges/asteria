namespace Asteria.Core.World;

/// <summary>Whether creatures can pursue and attack the player.</summary>
public enum CreatureAttackMode : byte
{
    Default = 0,
    Peaceful = 1,
}

/// <summary>
/// World-scoped mutable rules, shared across all Sphere sessions.
/// </summary>
public sealed class WorldGameRules
{
    public const uint DefaultTicksPerSecond = 40;

    public WorldGameRules(
        uint ticksPerSecond = DefaultTicksPerSecond,
        bool spawnCreatures = true,
        bool keepInventory = true,
        CreatureAttackMode creatureAttackMode = CreatureAttackMode.Default)
    {
        SetTicksPerSecond(ticksPerSecond);
        SpawnCreatures = spawnCreatures;
        KeepInventory = keepInventory;
        SetCreatureAttackMode(creatureAttackMode);
    }

    public uint TicksPerSecond { get; private set; }

    public bool SpawnCreatures { get; private set; }

    public bool KeepInventory { get; private set; }

    public CreatureAttackMode AttackMode { get; private set; }

    public bool AllowsCreatureAttacks => AttackMode == CreatureAttackMode.Default;

    public bool SetCreatureAttackMode(CreatureAttackMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (AttackMode == mode) return false;
        AttackMode = mode;
        return true;
    }

    public bool SetKeepInventory(bool value)
    {
        if (KeepInventory == value) return false;
        KeepInventory = value;
        return true;
    }

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
