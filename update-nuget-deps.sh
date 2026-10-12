#!/usr/bin/env bash
# Regenerates the NuGet dependency lockfiles for the Nix flake.
#
# The flake builds two frontends (GUI + CLI), each with its own lockfile:
#   nuget-deps-gui.json  -> MiBandHR.Avalonia
#   nuget-deps-cli.json  -> MiBandHR.Cli
#
# Each package's dependencies are restored separately via the `fetch-deps`
# passthru provided by buildDotnetModule.
#
# Usage: ./update-nuget-deps.sh
set -euo pipefail

cd "$(dirname "$0")"

fetch_deps() {
    local pkg="$1"
    local out="$2"

    echo "Fetching $pkg dependencies -> $out"
    local script
    script=$(nix build --no-link --print-out-paths ".#$pkg.passthru.fetch-deps")
    "$script" "$out"
}

fetch_deps miband-heartrate-gui nuget-deps-gui.json
fetch_deps miband-heartrate-cli nuget-deps-cli.json

echo "Done."
