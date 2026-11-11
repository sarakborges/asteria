#!/usr/bin/env python3
"""Compare repeated, same-runner WorldgenBenchmark A/B results."""
from __future__ import annotations

import argparse
import json
import math
import statistics
import sys
from pathlib import Path
from typing import Any


def _load_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"Could not read benchmark result {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise ValueError(f"Benchmark result {path} must contain a JSON object.")
    return value


def _at(data: dict[str, Any], *keys: str) -> Any:
    current: Any = data
    for key in keys:
        if not isinstance(current, dict) or key not in current:
            raise ValueError(f"Benchmark result is missing {'.'.join(keys)}.")
        current = current[key]
    return current


def _streaming_route_identity(run: dict[str, Any]) -> tuple[Any, ...] | None:
    route = run.get("streamingRouteDiagnostics")
    if route is None:
        return None
    if not isinstance(route, dict):
        raise ValueError("Streaming route diagnostics must contain an object.")

    fields = (
        "routeSteps",
        "movementChunks",
        "framesPerChunk",
        "horizontalRadius",
        "retentionRadius",
        "uniqueActivatedChunkCount",
        "routeDigest",
    )
    identity = tuple(_at(route, field) for field in fields)
    if route.get("generatedChunkDigestsMatchOnReactivation") is not True:
        raise ValueError(
            "Streaming route repeated activation produced inconsistent chunk content."
        )

    for field in (
        "totalElapsedMilliseconds",
        "totalWorkerMilliseconds",
        "averageWorkerMillisecondsPerProviderActivation",
        "selectionResultLatencyMedianFrames",
        "selectionResultLatencyP95Frames",
        "frameCount",
        "framesWithPlayerLocalGaps",
        "maximumPlayerLocalMissingChunks",
        "maximumPendingChunks",
        "maximumActiveWorkers",
        "totalFluidCellsActivated",
        "totalFluidFrontierCellsActivated",
        "totalFluidActivationSlices",
        "totalFluidActivationMilliseconds",
        "maximumFluidActivationSliceMilliseconds",
        "maximumPendingFluidActivationChunks",
    ):
        value = float(_at(route, field))
        if not math.isfinite(value) or value < 0:
            raise ValueError(
                f"Streaming route metric {field} must be finite and non-negative."
            )
    return identity


def _streaming_physics_signature(run: dict[str, Any]) -> tuple[Any, ...] | None:
    route = run.get("streamingRouteDiagnostics")
    if route is None:
        return None
    samples = _at(route, "fallingBlockSamples")
    if not isinstance(samples, list):
        raise ValueError("Streaming route fallingBlockSamples must be an array.")
    normalized = []
    for sample in samples:
        if not isinstance(sample, dict):
            raise ValueError("Falling block samples must be objects.")
        normalized.append((
            str(_at(sample, "block")),
            bool(_at(sample, "isGravityTagged")),
            int(_at(sample, "x")),
            int(_at(sample, "y")),
            int(_at(sample, "z")),
            int(_at(sample, "sourceChunkX")),
            int(_at(sample, "sourceChunkY")),
            int(_at(sample, "sourceChunkZ")),
            str(_at(sample, "belowBlock")),
            bool(_at(sample, "belowChunkLoaded")),
        ))
    return (
        int(_at(route, "totalFallingBlocksStarted")),
        int(_at(route, "totalUnsupportedBlocksRemoved")),
        int(_at(route, "observedFallingEntityCount")),
        tuple(normalized),
    )


def _streaming_route_chunks(
    run: dict[str, Any],
) -> dict[tuple[int, int, int], tuple[int, int, int, int]] | None:
    route = run.get("streamingRouteDiagnostics")
    if route is None:
        return None
    entries = _at(route, "activatedChunkDigests")
    if not isinstance(entries, list):
        raise ValueError("Streaming route activatedChunkDigests must be an array.")

    chunks: dict[tuple[int, int, int], tuple[int, int, int, int]] = {}
    for entry in entries:
        if not isinstance(entry, dict):
            raise ValueError("Streaming route chunk digest entries must be objects.")
        coord = (int(_at(entry, "x")), int(_at(entry, "y")), int(_at(entry, "z")))
        if coord in chunks:
            raise ValueError(f"Streaming route contains duplicate chunk coordinate {coord}.")
        chunks[coord] = (
            int(_at(entry, "digest")),
            int(_at(entry, "blockDigest")),
            int(_at(entry, "fluidDigest")),
            int(_at(entry, "objectDigest")),
        )

    expected_count = int(_at(route, "uniqueActivatedChunkCount"))
    if len(chunks) != expected_count:
        raise ValueError(
            f"Streaming route reports {expected_count} unique chunks but includes {len(chunks)} chunk digests."
        )
    return chunks

