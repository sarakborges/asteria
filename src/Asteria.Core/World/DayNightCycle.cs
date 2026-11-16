using System.Text.Json;
using Asteria.Core.Content;

namespace Asteria.Core.World;

public enum DayNightPhase
{
    Dawn,
    Day,
    Dusk,
    Night,
}

public readonly record struct DayNightPhaseTiming(
    ulong DurationTicks,
    float SkyLightFactor);

public readonly record struct DayNightSample(
    DayNightPhase Phase,
    DayNightPhase NextPhase,
    double Transition,
    float SkyLightFactor,
    double NormalizedTime);

public sealed record DayNightCelestialDefinition(
    DayNightPhase RisePhase,
    DayNightPhase SetPhase,
    float RiseAzimuthDegrees,
    float SetAzimuthDegrees,
    float MaxAltitudeDegrees,
    float Size,
    float OrbitRadius,
    DimensionColor Tint,
    string? Texture = null);

public sealed class DayNightCycleDefinition
{
    private readonly DayNightPhase[] _sequence;
    private readonly IReadOnlyDictionary<DayNightPhase, DayNightPhaseTiming> _phases;

    public DayNightCycleDefinition(
        string id,
        ulong dayDurationTicks,
        double initialTime,
        double worldTimeStartHour,
        IEnumerable<DayNightPhase> sequence,
        IReadOnlyDictionary<DayNightPhase, DayNightPhaseTiming> phases,
        DayNightCelestialDefinition? sun = null,
        DayNightCelestialDefinition? moon = null)
    {
        BlockDefinition.ValidateId(id);
        if (dayDurationTicks == 0 ||
            dayDurationTicks > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(dayDurationTicks));
        }
        if (!double.IsFinite(initialTime) || initialTime is < 0 or >= 1 ||
            !double.IsFinite(worldTimeStartHour) || worldTimeStartHour is < 0 or >= 24)
        {
            throw new ArgumentOutOfRangeException(nameof(initialTime));
        }

        _sequence = sequence?.ToArray() ??
            throw new ArgumentNullException(nameof(sequence));
        if (_sequence.Length != 4 ||
            _sequence.Distinct().Count() != 4 ||
            _sequence.Any(phase => !Enum.IsDefined(phase)))
        {
            throw new ArgumentException(
                "Sequence must contain Dawn, Day, Dusk and Night exactly once.",
                nameof(sequence));
        }

        ArgumentNullException.ThrowIfNull(phases);
        var validated = new Dictionary<DayNightPhase, DayNightPhaseTiming>();
        ulong total = 0;
        foreach (var phase in _sequence)
        {
            if (!phases.TryGetValue(phase, out var timing) ||
                timing.DurationTicks == 0 ||
                !float.IsFinite(timing.SkyLightFactor) ||
                timing.SkyLightFactor is < 0 or > 1)
            {
                throw new ArgumentException(
                    $"Invalid timing for {phase}.", nameof(phases));
            }
            total = checked(total + timing.DurationTicks);
            validated.Add(phase, timing);
        }
        if (total != dayDurationTicks || phases.Count != 4)
        {
            throw new ArgumentException(
                "Phase durations must sum to the day duration.",
                nameof(phases));
        }

