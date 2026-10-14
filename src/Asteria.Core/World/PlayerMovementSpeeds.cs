namespace Asteria.Core.World;

/// <summary>
/// The speed/jump constants shared with MineClone's movement/config.rs.
/// Keep authoritative numerical tuning in Core; Godot owns collision and input.
/// </summary>
public static class PlayerMovementSpeeds
{
    public const float Walk = 5f;
    public const float Jump = 7f;
    public const float FlightMultiplier = 5f;
    public const float WalkAcceleration = 28f;
    public const float WalkDeceleration = 36f;
    public const float FlightAcceleration = 75f;
    public const float FlightDeceleration = 90f;

    public const float Run = Walk * PlayerGroundMovement.RunSpeedMultiplier;
    public const float Flight = Walk * FlightMultiplier;
}
