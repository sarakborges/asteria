using System.Collections.Immutable;

namespace Asteria.Core.World;

/// <summary>
/// Stable identifiers for the logical steps that produce a playable world section.
/// These identify generation stages, not worker jobs or persisted chunk data.
/// </summary>
public enum ChunkGenerationStepId : byte
{
    ColumnBiomeData,
    SurfaceTerrainColumn,
    SectionTerrainSetup,
    SectionTerrainLayers,
    SurfaceDecorations,
    DirectionalFacePalettes,
    VolumeStructures,
    GeneratedFluids,
    SurfaceStructures,
    CaveSpikes,
    VerticalDecorations,
    Veins,
    WallLayers,
    WallObjects,
    ShellFinalization,
    GravitySupport,
    StructureObjects,
    PendingObjects,
    InitialLighting,
    SectionReady,
}

/// <summary>Execution ownership required by a generation step.</summary>
public enum ChunkGenerationExecutionPolicy : byte
{
    /// <summary>Short state transitions owned by the serial generation coordinator.</summary>
    Coordinator,
    /// <summary>CPU work over immutable inputs, admitted by the shared worker pool.</summary>
    SharedWorkerPool,
    /// <summary>Ordered mutation or deterministic application of generated write plans.</summary>
    OrderedMutation,
    /// <summary>Integration that must execute on the engine/main thread.</summary>
    MainThreadIntegration,
}

/// <summary>Spatial identity over which a generation step is coordinated.</summary>
public enum ChunkGenerationWorkScope : byte
{
    /// <summary>A horizontal X/Z column that may coordinate multiple vertical sections.</summary>
    Column,
    /// <summary>One existing 16x16x16 Asteria storage section.</summary>
    Section,
}

/// <summary>
/// Conservative spatial envelope around a target section.
/// Horizontal radius is a Chebyshev radius in X/Z columns; vertical offsets
/// are inclusive section-Y offsets relative to the target section.
/// </summary>
public readonly record struct ChunkGenerationDependencyEnvelope
{
    public ChunkGenerationDependencyEnvelope(
        int horizontalRadius,
        int minimumSectionOffset,
        int maximumSectionOffset)
    {
        if (horizontalRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(horizontalRadius));
        if (minimumSectionOffset > maximumSectionOffset)
            throw new ArgumentOutOfRangeException(nameof(minimumSectionOffset));

        HorizontalRadius = horizontalRadius;
        MinimumSectionOffset = minimumSectionOffset;
        MaximumSectionOffset = maximumSectionOffset;
    }

    public int HorizontalRadius { get; }

    public int MinimumSectionOffset { get; }

    public int MaximumSectionOffset { get; }

    public static ChunkGenerationDependencyEnvelope None => new(0, 0, 0);

    /// <summary>
    /// Returns the conservative envelope produced by following one dependency
    /// from this envelope into a nested dependency.
    /// </summary>
    public ChunkGenerationDependencyEnvelope Compose(
        ChunkGenerationDependencyEnvelope nested) =>
        new(
            checked(HorizontalRadius + nested.HorizontalRadius),
            checked(MinimumSectionOffset + nested.MinimumSectionOffset),
            checked(MaximumSectionOffset + nested.MaximumSectionOffset));

    /// <summary>Returns the smallest supported envelope containing both inputs.</summary>
    public ChunkGenerationDependencyEnvelope Union(
        ChunkGenerationDependencyEnvelope other) =>
        new(
            Math.Max(HorizontalRadius, other.HorizontalRadius),
            Math.Min(MinimumSectionOffset, other.MinimumSectionOffset),
            Math.Max(MaximumSectionOffset, other.MaximumSectionOffset));

    public bool ContainsOffset(int deltaX, int sectionYOffset, int deltaZ) =>
        Math.Max(Math.Abs((long)deltaX), Math.Abs((long)deltaZ)) <= HorizontalRadius &&
        sectionYOffset >= MinimumSectionOffset &&
        sectionYOffset <= MaximumSectionOffset;
}

/// <summary>
/// A direct prerequisite of a step and the spatial envelope in which that
/// prerequisite must be available.
/// </summary>
public readonly record struct ChunkGenerationStepDependency
{
    public ChunkGenerationStepDependency(
        ChunkGenerationStepId prerequisiteStep,
        ChunkGenerationDependencyEnvelope envelope)
    {
        if (!Enum.IsDefined(prerequisiteStep))
            throw new ArgumentOutOfRangeException(nameof(prerequisiteStep));

        PrerequisiteStep = prerequisiteStep;
        Envelope = envelope;
    }

    public ChunkGenerationStepId PrerequisiteStep { get; }

    public ChunkGenerationDependencyEnvelope Envelope { get; }
}

