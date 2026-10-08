namespace Asteria.Core.World;

/// <summary>
/// Current block placement selection and authored rotation policy belong to
/// one player, not to a Sphere or to the WebUI. Non-rotatable blocks do not
/// claim an unsupported tool action.
/// </summary>
public sealed class HeldBlockPlacement
{
    private BlockDefinition? _definition;

    public BlockRuntimeId Block { get; private set; }

    public BlockOrientation Orientation { get; private set; } =
        BlockOrientation.Y;

    public HorizontalFacing Facing { get; private set; } =
        HorizontalFacing.South;

    public bool CanRotate =>
        _definition is { } definition &&
        (definition.IsRotatable || definition.UsesHorizontalFacing);

    public void Clear()
    {
        Block = BlockRuntimeId.Air;
        _definition = null;
        Orientation = BlockOrientation.Y;
        Facing = HorizontalFacing.South;
    }

    public void Select(BlockRuntimeId id, BlockDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (id.IsAir)
        {
            throw new ArgumentException(
                "The selected placement block must not be air.",
                nameof(id));
        }

        if (id == Block && ReferenceEquals(definition, _definition))
        {
            return;
        }

        Block = id;
        _definition = definition;
        Orientation = definition.Orientations[0];
        Facing = HorizontalFacing.South;
    }

    public bool Rotate()
    {
        var definition = _definition;
        if (definition is null) return false;

        if (definition.UsesHorizontalFacing)
        {
            Facing = Facing switch
            {
                HorizontalFacing.South => HorizontalFacing.East,
                HorizontalFacing.East => HorizontalFacing.North,
                HorizontalFacing.North => HorizontalFacing.West,
                HorizontalFacing.West => HorizontalFacing.South,
                _ => throw new InvalidOperationException(
                    "Invalid held block facing."),
            };
            return true;
        }

        if (!definition.IsRotatable)
        {
            return false;
        }

        var index = -1;
        for (var i = 0; i < definition.Orientations.Count; i++)
        {
            if (definition.Orientations[i] == Orientation)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            throw new InvalidOperationException(
                "Selected block has an orientation outside its authored set.");
        }

        Orientation = definition.Orientations[
            (index + 1) % definition.Orientations.Count];
        return true;
    }

    public VoxelCell CurrentCell() =>
        Block.IsAir
            ? VoxelCell.Empty
            : new VoxelCell(Block, orientation: Orientation, facing: Facing);
}
