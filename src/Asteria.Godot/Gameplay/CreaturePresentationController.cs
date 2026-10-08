using Asteria.Client.Content;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Gameplay;

/// <summary>
/// Godot-only presentation for the authoritative dimension creature population.
/// Loads authored GLBs as raw files (no ResourceLoader or pack import sidecars).
/// Model templates are reused within this dimension session.
/// </summary>
public sealed class CreaturePresentationController
{
    private readonly Node3D _root;
    private readonly PackSelection _selection;
    private readonly PackContentRegistry<CreatureDefinition> _definitions;
    private readonly Dictionary<string, Node3D> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private readonly SortedDictionary<ulong, Node3D> _instances = [];

    public CreaturePresentationController(
        Node3D root,
        PackSelection selection,
        PackContentRegistry<CreatureDefinition> definitions)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _selection = selection;
        _definitions = definitions ??
            throw new ArgumentNullException(nameof(definitions));
    }

    public void Sync(IEnumerable<CreatureInstanceState> creatures)
    {
        var current = new HashSet<ulong>();
        foreach (var creature in creatures)
        {
            var id = creature.Id.Value;
            current.Add(id);
            if (!_instances.TryGetValue(id, out var model))
            {
                var definition = _definitions.Get(creature.DefinitionId);
                model = CreateModel(definition);
                model.Name = $"Creature_{id}";
                _root.AddChild(model);
                _instances.Add(id, model);
            }

            model.Position = new Vector3(
                creature.Position.X,
                creature.Position.Y,
                creature.Position.Z);
        }

        foreach (var entry in _instances.ToArray())
        {
            if (current.Contains(entry.Key)) continue;
            entry.Value.QueueFree();
            _instances.Remove(entry.Key);
        }
    }

    public void Retire()
    {
        foreach (var model in _models.Values) model.Free();
        _models.Clear();
        // Active scene nodes are owned by the dimension root, which is retired
        // by DimensionRuntimeSession.
        _instances.Clear();
        _textures.Clear();
    }

    private Node3D CreateModel(CreatureDefinition definition)
    {
        if (!_models.TryGetValue(definition.Model, out var template))
        {
            var path = ProjectPackFiles.AbsoluteResourcePath(
                _selection, definition.Model);
            using var gltf = new GltfDocument();
            using var state = new GltfState();
            var error = gltf.AppendFromFile(path, state);
            if (error != Error.Ok)
            {
                throw new InvalidDataException(
                    $"Could not decode creature model {definition.Model}: {error}");
            }

            template = gltf.GenerateScene(state) as Node3D ??
                throw new InvalidDataException(
                    $"Creature model {definition.Model} did not produce a Node3D root.");
            ApplyTextures(template, definition);
            _models.Add(definition.Model, template);
        }

        var model = template.Duplicate() as Node3D ??
            throw new InvalidOperationException(
                $"Could not instantiate creature model {definition.Model}.");

        PlayIdle(model, definition);
        return model;
    }

    private void ApplyTextures(Node node, CreatureDefinition definition)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh is not null)
        {
            for (var index = 0; index < mesh.Mesh.GetSurfaceCount(); index++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(index) is not BaseMaterial3D material)
                    continue;

                if (!definition.Textures.TryGetValue(material.ResourceName, out var relativePath))
                    continue;

                if (!_textures.TryGetValue(relativePath, out var texture))
                {
                    texture = ProjectPackFiles.LoadTexture(_selection, relativePath);
                    _textures.Add(relativePath, texture);
                }

                var copy = material.Duplicate() as BaseMaterial3D ??
                    throw new InvalidOperationException("Cannot duplicate creature material.");
                copy.AlbedoTexture = texture;
                if (definition.UnlitMaterials.Contains(material.ResourceName))
                    copy.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                mesh.SetSurfaceOverrideMaterial(index, copy);
            }
        }

        foreach (var child in node.GetChildren()) ApplyTextures(child, definition);
    }

    private static bool PlayIdle(Node node, CreatureDefinition definition)
    {
        if (!definition.Animations.TryGetValue("idle", out var animation))
            return false;

        if (node is AnimationPlayer player && player.HasAnimation(animation))
        {
            player.Play(animation);
            return true;
        }

        foreach (var child in node.GetChildren())
        {
            if (PlayIdle(child, definition)) return true;
        }

        return false;
    }
}
