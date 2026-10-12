using System.Numerics;

namespace Asteria.Core.Content;

/// <summary>MineClone player hand art framing shared by in-world, portrait
/// and camera-local renderers. Values are local to the GLB right-arm pivot;
/// no world or inventory state is stored here.</summary>
public readonly record struct HeldObjectLayout(
    float Scale, Vector3 CenterTranslation, bool FaceCamera);

public static class PlayerHeldVisualLayout
{
    public const float ThirdPersonBlockSize = .22f;
    public const float FirstPersonBlockSize = .18f;
    public const float SpriteSize = .52f;
    public const float ToolDisplayAngle = .30f;
    public static readonly Vector3 ThirdPersonHandPosition = new(0f, -.72f, -.06f);
    public static readonly Vector3 ThirdPersonHandEulerXyz = new(-.35f, .65f, .18f);

    public static Vector3 FirstPersonHandPosition(HeldVisualKind? kind) => kind switch
    {
        HeldVisualKind.Cube => new(-.02f, .715f, .20f),
        HeldVisualKind.GrippedSprite => new(-.08f, .52f, .21f),
        _ => new(.04f, .76f, .21f),
    };

    /// <summary>
    /// Frame an authored Object from its actual 3D target bounds, not from its
    /// icon or an arbitrary per-object heuristic. Matches MineClone's maximum
    /// extent scaling and centralizes the pose for all three player views.
    /// </summary>
    public static HeldObjectLayout ObjectPose(ObjectDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var size = definition.Target.Size;
        var extent = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        var scale = SpriteSize / extent;
        return new HeldObjectLayout(
            scale,
            -definition.Target.CenterOffset * scale,
            definition.Visual.Kind == ObjectVisualKind.ExtrudedSprite);
    }

    /// <summary>Sprite icons stay upright. Tools use MineClone's authored
    /// 0.30-radian rotation; applying a 0.30π rotation to every sprite is wrong.</summary>
    public static float SpriteFaceAngle(HeldVisualKind kind) =>
        kind == HeldVisualKind.GrippedSprite ? ToolDisplayAngle : 0f;
}
