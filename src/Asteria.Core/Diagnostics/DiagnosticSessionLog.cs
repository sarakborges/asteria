using System.Globalization;
using System.Text;

namespace Asteria.Core.Diagnostics;

/// <summary>
/// Owns the lifetime of a single process diagnostic file. Entries are serialized
/// across threads, immediately flushed and never overwrite an earlier run.
/// </summary>
public sealed class DiagnosticSessionLog : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;
    private bool _fatal;

    private DiagnosticSessionLog(string path, FileStream stream)
    {
        Path = path;
        _writer = new StreamWriter(stream, new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
    }

    public string Path { get; }

    public static DiagnosticSessionLog Open(string directory)
    {
        Directory.CreateDirectory(directory);
        var stamp = DateTimeOffset.UtcNow.ToString(
            "yyyy-MM-ddTHH-mm-ss-fffffffZ", CultureInfo.InvariantCulture);
        var prefix = $"run-{stamp}-p{Environment.ProcessId}";

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var suffix = attempt == 0 ? "" : $"-{attempt}";
            var path = System.IO.Path.Combine(directory, $"{prefix}{suffix}.log");
            FileStream stream;
            try
            {
                stream = new FileStream(
                    path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (IOException) when (File.Exists(path))
            {
                continue;
            }

            var session = new DiagnosticSessionLog(path, stream);
            session.Write(
                "INFO", "session.start",
                $"pid={Environment.ProcessId} " +
                $"platform={Environment.OSVersion.Platform} " +
                $"framework={Environment.Version} " +
                $"working_directory={Environment.CurrentDirectory}");
            return session;
        }

        throw new IOException("Unable to allocate a unique diagnostic log file.");
    }

    public void Write(string level, string eventName, string details = "")
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (level == "FATAL")
                _fatal = true;

            WriteLine(level, eventName, details);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            try
            {
                WriteLine(
                    "INFO", "session.stop",
                    $"status={(_fatal ? "failed" : "clean")}");
            }
            finally
            {
                _disposed = true;
                _writer.Dispose();
            }
        }
    }

    private void WriteLine(string level, string eventName, string details)
    {
        _writer.Write(DateTimeOffset.UtcNow.ToString(
            "O", CultureInfo.InvariantCulture));
        _writer.Write($" [{level}] event={eventName} thread={Environment.CurrentManagedThreadId}");
        if (details.Length > 0)
        {
            _writer.Write(' ');
            _writer.Write(details.Replace("\r", "\\r").Replace("\n", "\\n"));
        }
        _writer.WriteLine();
    }
}
