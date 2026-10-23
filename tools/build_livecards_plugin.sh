#!/usr/bin/env bash
set -euo pipefail

GAME="${GAME_DIR:-D:/Program Files (x86)/Steam/steamapps/common/The Bazaar}"
CSC="${CSC:-C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe}"
HERE="$(cd "$(dirname "$0")" && pwd)"
PLUGIN_DIR="$HERE/livecards_plugin"

if [ ! -f "$CSC" ]; then
  echo "csc.exe not found: $CSC" >&2
  exit 1
fi
if [ ! -f "$GAME/BepInEx/core/BepInEx.dll" ]; then
  echo "BepInEx not found under: $GAME" >&2
  exit 1
fi

cd "$PLUGIN_DIR"
"$CSC" -nologo -target:library -out:LiveCardsDump.dll \
  -r:"$GAME/BepInEx/core/BepInEx.dll" \
  -r:"$GAME/TheBazaar_Data/Managed/UnityEngine.dll" \
  -r:"$GAME/TheBazaar_Data/Managed/UnityEngine.CoreModule.dll" \
  -r:"$GAME/TheBazaar_Data/Managed/netstandard.dll" \
  -r:System.dll -r:System.Core.dll \
  LiveCardsDump.cs

cp -f LiveCardsDump.dll "$GAME/BepInEx/plugins/LiveCardsDump.dll"
echo "Built: $PLUGIN_DIR/LiveCardsDump.dll"
echo "Deployed: $GAME/BepInEx/plugins/LiveCardsDump.dll"
echo "Restart The Bazaar to load the plugin. Output: D:\\bazaar-action-axis\\captures\\live_cards_bpp.json"
