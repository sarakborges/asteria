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
        var ticket = new ChunkTicket(ChunkTicketType.Portal, Level: 30);
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
    public void DistanceManagerPausesPortalExpiryUntilItsHolderIsSaveReady()
    {
        var manager = new ChunkDistanceManager();
        var column = new ChunkColumnCoord(6, -2);
        var portal = new ChunkTicket(ChunkTicketType.Portal, Level: 30);
        Assert.True(manager.AddTicket(column, portal, currentTick: 100));
        Assert.NotNull(manager.GetHolder(column));

        Assert.Equal(ChunkTicketFlags.None, manager.AdvanceTick(400));
        Assert.NotNull(manager.GetHolder(column));

        Assert.True(manager.CompleteFullStatus(column, ChunkFullStatus.Full));
        Assert.Equal(
            ChunkTicketFlags.Loading | ChunkTicketFlags.Simulation,
            manager.AdvanceTick(700));
        Assert.Null(manager.GetHolder(column));
    }

    [Fact]
    public void PersistentTicketsCaptureOnlyPersistentIdentitiesAndRestoreRemainingLifetime()
    {
        var storage = new ChunkTicketStorage();
        var column = new ChunkColumnCoord(4, -3);
        var portal = new ChunkTicket(ChunkTicketType.Portal, Level: 30, SectionY: 7);
        var forced = new ChunkTicket(ChunkTicketType.Forced, Level: 30, SectionY: 8);
        var transient = new ChunkTicket(ChunkTicketType.EnderPearl, Level: 30, SectionY: 9);
        Assert.True(storage.AddTicket(column, portal, currentTick: 100));
        Assert.True(storage.AddTicket(column, forced, currentTick: 100));
        Assert.True(storage.AddTicket(column, transient, currentTick: 100));

        Assert.Equal(
            ChunkTicketFlags.Loading | ChunkTicketFlags.Simulation,
            storage.ExpireTickets(150, _ => true));
        var snapshot = storage.CapturePersistentTickets();

        Assert.Equal(2, snapshot.Entries.Count);
        Assert.Contains(snapshot.Entries, entry =>
            entry.Ticket == portal && entry.RemainingTicks == 250UL);
        Assert.Contains(snapshot.Entries, entry =>
            entry.Ticket == forced && entry.RemainingTicks is null);
        Assert.DoesNotContain(snapshot.Entries, entry => entry.Ticket == transient);

        var restored = new ChunkTicketStorage();
        restored.RestorePersistentTickets(snapshot, currentTick: 1000);
        Assert.Equal(2, restored.TicketCount);
        Assert.Equal(ChunkTicketFlags.None,
            restored.ExpireTickets(1249, _ => true));
        Assert.Contains(portal, restored.GetTickets(column));
        Assert.Equal(
            ChunkTicketFlags.Loading | ChunkTicketFlags.Simulation,
            restored.ExpireTickets(1250, _ => true));
        Assert.DoesNotContain(portal, restored.GetTickets(column));
        Assert.Contains(forced, restored.GetTickets(column));
    }

    [Fact]
    public void LoadingTicketsExposeExactVerticalMaterializationTargets()
    {
        var storage = new ChunkTicketStorage();
        var column = new ChunkColumnCoord(4, -3);
        Assert.True(storage.AddTicket(column,
            new ChunkTicket(ChunkTicketType.Forced, Level: 30, SectionY: 7)));
        Assert.True(storage.AddTicket(column,
            new ChunkTicket(ChunkTicketType.Portal, Level: 30, SectionY: 12)));
        Assert.True(storage.AddTicket(column,
            new ChunkTicket(ChunkTicketType.PlayerSimulation, Level: 31, SectionY: 15)));

        Assert.Collection(storage.FullMaterializationTargets,
            section => Assert.Equal(new ChunkCoord(4, 7, -3), section),
            section => Assert.Equal(new ChunkCoord(4, 12, -3), section));
    }

    [Fact]
    public void PersistentTicketSnapshotRejectsTransientAndInvalidTimerState()
    {
        var column = new ChunkColumnCoord(1, 2);
        Assert.Throws<InvalidDataException>(() =>
            new PersistentChunkTicketSnapshot([
                new PersistentChunkTicketSnapshotEntry(
                    column,
                    new ChunkTicket(ChunkTicketType.EnderPearl, Level: 30),
                    20UL)]));
        Assert.Throws<InvalidDataException>(() =>
            new PersistentChunkTicketSnapshot([
                new PersistentChunkTicketSnapshotEntry(
                    column,
                    new ChunkTicket(ChunkTicketType.Portal, Level: 30),
                    null)]));
        Assert.Throws<InvalidDataException>(() =>
            new PersistentChunkTicketSnapshot([
                new PersistentChunkTicketSnapshotEntry(
                    column,
                    new ChunkTicket(ChunkTicketType.Portal, Level: 30),
                    301UL)]));
    }

    [Fact]
    public void DistanceManagerRestoresPersistentTicketsIntoBothTicketGraphs()
    {
        var column = new ChunkColumnCoord(-7, 5);
        var portal = new ChunkTicket(ChunkTicketType.Portal, Level: 30);
        var snapshot = new PersistentChunkTicketSnapshot([
            new PersistentChunkTicketSnapshotEntry(column, portal, 180UL)]);

        var manager = new ChunkDistanceManager();
        manager.RestorePersistentTickets(snapshot, currentTick: 900);

        Assert.Contains(portal, manager.Tickets.GetTickets(column));
        Assert.Equal(30, manager.GetLoadingLevel(column));
        Assert.Equal(30, manager.GetSimulationLevel(column));
        Assert.Equal(snapshot.Entries,
            manager.CapturePersistentTickets().Entries);
    }

    [Fact]
    public void UnknownTicketExpiresEvenWhileHolderIsNotReadyForSaving()
    {
        var storage = new ChunkTicketStorage();
        var column = new ChunkColumnCoord(-8, 2);
        var ticket = new ChunkTicket(ChunkTicketType.Unknown, Level: 33);
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
        var ticket = new ChunkTicket(ChunkTicketType.EnderPearl, Level: 30);
        Assert.True(storage.AddTicket(column, ticket, currentTick: 10));

        Assert.Equal(
            ChunkTicketFlags.None,
            storage.ExpireTickets(30, _ => true));
        Assert.True(storage.AddTicket(column, ticket, currentTick: 30));
        Assert.Equal(1, storage.TicketCount);
        Assert.Equal(ticket, Assert.Single(storage.GetTickets(column)));
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
        var ticket = new ChunkTicket(ChunkTicketType.Portal, Level: 30);
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

    [Fact]
    public void DependencyClaimReleasedAfterTicketArrivalKeepsHolder()
    {
        var manager = new ChunkDistanceManager();
        var column = new ChunkColumnCoord(8, -4);
        var claim = manager.AcquireDependencyClaims(
            [new ChunkCoord(column.X, 2, column.Z)],
            ChunkGenerationDependencyEnvelope.None);
        var retainedHolder = Assert.IsType<ChunkHolder>(
            manager.GetHolder(column));

        Assert.True(manager.AddPlayerLoadingTicket(column));
        Assert.Same(retainedHolder, manager.GetHolder(column));

        claim.Dispose();

        Assert.Same(retainedHolder, manager.GetHolder(column));
        Assert.Equal(0, retainedHolder.DependencyClaimCount);
        Assert.Equal(ChunkLevel.EntityTicking, retainedHolder.TicketLevel);
    }

    [Fact]
    public void GenerationAndFullStatusFuturesAreIndependent()
    {
        var manager = new ChunkDistanceManager();
        var column = new ChunkColumnCoord(2, -5);
        Assert.True(manager.AddPlayerLoadingTicket(column));

        var holder = Assert.IsType<ChunkHolder>(manager.GetHolder(column));
        Assert.IsAssignableFrom<GenerationChunkHolder>(holder);
        var generationFuture = holder.GetGenerationFuture(
            ChunkGenerationStatus.Full);
        var fullFuture = holder.FullChunkFuture;

        Assert.False(generationFuture.IsCompleted);
        Assert.False(fullFuture.IsCompleted);

        Assert.True(holder.CompleteGenerationStatus(
            ChunkGenerationStatus.Full));
        Assert.True(generationFuture.IsCompletedSuccessfully);
        Assert.True(generationFuture.Result.Succeeded);
        Assert.False(fullFuture.IsCompleted);

        Assert.True(holder.CompleteFullStatus(ChunkFullStatus.Full));
        Assert.True(fullFuture.IsCompletedSuccessfully);
        Assert.True(fullFuture.Result.Succeeded);
    }

    private static void AssertTicket(
        ChunkTicketType type,
        ChunkTicketFlags flags,
        ulong? timeout)
    {
        var ticket = new ChunkTicket(type, Level: 31);
        Assert.Equal(flags, ticket.Flags);
        Assert.Equal(timeout, ticket.TimeoutTicks);
    }
}
