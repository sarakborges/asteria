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

    # Completion coverage is intentionally excluded. A deadline-driven
    # scheduler may materialize a different subset of otherwise valid chunks
    # before the route advances, while the requested route remains identical.
    fields = (
        "routeSteps",
        "movementChunks",
        "framesPerChunk",
        "horizontalRadius",
        "retentionRadius",
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
        "gravityWakeupsWithAirBelowBeforeProcessing",
    ):
        value = float(_at(route, field))
        if not math.isfinite(value) or value < 0:
            raise ValueError(
                f"Streaming route metric {field} must be finite and non-negative."
            )
    return identity


def _streaming_gravity_wakeup_samples(run: dict[str, Any]) -> list[dict[str, Any]]:
    route = run.get("streamingRouteDiagnostics")
    if route is None:
        return []
    samples = _at(route, "gravityWakeupSamples")
    if not isinstance(samples, list):
        raise ValueError("Streaming route gravityWakeupSamples must be an array.")
    if any(not isinstance(sample, dict) for sample in samples):
        raise ValueError("Gravity wakeup samples must be objects.")
    return samples


def _streaming_physics_signature(run: dict[str, Any]) -> tuple[Any, ...] | None:
    route = run.get("streamingRouteDiagnostics")
    if route is None:
        return None

    def optional_int(source: dict[str, Any], field: str) -> int | None:
        value = _at(source, field)
        return None if value is None else int(value)

    falling_samples = _at(route, "fallingBlockSamples")
    if not isinstance(falling_samples, list):
        raise ValueError("Streaming route fallingBlockSamples must be an array.")
    normalized_falling = []
    for sample in falling_samples:
        if not isinstance(sample, dict):
            raise ValueError("Falling block samples must be objects.")
        normalized_falling.append((
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

    wakeup_samples = _at(route, "gravityWakeupSamples")
    if not isinstance(wakeup_samples, list):
        raise ValueError("Streaming route gravityWakeupSamples must be an array.")
    normalized_wakeups = []
    for sample in wakeup_samples:
        if not isinstance(sample, dict):
            raise ValueError("Gravity wakeup samples must be objects.")
        # Physics timing and queue depth are observations, not deterministic
        # world outcomes. Compare only the generated material and coordinates;
        # retain the timing fields in the first-run report for diagnosis.
        normalized_wakeups.append((
            str(_at(sample, "block")),
            int(_at(sample, "x")),
            int(_at(sample, "y")),
            int(_at(sample, "z")),
            int(_at(sample, "sourceChunkX")),
            int(_at(sample, "sourceChunkY")),
            int(_at(sample, "sourceChunkZ")),
            int(_at(sample, "belowChunkX")),
            int(_at(sample, "belowChunkY")),
            int(_at(sample, "belowChunkZ")),
            str(_at(sample, "belowBlock")),
        ))

    wakeup_count = int(_at(
        route, "gravityWakeupsWithAirBelowBeforeProcessing"))
    if wakeup_count < len(normalized_wakeups):
        raise ValueError(
            "Gravity wakeup sample count exceeds observed air-below wakeups."
        )

    return (
        int(_at(route, "totalFallingBlocksStarted")),
        int(_at(route, "totalUnsupportedBlocksRemoved")),
        int(_at(route, "observedFallingEntityCount")),
        tuple(normalized_falling),
        wakeup_count,
        tuple(normalized_wakeups),
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


def _streaming_route_block_diagnostics(
    run: dict[str, Any],
) -> dict[tuple[int, int, int], dict[str, Any]] | None:
    route = run.get("streamingRouteDiagnostics")
    if route is None:
        return None
    entries = _at(route, "activatedChunkDigests")
    if not isinstance(entries, list):
        raise ValueError("Streaming route activatedChunkDigests must be an array.")

    diagnostics: dict[tuple[int, int, int], dict[str, Any]] = {}
    for entry in entries:
        if not isinstance(entry, dict):
            raise ValueError("Streaming route chunk diagnostic entries must be objects.")
        coord = (int(_at(entry, "x")), int(_at(entry, "y")), int(_at(entry, "z")))

        def digest_array(name: str, expected_length: int) -> tuple[int, ...]:
            values = entry.get(name)
            if values is None:
                return ()
            if not isinstance(values, list) or len(values) != expected_length:
                raise ValueError(
                    f"Streaming route {name} for chunk {coord} must contain {expected_length} values."
                )
            parsed = tuple(int(value) for value in values)
            if any(value < 0 for value in parsed):
                raise ValueError(f"Streaming route {name} for chunk {coord} cannot be negative.")
            return parsed

        def count_map(name: str) -> tuple[tuple[str, int], ...]:
            values = entry.get(name)
            if values is None:
                return ()
            if not isinstance(values, dict):
                raise ValueError(f"Streaming route {name} for chunk {coord} must be an object.")
            parsed = tuple(sorted((str(key), int(value)) for key, value in values.items()))
            if any(value < 0 for _, value in parsed):
                raise ValueError(f"Streaming route {name} for chunk {coord} cannot be negative.")
            return parsed

        raw_voxels = entry.get("blockVoxelIndicesById")
        if raw_voxels is None:
            voxel_map = ()
        else:
            if not isinstance(raw_voxels, dict):
                raise ValueError(
                    f"Streaming route blockVoxelIndicesById for chunk {coord} must be an object."
                )
            voxel_entries = []
            seen_indices = set()
            for block_id, indices in sorted(raw_voxels.items()):
                if not isinstance(indices, list):
                    raise ValueError(
                        f"Streaming route voxel indices for {block_id} in chunk {coord} must be an array."
                    )
                parsed_indices = tuple(int(value) for value in indices)
                if tuple(sorted(parsed_indices)) != parsed_indices:
                    raise ValueError(
                        f"Streaming route voxel indices for {block_id} in chunk {coord} must be sorted."
                    )
                for voxel_index in parsed_indices:
                    if voxel_index < 0 or voxel_index >= 4096:
                        raise ValueError(
                            f"Streaming route voxel index {voxel_index} for chunk {coord} is outside the chunk."
                        )
                    if voxel_index in seen_indices:
                        raise ValueError(
                            f"Streaming route chunk {coord} assigns voxel index {voxel_index} to multiple blocks."
                        )
                    seen_indices.add(voxel_index)
                    voxel_entries.append((voxel_index, str(block_id)))
            voxel_map = tuple(sorted(voxel_entries))

        diagnostics[coord] = {
            "blockLayerDigests": digest_array("blockLayerDigests", 16),
            "blockColumnDigests": digest_array("blockColumnDigests", 256),
            "blockCounts": count_map("blockCounts"),
            "gravityBlockCountsByLayer": digest_array("gravityBlockCountsByLayer", 16),
            "gravityBlockCounts": count_map("gravityBlockCounts"),
            "blockVoxelMap": voxel_map,
        }
    return diagnostics


def _block_change_diagnostics(
    changed_coords: list[tuple[int, int, int]],
    baseline: dict[tuple[int, int, int], dict[str, Any]],
    candidate: dict[tuple[int, int, int], dict[str, Any]],
) -> list[dict[str, Any]]:
    result = []
    for coord in changed_coords:
        before = baseline.get(coord, {})
        after = candidate.get(coord, {})
        before_layers = before.get("blockLayerDigests", ())
        after_layers = after.get("blockLayerDigests", ())
        before_columns = before.get("blockColumnDigests", ())
        after_columns = after.get("blockColumnDigests", ())
        changed_layers = (
            [index for index, (left, right) in enumerate(zip(before_layers, after_layers)) if left != right]
            if before_layers and after_layers else []
        )
        changed_columns = (
            [index for index, (left, right) in enumerate(zip(before_columns, after_columns)) if left != right]
            if before_columns and after_columns else []
        )
        before_counts = dict(before.get("blockCounts", ()))
        after_counts = dict(after.get("blockCounts", ()))
        block_count_changes = [
            {
                "block": block,
                "baseline": before_counts.get(block, 0),
                "candidate": after_counts.get(block, 0),
                "delta": after_counts.get(block, 0) - before_counts.get(block, 0),
            }
            for block in sorted(set(before_counts) | set(after_counts))
            if before_counts.get(block, 0) != after_counts.get(block, 0)
        ]
        before_gravity_layers = before.get("gravityBlockCountsByLayer", ())
        after_gravity_layers = after.get("gravityBlockCountsByLayer", ())
        gravity_layer_changes = [
            {
                "localY": index,
                "baseline": left,
                "candidate": right,
                "delta": right - left,
            }
            for index, (left, right) in enumerate(zip(before_gravity_layers, after_gravity_layers))
            if left != right
        ]
        before_gravity = dict(before.get("gravityBlockCounts", ()))
        after_gravity = dict(after.get("gravityBlockCounts", ()))
        gravity_type_changes = [
            {
                "block": block,
                "baseline": before_gravity.get(block, 0),
                "candidate": after_gravity.get(block, 0),
                "delta": after_gravity.get(block, 0) - before_gravity.get(block, 0),
            }
            for block in sorted(set(before_gravity) | set(after_gravity))
            if before_gravity.get(block, 0) != after_gravity.get(block, 0)
        ]
        before_voxels = dict(before.get("blockVoxelMap", ()))
        after_voxels = dict(after.get("blockVoxelMap", ()))
        changed_voxel_indices = [
            index for index in sorted(set(before_voxels) | set(after_voxels))
            if before_voxels.get(index) != after_voxels.get(index)
        ]
        transitions: dict[tuple[str, str], int] = {}
        voxel_samples = []
        for index in changed_voxel_indices:
            before_block = before_voxels.get(index, "air")
            after_block = after_voxels.get(index, "air")
            transition = (before_block, after_block)
            transitions[transition] = transitions.get(transition, 0) + 1
            if len(voxel_samples) < 48:
                local_y = index // 256
                in_layer = index % 256
                local_z = in_layer // 16
                local_x = in_layer % 16
                voxel_samples.append({
                    "x": local_x,
                    "y": local_y,
                    "z": local_z,
                    "baselineBlock": before_block,
                    "candidateBlock": after_block,
                })

        transition_rows = [
            {
                "baselineBlock": before_block,
                "candidateBlock": after_block,
                "voxelCount": count,
            }
            for (before_block, after_block), count in sorted(transitions.items())
        ]
        if not (
            changed_layers or changed_columns or block_count_changes
            or gravity_layer_changes or gravity_type_changes
            or changed_voxel_indices
        ):
            continue

        result.append({
            "chunk": list(coord),
            "changedLocalYLayers": changed_layers,
            "changedLocalColumnCount": len(changed_columns),
            "changedLocalColumnSamples": [
                [index % 16, index // 16] for index in changed_columns[:16]
            ],
            "blockCountChanges": block_count_changes[:16],
            "gravityBlockCountByLayerChanges": gravity_layer_changes[:16],
            "gravityBlockTypeCountChanges": gravity_type_changes[:16],
            "baselineGravityBlockCount": sum(before_gravity.values()),
            "candidateGravityBlockCount": sum(after_gravity.values()),
            "changedVoxelCount": len(changed_voxel_indices),
            "blockTransitions": transition_rows[:24],
            "changedVoxelSamples": voxel_samples,
        })
    return result


def _intersect_deterministic_profiles(
    previous: dict[tuple[int, int, int], Any],
    current: dict[tuple[int, int, int], Any],
    directory: Path,
    repetition: int,
    profile_name: str,
) -> dict[tuple[int, int, int], Any]:
    shared_coordinates = previous.keys() & current.keys()
    minimum_profile_size = min(len(previous), len(current))
    overlap_ratio = (
        len(shared_coordinates) / minimum_profile_size
        if minimum_profile_size else 0.0
    )
    if overlap_ratio < 0.8:
        raise ValueError(
            f"{directory}/{repetition}.json shares only {len(shared_coordinates)} "
            f"of {minimum_profile_size} {profile_name} coordinates with the "
            "previous repetition; at least 80% overlap is required."
        )

    changed = [
        coordinate for coordinate in shared_coordinates
        if previous[coordinate] != current[coordinate]
    ]
    if changed:
        raise ValueError(
            f"{directory}/{repetition}.json generated different per-coordinate "
            f"{profile_name} for shared chunks: {changed[:8]}."
        )

    # Only shared coordinates can establish deterministic world output.
    # Chunks absent from a run were not necessarily requested long enough to
    # finish before movement; absence is scheduler coverage, not a content hash.
    return {
        coordinate: previous[coordinate]
        for coordinate in shared_coordinates
    }


def load_repetitions(directory: Path, repetitions: int) -> list[dict[str, Any]]:
    runs = [_load_json(directory / f"{index}.json") for index in range(1, repetitions + 1)]
    identity = None
    expected_digests = None
    expected_stage_names = None
    expected_streaming_identity = None
    expected_streaming_chunks = None
    expected_streaming_block_diagnostics = None
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
        streaming_block_diagnostics = _streaming_route_block_diagnostics(run)
        physics_signature = _streaming_physics_signature(run)
        streaming_presence = streaming_identity is not None
        if expected_streaming_presence is None:
            expected_streaming_presence = streaming_presence
            expected_streaming_identity = streaming_identity
            expected_streaming_chunks = streaming_chunks
            expected_streaming_block_diagnostics = streaming_block_diagnostics
            expected_physics_signature = physics_signature
        elif streaming_presence != expected_streaming_presence:
            raise ValueError(
                f"{directory} repetitions must all include the same streaming route profile."
            )
        elif streaming_identity != expected_streaming_identity:
            raise ValueError(
                f"{directory} repetitions do not use the same streaming route configuration."
            )
        elif streaming_chunks is not None and expected_streaming_chunks is not None:
            expected_streaming_chunks = _intersect_deterministic_profiles(
                expected_streaming_chunks,
                streaming_chunks,
                directory,
                index,
                "streaming chunk digest",
            )
            if (
                streaming_block_diagnostics is not None and
                expected_streaming_block_diagnostics is not None
            ):
                expected_streaming_block_diagnostics = _intersect_deterministic_profiles(
                    expected_streaming_block_diagnostics,
                    streaming_block_diagnostics,
                    directory,
                    index,
                    "streaming block diagnostics",
                )
            if physics_signature != expected_physics_signature:
                raise ValueError(
                    f"{directory} repetitions observed different falling-block physics outcomes."
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
        baseline_block_diagnostics = (
            _streaming_route_block_diagnostics(baseline_first) or {}
        )
        candidate_block_diagnostics = (
            _streaming_route_block_diagnostics(candidate_first) or {}
        )
        block_change_diagnostics = _block_change_diagnostics(
            changed_block_route_coords,
            baseline_block_diagnostics,
            candidate_block_diagnostics,
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
            ("Gravity wakeups with air below before processing", "gravityWakeupsWithAirBelowBeforeProcessing"),
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
        baseline_wakeup_samples = _streaming_gravity_wakeup_samples(baseline_first)
        candidate_wakeup_samples = _streaming_gravity_wakeup_samples(candidate_first)
        streaming_route_result = {
            "gravityWakeupDiagnostics": {
                "baselineWakeupsWithAirBelow": baseline_physics[4],
                "candidateWakeupsWithAirBelow": candidate_physics[4],
                # Keep scheduler timing from the first repetition for diagnosis;
                # repetition validation compares only deterministic world outcomes.
                "baselineSamples": baseline_wakeup_samples,
                "candidateSamples": candidate_wakeup_samples,
            },
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
            "baselineRouteDigest": _at(
                baseline_first, "streamingRouteDiagnostics", "routeDigest"
            ),
            "candidateRouteDigest": _at(
                candidate_first, "streamingRouteDiagnostics", "routeDigest"
            ),
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
                "blockChangeDiagnostics": block_change_diagnostics[:12],
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
        wakeups = streaming_route["gravityWakeupDiagnostics"]
        if wakeups["baselineWakeupsWithAirBelow"] != wakeups["candidateWakeupsWithAirBelow"]:
            lines.append(
                f"- Gravity wakeups with air below before processing: baseline "
                f"{wakeups['baselineWakeupsWithAirBelow']}; candidate "
                f"{wakeups['candidateWakeupsWithAirBelow']}"
            )
        if wakeups["baselineSamples"] or wakeups["candidateSamples"]:
            lines.extend([
                f"- Baseline pre-wakeup gravity samples: {wakeups['baselineSamples']}",
                f"- Candidate pre-wakeup gravity samples: {wakeups['candidateSamples']}",
            ])
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
            block_details = differences.get("blockChangeDiagnostics", [])
            if block_details:
                lines.append("- Block-level divergence by changed chunk (local coordinates):")
                for detail in block_details:
                    columns = detail["changedLocalColumnSamples"]
                    layers = detail["changedLocalYLayers"]
                    block_changes = ", ".join(
                        f"{item['block']} {item['baseline']}→{item['candidate']}"
                        for item in detail["blockCountChanges"]
                    ) or "no block-count delta"
                    lines.append(
                        f"  - {detail['chunk']}: {detail['changedVoxelCount']} changed voxels across "
                        f"{detail['changedLocalColumnCount']} X/Z columns; local Y layers {layers}; "
                        f"gravity blocks {detail['baselineGravityBlockCount']}→"
                        f"{detail['candidateGravityBlockCount']}; {block_changes}"
                    )
                    for transition in detail["blockTransitions"]:
                        lines.append(
                            f"    - {transition['baselineBlock']} → "
                            f"{transition['candidateBlock']}: {transition['voxelCount']} voxels"
                        )
                    if detail["changedVoxelSamples"]:
                        lines.append(
                            f"    - Local voxel samples (x,y,z): {detail['changedVoxelSamples'][:16]}"
                        )
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