        ValidateCelestial(sun);
        ValidateCelestial(moon);
        Id = id;
        DayDurationTicks = dayDurationTicks;
        InitialTick = (ulong)Math.Floor(initialTime * dayDurationTicks);
        WorldTimeStartHour = worldTimeStartHour;
        _phases = validated;
        Sun = sun;
        Moon = moon;
    }

    public string Id { get; }
    public ulong DayDurationTicks { get; }
    public ulong InitialTick { get; }
    public double WorldTimeStartHour { get; }
    public DayNightCelestialDefinition? Sun { get; }
    public DayNightCelestialDefinition? Moon { get; }

    public DayNightSample Sample(ulong tickInDay)
    {
        if (tickInDay >= DayDurationTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(tickInDay));
        }

        ulong start = 0;
        for (var i = 0; i < _sequence.Length; i++)
        {
            var phase = _sequence[i];
            var timing = _phases[phase];
            var end = start + timing.DurationTicks;
            if (tickInDay < end)
            {
                var next = _sequence[(i + 1) % _sequence.Length];
                var transition = (double)(tickInDay - start) / timing.DurationTicks;
                var intensity = timing.SkyLightFactor +
                    (_phases[next].SkyLightFactor - timing.SkyLightFactor) * transition;
                return new DayNightSample(
                    phase, next, transition, (float)intensity,
                    (double)tickInDay / DayDurationTicks);
            }
            start = end;
        }
        throw new InvalidOperationException("Validated day-night phase sequence exhausted.");
    }

    public (int Hour, int Minute) WorldTime(ulong tickInDay)
    {
        if (tickInDay >= DayDurationTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(tickInDay));
        }
        var totalMinutes = ((int)Math.Floor(WorldTimeStartHour * 60) +
            (int)Math.Floor((double)tickInDay / DayDurationTicks * 1440)) % 1440;
        return (totalMinutes / 60, totalMinutes % 60);
    }

    public double? ProgressBetweenPhases(
        ulong tickInDay,
        DayNightPhase rise,
        DayNightPhase set)
    {
        if (tickInDay >= DayDurationTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(tickInDay));
        }
        var start = StartTick(rise);
        var end = checked(StartTick(set) + _phases[set].DurationTicks);
        if (end <= start)
        {
            end += DayDurationTicks;
        }
        var now = tickInDay < start
            ? tickInDay + DayDurationTicks
            : tickInDay;
        if (now < start || now >= end)
        {
            return null;
        }
        return (double)(now - start) / (end - start);
    }

    public ulong MidnightTick
    {
        get
        {
            var hoursUntil = (24.0 - WorldTimeStartHour) % 24.0;
            return (ulong)Math.Ceiling(
                hoursUntil / 24.0 * DayDurationTicks) % DayDurationTicks;
        }
    }

    private ulong StartTick(DayNightPhase target)
    {
        ulong start = 0;
        foreach (var phase in _sequence)
        {
            if (phase == target) return start;
            start += _phases[phase].DurationTicks;
        }
        throw new InvalidOperationException("Missing day-night phase.");
    }

    private static void ValidateCelestial(DayNightCelestialDefinition? body)
    {
        if (body is null) return;
        if (body.Texture is { } texture)
        {
            if (string.IsNullOrWhiteSpace(texture))
            {
                throw new FormatException("Celestial texture must not be empty.");
            }
            PackContentFields.ResourcePath(texture, "celestial.texture");
        }
        if (!Enum.IsDefined(body.RisePhase) ||
            !Enum.IsDefined(body.SetPhase) ||
            body.RisePhase == body.SetPhase ||
            !float.IsFinite(body.RiseAzimuthDegrees) ||
            !float.IsFinite(body.SetAzimuthDegrees) ||
            !float.IsFinite(body.MaxAltitudeDegrees) ||
            body.MaxAltitudeDegrees is <= 0 or > 90 ||
            !float.IsFinite(body.Size) || body.Size <= 0 ||
            !float.IsFinite(body.OrbitRadius) || body.OrbitRadius <= 0)
        {
            throw new ArgumentException("Invalid celestial orbit definition.");
        }
    }
}

public readonly record struct DayNightClockState(
    ulong Day,
    ulong TickInDay);

public sealed class DayNightClock
{
    private readonly DayNightCycleDefinition _cycle;

