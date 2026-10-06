using System.Numerics;

namespace Asteria.Core.World;

public static class ChunkMeshDataBuilder
{
    private const int FineResolution = BlockGeometry.Resolution;
    private const int FinePlaneArea = FineResolution * FineResolution;
    private const float DyableLayerFlag = 0.25f;

    // Asteria render mesh data uses clockwise front-face winding.
    private static readonly int[] TriangleOrder = [0, 2, 1, 0, 3, 2];
    private static readonly int[] FlippedTriangleOrder = [0, 3, 1, 1, 3, 2];
    private static readonly int[] SpriteFrontOrder = [0, 2, 1, 0, 3, 2];
    private static readonly int[] SpriteBackOrder = [0, 1, 2, 0, 2, 3];
    private static readonly Vector2[] SpriteUvs =
    [
        new(0f, 1f),
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
    ];

    private static readonly BlockFace[] Faces =
    [
        BlockFace.Right,
        BlockFace.Left,
        BlockFace.Top,
        BlockFace.Bottom,
        BlockFace.Front,
        BlockFace.Back,
    ];

    public static ChunkMeshData BuildMeshlet(
        VoxelWorld world,
        ChunkCoord coord,
        BlockRegistry blocks,
        TerrainTextureLookup textures,
        int meshletIndex,
        BiomeTintSampleGrid? tintSamples = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(textures);

        var chunk = world.GetChunk(coord);
        var surfaces =
            new Dictionary<TerrainRenderBatch, List<ChunkMeshVertex>>();
        var collision = new List<Vector3>(1024);
        var bounds = ChunkMeshletMask.Bounds(meshletIndex);
        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(coord);

        // Partial geometry keeps the correctness-first fine mesher. Ordinary
        // cubes are emitted in a separate face-by-face greedy pass below.
        for (var y = bounds.MinY; y < bounds.MaxYExclusive; y++)
        {
            for (var z = bounds.MinZ; z < bounds.MaxZExclusive; z++)
            {
                for (var x = bounds.MinX; x < bounds.MaxXExclusive; x++)
                {
                    var cell = chunk.GetCell(x, y, z);
                    if (cell.IsEmpty)
                    {
                        continue;
                    }

                    var definition = blocks.GetDefinition(cell.Block);
                    var worldPosition = new WorldVoxelCoord(
                        originX + x,
                        originY + y,
                        originZ + z);

                    if (definition.Visual.Kind ==
                        BlockVisualKind.CrossedSprite)
                    {
                        EmitCrossedSprite(
                            surfaces,
                            world,
                            blocks,
                            textures,
                            x,
                            y,
                            z,
                            worldPosition,
                            definition,
                            tintSamples);
                        continue;
                    }

                    if (!RequiresFineMeshing(
                            world,
                            blocks,
                            worldPosition,
                            cell,
                            definition))
                    {
                        continue;
                    }

                    EmitFineCell(
                        surfaces,
                        collision,
                        world,
                        blocks,
                        textures,
                        x,
                        y,
                        z,
                        worldPosition,
                        cell,
                        definition,
                        tintSamples);
                }
            }
        }

        EmitGreedyCubeFaces(
            surfaces,
            collision,
            world,
            coord,
            blocks,
            textures,
            bounds,
            tintSamples);

        var batches = surfaces
            .Where(entry => entry.Value.Count > 0)
            .OrderBy(entry => entry.Key)
            .Select(entry =>
                new ChunkRenderBatchData(
                    entry.Key,
                    entry.Value.ToArray()))
            .ToArray();

        return new ChunkMeshData(
            batches,
            collision.ToArray());
    }

