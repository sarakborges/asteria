using Asteria.Client.Gameplay;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Tests;

/// <summary>Headless Godot smoke for derived Object HUD identity.</summary>
public partial class TargetObjectHudSmoke : Node
{
    public bool Verify()
    {
        try
        {
            var world = new VoxelWorld();
            world.InsertChunk(ChunkCoord.Zero, new Chunk());
            var support = new WorldVoxelCoord(3, 4, 4);
            var cell = new ObjectCell("asteria:test_object", BlockFace.Top);
            var nearby = new WorldObjectHit(support, cell, 2.5f);
            var distant = new WorldObjectHit(support, cell, 3.75f);
            var first = TargetObjectHudSnapshot.FromWorld(world, nearby);
            if (first != TargetObjectHudSnapshot.FromWorld(world, distant))
                throw new InvalidDataException(
                    "Camera motion cannot republish unchanged Object HUD state.");

            if (TargetObjectHudSnapshot.FromWorld(world, new WorldObjectHit(
                    support, new ObjectCell("asteria:other", BlockFace.Top),
                    2.5f)) == first)
                throw new InvalidDataException("Object identity change was ignored.");

            GD.Print("Native Object HUD smoke: identity and distance-gating OK");
            return true;
        }
        catch (Exception error)
        {
            GD.PushError("Native Object HUD smoke failed: " + error);
            return false;
        }
    }
}
