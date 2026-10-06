namespace Asteria.Core.World;

public readonly record struct WorldFrameWorkBudget(
    long DeadlineTimestamp)
{
    private static readonly TimeSpan PressuredBudget =
        TimeSpan.FromMilliseconds(2);
    private static readonly TimeSpan NormalBudget =
        TimeSpan.FromMilliseconds(3);
    private static readonly TimeSpan FastBudget =
        TimeSpan.FromMilliseconds(4);

    private const double PressuredFrameSeconds = 1.0 / 55.0;
    private const double FastFrameSeconds = 1.0 / 70.0;

    public static TimeSpan DurationForFrameSeconds(
        double frameSeconds)
    {
        var seconds = Math.Max(frameSeconds, 0.0001);

        return seconds > PressuredFrameSeconds
            ? PressuredBudget
            : seconds > FastFrameSeconds
                ? NormalBudget
                : FastBudget;
    }

    public static WorldFrameWorkBudget Begin(
        double frameSeconds,
        long nowTimestamp,
        long timestampFrequency)
    {
        var durationTicks =
            FrameWorkBudget.ToTimestampTicks(
                DurationForFrameSeconds(frameSeconds),
                timestampFrequency);

        return new WorldFrameWorkBudget(
            FrameWorkBudget.SaturatingAdd(
                nowTimestamp,
                durationTicks));
    }

    public bool Exhausted(long nowTimestamp) =>
        nowTimestamp >= DeadlineTimestamp;
}

public struct FrameWorkBudget
{
    private readonly long _deadlineTimestamp;
    private readonly long? _globalDeadlineTimestamp;
    private readonly int _minimumItems;
    private readonly int? _maximumItems;
    private int _processedItems;

    private FrameWorkBudget(
        long deadlineTimestamp,
        long? globalDeadlineTimestamp,
        int minimumItems,
        int? maximumItems)
    {
        _deadlineTimestamp = deadlineTimestamp;
        _globalDeadlineTimestamp = globalDeadlineTimestamp;
        _minimumItems = minimumItems;
        _maximumItems = maximumItems;
        _processedItems = 0;
    }

    public int ProcessedItems => _processedItems;

    public static FrameWorkBudget Begin(
        TimeSpan duration,
        int minimumItems,
        long nowTimestamp,
        long timestampFrequency,
        int? maximumItems = null,
        long? globalDeadlineTimestamp = null)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration));
        }

        if (minimumItems < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumItems));
        }

        if (maximumItems is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumItems));
        }

        if (maximumItems is { } maximum &&
            maximum < minimumItems)
        {
            throw new ArgumentException(
                "Maximum items cannot be lower than minimum items.",
                nameof(maximumItems));
        }

        var durationTicks =
            ToTimestampTicks(
                duration,
                timestampFrequency);

        return new FrameWorkBudget(
            SaturatingAdd(
                nowTimestamp,
                durationTicks),
            globalDeadlineTimestamp,
            minimumItems,
            maximumItems);
    }

    public bool Exhausted(long nowTimestamp)
    {
        if (_maximumItems is { } maximum &&
            _processedItems >= maximum)
        {
            return true;
        }

        if (_processedItems < _minimumItems)
        {
            return false;
        }

        return nowTimestamp >= _deadlineTimestamp ||
               (_globalDeadlineTimestamp is { } global &&
                nowTimestamp >= global);
    }

    public void Record(int items = 1)
    {
        if (items < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(items));
        }

        _processedItems =
            items > int.MaxValue - _processedItems
                ? int.MaxValue
                : _processedItems + items;
    }

    internal static long ToTimestampTicks(
        TimeSpan duration,
        long timestampFrequency)
    {
        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestampFrequency));
        }

        var ticks =
            duration.TotalSeconds *
            timestampFrequency;

        if (ticks >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return Math.Max(
            0,
            (long)Math.Ceiling(ticks));
    }

    internal static long SaturatingAdd(
        long value,
        long amount)
    {
        if (amount <= 0)
        {
            return value;
        }

        return value > long.MaxValue - amount
            ? long.MaxValue
            : value + amount;
    }
}
