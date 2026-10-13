using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Content;

/// <summary>
/// Resolves and decodes the selected pack's player skin in MineClone's
/// resolution order. A missing or invalid high-resolution atlas cannot hide
/// an otherwise usable model when a lower-resolution atlas exists.
/// </summary>
internal static class PlayerSkinTextureLoader
{
    public static Texture2D Load(PackSelection selection, string authoredSkin)
    {
        var candidates = PlayerSkinAtlasCandidates.Paths(authoredSkin);
        foreach (var candidate in candidates)
        {
            var path = ProjectPackPaths.Resource(selection, candidate);
            if (!File.Exists(ProjectSettings.GlobalizePath(path)))
                continue;

            try
            {
                var image = ProjectPackFiles.LoadImage(selection, candidate);
                var size = image.GetSize();
                if (size.X != size.Y ||
                    size.X is not (64 or 128 or 256 or 512))
                {
                    GD.PushWarning(
                        $"Player skin '{path}' is not a square 64/128/256/512 atlas.");
                    continue;
                }

                var texture = ImageTexture.CreateFromImage(image);
                if (texture is null)
                {
                    GD.PushWarning($"Cannot create player skin texture from '{path}'.");
                    continue;
                }

                if (!string.Equals(candidate, authoredSkin, StringComparison.Ordinal))
                    GD.Print($"Player skin selected: {path}");
                return texture;
            }
            catch (IOException error)
            {
                GD.PushWarning($"Cannot read player skin '{path}': {error.Message}");
            }
            catch (InvalidDataException error)
            {
                GD.PushWarning($"Cannot decode player skin '{path}': {error.Message}");
            }
        }

        throw new InvalidDataException(
            $"No usable player skin for pack '{selection.Name}'. Tried: " +
            string.Join(", ", candidates));
    }
}
