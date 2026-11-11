from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from compare_worldgen_ab import compare, load_repetitions, render_report


def sample_run(*, palette_ms: float = 50.0, palette_bytes: int = 256, exposure_ms: float = 0.0, surface_decoration_ms: float = 100.0, surface_decoration_bytes: int = 1024 * 1024, stage_ms: float = 100.0, terrain_layer_bytes: int = 512, materialize_ms: float = 200.0, synthesis_ms: float = 220.0, digest: int = 123, chunks: int = 67, extra_stages: dict | None = None, streaming_route: dict | None = None) -> dict:
    stages = {
        "DirectionalFaceExposure": {"elapsedMs": exposure_ms, "allocatedBytes": 128},
        "DirectionalFacePalettes": {"elapsedMs": palette_ms, "allocatedBytes": palette_bytes},
        "SurfaceDecorations": {
            "elapsedMs": surface_decoration_ms,
            "allocatedBytes": surface_decoration_bytes,
        },
        "TerrainLayer": {"elapsedMs": stage_ms, "allocatedBytes": terrain_layer_bytes},
    }
    if extra_stages:
        stages.update(extra_stages)

    route = {
        "routeSteps": 6,
        "movementChunks": 5,
        "framesPerChunk": 8,
        "horizontalRadius": 3,
        "retentionRadius": 7,
        "uniqueActivatedChunkCount": 2,
        "routeDigest": 987654321,
        "activatedChunkDigests": [
            {"x": 1, "y": 2, "z": 3, "digest": 111, "blockDigest": 11, "fluidDigest": 12, "objectDigest": 13},
            {"x": 2, "y": 2, "z": 3, "digest": 222, "blockDigest": 22, "fluidDigest": 23, "objectDigest": 24},
        ],
        "generatedChunkDigestsMatchOnReactivation": True,
        "totalElapsedMilliseconds": 1000.0,
        "totalWorkerMilliseconds": 600.0,
        "averageWorkerMillisecondsPerProviderActivation": 8.0,
        "selectionResultLatencyMedianFrames": 2.0,
        "selectionResultLatencyP95Frames": 4.0,
        "frameCount": 90,
        "framesWithPlayerLocalGaps": 20,
        "maximumPlayerLocalMissingChunks": 60,
        "maximumPendingChunks": 24,
        "maximumActiveWorkers": 6,
        "totalFluidCellsActivated": 100,
        "totalFluidFrontierCellsActivated": 200,
        "totalFluidActivationSlices": 4,
        "totalFluidActivationMilliseconds": 8.0,
        "maximumFluidActivationSliceMilliseconds": 2.0,
        "maximumPendingFluidActivationChunks": 3,
        "totalFallingBlocksStarted": 0,
        "totalUnsupportedBlocksRemoved": 0,
        "observedFallingEntityCount": 0,
        "fallingBlockSamples": [],
    }
    if streaming_route:
        route.update(streaming_route)

    return {
        "streamingRouteDiagnostics": route,
        "seed": 181960897289965,
        "dimension": "asteria:overworld",
        "dimensionSeed": 987654321,
        "center": [12, -8],
        "initialAreaDiagnostics": {
            "spawnX": 192,
            "spawnY": 72,
            "spawnZ": -128,
            "centerChunkX": 12,
            "centerChunkY": 4,
            "centerChunkZ": -8,
            "desiredChunkCount": chunks,
        },
        "metrics": {
            "chunkSynthesis": {"warmMs": 42.0, "digest": digest},
            "initialAreaSynthesis": {"warmMs": synthesis_ms, "digest": digest},
        },
        "initialAreaMaterializationDiagnostics": {
            "warm": {"totalMaterializeMs": materialize_ms},
        },
        "initialAreaStageTimingDiagnostics": {
            "warm": {"byStage": stages},
        },
    }


