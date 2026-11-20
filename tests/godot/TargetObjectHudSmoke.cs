using Asteria.Client.Gameplay;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Tests;

/// <summary>Headless Godot smoke for derived Object HUD identity and light.</summary>
public partial class TargetObjectHudSmoke : Node
{
    public bool Verify()
    {
        try
        {
            var world = new VoxelWorld();
            world.InsertChunk(ChunkCoord.Zero, new Chunk());
            var support = new WorldVoxelCoord(3, 4, 4);
            var sampleAt = support + (0, 1, 0);
            var cell = new ObjectCell("asteria:test_object", BlockFace.Top);
            var nearby = new WorldObjectHit(support, cell, 2.5f);
            var distant = new WorldObjectHit(support, cell, 3.75f);
            var first = TargetObjectHudSnapshot.FromWorld(world, nearby);
            if (first != TargetObjectHudSnapshot.FromWorld(world, distant))
                throw new InvalidDataException(
                    "Camera motion cannot republish unchanged Object HUD state.");

            if (!world.TrySetLight(sampleAt, new VoxelLight(14, 4, 7, 2)))
                throw new InvalidDataException("Object light fixture did not change.");
            var changed = TargetObjectHudSnapshot.FromWorld(world, nearby);
            if (changed == first || changed.SkyLight != 14 ||
                changed.BlockLight != 7)
                throw new InvalidDataException("Object HUD ignored changed light.");

            // The public display is the peak RGB light, so changing a
            // subordinate channel must not republish the same HUD text.
            world.TrySetLight(sampleAt, new VoxelLight(14, 6, 7, 2));
            if (TargetObjectHudSnapshot.FromWorld(world, nearby) != changed)
                throw new InvalidDataException("Object HUD depends on unseen RGB data.");

            if (TargetObjectHudSnapshot.FromWorld(world, new WorldObjectHit(
                    support, new ObjectCell("asteria:other", BlockFace.Top),
                    2.5f)) == changed)
                throw new InvalidDataException("Object identity change was ignored.");

            GD.Print("Native Object HUD smoke: identity, light, distance-gating OK");
            return true;
        }
        catch (Exception error)
        {
            GD.PushError("Native Object HUD smoke failed: " + error);
            return false;
        }
    }
}
