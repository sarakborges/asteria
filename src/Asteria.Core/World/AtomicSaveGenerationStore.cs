using System.Globalization;
using System.Security.Cryptography;

namespace Asteria.Core.World;

/// <summary>
/// A single bounded, checksummed file is published per generation. The name
/// prefix distinguishes incomplete spatial snapshots from complete sessions.
/// Decoding is part of candidate selection, so corrupt latest saves fall
/// back to an earlier restorable generation.
/// </summary>
internal static class AtomicSaveGenerationStore
{
    internal const int RetainedGenerations = 4;
    private const int MaximumFiles = 4096;
    private const int DigestSize = 32;
    private const int BufferSize = 64 * 1024;

    public static ulong Publish(
        string directory, string prefix, long maximumBytes,
        Action<Stream> encode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(encode);
        Directory.CreateDirectory(directory);
        ValidateDirectory(directory);

        using var lease = AcquireLease(directory, prefix);
        var generations = Enumerate(directory, prefix);
        var highest = generations.Count == 0 ? 0 : generations.Max(entry => entry.Generation);
        if (highest == ulong.MaxValue)
            throw new IOException("Save generation counter exhausted.");
        var generation = highest + 1;
        var final = Path.Combine(directory, GenerationFileName(prefix, generation));
        var temporary = final + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew,
                       FileAccess.ReadWrite, FileShare.None, BufferSize,
                       FileOptions.SequentialScan))
            {
                encode(file);
                if (file.Length is < 1 || file.Length > maximumBytes)
                    throw new InvalidDataException("Encoded save exceeds supported size.");

                file.Flush(flushToDisk: true);
                file.Position = 0;
                var digest = SHA256.HashData(file);
                file.Write(digest);
                file.Flush(flushToDisk: true);
            }

            // Same-directory rename is the sole publication point; unfinished
            // files are never selected by readers.
            File.Move(temporary, final);
            Prune(directory, prefix);
            return generation;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static T ReadLatest<T>(
        string directory, string prefix, long maximumBytes,
        Func<Stream, long, T> decode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(decode);
        ValidateDirectory(directory);
        using var lease = AcquireLease(directory, prefix);

        var candidates = Enumerate(directory, prefix);
        if (candidates.Count == 0)
            throw new FileNotFoundException("No published save generation.", directory);

        InvalidDataException? lastCorruption = null;
        foreach (var (generation, path) in
                 candidates.OrderByDescending(entry => entry.Generation))
        {
            try
            {
                return ReadCandidate(path, maximumBytes, decode);
            }
            catch (InvalidDataException error)
            {
                lastCorruption = error;
            }
        }

        throw new InvalidDataException(
            "No fully restorable save generation was found.", lastCorruption);
    }

    public static string GenerationFileName(string prefix, ulong generation)
    {
        if (generation == 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if (string.IsNullOrWhiteSpace(prefix) ||
            prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Invalid generation prefix.", nameof(prefix));
        return prefix + generation.ToString("D20", CultureInfo.InvariantCulture) + ".bin";
    }

    private static T ReadCandidate<T>(
        string path, long maximumBytes, Func<Stream, long, T> decode)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Saved generation is not a regular file.");

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, BufferSize, FileOptions.SequentialScan);
        var length = file.Length;
        if (length <= DigestSize || length > maximumBytes + DigestSize)
            throw new InvalidDataException("Invalid saved generation length.");

        var payloadLength = length - DigestSize;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        var remaining = payloadLength;
        while (remaining > 0)
        {
            var count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (count == 0)
                throw new InvalidDataException("Truncated saved generation.");
            hash.AppendData(buffer, 0, count);
            remaining -= count;
        }

        Span<byte> storedHash = stackalloc byte[DigestSize];
        file.ReadExactly(storedHash);
        if (!CryptographicOperations.FixedTimeEquals(
                hash.GetHashAndReset(), storedHash))
            throw new InvalidDataException("Saved generation checksum mismatch.");

        file.Position = 0;
        var result = decode(file, payloadLength);
        if (file.Position != payloadLength)
            throw new InvalidDataException("Save decoder did not consume its complete payload.");
        return result;
    }

    private static FileStream AcquireLease(string directory, string prefix)
    {
        var path = Path.Combine(directory, prefix + "lock");
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
                $"Save directory does not exist: {directory}");
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Save directory cannot be a symbolic link.");
    }

    private static List<(ulong Generation, string Path)> Enumerate(
        string directory, string prefix)
    {
        var result = new List<(ulong Generation, string Path)>();
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var name = Path.GetFileName(path);
            if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
                !name.EndsWith(".bin", StringComparison.Ordinal))
                continue;
            if (result.Count >= MaximumFiles)
                throw new IOException("Too many save generation files.");

            var digits = name.AsSpan(prefix.Length,
                name.Length - prefix.Length - ".bin".Length);
            if (digits.Length != 20 ||
                digits.IndexOfAnyExceptInRange('0', '9') != -1 ||
                !ulong.TryParse(digits, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var generation) ||
                generation == 0)
                continue;
            result.Add((generation, path));
        }
        return result;
    }

    private static void Prune(string directory, string prefix)
    {
        foreach (var old in Enumerate(directory, prefix)
                     .OrderByDescending(entry => entry.Generation)
                     .Skip(RetainedGenerations))
            File.Delete(old.Path);

        foreach (var temporary in Directory.EnumerateFiles(directory,
                     prefix + "*.tmp"))
            File.Delete(temporary);
    }
}
