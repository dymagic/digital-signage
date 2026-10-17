#!/usr/bin/env bash
# Generates short test videos and images into artifacts/media (git-ignored) with ffmpeg in a container.
# Nothing is downloaded and no media is committed. Re-run with --force to regenerate.
#   scripts/generate-test-media.sh [--force]
set -euo pipefail
cd "$(dirname "$0")/.."

FFMPEG_IMAGE="linuxserver/ffmpeg:9.0.2"
OUT="artifacts/media"
if [[ -f "$OUT/.complete" && "${1:-}" != "--force" ]]; then
  exit 0
fi
mkdir -p "$OUT"
rm -f "$OUT/.complete"

ff() { docker run --rm -u "$(id -u):$(id -g)" -v "$PWD/$OUT:/out" "$FFMPEG_IMAGE" -hide_banner -loglevel error -y "$@"; }

# Clips: 1920x1080, 30 fps, H.264 + AAC in MP4 (what real deployments use; Playwright's Chromium plays it),
# distinct test patterns so screenshots show which clip plays.
clip() { # name seconds source
  ff -f lavfi -i "$3=duration=$2:size=1920x1080:rate=30" -f lavfi -i "sine=frequency=440:duration=$2" \
     -c:v libx264 -profile:v high -pix_fmt yuv420p -c:a aac -shortest -movflags +faststart "/out/$1-h264.mp4"
}
clip clip-a 3 testsrc2
clip clip-b 3 smptehdbars
clip clip-c 4 rgbtestsrc

# Images: full-HD stills.
for pattern in testsrc2 smptebars mandelbrot; do
  ff -f lavfi -i "$pattern=size=1920x1080" -frames:v 1 "/out/image-$pattern.png"
done

touch "$OUT/.complete"
ls -l "$OUT"
