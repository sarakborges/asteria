using Asteria.Client.Gameplay;
using Asteria.Core.World;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace Asteria.Client.Tests;

/// <summary>Verifies native source-fluid target selection and state transitions.</summary>
public partial class TargetFluidHudSmoke : Node
{
    public bool Verify()
    {
        try
        {
            var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
            var fluids = new FluidRegistry([
                new FluidDefinition("asteria:water", new FluidColor(30, 100, 205), 0.75f),
            ]);
            var world = new VoxelWorld();
            world.InsertChunk(ChunkCoord.Zero, new Chunk());
            var target = new WorldVoxelCoord(3, 4, 4);
            if (!world.SetFluidAt(
                target, FluidCell.Source(fluids.GetId("asteria:water")), out _))
                throw new InvalidOperationException("Could not create HUD fluid fixture.");

            var origin = new NVector3(1.5f, 4.5f, 4.5f);
            var hit = VoxelWorldRaycaster.Raycast(
                world, blocks, origin, NVector3.UnitX, 6f,
                includeFluidSources: true);
            var tracker = new TargetHudStateTracker();
            if (!tracker.TryCaptureFluid(world, hit, false, out var preview) ||
                preview is not { } source || source.Voxel != target ||
                source.Fluid.Level != FluidCell.MaxLevel ||
                tracker.TryCaptureFluid(world, hit, false, out _))
                throw new InvalidDataException(
                    "Fluid target selection failed to detect unchanged source.");

            if (!tracker.TryCapture(world, blocks,
                    (VoxelWorldHit?)null, false, out var cleared) ||
                cleared is not null)
                throw new InvalidDataException(
                    "Fluid to empty transition did not invalidate target HUD.");

            if (!tracker.TryCaptureFluid(world, hit, false, out _) ||
                !world.SetBlockAt(new WorldVoxelCoord(2, 4, 4),
                    blocks.GetId("asteria:stone"), out _))
                throw new InvalidDataException("Fluid to block fixture failed.");
            var obstruction = VoxelWorldRaycaster.Raycast(
                world, blocks, origin, NVector3.UnitX, 6f,
                includeFluidSources: true);
            if (tracker.TryCaptureFluid(world, obstruction, false, out var noFluid) ||
                noFluid is not null ||
                !tracker.TryCapture(world, blocks, obstruction, false,
                    out var block) || block is null ||
                block.Value.BlockId != "asteria:stone")
                throw new InvalidDataException(
                    "Fluid to occluding block transition not published.");

            GD.Print("Native fluid target smoke: source, stable, clear, occlusion OK");
            return true;
        }
        catch (Exception error)
        {
            GD.PushError("Native fluid target smoke failed: " + error);
            return false;
        }
    }
}
