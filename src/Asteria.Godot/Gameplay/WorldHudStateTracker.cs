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
    private (int X, int Y, int Z)? _biomePosition;
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
        Func<int, int, int, string> biomeAt,
        out WorldHudState state)
    {
        ArgumentNullException.ThrowIfNull(
            player);
        ArgumentNullException.ThrowIfNull(
            biomeAt);

        var position =
            player.GlobalPosition;
        var heading = PlayerCompassHeading.QuantizedDegrees(
            player.Rotation.Y, player.IsThirdPersonFront);

        var x =
            Mathf.FloorToInt(
                position.X);
        var y =
            Math.Max(
                0,
                Mathf.FloorToInt(
                    position.Y));
        var z =
            Mathf.FloorToInt(
                position.Z);

        if (_biomePosition !=
            (x, y, z))
        {
            _biomeId =
                biomeAt(
                    x,
                    y,
                    z);
            _biomePosition =
                (x, y, z);
        }

        state =
            new WorldHudState(
                dimension.Value,
                _biomeId,
                x,
                y,
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

}
