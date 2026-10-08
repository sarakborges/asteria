using System.Globalization;

namespace Asteria.Core.World;

/// <summary>
/// MineClone world-systems-rebuild chat grammar. Parsing never mutates the
/// world; the caller dispatches validated commands through runtime owners.
/// Coordinates are typed x, z, y by the authored chat contract.
/// </summary>
public static class PlayerChatCommandProcessor
{
    public static IReadOnlyList<string> SupportedCommands { get; } =
        Array.AsReadOnly(new[]
        {
            "/spawn", "/place", "/locate", "/warp", "/kill", "/modify",
        });

    public static ParsedChatCommand Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var line = input.Trim();
        if (!line.StartsWith("/", StringComparison.Ordinal))
            return new(ChatCommandKind.Say, line);

        var words = line.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return new(ChatCommandKind.Unknown, "/");

        var name = words[0];
        var args = words[1..];
        return name switch
        {
            "/spawn" => args.Length is 1 or 2
                ? new(ChatCommandKind.Spawn, args[0],
                    args.Length == 2 ? args[1] : null)
                : Usage("/spawn <id> [meta_tag]"),
            "/place" => ParseStructure(args),
            "/locate" => ParseLocate(args),
            "/warp" => ParseWarp(args),
            "/kill" => args.Length == 0
                ? new(ChatCommandKind.Kill)
                : Usage("/kill"),
            "/modify" => ParseModify(args),
            _ => new(ChatCommandKind.Unknown, name),
        };
    }

    private static ParsedChatCommand ParseStructure(string[] args)
    {
        if (args.Length is < 2 or > 3 || args[0] != "structure" ||
            (args.Length == 3 && !TryVariation(args[2], out _)))
            return Usage("/place structure <id> [variation]");
        return new(ChatCommandKind.Place, args[1],
            args.Length == 3 ? args[2] : null);
    }

    private static ParsedChatCommand ParseLocate(string[] args)
    {
        if (args.Length == 2 && args[0] == "biome")
            return new(ChatCommandKind.LocateBiome, args[1]);
        if (args.Length is 2 or 3 && args[0] == "structure" &&
            (args.Length == 2 || TryVariation(args[2], out _)))
            return new(ChatCommandKind.LocateStructure, args[1],
                args.Length == 3 ? args[2] : null);
        return Usage("/locate biome <id> | /locate structure <id> [variation]");
    }

    private static ParsedChatCommand ParseWarp(string[] args)
    {
        if (args.Length is < 3 or > 4 ||
            !int.TryParse(args[0], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(args[1], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var z) ||
            !int.TryParse(args[2], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var y))
            return Usage("/warp <x> <z> <y> [dimension]");

        return new(ChatCommandKind.Warp, null,
            args.Length == 4 ? args[3] : null, x, y, z);
    }

    private static ParsedChatCommand ParseModify(string[] args)
    {
        if (args.Length is < 2 or > 3 ||
            args[0] is not ("add" or "remove" or "edit") ||
            (args[0] == "remove" && args.Length == 3))
            return Usage("/modify <add|remove|edit> <meta_tag> [value]");
        return new(ChatCommandKind.Modify, args[1],
            args[0], Value: args.Length == 3 ? args[2] : null);
    }

    public static bool TryVariation(string value, out int variation) =>
        int.TryParse(value, NumberStyles.None,
            CultureInfo.InvariantCulture, out variation) && variation > 0;

    private static ParsedChatCommand Usage(string usage) =>
        new(ChatCommandKind.Usage, usage);
}

public enum ChatCommandKind : byte
{
    Say, Spawn, Place, LocateBiome, LocateStructure, Warp,
    Kill, Modify, Usage, Unknown,
}

public sealed record ParsedChatCommand(
    ChatCommandKind Kind,
    string? Argument = null,
    string? Option = null,
    int X = 0,
    int Y = 0,
    int Z = 0,
    string? Value = null);