def load_repetitions(directory: Path, repetitions: int) -> list[dict[str, Any]]:
    runs = [_load_json(directory / f"{index}.json") for index in range(1, repetitions + 1)]
    identity = None
    expected_digests = None
    expected_stage_names = None
    expected_streaming_identity = None
    expected_streaming_chunks = None
    expected_physics_signature = None
    expected_streaming_presence = None

    for index, run in enumerate(runs, start=1):
        run_identity = (
            _at(run, "seed"),
            _at(run, "dimension"),
            _at(run, "dimensionSeed"),
            tuple(_at(run, "center")),
            _at(run, "initialAreaDiagnostics", "spawnX"),
            _at(run, "initialAreaDiagnostics", "spawnY"),
            _at(run, "initialAreaDiagnostics", "spawnZ"),
            _at(run, "initialAreaDiagnostics", "centerChunkX"),
            _at(run, "initialAreaDiagnostics", "centerChunkY"),
            _at(run, "initialAreaDiagnostics", "centerChunkZ"),
            _at(run, "initialAreaDiagnostics", "desiredChunkCount"),
        )
        metrics = _at(run, "metrics")
        if not isinstance(metrics, dict) or not metrics:
            raise ValueError(f"{directory}/{index}.json contains no benchmark metrics.")

        stages = _at(run, "initialAreaStageTimingDiagnostics", "warm", "byStage")
        if not isinstance(stages, dict):
            raise ValueError(
                f"{directory}/{index}.json has an invalid materialization stage profile."
            )
        stage_names = tuple(sorted(stages))
        if expected_stage_names is None:
            expected_stage_names = stage_names
        elif stage_names != expected_stage_names:
            raise ValueError(
                f"{directory} repetitions do not contain the same materialization stages."
            )
        for stage_name, stage in stages.items():
            if not isinstance(stage, dict):
                raise ValueError(
                    f"{directory}/{index}.json stage {stage_name} must contain an object."
                )
            for field in ("elapsedMs", "allocatedBytes"):
                if field not in stage:
                    raise ValueError(
                        f"{directory}/{index}.json stage {stage_name} is missing {field}."
                    )
                value = float(stage[field])
                if not math.isfinite(value) or value < 0:
                    raise ValueError(
                        f"{directory}/{index}.json stage {stage_name} has invalid {field}."
                    )

        digests = {
            name: _at(metrics, name, "digest")
            for name in sorted(metrics)
        }

        streaming_identity = _streaming_route_identity(run)
        streaming_chunks = _streaming_route_chunks(run)
        physics_signature = _streaming_physics_signature(run)
        streaming_presence = streaming_identity is not None
        if expected_streaming_presence is None:
            expected_streaming_presence = streaming_presence
            expected_streaming_identity = streaming_identity
            expected_streaming_chunks = streaming_chunks
            expected_physics_signature = physics_signature
        elif streaming_presence != expected_streaming_presence:
            raise ValueError(
                f"{directory} repetitions must all include the same streaming route profile."
            )
        elif streaming_identity != expected_streaming_identity:
            raise ValueError(
                f"{directory} repetitions do not use the same streaming route or generated chunk digest."
            )
        elif streaming_chunks != expected_streaming_chunks:
            raise ValueError(
                f"{directory} repetitions generated different per-coordinate streaming chunk digests."
            )
        elif physics_signature != expected_physics_signature:
            raise ValueError(
                f"{directory} repetitions observed different falling-block physics outcomes."
            )

        if identity is None:
            identity = run_identity
            expected_digests = digests
        elif run_identity != identity:
            raise ValueError(
                f"{directory} repetitions do not use the same seed/region/chunk selection."
            )
        elif digests != expected_digests:
            raise ValueError(f"{directory} repetitions produced different worldgen digests.")

        # Validate all values used by the report while preserving the original
        # result JSON for cross-build parity checks.
        _at(run, "metrics", "initialAreaSynthesis", "warmMs")
        _at(run, "initialAreaMaterializationDiagnostics", "warm", "totalMaterializeMs")
        _stage_elapsed(run, "DirectionalFacePalettes")
        _stage_elapsed(run, "DirectionalFaceExposure")
        _at(run, "initialAreaStageTimingDiagnostics", "warm", "byStage", "SurfaceDecorations", "elapsedMs")
        _at(run, "initialAreaStageTimingDiagnostics", "warm", "byStage", "SurfaceDecorations", "allocatedBytes")
        _at(run, "initialAreaStageTimingDiagnostics", "warm", "byStage", "TerrainLayer", "elapsedMs")

    return runs


