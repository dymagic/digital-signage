# Spike harnesses (not shipped)

Throwaway measurement pages for investigations, served by the `testmedia` nginx in `docker-compose.e2e.yml`.

- `video-lifecycle/`: WP-07, how to release covered `<video>` elements. Results and decision: `docs/adr/0008-video-lifecycle.md`.
  Run: `SIGNAGE_E2E_SPIKE=1 scripts/test-e2e.sh -trait "Category=Spike"` (≈15 min, four strategies × 240 transitions).
  The long runs (release × video/image mix, videos only, images only, 2000 transitions each):
  `SIGNAGE_E2E_SPIKE=1 SIGNAGE_E2E_SPIKE_LONG=1 …` (≈1 h). Tracing is off while measuring. Output: `artifacts/e2e/spike/`.