    private static void EmitGreedyCubeFaces(
        Dictionary<TerrainRenderBatch, List<ChunkMeshVertex>> surfaces,
        List<Vector3> collision,
        VoxelWorld world,
        ChunkCoord coord,
        BlockRegistry blocks,
        TerrainTextureLookup textures,
        (
            int MinX,
            int MinY,
            int MinZ,
            int MaxXExclusive,
            int MaxYExclusive,
            int MaxZExclusive) bounds,
        BiomeTintSampleGrid? tintSamples)
    {
        var chunk = world.GetChunk(coord);
        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(coord);

        foreach (var face in Faces)
        {
            var plane = PlaneBounds(face, bounds);
            var width = plane.UMaxExclusive - plane.UMin;
            var height = plane.VMaxExclusive - plane.VMin;
            var candidates =
                new GreedyCubeFace?[width * height];

            for (var depth = plane.DepthMin;
                 depth < plane.DepthMaxExclusive;
                 depth++)
            {
                Array.Clear(candidates);

                for (var v = plane.VMin;
                     v < plane.VMaxExclusive;
                     v++)
                {
                    for (var u = plane.UMin;
                         u < plane.UMaxExclusive;
                         u++)
                    {
                        var (x, y, z) =
                            PlanePosition(face, depth, u, v);
                        var cell = chunk.GetCell(x, y, z);

                        if (cell.IsEmpty)
                        {
                            continue;
                        }

                        var definition =
                            blocks.GetDefinition(cell.Block);

                        if (definition.Visual.Kind !=
                            BlockVisualKind.Geometry)
                        {
                            continue;
                        }

                        var worldPosition =
                            new WorldVoxelCoord(
                                originX + x,
                                originY + y,
                                originZ + z);

                        if (RequiresFineMeshing(
                                world,
                                blocks,
                                worldPosition,
                                cell,
                                definition))
                        {
                            continue;
                        }

                        var offset = FaceOffset(face);
                        if (!FaceIsExposed(
                                world,
                                blocks,
                                cell,
                                definition,
                                worldPosition + offset))
                        {
                            continue;
                        }

                        var faceMaterial =
                            ResolveFaceMaterial(
                                textures,
                                definition,
                                cell,
                                face);
                        var lighting =
                            VoxelMeshLighting.SampleFace(
                                world,
                                blocks,
                                worldPosition,
                                face);
                        var batch = new TerrainRenderBatch(
                            definition.RenderMode,
                            definition.CastsShadow);
                        var uvRotation =
                            BlockUvRotation.ForWorldFace(
                                face,
                                cell.Orientation,
                                cell.TextureRotation,
                                faceMaterial.RotateTexture);

                        if (!BlockRenderModePolicy.CanGreedyMerge(
                                definition.RenderMode) ||
                            !TryUniformLighting(
                                lighting,
                                out var uniformLighting))
                        {
                            EmitCubeFace(
                                GetSurface(surfaces, batch),
                                collision,
                                new Vector3(x, y, z),
                                face,
                                cell,
                                faceMaterial,
                                lighting,
                                definition.IsCollidable,
                                originX,
                                originZ,
                                tintSamples);
                            continue;
                        }

                        candidates[
                            (u - plane.UMin) +
                            (v - plane.VMin) * width] =
                            new GreedyCubeFace(
                                batch,
                                faceMaterial,
                                uvRotation,
                                uniformLighting,
                                definition.IsCollidable);
                    }
                }

                EmitGreedyCubePlane(
                    surfaces,
                    collision,
                    face,
                    depth,
                    plane,
                    candidates,
                    originX,
                    originZ,
                    tintSamples);
            }
        }
    }

    private static void EmitGreedyCubePlane(
        Dictionary<TerrainRenderBatch, List<ChunkMeshVertex>> surfaces,
        List<Vector3> collision,
        BlockFace face,
        int depth,
        PlaneRange plane,
        GreedyCubeFace?[] candidates,
        int worldOriginX,
        int worldOriginZ,
        BiomeTintSampleGrid? tintSamples)
    {
        var planeWidth =
            plane.UMaxExclusive - plane.UMin;
        var planeHeight =
            plane.VMaxExclusive - plane.VMin;

        for (var localV = 0;
             localV < planeHeight;
             localV++)
        {
            for (var localU = 0;
                 localU < planeWidth;
                 localU++)
            {
                var index =
                    localU + localV * planeWidth;
                var candidate = candidates[index];

                if (candidate is null)
                {
                    continue;
                }

                var rectangleWidth = 1;
                while (localU + rectangleWidth <
                           planeWidth &&
                       candidates[
                           index + rectangleWidth] ==
                       candidate)
                {
                    rectangleWidth++;
                }

                var rectangleHeight = 1;
                while (localV + rectangleHeight <
                       planeHeight)
                {
                    var compatible = true;
                    for (var column = 0;
                         column < rectangleWidth;
                         column++)
                    {
                        if (candidates[
                                localU + column +
                                (localV + rectangleHeight) *
                                planeWidth] !=
                            candidate)
                        {
                            compatible = false;
                            break;
                        }
                    }

                    if (!compatible)
                    {
                        break;
                    }

                    rectangleHeight++;
                }

                for (var row = 0;
                     row < rectangleHeight;
                     row++)
                {
                    for (var column = 0;
                         column < rectangleWidth;
                         column++)
                    {
                        candidates[
                            localU + column +
                            (localV + row) * planeWidth] = null;
                    }
                }

                EmitCubeRectangle(
                    GetSurface(
                        surfaces,
                        candidate.Value.Batch),
                    collision,
                    face,
                    depth,
                    plane.UMin + localU,
                    plane.VMin + localV,
                    rectangleWidth,
                    rectangleHeight,
                    candidate.Value,
                    worldOriginX,
                    worldOriginZ,
                    tintSamples);
            }
        }
    }

