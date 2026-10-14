import tempfile
import unittest
from pathlib import Path
from compare_logs import read_session, summarize


class ComparisonTests(unittest.TestCase):
    def test_comparison_uses_post_ready_fixed_window_and_reports_ratios(self):
        data = "\n".join([
            "[INFO] event=world.start seed=4 dimension=asteria:overworld",
            "[INFO] event=world.settings render_distance_chunks=4 max_materialization_workers=4",
            "[INFO] event=world.snapshot uptime_s=1.0 mesh_stale=999",
            "[INFO] event=world.loading.ready",
            "[INFO] event=world.snapshot uptime_s=3.0 frames=120 frame_ms_avg=16.0 mesh_stale=3 mesh_accepted=7 lighting_pending=4",
            "[INFO] event=world.snapshot uptime_s=5.0 frames=120 frame_ms_avg=20.0 mesh_stale=1 mesh_accepted=9 lighting_pending=20",
            "[INFO] event=world.snapshot uptime_s=7.0 frames=120 frame_ms_avg=20.0 mesh_stale=0 mesh_accepted=10 lighting_pending=9",
            "[INFO] event=world.snapshot uptime_s=9.0 frames=120 frame_ms_avg=200.0 mesh_stale=999",
        ])
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "run.log"
            path.write_text(data, encoding="utf-8")
            meta, rows = read_session(path, 4)
            self.assertEqual(meta["seed"], "4")
            self.assertEqual(len(rows), 3)
            values = summarize(rows)
            self.assertEqual(values["Peak lighting queue"], 20)
            self.assertAlmostEqual(values["Terrain stale (%)"], 100 * 4 / 30)
            self.assertIsNone(values["Terrain GPU patches"])
            self.assertIsNone(values["Seeds per lighting worker"])
            self.assertIsNone(values["Terrain salvaged meshlets"])
            self.assertIsNone(values["Fluid salvaged meshlets"])
            self.assertAlmostEqual(values["Avg frame (ms)"], 56 / 3)

    def test_requires_full_duration(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "short.log"
            path.write_text("[INFO] event=world.loading.ready\n[INFO] event=world.snapshot uptime_s=2.0\n")
            with self.assertRaisesRegex(ValueError, "fewer than"):
                read_session(path, 60)


if __name__ == "__main__":
    unittest.main()
