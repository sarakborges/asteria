using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class MiningParticleRuntimeTests
{
    private static MiningFeedbackEvent Cue(bool completed = false) =>
        new(new VoxelWorldHit(new WorldVoxelCoord(2, 3, 4), 0, 1, 0),
            completed);

    [Fact]
    public void ParticlesSpawnOutsideHitFaceAndCompleteBurstIsLarger()
    {
        var runtime = new MiningParticleRuntime();
        runtime.Emit(Cue());
        Assert.Equal(3, runtime.Particles.Count);
        Assert.All(runtime.Particles, particle =>
        {
            Assert.InRange(particle.Position.Y, 4.014f, 4.016f);
            Assert.InRange(particle.Scale, 0.03f, 0.08f);
        });
        runtime.Emit(Cue(completed: true));
        Assert.Equal(14, runtime.Particles.Count);
        for (var i = 0; i < 20; i++)
            runtime.Advance(0.05f);
        Assert.Empty(runtime.Particles);
    }

    [Fact]
    public void ParticleCountIsBoundedAndResetDropsAllVisualState()
    {
        var runtime = new MiningParticleRuntime();
        for (var i = 0; i < 60; i++)
            runtime.Emit(Cue(completed: true));
        Assert.Equal(MiningParticleRuntime.MaximumActive, runtime.Particles.Count);
        runtime.Clear();
        Assert.Empty(runtime.Particles);
        runtime.Emit(Cue());
        Assert.Equal(3, runtime.Particles.Count);
    }

    [Fact]
    public void InvalidTimeNeverMutatesExistingParticles()
    {
        var runtime = new MiningParticleRuntime();
        runtime.Emit(Cue());
        var previous = runtime.Particles[0].Position;
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Advance(-0.01f));
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Advance(float.NaN));
        Assert.Equal(previous, runtime.Particles[0].Position);
    }
}
