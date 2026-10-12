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

        using var image = ProjectPackFiles.LoadImage(_selection, definition.Visual.Texture);
        var texture = ImageTexture.CreateFromImage(image) ??
            throw new InvalidDataException($"Object texture cannot be decoded: {definition.Visual.Texture}");
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        var visual = definition.Visual;
        switch (visual.Kind)
        {
            case ObjectVisualKind.CrossedSprite:
                EmitCrossedSprite(surface, visual);
                break;
            case ObjectVisualKind.ExtrudedSprite:
                EmitExtrudedSprite(surface, image, visual);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(visual.Kind));
        }

        var mesh = surface.Commit() ??
            throw new InvalidOperationException($"Object {definition.Id} generated no mesh.");
        // Match the voxel terrain's light model. StandardMaterial3D's
        // physical lights cannot see our propagated skylight/block light.
        Material material;
        if (definition.Unlit)
        {
            material = new StandardMaterial3D
            {
                AlbedoTexture = texture,
                VertexColorUseAsAlbedo = definition.Tint != BlockTint.None,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                AlphaScissorThreshold = visual.AlphaCutoff,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
        }
        else
        {
            var shader = ResourceLoader.Load<Shader>(
                "res://shaders/voxel_object.gdshader")
                ?? throw new FileNotFoundException("Missing voxel object shader.");
            var voxelMaterial = new ShaderMaterial { Shader = shader };
            voxelMaterial.SetShaderParameter("object_texture", texture);
            voxelMaterial.SetShaderParameter("alpha_cutoff", visual.AlphaCutoff);
            material = voxelMaterial;
        }
        mesh.SurfaceSetMaterial(0, material);
        _meshes.Add(definition.Id, mesh);
        return mesh;
    }

    private static void EmitCrossedSprite(
        SurfaceTool surface, ObjectVisualDefinition visual)
    {
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
    }

    private static void EmitExtrudedSprite(
        SurfaceTool surface, Image image, ObjectVisualDefinition visual)
    {
        var width = image.GetWidth();
        var height = image.GetHeight();
        if (width <= 0 || height <= 0 ||
            (long)width * height > ExtrudedSpriteMesh.MaximumTexturePixels)
            throw new InvalidDataException("Object extrudedSprite texture exceeds bounded size.");

        // Godot decodes the selected pack image; Core owns the contour mesh.
        image.Convert(Image.Format.Rgba8);
        var pixels = image.GetData();
        var alpha = new byte[checked(width * height)];
        for (var i = 0; i < alpha.Length; i++)
            alpha[i] = pixels[i * 4 + 3];

        foreach (var vertex in ExtrudedSpriteMesh.Build(visual, width, height, alpha))
            EmitVertex(surface,
                new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z),
                new Vector2(vertex.Uv.X, vertex.Uv.Y),
                new Vector3(vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z));
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
        // Always author white base vertex color so instance/preview tint
        // multiplies the same alpha-cutout texture without changing its hue.
        surface.SetColor(Colors.White);
        surface.AddVertex(position);
    }
}
