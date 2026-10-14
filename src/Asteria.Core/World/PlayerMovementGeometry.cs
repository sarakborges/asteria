namespace Asteria.Core.World;

/// <summary>
/// Player collision/eye dimensions and discrete stair stepping from MineClone.
/// Godot owns collision sweeps; these are independent, authored geometry facts.
/// </summary>
public static class PlayerMovementGeometry
{
    public const float HalfWidth = 0.3f;
    public const float Width = HalfWidth * 2f;
    public const float StandingHeight = 1.8f;
    public const float CrouchHeight = 1.5f;
    public const float EyeHeight = 1.62f;

    public const float MaximumStepHeight = 0.5f;
    public const float StepIncrement = 0.125f;
    public const float StepSmoothingSpeed = 4f;
    public const float GroundProbe = 0.05f;
    public const float StepMinimumRise = 0.025f;
    public const int StepCandidateCount = 4;

    public static float StepRiseAt(int index)
    {
        if (index is < 0 or >= StepCandidateCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        return (index + 1) * StepIncrement;
    }
}
