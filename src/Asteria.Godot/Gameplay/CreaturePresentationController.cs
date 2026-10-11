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
    private readonly SortedDictionary<ulong, AnimatableBody3D> _instances = [];
    private readonly Dictionary<ulong, (string Animation, uint Revision)> _animations = [];

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
            if (!_instances.TryGetValue(id, out var body))
            {
                var definition = _definitions.Get(creature.DefinitionId);
                body = CreateBody(definition, id);
                _root.AddChild(body);
                _instances.Add(id, body);
            }

            body.Position = new Vector3(
                creature.Position.X,
                creature.Position.Y,
                creature.Position.Z);
            // Keep physics AABBs axis-aligned while the model rotates.
            var model = body.GetNode<Node3D>("Visual");
            model.Rotation = new Vector3(
                0f, creature.Motion.FacingRadians, 0f);
            body.GetNode<CollisionShape3D>("Collider").Disabled = creature.IsDying;

            var visual = CreatureVisualState.From(creature);
            var animation = visual.Phase;
            var revision = visual.Revision;
            if (!_animations.TryGetValue(id, out var shown) ||
                shown != (animation, revision))
            {
                PlayAnimation(model,
                    _definitions.Get(creature.DefinitionId),
                    animation);
                _animations[id] = (animation, revision);
            }
        }

        foreach (var entry in _instances.ToArray())
        {
            if (current.Contains(entry.Key)) continue;
            entry.Value.QueueFree();
            _instances.Remove(entry.Key);
            _animations.Remove(entry.Key);
        }
    }

    public void Retire()
    {
        foreach (var model in _models.Values) model.Free();
        _models.Clear();
        // Active scene nodes are owned by the dimension root, which is retired
        // by DimensionRuntimeSession.
        _instances.Clear();
        _animations.Clear();
        _textures.Clear();
    }

    /// <summary>
    /// Creates a detached, textured GLB model for an offscreen entity portrait.
    /// Uses the same authored materials and idle animation as world creatures.
    /// </summary>
    public Node3D CreatePreviewModel(CreatureDefinition definition) =>
        CreateModel(definition);

    private AnimatableBody3D CreateBody(CreatureDefinition definition, ulong id)
    {
        // Core owns movement; this Godot body makes creatures solid to the
        // player's CharacterBody3D without a second movement simulation.
        var collider = definition.Collider;
        var body = new AnimatableBody3D
        {
            Name = $"Creature_{id}",
            CollisionLayer = 1u,
            CollisionMask = 0u,
        };
        body.AddChild(new CollisionShape3D
        {
            Name = "Collider",
            Position = new Vector3(collider.CenterOffset.X,
                collider.CenterOffset.Y, collider.CenterOffset.Z),
            Shape = new BoxShape3D
            {
                Size = new Vector3(collider.Size.X,
                    collider.Size.Y, collider.Size.Z),
            },
        });
        var model = CreateModel(definition);
        model.Name = "Visual";
        body.AddChild(model);
        return body;
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

        PlayAnimation(model, definition, "idle");
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

    private static bool PlayAnimation(
        Node node,
        CreatureDefinition definition,
        string phase)
    {
        if (!definition.Animations.TryGetValue(phase, out var animation) &&
            !(phase == "attack" && definition.Animations.TryGetValue("land", out animation)))
            return false;
        // Older slime GLBs have no dedicated attack clip. Their authored Land
        // squash plays on impact until a pack supplies animations.attack.

        if (node is AnimationPlayer player && player.HasAnimation(animation))
        {
            player.Play(animation);
            return true;
        }

        foreach (var child in node.GetChildren())
        {
            if (PlayAnimation(child, definition, phase)) return true;
        }

        return false;
    }
}
