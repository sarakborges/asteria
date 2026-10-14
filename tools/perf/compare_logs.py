#!/usr/bin/env python3
"""Compare two Asteria world-session diagnostic logs using equal sample windows."""
import argparse
import re
from pathlib import Path

FIELD = re.compile(r"(?:^|\s)([A-Za-z][A-Za-z_0-9]*)=([^\s]+)")


def fields(line):
    return dict(FIELD.findall(line))


def read_session(path, seconds):
    metadata = {}
    snapshots = []
    ready = False
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        if "event=" not in line:
            continue
        data = fields(line)
        event = data.get("event")
        if event == "build.identity":
            metadata["build"] = data
        elif event == "world.start":
            if "seed" in metadata:
                break  # One world per comparison; never mix dimension sessions.
            metadata["seed"] = data.get("seed")
            metadata["dimension"] = data.get("dimension")
        elif event == "world.settings":
            metadata["render_distance_chunks"] = data.get("render_distance_chunks")
            metadata["max_materialization_workers"] = data.get("max_materialization_workers")
        elif event == "world.loading.ready":
            ready = True
        elif event == "world.snapshot" and ready:
            snapshots.append(data)
        elif event in ("world.leave.complete", "world.save.begin", "dimension.transition.begin"):
            if ready:
                break
    if not snapshots:
        raise ValueError(f"{path}: no post-ready world snapshots")
    origin = float(snapshots[0]["uptime_s"])
    selected = [r for r in snapshots if float(r["uptime_s"]) - origin <= seconds]
    if len(selected) < 2 or float(selected[-1]["uptime_s"]) - origin < seconds - 3:
        raise ValueError(f"{path}: fewer than {seconds}s of post-ready snapshots")
    return metadata, selected


def total(rows, key):
    return sum(float(row.get(key, 0)) for row in rows)


def peak(rows, key):
    return max(float(row.get(key, 0)) for row in rows)


def summarize(rows):
    accepted = total(rows, "mesh_accepted")
    stale = total(rows, "mesh_stale")
    fluid_accepted = total(rows, "fluid_mesh_accepted")
    fluid_stale = total(rows, "fluid_mesh_stale")
    frames = total(rows, "frames")
    result = {
        "Avg frame (ms)": sum(float(r.get("frame_ms_avg", 0)) * float(r.get("frames", 0)) for r in rows) / max(frames, 1),
        "Worst frame (ms)": peak(rows, "frame_ms_max"),
        "Frames >50ms": total(rows, "frames_over_50ms"),
        "Frames >100ms": total(rows, "frames_over_100ms"),
        "Peak lighting queue": peak(rows, "lighting_pending"),
        "Peak terrain queue": peak(rows, "terrain_mesh_pending"),
        "Peak fluid queue": peak(rows, "fluid_mesh_pending"),
        "Terrain stale (%)": 100 * stale / max(stale + accepted, 1),
        "Fluid stale (%)": 100 * fluid_stale / max(fluid_stale + fluid_accepted, 1),
        "Wasted lighting worker (ms)": total(rows, "lighting_discarded_worker_ms_total"),
        "Terrain relight-only": total(rows, "mesh_light_only"),
        "Fluid relight-only": total(rows, "fluid_light_only"),
    }
    if any("lighting_seed_count" in r for r in rows):
        batches = total(rows, "lighting_batches") + total(rows, "lighting_stale")
        result["Seeds per lighting worker"] = (
            (total(rows, "lighting_seed_count") +
             total(rows, "lighting_stale_seed_count")) / max(batches, 1))
        result["Peak seeds per worker"] = peak(rows, "lighting_seed_batch_max")
    else:
        result["Seeds per lighting worker"] = None
        result["Peak seeds per worker"] = None
    for label, key in (
        ("Terrain salvaged meshlets", "mesh_salvaged_from_stale_batch"),
        ("Fluid salvaged meshlets", "fluid_mesh_salvaged_from_stale_batch"),
    ):
        result[label] = total(rows, key) if any(key in r for r in rows) else None
    for label, key in (("Terrain GPU patches", "mesh_gpu_attribute_patches"),
                       ("Fluid GPU patches", "fluid_gpu_attribute_patches")):
        result[label] = total(rows, key) if any(key in r for r in rows) else None
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--seconds", type=int, default=60)
    args = parser.parse_args()
    if args.seconds <= 0:
        parser.error("--seconds must be positive")
    try:
        old_meta, old_rows = read_session(args.baseline, args.seconds)
        new_meta, new_rows = read_session(args.candidate, args.seconds)
    except (OSError, ValueError) as exc:
        parser.error(str(exc))
    for key in ("seed", "dimension", "render_distance_chunks", "max_materialization_workers"):
        if not old_meta.get(key) or old_meta[key] != new_meta.get(key):
            parser.error(f"incompatible benchmark {key}: {old_meta.get(key)} != {new_meta.get(key)}")
    old_build, new_build = old_meta.get("build", {}), new_meta.get("build", {})
    print(f"Seed: {old_meta['seed']} | Sphere: {old_meta['dimension']} | Window: {args.seconds}s")
    print(f"Baseline build: {old_build.get('commit', 'unrecorded')} / {old_build.get('mvid', 'unrecorded')}")
    print(f"Candidate build: {new_build.get('commit', 'unrecorded')} / {new_build.get('mvid', 'unrecorded')}")
    if old_build and new_build and old_build.get("mvid") == new_build.get("mvid"):
        print("WARNING: identical assembly MVID; this may be the same compiled build.")
    print(f"{'Metric':<31} {'Before':>12} {'After':>12} {'Delta':>12}")
    before, after = summarize(old_rows), summarize(new_rows)
    for label, a in before.items():
        b = after[label]
        if a is None or b is None:
            print(f"{label:<31} {'n/a' if a is None else f'{a:.1f}':>12} {'n/a' if b is None else f'{b:.1f}':>12} {'n/a':>12}")
        else:
            print(f"{label:<31} {a:>12.1f} {b:>12.1f} {b-a:>+12.1f}")
    print("Interpretation requires the same movement path, machine and graphics settings.")


if __name__ == "__main__":
    main()
