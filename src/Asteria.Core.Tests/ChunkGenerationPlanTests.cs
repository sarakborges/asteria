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
    public void ImmutableGeneratorHaloDoesNotRequireNeighborChunkHolders()
    {
        Assert.Equal(
            1,
            AsteriaChunkGenerationPlan.FullSectionAccessEnvelope.HorizontalRadius);
        Assert.Equal(
            0,
            AsteriaChunkGenerationPlan.MaximumHorizontalDependencyRadius);
        Assert.Equal(
            0,
            AsteriaChunkGenerationPlan.FullSectionEnvelope.HorizontalRadius);

        var accumulated = AsteriaChunkGenerationPlan.Default.GetAccumulatedDependencies(
            ChunkGenerationStepId.SectionReady);
        Assert.Contains(
            accumulated,
            dependency =>
                dependency.PrerequisiteStep ==
                    ChunkGenerationStepId.SurfaceTerrainColumn &&
                dependency.Kind == ChunkGenerationDependencyKind.ImmutableWorldData &&
                dependency.Envelope.HorizontalRadius == 1);
        Assert.DoesNotContain(
            accumulated,
            dependency =>
                dependency.Envelope.HorizontalRadius > 0 &&
                dependency.Kind == ChunkGenerationDependencyKind.HolderStatus);
    }

    [Fact]
    public void ProceduralNeighborReadsAreDeclaredWithoutLoadingNeighborHolders()
    {
        var plan = AsteriaChunkGenerationPlan.Default;
        var expectedReads = new[]
        {
            (ChunkGenerationStepId.SurfaceDecorations, 0, 0),
            (ChunkGenerationStepId.DirectionalFacePalettes, -1, 1),
            (ChunkGenerationStepId.WallLayers, 0, 0),
            (ChunkGenerationStepId.WallObjects, 0, 0),
        };

        foreach (var (stepId, minimumSectionOffset, maximumSectionOffset)
                 in expectedReads)
        {
            var step = plan.GetStep(stepId);
            Assert.Equal(1, step.ReadEnvelope.HorizontalRadius);
            Assert.Equal(minimumSectionOffset, step.ReadEnvelope.MinimumSectionOffset);
            Assert.Equal(maximumSectionOffset, step.ReadEnvelope.MaximumSectionOffset);

            Assert.Contains(
                plan.GetAccumulatedDependencies(stepId),
                dependency =>
                    dependency.PrerequisiteStep ==
                        ChunkGenerationStepId.SurfaceTerrainColumn &&
                    dependency.Kind ==
                        ChunkGenerationDependencyKind.ImmutableWorldData &&
                    dependency.Envelope.HorizontalRadius == 1);
        }

        Assert.Equal(
            1,
            AsteriaChunkGenerationPlan.FullSectionAccessEnvelope.HorizontalRadius);
        Assert.Equal(
            -1,
            AsteriaChunkGenerationPlan.FullSectionAccessEnvelope.MinimumSectionOffset);
        Assert.Equal(
            1,
            AsteriaChunkGenerationPlan.FullSectionAccessEnvelope.MaximumSectionOffset);

        Assert.Equal(
            0,
            AsteriaChunkGenerationPlan.FullSectionEnvelope.HorizontalRadius);
        Assert.Equal(
            -1,
            AsteriaChunkGenerationPlan.FullSectionEnvelope.MinimumSectionOffset);
        Assert.Equal(
            0,
            AsteriaChunkGenerationPlan.FullSectionEnvelope.MaximumSectionOffset);

        Assert.DoesNotContain(
            plan.GetAccumulatedDependencies(ChunkGenerationStepId.SectionReady),
            dependency =>
                dependency.Envelope.HorizontalRadius > 0 &&
                dependency.Kind == ChunkGenerationDependencyKind.HolderStatus);
    }

    [Fact]
    public void ProceduralNeighborReadStepsDeclareImmutableHorizontalHalos()
    {
        var plan = AsteriaChunkGenerationPlan.Default;
        var steps = new[]
        {
            ChunkGenerationStepId.SurfaceDecorations,
            ChunkGenerationStepId.DirectionalFacePalettes,
            ChunkGenerationStepId.WallLayers,
            ChunkGenerationStepId.WallObjects,
        };

        foreach (var stepId in steps)
        {
            var step = plan.GetStep(stepId);
            Assert.Equal(1, step.ReadEnvelope.HorizontalRadius);
            Assert.Contains(
                plan.GetAccumulatedDependencies(stepId),
                dependency =>
                    dependency.PrerequisiteStep ==
                        ChunkGenerationStepId.SurfaceTerrainColumn &&
                    dependency.Kind ==
                        ChunkGenerationDependencyKind.ImmutableWorldData &&
                    dependency.Envelope.HorizontalRadius == 1);
        }

        var palette = plan.GetStep(
            ChunkGenerationStepId.DirectionalFacePalettes);
        Assert.Equal(-1, palette.ReadEnvelope.MinimumSectionOffset);
        Assert.Equal(1, palette.ReadEnvelope.MaximumSectionOffset);

        Assert.Equal(
            1,
            AsteriaChunkGenerationPlan.FullSectionAccessEnvelope.HorizontalRadius);
        Assert.Equal(
            -1,
            AsteriaChunkGenerationPlan.FullSectionAccessEnvelope.MinimumSectionOffset);
        Assert.Equal(
            1,
            AsteriaChunkGenerationPlan.FullSectionAccessEnvelope.MaximumSectionOffset);

        Assert.Equal(
            0,
            AsteriaChunkGenerationPlan.FullSectionEnvelope.HorizontalRadius);
        Assert.Equal(
            -1,
            AsteriaChunkGenerationPlan.FullSectionEnvelope.MinimumSectionOffset);
        Assert.Equal(
            0,
            AsteriaChunkGenerationPlan.FullSectionEnvelope.MaximumSectionOffset);
    }

    [Fact]
    public void AsteriaFullStepHasNoHorizontalHolderStatusPrerequisite()
    {
        Assert.Equal(
            ChunkGenerationStatus.Unloaded,
            AsteriaChunkGenerationPlan.GetRequiredStatusAtHorizontalDistance(
                ChunkGenerationStepId.SectionReady,
                horizontalDistance: 1));
        Assert.Equal(
            ChunkGenerationStatus.Unloaded,
            AsteriaChunkGenerationPlan.GetRequiredStatusAtHorizontalDistance(
                ChunkGenerationStepId.SectionReady,
                horizontalDistance: 2));
    }

    [Fact]
    public void ChunkTicketLevelsUseOnlyHolderStatusDependencies()
    {
        Assert.Equal(
            ChunkLevel.Full,
            ChunkLevel.MaximumLoading);
        Assert.Equal(
            ChunkGenerationStatus.Unloaded,
            ChunkLevel.GetGenerationStatus(ChunkLevel.Full + 1));
    }

    [Fact]
    public void HolderEnvelopeDoesNotIncludeImmutableDataButKeepsHolderPrerequisites()
    {
        var readEnvelope = new ChunkGenerationDependencyEnvelope(2, -1, 1);
        var holderEnvelope = new ChunkGenerationDependencyEnvelope(1, 0, 0);
        var plan = new ChunkGenerationPlan(
        [
            new ChunkGenerationStepDefinition(
                ChunkGenerationStepId.ColumnBiomeData,
                ChunkGenerationWorkScope.Column,
                ChunkGenerationExecutionPolicy.SharedWorkerPool,
                Array.Empty<ChunkGenerationStepDependency>(),
                ChunkGenerationDependencyEnvelope.None,
                ChunkGenerationDependencyEnvelope.None),
            new ChunkGenerationStepDefinition(
                ChunkGenerationStepId.SurfaceTerrainColumn,
                ChunkGenerationWorkScope.Column,
                ChunkGenerationExecutionPolicy.SharedWorkerPool,
                [new ChunkGenerationStepDependency(
                    ChunkGenerationStepId.ColumnBiomeData,
                    ChunkGenerationDependencyEnvelope.None,
                    ChunkGenerationDependencyKind.ImmutableWorldData)],
                ChunkGenerationDependencyEnvelope.None,
                ChunkGenerationDependencyEnvelope.None),
            new ChunkGenerationStepDefinition(
                ChunkGenerationStepId.SectionTerrainSetup,
                ChunkGenerationWorkScope.Section,
                ChunkGenerationExecutionPolicy.SharedWorkerPool,
                [
                    new ChunkGenerationStepDependency(
                        ChunkGenerationStepId.SurfaceTerrainColumn,
                        readEnvelope,
                        ChunkGenerationDependencyKind.ImmutableWorldData),
                    new ChunkGenerationStepDependency(
                        ChunkGenerationStepId.ColumnBiomeData,
                        holderEnvelope,
                        ChunkGenerationDependencyKind.HolderStatus),
                ],
                readEnvelope,
                ChunkGenerationDependencyEnvelope.None),
        ]);

        var allDependencies = plan.GetAccumulatedDependencies(
            ChunkGenerationStepId.SectionTerrainSetup);
        var holderDependency = Assert.Single(
            allDependencies.Where(item =>
                item.Kind == ChunkGenerationDependencyKind.HolderStatus));
        Assert.Equal(
            ChunkGenerationStepId.ColumnBiomeData,
            holderDependency.PrerequisiteStep);
        Assert.Equal(holderEnvelope, holderDependency.Envelope);
        Assert.Contains(
            allDependencies,
            item => item.Kind == ChunkGenerationDependencyKind.ImmutableWorldData &&
                item.Envelope.HorizontalRadius == 2);

        Assert.Equal(
            holderEnvelope,
            plan.GetHolderDependencyEnvelope(
                ChunkGenerationStepId.SectionTerrainSetup));
        Assert.Equal(
            2,
            plan.GetDependencyEnvelope(
                ChunkGenerationStepId.SectionTerrainSetup).HorizontalRadius);
    }

    [Fact]
    public void DependencyClaimsRetainHolderStatusTargetsNotImmutableHaloColumns()
    {
        var manager = new ChunkDistanceManager();
        var target = new ChunkColumnCoord(12, -7);
        var neighbor = new ChunkColumnCoord(15, -7);

        Assert.True(manager.AddPlayerLoadingTicket(target));
        // A ticket at level 31 propagates through level 33 (two 8-neighbor
        // rings), creating the expected 5x5 holder area.
        Assert.Equal(25, manager.HolderCount);
        var holder = Assert.IsType<ChunkHolder>(manager.GetHolder(target));
        var lightFuture = manager.GetGenerationFuture(
            target, ChunkGenerationStatus.Light);
        Assert.False(lightFuture.IsCompleted);

        using (manager.AcquireDependencyClaims(
                   new ChunkCoord(target.X, 4, target.Z),
                   AsteriaChunkGenerationPlan.FullSectionEnvelope))
        {
            Assert.Equal(1, holder.DependencyClaimCount);
            Assert.Null(manager.GetHolder(neighbor));
            Assert.Equal(25, manager.HolderCount);
        }

        Assert.Equal(0, holder.DependencyClaimCount);
        Assert.Null(manager.GetHolder(neighbor));
        Assert.Equal(25, manager.HolderCount);
    }

    [Fact]
    public void SectionGenerationFuturesAreDistinctForVerticalCoordinates()
    {
        var holder = new ChunkHolder(
            new ChunkColumnCoord(7, -3),
            ChunkLevel.EntityTicking);
        var lower = new ChunkCoord(7, 2, -3);
        var upper = new ChunkCoord(7, 3, -3);

        var lowerTerrain = holder.GetSectionGenerationFuture(
            lower, ChunkGenerationStatus.Terrain);
        var upperTerrain = holder.GetSectionGenerationFuture(
            upper, ChunkGenerationStatus.Terrain);

        Assert.False(lowerTerrain.IsCompleted);
        Assert.False(upperTerrain.IsCompleted);
        Assert.NotSame(lowerTerrain, upperTerrain);

        Assert.True(holder.CompleteSectionGenerationStatus(
            lower, ChunkGenerationStatus.Terrain));

        Assert.True(lowerTerrain.IsCompletedSuccessfully);
        Assert.True(lowerTerrain.Result.Succeeded);
        Assert.Equal(ChunkGenerationStatus.Terrain, lowerTerrain.Result.Status);
        Assert.False(upperTerrain.IsCompleted);
        Assert.Equal(
            ChunkGenerationStatus.Unloaded,
            holder.GetCompletedSectionGenerationStatus(upper));
    }

    [Fact]
    public void SectionGenerationFutureReconcilesTicketDemotionAndPromotion()
    {
        var holder = new ChunkHolder(
            new ChunkColumnCoord(-4, 8),
            ChunkLevel.EntityTicking);
        var section = new ChunkCoord(-4, 6, 8);

        Assert.True(holder.CompleteSectionGenerationStatus(
            section, ChunkGenerationStatus.Features));
        var completed = holder.GetSectionGenerationFuture(
            section, ChunkGenerationStatus.Features);
        Assert.True(completed.Result.Succeeded);
        var revision = completed.Result.Revision;

        Assert.True(holder.UpdateTicketLevel(ChunkLevel.Unloaded));
        var demoted = holder.GetSectionGenerationFuture(
            section, ChunkGenerationStatus.Features);
        Assert.True(demoted.IsCompletedSuccessfully);
        Assert.False(demoted.Result.Succeeded);
        Assert.Equal(revision, demoted.Result.Revision);

        Assert.True(holder.UpdateTicketLevel(ChunkLevel.EntityTicking));
        var promoted = holder.GetSectionGenerationFuture(
            section, ChunkGenerationStatus.Features);
        Assert.True(promoted.IsCompletedSuccessfully);
        Assert.True(promoted.Result.Succeeded);
        Assert.Equal(revision, promoted.Result.Revision);
    }

    [Fact]
    public void ResettingOneSectionRearmsOnlyItsStatusFutures()
    {
        var holder = new ChunkHolder(
            new ChunkColumnCoord(11, 5),
            ChunkLevel.EntityTicking);
        var lower = new ChunkCoord(11, 0, 5);
        var upper = new ChunkCoord(11, 1, 5);
        Assert.True(holder.CompleteSectionGenerationStatus(
            lower, ChunkGenerationStatus.Terrain));
        Assert.True(holder.CompleteSectionGenerationStatus(
            upper, ChunkGenerationStatus.Biomes));

        var lowerOld = holder.GetSectionGenerationFuture(
            lower, ChunkGenerationStatus.Terrain);
        var upperExisting = holder.GetSectionGenerationFuture(
            upper, ChunkGenerationStatus.Biomes);
        var lowerRevision = lowerOld.Result.Revision;

        Assert.True(holder.ResetSectionGenerationProgress(
            lower, "Lower section was discarded."));

        var lowerCurrent = holder.GetSectionGenerationFuture(
            lower, ChunkGenerationStatus.Terrain);
        // An already-completed future cannot be rewritten. Its successful
        // result remains tied to the old revision and must be rejected by any
        // waiter that compares it with the holder's current section revision.
        Assert.True(lowerOld.IsCompletedSuccessfully);
        Assert.True(lowerOld.Result.Succeeded);
        Assert.Equal(lowerRevision, lowerOld.Result.Revision);
        Assert.False(lowerCurrent.IsCompleted);
        Assert.Equal(lowerRevision + 1,
            holder.GetSectionGenerationRevision(lower));
        Assert.Same(upperExisting,
            holder.GetSectionGenerationFuture(
                upper, ChunkGenerationStatus.Biomes));
        Assert.True(upperExisting.Result.Succeeded);
    }

    [Fact]
    public void SectionDependencyClaimKeepsExactVerticalTargetsUnderOneColumnLease()
    {
        var manager = new ChunkDistanceManager();
        var column = new ChunkColumnCoord(12, -7);
        Assert.True(manager.AddPlayerLoadingTicket(column));
        var holder = Assert.IsType<ChunkHolder>(manager.GetHolder(column));

        using (var claim = manager.AcquireDependencyClaims(
                   new ChunkCoord(column.X, 4, column.Z),
                   AsteriaChunkGenerationPlan.FullSectionEnvelope))
        {
            Assert.Single(claim.Columns);
            Assert.Equal(1, holder.DependencyClaimCount);
            Assert.Collection(
                claim.SectionTargets.OrderBy(section => section.Y),
                section => Assert.Equal(new ChunkCoord(column.X, 3, column.Z), section),
                section => Assert.Equal(new ChunkCoord(column.X, 4, column.Z), section));

            var lower = new ChunkCoord(column.X, 3, column.Z);
            var lowerTerrain = claim.GetSectionGenerationFuture(
                lower, ChunkGenerationStatus.Terrain);
            Assert.False(lowerTerrain.IsCompleted);
            Assert.True(manager.CompleteSectionGenerationStatus(
                lower, ChunkGenerationStatus.Terrain));
            Assert.True(lowerTerrain.Result.Succeeded);
        }

        Assert.Equal(0, holder.DependencyClaimCount);
    }

    [Fact]
    public void MultiSectionDependencyClaimDeduplicatesHorizontalHolderLeases()
    {
        var manager = new ChunkDistanceManager();
        var column = new ChunkColumnCoord(-13, 6);
        Assert.True(manager.AddPlayerLoadingTicket(column));
        var holder = Assert.IsType<ChunkHolder>(manager.GetHolder(column));
        var targets = new[]
        {
            new ChunkCoord(column.X, 5, column.Z),
            new ChunkCoord(column.X, 6, column.Z),
            new ChunkCoord(column.X, 7, column.Z),
            new ChunkCoord(column.X, 8, column.Z),
        };

        using (var claim = manager.AcquireDependencyClaims(
                   targets,
                   AsteriaChunkGenerationPlan.FullSectionEnvelope))
        {
            Assert.Single(claim.Columns);
            Assert.Equal(1, holder.DependencyClaimCount);
            Assert.Collection(
                claim.SectionTargets.OrderBy(section => section.Y),
                section => Assert.Equal(4, section.Y),
                section => Assert.Equal(5, section.Y),
                section => Assert.Equal(6, section.Y),
                section => Assert.Equal(7, section.Y),
                section => Assert.Equal(8, section.Y));
        }

        Assert.Equal(0, holder.DependencyClaimCount);
    }

    [Fact]
    public void GenerationDagExpandsHolderRequirementsIntoClaimedSectionFutures()
    {
        var target = new ChunkCoord(14, 4, -9);
        var lower = new ChunkCoord(14, 3, -9);
        var requirements = AsteriaChunkGenerationPlan.GetHolderStatusRequirements(
            ChunkGenerationStepId.SectionReady,
            target);

        Assert.Contains(requirements, requirement =>
            requirement.Coord == lower &&
            requirement.PrerequisiteStep == ChunkGenerationStepId.SectionTerrainLayers &&
            requirement.Status == ChunkGenerationStatus.Terrain);

        var manager = new ChunkDistanceManager();
        var column = ChunkColumnCoord.FromChunk(target);
        Assert.True(manager.AddPlayerLoadingTicket(column));
        using var claim = manager.AcquireDependencyClaims(
            target,
            AsteriaChunkGenerationPlan.FullSectionEnvelope);

        foreach (var requirement in requirements)
        {
            Assert.Contains(requirement.Coord, claim.SectionTargets);
            var future = claim.GetSectionGenerationFuture(requirement);
            Assert.NotNull(future);
        }
    }

    [Fact]
    public void HolderRetainsAchievedStatusUntilTicketPromotionPermitsIt()
    {
        var column = new ChunkColumnCoord(20, -11);
        var holder = new ChunkHolder(column, ChunkLevel.Full + 1);

        Assert.Equal(
            ChunkGenerationStatus.Unloaded,
            holder.HighestAllowedGenerationStatus);
        Assert.False(holder.CompleteGenerationStatus(
            ChunkGenerationStatus.Biomes));

        Assert.Equal(
            ChunkGenerationStatus.Biomes,
            holder.CompletedGenerationStatus);
        Assert.False(holder.GetGenerationFuture(
            ChunkGenerationStatus.Biomes).Result.Succeeded);

        Assert.True(holder.UpdateTicketLevel(ChunkLevel.Full));

        var promoted = holder.GetGenerationFuture(
            ChunkGenerationStatus.Biomes).Result;
        Assert.True(promoted.Succeeded);
        Assert.Equal(ChunkGenerationStatus.Biomes, promoted.Status);
        Assert.Equal(holder.GenerationProgressRevision, promoted.Revision);
        Assert.Equal(
            ChunkGenerationStatus.Biomes,
            holder.CompletedGenerationStatus);
    }

    [Fact]
    public void ResetGenerationProgressInvalidatesOldRevisionAndRearmsPromotionFutures()
    {
        var column = new ChunkColumnCoord(-15, 9);
        var holder = new ChunkHolder(column, ChunkLevel.EntityTicking);
        Assert.True(holder.CompleteGenerationStatus(
            ChunkGenerationStatus.Features));
        var oldRevision = holder.GenerationProgressRevision;
        var oldFuture = holder.GetGenerationFuture(
            ChunkGenerationStatus.Features);
        var oldFullFuture = holder.FullChunkFuture;

        Assert.True(holder.ResetGenerationProgress(
            "The partial column was discarded."));
        Assert.Equal(oldRevision + 1, holder.GenerationProgressRevision);
        Assert.Equal(
            ChunkGenerationStatus.Unloaded,
            holder.CompletedGenerationStatus);
        Assert.True(oldFuture.IsCompletedSuccessfully);
        Assert.Equal(oldRevision, oldFuture.Result.Revision);
        Assert.False(holder.GetGenerationFuture(
            ChunkGenerationStatus.Features).IsCompleted);
        Assert.True(oldFullFuture.IsCompletedSuccessfully);
        Assert.False(oldFullFuture.Result.Succeeded);
        Assert.False(holder.FullChunkFuture.IsCompleted);
    }

    [Fact]
    public void AdditionalVerticalSectionsInvalidatePriorColumnFullStatus()
    {
        var manager = new ChunkDistanceManager();
        var admission = new ChunkTicketAdmissionController(manager);
        var column = new ChunkColumnCoord(6, 8);
        var existing = new ChunkCoord(6, 0, 8);
        var newlyRequired = new ChunkCoord(6, 1, 8);

        Assert.True(admission.TryAdmit(column, [existing], []));
        Assert.True(admission.MarkSectionFull(existing));

        var holder = Assert.IsType<ChunkHolder>(manager.GetHolder(column));
        Assert.Equal(ChunkGenerationStatus.Full, holder.CompletedGenerationStatus);
        Assert.True(holder.FullChunkFuture.IsCompletedSuccessfully);
        var previousRevision = holder.GenerationProgressRevision;

        Assert.True(admission.TryAdmit(
            column,
            [existing, newlyRequired],
            [existing]));

        Assert.Equal(1, admission.PendingAdmissionCount);
        Assert.Equal(previousRevision + 1, holder.GenerationProgressRevision);
        Assert.Equal(
            ChunkGenerationStatus.Unloaded,
            holder.CompletedGenerationStatus);
        Assert.False(holder.FullChunkFuture.IsCompleted);
        Assert.False(holder.GetGenerationFuture(
            ChunkGenerationStatus.Features).IsCompleted);

        Assert.True(admission.MarkSectionFull(newlyRequired));
        Assert.Equal(0, admission.PendingAdmissionCount);
        Assert.Equal(ChunkGenerationStatus.Full, holder.CompletedGenerationStatus);
        Assert.True(holder.FullChunkFuture.IsCompletedSuccessfully);
    }

    [Fact]
    public void RequiredStatusRejectsNegativeHorizontalDistance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AsteriaChunkGenerationPlan.GetRequiredStatusAtHorizontalDistance(
                ChunkGenerationStepId.SectionReady,
                horizontalDistance: -1));
    }

    [Fact]
    public void MaterializationStagesMapToTheDeclaredExecutionLanes()
    {
        Assert.Equal(
            ChunkGenerationStepId.SectionTerrainSetup,
            AsteriaChunkGenerationPlan.GetStepId(
                ChunkMaterializationStage.TerrainSetup));
        Assert.Equal(
            ChunkGenerationStepId.SectionTerrainLayers,
            AsteriaChunkGenerationPlan.GetStepId(
                ChunkMaterializationStage.TerrainLayer));

        Assert.Equal(
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            AsteriaChunkGenerationPlan.GetExecutionPolicy(
                ChunkMaterializationStage.TerrainSetup));
        Assert.Equal(
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            AsteriaChunkGenerationPlan.GetExecutionPolicy(
                ChunkMaterializationStage.TerrainLayer));

        var orderedStages = new[]
        {
            ChunkMaterializationStage.SurfaceDecorations,
            ChunkMaterializationStage.DirectionalFacePalettes,
            ChunkMaterializationStage.VolumeStructures,
            ChunkMaterializationStage.GeneratedFluids,
            ChunkMaterializationStage.SurfaceStructures,
            ChunkMaterializationStage.CaveSpikes,
            ChunkMaterializationStage.VerticalDecorations,
            ChunkMaterializationStage.Veins,
            ChunkMaterializationStage.WallLayers,
            ChunkMaterializationStage.WallObjects,
            ChunkMaterializationStage.ShellFinalization,
            ChunkMaterializationStage.GravitySupport,
            ChunkMaterializationStage.StructureObjects,
            ChunkMaterializationStage.PendingObjects,
        };

        foreach (var stage in orderedStages)
        {
            Assert.Equal(
                ChunkGenerationExecutionPolicy.OrderedMutation,
                AsteriaChunkGenerationPlan.GetExecutionPolicy(stage));
        }

        Assert.Equal(
            ChunkGenerationExecutionPolicy.LightExecutor,
            AsteriaChunkGenerationPlan.Default
                .GetStep(ChunkGenerationStepId.InitialLighting)
                .ExecutionPolicy);
        Assert.Equal(
            ChunkGenerationExecutionPolicy.MainThreadIntegration,
            AsteriaChunkGenerationPlan.Default
                .GetStep(ChunkGenerationStepId.SectionReady)
                .ExecutionPolicy);
        Assert.Equal(
            ChunkGenerationExecutionPolicy.SharedWorkerPool,
            AsteriaChunkGenerationPlan.GetExecutionPolicy(
                ChunkMaterializationStage.ProviderFallback));
    }

    [Fact]
    public void NonStepMaterializationStatesDoNotResolveToPlanSteps()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AsteriaChunkGenerationPlan.GetStepId(
                ChunkMaterializationStage.ProviderFallback));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AsteriaChunkGenerationPlan.GetExecutionPolicy(
                ChunkMaterializationStage.Complete));
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
