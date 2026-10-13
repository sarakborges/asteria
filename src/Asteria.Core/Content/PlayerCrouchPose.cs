namespace Asteria.Core.Content;

/// <summary>
/// Canonical cosmetic crouch offsets for the named joints of the MineClone
/// player GLB. Keeps limb proportions intact instead of scaling the model.
/// This never changes the authoritative player capsule or camera.
/// </summary>
public static class PlayerCrouchPose
{
    public static float VerticalDrop(string pivotName, float blend)
    {
        ArgumentNullException.ThrowIfNull(pivotName);
        if (!float.IsFinite(blend))
            throw new ArgumentOutOfRangeException(nameof(blend));

        var drop = pivotName switch
        {
            "BodyPivot" => 0.18f,
            "HeadPivot" => 0.28f,
            "RightArmPivot" or "LeftArmPivot" => 0.24f,
            "RightLegPivot" or "LeftLegPivot" => 0.02f,
            _ => 0f,
        };
        return drop * Math.Clamp(blend, 0f, 1f);
    }
}