/// <summary>
/// Immutable description of one generation step. Dependency order and
/// accumulated dependency closure are validated by ChunkGenerationPlan.
/// </summary>
public sealed class ChunkGenerationStepDefinition
{
    public ChunkGenerationStepDefinition(
        ChunkGenerationStepId id,
        ChunkGenerationWorkScope workScope,
        ChunkGenerationExecutionPolicy executionPolicy,
        IEnumerable<ChunkGenerationStepDependency> directDependencies,
        ChunkGenerationDependencyEnvelope readEnvelope,
        ChunkGenerationDependencyEnvelope writeEnvelope)
    {
        if (!Enum.IsDefined(id))
            throw new ArgumentOutOfRangeException(nameof(id));
        if (!Enum.IsDefined(workScope))
            throw new ArgumentOutOfRangeException(nameof(workScope));
        if (!Enum.IsDefined(executionPolicy))
            throw new ArgumentOutOfRangeException(nameof(executionPolicy));

        ArgumentNullException.ThrowIfNull(directDependencies);
        var dependencies = directDependencies.ToImmutableArray();
        if (dependencies.Select(item => item.PrerequisiteStep).Distinct().Count() !=
            dependencies.Length)
        {
            throw new ArgumentException(
                "A generation step may declare each direct prerequisite only once.",
                nameof(directDependencies));
        }

        Id = id;
        WorkScope = workScope;
        ExecutionPolicy = executionPolicy;
        DirectDependencies = dependencies;
        ReadEnvelope = readEnvelope;
        WriteEnvelope = writeEnvelope;
    }

    public ChunkGenerationStepId Id { get; }

    public ChunkGenerationWorkScope WorkScope { get; }

    public ChunkGenerationExecutionPolicy ExecutionPolicy { get; }

    public ImmutableArray<ChunkGenerationStepDependency> DirectDependencies { get; }

    public ChunkGenerationDependencyEnvelope ReadEnvelope { get; }

    public ChunkGenerationDependencyEnvelope WriteEnvelope { get; }
}

/// <summary>
/// Immutable, validated generation-step graph. Independent steps retain the
/// declaration order supplied to the constructor; prerequisites are always
/// emitted before their dependents. Accumulated dependencies are the
/// transitive, spatially composed prerequisite envelopes.
/// </summary>
public sealed class ChunkGenerationPlan
{
    private readonly ImmutableDictionary<ChunkGenerationStepId,
        ChunkGenerationStepDefinition> _stepsById;
    private readonly ImmutableDictionary<ChunkGenerationStepId,
        ImmutableArray<ChunkGenerationStepDependency>> _accumulatedDependencies;

    public ChunkGenerationPlan(
        IEnumerable<ChunkGenerationStepDefinition> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var declared = steps.ToImmutableArray();
        if (declared.IsDefaultOrEmpty)
            throw new ArgumentException(
                "A generation plan must contain at least one step.",
                nameof(steps));

        var declaredById = ImmutableDictionary.CreateBuilder<
            ChunkGenerationStepId, ChunkGenerationStepDefinition>();
        var declaredIndex = new Dictionary<ChunkGenerationStepId, int>();
        for (var index = 0; index < declared.Length; index++)
        {
            var step = declared[index] ??
                throw new ArgumentException(
                    "A generation plan cannot contain a null step.",
                    nameof(steps));
            if (!declaredById.TryAdd(step.Id, step))
                throw new ArgumentException(
                    $"Generation step {step.Id} is declared more than once.",
                    nameof(steps));
            declaredIndex.Add(step.Id, index);
        }

        foreach (var step in declared)
        foreach (var dependency in step.DirectDependencies)
        {
            if (!declaredById.ContainsKey(dependency.PrerequisiteStep))
                throw new ArgumentException(
                    $"Step {step.Id} depends on missing step {dependency.PrerequisiteStep}.",
                    nameof(steps));
        }

        Steps = TopologicallyOrder(
            declared,
            declaredIndex,
            declaredById.ToImmutable());

        var orderedIndex = Steps
            .Select((step, index) => (step.Id, index))
            .ToDictionary(item => item.Id, item => item.index);
        _stepsById = Steps.ToImmutableDictionary(step => step.Id);
        _accumulatedDependencies = BuildAccumulatedDependencies(
            Steps, orderedIndex);
    }

