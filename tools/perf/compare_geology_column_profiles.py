#!/usr/bin/env python3
"""Compare repeated world-generation runs using exact and interpolated geology columns."""
from __future__ import annotations

import argparse
import json
import statistics
import sys
from pathlib import Path
from typing import Any


def load_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"Could not read benchmark result {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise ValueError(f"Benchmark result {path} must contain a JSON object.")
    return value


def at(data: dict[str, Any], *keys: str) -> Any:
    current: Any = data
    for key in keys:
        if not isinstance(current, dict) or key not in current:
            raise ValueError(f"Benchmark result is missing {'.'.join(keys)}.")
        current = current[key]
    return current


def identity(run: dict[str, Any]) -> tuple[Any, ...]:
    diagnostics = at(run, "initialAreaDiagnostics")
    return (
        at(run, "seed"),
        at(run, "dimension"),
        at(run, "dimensionSeed"),
        tuple(at(run, "center")),
        at(diagnostics, "spawnX"),
        at(diagnostics, "spawnY"),
        at(diagnostics, "spawnZ"),
        at(diagnostics, "centerChunkX"),
        at(diagnostics, "centerChunkY"),
        at(diagnostics, "centerChunkZ"),
        at(diagnostics, "desiredChunkCount"),
    )


def load_repetitions(
    directory: Path,
    repetitions: int,
    expected_stride: int,
) -> list[dict[str, Any]]:
    runs = [
        load_json(directory / f"{index}.json")
        for index in range(1, repetitions + 1)
    ]
    expected_identity: tuple[Any, ...] | None = None
    expected_digests: dict[str, Any] | None = None

    for index, run in enumerate(runs, start=1):
        stride = at(run, "geologyColumnSampleStride")
        if stride != expected_stride:
            raise ValueError(
                f"{directory}/{index}.json uses stride {stride}, "
                f"expected {expected_stride}."
            )
        run_identity = identity(run)
        metrics = at(run, "metrics")
        if not isinstance(metrics, dict) or not metrics:
            raise ValueError(f"{directory}/{index}.json contains no metrics.")
        digests = {
            name: at(metrics, name, "digest")
            for name in sorted(metrics)
        }

        if expected_identity is None:
            expected_identity = run_identity
            expected_digests = digests
        elif run_identity != expected_identity:
            raise ValueError(
                f"{directory} repetitions do not use the same seed and region."
            )
        elif digests != expected_digests:
            raise ValueError(
                f"{directory} repetitions produced different world-generation digests."
            )

        at(run, "metrics", "chunkSynthesis", "warmMs")
        at(run, "metrics", "initialAreaSynthesis", "warmMs")
        at(run, "initialAreaMaterializationDiagnostics", "warm", "totalMaterializeMs")
        at(run, "initialAreaStageTimingDiagnostics", "warm", "byStage", "TerrainLayer", "elapsedMs")

    return runs


def median(runs: list[dict[str, Any]], *keys: str) -> float:
    values = [float(at(run, *keys)) for run in runs]
    if any(value < 0 for value in values):
        raise ValueError(f"Metric {'.'.join(keys)} cannot be negative.")
    return statistics.median(values)


