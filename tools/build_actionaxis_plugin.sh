#!/usr/bin/env bash
set -euo pipefail

CSC="${CSC:-C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe}"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
SRC="$HERE/actionaxis_plugin"
BUILD="$SRC/build"
GAME="${GAME_DIR:-D:/Program Files (x86)/Steam/steamapps/common/The Bazaar}"
MODE="${1:-deploy}"

if [ ! -f "$CSC" ]; then
  echo "csc.exe not found: $CSC" >&2
  exit 1
fi

win() { cygpath -w "$1"; }
mkdir -p "$BUILD"

compile_engine() {
  local sources=()
  for file in "$SRC"/engine/*.cs; do sources+=("$(win "$file")"); done
  "$CSC" -nologo -target:library -out:"$(win "$BUILD/ActionAxisEngine.dll")" \
    -r:System.dll -r:System.Core.dll \
    "${sources[@]}"
}

compile_plugin() {
  local sources=()
  for file in "$SRC"/plugin/*.cs "$SRC"/plugin/Ui/*.cs; do sources+=("$(win "$file")"); done
  "$CSC" -nologo -target:library -out:"$(win "$BUILD/ActionAxisPlugin.dll")" \
    -r:System.dll -r:System.Core.dll \
    -r:"$(win "$BUILD/ActionAxisEngine.dll")" \
    -r:"$(win "$GAME/BepInEx/core/BepInEx.dll")" \
    -r:"$(win "$GAME/BepInEx/core/0Harmony.dll")" \
    -r:"$(win "$GAME/TheBazaar_Data/Managed/UnityEngine.dll")" \
    -r:"$(win "$GAME/TheBazaar_Data/Managed/UnityEngine.CoreModule.dll")" \
    -r:"$(win "$GAME/TheBazaar_Data/Managed/UnityEngine.IMGUIModule.dll")" \
    -r:"$(win "$GAME/TheBazaar_Data/Managed/UnityEngine.TextRenderingModule.dll")" \
    -r:"$(win "$GAME/TheBazaar_Data/Managed/UnityEngine.ImageConversionModule.dll")" \
    -r:"$(win "$GAME/TheBazaar_Data/Managed/UnityEngine.InputLegacyModule.dll")" \
    -r:"$(win "$GAME/TheBazaar_Data/Managed/netstandard.dll")" \
    "${sources[@]}"
}

compile_tests() {
  bash "$SRC/compile_tests.sh"
}

deploy() {
  if [ ! -f "$GAME/BepInEx/core/BepInEx.dll" ]; then
    echo "BepInEx not found under: $GAME" >&2
    exit 1
  fi
  mkdir -p "$GAME/BepInEx/plugins/BazaarActionAxis/icons"
  cp -f "$BUILD/ActionAxisEngine.dll" "$BUILD/ActionAxisPlugin.dll" "$GAME/BepInEx/plugins/"
  cp -f "$ROOT/src/data/cards.json" "$GAME/BepInEx/plugins/BazaarActionAxis/cards.json"
  if [ -f "$ROOT/data/skills.json" ]; then
    cp -f "$ROOT/data/skills.json" "$GAME/BepInEx/plugins/BazaarActionAxis/skills.json"
  fi
  local icons=()
  local file
  for file in "$ROOT"/captures/card_icons/*.png; do
    if [ -f "$file" ]; then icons+=("$file"); fi
  done
  if [ "${#icons[@]}" -gt 0 ]; then
    # Preserve artwork captured in game or supplied manually on earlier runs.
    cp -n "${icons[@]}" "$GAME/BepInEx/plugins/BazaarActionAxis/icons/"
  else
    echo "卡图缺失时显示名称；打开行动轴会尝试从游戏补全并缓存，已有卡图保留。"
  fi
  echo "Deployed: $GAME/BepInEx/plugins/ActionAxisPlugin.dll"
  echo "Data:     $GAME/BepInEx/plugins/BazaarActionAxis/"
}

case "$MODE" in
  --test|test)
    compile_tests
    ;;
  --compile-only|compile)
    compile_engine
    compile_plugin
    echo "Compiled: $BUILD/ActionAxisPlugin.dll"
    ;;
  --deploy|deploy|"")
    compile_engine
    compile_plugin
    deploy
    ;;
  *)
    echo "usage: $0 [--test|--compile-only|--deploy]" >&2
    exit 2
    ;;
esac