    public ImmutableArray<ChunkGenerationStepDefinition> Steps { get; }

    public ChunkGenerationStepDefinition GetStep(ChunkGenerationStepId id) =>
        _stepsById.TryGetValue(id, out var step)
            ? step
            : throw new KeyNotFoundException($"Generation step is not in this plan: {id}");

    /// <summary>
    /// Gets every direct and transitive prerequisite for a step. Envelopes are
    /// composed along dependency paths and unioned where multiple paths reach
    /// the same prerequisite.
    /// </summary>
    public ImmutableArray<ChunkGenerationStepDependency> GetAccumulatedDependencies(
        ChunkGenerationStepId id) =>
        _accumulatedDependencies.TryGetValue(id, out var dependencies)
            ? dependencies
            : throw new KeyNotFoundException($"Generation step is not in this plan: {id}");

    private static ImmutableArray<ChunkGenerationStepDefinition> TopologicallyOrder(
        ImmutableArray<ChunkGenerationStepDefinition> declared,
        IReadOnlyDictionary<ChunkGenerationStepId, int> declaredIndex,
        ImmutableDictionary<ChunkGenerationStepId, ChunkGenerationStepDefinition> byId)
    {
        var incoming = declared.ToDictionary(
            step => step.Id,
            step => step.DirectDependencies.Length);
        var dependents = declared.ToDictionary(
            step => step.Id,
            _ => new List<ChunkGenerationStepId>());

        foreach (var step in declared)
        foreach (var dependency in step.DirectDependencies)
            dependents[dependency.PrerequisiteStep].Add(step.Id);

        var ready = new SortedSet<int>(
            declared.Where(step => incoming[step.Id] == 0)
                .Select(step => declaredIndex[step.Id]));
        var ordered = ImmutableArray.CreateBuilder<ChunkGenerationStepDefinition>(
            declared.Length);

        while (ready.Count > 0)
        {
            var index = ready.Min;
            ready.Remove(index);
            var step = declared[index];
            ordered.Add(byId[step.Id]);

            foreach (var dependent in dependents[step.Id])
            {
                incoming[dependent]--;
                if (incoming[dependent] == 0)
                    ready.Add(declaredIndex[dependent]);
            }
        }

        if (ordered.Count != declared.Length)
            throw new ArgumentException(
                "Generation step dependencies must form an acyclic graph.",
                nameof(declared));

        return ordered.MoveToImmutable();
    }

    private static ImmutableDictionary<ChunkGenerationStepId,
        ImmutableArray<ChunkGenerationStepDependency>> BuildAccumulatedDependencies(
        ImmutableArray<ChunkGenerationStepDefinition> ordered,
        IReadOnlyDictionary<ChunkGenerationStepId, int> orderedIndex)
    {
        var result = ImmutableDictionary.CreateBuilder<
            ChunkGenerationStepId,
            ImmutableArray<ChunkGenerationStepDependency>>();

        foreach (var step in ordered)
        {
            var envelopes = new Dictionary<
                ChunkGenerationStepId, ChunkGenerationDependencyEnvelope>();

            foreach (var direct in step.DirectDependencies)
            {
                AddOrUnion(envelopes, direct.PrerequisiteStep, direct.Envelope);

                if (!result.TryGetValue(direct.PrerequisiteStep, out var nested))
                    continue;

                foreach (var ancestor in nested)
                {
                    AddOrUnion(
                        envelopes,
                        ancestor.PrerequisiteStep,
                        direct.Envelope.Compose(ancestor.Envelope));
                }
            }

            var accumulated = envelopes
                .OrderBy(entry => orderedIndex[entry.Key])
                .Select(entry => new ChunkGenerationStepDependency(
                    entry.Key, entry.Value))
                .ToImmutableArray();
            result.Add(step.Id, accumulated);
        }

        return result.ToImmutable();
    }

    private static void AddOrUnion(
        IDictionary<ChunkGenerationStepId, ChunkGenerationDependencyEnvelope> target,
        ChunkGenerationStepId id,
        ChunkGenerationDependencyEnvelope envelope)
    {
        target[id] = target.TryGetValue(id, out var current)
            ? current.Union(envelope)
            : envelope;
    }
}
