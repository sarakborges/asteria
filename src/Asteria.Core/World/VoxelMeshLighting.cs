namespace Asteria.Core.World;

public readonly record struct VoxelVertexLighting(
    float Sky,
    float BlockRed,
    float BlockGreen,
    float BlockBlue,
    float AmbientOcclusion);

public readonly record struct VoxelFaceLighting(
    VoxelVertexLighting Corner0,
    VoxelVertexLighting Corner1,
    VoxelVertexLighting Corner2,
    VoxelVertexLighting Corner3)
{
    public VoxelVertexLighting this[int index] => index switch
    {
        0 => Corner0,
        1 => Corner1,
        2 => Corner2,
        3 => Corner3,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public bool ShouldFlipDiagonal
    {
        get
        {
            var balance =
                Corner0.AmbientOcclusion +
                Corner2.AmbientOcclusion -
                Corner1.AmbientOcclusion -
                Corner3.AmbientOcclusion;

            return balance > 0.001f;
        }
    }
}

public static class VoxelMeshLighting
{
    private const float AoLevel0 = 1.00f;
    private const float AoLevel1 = 0.86f;
    private const float AoLevel2 = 0.72f;
    private const float AoLevel3 = 0.58f;

    public static VoxelFaceLighting SampleFace(
        Chunk chunk,
        BlockRegistry blocks,
        int x,
        int y,
        int z,
        BlockFace face)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(blocks);

        var basis = GetFaceBasis(face);

        return new VoxelFaceLighting(
            SampleCorner(chunk, blocks, x, y, z, face, basis, basis.Corner0),
            SampleCorner(chunk, blocks, x, y, z, face, basis, basis.Corner1),
            SampleCorner(chunk, blocks, x, y, z, face, basis, basis.Corner2),
            SampleCorner(chunk, blocks, x, y, z, face, basis, basis.Corner3));
    }

    public static VoxelFaceLighting SampleFace(
        VoxelWorld world,
        BlockRegistry blocks,
        WorldVoxelCoord position,
        BlockFace face)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);

        var basis = GetFaceBasis(face);

        return new VoxelFaceLighting(
            SampleCorner(world, blocks, position, basis, basis.Corner0),
            SampleCorner(world, blocks, position, basis, basis.Corner1),
            SampleCorner(world, blocks, position, basis, basis.Corner2),
            SampleCorner(world, blocks, position, basis, basis.Corner3));
    }

    public static float OccupancyFraction(
        VoxelWorld world,
        BlockRegistry blocks,
        WorldVoxelCoord position)
    {
        if (!world.TryGetCell(position, out var cell) || cell.IsEmpty)
        {
            return 0f;
        }

        return OccupancyFraction(
            cell,
            blocks.GetDefinition(cell.Block),
            world.GetMicroblockMaskOrEmpty(position));
    }

    public static float OccupancyFraction(
        Chunk chunk,
        BlockRegistry blocks,
        int x,
        int y,
        int z)
    {
        var cell = chunk.GetCellOrEmpty(x, y, z);
        if (cell.IsEmpty)
        {
            return 0f;
        }

        return OccupancyFraction(
            chunk,
            blocks,
            x,
            y,
            z,
            cell,
            blocks.GetDefinition(cell.Block));
    }

    internal static float OccupancyFraction(
        Chunk chunk,
        BlockRegistry blocks,
        int x,
        int y,
        int z,
        VoxelCell cell,
        BlockDefinition definition) =>
        OccupancyFraction(
            cell,
            definition,
            chunk.GetMicroblockMask(x, y, z));

    internal static float OccupancyFraction(
        VoxelCell cell,
        BlockDefinition definition,
        MicroblockMask mask)
    {
        if (cell.IsEmpty)
        {
            return 0f;
        }

        if (definition.Visual.Kind ==
            BlockVisualKind.CrossedSprite)
        {
            return 0f;
        }

        if (cell.HasMicroblockGeometry)
        {
            return mask.OccupiedCount /
                   (float)MicroblockMask.CellCount;
        }

        return definition.Shape.Kind switch
        {
            BlockShapeKind.Cube => 1f,
            BlockShapeKind.Layer => Math.Clamp(definition.Shape.Thickness, 0f, 1f),
            BlockShapeKind.Hollow => HollowOccupancy(definition.Shape.WallThickness),
            _ => 1f,
        };
    }

    private static VoxelVertexLighting SampleCorner(
        Chunk chunk,
        BlockRegistry blocks,
        int x,
        int y,
        int z,
        BlockFace face,
        FaceBasis basis,
        CornerSigns signs)
    {
        var baseX = x + basis.Normal.X;
        var baseY = y + basis.Normal.Y;
        var baseZ = z + basis.Normal.Z;

        var sideA = Sample(
            chunk,
            blocks,
            baseX + basis.TangentA.X * signs.A,
            baseY + basis.TangentA.Y * signs.A,
            baseZ + basis.TangentA.Z * signs.A,
            face);

        var sideB = Sample(
            chunk,
            blocks,
            baseX + basis.TangentB.X * signs.B,
            baseY + basis.TangentB.Y * signs.B,
            baseZ + basis.TangentB.Z * signs.B,
            face);

        var corner = Sample(
            chunk,
            blocks,
            baseX + basis.TangentA.X * signs.A + basis.TangentB.X * signs.B,
            baseY + basis.TangentA.Y * signs.A + basis.TangentB.Y * signs.B,
            baseZ + basis.TangentA.Z * signs.A + basis.TangentB.Z * signs.B,
            face);

        var center = Sample(chunk, blocks, baseX, baseY, baseZ, face);

        var occlusion = sideA.Occupancy >= 0.999f && sideB.Occupancy >= 0.999f
            ? 3f
            : sideA.Occupancy + sideB.Occupancy + corner.Occupancy;

        var ao = AoBrightness(occlusion);
        var (sky, red, green, blue) = AverageLight(center, sideA, sideB, corner);

        return new VoxelVertexLighting(
            sky / VoxelLight.MaxLevel,
            red / VoxelLight.MaxLevel,
            green / VoxelLight.MaxLevel,
            blue / VoxelLight.MaxLevel,
            ao);
    }

    private static LightingSample Sample(
        Chunk chunk,
        BlockRegistry blocks,
        int x,
        int y,
        int z,
        BlockFace face)
    {
        if (!Chunk.Contains(x, y, z))
        {
            // Until multi-chunk residency exists, only the missing chunk above is
            // provisional open sky. Other missing neighbors contribute neither
            // darkness nor occlusion to the average.
            return face == BlockFace.Top && y >= Chunk.Size
                ? new LightingSample(true, new VoxelLight(VoxelLight.MaxLevel, 0, 0, 0), 0f)
                : default;
        }

        return new LightingSample(
            true,
            chunk.GetLight(x, y, z),
            OccupancyFraction(chunk, blocks, x, y, z));
    }

    private static VoxelVertexLighting SampleCorner(
        VoxelWorld world,
        BlockRegistry blocks,
        WorldVoxelCoord position,
        FaceBasis basis,
        CornerSigns signs)
    {
        var basePosition = new WorldVoxelCoord(
            position.X + basis.Normal.X,
            position.Y + basis.Normal.Y,
            position.Z + basis.Normal.Z);

        var sideA = Sample(
            world,
            blocks,
            basePosition + (
                basis.TangentA.X * signs.A,
                basis.TangentA.Y * signs.A,
                basis.TangentA.Z * signs.A));

        var sideB = Sample(
            world,
            blocks,
            basePosition + (
                basis.TangentB.X * signs.B,
                basis.TangentB.Y * signs.B,
                basis.TangentB.Z * signs.B));

        var corner = Sample(
            world,
            blocks,
            basePosition + (
                basis.TangentA.X * signs.A + basis.TangentB.X * signs.B,
                basis.TangentA.Y * signs.A + basis.TangentB.Y * signs.B,
                basis.TangentA.Z * signs.A + basis.TangentB.Z * signs.B));

        var center = Sample(world, blocks, basePosition);

        var occlusion = sideA.Occupancy >= 0.999f &&
                        sideB.Occupancy >= 0.999f
            ? 3f
            : sideA.Occupancy +
              sideB.Occupancy +
              corner.Occupancy;

        var ao = AoBrightness(occlusion);
        var (sky, red, green, blue) =
            AverageLight(center, sideA, sideB, corner);

        return new VoxelVertexLighting(
            sky / VoxelLight.MaxLevel,
            red / VoxelLight.MaxLevel,
            green / VoxelLight.MaxLevel,
            blue / VoxelLight.MaxLevel,
            ao);
    }

    private static LightingSample Sample(
        VoxelWorld world,
        BlockRegistry blocks,
        WorldVoxelCoord position)
    {
        if (!world.TryGetCell(position, out var cell))
        {
            // Missing residency is provisional open air for presentation.
            // When the neighbor becomes resident its halo is remeshed.
            return new LightingSample(
                true,
                new VoxelLight(VoxelLight.MaxLevel, 0, 0, 0),
                0f);
        }

        return new LightingSample(
            true,
            world.GetLightOrDark(position),
            cell.IsEmpty
                ? 0f
                : OccupancyFraction(
                    cell,
                    blocks.GetDefinition(cell.Block),
                    world.GetMicroblockMaskOrEmpty(position)));
    }

    private static (
        float Sky,
        float Red,
        float Green,
        float Blue) AverageLight(
        LightingSample center,
        LightingSample sideA,
        LightingSample sideB,
        LightingSample corner)
    {
        var sky = 0f;
        var red = 0f;
        var green = 0f;
        var blue = 0f;
        var weightTotal = 0f;

        AccumulateLight(
            center,
            ref sky,
            ref red,
            ref green,
            ref blue,
            ref weightTotal);
        AccumulateLight(
            sideA,
            ref sky,
            ref red,
            ref green,
            ref blue,
            ref weightTotal);
        AccumulateLight(
            sideB,
            ref sky,
            ref red,
            ref green,
            ref blue,
            ref weightTotal);
        AccumulateLight(
            corner,
            ref sky,
            ref red,
            ref green,
            ref blue,
            ref weightTotal);

        if (weightTotal <= float.Epsilon)
        {
            return (0f, 0f, 0f, 0f);
        }

        return (
            sky / weightTotal,
            red / weightTotal,
            green / weightTotal,
            blue / weightTotal);
    }

    private static void AccumulateLight(
        LightingSample sample,
        ref float sky,
        ref float red,
        ref float green,
        ref float blue,
        ref float weightTotal)
    {
        if (!sample.Loaded)
        {
            return;
        }

        var weight =
            1f - sample.Occupancy;

        if (weight <= float.Epsilon)
        {
            return;
        }

        sky += sample.Light.Sky * weight;
        red += sample.Light.Red * weight;
        green += sample.Light.Green * weight;
        blue += sample.Light.Blue * weight;
        weightTotal += weight;
    }

    private static float HollowOccupancy(float wallThickness)
    {
        var inner = Math.Clamp(1f - 2f * wallThickness, 0f, 1f);
        return 1f - inner * inner;
    }

    private static float AoBrightness(
        float occlusion)
    {
        var clamped =
            Math.Clamp(
                occlusion,
                0f,
                3f);
        var lower =
            (int)MathF.Floor(
                clamped);

        if (lower >= 3)
        {
            return AoLevel3;
        }

        var lowerLevel =
            lower switch
            {
                0 => AoLevel0,
                1 => AoLevel1,
                2 => AoLevel2,
                _ => AoLevel3,
            };
        var upperLevel =
            lower switch
            {
                0 => AoLevel1,
                1 => AoLevel2,
                _ => AoLevel3,
            };
        var fraction =
            clamped - lower;

        return lowerLevel *
                   (1f - fraction) +
               upperLevel *
                   fraction;
    }

    private static readonly FaceBasis RightBasis =
        new(
            new Axis(1, 0, 0),
            new Axis(0, 1, 0),
            new Axis(0, 0, 1),
            new CornerSigns(-1, -1),
            new CornerSigns(1, -1),
            new CornerSigns(1, 1),
            new CornerSigns(-1, 1));

    private static readonly FaceBasis LeftBasis =
        new(
            new Axis(-1, 0, 0),
            new Axis(0, 1, 0),
            new Axis(0, 0, 1),
            new CornerSigns(-1, 1),
            new CornerSigns(1, 1),
            new CornerSigns(1, -1),
            new CornerSigns(-1, -1));

    private static readonly FaceBasis TopBasis =
        new(
            new Axis(0, 1, 0),
            new Axis(1, 0, 0),
            new Axis(0, 0, 1),
            new CornerSigns(-1, 1),
            new CornerSigns(1, 1),
            new CornerSigns(1, -1),
            new CornerSigns(-1, -1));

    private static readonly FaceBasis BottomBasis =
        new(
            new Axis(0, -1, 0),
            new Axis(1, 0, 0),
            new Axis(0, 0, 1),
            new CornerSigns(-1, -1),
            new CornerSigns(1, -1),
            new CornerSigns(1, 1),
            new CornerSigns(-1, 1));

    private static readonly FaceBasis FrontBasis =
        new(
            new Axis(0, 0, 1),
            new Axis(1, 0, 0),
            new Axis(0, 1, 0),
            new CornerSigns(1, -1),
            new CornerSigns(1, 1),
            new CornerSigns(-1, 1),
            new CornerSigns(-1, -1));

    private static readonly FaceBasis BackBasis =
        new(
            new Axis(0, 0, -1),
            new Axis(1, 0, 0),
            new Axis(0, 1, 0),
            new CornerSigns(-1, -1),
            new CornerSigns(-1, 1),
            new CornerSigns(1, 1),
            new CornerSigns(1, -1));

    private static FaceBasis GetFaceBasis(
        BlockFace face) =>
        face switch
        {
            BlockFace.Right => RightBasis,
            BlockFace.Left => LeftBasis,
            BlockFace.Top => TopBasis,
            BlockFace.Bottom => BottomBasis,
            BlockFace.Front => FrontBasis,
            BlockFace.Back => BackBasis,
            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private readonly record struct Axis(
        int X,
        int Y,
        int Z);

    private readonly record struct CornerSigns(
        int A,
        int B);

    private readonly record struct FaceBasis(
        Axis Normal,
        Axis TangentA,
        Axis TangentB,
        CornerSigns Corner0,
        CornerSigns Corner1,
        CornerSigns Corner2,
        CornerSigns Corner3);

    private readonly record struct LightingSample(
        bool Loaded,
        VoxelLight Light,
        float Occupancy);
}
