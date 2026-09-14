#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
editor_root="${UNITY_EDITOR_ROOT:-/Applications/Unity/Hub/Editor/6000.1.12f1/Unity.app/Contents}"
mono_root="$editor_root/MonoBleedingEdge"
nunit_dll="${NUNIT_DLL:-}"
if [[ -z "$nunit_dll" ]]; then
  for candidate in "$project_root"/POSE_SWORD_Unity/Library/PackageCache/com.unity.ext.nunit*/net40/unity-custom/nunit.framework.dll; do
    if [[ -f "$candidate" ]]; then nunit_dll="$candidate"; break; fi
  done
fi
if [[ ! -x "$mono_root/bin/mono" || ! -f "$nunit_dll" ]]; then
  echo 'Set UNITY_EDITOR_ROOT and NUNIT_DLL to an installed Unity Editor and nunit.framework.dll.' >&2
  exit 1
fi

test_output="$(mktemp -d "${TMPDIR:-/tmp}/pose-sword-rules.XXXXXX")"
# Keep artifacts in the temporary directory, never in Assets or Library.
cp "$nunit_dll" "$test_output/nunit.framework.dll"
"$mono_root/bin/mono" "$mono_root/lib/mono/4.5/mcs.exe" -langversion:latest \
  -r:"$test_output/nunit.framework.dll" -out:"$test_output/MatchRulesTests.exe" \
  "$project_root"/POSE_SWORD_Unity/Assets/Multiplayer/Core/*.cs \
  "$project_root/POSE_SWORD_Unity/Assets/Tests/EditMode/MatchRulesTests.cs" \
  "$project_root/tests/MatchRulesRunner.cs"
"$mono_root/bin/mono" "$test_output/MatchRulesTests.exe"
