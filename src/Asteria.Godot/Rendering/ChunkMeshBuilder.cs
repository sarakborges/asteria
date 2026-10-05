using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public static class ChunkMeshBuilder
{
    private const int FineResolution = BlockGeometry.Resolution;
    private const int FinePlaneArea = FineResolution * FineResolution;
    private const float DyableLayerFlag = 0.25f;

    // Godot treats clockwise triangle winding as the front face.
    private static readonly int[] TriangleOrder = [0, 2, 1, 0, 3, 2];

    private static readonly BlockFace[] Faces =
    [
        BlockFace.Right,
        BlockFace.Left,
        BlockFace.Top,
        BlockFace.Bottom,
        BlockFace.Front,
        BlockFace.Back,
    ];

    public static ArrayMesh Build(
        Chunk chunk,
        BlockRegistry blocks,
        TerrainTextureCatalog textures,
        Material material)
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);

        for (var y = 0; y < Chunk.Size; y++)
        {
            for (var z = 0; z < Chunk.Size; z++)
            {
                for (var x = 0; x < Chunk.Size; x++)
                {
                    var cell = chunk.GetCell(x, y, z);
                    if (cell.IsEmpty)
                    {
                        continue;
                    }

                    var definition = blocks.GetDefinition(cell.Block);
                    if (RequiresFineMeshing(chunk, blocks, x, y, z, cell, definition))
                    {
                        EmitFineCell(surface, chunk, blocks, textures, x, y, z, cell, definition);
                    }
                    else
                    {
                        EmitCubeCell(surface, chunk, blocks, textures, x, y, z, cell, definition);
                    }
                }
            }
        }

        var mesh = surface.Commit();
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    private static bool RequiresFineMeshing(
        Chunk chunk,
        BlockRegistry blocks,
        int x,
        int y,
        int z,
        VoxelCell cell,
        BlockDefinition definition)
    {
        if (BlockGeometry.RequiresFineMeshing(definition, cell))
        {
            return true;
        }

        foreach (var face in Faces)
        {
            var (dx, dy, dz) = FaceOffset(face);
            var neighbor = chunk.GetCellOrEmpty(x + dx, y + dy, z + dz);
            if (neighbor.IsEmpty)
            {
                continue;
            }

            var neighborDefinition = blocks.GetDefinition(neighbor.Block);
            if (BlockGeometry.RequiresFineMeshing(neighborDefinition, neighbor))
            {
                return true;
            }
        }

        return false;
    }

    private static void EmitCubeCell(
        SurfaceTool surface,
        Chunk chunk,
        BlockRegistry blocks,
        TerrainTextureCatalog textures,
        int x,
        int y,
        int z,
        VoxelCell cell,
        BlockDefinition definition)
    {
        var origin = new Vector3(x, y, z);

        foreach (var face in Faces)
        {
            var (dx, dy, dz) = FaceOffset(face);
            if (!FaceIsExposed(
                    chunk,
                    blocks,
                    cell,
                    definition,
                    x + dx,
                    y + dy,
                    z + dz))
            {
                continue;
            }

            var faceMaterial = ResolveFaceMaterial(textures, definition, cell, face);
            AddQuad(
                surface,
                origin,
                FaceNormal(face),
                FaceCorners(face),
                face,
                cell,
                faceMaterial);
        }
    }

    private static bool FaceIsExposed(
        Chunk chunk,
        BlockRegistry blocks,
        VoxelCell source,
        BlockDefinition sourceDefinition,
        int x,
        int y,
        int z)
    {
        var neighbor = chunk.GetCellOrEmpty(x, y, z);
        if (neighbor.IsEmpty)
        {
            return true;
        }

        var neighborDefinition = blocks.GetDefinition(neighbor.Block);
        return !Occludes(source, sourceDefinition, neighbor, neighborDefinition);
    }

    private static void EmitFineCell(
        SurfaceTool surface,
        Chunk chunk,
        BlockRegistry blocks,
        TerrainTextureCatalog textures,
        int blockX,
        int blockY,
        int blockZ,
        VoxelCell cell,
        BlockDefinition definition)
    {
        var origin = new Vector3(blockX, blockY, blockZ);
        var sourceMask = chunk.GetMicroblockMask(blockX, blockY, blockZ);
        var visible = new bool[FinePlaneArea];

        foreach (var face in Faces)
        {
            var faceMaterial = ResolveFaceMaterial(textures, definition, cell, face);

            for (var depth = 0; depth < FineResolution; depth++)
            {
                Array.Clear(visible);

                for (var v = 0; v < FineResolution; v++)
                {
                    for (var u = 0; u < FineResolution; u++)
                    {
                        var (localX, localY, localZ) = FinePosition(face, depth, u, v);
                        if (!BlockGeometry.IsOccupied(
                                definition,
                                cell,
                                sourceMask,
                                localX,
                                localY,
                                localZ))
                        {
                            continue;
                        }

                        if (FineNeighborOccludes(
                                chunk,
                                blocks,
                                blockX,
                                blockY,
                                blockZ,
                                localX,
                                localY,
                                localZ,
                                face,
                                cell,
                                definition))
                        {
                            continue;
                        }

                        visible[u + v * FineResolution] = true;
                    }
                }

                EmitGreedyRectangles(
                    surface,
                    origin,
                    face,
                    depth,
                    visible,
                    cell,
                    faceMaterial);
            }
        }
    }

    private static bool FineNeighborOccludes(
        Chunk chunk,
        BlockRegistry blocks,
        int blockX,
        int blockY,
        int blockZ,
        int localX,
        int localY,
        int localZ,
        BlockFace face,
        VoxelCell source,
        BlockDefinition sourceDefinition)
    {
        var (dx, dy, dz) = FaceOffset(face);
        var neighborBlockX = blockX;
        var neighborBlockY = blockY;
        var neighborBlockZ = blockZ;
        var neighborLocalX = localX + dx;
        var neighborLocalY = localY + dy;
        var neighborLocalZ = localZ + dz;

        WrapFineCoordinate(ref neighborBlockX, ref neighborLocalX);
        WrapFineCoordinate(ref neighborBlockY, ref neighborLocalY);
        WrapFineCoordinate(ref neighborBlockZ, ref neighborLocalZ);

        if (!Chunk.Contains(neighborBlockX, neighborBlockY, neighborBlockZ))
        {
            return false;
        }

        var neighbor = chunk.GetCell(neighborBlockX, neighborBlockY, neighborBlockZ);
        if (neighbor.IsEmpty)
        {
            return false;
        }

        var neighborDefinition = blocks.GetDefinition(neighbor.Block);
        var neighborMask = chunk.GetMicroblockMask(neighborBlockX, neighborBlockY, neighborBlockZ);

        if (!BlockGeometry.IsOccupied(
                neighborDefinition,
                neighbor,
                neighborMask,
                neighborLocalX,
                neighborLocalY,
                neighborLocalZ))
        {
            return false;
        }

        return Occludes(source, sourceDefinition, neighbor, neighborDefinition);
    }

    private static bool Occludes(
        VoxelCell source,
        BlockDefinition sourceDefinition,
        VoxelCell neighbor,
        BlockDefinition neighborDefinition) =>
        neighborDefinition.IsOpaque ||
        (source.Block == neighbor.Block && sourceDefinition.RenderMode != BlockRenderMode.Opaque);

    private static void WrapFineCoordinate(ref int block, ref int local)
    {
        if (local < 0)
        {
            block--;
            local += FineResolution;
        }
        else if (local >= FineResolution)
        {
            block++;
            local -= FineResolution;
        }
    }

    private static void EmitGreedyRectangles(
        SurfaceTool surface,
        Vector3 origin,
        BlockFace face,
        int depth,
        bool[] visible,
        VoxelCell cell,
        TerrainFaceMaterial faceMaterial)
    {
        for (var v = 0; v < FineResolution; v++)
        {
            for (var u = 0; u < FineResolution; u++)
            {
                if (!visible[u + v * FineResolution])
                {
                    continue;
                }

                var width = 1;
                while (u + width < FineResolution &&
                       visible[u + width + v * FineResolution])
                {
                    width++;
                }

                var height = 1;
                while (v + height < FineResolution)
                {
                    var rowVisible = true;
                    for (var column = u; column < u + width; column++)
                    {
                        if (!visible[column + (v + height) * FineResolution])
                        {
                            rowVisible = false;
                            break;
                        }
                    }

                    if (!rowVisible)
                    {
                        break;
                    }

                    height++;
                }

                for (var row = v; row < v + height; row++)
                {
                    for (var column = u; column < u + width; column++)
                    {
                        visible[column + row * FineResolution] = false;
                    }
                }

                EmitFineRectangle(
                    surface,
                    origin,
                    face,
                    depth,
                    u,
                    v,
                    width,
                    height,
                    cell,
                    faceMaterial);
            }
        }
    }

    private static void EmitFineRectangle(
        SurfaceTool surface,
        Vector3 origin,
        BlockFace face,
        int depth,
        int u,
        int v,
        int width,
        int height,
        VoxelCell cell,
        TerrainFaceMaterial faceMaterial)
    {
        var (minX, minY, minZ) = FinePosition(face, depth, u, v);
        var lowerX = minX;
        var lowerY = minY;
        var lowerZ = minZ;
        var upperX = minX;
        var upperY = minY;
        var upperZ = minZ;

        switch (face)
        {
            case BlockFace.Right:
            case BlockFace.Left:
                upperX++;
                upperY += height;
                upperZ += width;
                break;

            case BlockFace.Top:
            case BlockFace.Bottom:
                upperX += width;
                upperY++;
                upperZ += height;
                break;

            case BlockFace.Front:
            case BlockFace.Back:
                upperX += width;
                upperY += height;
                upperZ++;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(face));
        }

        switch (face)
        {
            case BlockFace.Right:
                lowerX = upperX;
                break;
            case BlockFace.Left:
                upperX = lowerX;
                break;
            case BlockFace.Top:
                lowerY = upperY;
                break;
            case BlockFace.Bottom:
                upperY = lowerY;
                break;
            case BlockFace.Front:
                lowerZ = upperZ;
                break;
            case BlockFace.Back:
                upperZ = lowerZ;
                break;
        }

        var scale = 1f / FineResolution;
        var lower = new Vector3(lowerX * scale, lowerY * scale, lowerZ * scale);
        var upper = new Vector3(upperX * scale, upperY * scale, upperZ * scale);
        var corners = FaceCorners(face);
        var normal = FaceNormal(face);

        foreach (var index in TriangleOrder)
        {
            var selector = corners[index];
            var local = new Vector3(
                selector.X == 0f ? lower.X : upper.X,
                selector.Y == 0f ? lower.Y : upper.Y,
                selector.Z == 0f ? lower.Z : upper.Z);

            WriteVertex(
                surface,
                origin + local,
                normal,
                face,
                local,
                cell,
                faceMaterial);
        }
    }

    private static void AddQuad(
        SurfaceTool surface,
        Vector3 origin,
        Vector3 normal,
        Vector3[] corners,
        BlockFace face,
        VoxelCell cell,
        TerrainFaceMaterial faceMaterial)
    {
        foreach (var index in TriangleOrder)
        {
            var local = corners[index];
            WriteVertex(
                surface,
                origin + local,
                normal,
                face,
                local,
                cell,
                faceMaterial);
        }
    }

    private static void WriteVertex(
        SurfaceTool surface,
        Vector3 position,
        Vector3 normal,
        BlockFace worldFace,
        Vector3 local,
        VoxelCell cell,
        TerrainFaceMaterial faceMaterial)
    {
        var sourcePoint = ToSourcePoint(local, cell);
        var uv = MacroUv(faceMaterial.SourceFace, sourcePoint);
        uv = RotateUv(uv, cell.TextureRotation, faceMaterial.RotateTexture);

        surface.SetNormal(normal);
        surface.SetColor(faceMaterial.Tint);
        surface.SetUV(uv);
        surface.SetUV2(faceMaterial.EncodedLayers);
        surface.AddVertex(position);
    }

    private static TerrainFaceMaterial ResolveFaceMaterial(
        TerrainTextureCatalog textures,
        BlockDefinition definition,
        VoxelCell cell,
        BlockFace worldFace)
    {
        var sourceFace = BlockFaceTransform.SourceFaceForWorldFace(worldFace, cell, definition);
        var layers = definition.Textures.ResolveForFace(sourceFace);

        if (layers.Count > 2)
        {
            throw new InvalidOperationException(
                $"Terrain texture array currently supports at most two layers per face: " +
                $"{definition.Id} {sourceFace} has {layers.Count}.");
        }

        var baseCode = 0f;
        var overlayCode = -1f;

        if (layers.Count > 0)
        {
            baseCode = textures.GetIndex(layers[0].Texture) +
                       (layers[0].Dyable ? DyableLayerFlag : 0f);
        }

        if (layers.Count > 1)
        {
            overlayCode = textures.GetIndex(layers[1].Texture) +
                          (layers[1].Dyable ? DyableLayerFlag : 0f);
        }

        var tint = definition.Tint == BlockTint.None
            ? Colors.White
            : ToGodotColor(definition.PreviewColor);

        return new TerrainFaceMaterial(
            sourceFace,
            new Vector2(baseCode, overlayCode),
            tint,
            definition.RotateTexture.Rotates(sourceFace));
    }

    private static Vector3 ToSourcePoint(Vector3 worldPoint, VoxelCell cell)
    {
        var point = cell.Orientation switch
        {
            BlockOrientation.Y => worldPoint,
            BlockOrientation.Z => new Vector3(
                worldPoint.X,
                worldPoint.Z,
                1f - worldPoint.Y),
            BlockOrientation.X => new Vector3(
                1f - worldPoint.Y,
                worldPoint.X,
                worldPoint.Z),
            _ => throw new ArgumentOutOfRangeException(nameof(cell.Orientation)),
        };

        return cell.Facing switch
        {
            HorizontalFacing.South => point,
            HorizontalFacing.East => new Vector3(1f - point.Z, point.Y, point.X),
            HorizontalFacing.North => new Vector3(1f - point.X, point.Y, 1f - point.Z),
            HorizontalFacing.West => new Vector3(point.Z, point.Y, 1f - point.X),
            _ => throw new ArgumentOutOfRangeException(nameof(cell.Facing)),
        };
    }

    private static Vector2 MacroUv(BlockFace face, Vector3 point) => face switch
    {
        BlockFace.Right => new Vector2(1f - point.Z, 1f - point.Y),
        BlockFace.Left => new Vector2(point.Z, 1f - point.Y),
        BlockFace.Top => new Vector2(point.X, point.Z),
        BlockFace.Bottom => new Vector2(point.X, 1f - point.Z),
        BlockFace.Front => new Vector2(point.X, 1f - point.Y),
        BlockFace.Back => new Vector2(1f - point.X, 1f - point.Y),
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    private static Vector2 RotateUv(
        Vector2 uv,
        TextureRotation rotation,
        bool enabled)
    {
        if (!enabled)
        {
            return uv;
        }

        return rotation switch
        {
            TextureRotation.Degrees0 => uv,
            TextureRotation.Degrees90 => new Vector2(1f - uv.Y, uv.X),
            TextureRotation.Degrees180 => new Vector2(1f - uv.X, 1f - uv.Y),
            TextureRotation.Degrees270 => new Vector2(uv.Y, 1f - uv.X),
            _ => throw new ArgumentOutOfRangeException(nameof(rotation)),
        };
    }

    private static (int X, int Y, int Z) FinePosition(
        BlockFace face,
        int depth,
        int u,
        int v) => face switch
    {
        BlockFace.Right or BlockFace.Left => (depth, v, u),
        BlockFace.Top or BlockFace.Bottom => (u, depth, v),
        BlockFace.Front or BlockFace.Back => (u, v, depth),
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    private static (int X, int Y, int Z) FaceOffset(BlockFace face) => face switch
    {
        BlockFace.Right => (1, 0, 0),
        BlockFace.Left => (-1, 0, 0),
        BlockFace.Top => (0, 1, 0),
        BlockFace.Bottom => (0, -1, 0),
        BlockFace.Front => (0, 0, 1),
        BlockFace.Back => (0, 0, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    private static Vector3 FaceNormal(BlockFace face) => face switch
    {
        BlockFace.Right => Vector3.Right,
        BlockFace.Left => Vector3.Left,
        BlockFace.Top => Vector3.Up,
        BlockFace.Bottom => Vector3.Down,
        BlockFace.Front => Vector3.Back,
        BlockFace.Back => Vector3.Forward,
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    private static Vector3[] FaceCorners(BlockFace face) => face switch
    {
        BlockFace.Right => FacePositiveX,
        BlockFace.Left => FaceNegativeX,
        BlockFace.Top => FacePositiveY,
        BlockFace.Bottom => FaceNegativeY,
        BlockFace.Front => FacePositiveZ,
        BlockFace.Back => FaceNegativeZ,
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    private static Color ToGodotColor(BlockPreviewColor color) =>
        new(color.Red / 255f, color.Green / 255f, color.Blue / 255f, 1f);

    private readonly record struct TerrainFaceMaterial(
        BlockFace SourceFace,
        Vector2 EncodedLayers,
        Color Tint,
        bool RotateTexture);

    private static readonly Vector3[] FacePositiveX =
    [
        new(1, 0, 0), new(1, 1, 0), new(1, 1, 1), new(1, 0, 1),
    ];

    private static readonly Vector3[] FaceNegativeX =
    [
        new(0, 0, 1), new(0, 1, 1), new(0, 1, 0), new(0, 0, 0),
    ];

    private static readonly Vector3[] FacePositiveY =
    [
        new(0, 1, 1), new(1, 1, 1), new(1, 1, 0), new(0, 1, 0),
    ];

    private static readonly Vector3[] FaceNegativeY =
    [
        new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1),
    ];

    private static readonly Vector3[] FacePositiveZ =
    [
        new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1),
    ];

    private static readonly Vector3[] FaceNegativeZ =
    [
        new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0),
    ];
}
