using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Content;

internal static class ProjectPackFiles
{
    public static string AbsoluteResourcePath(
        PackSelection selection,
        string relativePath)
    {
        var resourcePath =
            ProjectPackPaths.Resource(
                selection,
                relativePath);
        var absolutePath =
            ProjectSettings.GlobalizePath(
                resourcePath);

        if (!File.Exists(
                absolutePath))
        {
            throw new FileNotFoundException(
                $"Pack resource does not exist: {resourcePath}",
                absolutePath);
        }

        return absolutePath;
    }

    public static Image LoadImage(
        PackSelection selection,
        string relativePath)
    {
        var absolutePath =
            AbsoluteResourcePath(
                selection,
                relativePath);
        var image =
            Image.LoadFromFile(
                absolutePath);

        if (image is null ||
            image.IsEmpty())
        {
            throw new InvalidDataException(
                $"Pack image could not be decoded: {relativePath}");
        }

        return image;
    }

    public static Texture2D LoadTexture(
        PackSelection selection,
        string relativePath)
    {
        var image =
            LoadImage(
                selection,
                relativePath);
        var texture =
            ImageTexture.CreateFromImage(
                image);

        if (texture is null)
        {
            throw new InvalidDataException(
                $"Pack image could not be converted to a texture: {relativePath}");
        }

        return texture;
    }
}
