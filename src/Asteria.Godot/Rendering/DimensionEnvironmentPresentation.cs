using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public sealed class DimensionEnvironmentPresentation
{
    private readonly Node _parent;
    private WorldEnvironment? _node;

    public DimensionEnvironmentPresentation(
        Node parent)
    {
        _parent =
            parent ??
            throw new ArgumentNullException(
                nameof(parent));
    }

    public void Apply(
        DimensionDefinition dimension)
    {
        ArgumentNullException.ThrowIfNull(
            dimension);

        var definition =
            dimension.Environment;
        var environment =
            new Environment
            {
                BackgroundMode =
                    Environment.BGMode.Color,
                BackgroundColor =
                    ToColor(
                        definition.BackgroundColor),
                AmbientLightSource =
                    Environment.AmbientSource.Color,
                AmbientLightColor =
                    ToColor(
                        definition.AmbientColor),
                AmbientLightEnergy =
                    definition.AmbientEnergy,
                FogEnabled =
                    definition.FogDensity >
                    0f,
                FogLightColor =
                    ToColor(
                        definition.FogColor),
                FogDensity =
                    definition.FogDensity,
            };

        if (_node is null)
        {
            _node =
                new WorldEnvironment
                {
                    Name =
                        "DimensionEnvironment",
                };
            _parent.AddChild(
                _node);
        }

        _node.Environment =
            environment;
    }

    private static Color ToColor(
        DimensionColor color) =>
        new(
            color.Red / 255f,
            color.Green / 255f,
            color.Blue / 255f,
            1f);
}