def compare(
    exact: list[dict[str, Any]],
    profile: list[dict[str, Any]],
    expected_chunks: int,
    exact_stride: int,
    profile_stride: int,
) -> dict[str, Any]:
    if len(exact) != len(profile):
        raise ValueError("Both versions must have the same repetition count.")
    if identity(exact[0]) != identity(profile[0]):
        raise ValueError("The two versions do not represent the same initial region.")
    if at(exact[0], "initialAreaDiagnostics", "desiredChunkCount") != expected_chunks:
        raise ValueError(
            f"Expected {expected_chunks} initial-area chunks, got "
            f"{at(exact[0], 'initialAreaDiagnostics', 'desiredChunkCount')}."
        )

    exact_digests = {
        name: at(at(exact[0], "metrics"), name, "digest")
        for name in sorted(at(exact[0], "metrics"))
    }
    profile_digests = {
        name: at(at(profile[0], "metrics"), name, "digest")
        for name in sorted(at(profile[0], "metrics"))
    }

    definitions = [
        ("One chunk, warm", ("metrics", "chunkSynthesis", "warmMs")),
        ("67-chunk area, warm", ("metrics", "initialAreaSynthesis", "warmMs")),
        (
            "Area materialization stages, warm",
            ("initialAreaMaterializationDiagnostics", "warm", "totalMaterializeMs"),
        ),
        (
            "TerrainLayer stage, warm",
            ("initialAreaStageTimingDiagnostics", "warm", "byStage", "TerrainLayer", "elapsedMs"),
        ),
    ]
    metrics = []
    for label, path in definitions:
        before = median(exact, *path)
        after = median(profile, *path)
        change = 0.0 if before == 0 else (after - before) / before * 100.0
        metrics.append({
            "metric": label,
            "exactMedianMs": before,
            "profileMedianMs": after,
            "changePercent": change,
        })

    exact_allocations = median(
        exact, "metrics", "initialAreaSynthesis", "warmAllocatedBytes"
    )
    profile_allocations = median(
        profile, "metrics", "initialAreaSynthesis", "warmAllocatedBytes"
    )
    allocation_change = (
        0.0 if exact_allocations == 0
        else (profile_allocations - exact_allocations) / exact_allocations * 100.0
    )

    return {
        "seed": at(exact[0], "seed"),
        "dimension": at(exact[0], "dimension"),
        "center": list(at(exact[0], "center")),
        "chunkCount": expected_chunks,
        "repetitions": len(exact),
        "exactStride": exact_stride,
        "profileStride": profile_stride,
        "digestsStableWithinEachVersion": True,
        "digestsMatchAcrossVersions": exact_digests == profile_digests,
        "exactDigests": exact_digests,
        "profileDigests": profile_digests,
        "metrics": metrics,
        "initialAreaWarmAllocation": {
            "exactBytes": exact_allocations,
            "profileBytes": profile_allocations,
            "changePercent": allocation_change,
        },
        "notes": [
            "Each value is the median across independent Release processes; each process also reports its internal warm median.",
            "The interpolated score profile is an approximation, so digests are expected to differ from stride 1; digests must remain stable across repetitions of the same stride.",
            "The exact stride-1 path retains per-voxel geology sampling.",
        ],
    }


def render_report(result: dict[str, Any]) -> str:
    lines = [
        "# Geological column profile A/B",
        "",
        f"- Seed: `{result['seed']}` ({result['dimension']})",
        f"- Initial region: `{result['center']}`",
        f"- Chunks: **{result['chunkCount']}**",
        f"- Independent processes per stride: **{result['repetitions']}**",
        f"- Strides: **{result['exactStride']}** (exact) vs **{result['profileStride']}** (interpolated profile)",
        "- Repeated-run digests: **stable within each stride**",
        f"- Digests equal across modes: **{result['digestsMatchAcrossVersions']}** (different output is expected for interpolation)",
        "",
        "| Métrica | Exato (ms) | Perfil (ms) | Variação |",
        "|---|---:|---:|---:|",
    ]
    for metric in result["metrics"]:
        lines.append(
            f"| {metric['metric']} | {metric['exactMedianMs']:.2f} "
            f"| {metric['profileMedianMs']:.2f} "
            f"| {metric['changePercent']:+.1f}% |"
        )
    allocation = result["initialAreaWarmAllocation"]
    lines.extend([
        "",
        "## Alocação da área inicial (warm)",
        "",
        f"- Exato: {allocation['exactBytes'] / (1024 * 1024):.2f} MiB",
        f"- Perfil: {allocation['profileBytes'] / (1024 * 1024):.2f} MiB",
        f"- Variação: {allocation['changePercent']:+.1f}%",
        "",
        *[f"- {note}" for note in result["notes"]],
        "",
    ])
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exact-dir", required=True, type=Path)
    parser.add_argument("--profile-dir", required=True, type=Path)
    parser.add_argument("--repetitions", type=int, default=4)
    parser.add_argument("--expected-chunks", type=int, default=67)
    parser.add_argument("--exact-stride", type=int, default=1)
    parser.add_argument("--profile-stride", type=int, default=4)
    parser.add_argument("--json-output", type=Path)
    parser.add_argument("--report-output", type=Path)
    args = parser.parse_args()

    if args.repetitions < 3:
        parser.error("--repetitions must be at least 3.")
    if args.expected_chunks < 1 or args.exact_stride < 1 or args.profile_stride < 2:
        parser.error("Chunk count and strides must be positive; profile stride must exceed one.")

    try:
        exact = load_repetitions(args.exact_dir, args.repetitions, args.exact_stride)
        profile = load_repetitions(args.profile_dir, args.repetitions, args.profile_stride)
        result = compare(
            exact,
            profile,
            args.expected_chunks,
            args.exact_stride,
            args.profile_stride,
        )
    except ValueError as exc:
        print(f"Geology column profile A/B failed: {exc}", file=sys.stderr)
        return 1

    report = render_report(result)
    print(report)
    if args.json_output:
        args.json_output.parent.mkdir(parents=True, exist_ok=True)
        args.json_output.write_text(
            json.dumps(result, indent=2) + "\n", encoding="utf-8"
        )
    if args.report_output:
        args.report_output.parent.mkdir(parents=True, exist_ok=True)
        args.report_output.write_text(report, encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
