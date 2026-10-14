using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerMovementSpeedsTests
{
    [Fact]
    public void MineCloneGroundAndFlightSpeedsMatchAuthoredConstants()
    {
        Assert.Equal(5f, PlayerMovementSpeeds.Walk);
        Assert.Equal(7.5f, PlayerMovementSpeeds.Run);
        Assert.Equal(25f, PlayerMovementSpeeds.Flight);
        Assert.Equal(7f, PlayerMovementSpeeds.Jump);
        Assert.Equal(1.5f, PlayerGroundMovement.RunSpeedMultiplier);
        Assert.Equal(0.3f, PlayerGroundMovement.CrouchSpeedMultiplier);
    }
}