    public DayNightClock(
        DayNightCycleDefinition cycle,
        DayNightClockState? restore = null)
    {
        _cycle = cycle ?? throw new ArgumentNullException(nameof(cycle));
        var initial = restore ?? new DayNightClockState(1, cycle.InitialTick);
        if (initial.Day == 0 || initial.TickInDay >= cycle.DayDurationTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(restore));
        }
        Day = initial.Day;
        TickInDay = initial.TickInDay;
    }

    public ulong Day { get; private set; }
    public ulong TickInDay { get; private set; }
    public DayNightSample Sample => _cycle.Sample(TickInDay);
    public (int Hour, int Minute) WorldTime => _cycle.WorldTime(TickInDay);
    public DayNightClockState CaptureState() => new(Day, TickInDay);

    public void Advance(ulong elapsedTicks)
    {
        if (elapsedTicks == 0) return;

        var duration = (UInt128)_cycle.DayDurationTicks;
        var shifted = ((UInt128)TickInDay + duration - _cycle.MidnightTick) % duration;
        var crossed = (shifted + elapsedTicks) / duration;
        Day = crossed >= (UInt128)ulong.MaxValue - Day
            ? ulong.MaxValue
            : Day + (ulong)crossed;
        TickInDay = (ulong)(((UInt128)TickInDay + elapsedTicks) % duration);
    }
}

public sealed class DayNightCycleRegistry
{
    private readonly Dictionary<string, DayNightCycleDefinition> _cycles;

    public DayNightCycleRegistry(IEnumerable<DayNightCycleDefinition> cycles)
    {
        _cycles = new Dictionary<string, DayNightCycleDefinition>(StringComparer.Ordinal);
        foreach (var cycle in cycles ?? throw new ArgumentNullException(nameof(cycles)))
        {
            if (!_cycles.TryAdd(cycle.Id, cycle))
            {
                throw new ArgumentException($"Duplicate day-night cycle {cycle.Id}.");
            }
        }
        if (_cycles.Count == 0)
        {
            throw new ArgumentException("Day-night cycle registry cannot be empty.");
        }
    }

    public DayNightCycleDefinition Get(string id) =>
        _cycles.TryGetValue(id, out var cycle)
            ? cycle
            : throw new KeyNotFoundException($"Unknown day-night cycle {id}.");

    public static DayNightCycleRegistry FromJson(IEnumerable<string> documents) =>
        new(documents.Select(DayNightCycleDefinitionJson.Parse));
}

public static class DayNightCycleDefinitionJson
{
    public static DayNightCycleDefinition Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var timings = root.GetProperty("phases");
        var phases = new Dictionary<DayNightPhase, DayNightPhaseTiming>();

        foreach (var phase in Enum.GetValues<DayNightPhase>())
        {
            var name = phase.ToString().ToLowerInvariant();
            var entry = timings.GetProperty(name);
            phases.Add(
                phase,
                new DayNightPhaseTiming(
                    entry.GetProperty("durationTicks").GetUInt64(),
                    entry.GetProperty("skyLightFactor").GetSingle()));
        }

        return new DayNightCycleDefinition(
            root.GetProperty("id").GetString()!,
            root.GetProperty("dayDurationTicks").GetUInt64(),
            root.GetProperty("initialTime").GetDouble(),
            root.GetProperty("worldTimeStartHour").GetDouble(),
            root.GetProperty("sequence").EnumerateArray().Select(
                element => Enum.Parse<DayNightPhase>(
                    element.GetString()!, ignoreCase: false)),
            phases,
            ParseCelestial(root, "sun"),
            ParseCelestial(root, "moon"));
    }

    private static DayNightCelestialDefinition? ParseCelestial(
        JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var body) ||
            body.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return new DayNightCelestialDefinition(
            Enum.Parse<DayNightPhase>(body.GetProperty("risePhase").GetString()!),
            Enum.Parse<DayNightPhase>(body.GetProperty("setPhase").GetString()!),
            body.GetProperty("riseAzimuthDegrees").GetSingle(),
            body.GetProperty("setAzimuthDegrees").GetSingle(),
            body.GetProperty("maxAltitudeDegrees").GetSingle(),
            body.GetProperty("size").GetSingle(),
            body.GetProperty("orbitRadius").GetSingle(),
            DimensionColor.ParseHex(
                body.GetProperty("tint").GetString()!,
                property + ".tint"),
            PackContentFields.OptionalString(body, "texture"));
    }
}
