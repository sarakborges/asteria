using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// Attaches authored cuboid equipment to the original animated mesh parts.
/// The authoritative PlayerInventory is read-only here. Both the in-world
/// model and the isolated portrait use the same renderer and selected pack.
/// </summary>
internal sealed class PlayerEquipmentPresentation
{
    private readonly PlayerInventory _inventory;
    private readonly PackContentRegistry<ItemDefinition> _items;
    private readonly Dictionary<string, MeshInstance3D> _bodyMeshes;
    private readonly List<MeshInstance3D>[] _published =
        Enumerable.Range(0, PlayerInventory.EquipmentSlots)
            .Select(_ => new List<MeshInstance3D>()).ToArray();
    private readonly InventoryEntry?[] _displayed =
        new InventoryEntry?[PlayerInventory.EquipmentSlots];

    public PlayerEquipmentPresentation(
        Node scene, PlayerInventory inventory,
        PackContentRegistry<ItemDefinition> items)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        ArgumentNullException.ThrowIfNull(scene);
        _bodyMeshes = PlayerVisualSceneFactory.Descendants(scene)
            .OfType<MeshInstance3D>()
            .Where(mesh => !mesh.Name.ToString().EndsWith("Overlay", StringComparison.Ordinal))
            .ToDictionary(mesh => mesh.Name.ToString(), StringComparer.Ordinal);
    }

    public bool Sync()
    {
        var changed = false;
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var index = (int)slot;
            var current = _inventory.EquipmentAt(slot)?.Entry;
            if (Equals(_displayed[index], current)) continue;

            var appearance = Resolve(slot, current);
            // Resolve before releasing the current presentation; invalid
            // authored definitions do not leave a partially-applied outfit.
            foreach (var mesh in _published[index])
            {
                if (mesh.GetParent() is { } parent)
                    parent.RemoveChild(mesh);
                mesh.QueueFree();
            }
            _published[index].Clear();
            if (appearance is not null)
                foreach (var part in appearance.Parts)
                    _published[index].Add(Publish(part));
            _displayed[index] = current;
            changed = true;
        }
        return changed;
    }

    private EquipmentVisualDefinition? Resolve(EquipmentSlot slot, InventoryEntry? entry)
    {
        if (entry is not { Kind: InventoryEntryKind.Item } ||
            !_items.TryGet(entry.Id, out var item) || item?.EquipmentSlot != slot)
            return null;

        var visual = item.EquipmentVisual;
        if (visual is not null && visual.Parts.Any(
            part => !_bodyMeshes.ContainsKey(part.Mesh)))
            throw new InvalidDataException(
                $"Player GLB is missing an authored equipment attachment mesh for {item.Id}.");
        return visual;
    }

    private MeshInstance3D Publish(EquipmentVisualPart part)
    {
        var color = new Color(
            ((part.Rgb >> 16) & 0xff) / 255f,
            ((part.Rgb >> 8) & 0xff) / 255f,
            (part.Rgb & 0xff) / 255f);
        var mesh = new MeshInstance3D
        {
            Name = "EquipmentCuboid",
            Mesh = new BoxMesh
            {
                Size = new Vector3(part.Size.X, part.Size.Y, part.Size.Z),
            },
            Position = new Vector3(part.Offset.X, part.Offset.Y, part.Offset.Z),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Metallic = 0f,
                Roughness = 0.86f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            }
        };
        _bodyMeshes[part.Mesh].AddChild(mesh);
        return mesh;
    }
}
