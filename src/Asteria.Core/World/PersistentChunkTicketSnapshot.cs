namespace Asteria.Core.World;

/// <summary>
/// One saved persistent ticket identity and, for timed tickets, its remaining
/// lifetime in world ticks at the snapshot boundary.
/// </summary>
public readonly record struct PersistentChunkTicketSnapshotEntry(
    ChunkColumnCoord Column,
    ChunkTicket Ticket,
    ulong? RemainingTicks);

/// <summary>
/// Immutable, validated snapshot of the ticket types marked Persist by their
/// authored ticket definitions. Player/view and transient tickets are
/// reconstructed by their owning runtime and are deliberately excluded.
/// </summary>
public sealed class PersistentChunkTicketSnapshot
{
    public const int MaximumEntries = 65536;

    private readonly IReadOnlyList<PersistentChunkTicketSnapshotEntry> _entries;

    public static PersistentChunkTicketSnapshot Empty { get; } =
        new(Array.Empty<PersistentChunkTicketSnapshotEntry>());

    public PersistentChunkTicketSnapshot(
        IEnumerable<PersistentChunkTicketSnapshotEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var captured = entries.Take(MaximumEntries + 1).ToArray();
        if (captured.Length > MaximumEntries)
            throw new InvalidDataException(
                "Persistent chunk ticket snapshot exceeds maximum capacity.");

        var identities = new HashSet<(ChunkColumnCoord Column, ChunkTicket Ticket)>();
        foreach (var entry in captured)
        {
            if (!Enum.IsDefined(entry.Ticket.Type) ||
                entry.Ticket.Level < 0 ||
                entry.Ticket.Level >= ChunkLevel.Unloaded ||
                entry.Ticket.SectionY is < 0 ||
                (entry.Ticket.Flags & ChunkTicketFlags.Persist) == 0 ||
                !identities.Add((entry.Column, entry.Ticket)))
                throw new InvalidDataException(
                    "Persistent chunk ticket snapshot contains an invalid or repeated ticket.");

            if (entry.Ticket.TimeoutTicks is { } timeout)
            {
                if (entry.RemainingTicks is not { } remaining ||
                    remaining > timeout)
                    throw new InvalidDataException(
                        "Timed persistent tickets require a valid remaining lifetime.");
            }
            else if (entry.RemainingTicks is not null)
            {
                throw new InvalidDataException(
                    "Permanent persistent tickets cannot have a remaining lifetime.");
            }
        }

        Array.Sort(captured, static (left, right) =>
        {
            var result = left.Column.X.CompareTo(right.Column.X);
            if (result != 0)
                return result;
            result = left.Column.Z.CompareTo(right.Column.Z);
            if (result != 0)
                return result;
            result = left.Ticket.Type.CompareTo(right.Ticket.Type);
            return result != 0
                ? result
                : left.Ticket.Level.CompareTo(right.Ticket.Level);
        });
        _entries = Array.AsReadOnly(captured);
    }

    public IReadOnlyList<PersistentChunkTicketSnapshotEntry> Entries =>
        _entries;
}