    private static void EmitCubeRectangle(
        List<ChunkMeshVertex> surface,
        List<Vector3> collision,
        BlockFace face,
        int depth,
        int u,
        int v,
        int width,
        int height,
        GreedyCubeFace candidate,
        int worldOriginX,
        int worldOriginZ,
        BiomeTintSampleGrid? tintSamples)
    {
        var (lower, upper) =
            CubeRectangleBounds(
                face,
                depth,
                u,
                v,
                width,
                height);
        var corners = FaceCorners(face);
        var normal = FaceNormal(face);
        Span<Vector3> positions = stackalloc Vector3[4];

        for (var corner = 0; corner < 4; corner++)
        {
            var selector = corners[corner];
            positions[corner] = new Vector3(
                selector.X == 0f ? lower.X : upper.X,
                selector.Y == 0f ? lower.Y : upper.Y,
                selector.Z == 0f ? lower.Z : upper.Z);
        }

        EmitQuadVertices(
            surface,
            collision,
            positions,
            normal,
            face,
            candidate.FaceMaterial,
            candidate.UvRotation,
            new VoxelFaceLighting(
                candidate.Lighting,
                candidate.Lighting,
                candidate.Lighting,
                candidate.Lighting),
            candidate.IsCollidable,
            useAbsoluteUv: true,
            worldOriginX: worldOriginX,
            worldOriginZ: worldOriginZ,
            tintSamples: tintSamples);
    }

