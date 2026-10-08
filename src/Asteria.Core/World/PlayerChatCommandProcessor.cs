namespace Asteria.Core.World;

/// <summary>
/// Small, deterministic local-chat command dialect. Future gameplay commands
/// must invoke authoritative runtime capabilities, never mutate them here.
/// </summary>
public static class PlayerChatCommandProcessor
{
    public static readonly string[] SupportedCommands =
        ["/help", "/position", "/time"];

    public static void Execute(
        PlayerChatSession session,
        string line,
        string position,
        string worldTime)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!line.StartsWith("/", StringComparison.Ordinal))
        {
            session.Append($"Player: {line}");
            return;
        }

        var trimmed = line.Trim();
        switch (trimmed)
        {
            case "/help":
                session.Append(string.Join(", ", SupportedCommands),
                    localizationKey: "chat.local.help");
                break;
            case "/position":
                session.Append(position, localizationKey: "chat.local.position");
                break;
            case "/time":
                session.Append(worldTime, localizationKey: "chat.local.time");
                break;
            default:
                session.Append(trimmed.Split(' ', 2)[0], isError: true,
                    localizationKey: "chat.local.unknown");
                break;
        }
    }
}
