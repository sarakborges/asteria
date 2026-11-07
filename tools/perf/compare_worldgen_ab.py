#!/usr/bin/env python3
"""Compare repeated, same-runner WorldgenBenchmark A/B results."""
from __future__ import annotations

import argparse
import json
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


def load_repetitions(directory: Path, repetitions: int) -> list[dict[str, Any]]:
    runs = [_load_json(directory / f"{index}.json") for index in range(1, repetitions + 1)]
    identity = None
    expected_digests = None

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
        digests = {
            name: _at(metrics, name, "digest")
            for name in sorted(metrics)
        }

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
    if any(value < 0 for value in values):
        raise ValueError(f"Benchmark metric {'.'.join(keys)} cannot be negative.")
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

    definitions = [
        (
            "DirectionalFacePalettes (warm)",
            _directional_face_palette_elapsed,
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

    return {
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
            "DirectionalFacePalettes timing includes both exposure derivation and palette application stages.",
            "Release A/B runs set DOTNET_TieredCompilation=1 and DOTNET_TC_QuickJit=0 for both revisions, matching the game's runtime configuration.",
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
    lines.extend(["", *[f"- {note}" for note in result["notes"]], ""])
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
