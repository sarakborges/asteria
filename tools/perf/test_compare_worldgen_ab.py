from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from compare_worldgen_ab import compare, load_repetitions, render_report


def sample_run(*, palette_ms: float = 50.0, surface_decoration_ms: float = 100.0, surface_decoration_bytes: int = 1024 * 1024, stage_ms: float = 100.0, materialize_ms: float = 200.0, synthesis_ms: float = 220.0, digest: int = 123, chunks: int = 67) -> dict:
    return {
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
            "warm": {"byStage": {
                "DirectionalFacePalettes": {"elapsedMs": palette_ms},
                "SurfaceDecorations": {
                    "elapsedMs": surface_decoration_ms,
                    "allocatedBytes": surface_decoration_bytes,
                },
                "TerrainLayer": {"elapsedMs": stage_ms},
            }},
        },
    }


class CompareWorldgenAbTests(unittest.TestCase):
    def test_reports_directional_palette_and_materialization_medians(self) -> None:
        baseline = [
            sample_run(palette_ms=value, stage_ms=stage)
            for value, stage in zip((60.0, 80.0, 70.0), (100.0, 120.0, 110.0))
        ]
        candidate = [
            sample_run(palette_ms=value, stage_ms=stage)
            for value, stage in zip((40.0, 50.0, 60.0), (80.0, 90.0, 100.0))
        ]

        result = compare(baseline, candidate, expected_chunks=67)

        palette = result["metrics"][0]
        self.assertEqual("DirectionalFacePalettes (warm)", palette["metric"])
        self.assertEqual(70.0, palette["baselineMedianMs"])
        self.assertEqual(50.0, palette["candidateMedianMs"])
        self.assertAlmostEqual(28.5714, palette["reductionPercent"], places=3)

        terrain = result["metrics"][1]
        self.assertEqual("TerrainLayer (warm)", terrain["metric"])
        self.assertEqual(110.0, terrain["baselineMedianMs"])
        self.assertEqual(90.0, terrain["candidateMedianMs"])
        self.assertAlmostEqual(18.1818, terrain["reductionPercent"], places=3)
        report = render_report(result)
        self.assertIn("DirectionalFacePalettes", report)
        self.assertIn("identical", report)

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
