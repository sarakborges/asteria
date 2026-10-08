using System.Security.Cryptography;

namespace Asteria.Core.World;

/// <summary>
/// Crash-recoverable publication of complete *spatial-only* snapshots.
/// These files deliberately do not register with WorldSaveCatalog until
/// player, entities, rules and all per-Sphere runtime state are restorable.
/// </summary>
public static class SpatialSaveStorage
{
    public const int RetainedGenerations = 4;
    private const int MaximumFiles = 4096;
    private const string Prefix = "sphere-chunks-";
    private const string Extension = ".bin";
    private const string LockName = "sphere-chunks.lock";
    private const int DigestLength = 32;
    private const int CopyBufferSize = 64 * 1024;

    public static ulong Publish(
        string directory, DimensionChunkSaveSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(snapshot);
        Directory.CreateDirectory(directory);
        ValidateDirectory(directory);

        using var lease = AcquireLease(directory);
        var generations = EnumerateGenerations(directory);
        var highest = generations.Count == 0 ? 0 : generations.Max(entry => entry.Generation);
        if (highest == ulong.MaxValue)
            throw new IOException("Spatial generation counter exhausted.");
        var generation = highest + 1;
        var destination = Path.Combine(directory, GenerationFileName(generation));
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            // Nothing becomes visible until the complete payload, checksum
            // and file buffers have been written and flushed.
            using (var file = new FileStream(temporary, FileMode.CreateNew,
                       FileAccess.ReadWrite, FileShare.None, CopyBufferSize,
                       FileOptions.SequentialScan))
            {
                DimensionChunkFileCodec.Write(file, snapshot);
                file.Flush(flushToDisk: true);
                file.Position = 0;
                var digest = SHA256.HashData(file);
                file.Write(digest);
                file.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination);
            // The latest complete generation is recoverable even if pruning
            // below fails. Directory fsync is platform-specific and not
            // guaranteed by File.Move; this is atomic publication, not a
            // power-loss durability guarantee on every filesystem.
            Prune(directory);
            return generation;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static DimensionChunkSaveSnapshot ReadLatest(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ValidateDirectory(directory);
        using var lease = AcquireLease(directory);
        var candidates = EnumerateGenerations(directory);
        if (candidates.Count == 0)
            throw new FileNotFoundException("No committed spatial save generations.", directory);

        InvalidDataException? lastCorruption = null;
        foreach (var candidate in candidates.OrderByDescending(entry => entry.Generation))
        {
            try
            {
                return ReadCandidate(candidate.Path);
            }
            catch (InvalidDataException error)
            {
                lastCorruption = error;
            }
        }

        throw new InvalidDataException(
            "No intact spatial snapshot generation was found.", lastCorruption);
    }

    public static string GenerationFileName(ulong generation)
    {
        if (generation == 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        return Prefix + generation.ToString("D20", System.Globalization.CultureInfo.InvariantCulture)
            + Extension;
    }

    private static DimensionChunkSaveSnapshot ReadCandidate(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            (attributes & FileAttributes.Directory) != 0)
            throw new InvalidDataException("Spatial generation is not a regular file.");

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            CopyBufferSize, FileOptions.SequentialScan);
        var length = file.Length;
        if (length <= DigestLength ||
            length > DimensionChunkFileCodec.MaximumPayloadBytes + DigestLength)
            throw new InvalidDataException("Invalid spatial generation size.");

        var payloadLength = length - DigestLength;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[CopyBufferSize];
        var remaining = payloadLength;
        while (remaining != 0)
        {
            var count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (count == 0)
                throw new InvalidDataException("Truncated spatial generation.");
            hash.AppendData(buffer, 0, count);
            remaining -= count;
        }

        Span<byte> storedHash = stackalloc byte[DigestLength];
        file.ReadExactly(storedHash);
        if (!CryptographicOperations.FixedTimeEquals(
                hash.GetHashAndReset(), storedHash))
            throw new InvalidDataException("Spatial generation checksum mismatch.");

        file.Position = 0;
        return DimensionChunkFileCodec.Read(file, payloadLength);
    }

    private static FileStream AcquireLease(string directory)
    {
        var path = Path.Combine(directory, LockName);
        if (File.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Save lock cannot be a symbolic link.");

        return new FileStream(path, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }

    private static void ValidateDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(
                $"World spatial save directory does not exist: {directory}");
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("World save directory cannot be a symbolic link.");
    }

    private static List<(ulong Generation, string Path)> EnumerateGenerations(string directory)
    {
        var result = new List<(ulong Generation, string Path)>();
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if (result.Count >= MaximumFiles)
                throw new IOException("Too many spatial save generation files.");
            var name = Path.GetFileName(path);
            if (TryParseGeneration(name, out var generation))
                result.Add((generation, path));
        }
        return result;
    }

    private static bool TryParseGeneration(string fileName, out ulong generation)
    {
        generation = 0;
        if (!fileName.StartsWith(Prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(Extension, StringComparison.Ordinal))
            return false;

        var digits = fileName.AsSpan(Prefix.Length,
            fileName.Length - Prefix.Length - Extension.Length);
        return digits.Length == 20 &&
            digits.IndexOfAnyExceptInRange('0', '9') == -1 &&
            ulong.TryParse(digits,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out generation) &&
            generation != 0;
    }

    private static void Prune(string directory)
    {
        var generations = EnumerateGenerations(directory);
        foreach (var old in generations
                     .OrderByDescending(entry => entry.Generation)
                     .Skip(RetainedGenerations))
            File.Delete(old.Path);

        // Any staging file left by a previously terminated writer has never
        // been published and cannot be selected by ReadLatest.
        foreach (var temporary in Directory.EnumerateFiles(directory,
                     Prefix + "*.tmp"))
            File.Delete(temporary);
    }
}
