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
TEST_EXE="${ACTIONAXIS_TEST_EXE:-$HERE/build/EngineTests.exe}"
OUT="$(cygpath -w "$TEST_EXE")"
SOURCES=()
for file in "$HERE"/engine/*.cs "$HERE"/tests/*.cs; do
  SOURCES+=("$(cygpath -w "$file")")
done
SOURCES+=("$(cygpath -w "$HERE/plugin/LiveSkillReader.cs")")
SOURCES+=("$(cygpath -w "$HERE/plugin/LiveMonsterReader.cs")")
SOURCES+=("$(cygpath -w "$HERE/plugin/LiveCardDetailReader.cs")")
SOURCES+=("$(cygpath -w "$HERE/plugin/NativeCombatReplayReader.cs")")
SOURCES+=("$(cygpath -w "$HERE/plugin/ValidationReplayCapture.cs")")
SOURCES+=("$(cygpath -w "$HERE/plugin/ValidationSnapshot.cs")")
SOURCES+=("$(cygpath -w "$HERE/plugin/ValidationReplayWriter.cs")")
SOURCES+=("$(cygpath -w "$HERE/plugin/DockButtonVisibility.cs")")

"$CSC" -nologo -target:exe -debug -out:"$OUT" \
  -r:System.dll -r:System.Core.dll \
  "${SOURCES[@]}"

cd "$ROOT"
"$TEST_EXE"