def median_metric(runs: list[dict[str, Any]], *keys: str) -> float:
    values = [float(_at(run, *keys)) for run in runs]
    if any(not math.isfinite(value) or value < 0 for value in values):
        raise ValueError(f"Benchmark metric {'.'.join(keys)} must be finite and non-negative.")
    return statistics.median(values)


def _stage_elapsed(run: dict[str, Any], stage_name: str) -> float:
    stages = _at(run, "initialAreaStageTimingDiagnostics", "warm", "byStage")
    stage = stages.get(stage_name, {})
    if not isinstance(stage, dict):
        raise ValueError(f"Benchmark stage {stage_name} must contain an object.")
    value = float(stage.get("elapsedMs", 0.0))
    if value < 0:
        raise ValueError(f"Benchmark stage {stage_name} cannot be negative.")
    return value


def _directional_face_palette_elapsed(run: dict[str, Any]) -> float:
    # Exposure selection is now a distinct generation stage. Include both it
    # and the subsequent palette application when comparing the full operation.
    return (
        _stage_elapsed(run, "DirectionalFaceExposure")
        + _stage_elapsed(run, "DirectionalFacePalettes")
    )


def _combined_stage_median(
    runs: list[dict[str, Any]],
    stage_names: tuple[str, ...],
    field: str,
) -> float:
    values = []
    for run in runs:
        stages = _at(run, "initialAreaStageTimingDiagnostics", "warm", "byStage")
        missing = [name for name in stage_names if name not in stages]
        if missing:
            raise ValueError(
                "Benchmark result is missing materialization stages: "
                + ", ".join(missing)
            )
        total = sum(float(stages[name][field]) for name in stage_names)
        if not math.isfinite(total) or total < 0:
            raise ValueError(
                f"Combined materialization stages {stage_names}.{field} must be finite and non-negative."
            )
        values.append(total)
    return statistics.median(values)


def _stage_median(
    runs: list[dict[str, Any]],
    stage_name: str,
    field: str,
) -> float | None:
    stages = _at(runs[0], "initialAreaStageTimingDiagnostics", "warm", "byStage")
    if stage_name not in stages:
        return None
    values = [
        float(_at(run, "initialAreaStageTimingDiagnostics", "warm", "byStage", stage_name)[field])
        for run in runs
    ]
    if any(not math.isfinite(value) or value < 0 for value in values):
        raise ValueError(
            f"Materialization stage {stage_name}.{field} must be finite and non-negative."
        )
    return statistics.median(values)


def _reduction_percent(before: float | None, after: float | None) -> float | None:
    if before is None or after is None:
        return None
    return 0.0 if before == 0 else (before - after) / before * 100.0


def _compare_materialization_stages(
    baseline: list[dict[str, Any]],
    candidate: list[dict[str, Any]],
    expected_chunks: int,
) -> list[dict[str, Any]]:
    baseline_stages = _at(
        baseline[0], "initialAreaStageTimingDiagnostics", "warm", "byStage"
    )
    candidate_stages = _at(
        candidate[0], "initialAreaStageTimingDiagnostics", "warm", "byStage"
    )
    results = []

    for stage_name in sorted(set(baseline_stages) | set(candidate_stages)):
        before_ms = _stage_median(baseline, stage_name, "elapsedMs")
        after_ms = _stage_median(candidate, stage_name, "elapsedMs")
        before_bytes = _stage_median(baseline, stage_name, "allocatedBytes")
        after_bytes = _stage_median(candidate, stage_name, "allocatedBytes")
        before_mib = None if before_bytes is None else before_bytes / (1024 * 1024)
        after_mib = None if after_bytes is None else after_bytes / (1024 * 1024)

        results.append({
            "stage": stage_name,
            "availability": (
                "both" if before_ms is not None and after_ms is not None
                else "baseline-only" if before_ms is not None
                else "candidate-only"
            ),
            "baselineMedianMs": before_ms,
            "candidateMedianMs": after_ms,
            "timeReductionPercent": _reduction_percent(before_ms, after_ms),
            "baselineMedianMiB": before_mib,
            "candidateMedianMiB": after_mib,
            "allocationReductionPercent": _reduction_percent(before_bytes, after_bytes),
            "baselineMsPerChunk": (
                None if before_ms is None else before_ms / expected_chunks
            ),
            "candidateMsPerChunk": (
                None if after_ms is None else after_ms / expected_chunks
            ),
            "baselineMiBPerChunk": (
                None if before_mib is None else before_mib / expected_chunks
            ),
            "candidateMiBPerChunk": (
                None if after_mib is None else after_mib / expected_chunks
            ),
        })

    return results


