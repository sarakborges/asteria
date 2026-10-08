namespace Asteria.Core.World;

public enum PlayerGameMode : byte
{
    Survival = 0,
    Creative = 1,
    Spectator = 2,
}

/// <summary>Policies only; gameplay consumers are integrated separately.</summary>
public static class PlayerGameModePolicy
{
    public static bool AllowsFlight(this PlayerGameMode mode) =>
        mode is PlayerGameMode.Creative or PlayerGameMode.Spectator;

    public static bool HasCreativeInventory(this PlayerGameMode mode) =>
        mode == PlayerGameMode.Creative;

    public static BlockBreakLootPolicy BreakLootPolicy(this PlayerGameMode mode) =>
        mode == PlayerGameMode.Survival
            ? BlockBreakLootPolicy.DropSelf
            : BlockBreakLootPolicy.Suppress;

    public static bool IsSpectator(this PlayerGameMode mode) =>
        mode == PlayerGameMode.Spectator;
}
