using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkGenerationPlanTests
{
    [Fact]
    public void DependencyEnvelopeComposesHorizontalAndVerticalOffsets()
    {
        var outer = new ChunkGenerationDependencyEnvelope(
            horizontalRadius: 2,
            minimumSectionOffset: -1,
            maximumSectionOffset: 1);
        var inner = new ChunkGenerationDependencyEnvelope(
            horizontalRadius: 1,
            minimumSectionOffset: -2,
            maximumSectionOffset: 3);

        Assert.Equal(
            new ChunkGenerationDependencyEnvelope(3, -3, 4),
            outer.Compose(inner));
    }

    [Fact]
    public void DependencyEnvelopeUnionContainsBothInputRanges()
    {
        var left = new ChunkGenerationDependencyEnvelope(1, -2, 0);
        var right = new ChunkGenerationDependencyEnvelope(3, -1, 4);

        Assert.Equal(
            new ChunkGenerationDependencyEnvelope(3, -2, 4),
            left.Union(right));
    }

    [Theory]
    [InlineData(2, -1, 2, true)]
    [InlineData(-2, 0, -2, true)]
    [InlineData(3, 0, 0, false)]
    [InlineData(0, -2, 0, false)]
    [InlineData(0, 2, 0, false)]
    public void DependencyEnvelopeChecksInclusiveSpatialOffsets(
        int deltaX,
        int sectionYOffset,
        int deltaZ,
        bool expected)
    {
        var envelope = new ChunkGenerationDependencyEnvelope(2, -1, 1);

        Assert.Equal(
            expected,
            envelope.ContainsOffset(deltaX, sectionYOffset, deltaZ));
    }

    [Fact]
    public void DependencyEnvelopeRejectsInvalidRanges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChunkGenerationDependencyEnvelope(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChunkGenerationDependencyEnvelope(0, 2, 1));
    }

    [Fact]
    public void PlanOrdersPrerequisitesBeforeDependentsAndKeepsIndependentOrder()
    {
        var steps = new[]
        {
            Step(
                ChunkGenerationStepId.SectionTerrainSetup,
                [Dependency(ChunkGenerationStepId.ColumnBiomeData)]),
            Step(ChunkGenerationStepId.ColumnBiomeData),
            Step(ChunkGenerationStepId.SurfaceDecorations),
            Step(
                ChunkGenerationStepId.SectionTerrainLayers,
                [Dependency(ChunkGenerationStepId.SectionTerrainSetup)]),
        };

        var plan = new ChunkGenerationPlan(steps);

        Assert.Equal(
            new[]
            {
                ChunkGenerationStepId.ColumnBiomeData,
                ChunkGenerationStepId.SectionTerrainSetup,
                ChunkGenerationStepId.SurfaceDecorations,
                ChunkGenerationStepId.SectionTerrainLayers,
            },
            plan.Steps.Select(step => step.Id));
    }

    [Fact]
    public void PlanComposesTransitiveDependencyEnvelopes()
    {
        var columnEnvelope = new ChunkGenerationDependencyEnvelope(1, -1, 1);
        var terrainEnvelope = new ChunkGenerationDependencyEnvelope(2, -2, 2);
        var plan = new ChunkGenerationPlan(
        [
            Step(ChunkGenerationStepId.ColumnBiomeData),
            Step(
                ChunkGenerationStepId.SectionTerrainSetup,
                [Dependency(
                    ChunkGenerationStepId.ColumnBiomeData,
                    columnEnvelope)]),
            Step(
                ChunkGenerationStepId.VolumeStructures,
                [Dependency(
                    ChunkGenerationStepId.SectionTerrainSetup,
                    terrainEnvelope)]),
        ]);

        var dependencies = plan.GetAccumulatedDependencies(
            ChunkGenerationStepId.VolumeStructures);

        Assert.Collection(
            dependencies,
            dependency =>
            {
                Assert.Equal(ChunkGenerationStepId.ColumnBiomeData,
                    dependency.PrerequisiteStep);
                Assert.Equal(
                    new ChunkGenerationDependencyEnvelope(3, -3, 3),
                    dependency.Envelope);
            },
            dependency =>
            {
                Assert.Equal(ChunkGenerationStepId.SectionTerrainSetup,
                    dependency.PrerequisiteStep);
                Assert.Equal(terrainEnvelope, dependency.Envelope);
            });
    }

    [Fact]
    public void PlanRejectsDependenciesThatAreNotDeclared()
    {
        var steps = new[]
        {
            Step(
                ChunkGenerationStepId.SectionTerrainSetup,
                [Dependency(ChunkGenerationStepId.ColumnBiomeData)]),
        };

        Assert.Throws<ArgumentException>(() => new ChunkGenerationPlan(steps));
    }

    [Fact]
    public void PlanRejectsDependencyCycles()
    {
        var steps = new[]
        {
            Step(
                ChunkGenerationStepId.SectionTerrainSetup,
                [Dependency(ChunkGenerationStepId.VolumeStructures)]),
            Step(
                ChunkGenerationStepId.VolumeStructures,
                [Dependency(ChunkGenerationStepId.SectionTerrainSetup)]),
        };

        Assert.Throws<ArgumentException>(() => new ChunkGenerationPlan(steps));
    }

    [Fact]
    public void PlanRejectsDuplicateStepIds()
    {
        var steps = new[]
        {
            Step(ChunkGenerationStepId.ColumnBiomeData),
            Step(ChunkGenerationStepId.ColumnBiomeData),
        };

        Assert.Throws<ArgumentException>(() => new ChunkGenerationPlan(steps));
    }

    [Fact]
    public void StepRejectsDuplicateDirectPrerequisites()
    {
        Assert.Throws<ArgumentException>(
            () => Step(
                ChunkGenerationStepId.SectionTerrainSetup,
                [
                    Dependency(ChunkGenerationStepId.ColumnBiomeData),
                    Dependency(ChunkGenerationStepId.ColumnBiomeData,
                        new ChunkGenerationDependencyEnvelope(1, 0, 0)),
                ]));
    }

    private static ChunkGenerationStepDefinition Step(
        ChunkGenerationStepId id,
        IEnumerable<ChunkGenerationStepDependency>? dependencies = null) =>
        new(
            id,
            ChunkGenerationWorkScope.Section,
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            dependencies ?? Array.Empty<ChunkGenerationStepDependency>(),
            ChunkGenerationDependencyEnvelope.None,
            ChunkGenerationDependencyEnvelope.None);

    private static ChunkGenerationStepDependency Dependency(
        ChunkGenerationStepId id,
        ChunkGenerationDependencyEnvelope? envelope = null) =>
        new(id, envelope ?? ChunkGenerationDependencyEnvelope.None);
}
