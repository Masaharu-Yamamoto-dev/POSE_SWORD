"""Compile project scripts against installed Unity assemblies without launching the Editor.

This checks C# types only; it does not simulate scenes or replace Unity PlayMode tests.
"""
import os
from pathlib import Path
import subprocess
import tempfile

root = Path(__file__).resolve().parent.parent
editor = Path(os.environ.get("UNITY_EDITOR_ROOT", "/Applications/Unity/Hub/Editor/6000.1.12f1/Unity.app/Contents"))
mono = editor / "MonoBleedingEdge"
refs = list((editor / "Managed/UnityEngine").glob("*.dll"))
refs += [root / "POSE_SWORD_Unity/Library/ScriptAssemblies" / name for name in ("UnityEngine.UI.dll", "Unity.TextMeshPro.dll")]
refs.append(mono / "lib/mono/4.5/Facades/netstandard.dll")
assets = root / "POSE_SWORD_Unity/Assets"
sources = [p for p in assets.rglob("*.cs") if "TextMesh Pro" not in p.parts]
refs.append(root / "POSE_SWORD_Unity/Library/ScriptAssemblies/UnityEngine.TestRunner.dll")
refs.append(next((root / "POSE_SWORD_Unity/Library/PackageCache").glob("com.unity.ext.nunit*/net40/unity-custom/nunit.framework.dll")))
with tempfile.TemporaryDirectory(prefix="pose-sword-compile-") as output:
    response = Path(output) / "compile.rsp"
    response.write_text("\n".join([
        "-target:library", "-langversion:latest", "-define:UNITY_EDITOR",
        f'-out:"{output}/Assembly-CSharp.dll"',
        *[f'-r:"{p}"' for p in refs], *[f'"{p}"' for p in sources],
    ]))
    result = subprocess.run([str(mono / "bin/mono"), str(mono / "lib/mono/4.5/mcs.exe"), "@" + str(response)], timeout=60)
    raise SystemExit(result.returncode)
