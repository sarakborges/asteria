namespace Asteria.Core.World;

public readonly record struct ChunkNeighborMeshInvalidation(
    ChunkCoord Neighbor,
    ChunkMeshletMask Terrain,
    ChunkMeshletMask Fluid);

public static class ChunkTopologyFrontier
{
    public static IEnumerable<WorldVoxelCoord> LightingSeeds(
        ChunkCoord coord) =>
        LightingSeeds(coord, static _ => true);

    // Only a resident neighbor can exchange light with this chunk.
    // The two opposing faces are seeded together, including when the
    // source chunk was just retired and only the neighbor remains.
    public static IEnumerable<WorldVoxelCoord> LightingSeeds(
        ChunkCoord coord,
        Func<ChunkCoord, bool> neighborIsResident)
    {
        ArgumentNullException.ThrowIfNull(neighborIsResident);

        // A seed also visits its six neighboring voxels during propagation.
        // Seed only the resident side of each face: the newly inserted chunk
        // when present, or the surviving neighbor after retirement.
        var sourceIsResident = neighborIsResident(coord);
        var above =
            neighborIsResident(new ChunkCoord(coord.X, coord.Y + 1, coord.Z));
        var below =
            coord.Y > 0 &&
            neighborIsResident(new ChunkCoord(coord.X, coord.Y - 1, coord.Z));
        var negativeX =
            neighborIsResident(new ChunkCoord(coord.X - 1, coord.Y, coord.Z));
        var positiveX =
            neighborIsResident(new ChunkCoord(coord.X + 1, coord.Y, coord.Z));
        var negativeZ =
            neighborIsResident(new ChunkCoord(coord.X, coord.Y, coord.Z - 1));
        var positiveZ =
            neighborIsResident(new ChunkCoord(coord.X, coord.Y, coord.Z + 1));

        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(coord);

        if (above || below)
        {
            for (var z = 0; z < Chunk.Size; z++)
            {
                for (var x = 0; x < Chunk.Size; x++)
                {
                    if (above)
                    {
                        yield return new WorldVoxelCoord(
                            originX + x,
                            originY + (sourceIsResident ? Chunk.Size - 1 : Chunk.Size),
                            originZ + z);
                    }

                    if (below)
                    {
                        yield return new WorldVoxelCoord(
                            originX + x,
                            originY + (sourceIsResident ? 0 : -1),
                            originZ + z);
                    }
                }
            }
        }

        if (negativeX || positiveX)
        {
            for (var y = 0; y < Chunk.Size; y++)
            {
                for (var z = 0; z < Chunk.Size; z++)
                {
                    if (negativeX)
                    {
                        yield return new WorldVoxelCoord(
                            originX + (sourceIsResident ? 0 : -1),
                            originY + y, originZ + z);
                    }

                    if (positiveX)
                    {
                        yield return new WorldVoxelCoord(
                            originX + (sourceIsResident ? Chunk.Size - 1 : Chunk.Size),
                            originY + y, originZ + z);
                    }
                }
            }
        }

        if (negativeZ || positiveZ)
        {
            for (var y = 0; y < Chunk.Size; y++)
            {
                for (var x = 0; x < Chunk.Size; x++)
                {
                    if (negativeZ)
                    {
                        yield return new WorldVoxelCoord(
                            originX + x, originY + y,
                            originZ + (sourceIsResident ? 0 : -1));
                    }

                    if (positiveZ)
                    {
                        yield return new WorldVoxelCoord(
                            originX + x, originY + y,
                            originZ + (sourceIsResident ? Chunk.Size - 1 : Chunk.Size));
                    }
                }
            }
        }
    }

