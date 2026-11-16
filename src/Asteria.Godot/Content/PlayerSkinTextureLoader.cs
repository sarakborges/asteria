using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Content;

internal static class PlayerSkinTextureLoader
{
    public static Texture2D Load(PackSelection selection, string authoredSkin)
    {
        var candidates = PlayerSkinAtlasCandidates.Paths(authoredSkin);
        foreach (var candidate in candidates)
        {
            if (!ProjectPackFiles.ResourceExists(selection, candidate))
                continue;
            try
            {
                var image = ProjectPackFiles.LoadImage(selection, candidate);
                var size = image.GetSize();
                if (size.X != size.Y ||
                    size.X is not (64 or 128 or 256 or 512))
                {
                    GD.PushWarning($"Player skin '{candidate}' is not a supported square atlas.");
                    continue;
                }
                var texture = ImageTexture.CreateFromImage(image);
                if (texture is null) continue;
                return texture;
            }
            catch (IOException error)
            {
                GD.PushWarning($"Cannot read player skin '{candidate}': {error.Message}");
            }
            catch (InvalidDataException error)
            {
                GD.PushWarning($"Cannot decode player skin '{candidate}': {error.Message}");
            }
        }
        throw new InvalidDataException(
            $"No usable player skin for pack '{selection.Name}'. Tried: " +
            string.Join(", ", candidates));
    }
}