def compare(
    baseline: list[dict[str, Any]],
    candidate: list[dict[str, Any]],
    expected_chunks: int,
    baseline_sha: str | None = None,
    candidate_sha: str | None = None,
) -> dict[str, Any]:
    if len(baseline) != len(candidate):
        raise ValueError("Baseline and candidate must have the same number of repetitions.")

    baseline_first = baseline[0]
    candidate_first = candidate[0]
    baseline_identity = (
        _at(baseline_first, "seed"),
        _at(baseline_first, "dimension"),
        _at(baseline_first, "dimensionSeed"),
        tuple(_at(baseline_first, "center")),
        _at(baseline_first, "initialAreaDiagnostics", "spawnX"),
        _at(baseline_first, "initialAreaDiagnostics", "spawnY"),
        _at(baseline_first, "initialAreaDiagnostics", "spawnZ"),
        _at(baseline_first, "initialAreaDiagnostics", "centerChunkX"),
        _at(baseline_first, "initialAreaDiagnostics", "centerChunkY"),
        _at(baseline_first, "initialAreaDiagnostics", "centerChunkZ"),
        _at(baseline_first, "initialAreaDiagnostics", "desiredChunkCount"),
    )
    candidate_identity = (
        _at(candidate_first, "seed"),
        _at(candidate_first, "dimension"),
        _at(candidate_first, "dimensionSeed"),
        tuple(_at(candidate_first, "center")),
        _at(candidate_first, "initialAreaDiagnostics", "spawnX"),
        _at(candidate_first, "initialAreaDiagnostics", "spawnY"),
        _at(candidate_first, "initialAreaDiagnostics", "spawnZ"),
        _at(candidate_first, "initialAreaDiagnostics", "centerChunkX"),
        _at(candidate_first, "initialAreaDiagnostics", "centerChunkY"),
        _at(candidate_first, "initialAreaDiagnostics", "centerChunkZ"),
        _at(candidate_first, "initialAreaDiagnostics", "desiredChunkCount"),
    )
    if baseline_identity != candidate_identity:
        raise ValueError("Baseline and candidate do not represent the same seed and initial region.")
    if baseline_identity[-1] != expected_chunks:
        raise ValueError(
            f"Expected {expected_chunks} initial-area chunks, got {baseline_identity[-1]}."
        )

    baseline_digests = {
        name: _at(_at(baseline_first, "metrics"), name, "digest")
        for name in sorted(_at(baseline_first, "metrics"))
    }
    candidate_digests = {
        name: _at(_at(candidate_first, "metrics"), name, "digest")
        for name in sorted(_at(candidate_first, "metrics"))
    }
    if baseline_digests != candidate_digests:
        differing = sorted(
            name for name in set(baseline_digests) | set(candidate_digests)
            if baseline_digests.get(name) != candidate_digests.get(name)
        )
        raise ValueError(
            "Baseline and candidate generated different results for metrics: "
            + ", ".join(differing)
        )

    baseline_route_identity = _streaming_route_identity(baseline_first)
    candidate_route_identity = _streaming_route_identity(candidate_first)
    if (baseline_route_identity is None) != (candidate_route_identity is None):
        raise ValueError(
            "Baseline and candidate must both include the streaming route profile."
        )

    streaming_route_result = None
    if baseline_route_identity is not None:
        if baseline_route_identity[:5] != candidate_route_identity[:5]:
            raise ValueError("Baseline and candidate streaming route geometry does not match.")

        baseline_route_chunks = _streaming_route_chunks(baseline_first) or {}
        candidate_route_chunks = _streaming_route_chunks(candidate_first) or {}
        shared_route_coords = set(baseline_route_chunks) & set(candidate_route_chunks)
        changed_route_coords = sorted(
            coord for coord in shared_route_coords
            if baseline_route_chunks[coord] != candidate_route_chunks[coord]
        )
        changed_block_route_coords = sorted(
            coord for coord in shared_route_coords
            if baseline_route_chunks[coord][1] != candidate_route_chunks[coord][1]
        )
        changed_fluid_route_coords = sorted(
            coord for coord in shared_route_coords
            if baseline_route_chunks[coord][2] != candidate_route_chunks[coord][2]
        )
        changed_object_route_coords = sorted(
            coord for coord in shared_route_coords
            if baseline_route_chunks[coord][3] != candidate_route_chunks[coord][3]
        )
        baseline_only_route_coords = sorted(
            set(baseline_route_chunks) - set(candidate_route_chunks)
        )
        candidate_only_route_coords = sorted(
            set(candidate_route_chunks) - set(baseline_route_chunks)
        )
        route_digests_match = (
            baseline_route_identity[5:] == candidate_route_identity[5:]
            and not changed_route_coords
            and not baseline_only_route_coords
            and not candidate_only_route_coords
        )

    definitions = [
        (
            "DirectionalFacePalettes (warm)",
            _directional_face_palette_elapsed,
        ),
        (
            "TerrainLayer + directional faces (warm)",
            lambda run: (
                float(_at(run, "initialAreaStageTimingDiagnostics", "warm", "byStage", "TerrainLayer", "elapsedMs"))
                + float(_at(run, "initialAreaStageTimingDiagnostics", "warm", "byStage", "DirectionalFacePalettes", "elapsedMs"))
            ),
        ),
        (
            "SurfaceDecorations (warm)",
            lambda run: _at(run, "initialAreaStageTimingDiagnostics", "warm", "byStage", "SurfaceDecorations", "elapsedMs"),
        ),
        (
            "TerrainLayer (warm)",
            lambda run: _at(run, "initialAreaStageTimingDiagnostics", "warm", "byStage", "TerrainLayer", "elapsedMs"),
        ),
        (
            "Materialização da região (warm)",
            lambda run: _at(run, "initialAreaMaterializationDiagnostics", "warm", "totalMaterializeMs"),
        ),
        (
            "Síntese da área inicial (warm)",
            lambda run: _at(run, "metrics", "initialAreaSynthesis", "warmMs"),
        ),
    ]
    metric_results = []
    for label, read_value in definitions:
        before_values = [float(read_value(run)) for run in baseline]
        after_values = [float(read_value(run)) for run in candidate]
        if any(value < 0 for value in before_values + after_values):
            raise ValueError(f"Benchmark metric {label} cannot be negative.")
        before = statistics.median(before_values)
        after = statistics.median(after_values)
        reduction = 0.0 if before == 0 else (before - after) / before * 100.0
        metric_results.append({
            "metric": label,
            "baselineMedianMs": before,
            "candidateMedianMs": after,
            "reductionPercent": reduction,
        })

    baseline_surface_decoration_bytes = median_metric(
        baseline,
        "initialAreaStageTimingDiagnostics", "warm", "byStage",
        "SurfaceDecorations", "allocatedBytes",
    )
    candidate_surface_decoration_bytes = median_metric(
        candidate,
        "initialAreaStageTimingDiagnostics", "warm", "byStage",
        "SurfaceDecorations", "allocatedBytes",
    )
    baseline_surface_decoration_mib = baseline_surface_decoration_bytes / (1024 * 1024)
    candidate_surface_decoration_mib = candidate_surface_decoration_bytes / (1024 * 1024)
    allocation_reduction = (
        0.0 if baseline_surface_decoration_bytes == 0
        else (baseline_surface_decoration_bytes - candidate_surface_decoration_bytes)
        / baseline_surface_decoration_bytes * 100.0
    )

    materialization_stages = _compare_materialization_stages(
        baseline, candidate, expected_chunks
    )
    baseline_terrain_face_ms = _combined_stage_median(
        baseline, ("TerrainLayer", "DirectionalFacePalettes"), "elapsedMs"
    )
    candidate_terrain_face_ms = _combined_stage_median(
        candidate, ("TerrainLayer", "DirectionalFacePalettes"), "elapsedMs"
    )
    baseline_terrain_face_bytes = _combined_stage_median(
        baseline, ("TerrainLayer", "DirectionalFacePalettes"), "allocatedBytes"
    )
    candidate_terrain_face_bytes = _combined_stage_median(
        candidate, ("TerrainLayer", "DirectionalFacePalettes"), "allocatedBytes"
    )
    terrain_face_allocations = {
        "baselineMedianMiB": baseline_terrain_face_bytes / (1024 * 1024),
        "candidateMedianMiB": candidate_terrain_face_bytes / (1024 * 1024),
        "reductionPercent": _reduction_percent(
            baseline_terrain_face_bytes, candidate_terrain_face_bytes
        ),
    }

    if baseline_route_identity is not None:
        streaming_route_definitions = [
            ("Wall-clock route completion (ms)", "totalElapsedMilliseconds"),
            ("Total chunk worker time (ms)", "totalWorkerMilliseconds"),
            ("Selection result latency p95 (frames)", "selectionResultLatencyP95Frames"),
            ("Frames until route drain", "frameCount"),
            ("Frames with player-local gaps", "framesWithPlayerLocalGaps"),
            ("Maximum missing player-local chunks", "maximumPlayerLocalMissingChunks"),
            ("Maximum pending chunks", "maximumPendingChunks"),
            ("Fluid activation processing time (ms)", "totalFluidActivationMilliseconds"),
            ("Maximum fluid activation slice (ms)", "maximumFluidActivationSliceMilliseconds"),
            ("Fluid activation slices", "totalFluidActivationSlices"),
            ("Fluid cells activated", "totalFluidCellsActivated"),
            ("Fluid frontier cells processed", "totalFluidFrontierCellsActivated"),
            ("Maximum pending fluid-activation chunks", "maximumPendingFluidActivationChunks"),
            ("Gravity blocks started falling", "totalFallingBlocksStarted"),
            ("Unsupported blocks removed", "totalUnsupportedBlocksRemoved"),
        ]
        route_metrics = []
        for label, field in streaming_route_definitions:
            before = median_metric(baseline, "streamingRouteDiagnostics", field)
            after = median_metric(candidate, "streamingRouteDiagnostics", field)
            route_metrics.append({
                "metric": label,
                "baselineMedian": before,
                "candidateMedian": after,
                "reductionPercent": (
                    _reduction_percent(before, after)
                    if route_digests_match
                    else None
                ),
            })
        baseline_physics = _streaming_physics_signature(baseline_first)
        candidate_physics = _streaming_physics_signature(candidate_first)
        streaming_route_result = {
            "physicsDifferences": {
                "baselineFallingBlocksStarted": baseline_physics[0],
                "candidateFallingBlocksStarted": candidate_physics[0],
                "baselineUnsupportedBlocksRemoved": baseline_physics[1],
                "candidateUnsupportedBlocksRemoved": candidate_physics[1],
                "baselineFallingBlockSamples": [
                    {
                        "block": item[0],
                        "isGravityTagged": item[1],
                        "x": item[2], "y": item[3], "z": item[4],
                        "chunk": [item[5], item[6], item[7]],
                        "belowBlock": item[8],
                        "belowChunkLoaded": item[9],
                    }
                    for item in baseline_physics[3]
                ],
                "candidateFallingBlockSamples": [
                    {
                        "block": item[0],
                        "isGravityTagged": item[1],
                        "x": item[2], "y": item[3], "z": item[4],
                        "chunk": [item[5], item[6], item[7]],
                        "belowBlock": item[8],
                        "belowChunkLoaded": item[9],
                    }
                    for item in candidate_physics[3]
                ],
            },
            "routeSteps": baseline_route_identity[0],
            "movementChunks": baseline_route_identity[1],
            "framesPerChunk": baseline_route_identity[2],
            "horizontalRadius": baseline_route_identity[3],
            "retentionRadius": baseline_route_identity[4],
            "baselineUniqueActivatedChunkCount": len(baseline_route_chunks),
            "candidateUniqueActivatedChunkCount": len(candidate_route_chunks),
            "baselineRouteDigest": baseline_route_identity[6],
            "candidateRouteDigest": candidate_route_identity[6],
            "digestsMatch": route_digests_match,
            "routeContentDifferences": {
                "changedContentChunkCount": len(changed_route_coords),
                "changedBlockContentChunkCount": len(changed_block_route_coords),
                "changedFluidContentChunkCount": len(changed_fluid_route_coords),
                "changedObjectContentChunkCount": len(changed_object_route_coords),
                "baselineOnlyChunkCount": len(baseline_only_route_coords),
                "candidateOnlyChunkCount": len(candidate_only_route_coords),
                "changedContentSamples": [list(coord) for coord in changed_route_coords[:12]],
                "changedBlockContentSamples": [list(coord) for coord in changed_block_route_coords[:12]],
                "changedFluidContentSamples": [list(coord) for coord in changed_fluid_route_coords[:12]],
                "changedObjectContentSamples": [list(coord) for coord in changed_object_route_coords[:12]],
                "baselineOnlySamples": [list(coord) for coord in baseline_only_route_coords[:12]],
                "candidateOnlySamples": [list(coord) for coord in candidate_only_route_coords[:12]],
            },
            "metrics": route_metrics,
            "baselineMaximumActiveWorkers": max(
                int(_at(run, "streamingRouteDiagnostics", "maximumActiveWorkers"))
                for run in baseline
            ),
            "candidateMaximumActiveWorkers": max(
                int(_at(run, "streamingRouteDiagnostics", "maximumActiveWorkers"))
                for run in candidate
            ),
        }

    return {
        "streamingRoute": streaming_route_result,
        "materializationStages": materialization_stages,
        "terrainAndFaceAllocations": terrain_face_allocations,
        "surfaceDecorationAllocations": {
            "baselineMedianMiB": baseline_surface_decoration_mib,
            "candidateMedianMiB": candidate_surface_decoration_mib,
            "reductionPercent": allocation_reduction,
        },
        "seed": _at(baseline_first, "seed"),
        "dimension": _at(baseline_first, "dimension"),
        "baselineCommit": baseline_sha,
        "candidateCommit": candidate_sha,
        "center": list(_at(baseline_first, "center")),
        "chunkCount": expected_chunks,
        "repetitions": len(baseline),
        "digestsMatch": True,
        "metrics": metric_results,
        "notes": [
            "Baseline is the fixed pre-feature worldgen revision; candidate is the complete pull-request head.",
            "Each value is the median across separate benchmark processes; each process also reports the benchmark's internal warm median where available.",
            "TerrainLayer and DirectionalFacePalettes are also compared as one combined operation because directional-face candidate preparation is performed during TerrainLayer in the candidate implementation.",
            "Release A/B runs set DOTNET_TieredCompilation=0 for both revisions, matching the game's single-tier optimizing JIT configuration.",
            "Wall-clock deltas are informational and do not fail CI; seed, region, chunk count and generated-result digest mismatches do fail CI.",
        ],
    }


