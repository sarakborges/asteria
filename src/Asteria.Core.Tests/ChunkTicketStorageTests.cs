using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkTicketStorageTests
{
    [Fact]
    public void TicketFlagsAndLifetimesMatchMinecraft263Definitions()
    {
        AssertTicket(
            ChunkTicketType.PlayerLoading,
            ChunkTicketFlags.Loading,
            timeout: null);
        AssertTicket(
            ChunkTicketType.PlayerSimulation,
            ChunkTicketFlags.Simulation |
            ChunkTicketFlags.KeepDimensionActive,
            timeout: null);
        AssertTicket(
            ChunkTicketType.Forced,
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.Simulation |
            ChunkTicketFlags.Persist |
            ChunkTicketFlags.KeepDimensionActive,
            timeout: null);
        AssertTicket(
            ChunkTicketType.Portal,
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.Simulation |
            ChunkTicketFlags.Persist |
            ChunkTicketFlags.KeepDimensionActive,
            timeout: 300);
        AssertTicket(
            ChunkTicketType.EnderPearl,
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.Simulation |
            ChunkTicketFlags.KeepDimensionActive,
            timeout: 40);
        AssertTicket(
            ChunkTicketType.Dragon,
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.Simulation,
            timeout: null);
        AssertTicket(
            ChunkTicketType.PlayerSpawn,
            ChunkTicketFlags.Loading,
            timeout: 20);
        AssertTicket(
            ChunkTicketType.SpawnSearch,
            ChunkTicketFlags.Loading,
            timeout: 1);
        AssertTicket(
            ChunkTicketType.Unknown,
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.CanExpireIfUnloaded,
            timeout: 1);
    }

    [Fact]
    public void PortalTicketCountdownPausesWhileHolderIsNotReadyForSaving()
    {
        var storage = new ChunkTicketStorage();
        var column = new ChunkColumnCoord(4, -3);
        var ticket = new ChunkTicket(ChunkTicketType.Portal, level: 30);
        Assert.True(storage.AddTicket(column, ticket, currentTick: 100));

        // 300 ticks elapsed, but the holder is still generating. The full
        // interval is paused rather than expiring a ticket under active work.
        Assert.Equal(
            ChunkTicketFlags.None,
            storage.ExpireTickets(400, _ => false));
        Assert.Contains(ticket, storage.GetTickets(column));

        // The 300 active countdown ticks begin only after the holder becomes
        // ready for saving: expiry is now at tick 700, not tick 400.
        Assert.Equal(
            ChunkTicketFlags.None,
            storage.ExpireTickets(699, _ => true));
        Assert.Contains(ticket, storage.GetTickets(column));

        Assert.Equal(
            ChunkTicketFlags.Loading | ChunkTicketFlags.Simulation,
            storage.ExpireTickets(700, _ => true));
        Assert.DoesNotContain(ticket, storage.GetTickets(column));
    }

    [Fact]
    public void UnknownTicketExpiresEvenWhileHolderIsNotReadyForSaving()
    {
        var storage = new ChunkTicketStorage();
        var column = new ChunkColumnCoord(-8, 2);
        var ticket = new ChunkTicket(ChunkTicketType.Unknown, level: 33);
        Assert.True(storage.AddTicket(column, ticket, currentTick: 12));

        Assert.Equal(
            ChunkTicketFlags.Loading,
            storage.ExpireTickets(13, _ => false));
        Assert.DoesNotContain(ticket, storage.GetTickets(column));
    }

    [Fact]
    public void TimedTicketReadditionResetsItsRemainingLifetime()
    {
        var storage = new ChunkTicketStorage();
        var column = new ChunkColumnCoord(7, 9);
        var ticket = new ChunkTicket(ChunkTicketType.EnderPearl, level: 30);
        Assert.True(storage.AddTicket(column, ticket, currentTick: 10));

        Assert.Equal(
            ChunkTicketFlags.None,
            storage.ExpireTickets(30, _ => true));
        Assert.False(storage.AddTicket(column, ticket, currentTick: 30));
        Assert.Equal(
            ChunkTicketFlags.None,
            storage.ExpireTickets(69, _ => true));
        Assert.Equal(
            ChunkTicketFlags.Loading | ChunkTicketFlags.Simulation,
            storage.ExpireTickets(70, _ => true));
    }

    [Fact]
    public void TimedTicketExpiryRejectsWorldTickMovingBackwards()
    {
        var storage = new ChunkTicketStorage();
        var ticket = new ChunkTicket(ChunkTicketType.Portal, level: 30);
        var column = new ChunkColumnCoord(0, 0);
        storage.AddTicket(column, ticket, currentTick: 25);

        Assert.Throws<InvalidOperationException>(
            () => storage.ExpireTickets(24, _ => true));
    }

    [Fact]
    public void HolderIsReadyForSavingOnlyWhenFullAndNoGenerationClaimRemains()
    {
        var manager = new ChunkDistanceManager();
        var column = new ChunkColumnCoord(3, -4);
        Assert.True(manager.AddPlayerLoadingTicket(column));
        var holder = Assert.IsType<ChunkHolder>(manager.GetHolder(column));
        Assert.False(holder.IsReadyForSaving);

        Assert.True(manager.CompleteFullStatus(column, ChunkFullStatus.Full));
        Assert.True(holder.IsReadyForSaving);

        using var claim = manager.AcquireDependencyClaims(
            [new ChunkCoord(column.X, 0, column.Z)],
            ChunkGenerationDependencyEnvelope.None);
        Assert.False(holder.IsReadyForSaving);

        claim.Dispose();
        Assert.True(holder.IsReadyForSaving);
    }

    private static void AssertTicket(
        ChunkTicketType type,
        ChunkTicketFlags flags,
        ulong? timeout)
    {
        var ticket = new ChunkTicket(type, level: 31);
        Assert.Equal(flags, ticket.Flags);
        Assert.Equal(timeout, ticket.TimeoutTicks);
    }
}
