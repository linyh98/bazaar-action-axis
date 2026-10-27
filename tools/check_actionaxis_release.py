#!/usr/bin/env python3
"""Read-only G12 release consistency check for the Action Axis DLLs.

It never deploys, stops the game, or writes to the game directory.  It compares
the build output hashes with the installed plugin hashes, and reports the
engine MVID and plugin version loaded in the latest matching BepInEx log lines.
Runtime comparisons are null when evidence is unavailable; UNKNOWN is not a
verified running release. Exit status remains successful for matching disk
files with no known runtime mismatch, so offline disk checks remain usable.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import re
import subprocess
import sys


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def module_version_id(path):
    absolute = str(pathlib.Path(path).resolve())
    command = (
        "$a=[System.Reflection.Assembly]::LoadFile('%s'); "
        "$a.ManifestModule.ModuleVersionId.ToString()" % absolute.replace("'", "''")
    )
    try:
        result = subprocess.run(["powershell", "-NoProfile", "-Command", command],
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30)
        if result.returncode != 0:
            return None
        return result.stdout.decode("utf-8", errors="replace").strip()
    except (OSError, subprocess.SubprocessError):
        return None


def plugin_version(path, cecil_path):
    """Read BepInPlugin.Version as metadata without loading the game plugin."""
    if not path.exists() or not cecil_path.exists():
        return None
    absolute = str(path.resolve()).replace("'", "''")
    cecil_absolute = str(cecil_path.resolve()).replace("'", "''")
    command = (
        "$ErrorActionPreference='Stop'; "
        "[void][System.Reflection.Assembly]::LoadFile('%s'); "
        "$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('%s'); "
        "try { $version=$null; foreach ($type in $assembly.MainModule.Types) { "
        "foreach ($attribute in $type.CustomAttributes) { "
        "if ($attribute.AttributeType.FullName -eq 'BepInEx.BepInPlugin' -and "
        "$attribute.ConstructorArguments.Count -ge 3 -and "
        "[string]$attribute.ConstructorArguments[0].Value -eq 'com.bazaarsim.actionaxis') { "
        "$version=[string]$attribute.ConstructorArguments[2].Value } } }; "
        "if ($version) { [Console]::Out.Write($version) } else { exit 1 } "
        "} finally { $assembly.Dispose() }" % (cecil_absolute, absolute)
    )
    try:
        result = subprocess.run(["powershell", "-NoProfile", "-Command", command],
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30)
        if result.returncode != 0:
            return None
        return result.stdout.decode("utf-8", errors="replace").strip() or None
    except (OSError, subprocess.SubprocessError):
        return None


def loaded_engine(log_path):
    if not log_path.exists():
        return None
    pattern = re.compile(r"\[ActionAxis\] loaded; engine=([0-9a-fA-F-]+)")
    value = None
    for line in log_path.read_text(encoding="utf-8", errors="replace").splitlines():
        match = pattern.search(line)
        if match:
            value = match.group(1).lower()
    return value


def loaded_plugin_version(log_path):
    if not log_path.exists():
        return None
    pattern = re.compile(r"Loading\s*\[Bazaar Action Axis\s+([^\]\s]+)\]")
    value = None
    for line in log_path.read_text(encoding="utf-8", errors="replace").splitlines():
        match = pattern.search(line)
        if match:
            value = match.group(1)
    return value


def matches_known_values(loaded, installed):
    if loaded is None or installed is None:
        return None
    return loaded.lower() == installed.lower()


def main(argv=None):
    parser = argparse.ArgumentParser(description="Read-only Action Axis release consistency check")
    parser.add_argument("--game-dir", required=True)
    parser.add_argument("--build-dir", default="tools/actionaxis_plugin/build")
    parser.add_argument("--json")
    args = parser.parse_args(argv)

    game = pathlib.Path(args.game_dir)
    build = pathlib.Path(args.build_dir)
    cecil_path = game / "BepInEx" / "core" / "Mono.Cecil.dll"
    names = ("ActionAxisEngine.dll", "ActionAxisPlugin.dll")
    files = {}
    same = {}
    for name in names:
        source = build / name
        target = game / "BepInEx" / "plugins" / name
        files[name] = {
            "source": {"path": str(source), "sha256": sha256(source) if source.exists() else None,
                       "mvid": module_version_id(source) if source.exists() else None},
            "target": {"path": str(target), "sha256": sha256(target) if target.exists() else None,
                       "mvid": module_version_id(target) if target.exists() else None},
        }
        same[name] = (files[name]["source"]["sha256"] is not None and
                      files[name]["source"]["sha256"] == files[name]["target"]["sha256"])
    for location in ("source", "target"):
        record = files["ActionAxisPlugin.dll"][location]
        record["pluginVersion"] = plugin_version(pathlib.Path(record["path"]), cecil_path)
    log_path = game / "BepInEx" / "LogOutput.log"
    loaded = loaded_engine(log_path)
    loaded_plugin = loaded_plugin_version(log_path)
    engine_target_mvid = files["ActionAxisEngine.dll"]["target"]["mvid"]
    engine_matches = matches_known_values(loaded, engine_target_mvid)
    plugin_matches = matches_known_values(loaded_plugin, files["ActionAxisPlugin.dll"]["target"]["pluginVersion"])
    disk_verdict = "MATCH" if all(same.values()) else "MISMATCH"
    runtime_checks = (engine_matches, plugin_matches)
    runtime_verdict = ("MISMATCH" if False in runtime_checks else
                       "MATCH" if all(value is True for value in runtime_checks) else "UNKNOWN")
    verdict = ("MISMATCH" if "MISMATCH" in (disk_verdict, runtime_verdict) else
               "MATCH" if runtime_verdict == "MATCH" else "UNKNOWN")
    report = {
        "files": files,
        "sameBuildAndInstalled": same,
        "log": {"path": str(log_path), "loadedEngineMvid": loaded,
                "loadedPluginVersion": loaded_plugin},
        "loadedEngineMatchesInstalled": engine_matches,
        "loadedPluginMatchesInstalled": plugin_matches,
        "diskVerdict": disk_verdict,
        "runtimeVerdict": runtime_verdict,
        "deployment": "NOT_EXECUTED_READ_ONLY_CHECK",
        "verdict": verdict,
    }
    if args.json:
        target = pathlib.Path(args.json)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(report, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=1))
    return 1 if verdict == "MISMATCH" else 0


if __name__ == "__main__":
    raise SystemExit(main())
