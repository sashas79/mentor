#!/usr/bin/env bash
# Delivery team only. Packs the Engineering Harness distribution that the repository expects beside itself:
#
#   <workspace>/harness-core              the core binary (macOS arm64)
#   <workspace>/_root/releases/v0.3.0     the release data the core resolves by itself
#   <workspace>/mentor                    this repository
#
# Run from anywhere:  bash docs/handover/prepare-bundle.sh
# Produces <workspace>/mentor-harness-v0.3.0-darwin-arm64.tar.gz and its .sha256 beside the repository.

set -euo pipefail

repo=$(cd "$(dirname "$0")/../.." && pwd)
ws=$(cd "$repo/.." && pwd)
out="$ws/mentor-harness-v0.3.0-darwin-arm64.tar.gz"

[ -x "$ws/harness-core" ] || { echo "harness-core not found beside the repository at $ws"; exit 1; }
[ -f "$ws/_root/releases/v0.3.0/VERSION" ] || { echo "_root/releases/v0.3.0 not found beside the repository at $ws"; exit 1; }

printf 'harness-core sha256: '; shasum -a 256 "$ws/harness-core" | cut -d' ' -f1
tar --exclude '.DS_Store' -czf "$out" -C "$ws" harness-core _root/releases/v0.3.0
(cd "$ws" && shasum -a 256 "$(basename "$out")" > "$out.sha256")

echo "Bundle: $out"
cat "$out.sha256"
