using Godot;

namespace Asteria.Client.Rendering;

public static class ChunkFluidMeshBuilder
{
    public static ArrayMesh CreateMesh(
        ChunkFluidMeshData data,
        FluidMaterialCatalog materials)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(materials);

        var mesh = new ArrayMesh();

        foreach (var batch in data.Batches)
        {
            if (batch.Vertices.Length == 0)
            {
                continue;
            }

            var surfaceIndex =
                mesh.GetSurfaceCount();
            var surface =
                new SurfaceTool();
            surface.Begin(
                Mesh.PrimitiveType.Triangles);
            surface.SetCustomFormat(
                0,
                SurfaceTool.CustomFormat.Rgba8Unorm);

            foreach (var vertex in batch.Vertices)
            {
                surface.SetNormal(
                    new Vector3(
                        vertex.Normal.X,
                        vertex.Normal.Y,
                        vertex.Normal.Z));
                surface.SetColor(
                    new Color(
                        vertex.TintAndAo.X,
                        vertex.TintAndAo.Y,
                        vertex.TintAndAo.Z,
                        vertex.TintAndAo.W));
                surface.SetCustom(
                    0,
                    new Color(
                        vertex.VoxelLight.X,
                        vertex.VoxelLight.Y,
                        vertex.VoxelLight.Z,
                        vertex.VoxelLight.W));
                surface.AddVertex(
                    new Vector3(
                        vertex.Position.X,
                        vertex.Position.Y,
                        vertex.Position.Z));
            }

            surface.Commit(mesh);
            mesh.SurfaceSetMaterial(
                surfaceIndex,
                materials.Get(batch.Fluid));
        }

        return mesh;
    }
}
