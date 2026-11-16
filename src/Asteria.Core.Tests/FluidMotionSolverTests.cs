using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FluidMotionSolverTests
{
    private static readonly FluidBodyContact DeepWater =
        new(true, false, new FluidRuntimeId(1), 8f, 4f);

    [Fact]
    public void PassiveImmersionSinksWithoutSwimInput()
    {
        var speed = FluidMotionSolver.VerticalSpeed(
            currentSpeed: 0f,
            deltaSeconds: 0.1f,
            DeepWater,
            FluidMotionDefinition.Default,
            ascendRequested: false,
            descendRequested: false);

        Assert.Equal(-0.6f, speed, 4);
    }

    [Fact]
    public void ExplicitDescentUsesAuthoredSinkSpeed()
    {
        var speed = FluidMotionSolver.VerticalSpeed(
            currentSpeed: 0f,
            deltaSeconds: 1f,
            DeepWater,
            FluidMotionDefinition.Default,
            ascendRequested: false,
            descendRequested: true);

        Assert.Equal(-3f, speed, 4);
    }

    [Fact]
    public void JumpInputIsRequiredForUpwardSwimming()
    {
        var speed = FluidMotionSolver.VerticalSpeed(
            currentSpeed: 0f,
            deltaSeconds: 0.1f,
            DeepWater,
            FluidMotionDefinition.Default,
            ascendRequested: true,
            descendRequested: false);

        Assert.Equal(1.2f, speed, 4);
    }

    [Fact]
    public void PassiveImmersionBrakesAnExistingUpwardVelocity()
    {
        var speed = FluidMotionSolver.VerticalSpeed(
            currentSpeed: 1f,
            deltaSeconds: 0.1f,
            DeepWater,
            FluidMotionDefinition.Default,
            ascendRequested: false,
            descendRequested: false);

        Assert.Equal(-0.2f, speed, 4);
    }

    [Fact]
    public void UnimmersedPlayerKeepsCurrentVerticalSpeed()
    {
        var speed = FluidMotionSolver.VerticalSpeed(
            currentSpeed: 2.5f,
            deltaSeconds: 0.1f,
            default,
            FluidMotionDefinition.Default,
            ascendRequested: false,
            descendRequested: false);

        Assert.Equal(2.5f, speed, 4);
    }

    [Fact]
    public void JsonParsesAuthoredPassiveSinkSpeed()
    {
        const string json = """
            {
              "id": "asteria:water",
              "color": "4f9fd6",
              "opacity": 0.72,
              "motion": {
                "passiveSinkSpeed": 0.9
              }
            }
            """;

        var fluid = FluidDefinitionJson.Parse(json);

        Assert.Equal(0.9f, fluid.Motion.PassiveSinkSpeed, 4);
    }
}
