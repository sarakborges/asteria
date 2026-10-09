using System.Collections.ObjectModel;
using System.Text.Json;

namespace Asteria.Core.Content;

/// <summary>
/// Selected-pack player presentation only. Native model/animation playback
/// derives from the authoritative player's movement, never owns gameplay facts.
/// </summary>
public sealed class PlayerVisualDefinition
{
    private static readonly string[] RequiredClips = ["idle", "walk", "run", "jump", "fall"];

    private PlayerVisualDefinition(
        string id, string model, string skin,
        IReadOnlyDictionary<string, string> animations)
    {
        Id = id;
        Model = model;
        Skin = skin;
        Animations = animations;
    }

    public string Id { get; }
    public string Model { get; }
    public string Skin { get; }
    public IReadOnlyDictionary<string, string> Animations { get; }

    public static PlayerVisualDefinition Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = PackContentFields.Id(root);
        var model = PackContentFields.ResourcePath(
            PackContentFields.RequiredString(root, "model"), "model");
        var skin = PackContentFields.ResourcePath(
            PackContentFields.RequiredString(root, "skin"), "skin");
        if (!model.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
            !skin.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Player model must be GLB and skin must be PNG.");

        var mappings = PackContentFields.Strings(root, "animations");
        if (mappings.Count is < 5 or > 32 ||
            RequiredClips.Any(clip => !mappings.ContainsKey(clip)) ||
            mappings.Any(pair => pair.Key.Length > 64 || pair.Value.Length > 64))
            throw new FormatException("Player animation states are missing or invalid.");

        return new PlayerVisualDefinition(
            id, model, skin,
            new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(mappings, StringComparer.Ordinal)));
    }
}
