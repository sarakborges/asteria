using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Gameplay;

/// <summary>
/// Captures only the Godot world viewport before its gameplay camera is
/// retired. WRY is an external native overlay, not part of this viewport.
/// A failed capture or preview publication never rejects a saved session.
/// </summary>
public sealed class WorldThumbnailCapture
{
    private byte[]? _pendingPng;

    public void Capture(Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        _pendingPng = null;
        try
        {
            using var screenshot = viewport.GetTexture().GetImage();
            var width = screenshot.GetWidth();
            var height = screenshot.GetHeight();
            if (width <= 0 || height <= 0)
                throw new InvalidDataException("Game viewport has no pixels.");

            // Match MineClone's centered 16:9 crop, then downscale to 320x180.
            var cropWidth = (long)width * WorldThumbnailStorage.Height >
                (long)height * WorldThumbnailStorage.Width
                ? (int)((long)height * WorldThumbnailStorage.Width /
                    WorldThumbnailStorage.Height)
                : width;
            var cropHeight = (long)height * WorldThumbnailStorage.Width >
                (long)width * WorldThumbnailStorage.Height
                ? (int)((long)width * WorldThumbnailStorage.Height /
                    WorldThumbnailStorage.Width)
                : height;
            using var crop = screenshot.GetRect(new Rect2I(
                (width - cropWidth) / 2, (height - cropHeight) / 2,
                cropWidth, cropHeight));
            crop.Resize(WorldThumbnailStorage.Width,
                WorldThumbnailStorage.Height, Image.Interpolation.Lanczos);
            var bytes = crop.SavePngToBuffer();
            if (!WorldThumbnailStorage.IsValidPng(bytes))
                throw new InvalidDataException("Captured thumbnail is invalid or too large.");
            _pendingPng = bytes;
        }
        catch (Exception error)
        {
            GD.PushWarning($"world.thumbnail.capture.failed: {error.Message}");
        }
    }

    public void Complete(string worldDirectory, bool saveCommitted)
    {
        var captured = _pendingPng;
        _pendingPng = null;
        if (!saveCommitted || captured is null) return;
        try
        {
            WorldThumbnailStorage.Publish(worldDirectory, captured);
        }
        catch (Exception error) when (error is IOException or
                UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            GD.PushWarning($"world.thumbnail.publish.failed: {error.Message}");
        }
    }
}