def render_report(result: dict[str, Any]) -> str:
    lines = [
        "# Terrain world-generation A/B",
        "",
        f"- Seed: `{result['seed']}` ({result['dimension']})",
        f"- Initial region center: `{result['center']}`",
        f"- Baseline commit: `{result['baselineCommit'] or 'not supplied'}`",
        f"- Candidate commit: `{result['candidateCommit'] or 'not supplied'}`",
        f"- Chunks: **{result['chunkCount']}**",
        f"- Repetitions per version: **{result['repetitions']}**",
        "- Generated-result digests: **identical**",
        "",
        "| Métrica | Baseline (ms) | Otimizada (ms) | Redução |",
        "|---|---:|---:|---:|",
    ]
    for metric in result["metrics"]:
        lines.append(
            f"| {metric['metric']} | {metric['baselineMedianMs']:.2f} "
            f"| {metric['candidateMedianMs']:.2f} "
            f"| {metric['reductionPercent']:+.1f}% |"
        )
    terrain_face_allocations = result["terrainAndFaceAllocations"]
    lines.extend([
        "",
        "## TerrainLayer + directional-face combined allocations",
        "",
        "| Métrica | Baseline (MiB) | Candidate (MiB) | Redução |",
        "|---|---:|---:|---:|",
        f"| Alocações combinadas | {terrain_face_allocations['baselineMedianMiB']:.2f} "
        f"| {terrain_face_allocations['candidateMedianMiB']:.2f} "
        f"| {terrain_face_allocations['reductionPercent']:+.1f}% |",
    ])
    allocations = result["surfaceDecorationAllocations"]
    lines.extend([
        "",
        "## SurfaceDecorations allocations",
        "",
        "| Métrica | Baseline (MiB) | Otimizada (MiB) | Redução |",
        "|---|---:|---:|---:|",
        f"| Alocações warm | {allocations['baselineMedianMiB']:.2f} "
        f"| {allocations['candidateMedianMiB']:.2f} "
        f"| {allocations['reductionPercent']:+.1f}% |",
    ])
    lines.extend([
        "",
        "## Materialization stage breakdown",
        "",
        "| Stage | Baseline (ms) | Candidate (ms) | Time delta | Baseline (MiB) | Candidate (MiB) | Allocation delta |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ])
    for stage in result["materializationStages"]:
        before_ms = stage["baselineMedianMs"]
        after_ms = stage["candidateMedianMs"]
        before_mib = stage["baselineMedianMiB"]
        after_mib = stage["candidateMedianMiB"]
        time_delta = stage["timeReductionPercent"]
        allocation_delta = stage["allocationReductionPercent"]
        lines.append(
            f"| {stage['stage']} "
            f"| {'n/a' if before_ms is None else f'{before_ms:.2f}'} "
            f"| {'n/a' if after_ms is None else f'{after_ms:.2f}'} "
            f"| {'n/a' if time_delta is None else f'{time_delta:+.1f}%'} "
            f"| {'n/a' if before_mib is None else f'{before_mib:.2f}'} "
            f"| {'n/a' if after_mib is None else f'{after_mib:.2f}'} "
            f"| {'n/a' if allocation_delta is None else f'{allocation_delta:+.1f}%'} |"
        )
    streaming_route = result.get("streamingRoute")
    if streaming_route is not None:
        lines.extend([
            "",
            "## Headless moving-route A/B",
            "",
            f"- Route: {streaming_route['routeSteps']} positions, radius {streaming_route['horizontalRadius']} chunks",
            f"- Unique activated chunks: baseline {streaming_route['baselineUniqueActivatedChunkCount']}; candidate {streaming_route['candidateUniqueActivatedChunkCount']}",
        ])
        differences = streaming_route["routeContentDifferences"]
        physics = streaming_route["physicsDifferences"]
        if physics["baselineFallingBlocksStarted"] != physics["candidateFallingBlocksStarted"] or physics["baselineUnsupportedBlocksRemoved"] != physics["candidateUnsupportedBlocksRemoved"]:
            lines.extend([
                f"- Gravity blocks started falling: baseline {physics['baselineFallingBlocksStarted']}; candidate {physics['candidateFallingBlocksStarted']}",
                f"- Unsupported blocks removed: baseline {physics['baselineUnsupportedBlocksRemoved']}; candidate {physics['candidateUnsupportedBlocksRemoved']}",
            ])
        if physics["baselineFallingBlockSamples"] or physics["candidateFallingBlockSamples"]:
            lines.extend([
                f"- Baseline falling-block samples: {physics['baselineFallingBlockSamples']}",
                f"- Candidate falling-block samples: {physics['candidateFallingBlockSamples']}",
            ])
        if streaming_route["digestsMatch"]:
            lines.append(f"- Route content digest: identical ({streaming_route['baselineRouteDigest']})")
        else:
            lines.extend([
                "- Route content digest: DIFFERENT. Timing values are descriptive only; percentage deltas are withheld.",
                f"- Chunks with changed blocks / fluids / Objects: "
                f"{differences['changedBlockContentChunkCount']} / "
                f"{differences['changedFluidContentChunkCount']} / "
                f"{differences['changedObjectContentChunkCount']}",
                f"- Coordinates only in baseline/candidate: {differences['baselineOnlyChunkCount']}/{differences['candidateOnlyChunkCount']}",
                f"- Changed-coordinate samples: {differences['changedContentSamples']}",
                f"- Block-changed samples: {differences['changedBlockContentSamples']}",
                f"- Fluid-changed samples: {differences['changedFluidContentSamples']}",
                f"- Object-changed samples: {differences['changedObjectContentSamples']}",
                f"- Baseline-only samples: {differences['baselineOnlySamples']}",
                f"- Candidate-only samples: {differences['candidateOnlySamples']}",
            ])
        lines.extend([
            "",
            "| Metric | Baseline median | Candidate median | Reduction |",
            "|---|---:|---:|---:|",
        ])
        for metric in streaming_route["metrics"]:
            reduction = metric["reductionPercent"]
            reduction_text = "n/a" if reduction is None else f"{reduction:+.1f}%"
            lines.append(
                f"| {metric['metric']} | {metric['baselineMedian']:.2f} "
                f"| {metric['candidateMedian']:.2f} | {reduction_text} |"
            )
        lines.extend([
            "",
            f"Maximum active materialization workers: baseline "
            f"{streaming_route['baselineMaximumActiveWorkers']}; candidate "
            f"{streaming_route['candidateMaximumActiveWorkers']}.",
        ])
    lines.extend([
        "",
        "Stage timings are medians across benchmark processes. Per-chunk values are also available in the JSON output.",
        "A stage present in only one revision is reported as n/a on the other side; it is never treated as zero.",
        "DirectionalFacePalettes (warm) in the headline table includes exposure plus palette application; the breakdown keeps them separate.",
        "",
        *[f"- {note}" for note in result["notes"]],
        "",
    ])
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline-dir", required=True, type=Path)
    parser.add_argument("--candidate-dir", required=True, type=Path)
    parser.add_argument("--repetitions", type=int, default=4)
    parser.add_argument("--expected-chunks", type=int, default=67)
    parser.add_argument("--baseline-sha")
    parser.add_argument("--candidate-sha")
    parser.add_argument("--json-output", type=Path)
    parser.add_argument("--report-output", type=Path)
    args = parser.parse_args()

    if args.repetitions < 3:
        parser.error("--repetitions must be at least 3 to reduce shared-runner noise.")
    if args.expected_chunks < 1:
        parser.error("--expected-chunks must be positive.")

    try:
        baseline = load_repetitions(args.baseline_dir, args.repetitions)
        candidate = load_repetitions(args.candidate_dir, args.repetitions)
        result = compare(
            baseline,
            candidate,
            args.expected_chunks,
            args.baseline_sha,
            args.candidate_sha,
        )
    except ValueError as exc:
        print(f"A/B validation failed: {exc}", file=sys.stderr)
        return 1

    report = render_report(result)
    print(report)
    if args.json_output:
        args.json_output.parent.mkdir(parents=True, exist_ok=True)
        args.json_output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    if args.report_output:
        args.report_output.parent.mkdir(parents=True, exist_ok=True)
        args.report_output.write_text(report, encoding="utf-8")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
