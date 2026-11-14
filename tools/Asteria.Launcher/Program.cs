using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Asteria.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var portableRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var gameDirectory = Path.Combine(portableRoot, "Game");
        var gameExecutable = Path.Combine(gameDirectory, "AsteriaGame.exe");
        var logsDirectory = Path.Combine(portableRoot, "logs");

        try
        {
            Directory.CreateDirectory(logsDirectory);
            if (!File.Exists(gameExecutable))
            {
                ShowError(
                    "Não foi possível localizar o executável interno do Asteria.\n\n" +
                    $"Arquivo esperado: {gameExecutable}\n\n" +
                    "Extraia a pasta Asteria inteira e mantenha Game/ ao lado deste executável.");
                return 1;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = gameExecutable,
                WorkingDirectory = gameDirectory,
                UseShellExecute = false,
            };
            startInfo.Environment["ASTERIA_PORTABLE_ROOT"] = portableRoot;
            startInfo.Environment["WEBVIEW2_BROWSER_EXECUTABLE_FOLDER"] =
                Path.Combine(gameDirectory, "WebView2Runtime");
            startInfo.ArgumentList.Add("--log-file");
            startInfo.ArgumentList.Add(Path.Combine(logsDirectory, "godot-engine.log"));
            foreach (var argument in args)
                startInfo.ArgumentList.Add(argument);

            using var game = Process.Start(startInfo)
                ?? throw new InvalidOperationException("O processo do jogo não pôde ser iniciado.");
            game.WaitForExit();
            return game.ExitCode;
        }
        catch (Exception exception)
        {
            ShowError(
                "O Asteria não conseguiu iniciar.\n\n" +
                exception.GetBaseException().Message +
                "\n\nConsulte logs/ ao lado deste executável.");
            return 1;
        }
    }

    private static void ShowError(string message) =>
        _ = MessageBoxW(IntPtr.Zero, message, "Asteria", 0x10);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(
        IntPtr window,
        string text,
        string caption,
        uint type);
}
