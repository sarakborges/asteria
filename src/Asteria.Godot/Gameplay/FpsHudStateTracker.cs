namespace Asteria.Client.Gameplay;

public sealed class FpsHudStateTracker
{
    private const double UpdateIntervalSeconds =
        0.25;

    private double _elapsedSeconds;
    private uint _frameCount;

    public int? Current { get; private set; }

    public bool TryAdvance(
        double deltaSeconds,
        out int framesPerSecond)
    {
        if (!double.IsFinite(deltaSeconds) ||
            deltaSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaSeconds));
        }

        _elapsedSeconds +=
            deltaSeconds;
        _frameCount =
            _frameCount == uint.MaxValue
                ? uint.MaxValue
                : _frameCount + 1;

        if (_elapsedSeconds <
            UpdateIntervalSeconds)
        {
            framesPerSecond =
                Current ?? 0;
            return false;
        }

        var measured =
            _elapsedSeconds <=
                double.Epsilon
                ? 0
                : (int)Math.Clamp(
                    Math.Round(
                        _frameCount /
                        _elapsedSeconds),
                    0,
                    int.MaxValue);

        _elapsedSeconds = 0;
        _frameCount = 0;
        framesPerSecond = measured;

        if (Current == measured)
        {
            return false;
        }

        Current = measured;
        return true;
    }

    public void Reset()
    {
        _elapsedSeconds = 0;
        _frameCount = 0;
        Current = null;
    }
}
