using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Gameplay;

public readonly record struct WorldHudState(
    string Sphere,
    string Biome,
    int X,
    int Y,
    int Z,
    float HeadingDegrees);

public sealed class WorldHudStateTracker
{
    private WorldHudState? _last;
    private (int X, int Z)? _biomePosition;
    private string _biomeId = "";

    public void Reset()
    {
        _last = null;
        _biomePosition = null;
        _biomeId = "";
    }

    public bool TryCapture(
        FpsPlayer player,
        DimensionId dimension,
        BiomeField biomes,
        out WorldHudState state)
    {
        ArgumentNullException.ThrowIfNull(
            player);
        ArgumentNullException.ThrowIfNull(
            biomes);

        var position =
            player.GlobalPosition;
        var heading =
            QuantizeHeading(
                -Mathf.RadToDeg(
                    player.Rotation.Y));

        var x =
            Mathf.FloorToInt(
                position.X);
        var z =
            Mathf.FloorToInt(
                position.Z);

        if (_biomePosition !=
            (x, z))
        {
            _biomeId =
                biomes.Sample(
                    x,
                    z).Primary;
            _biomePosition =
                (x, z);
        }

        state =
            new WorldHudState(
                dimension.Value,
                _biomeId,
                x,
                Math.Max(
                    0,
                    Mathf.FloorToInt(
                        position.Y)),
                z,
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
