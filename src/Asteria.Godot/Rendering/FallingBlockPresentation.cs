using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public sealed class FallingBlockPresentation
{
    public FallingBlockPresentation(
        FallingBlockState state,
        ArrayMesh mesh)
    {
        Id = state.Id;

        Root = new Node3D
        {
            Name =
                $"FallingBlock_{state.Id.Value}",
        };

        Root.AddChild(
            new MeshInstance3D
            {
                Name = "Mesh",
                Mesh = mesh,
            });

        Apply(state);
    }

    public FallingBlockId Id { get; }

    public Node3D Root { get; }

    public void Apply(
        FallingBlockState state)
    {
        Root.Position =
            new Vector3(
                state.ColumnX,
                (float)(state.CenterY - 0.5),
                state.ColumnZ);
    }

    public void Retire()
    {
        Root.QueueFree();
    }

    public static ArrayMesh BuildMesh(
        VoxelCell cell,
        BlockRegistry blocks,
        FluidRegistry fluids,
        TerrainTextureLookup textures,
        VoxelTerrainMaterialSet materials)
    {
        var chunk = new Chunk();
        chunk.SetCell(
            0,
            0,
            0,
            cell);

        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            fluids);

        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);

        var data =
            ChunkMeshDataBuilder.BuildMeshlet(
                world,
                ChunkCoord.Zero,
                blocks,
                textures,
                meshletIndex: 0);

        return ChunkMeshBuilder.CreateMesh(
            data,
            materials);
    }
}
