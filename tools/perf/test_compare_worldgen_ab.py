from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from compare_worldgen_ab import compare, load_repetitions, render_report


def sample_run(*, stage_ms: float = 100.0, materialize_ms: float = 200.0, synthesis_ms: float = 220.0, digest: int = 123, chunks: int = 67) -> dict:
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
            "warm": {"byStage": {"TerrainLayer": {"elapsedMs": stage_ms}}},
        },
    }


class CompareWorldgenAbTests(unittest.TestCase):
    def test_reports_medians_and_reduction(self) -> None:
        baseline = [sample_run(stage_ms=value) for value in (100.0, 120.0, 110.0)]
        candidate = [sample_run(stage_ms=value) for value in (80.0, 90.0, 100.0)]

        result = compare(baseline, candidate, expected_chunks=67)

        terrain = result["metrics"][0]
        self.assertEqual(110.0, terrain["baselineMedianMs"])
        self.assertEqual(90.0, terrain["candidateMedianMs"])
        self.assertAlmostEqual(18.1818, terrain["reductionPercent"], places=3)
        self.assertIn("identical", render_report(result))

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
