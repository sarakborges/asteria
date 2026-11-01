using Asteria.Core.World;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace Asteria.Client.Rendering;

/// <summary>
/// Rasterizes the canonical terrain triangles (including microblock masks,
/// partial shapes, UV rotation and authored texture layers) to a small
/// isometric PNG. No new mesh generator, viewport or browser 3D runtime.
/// </summary>
internal sealed class InventoryVoxelIconRasterizer
{
    private const int Pixels = 96;
    private const int MaximumTriangles = 60_000;
    private const int MaximumCachedIcons = 384;

    private readonly BlockRegistry _blocks;
    private readonly FluidRegistry _fluids;
    private readonly AttachedLayerRegistry _layers;
    private readonly DyeRegistry _dyes;
    private readonly TerrainTextureLookup _lookup;
    private readonly TerrainTextureCatalog _textures;
    // Dye and sampled biome tint both affect the visual identity. A target
    // moving between blocks in the same biome reuses the identical PNG;
    // mutable attached-layer states never share cached images.
    private readonly Dictionary<
        (string Id, VoxelCell Cell, MicroblockMask Mask, string? DyeId,
            BlockPreviewColor? Tint), string> _cache = [];

    public InventoryVoxelIconRasterizer(
        BlockRegistry blocks, FluidRegistry fluids,
        AttachedLayerRegistry layers, DyeRegistry dyes,
        TerrainTextureLookup lookup, TerrainTextureCatalog textures)
    {
        _blocks = blocks;
        _fluids = fluids;
        _layers = layers;
        _dyes = dyes ?? throw new ArgumentNullException(nameof(dyes));
        _lookup = lookup;
        _textures = textures;
    }

    public string? Render(BlockDefinition definition, BlockStateSnapshot state,
        BlockPreviewColor? tintOverride = null)
    {
        // The sampled tint is part of the key, so a biome transition never
        // reuses a stale icon. Mutable attached layers still bypass caching.
        var cacheable = state.SurfaceState.Layers.Count == 0;
        var key = (definition.Id, state.Cell, state.MicroblockMask,
            state.SurfaceState.DyeId, tintOverride);
        if (cacheable && _cache.TryGetValue(key, out var cached))
            return cached;

        var data = BlockStateMeshBuilder.BuildData(
            state, _blocks, _fluids, _lookup, _layers, _dyes);
        if (!data.HasRenderGeometry ||
            data.TriangleCount > MaximumTriangles)
            return null;

        var screen = new Color[Pixels * Pixels];
        var depth = new float[screen.Length];
        Array.Fill(depth, float.NegativeInfinity);
        // An authored dye takes precedence over biome fallback tint, just
        // as the canonical terrain mesher does. Otherwise a sampled world
        // tint overrides the fallback color stored in mesh vertices.
        Color? sampledPalette = null;
        if (tintOverride is { } tint && state.SurfaceState.DyeId is null)
            sampledPalette = new Color(
                tint.Red / 255f, tint.Green / 255f, tint.Blue / 255f);

        foreach (var batch in data.RenderBatches)
        {
            var vertices = batch.Vertices;
            for (var i = 0; i + 2 < vertices.Length; i += 3)
                DrawTriangle(vertices[i], vertices[i + 1], vertices[i + 2],
                    sampledPalette, batch.Batch.RenderMode, screen, depth);
        }

        var pngPixels = new byte[Pixels * Pixels * 4];
        for (var i = 0; i < screen.Length; i++)
        {
            var color = screen[i];
            var offset = i * 4;
            pngPixels[offset] = ToByte(color.R);
            pngPixels[offset + 1] = ToByte(color.G);
            pngPixels[offset + 2] = ToByte(color.B);
            pngPixels[offset + 3] = ToByte(color.A);
        }
        using var image = Image.CreateFromData(
            Pixels, Pixels, false, Image.Format.Rgba8, pngPixels);
        var uri = "data:image/png;base64," +
            Convert.ToBase64String(image.SavePngToBuffer());

        if (cacheable)
        {
            if (_cache.Count >= MaximumCachedIcons)
                _cache.Clear();
            _cache[key] = uri;
        }
        return uri;
    }

