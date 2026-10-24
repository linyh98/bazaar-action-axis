#!/usr/bin/env bash
set -euo pipefail

CSC="${CSC:-C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe}"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"

if [ ! -f "$CSC" ]; then
  echo "csc.exe not found: $CSC" >&2
  exit 1
fi

mkdir -p "$HERE/build"
OUT="$(cygpath -w "$HERE/build/EngineTests.exe")"
SOURCES=()
for file in "$HERE"/engine/*.cs "$HERE"/tests/*.cs; do
  SOURCES+=("$(cygpath -w "$file")")
done
SOURCES+=("$(cygpath -w "$HERE/plugin/LiveSkillReader.cs")")
SOURCES+=("$(cygpath -w "$HERE/plugin/LiveCardDetailReader.cs")")
SOURCES+=("$(cygpath -w "$HERE/plugin/NativeCombatReplayReader.cs")")
SOURCES+=("$(cygpath -w "$HERE/plugin/DockButtonVisibility.cs")")

"$CSC" -nologo -target:exe -out:"$OUT" \
  -r:System.dll -r:System.Core.dll \
  "${SOURCES[@]}"

cd "$ROOT"
"$HERE/build/EngineTests.exe"
