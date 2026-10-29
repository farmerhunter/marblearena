#!/bin/bash
set -euo pipefail
project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unity_bin="${UNITY_BIN:-/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity}"
if [[ ! -x "$unity_bin" ]]; then
    printf 'Unity Editor executable missing: %s\nSet UNITY_BIN to your Unity 6000.6.7f1 executable.\n' "$unity_bin" >&2
    exit 1
fi
mkdir -p "$project_dir/test-output"
"$unity_bin" -batchmode -quit -projectPath "$project_dir" -executeMethod BuildGame.Mac -logFile "$project_dir/test-output/unity-build.log"
/usr/bin/codesign --verify --deep --strict "$project_dir/build/Marble Arena.app"
printf 'Built and verified: %s\n' "$project_dir/build/Marble Arena.app"