    private static bool RequiresFineMeshing(
        VoxelWorld world,
        BlockRegistry blocks,
        WorldVoxelCoord position,
        VoxelCell cell,
        BlockDefinition definition)
    {
        if (BlockGeometry.RequiresFineMeshing(
                definition,
                cell))
        {
            return true;
        }

        foreach (var face in Faces)
        {
            var offset = FaceOffset(face);
            var neighbor =
                world.GetCellOrEmpty(
                    position + offset);

            if (neighbor.IsEmpty)
            {
                continue;
            }

            var neighborDefinition =
                blocks.GetDefinition(neighbor.Block);

            if (BlockGeometry.RequiresFineMeshing(
                    neighborDefinition,
                    neighbor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FaceIsExposed(
        VoxelWorld world,
        BlockRegistry blocks,
        VoxelCell source,
        BlockDefinition sourceDefinition,
        WorldVoxelCoord neighborPosition)
    {
        var neighbor =
            world.GetCellOrEmpty(neighborPosition);

        if (neighbor.IsEmpty)
        {
            return true;
        }

        var neighborDefinition =
            blocks.GetDefinition(neighbor.Block);

        if (neighborDefinition.Visual.Kind !=
            BlockVisualKind.Geometry)
        {
            return true;
        }

        return !Occludes(
            source,
            sourceDefinition,
            neighbor,
            neighborDefinition);
    }

    private static void EmitCrossedSprite(
        Dictionary<TerrainRenderBatch, List<ChunkMeshVertex>> surfaces,
        VoxelWorld world,
        BlockRegistry blocks,
        TerrainTextureLookup textures,
        int blockX,
        int blockY,
        int blockZ,
        WorldVoxelCoord worldPosition,
        BlockDefinition definition,
        BiomeTintSampleGrid? tintSamples)
    {
        var visual = definition.Visual;
        var texture =
            visual.Texture ??
            throw new InvalidOperationException(
                $"Crossed-sprite block {definition.Id} is missing its visual texture.");
        var baseCode =
            textures.GetIndex(
                texture.Texture) +
            (texture.Dyable
                ? DyableLayerFlag
                : 0f);
        var tint =
            ResolveTint(
                definition.Tint,
                definition.PreviewColor,
                tintSamples,
                worldPosition.X,
                worldPosition.Z);
        var encodedLayers =
            new Vector2(
                baseCode,
                -1f);
        var batch =
            new TerrainRenderBatch(
                definition.RenderMode,
                definition.CastsShadow);
        var surface =
            GetSurface(
                surfaces,
                batch);
        var lighting =
            VoxelMeshLighting.SampleFace(
                world,
                blocks,
                worldPosition,
                BlockFace.Top);
        var centerX =
            blockX + 0.5f;
        var centerZ =
            blockZ + 0.5f;
        var bottomY =
            blockY +
            visual.BaseOffset;
        var topY =
            bottomY +
            visual.Height;
        var halfWidth =
            visual.Width * 0.5f;

        Span<Vector3> positions =
            stackalloc Vector3[4];

        for (var plane = 0;
             plane < visual.Planes;
             plane++)
        {
            var angle =
                MathF.PI *
                plane /
                visual.Planes;
            var offsetX =
                MathF.Cos(angle) *
                halfWidth;
            var offsetZ =
                MathF.Sin(angle) *
                halfWidth;
            positions[0] =
                new Vector3(
                    centerX - offsetX,
                    bottomY,
                    centerZ - offsetZ);
            positions[1] =
                new Vector3(
                    centerX - offsetX,
                    topY,
                    centerZ - offsetZ);
            positions[2] =
                new Vector3(
                    centerX + offsetX,
                    topY,
                    centerZ + offsetZ);
            positions[3] =
                new Vector3(
                    centerX + offsetX,
                    bottomY,
                    centerZ + offsetZ);

            EmitSpriteSide(
                surface,
                positions,
                SpriteFrontOrder,
                encodedLayers,
                tint,
                lighting);
            EmitSpriteSide(
                surface,
                positions,
                SpriteBackOrder,
                encodedLayers,
                tint,
                lighting);
        }
    }

    private static void EmitSpriteSide(
        List<ChunkMeshVertex> surface,
        ReadOnlySpan<Vector3> positions,
        IReadOnlyList<int> triangleOrder,
        Vector2 encodedLayers,
        Vector3 tint,
        VoxelFaceLighting lighting)
    {
        foreach (var index in triangleOrder)
        {
            var vertexLighting =
                lighting[index];

            surface.Add(
                new ChunkMeshVertex(
                    positions[index],
                    Vector3.UnitY,
                    SpriteUvs[index],
                    encodedLayers,
                    new Vector4(
                        tint.X,
                        tint.Y,
                        tint.Z,
                        vertexLighting.AmbientOcclusion),
                    new Vector4(
                        vertexLighting.Sky,
                        vertexLighting.BlockRed,
                        vertexLighting.BlockGreen,
                        vertexLighting.BlockBlue)));
        }
    }

    private static void EmitCubeFace(
        List<ChunkMeshVertex> surface,
        List<Vector3> collision,
        Vector3 origin,
        BlockFace face,
        VoxelCell cell,
        TerrainFaceMaterial faceMaterial,
        VoxelFaceLighting faceLighting,
        bool isCollidable,
        int worldOriginX,
        int worldOriginZ,
        BiomeTintSampleGrid? tintSamples)
    {
        var corners = FaceCorners(face);
        Span<Vector3> positions = stackalloc Vector3[4];

        for (var index = 0; index < 4; index++)
        {
            positions[index] =
                origin + corners[index];
        }

        var uvRotation =
            BlockUvRotation.ForWorldFace(
                face,
                cell.Orientation,
                cell.TextureRotation,
                faceMaterial.RotateTexture);

        EmitQuadVertices(
            surface,
            collision,
            positions,
            FaceNormal(face),
            face,
            faceMaterial,
            uvRotation,
            faceLighting,
            isCollidable,
            useAbsoluteUv: false,
            uvOrigin: origin,
            worldOriginX: worldOriginX,
            worldOriginZ: worldOriginZ,
            tintSamples: tintSamples);
    }

    private static void EmitFineCell(
        Dictionary<TerrainRenderBatch, List<ChunkMeshVertex>> surfaces,
        List<Vector3> collision,
        VoxelWorld world,
        BlockRegistry blocks,
        TerrainTextureLookup textures,
        int blockX,
        int blockY,
        int blockZ,
        WorldVoxelCoord worldPosition,
        VoxelCell cell,
        BlockDefinition definition,
        BiomeTintSampleGrid? tintSamples)
    {
        var origin = new Vector3(
            blockX,
            blockY,
            blockZ);
        var sourceMask =
            world.GetMicroblockMaskOrEmpty(worldPosition);
        var visible = new bool[FinePlaneArea];
        var batch = new TerrainRenderBatch(
            definition.RenderMode,
            definition.CastsShadow);
        var surface = GetSurface(surfaces, batch);

        foreach (var face in Faces)
        {
            var faceMaterial =
                ResolveFaceMaterial(
                    textures,
                    definition,
                    cell,
                    face);
            var faceLighting =
                VoxelMeshLighting.SampleFace(
                    world,
                    blocks,
                    worldPosition,
                    face);

            for (var depth = 0;
                 depth < FineResolution;
                 depth++)
            {
                Array.Clear(visible);

                for (var v = 0;
                     v < FineResolution;
                     v++)
                {
                    for (var u = 0;
                         u < FineResolution;
                         u++)
                    {
                        var (localX, localY, localZ) =
                            FinePosition(
                                face,
                                depth,
                                u,
                                v);

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
                                world,
                                blocks,
                                worldPosition,
                                localX,
                                localY,
                                localZ,
                                face,
                                cell,
                                definition))
                        {
                            continue;
                        }

                        visible[
                            u +
                            v * FineResolution] = true;
                    }
                }

                EmitGreedyFineRectangles(
                    surface,
                    collision,
                    origin,
                    face,
                    depth,
                    visible,
                    cell,
                    faceMaterial,
                    faceLighting,
                    definition.IsCollidable,
                    worldPosition.X -
                        blockX,
                    worldPosition.Z -
                        blockZ,
                    tintSamples);
            }
        }
    }

    private static bool FineNeighborOccludes(
        VoxelWorld world,
        BlockRegistry blocks,
        WorldVoxelCoord sourcePosition,
        int localX,
        int localY,
        int localZ,
        BlockFace face,
        VoxelCell source,
        BlockDefinition sourceDefinition)
    {
        var (dx, dy, dz) = FaceOffset(face);
        var neighborWorldX = sourcePosition.X;
        var neighborWorldY = sourcePosition.Y;
        var neighborWorldZ = sourcePosition.Z;
        var neighborLocalX = localX + dx;
        var neighborLocalY = localY + dy;
        var neighborLocalZ = localZ + dz;

        WrapFineCoordinate(
            ref neighborWorldX,
            ref neighborLocalX);
        WrapFineCoordinate(
            ref neighborWorldY,
            ref neighborLocalY);
        WrapFineCoordinate(
            ref neighborWorldZ,
            ref neighborLocalZ);

        var neighborPosition =
            new WorldVoxelCoord(
                neighborWorldX,
                neighborWorldY,
                neighborWorldZ);

        if (!world.TryGetCell(
                neighborPosition,
                out var neighbor) ||
            neighbor.IsEmpty)
        {
            return false;
        }

        var neighborDefinition =
            blocks.GetDefinition(neighbor.Block);
        var neighborMask =
            world.GetMicroblockMaskOrEmpty(
                neighborPosition);

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

        return Occludes(
            source,
            sourceDefinition,
            neighbor,
            neighborDefinition);
    }

    private static bool Occludes(
        VoxelCell source,
        BlockDefinition sourceDefinition,
        VoxelCell neighbor,
        BlockDefinition neighborDefinition) =>
        neighborDefinition.IsOpaque ||
        (source.Block == neighbor.Block &&
         sourceDefinition.RenderMode !=
             BlockRenderMode.Opaque);

    private static void WrapFineCoordinate(
        ref int block,
        ref int local)
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

    private static void EmitGreedyFineRectangles(
        List<ChunkMeshVertex> surface,
        List<Vector3> collision,
        Vector3 origin,
        BlockFace face,
        int depth,
        bool[] visible,
        VoxelCell cell,
        TerrainFaceMaterial faceMaterial,
        VoxelFaceLighting faceLighting,
        bool isCollidable,
        int worldOriginX,
        int worldOriginZ,
        BiomeTintSampleGrid? tintSamples)
    {
        for (var v = 0;
             v < FineResolution;
             v++)
        {
            for (var u = 0;
                 u < FineResolution;
                 u++)
            {
                if (!visible[
                        u +
                        v * FineResolution])
                {
                    continue;
                }

                var width = 1;
                while (u + width <
                           FineResolution &&
                       visible[
                           u + width +
                           v * FineResolution])
                {
                    width++;
                }

                var height = 1;
                while (v + height <
                       FineResolution)
                {
                    var rowVisible = true;

                    for (var column = u;
                         column < u + width;
                         column++)
                    {
                        if (!visible[
                                column +
                                (v + height) *
                                FineResolution])
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

                for (var row = v;
                     row < v + height;
                     row++)
                {
                    for (var column = u;
                         column < u + width;
                         column++)
                    {
                        visible[
                            column +
                            row * FineResolution] = false;
                    }
                }

                EmitFineRectangle(
                    surface,
                    collision,
                    origin,
                    face,
                    depth,
                    u,
                    v,
                    width,
                    height,
                    cell,
                    faceMaterial,
                    faceLighting,
                    isCollidable,
                    worldOriginX,
                    worldOriginZ,
                    tintSamples);
            }
        }
    }

    private static void EmitFineRectangle(
        List<ChunkMeshVertex> surface,
        List<Vector3> collision,
        Vector3 origin,
        BlockFace face,
        int depth,
        int u,
        int v,
        int width,
        int height,
        VoxelCell cell,
        TerrainFaceMaterial faceMaterial,
        VoxelFaceLighting faceLighting,
        bool isCollidable,
        int worldOriginX,
        int worldOriginZ,
        BiomeTintSampleGrid? tintSamples)
    {
        var (minX, minY, minZ) =
            FinePosition(
                face,
                depth,
                u,
                v);
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
                throw new ArgumentOutOfRangeException(
                    nameof(face));
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
        var lower = new Vector3(
            lowerX * scale,
            lowerY * scale,
            lowerZ * scale);
        var upper = new Vector3(
            upperX * scale,
            upperY * scale,
            upperZ * scale);
        var corners = FaceCorners(face);
        Span<Vector3> positions =
            stackalloc Vector3[4];

        for (var index = 0; index < 4; index++)
        {
            var selector = corners[index];
            positions[index] =
                origin +
                new Vector3(
                    selector.X == 0f
                        ? lower.X
                        : upper.X,
                    selector.Y == 0f
                        ? lower.Y
                        : upper.Y,
                    selector.Z == 0f
                        ? lower.Z
                        : upper.Z);
        }

        var uvRotation =
            BlockUvRotation.ForWorldFace(
                face,
                cell.Orientation,
                cell.TextureRotation,
                faceMaterial.RotateTexture);

        EmitQuadVertices(
            surface,
            collision,
            positions,
            FaceNormal(face),
            face,
            faceMaterial,
            uvRotation,
            faceLighting,
            isCollidable,
            useAbsoluteUv: false,
            uvOrigin: origin,
            worldOriginX: worldOriginX,
            worldOriginZ: worldOriginZ,
            tintSamples: tintSamples);
    }

    private static void EmitQuadVertices(
        List<ChunkMeshVertex> surface,
        List<Vector3> collision,
        ReadOnlySpan<Vector3> positions,
        Vector3 normal,
        BlockFace face,
        TerrainFaceMaterial faceMaterial,
        TextureRotation uvRotation,
        VoxelFaceLighting lighting,
        bool isCollidable,
        bool useAbsoluteUv,
        Vector3 uvOrigin = default,
        int worldOriginX = 0,
        int worldOriginZ = 0,
        BiomeTintSampleGrid? tintSamples = null)
    {
        var triangleOrder =
            lighting.ShouldFlipDiagonal
                ? FlippedTriangleOrder
                : TriangleOrder;

        foreach (var index in triangleOrder)
        {
            var position = positions[index];
            var uvPoint = useAbsoluteUv
                ? position
                : position - uvOrigin;
            var uv = RotateUv(
                MacroUv(face, uvPoint),
                uvRotation);
            var vertexLighting = lighting[index];
            var tint =
                ResolveTint(
                    faceMaterial.Tint,
                    faceMaterial.TintFallback,
                    tintSamples,
                    checked(
                        worldOriginX +
                        (int)MathF.Floor(
                            position.X)),
                    checked(
                        worldOriginZ +
                        (int)MathF.Floor(
                            position.Z)));

            surface.Add(
                new ChunkMeshVertex(
                    position,
                    normal,
                    uv,
                    faceMaterial.EncodedLayers,
                    new Vector4(
                        tint.X,
                        tint.Y,
                        tint.Z,
                        vertexLighting.AmbientOcclusion),
                    new Vector4(
                        vertexLighting.Sky,
                        vertexLighting.BlockRed,
                        vertexLighting.BlockGreen,
                        vertexLighting.BlockBlue)));

            if (isCollidable)
            {
                collision.Add(position);
            }
        }
    }

    private static bool TryUniformLighting(
        VoxelFaceLighting lighting,
        out VoxelVertexLighting uniform)
    {
        uniform = lighting.Corner0;
        return lighting.Corner1 == uniform &&
               lighting.Corner2 == uniform &&
               lighting.Corner3 == uniform;
    }

    private static List<ChunkMeshVertex> GetSurface(
        Dictionary<TerrainRenderBatch, List<ChunkMeshVertex>> surfaces,
        TerrainRenderBatch batch)
    {
        if (!surfaces.TryGetValue(
                batch,
                out var surface))
        {
            surface =
                new List<ChunkMeshVertex>(1024);
            surfaces.Add(batch, surface);
        }

        return surface;
    }

    private static TerrainFaceMaterial ResolveFaceMaterial(
        TerrainTextureLookup textures,
        BlockDefinition definition,
        VoxelCell cell,
        BlockFace worldFace)
    {
        var sourceFace =
            BlockFaceTransform.SourceFaceForWorldFace(
                worldFace,
                cell,
                definition);
        var layers =
            definition.Textures.ResolveForFace(
                sourceFace);

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
            baseCode =
                textures.GetIndex(
                    layers[0].Texture) +
                (layers[0].Dyable
                    ? DyableLayerFlag
                    : 0f);
        }

        if (layers.Count > 1)
        {
            overlayCode =
                textures.GetIndex(
                    layers[1].Texture) +
                (layers[1].Dyable
                    ? DyableLayerFlag
                    : 0f);
        }

        return new TerrainFaceMaterial(
            new Vector2(
                baseCode,
                overlayCode),
            definition.Tint,
            definition.PreviewColor,
            definition.RotateTexture.Rotates(
                sourceFace));
    }

    private static PlaneRange PlaneBounds(
        BlockFace face,
        (
            int MinX,
            int MinY,
            int MinZ,
            int MaxXExclusive,
            int MaxYExclusive,
            int MaxZExclusive) bounds) =>
        face switch
        {
            BlockFace.Right or
            BlockFace.Left =>
                new PlaneRange(
                    bounds.MinX,
                    bounds.MaxXExclusive,
                    bounds.MinZ,
                    bounds.MaxZExclusive,
                    bounds.MinY,
                    bounds.MaxYExclusive),

            BlockFace.Top or
            BlockFace.Bottom =>
                new PlaneRange(
                    bounds.MinY,
                    bounds.MaxYExclusive,
                    bounds.MinX,
                    bounds.MaxXExclusive,
                    bounds.MinZ,
                    bounds.MaxZExclusive),

            BlockFace.Front or
            BlockFace.Back =>
                new PlaneRange(
                    bounds.MinZ,
                    bounds.MaxZExclusive,
                    bounds.MinX,
                    bounds.MaxXExclusive,
                    bounds.MinY,
                    bounds.MaxYExclusive),

            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private static (int X, int Y, int Z) PlanePosition(
        BlockFace face,
        int depth,
        int u,
        int v) =>
        face switch
        {
            BlockFace.Right or
            BlockFace.Left =>
                (depth, v, u),

            BlockFace.Top or
            BlockFace.Bottom =>
                (u, depth, v),

            BlockFace.Front or
            BlockFace.Back =>
                (u, v, depth),

            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private static (Vector3 Lower, Vector3 Upper)
        CubeRectangleBounds(
            BlockFace face,
            int depth,
            int u,
            int v,
            int width,
            int height) =>
        face switch
        {
            BlockFace.Right =>
                (
                    new Vector3(
                        depth + 1,
                        v,
                        u),
                    new Vector3(
                        depth + 1,
                        v + height,
                        u + width)
                ),

            BlockFace.Left =>
                (
                    new Vector3(
                        depth,
                        v,
                        u),
                    new Vector3(
                        depth,
                        v + height,
                        u + width)
                ),

            BlockFace.Top =>
                (
                    new Vector3(
                        u,
                        depth + 1,
                        v),
                    new Vector3(
                        u + width,
                        depth + 1,
                        v + height)
                ),

            BlockFace.Bottom =>
                (
                    new Vector3(
                        u,
                        depth,
                        v),
                    new Vector3(
                        u + width,
                        depth,
                        v + height)
                ),

            BlockFace.Front =>
                (
                    new Vector3(
                        u,
                        v,
                        depth + 1),
                    new Vector3(
                        u + width,
                        v + height,
                        depth + 1)
                ),

            BlockFace.Back =>
                (
                    new Vector3(
                        u,
                        v,
                        depth),
                    new Vector3(
                        u + width,
                        v + height,
                        depth)
                ),

            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private static Vector2 MacroUv(
        BlockFace face,
        Vector3 point) =>
        face switch
        {
            BlockFace.Right =>
                new Vector2(
                    1f - point.Z,
                    1f - point.Y),
            BlockFace.Left =>
                new Vector2(
                    point.Z,
                    1f - point.Y),
            BlockFace.Top =>
                new Vector2(
                    point.X,
                    point.Z),
            BlockFace.Bottom =>
                new Vector2(
                    point.X,
                    1f - point.Z),
            BlockFace.Front =>
                new Vector2(
                    point.X,
                    1f - point.Y),
            BlockFace.Back =>
                new Vector2(
                    1f - point.X,
                    1f - point.Y),
            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private static Vector2 RotateUv(
        Vector2 uv,
        TextureRotation rotation) =>
        rotation switch
        {
            TextureRotation.Degrees0 =>
                uv,
            TextureRotation.Degrees90 =>
                new Vector2(
                    1f - uv.Y,
                    uv.X),
            TextureRotation.Degrees180 =>
                new Vector2(
                    1f - uv.X,
                    1f - uv.Y),
            TextureRotation.Degrees270 =>
                new Vector2(
                    uv.Y,
                    1f - uv.X),
            _ => throw new ArgumentOutOfRangeException(
                nameof(rotation)),
        };

    private static (int X, int Y, int Z) FinePosition(
        BlockFace face,
        int depth,
        int u,
        int v) =>
        face switch
        {
            BlockFace.Right or
            BlockFace.Left =>
                (depth, v, u),
            BlockFace.Top or
            BlockFace.Bottom =>
                (u, depth, v),
            BlockFace.Front or
            BlockFace.Back =>
                (u, v, depth),
            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private static (int X, int Y, int Z) FaceOffset(
        BlockFace face) =>
        face switch
        {
            BlockFace.Right => (1, 0, 0),
            BlockFace.Left => (-1, 0, 0),
            BlockFace.Top => (0, 1, 0),
            BlockFace.Bottom => (0, -1, 0),
            BlockFace.Front => (0, 0, 1),
            BlockFace.Back => (0, 0, -1),
            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private static Vector3 FaceNormal(
        BlockFace face) =>
        face switch
        {
            BlockFace.Right => Vector3.UnitX,
            BlockFace.Left => -Vector3.UnitX,
            BlockFace.Top => Vector3.UnitY,
            BlockFace.Bottom => -Vector3.UnitY,
            BlockFace.Front => Vector3.UnitZ,
            BlockFace.Back => -Vector3.UnitZ,
            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private static Vector3[] FaceCorners(
        BlockFace face) =>
        face switch
        {
            BlockFace.Right => FacePositiveX,
            BlockFace.Left => FaceNegativeX,
            BlockFace.Top => FacePositiveY,
            BlockFace.Bottom => FaceNegativeY,
            BlockFace.Front => FacePositiveZ,
            BlockFace.Back => FaceNegativeZ,
            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private static Vector3 ResolveTint(
        BlockTint tint,
        BlockPreviewColor fallback,
        BiomeTintSampleGrid? samples,
        int worldX,
        int worldZ)
    {
        if (tint == BlockTint.None)
        {
            return Vector3.One;
        }

        return samples?.Resolve(
                   tint,
                   fallback,
                   worldX,
                   worldZ) ??
               new Vector3(
                   fallback.Red / 255f,
                   fallback.Green / 255f,
                   fallback.Blue / 255f);
    }

    private readonly record struct TerrainFaceMaterial(
        Vector2 EncodedLayers,
        BlockTint Tint,
        BlockPreviewColor TintFallback,
        bool RotateTexture);

    private readonly record struct GreedyCubeFace(
        TerrainRenderBatch Batch,
        TerrainFaceMaterial FaceMaterial,
        TextureRotation UvRotation,
        VoxelVertexLighting Lighting,
        bool IsCollidable);

    private readonly record struct PlaneRange(
        int DepthMin,
        int DepthMaxExclusive,
        int UMin,
        int UMaxExclusive,
        int VMin,
        int VMaxExclusive);

    private static readonly Vector3[] FacePositiveX =
    [
        new(1, 0, 0),
        new(1, 1, 0),
        new(1, 1, 1),
        new(1, 0, 1),
    ];

    private static readonly Vector3[] FaceNegativeX =
    [
        new(0, 0, 1),
        new(0, 1, 1),
        new(0, 1, 0),
        new(0, 0, 0),
    ];

    private static readonly Vector3[] FacePositiveY =
    [
        new(0, 1, 1),
        new(1, 1, 1),
        new(1, 1, 0),
        new(0, 1, 0),
    ];

    private static readonly Vector3[] FaceNegativeY =
    [
        new(0, 0, 0),
        new(1, 0, 0),
        new(1, 0, 1),
        new(0, 0, 1),
    ];

    private static readonly Vector3[] FacePositiveZ =
    [
        new(1, 0, 1),
        new(1, 1, 1),
        new(0, 1, 1),
        new(0, 0, 1),
    ];

    private static readonly Vector3[] FaceNegativeZ =
    [
        new(0, 0, 0),
        new(0, 1, 0),
        new(1, 1, 0),
        new(1, 0, 0),
    ];
}
