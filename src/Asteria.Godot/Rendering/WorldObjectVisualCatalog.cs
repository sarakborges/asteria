using Asteria.Client.Content;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// One authored mesh/material per Object type and active Sphere. Chunks
/// reference these immutable resources in MultiMesh batches; no per-object
/// scene nodes or Godot pack imports are created.
/// </summary>
internal sealed class WorldObjectVisualCatalog
{
    private readonly PackSelection _selection;
    private readonly Dictionary<string, Mesh> _meshes = new(StringComparer.Ordinal);

    public WorldObjectVisualCatalog(PackSelection selection) =>
        _selection = selection;

    public Mesh GetMesh(ObjectDefinition definition)
    {
        if (_meshes.TryGetValue(definition.Id, out var cached))
            return cached;

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        var visual = definition.Visual;
        if (visual.Kind != ObjectVisualKind.CrossedSprite)
            throw new InvalidOperationException("Unimplemented Object mesh type.");
        for (var plane = 0; plane < visual.Planes; plane++)
        {
                var angle = MathF.PI * plane / visual.Planes;
                var forward = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
                var half = forward * visual.Width * .5f;
                EmitQuad(surface,
                    -half + Vector3.Up * visual.BaseOffset,
                    half + Vector3.Up * visual.BaseOffset,
                    half + Vector3.Up * (visual.BaseOffset + visual.Height),
                    -half + Vector3.Up * (visual.BaseOffset + visual.Height));
        }

        var mesh = surface.Commit() ??
            throw new InvalidOperationException($"Object {definition.Id} generated no mesh.");
        var material = new StandardMaterial3D
        {
            AlbedoTexture = ProjectPackFiles.LoadTexture(_selection, visual.Texture),
            ShadingMode = definition.Unlit
                ? BaseMaterial3D.ShadingModeEnum.Unshaded
                : BaseMaterial3D.ShadingModeEnum.PerPixel,
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = visual.AlphaCutoff,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        mesh.SurfaceSetMaterial(0, material);
        _meshes.Add(definition.Id, mesh);
        return mesh;
    }

    private static void EmitQuad(SurfaceTool surface,
        Vector3 bottomLeft, Vector3 bottomRight,
        Vector3 topRight, Vector3 topLeft)
    {
        var normal = (bottomRight - bottomLeft).Cross(topLeft - bottomLeft).Normalized();
        EmitVertex(surface, bottomLeft, new Vector2(0, 1), normal);
        EmitVertex(surface, bottomRight, new Vector2(1, 1), normal);
        EmitVertex(surface, topRight, new Vector2(1, 0), normal);
        EmitVertex(surface, bottomLeft, new Vector2(0, 1), normal);
        EmitVertex(surface, topRight, new Vector2(1, 0), normal);
        EmitVertex(surface, topLeft, new Vector2(0, 0), normal);
    }

    private static void EmitVertex(
        SurfaceTool surface, Vector3 position, Vector2 uv, Vector3 normal)
    {
        surface.SetNormal(normal);
        surface.SetUV(uv);
        surface.AddVertex(position);
    }
}
