using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// One camera-held RGB emitter for non-voxel receivers plus a render-only
/// uniform for unshaded terrain. Never participates in authoritative voxel
/// light or dimension persistence.
/// </summary>
internal sealed class HeldDynamicLightPresentation
{
    private static readonly Vector3 HandOffset =
        new(0.32f, -0.24f, -0.52f);
    private readonly Node _parent;
    private readonly VoxelTerrainMaterialSet _terrain;
    private readonly BlockRegistry _blocks;
    private readonly PackContentRegistry<ObjectDefinition> _objects;

    private OmniLight3D? _light;
    private ulong _lastInventoryRevision = ulong.MaxValue;
    private BlockLightEmission _emission;
    private Vector3? _lastPosition;

    public HeldDynamicLightPresentation(
        Node parent,
        VoxelTerrainMaterialSet terrain,
        BlockRegistry blocks,
        PackContentRegistry<ObjectDefinition> objects)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    }

    public void Sync(PlayerInventory? inventory, Camera3D? camera)
    {
        if (inventory is null || camera is null)
        {
            Clear();
            return;
        }

        if (_lastInventoryRevision != inventory.Revision)
        {
            _lastInventoryRevision = inventory.Revision;
            var next = HeldLightResolver.Resolve(
                inventory.SelectedStack, _blocks, _objects);
            if (_emission != next)
            {
                _emission = next;
                _lastPosition = null;
                ConfigureLight();
            }
        }

        if (_emission.IsDark)
            return;

        var worldPosition = camera.GlobalTransform * HandOffset;
        if (_lastPosition == worldPosition)
            return;

        _lastPosition = worldPosition;
        _terrain.SetHeldLight(worldPosition, _emission);
        if (_light is not null)
            _light.GlobalPosition = worldPosition;
    }

    public void Clear()
    {
        // Clearing the source is immediate, including if the camera
        // disappears during Sphere retirement or world unloading.
        if (_light is not null)
            _light.Visible = false;
        _terrain.ClearHeldLight();
        _lastPosition = null;
        _lastInventoryRevision = ulong.MaxValue;
        _emission = default;
    }

    private void ConfigureLight()
    {
        if (_emission.IsDark)
        {
            if (_light is not null)
                _light.Visible = false;
            _terrain.ClearHeldLight();
            return;
        }

        if (_light is null)
        {
            _light = new OmniLight3D
            {
                Name = "HeldDynamicLight",
                OmniRange = 8f,
                ShadowEnabled = false,
            };
            _parent.AddChild(_light);
        }

        var peak = Math.Max(_emission.Red,
            Math.Max(_emission.Green, _emission.Blue));
        _light.LightColor = new Color(
            _emission.Red / (float)peak,
            _emission.Green / (float)peak,
            _emission.Blue / (float)peak);
        _light.LightEnergy = 1.8f * peak / VoxelLight.MaxLevel;
        _light.Visible = true;
    }
}