    private void DrawTriangle(ChunkMeshVertex a, ChunkMeshVertex b,
        ChunkMeshVertex c, Color? sampledPalette, BlockRenderMode mode,
        Color[] screen, float[] depth)
    {
        // Orthographic view from above +X/+Z, exactly the inventory's
        // familiar top/front/right orientation. Cull hidden backfaces.
        var normal = a.Normal;
        if (normal.X + normal.Y + normal.Z <= .00001f)
            return;

        var pa = Project(a.Position);
        var pb = Project(b.Position);
        var pc = Project(c.Position);
        var twiceArea = Cross(pb, pc, pa);
        if (MathF.Abs(twiceArea) < 0.0000001f)
            return;
        var minX = Math.Clamp((int)MathF.Floor(
            MathF.Min(pa.X, MathF.Min(pb.X, pc.X)) * Pixels), 0, Pixels - 1);
        var maxX = Math.Clamp((int)MathF.Ceiling(
            MathF.Max(pa.X, MathF.Max(pb.X, pc.X)) * Pixels), 0, Pixels - 1);
        var minY = Math.Clamp((int)MathF.Floor(
            MathF.Min(pa.Y, MathF.Min(pb.Y, pc.Y)) * Pixels), 0, Pixels - 1);
        var maxY = Math.Clamp((int)MathF.Ceiling(
            MathF.Max(pa.Y, MathF.Max(pb.Y, pc.Y)) * Pixels), 0, Pixels - 1);

        // A preview of a dyed block must use the actual tint resolved by
        // the same Core mesh path as world terrain. The previous code used
        // definition.PreviewColor for every block and discarded dye RGB.
        var palette = sampledPalette ?? new Color(
            a.TintAndAo.X, a.TintAndAo.Y, a.TintAndAo.Z);
        var baseCode = a.EncodedTextureLayers.X;
        var baseIndex = (int)MathF.Floor(baseCode + .001f);
        var baseTint = Dyable(baseCode);
        var overlayCode = a.EncodedTextureLayers.Y;
        var hasOverlay = overlayCode >= 0f;
        var overlayIndex = (int)MathF.Floor(overlayCode + .001f);
        var overlayTint = hasOverlay && Dyable(overlayCode);
        var axis = NVector3.Abs(normal);
        var faceShade = axis.Y >= axis.X && axis.Y >= axis.Z
            ? (normal.Y >= 0f ? 1f : .5f)
            : axis.Z >= axis.X ? .8f : .6f;

        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var point = new NVector3(
                (x + .5f) / Pixels, (y + .5f) / Pixels, 0);
            var w0 = Cross(pb, pc, point) / twiceArea;
            var w1 = Cross(pc, pa, point) / twiceArea;
            var w2 = 1f - w0 - w1;
            if (w0 < -.00001f || w1 < -.00001f || w2 < -.00001f)
                continue;
            var z = w0 * pa.Z + w1 * pb.Z + w2 * pc.Z;
            var index = y * Pixels + x;
            if (z < depth[index])
                continue;

            var uv = a.Uv * w0 + b.Uv * w1 + c.Uv * w2;
            var basePixel = _textures.SamplePreviewPixel(
                baseIndex, uv.X, uv.Y);
            if (baseTint)
                basePixel = Tint(basePixel, palette);
            var shaded = basePixel;
            if (hasOverlay)
            {
                var overlay = _textures.SamplePreviewPixel(
                    overlayIndex, uv.X, uv.Y);
                if (overlayTint)
                    overlay = Tint(overlay, palette);
                shaded = new Color(
                    Mathf.Lerp(basePixel.R, overlay.R, overlay.A),
                    Mathf.Lerp(basePixel.G, overlay.G, overlay.A),
                    Mathf.Lerp(basePixel.B, overlay.B, overlay.A),
                    basePixel.A + overlay.A * (1f - basePixel.A));
            }

            if (mode == BlockRenderMode.Cutout && shaded.A < .5f ||
                shaded.A <= 0f)
                continue;
            var ao = a.TintAndAo.W * w0 +
                b.TintAndAo.W * w1 + c.TintAndAo.W * w2;
            var value = Math.Clamp(faceShade * ao, 0f, 1f);
            var final = new Color(
                shaded.R * value, shaded.G * value, shaded.B * value, shaded.A);
            if (final.A < 1f && screen[index].A > 0f)
            {
                var previous = screen[index];
                final = new Color(
                    Mathf.Lerp(previous.R, final.R, final.A),
                    Mathf.Lerp(previous.G, final.G, final.A),
                    Mathf.Max(previous.A, final.A));
            }
            screen[index] = final;
            depth[index] = z;
        }
    }

    private static NVector3 Project(NVector3 p) => new(
        .5f + .44f * (p.X - p.Z),
        .005f + .50f * (1f - p.Y) + .245f * (p.X + p.Z),
        p.X + p.Y + p.Z);

    private static float Cross(NVector3 a, NVector3 b, NVector3 p) =>
        (b.X - a.X) * (p.Y - a.Y) -
        (b.Y - a.Y) * (p.X - a.X);

    private static bool Dyable(float code)
    {
        var fraction = code - MathF.Floor(code);
        return ((int)MathF.Floor(fraction * 4f + .01f) & 1) != 0;
    }

    private static Color Tint(Color source, Color tint) =>
        new(source.R * tint.R, source.G * tint.G,
            source.B * tint.B, source.A);

    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
