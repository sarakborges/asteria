using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class DayNightCycleTests
{
    private static DayNightCycleDefinition Overworld() =>
        DayNightCycleDefinitionJson.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "packs",
                    "default",
                    "data",
                    "day_night_cycles",
                    "overworld.json")));

    [Fact]
    public void DefaultPacksDeclareValidIndependentCycles()
    {
        var dataPath = Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data");
        var cycles = DayNightCycleRegistry.FromJson(
            Directory.EnumerateFiles(
                Path.Combine(dataPath, "day_night_cycles"), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
        var dimensions = DimensionRegistry.FromJson(
            Directory.EnumerateFiles(
                Path.Combine(dataPath, "dimensions"), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

        dimensions.ValidateDayNightCycles(cycles);
        Assert.Equal(
            "asteria:overworld/day_night_cycle",
            dimensions.Get(DimensionId.Overworld).DayNightCycleId);
        Assert.Equal(
            "asteria:umbral/day_night_cycle",
            dimensions.Get(new DimensionId("asteria:umbral")).DayNightCycleId);
    }

    [Fact]
    public void OverworldHasAuthoredTwentyMinuteCycleAndPhases()
    {
        var cycle = Overworld();
        Assert.Equal(48_000UL, cycle.DayDurationTicks);
        Assert.Equal(0UL, cycle.InitialTick);
        Assert.Equal((6, 0), cycle.WorldTime(0));
        Assert.Equal((12, 0), cycle.WorldTime(12_000));
        Assert.Equal((18, 0), cycle.WorldTime(24_000));
        Assert.Equal((0, 0), cycle.WorldTime(36_000));

        Assert.Equal(DayNightPhase.Dawn, cycle.Sample(0).Phase);
        Assert.Equal(DayNightPhase.Day, cycle.Sample(12_000).Phase);
        Assert.Equal(DayNightPhase.Dusk, cycle.Sample(24_000).Phase);
        Assert.Equal(DayNightPhase.Night, cycle.Sample(36_000).Phase);
        Assert.Equal(0.35f, cycle.Sample(0).SkyLightFactor);
        Assert.Equal(1f, cycle.Sample(12_000).SkyLightFactor);
        Assert.Equal(0.06f, cycle.Sample(36_000).SkyLightFactor);
        Assert.Equal(36_000UL, cycle.MidnightTick);
    }

    [Fact]
    public void CelestialArtUsesValidatedSelectedPackPaths()
    {
        var cycle = Overworld();
        Assert.Equal("textures/sky/sun.png", cycle.Sun?.Texture);
        Assert.Equal("textures/sky/moon.png", cycle.Moon?.Texture);

        var authored = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "packs", "default",
                "data", "day_night_cycles", "overworld.json"));

        Assert.Throws<FormatException>(() =>
            DayNightCycleDefinitionJson.Parse(
                authored.Replace("textures/sky/sun.png",
                    "../sun.png", StringComparison.Ordinal)));
        Assert.Throws<FormatException>(() =>
            DayNightCycleDefinitionJson.Parse(
                authored.Replace("textures/sky/moon.png",
                    "C:/moon.png", StringComparison.Ordinal)));
        Assert.Throws<FormatException>(() =>
            DayNightCycleDefinitionJson.Parse(
                authored.Replace("textures/sky/moon.png",
                    "", StringComparison.Ordinal)));
    }

    [Fact]
    public void SunAndMoonFollowAuthoredPhasesAndWorldAxes()
    {
        var cycle = Overworld();
        Assert.NotNull(cycle.Sun);
        Assert.NotNull(cycle.Moon);

        // Rising at azimuth 90 begins on +X. Zenith at 180-degree arc
        // faces -Z; this is the same path for both engine adapters.
        var rise = CelestialPath.Direction(cycle.Sun!, cycle, 0);
        Assert.NotNull(rise);
        Assert.InRange(rise.Value.X, 0.9999f, 1.0001f);
        Assert.InRange(MathF.Abs(rise.Value.Y), 0f, 0.0001f);
        Assert.InRange(MathF.Abs(rise.Value.Z), 0f, 0.0001f);

        var noon = CelestialPath.Direction(cycle.Sun!, cycle, 12_000);
        Assert.NotNull(noon);
        Assert.InRange(noon.Value.Y, 0.90f, 1f);
        Assert.True(noon.Value.Z < 0f);
        Assert.InRange(noon.Value.Length(), 0.9999f, 1.0001f);
        Assert.Null(CelestialPath.Direction(cycle.Sun!, cycle, 24_000));
        Assert.Null(CelestialPath.Direction(cycle.Moon!, cycle, 12_000));
        Assert.NotNull(CelestialPath.Direction(cycle.Moon!, cycle, 36_000));
        Assert.Null(CelestialPath.Direction(cycle.Moon!, cycle, 0));
    }

    [Fact]
    public void CelestialAzimuthTakesShortestPathAcrossZeroDegrees()
    {
        var cycle = Overworld();
        var crossZero = cycle.Sun! with
        {
            RiseAzimuthDegrees = 350f,
            SetAzimuthDegrees = 10f,
            MaxAltitudeDegrees = 60f,
        };

        var start = CelestialPath.Direction(crossZero, cycle, 0)!.Value;
        var middle = CelestialPath.Direction(crossZero, cycle, 12_000)!.Value;
        var finish = CelestialPath.Direction(crossZero, cycle, 23_999)!.Value;

        Assert.True(start.X < 0f);
        Assert.True(finish.X > 0f);
        Assert.InRange(MathF.Abs(middle.X), 0f, 0.0001f);
        Assert.True(middle.Z < -0.49f);
        Assert.InRange(middle.Y, 0.865f, 0.867f);
        Assert.InRange(finish.Length(), 0.9999f, 1.0001f);

        // A cycle advanced across phase boundaries must use its restored
        // authoritative TickInDay, not free-running render elapsed time.
        var clock = new DayNightClock(cycle);
        clock.Advance(12_000);
        Assert.Equal(middle,
            CelestialPath.Direction(crossZero, cycle, clock.TickInDay)!.Value);
    }

    [Fact]
    public void ClockCrossesMidnightAndRestoresExactly()
    {
        var cycle = Overworld();
        var clock = new DayNightClock(cycle);

        clock.Advance(35_999);
        Assert.Equal(1UL, clock.Day);
        Assert.Equal((23, 59), clock.WorldTime);

        clock.Advance(1);
        Assert.Equal(2UL, clock.Day);
        Assert.Equal((0, 0), clock.WorldTime);

        var saved = clock.CaptureState();
        var restored = new DayNightClock(cycle, saved);
        Assert.Equal(saved, restored.CaptureState());

        restored.Advance(48_000 * 3 + 12_000);
        Assert.Equal(5UL, restored.Day);
        Assert.Equal(0UL, restored.TickInDay);
        Assert.Equal((6, 0), restored.WorldTime);
    }

    [Fact]
    public void ClockHandlesLargeTickBatchesWithoutOverflow()
    {
        var cycle = Overworld();
        var clock = new DayNightClock(cycle);
        clock.Advance(ulong.MaxValue);
        Assert.InRange(clock.TickInDay, 0UL, cycle.DayDurationTicks - 1);
        Assert.True(clock.Day > 1);
    }

    [Fact]
    public void PhaseProgressAndSkyLightAreContinuous()
    {
        var cycle = Overworld();
        var beforeDay = cycle.Sample(11_999);
        var atDay = cycle.Sample(12_000);
        Assert.True(beforeDay.SkyLightFactor <= atDay.SkyLightFactor);
        Assert.InRange(atDay.SkyLightFactor - beforeDay.SkyLightFactor, 0, 0.001f);

        Assert.Equal(0d, cycle.ProgressBetweenPhases(
            0, DayNightPhase.Dawn, DayNightPhase.Day));
        Assert.Null(cycle.ProgressBetweenPhases(
            24_000, DayNightPhase.Dawn, DayNightPhase.Day));
        Assert.NotNull(cycle.ProgressBetweenPhases(
            24_000, DayNightPhase.Dusk, DayNightPhase.Night));
    }

    [Fact]
    public void InvalidCyclesAndRestoresAreRejected()
    {
        var cycle = Overworld();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DayNightClock(
                cycle, new DayNightClockState(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DayNightClock(
                cycle, new DayNightClockState(1, 48_000)));

        var invalidJson = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory, "packs", "default",
                "data", "day_night_cycles", "overworld.json"))
            .Replace(
                "\"durationTicks\": 12000",
                "\"durationTicks\": 11999",
                StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(
            () => DayNightCycleDefinitionJson.Parse(invalidJson));
    }

    [Fact]
    public void SessionSnapshotsStayIndependentBetweenSpheres()
    {
        var cycle = Overworld();
        var overworld = new DayNightClock(cycle);
        var umbral = new DayNightClock(cycle);
        overworld.Advance(36_000);

        Assert.Equal(2UL, overworld.Day);
        Assert.Equal(1UL, umbral.Day);

        var retained = overworld.CaptureState();
        umbral.Advance(12_000);
        Assert.Equal(retained, new DayNightClock(cycle, retained).CaptureState());
    }
}