    public static bool NeedsSeamLightSeed(
        VoxelWorld world, BlockRegistry blocks, FluidRegistry fluids,
        WorldVoxelCoord position)
    {
        var address = VoxelCoordinates.FromWorld(position.X, position.Y, position.Z);
        if (address.Local.Y == 0 || address.Local.Y == Chunk.Size - 1 ||
            !world.TryGetChunk(address.Chunk, out var chunk))
            return true;

        var x = address.Local.X;
        var y = address.Local.Y;
        var z = address.Local.Z;
        if (!chunk.GetLight(x, y, z).IsDark)
            return true;

        var cell = chunk.GetCell(x, y, z);
        var fluid = chunk.GetFluid(x, y, z);
        var dampening = VoxelLightingMedium.Dampening(
            chunk, blocks, fluids, x, y, z, cell, fluid);
        var emission = VoxelLightingMedium.Emission(blocks, fluids, cell, fluid);
        return dampening < VoxelLight.MaxLevel ||
               !VoxelLight.FromEmission(emission).IsDark;
    }

    /// <summary>
    /// A newly resident chunk already has its own initialized lighting.
    /// On horizontal seams, channel differences of at most one cannot
    /// improve either voxel: all cross-voxel propagation attenuates by at
    /// least one. Avoid queuing those redundant seeds. Keep vertical frontiers,
    /// whose direct-sky state can change when a column gains a chunk, and
    /// preserve the original checks for chunk retirement.
    /// </summary>
    public static bool NeedsArrivalSeamLightSeed(
        VoxelWorld world, BlockRegistry blocks, FluidRegistry fluids,
        ChunkCoord arrivingChunk, WorldVoxelCoord position)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!NeedsSeamLightSeed(world, blocks, fluids, position))
            return false;

        var address = VoxelCoordinates.FromWorld(
            position.X, position.Y, position.Z);
        if (address.Chunk == arrivingChunk &&
            (address.Local.Y == 0 || address.Local.Y == Chunk.Size - 1) &&
            CanSkipUnchangedVerticalArrivalSeam(
                world, blocks, fluids, position, address.Local.Y))
            return false;

        if (address.Chunk != arrivingChunk ||
            address.Local.Y == 0 ||
            address.Local.Y == Chunk.Size - 1 ||
            !world.TryGetChunk(arrivingChunk, out var chunk))
            return true;

        var x = address.Local.X;
        var z = address.Local.Z;
        var light = chunk.GetLight(x, address.Local.Y, z);
        var hasResidentHorizontalNeighbor = false;

        bool MayExchangeLight(int dx, int dz)
        {
            var neighborChunk = new ChunkCoord(
                arrivingChunk.X + dx, arrivingChunk.Y,
                arrivingChunk.Z + dz);
            if (!world.ContainsChunk(neighborChunk))
                return false;

            hasResidentHorizontalNeighbor = true;
            var opposite = world.GetLightOrDark(position + (dx, 0, dz));
            // Both independently initialized chunks are already internally
            // converged. A one-level difference is stable across a horizontal
            // edge because propagation attenuates every channel by >= 1.
            // Any larger difference conservatively retains reconciliation.
            return Math.Abs(light.Sky - opposite.Sky) > 1 ||
                   Math.Abs(light.Red - opposite.Red) > 1 ||
                   Math.Abs(light.Green - opposite.Green) > 1 ||
                   Math.Abs(light.Blue - opposite.Blue) > 1;
        }

        if (x == 0 && MayExchangeLight(-1, 0) ||
            x == Chunk.Size - 1 && MayExchangeLight(1, 0) ||
            z == 0 && MayExchangeLight(0, -1) ||
            z == Chunk.Size - 1 && MayExchangeLight(0, 1))
            return true;

        // Conservatively keep anything that was not an actual horizontal
        // interface between two loaded chunks.
        return !hasResidentHorizontalNeighbor;
    }

    // Vertically adjacent chunks initialize their own light before residency.
    // A fully lit skylight seam with identical RGB cannot gain light across
    // the seam, and no new blocker is introduced into the lower column.
    // Likewise two dark opaque faces cannot exchange light. Avoid enqueuing
    // entire 16x16 faces for these already-converged cases.
    private static bool CanSkipUnchangedVerticalArrivalSeam(
        VoxelWorld world, BlockRegistry blocks, FluidRegistry fluids,
        WorldVoxelCoord position, int localY)
    {
        var opposite = position + (0, localY == 0 ? -1 : 1, 0);
        if (opposite.Y < 0 || !world.IsLoadedAt(opposite))
            return false;

        var light = world.GetLightOrDark(position);
        if (light != world.GetLightOrDark(opposite))
            return false;

        if (light.Sky == VoxelLight.MaxLevel)
        {
            // At intersections with horizontal seams, preserve a seed if
            // another resident neighbor still has a light disagreement.
            var local = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z).Local;
            return !(local.X == 0 && HasDifferentNeighbor(-1, 0) ||
                     local.X == Chunk.Size - 1 && HasDifferentNeighbor(1, 0) ||
                     local.Z == 0 && HasDifferentNeighbor(0, -1) ||
                     local.Z == Chunk.Size - 1 && HasDifferentNeighbor(0, 1));
        }

        return light.IsDark && IsOpaque(position) && IsOpaque(opposite);

        bool HasDifferentNeighbor(int dx, int dz)
        {
            var neighbor = position + (dx, 0, dz);
            return world.IsLoadedAt(neighbor) &&
                   world.GetLightOrDark(neighbor) != light;
        }

        bool IsOpaque(WorldVoxelCoord voxel)
        {
            var address = VoxelCoordinates.FromWorld(
                voxel.X, voxel.Y, voxel.Z);
            if (!world.TryGetChunk(address.Chunk, out var chunk))
                return false;
            var cell = chunk.GetCell(
                address.Local.X, address.Local.Y, address.Local.Z);
            var fluid = chunk.GetFluid(
                address.Local.X, address.Local.Y, address.Local.Z);
            return VoxelLightingMedium.Dampening(
                chunk, blocks, fluids,
                address.Local.X, address.Local.Y, address.Local.Z,
                cell, fluid) >= VoxelLight.MaxLevel;
        }
    }

    /// <summary>
    /// Retirement cannot create light on a horizontal seam. An already-dark
    /// surviving voxel therefore needs no reconciliation there. Losing a
    /// chunk below also cannot expose new direct sky above it. Losing an
    /// upper chunk can expose sky below, so preserve those dark seeds.
    /// Maximal skylight with no RGB light is already unaffected in all cases.
    /// </summary>
    public static bool NeedsRetirementSeamLightSeed(
        VoxelWorld world, ChunkCoord retiredChunk, WorldVoxelCoord position)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.IsLoadedAt(position))
            return false;

        var light = world.GetLightOrDark(position);
        if (light.Sky == VoxelLight.MaxLevel && light.BlockPeak == 0)
            return false;

        var survivingChunk = VoxelCoordinates.FromWorld(
            position.X, position.Y, position.Z).Chunk;

        if (survivingChunk.Y == retiredChunk.Y && light.IsDark)
            return false;

        if (survivingChunk.Y > retiredChunk.Y && light.IsDark)
            return false;

        return true;
    }

    public static IEnumerable<ChunkNeighborMeshInvalidation>
        PresentedNeighborMeshInvalidationsForAddition(
            ChunkCoord source,
            VoxelWorld world,
            Func<ChunkCoord, bool> isPresented)
    {
        ArgumentNullException.ThrowIfNull(
            world);
        ArgumentNullException.ThrowIfNull(
            isPresented);

        if (!world.TryGetChunk(
                source,
                out var sourceChunk))
        {
            yield break;
        }

        for (var y = -1;
             y <= 1;
             y++)
        {
            for (var z = -1;
                 z <= 1;
                 z++)
            {
                for (var x = -1;
                     x <= 1;
                     x++)
                {
                    if (x == 0 &&
                        y == 0 &&
                        z == 0)
                    {
                        continue;
                    }

                    var neighbor =
                        new ChunkCoord(
                            source.X + x,
                            source.Y + y,
                            source.Z + z);

                    if (!isPresented(
                            neighbor) ||
                        !world.TryGetChunk(
                            neighbor,
                            out var neighborChunk))
                    {
                        continue;
                    }

                    var sourceHasContent =
                        sourceChunk
                            .DependencyBoundaryHasContent(
                                x,
                                y,
                                z);
                    var neighborHasContent =
                        neighborChunk
                            .DependencyBoundaryHasContent(
                                -x,
                                -y,
                                -z);
                    var neighborHasFluid =
                        neighborChunk
                            .DependencyBoundaryHasFluid(
                                -x,
                                -y,
                                -z);
                    var cardinal =
                        Math.Abs(x) +
                        Math.Abs(y) +
                        Math.Abs(z) ==
                        1;
                    var sourceHasCardinalFluid =
                        cardinal &&
                        sourceChunk
                            .DependencyBoundaryHasFluid(
                                x,
                                y,
                                z);
                    var meshlets =
                        ChunkMeshletMask
                            .ForDependencyOffset(
                                -x,
                                -y,
                                -z);
                    var terrain =
                        sourceHasContent &&
                        neighborHasContent
                            ? meshlets
                            : ChunkMeshletMask.None;
                    var fluid =
                        neighborHasFluid &&
                        sourceHasContent ||
                        sourceHasCardinalFluid
                            ? meshlets
                            : ChunkMeshletMask.None;

                    if (terrain.IsEmpty &&
                        fluid.IsEmpty)
                    {
                        continue;
                    }

                    yield return
                        new ChunkNeighborMeshInvalidation(
                            neighbor,
                            terrain,
                            fluid);
                }
            }
        }
    }

    /// <summary>
    /// A retired chunk is absent from the live world. Only geometry belonging
    /// to the surviving neighbor can need a new boundary mesh. Empty
    /// neighbors never need a rebuild, and fluid-free neighbors never need
    /// a fluid rebuild. Lighting remains reconciled separately by residency.
    /// </summary>
    public static IEnumerable<ChunkNeighborMeshInvalidation>
        PresentedNeighborMeshInvalidationsForRetirement(
            ChunkCoord source,
            VoxelWorld world,
            Func<ChunkCoord, bool> isPresented)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(isPresented);

        foreach (var (neighbor, meshlets) in
                 PresentedNeighborMeshlets(source, isPresented))
        {
            if (!world.TryGetChunk(neighbor, out var chunk))
                continue;

            var dx = source.X - neighbor.X;
            var dy = source.Y - neighbor.Y;
            var dz = source.Z - neighbor.Z;
            var terrain =
                chunk.HasTerrainContent &&
                chunk.DependencyBoundaryHasContent(dx, dy, dz)
                    ? meshlets
                    : ChunkMeshletMask.None;
            var fluid =
                chunk.HasFluidContent &&
                chunk.DependencyBoundaryHasFluid(dx, dy, dz)
                    ? meshlets
                    : ChunkMeshletMask.None;

            if (!terrain.IsEmpty || !fluid.IsEmpty)
                yield return new ChunkNeighborMeshInvalidation(
                    neighbor, terrain, fluid);
        }
    }

    public static IEnumerable<(
        ChunkCoord Neighbor,
        ChunkMeshletMask Meshlets)> PresentedNeighborMeshlets(
        ChunkCoord source,
        Func<ChunkCoord, bool> isPresented)
    {
        ArgumentNullException.ThrowIfNull(isPresented);

        for (var y = -1; y <= 1; y++)
        {
            for (var z = -1; z <= 1; z++)
            {
                for (var x = -1; x <= 1; x++)
                {
                    if (x == 0 && y == 0 && z == 0)
                    {
                        continue;
                    }

                    var neighbor = new ChunkCoord(
                        source.X + x,
                        source.Y + y,
                        source.Z + z);

                    if (!isPresented(neighbor))
                    {
                        continue;
                    }

                    yield return (
                        neighbor,
                        ChunkMeshletMask.ForDependencyOffset(
                            -x,
                            -y,
                            -z));
                }
            }
        }
    }
}
