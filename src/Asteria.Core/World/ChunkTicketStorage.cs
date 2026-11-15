namespace Asteria.Core.World;

/// <summary>
/// Authoritative set of ticket reasons per horizontal chunk column.
/// Re-adding an identical (type, level) ticket is a no-op, matching vanilla's
/// TicketStorage identity and replacement behavior.
/// </summary>
public sealed class ChunkTicketStorage
{
    private readonly Dictionary<ChunkColumnCoord, HashSet<ChunkTicket>> _tickets = [];
    private readonly Dictionary<(ChunkColumnCoord Column, ChunkTicket Ticket), TicketExpiration>
        _expirationTicks = [];
    private readonly HashSet<(ChunkColumnCoord Column, ChunkTicket Ticket)>
        _persistentTickets = [];
    private int _persistentTimedTicketCount;

    public int ColumnCount => _tickets.Count;

    public int TicketCount => _tickets.Values.Sum(tickets => tickets.Count);

    public int PersistentTicketCount => _persistentTickets.Count;

    public int PersistentTimedTicketCount => _persistentTimedTicketCount;

    public IEnumerable<(ChunkColumnCoord Column, ChunkTicket Ticket)> AllTickets =>
        _tickets.SelectMany(entry =>
            entry.Value.Select(ticket => (entry.Key, ticket)));

    public IReadOnlyCollection<ChunkTicket> GetTickets(ChunkColumnCoord column) =>
        _tickets.TryGetValue(column, out var tickets)
            ? tickets.ToArray()
            : Array.Empty<ChunkTicket>();

    public int GetLowestLevel(
        ChunkColumnCoord column,
        ChunkTicketFlags requiredFlags)
    {
        var lowest = int.MaxValue;
        if (!_tickets.TryGetValue(column, out var tickets))
            return lowest;

        foreach (var ticket in tickets)
        {
            if ((ticket.Flags & requiredFlags) == requiredFlags)
                lowest = Math.Min(lowest, ticket.Level);
        }

        return lowest;
    }

    public bool AddTicket(
        ChunkColumnCoord column,
        ChunkTicket ticket,
        ulong currentTick = 0)
    {
        if (!Enum.IsDefined(ticket.Type))
            throw new ArgumentOutOfRangeException(nameof(ticket));
        if (ticket.Level < 0 || ticket.Level >= ChunkLevel.Unloaded)
            throw new ArgumentOutOfRangeException(nameof(ticket));

        if (!_tickets.TryGetValue(column, out var tickets))
        {
            tickets = [];
            _tickets.Add(column, tickets);
        }

        var added = tickets.Add(ticket);
        if ((ticket.Flags & ChunkTicketFlags.Persist) != 0 &&
            _persistentTickets.Add((column, ticket)) &&
            ticket.TimeoutTicks is not null)
            _persistentTimedTicketCount++;

        if (ticket.TimeoutTicks is { } timeout)
        {
            var expiration = new TicketExpiration(
                SaturatingAdd(currentTick, timeout), currentTick);
            var key = (column, ticket);
            if (!_expirationTicks.TryGetValue(key, out var existing) ||
                expiration != existing)
            {
                // Re-adding the same ticket resets its countdown just as
                // Ticket.resetTicksLeft does in Minecraft.
                _expirationTicks[key] = expiration;
                added = true;
            }
        }

        return added;
    }

    /// <summary>
    /// Captures only tickets whose authored flags make them survive session
    /// recreation. Timed tickets store their remaining lifetime, not an
    /// absolute world tick, so the snapshot is independent of the next session.
    /// </summary>
    public PersistentChunkTicketSnapshot CapturePersistentTickets()
    {
        if (_persistentTickets.Count == 0)
            return PersistentChunkTicketSnapshot.Empty;

        var entries = new List<PersistentChunkTicketSnapshotEntry>(
            _persistentTickets.Count);
        foreach (var identity in _persistentTickets)
        {
            ulong? remainingTicks = null;
            if (identity.Ticket.TimeoutTicks is not null)
            {
                if (!_expirationTicks.TryGetValue(identity, out var expiration))
                    throw new InvalidOperationException(
                        $"Persistent timed ticket has no expiration state: {identity}.");

                remainingTicks = expiration.ExpiresAtTick > expiration.LastEvaluatedTick
                    ? expiration.ExpiresAtTick - expiration.LastEvaluatedTick
                    : 0UL;
            }

            entries.Add(new PersistentChunkTicketSnapshotEntry(
                identity.Column, identity.Ticket, remainingTicks));
        }

        return new PersistentChunkTicketSnapshot(entries);
    }

    /// <summary>
    /// Restores a persistent-ticket snapshot into an empty store. Remaining
    /// timeout ticks are rebased onto the new session clock without resetting
    /// the ticket's lifetime.
    /// </summary>
    public void RestorePersistentTickets(
        PersistentChunkTicketSnapshot snapshot,
        ulong currentTick)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (TicketCount != 0 || _expirationTicks.Count != 0 ||
            _persistentTickets.Count != 0)
            throw new InvalidOperationException(
                "Persistent tickets can only be restored into an empty ticket store.");

