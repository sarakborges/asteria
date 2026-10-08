namespace Asteria.Core.World;

public static class ChunkLightingSolver
{
    private static readonly (int X, int Y, int Z)[] Neighbors =
    [
        (1, 0, 0),
        (-1, 0, 0),
        (0, 1, 0),
        (0, -1, 0),
        (0, 0, 1),
        (0, 0, -1),
    ];

    public static void Initialize(
        Chunk chunk,
        BlockRegistry blocks,
        FluidRegistry fluids)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);

        chunk.ClearLight();
        var needsRelaxation = SeedDirectLight(
            chunk,
            blocks,
            fluids);

        if (chunk.IsEmpty || !needsRelaxation)
        {
            return;
        }

        Relax(
            chunk,
            blocks,
            fluids);
    }

    private static bool SeedDirectLight(
        Chunk chunk,
        BlockRegistry blocks,
        FluidRegistry fluids)
    {
        var hasTransmittingMedium = false;
        for (var z = 0; z < Chunk.Size; z++)
        {
            for (var x = 0; x < Chunk.Size; x++)
            {
                byte sky =
                    VoxelLight.MaxLevel;

                for (var y =
                         Chunk.Size - 1;
                     y >= 0;
                     y--)
                {
                    var cell =
                        chunk.GetCell(x, y, z);
                    var fluid =
                        chunk.GetFluid(x, y, z);
                    var dampening =
                        VoxelLightingMedium.Dampening(
                            chunk,
                            blocks,
                            fluids,
                            x,
                            y,
                            z,
                            cell,
                            fluid);

                    hasTransmittingMedium |=
                        dampening < VoxelLight.MaxLevel;

                    if (sky > 0)
                    {
                        sky =
                            SaturatingSubtract(
                                sky,
                                dampening);
                    }

                    var emission =
                        VoxelLightingMedium.Emission(
                            blocks,
                            fluids,
                            cell,
                            fluid);

                    chunk.SetLight(
                        x,
                        y,
                        z,
                        new VoxelLight(
                            sky,
                            emission.Red,
                            emission.Green,
                            emission.Blue));
                }
            }
        }

        // An entirely opaque chunk has no voxel through which the local
        // flood-fill could propagate. Direct light and emissions are final.
        return hasTransmittingMedium;
    }

    private static void Relax(
        Chunk chunk,
        BlockRegistry blocks,
        FluidRegistry fluids)
    {
        var queue =
            new Queue<int>(Chunk.Volume);
        var queued =
            new bool[Chunk.Volume];

        for (var y = 0;
             y < Chunk.Size;
             y++)
        {
            for (var z = 0;
                 z < Chunk.Size;
                 z++)
            {
                for (var x = 0;
                     x < Chunk.Size;
                     x++)
                {
                    Enqueue(
                        queue,
                        queued,
                        x,
                        y,
                        z);
                }
            }
        }

        while (queue.Count > 0)
        {
            var index =
                queue.Dequeue();
            queued[index] = false;
            var (x, y, z) =
                FromIndex(index);
            var cell =
                chunk.GetCell(x, y, z);
            var fluid =
                chunk.GetFluid(x, y, z);
            var dampening =
                VoxelLightingMedium.Dampening(
                    chunk,
                    blocks,
                    fluids,
                    x,
                    y,
                    z,
                    cell,
                    fluid);

            if (dampening >=
                VoxelLight.MaxLevel)
            {
                continue;
            }

            var attenuation =
                Math.Max(
                    (byte)1,
                    dampening);
            var current =
                chunk.GetLight(x, y, z);
            var candidate =
                VoxelLight.FromEmission(
                    VoxelLightingMedium.Emission(
                        blocks,
                        fluids,
                        cell,
                        fluid));

            foreach (var (dx, dy, dz) in Neighbors)
            {
                var nx = x + dx;
                var ny = y + dy;
                var nz = z + dz;

                if (!Chunk.Contains(
                        nx,
                        ny,
                        nz))
                {
                    continue;
                }

                candidate =
                    VoxelLight.Max(
                        candidate,
                        VoxelLight.Attenuate(
                            chunk.GetLight(
                                nx,
                                ny,
                                nz),
                            attenuation));
            }

            // Direct skylight is authoritative and must never be reduced by
            // this initial local relaxation pass.
            candidate =
                VoxelLight.Max(
                    candidate,
                    current);

            if (candidate == current)
            {
                continue;
            }

            chunk.SetLight(
                x,
                y,
                z,
                candidate);

            foreach (var (dx, dy, dz) in Neighbors)
            {
                var nx = x + dx;
                var ny = y + dy;
                var nz = z + dz;

                if (Chunk.Contains(
                        nx,
                        ny,
                        nz))
                {
                    Enqueue(
                        queue,
                        queued,
                        nx,
                        ny,
                        nz);
                }
            }
        }
    }

    public static byte MediumDampening(
        Chunk chunk,
        BlockRegistry blocks,
        FluidRegistry fluids,
        int x,
        int y,
        int z) =>
        VoxelLightingMedium.Dampening(
            chunk,
            blocks,
            fluids,
            x,
            y,
            z);

    private static void Enqueue(
        Queue<int> queue,
        bool[] queued,
        int x,
        int y,
        int z)
    {
        var index =
            ToIndex(x, y, z);

        if (queued[index])
        {
            return;
        }

        queued[index] = true;
        queue.Enqueue(index);
    }

    private static int ToIndex(
        int x,
        int y,
        int z) =>
        x +
        Chunk.Size *
        (z + Chunk.Size * y);

    private static (
        int X,
        int Y,
        int Z) FromIndex(
        int index)
    {
        var y =
            index / Chunk.Area;
        var remainder =
            index - y * Chunk.Area;
        var z =
            remainder / Chunk.Size;
        var x =
            remainder -
            z * Chunk.Size;
        return (x, y, z);
    }

    private static byte SaturatingSubtract(
        byte value,
        byte amount) =>
        value > amount
            ? (byte)(value - amount)
            : (byte)0;
}
