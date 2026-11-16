using Asteria.Core.Diagnostics;

namespace Asteria.Core.Tests;

public sealed class DiagnosticSessionLogTests
{
    [Fact]
    public void SeparateRunsHaveUniqueFilesAndRetainTheirEvents()
    {
        var directory = NewDirectory();
        try
        {
            string firstPath;
            using (var first = DiagnosticSessionLog.Open(directory))
            {
                firstPath = first.Path;
                first.Write("INFO", "world.start", "seed=123");
                Assert.Contains("seed=123", File.ReadAllText(firstPath));
            }

            string secondPath;
            using (var second = DiagnosticSessionLog.Open(directory))
            {
                secondPath = second.Path;
                second.Write("WARN", "worker.slow", "elapsed_ms=250");
            }

            Assert.NotEqual(firstPath, secondPath);
            Assert.Equal(2, Directory.GetFiles(directory, "run-*.log").Length);
            Assert.Contains("event=world.start", File.ReadAllText(firstPath));
            Assert.Contains("event=session.stop", File.ReadAllText(firstPath));
            Assert.Contains("status=clean", File.ReadAllText(firstPath));
            Assert.Contains("event=worker.slow", File.ReadAllText(secondPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ErrorsAreFlushedAndMultilineDetailsStayOneEvent()
    {
        var directory = NewDirectory();
        try
        {
            string path;
            using (var session = DiagnosticSessionLog.Open(directory))
            {
                path = session.Path;
                session.Write("FATAL", "worker.crash", "line one\nline two");
                var contents = File.ReadAllText(path);
                Assert.Contains("event=worker.crash", contents);
                Assert.Contains("line one\\nline two", contents);
                Assert.DoesNotContain("line one\nline two", contents);
            }

            Assert.Contains("status=failed", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NewDirectory() =>
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "asteria-diagnostics-" + Guid.NewGuid().ToString("N"));
}
