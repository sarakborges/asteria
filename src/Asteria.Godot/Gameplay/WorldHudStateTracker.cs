using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Gameplay;

public readonly record struct WorldHudState(
    string Sphere,
    int X,
    int Y,
    int Z,
    float HeadingDegrees);

public sealed class WorldHudStateTracker
{
    private WorldHudState? _last;

    public void Reset()
    {
        _last = null;
    }

    public bool TryCapture(
        FpsPlayer player,
        DimensionId dimension,
        out WorldHudState state)
    {
        ArgumentNullException.ThrowIfNull(
            player);

        var position =
            player.GlobalPosition;
        var heading =
            QuantizeHeading(
                -Mathf.RadToDeg(
                    player.Rotation.Y));

        state =
            new WorldHudState(
                dimension.Value,
                Mathf.FloorToInt(
                    position.X),
                Math.Max(
                    0,
                    Mathf.FloorToInt(
                        position.Y)),
                Mathf.FloorToInt(
                    position.Z),
                heading);

        if (_last is
                { } previous &&
            previous ==
                state)
        {
            return false;
        }

        _last = state;
        return true;
    }

    private static float QuantizeHeading(
        float degrees)
    {
        var normalized =
            degrees %
            360f;

        if (normalized < 0f)
        {
            normalized +=
                360f;
        }

        return MathF.Round(
                   normalized *
                   2f) /
               2f;
    }
}