        foreach (var entry in snapshot.Entries)
        {
            if (!_tickets.TryGetValue(entry.Column, out var tickets))
            {
                tickets = [];
                _tickets.Add(entry.Column, tickets);
            }

            if (!tickets.Add(entry.Ticket))
                throw new InvalidDataException(
                    $"Persistent ticket snapshot contains a duplicate identity: {entry.Column}.");

            _persistentTickets.Add((entry.Column, entry.Ticket));
            if (entry.RemainingTicks is { } remainingTicks)
            {
                _persistentTimedTicketCount++;
                _expirationTicks.Add(
                    (entry.Column, entry.Ticket),
                    new TicketExpiration(
                        SaturatingAdd(currentTick, remainingTicks),
                        currentTick));
            }
        }
    }

    /// <summary>
    /// Advances timed ticket countdowns. Non-UNKNOWN tickets pause while their
    /// holder is neither absent nor ready for saving. Other identities in the
    /// same column remain untouched. The returned flags tell DistanceManager
    /// which distance graphs need to be propagated after an expiry.
    /// </summary>
    public ChunkTicketFlags ExpireTickets(
        ulong currentTick,
        Func<ChunkColumnCoord, bool> isHolderReadyForSaving)
    {
        ArgumentNullException.ThrowIfNull(isHolderReadyForSaving);
        var changed = ChunkTicketFlags.None;
        foreach (var (identity, expiration) in _expirationTicks.ToArray())
        {
            if (currentTick < expiration.LastEvaluatedTick)
                throw new InvalidOperationException(
                    "Chunk ticket expiration ticks must be monotonic.");

            var elapsed = currentTick - expiration.LastEvaluatedTick;
            var mayExpireWhileLoaded =
                (identity.Ticket.Flags & ChunkTicketFlags.CanExpireIfUnloaded) != 0;
            var countdownRuns = mayExpireWhileLoaded ||
                isHolderReadyForSaving(identity.Column);

            if (!countdownRuns)
            {
                // Non-UNKNOWN timed tickets pause while a holder is still
                // being generated or retained by another generation task.
                _expirationTicks[identity] = new TicketExpiration(
                    SaturatingAdd(expiration.ExpiresAtTick, elapsed), currentTick);
                continue;
            }

            if (currentTick < expiration.ExpiresAtTick)
            {
                _expirationTicks[identity] = expiration with
                {
                    LastEvaluatedTick = currentTick,
                };
                continue;
            }

            if (RemoveTicket(identity.Column, identity.Ticket))
                changed |= identity.Ticket.Flags &
                    (ChunkTicketFlags.Loading | ChunkTicketFlags.Simulation);
        }

        return changed;
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private readonly record struct TicketExpiration(
        ulong ExpiresAtTick,
        ulong LastEvaluatedTick);

    public bool RemoveTicket(ChunkColumnCoord column, ChunkTicket ticket)
    {
        if (!_tickets.TryGetValue(column, out var tickets) ||
            !tickets.Remove(ticket))
            return false;

        _expirationTicks.Remove((column, ticket));
        if (_persistentTickets.Remove((column, ticket)) &&
            ticket.TimeoutTicks is not null)
            _persistentTimedTicketCount--;
        if (tickets.Count == 0)
            _tickets.Remove(column);
        return true;
    }

    public bool RemoveTicketsWhere(
        ChunkTicketType type,
        Predicate<ChunkColumnCoord> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));

        var removedAny = false;
        foreach (var (column, tickets) in _tickets.ToArray())
        {
            if (!predicate(column))
                continue;

            foreach (var ticket in tickets.Where(ticket => ticket.Type == type).ToArray())
                removedAny |= RemoveTicket(column, ticket);
        }

        return removedAny;
    }

    /// <summary>
    /// Replaces every ticket of one type with a new authoritative set.
    /// Other ticket types on the same columns are preserved.
    /// </summary>
    public bool ReplaceTickets(
        ChunkTicketType type,
        IEnumerable<ChunkTicketPlacement> replacements,
        ulong currentTick = 0)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));

        var desired = replacements
            .Where(item => item.Ticket.Type == type)
            .ToHashSet();
        var current = AllTickets
            .Where(item => item.Ticket.Type == type)
            .Select(item => new ChunkTicketPlacement(item.Column, item.Ticket))
            .ToArray();

        var changed = false;
        foreach (var existing in current)
        {
            if (!desired.Contains(existing))
                changed |= RemoveTicket(existing.Column, existing.Ticket);
        }

        foreach (var replacement in desired)
        {
            if (!AddTicket(replacement.Column, replacement.Ticket, currentTick))
                continue;
            changed = true;
        }

        return changed;
    }
}

/// <summary>
/// Propagates the minimum ticket level outwards through the eight neighboring
/// columns. The priority queue is ordered by level then coordinates, so graph
/// results are deterministic regardless of dictionary enumeration order.
/// </summary>
