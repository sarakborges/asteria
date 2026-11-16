using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Gameplay;

/// <summary>
/// Presentation-only view of the Core projectile population. All movement,
/// lifetime and collisions are resolved by ProjectileRuntime.
/// </summary>
public sealed class ProjectilePresentationController
{
    private readonly Node3D _root;
    private readonly Dictionary<ulong, MeshInstance3D> _models = [];
    private readonly BoxMesh _mesh = new()
    {
        Size = Vector3.One,
    };
    private readonly StandardMaterial3D _material = new()
    {
        AlbedoColor = new Color(1f, 0.87f, 0.54f),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    public ProjectilePresentationController(Node3D root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public void Sync(IEnumerable<ProjectileState> projectiles)
    {
        var alive = new HashSet<ulong>();
        foreach (var projectile in projectiles)
        {
            alive.Add(projectile.Id.Value);
            if (!_models.TryGetValue(projectile.Id.Value, out var visual))
            {
                visual = new MeshInstance3D
                {
                    Name = $"Projectile_{projectile.Id.Value}",
                    Mesh = _mesh,
                    MaterialOverride = _material,
                };
                _root.AddChild(visual);
                _models.Add(projectile.Id.Value, visual);
            }

            var position = projectile.Position;
            visual.Position = new Vector3(position.X, position.Y, position.Z);
            var diameter = projectile.Launch.Radius * 2f;
            visual.Scale = new Vector3(diameter, diameter, diameter);
        }

        foreach (var pair in _models.ToArray())
        {
            if (alive.Contains(pair.Key)) continue;
            pair.Value.QueueFree();
            _models.Remove(pair.Key);
        }
    }

    public void Retire()
    {
        // The dimension root owns the Godot nodes and will free them.
        _models.Clear();
    }
}