class CompareWorldgenAbTests(unittest.TestCase):
    def test_reports_directional_palette_and_materialization_medians(self) -> None:
        baseline = [
            sample_run(palette_ms=value, exposure_ms=5.0, stage_ms=stage)
            for value, stage in zip((60.0, 80.0, 70.0), (100.0, 120.0, 110.0))
        ]
        candidate = [
            sample_run(palette_ms=value, exposure_ms=10.0, stage_ms=stage)
            for value, stage in zip((40.0, 50.0, 60.0), (80.0, 90.0, 100.0))
        ]

        result = compare(baseline, candidate, expected_chunks=67)

        palette = result["metrics"][0]
        self.assertEqual("DirectionalFacePalettes (warm)", palette["metric"])
        self.assertEqual(75.0, palette["baselineMedianMs"])
        self.assertEqual(60.0, palette["candidateMedianMs"])
        self.assertAlmostEqual(20.0, palette["reductionPercent"], places=3)

        terrain = next(
            metric for metric in result["metrics"]
            if metric["metric"] == "TerrainLayer (warm)"
        )
        self.assertEqual("TerrainLayer (warm)", terrain["metric"])
        self.assertEqual(110.0, terrain["baselineMedianMs"])
        self.assertEqual(90.0, terrain["candidateMedianMs"])
        self.assertAlmostEqual(18.1818, terrain["reductionPercent"], places=3)
        report = render_report(result)
        self.assertIn("DirectionalFacePalettes", report)
        self.assertIn("identical", report)

    def test_combines_terrain_and_directional_face_work_before_median(self) -> None:
        baseline = [
            sample_run(
                palette_ms=palette,
                stage_ms=terrain,
                palette_bytes=palette_bytes,
                terrain_layer_bytes=terrain_bytes,
            )
            for palette, terrain, palette_bytes, terrain_bytes in zip(
                (60.0, 80.0, 70.0),
                (100.0, 120.0, 110.0),
                (2, 4, 6),
                (6, 8, 10),
            )
        ]
        candidate = [
            sample_run(
                palette_ms=palette,
                stage_ms=terrain,
                palette_bytes=palette_bytes,
                terrain_layer_bytes=terrain_bytes,
            )
            for palette, terrain, palette_bytes, terrain_bytes in zip(
                (40.0, 50.0, 60.0),
                (80.0, 90.0, 100.0),
                (4, 6, 8),
                (8, 10, 12),
            )
        ]

        result = compare(baseline, candidate, expected_chunks=67)
        combined = next(
            metric for metric in result["metrics"]
            if metric["metric"] == "TerrainLayer + directional faces (warm)"
        )

        # Sum within each process before taking the median; do not add
        # independently calculated medians from different samples.
        self.assertEqual(180.0, combined["baselineMedianMs"])
        self.assertEqual(140.0, combined["candidateMedianMs"])
        self.assertAlmostEqual(22.2222, combined["reductionPercent"], places=3)

        allocations = result["terrainAndFaceAllocations"]
        self.assertAlmostEqual(12.0 / (1024 * 1024), allocations["baselineMedianMiB"])
        self.assertAlmostEqual(16.0 / (1024 * 1024), allocations["candidateMedianMiB"])
        self.assertAlmostEqual(-33.3333, allocations["reductionPercent"], places=3)

        report = render_report(result)
        self.assertIn("TerrainLayer + directional-face combined allocations", report)
        self.assertIn("TerrainLayer + directional faces (warm)", report)

    def test_reports_gravity_fall_starts_and_sample_coordinates(self) -> None:
        baseline = [sample_run() for _ in range(3)]
        falling_sample = {
            "fallingId": 1,
            "block": "asteria:gravel",
            "isGravityTagged": True,
            "x": 69,
            "y": 42,
            "z": 3,
            "sourceChunkX": 4,
            "sourceChunkY": 2,
            "sourceChunkZ": 0,
            "belowBlock": "asteria:air",
            "belowChunkLoaded": True,
        }
        candidate = [
            sample_run(streaming_route={
                "totalFallingBlocksStarted": 10,
                "observedFallingEntityCount": 10,
                "fallingBlockSamples": [falling_sample],
            })
            for _ in range(3)
        ]

        result = compare(baseline, candidate, expected_chunks=67)
        route = result["streamingRoute"]
        physics = route["physicsDifferences"]

        self.assertEqual(0, physics["baselineFallingBlocksStarted"])
        self.assertEqual(10, physics["candidateFallingBlocksStarted"])
        self.assertEqual([], physics["baselineFallingBlockSamples"])
        self.assertEqual("asteria:gravel", physics["candidateFallingBlockSamples"][0]["block"])
        self.assertEqual([4, 2, 0], physics["candidateFallingBlockSamples"][0]["chunk"])

        report = render_report(result)
        self.assertIn(
            "Gravity blocks started falling: baseline 0; candidate 10",
            report,
        )
        self.assertIn("asteria:gravel", report)
        self.assertIn("'x': 69, 'y': 42, 'z': 3", report)

    def test_rejects_nondeterministic_falling_block_samples_between_route_runs(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for index, x in enumerate((69, 69, 70), start=1):
                (root / f"{index}.json").write_text(
                    json.dumps(sample_run(streaming_route={
                        "totalFallingBlocksStarted": 1,
                        "observedFallingEntityCount": 1,
                        "fallingBlockSamples": [{
                            "fallingId": 1,
                            "block": "asteria:gravel",
                            "isGravityTagged": True,
                            "x": x,
                            "y": 42,
                            "z": 3,
                            "sourceChunkX": 4,
                            "sourceChunkY": 2,
                            "sourceChunkZ": 0,
                            "belowBlock": "asteria:air",
                            "belowChunkLoaded": True,
                        }],
                    })),
                    encoding="utf-8",
                )
            with self.assertRaisesRegex(ValueError, "falling-block physics outcomes"):
                load_repetitions(root, 3)

    def test_compares_streaming_route_metrics_when_route_content_matches(self) -> None:
        baseline = [
            sample_run(streaming_route={
                "totalElapsedMilliseconds": value,
                "totalWorkerMilliseconds": worker,
                "framesWithPlayerLocalGaps": gaps,
                "maximumPendingChunks": pending,
                "totalFluidActivationMilliseconds": value / 10.0,
                "maximumFluidActivationSliceMilliseconds": value / 100.0,
                "totalFluidFrontierCellsActivated": int(value),
            })
            for value, worker, gaps, pending in zip(
                (1000.0, 1100.0, 1200.0),
                (600.0, 650.0, 700.0),
                (20, 24, 28),
                (30, 32, 34),
            )
        ]
        candidate = [
            sample_run(streaming_route={
                "totalElapsedMilliseconds": value,
                "totalWorkerMilliseconds": worker,
                "framesWithPlayerLocalGaps": gaps,
                "maximumPendingChunks": pending,
                "totalFluidActivationMilliseconds": value / 10.0,
                "maximumFluidActivationSliceMilliseconds": value / 100.0,
                "totalFluidFrontierCellsActivated": int(value),
            })
            for value, worker, gaps, pending in zip(
                (800.0, 900.0, 1000.0),
                (500.0, 550.0, 600.0),
                (10, 12, 14),
                (20, 22, 24),
            )
        ]

        result = compare(baseline, candidate, expected_chunks=67)
        route = result["streamingRoute"]
        self.assertTrue(route["digestsMatch"])
        self.assertEqual(2, route["baselineUniqueActivatedChunkCount"])
        self.assertEqual(2, route["candidateUniqueActivatedChunkCount"])

        elapsed = next(
            metric for metric in route["metrics"]
            if metric["metric"] == "Wall-clock route completion (ms)"
        )
        self.assertEqual(1100.0, elapsed["baselineMedian"])
        self.assertEqual(900.0, elapsed["candidateMedian"])
        self.assertAlmostEqual(18.1818, elapsed["reductionPercent"], places=3)

        selection_latency = next(
            metric for metric in route["metrics"]
            if metric["metric"] == "Selection result latency p95 (frames)"
        )
        self.assertEqual(4.0, selection_latency["baselineMedian"])
        self.assertEqual(4.0, selection_latency["candidateMedian"])

        fluid_time = next(
            metric for metric in route["metrics"]
            if metric["metric"] == "Fluid activation processing time (ms)"
        )
        self.assertEqual(110.0, fluid_time["baselineMedian"])
        self.assertEqual(90.0, fluid_time["candidateMedian"])
        self.assertAlmostEqual(18.1818, fluid_time["reductionPercent"], places=3)

        fluid_frontier = next(
            metric for metric in route["metrics"]
            if metric["metric"] == "Fluid frontier cells processed"
        )
        self.assertEqual(1100.0, fluid_frontier["baselineMedian"])
        self.assertEqual(900.0, fluid_frontier["candidateMedian"])

        report = render_report(result)
        self.assertIn("Headless moving-route A/B", report)
        self.assertIn("Route content digest: identical", report)

    def test_compares_static_benchmark_without_streaming_route_profile(self) -> None:
        baseline = [sample_run() for _ in range(3)]
        candidate = [sample_run() for _ in range(3)]
        for run in baseline + candidate:
            run.pop("streamingRouteDiagnostics")

        result = compare(baseline, candidate, expected_chunks=67)
        self.assertIsNone(result["streamingRoute"])

    def test_reports_route_coordinate_set_mismatch_without_fake_delta(self) -> None:
        baseline = [sample_run() for _ in range(3)]
        candidate = [
            sample_run(streaming_route={
                "routeDigest": 987654322,
                "activatedChunkDigests": [
                    {"x": 1, "y": 2, "z": 3, "digest": 111, "blockDigest": 11, "fluidDigest": 12, "objectDigest": 13},
                    {"x": 3, "y": 2, "z": 3, "digest": 333, "blockDigest": 33, "fluidDigest": 34, "objectDigest": 35},
                ],
            })
            for _ in range(3)
        ]

        result = compare(baseline, candidate, expected_chunks=67)
        route = result["streamingRoute"]
        differences = route["routeContentDifferences"]

        self.assertFalse(route["digestsMatch"])
        self.assertEqual(0, differences["changedContentChunkCount"])
        self.assertEqual(0, differences["changedBlockContentChunkCount"])
        self.assertEqual(0, differences["changedFluidContentChunkCount"])
        self.assertEqual(0, differences["changedObjectContentChunkCount"])
        self.assertEqual(1, differences["baselineOnlyChunkCount"])
        self.assertEqual(1, differences["candidateOnlyChunkCount"])
        self.assertEqual([[2, 2, 3]], differences["baselineOnlySamples"])
        self.assertEqual([[3, 2, 3]], differences["candidateOnlySamples"])

        elapsed = next(
            metric for metric in route["metrics"]
            if metric["metric"] == "Wall-clock route completion (ms)"
        )
        self.assertIsNone(elapsed["reductionPercent"])

    def test_reports_route_content_mismatch_without_fake_delta(self) -> None:
        baseline = [sample_run()] * 3
        candidate = [
            sample_run(streaming_route={
                "routeDigest": 987654322,
                "activatedChunkDigests": [
                    {"x": 1, "y": 2, "z": 3, "digest": 111, "blockDigest": 11, "fluidDigest": 12, "objectDigest": 13},
                    {"x": 2, "y": 2, "z": 3, "digest": 333, "blockDigest": 22, "fluidDigest": 333, "objectDigest": 24},
                ],
            })
        ] * 3

        result = compare(baseline, candidate, expected_chunks=67)
        route = result["streamingRoute"]

        self.assertFalse(route["digestsMatch"])
        self.assertEqual(1, route["routeContentDifferences"]["changedContentChunkCount"])
        self.assertEqual(0, route["routeContentDifferences"]["changedBlockContentChunkCount"])
        self.assertEqual(1, route["routeContentDifferences"]["changedFluidContentChunkCount"])
        self.assertEqual(0, route["routeContentDifferences"]["changedObjectContentChunkCount"])
        self.assertEqual([[2, 2, 3]], route["routeContentDifferences"]["changedFluidContentSamples"])
        elapsed = next(
            metric for metric in route["metrics"]
            if metric["metric"] == "Wall-clock route completion (ms)"
        )
        self.assertEqual(1000.0, elapsed["baselineMedian"])
        self.assertEqual(1000.0, elapsed["candidateMedian"])
        self.assertIsNone(elapsed["reductionPercent"])

        report = render_report(result)
        self.assertIn("Timing values are descriptive only", report)
        self.assertIn("Chunks with changed blocks / fluids / Objects: 0 / 1 / 0", report)
        self.assertIn("| Wall-clock route completion (ms) | 1000.00 | 1000.00 | n/a |", report)

    def test_rejects_inconsistent_route_chunk_digest_between_repetitions(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for index, digest in enumerate((111, 111, 333), start=1):
                (root / f"{index}.json").write_text(
                    json.dumps(sample_run(streaming_route={
                        "activatedChunkDigests": [
                            {"x": 1, "y": 2, "z": 3, "digest": digest, "blockDigest": digest, "fluidDigest": 12, "objectDigest": 13},
                            {"x": 2, "y": 2, "z": 3, "digest": 222, "blockDigest": 22, "fluidDigest": 23, "objectDigest": 24},
                        ],
                    })),
                    encoding="utf-8",
                )

            with self.assertRaisesRegex(ValueError, "different per-coordinate"):
                load_repetitions(root, 3)

    def test_load_repetitions_rejects_inconsistent_streaming_route_digest(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for index, route_digest in enumerate(
                (987654321, 987654321, 987654322), start=1
            ):
                (root / f"{index}.json").write_text(
                    json.dumps(sample_run(
                        streaming_route={"routeDigest": route_digest}
                    )),
                    encoding="utf-8",
                )

            with self.assertRaisesRegex(ValueError, "same streaming route"):
                load_repetitions(root, 3)

    def test_reports_surface_decoration_time_and_allocations(self) -> None:
        baseline = [
            sample_run(surface_decoration_ms=value, surface_decoration_bytes=bytes_value)
            for value, bytes_value in zip(
                (100.0, 120.0, 110.0),
                (30 * 1024 * 1024, 32 * 1024 * 1024, 31 * 1024 * 1024),
            )
        ]
        candidate = [
            sample_run(surface_decoration_ms=value, surface_decoration_bytes=bytes_value)
            for value, bytes_value in zip(
                (80.0, 90.0, 100.0),
                (20 * 1024 * 1024, 21 * 1024 * 1024, 22 * 1024 * 1024),
            )
        ]

        result = compare(baseline, candidate, expected_chunks=67)

        surface = next(
            metric for metric in result["metrics"]
            if metric["metric"] == "SurfaceDecorations (warm)"
        )
        self.assertEqual(110.0, surface["baselineMedianMs"])
        self.assertEqual(90.0, surface["candidateMedianMs"])
        self.assertAlmostEqual(18.1818, surface["reductionPercent"], places=3)

        allocations = result["surfaceDecorationAllocations"]
        self.assertEqual(31.0, allocations["baselineMedianMiB"])
        self.assertEqual(21.0, allocations["candidateMedianMiB"])
        self.assertAlmostEqual(
            32.2580, allocations["reductionPercent"], places=3
        )
        report = render_report(result)
        self.assertIn("SurfaceDecorations allocations", report)
        self.assertIn("21.00", report)

    def test_compares_all_materialization_stages_and_normalizes_per_chunk(self) -> None:
        baseline = [
            sample_run(extra_stages={
                "GravitySupport": {
                    "elapsedMs": value,
                    "allocatedBytes": allocation * 1024 * 1024,
                },
            })
            for value, allocation in zip((24.0, 28.0, 32.0), (6, 7, 8))
        ]
        candidate = [
            sample_run(extra_stages={
                "GravitySupport": {
                    "elapsedMs": value,
                    "allocatedBytes": allocation * 1024 * 1024,
                },
            })
            for value, allocation in zip((12.0, 16.0, 20.0), (3, 4, 5))
        ]

        result = compare(baseline, candidate, expected_chunks=67)
        gravity = next(
            stage for stage in result["materializationStages"]
            if stage["stage"] == "GravitySupport"
        )

        self.assertEqual("both", gravity["availability"])
        self.assertEqual(28.0, gravity["baselineMedianMs"])
        self.assertEqual(16.0, gravity["candidateMedianMs"])
        self.assertAlmostEqual(42.8571, gravity["timeReductionPercent"], places=3)
        self.assertEqual(7.0, gravity["baselineMedianMiB"])
        self.assertEqual(4.0, gravity["candidateMedianMiB"])
        self.assertAlmostEqual(7.0 / 67.0, gravity["baselineMiBPerChunk"])
        self.assertAlmostEqual(16.0 / 67.0, gravity["candidateMsPerChunk"])

        report = render_report(result)
        self.assertIn("Materialization stage breakdown", report)
        self.assertIn("| GravitySupport | 28.00 | 16.00 | +42.9%", report)

    def test_stage_added_in_candidate_is_reported_without_fake_baseline_zero(self) -> None:
        baseline = [sample_run()] * 3
        candidate = [
            sample_run(extra_stages={
                "NewStage": {"elapsedMs": 5.0, "allocatedBytes": 4096},
            })
        ] * 3

        result = compare(baseline, candidate, expected_chunks=67)
        stage = next(
            value for value in result["materializationStages"]
            if value["stage"] == "NewStage"
        )

        self.assertEqual("candidate-only", stage["availability"])
        self.assertIsNone(stage["baselineMedianMs"])
        self.assertEqual(5.0, stage["candidateMedianMs"])
        self.assertIsNone(stage["timeReductionPercent"])
        self.assertIn("| NewStage | n/a | 5.00 | n/a |", render_report(result))

    def test_load_repetitions_rejects_inconsistent_stage_profiles(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "1.json").write_text(
                json.dumps(sample_run()), encoding="utf-8"
            )
            (root / "2.json").write_text(
                json.dumps(sample_run(extra_stages={
                    "UnexpectedStage": {"elapsedMs": 1.0, "allocatedBytes": 1},
                })), encoding="utf-8"
            )
            (root / "3.json").write_text(
                json.dumps(sample_run()), encoding="utf-8"
            )

            with self.assertRaisesRegex(ValueError, "same materialization stages"):
                load_repetitions(root, 3)

    def test_rejects_different_chunk_count(self) -> None:
        with self.assertRaisesRegex(ValueError, "Expected 67 initial-area chunks"):
            compare([sample_run(chunks=66)] * 3, [sample_run(chunks=66)] * 3, 67)

    def test_rejects_different_generated_output(self) -> None:
        with self.assertRaisesRegex(ValueError, "generated different results"):
            compare([sample_run()] * 3, [sample_run(digest=456)] * 3, 67)

    def test_rejects_different_regions(self) -> None:
        candidate = sample_run()
        candidate["initialAreaDiagnostics"]["spawnX"] += 1
        with self.assertRaisesRegex(ValueError, "same seed and initial region"):
            compare([sample_run()] * 3, [candidate] * 3, 67)

    def test_load_repetitions_rejects_inconsistent_digest(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for index, digest in enumerate((1, 1, 2), start=1):
                (root / f"{index}.json").write_text(
                    json.dumps(sample_run(digest=digest)), encoding="utf-8"
                )
            with self.assertRaisesRegex(ValueError, "different worldgen digests"):
                load_repetitions(root, 3)


if __name__ == "__main__":
    unittest.main()
