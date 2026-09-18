using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class MultiplayerBuild
{
    // 出力先のバージョン。ここを上げてビルドし、React 側 BattleArena.jsx の4つのURLを合わせる。
    // 上書きせず別フォルダに出すので、問題があれば参照を戻すだけで前のビルドに復帰できる。
    const string Version = "ver3.12";

    [MenuItem("POSE SWORD/Build four-player WebGL")]
    public static void BuildWebGL()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            throw new InvalidOperationException("Install WebGL Build Support for this Unity Editor first.");
        string project = Directory.GetParent(Application.dataPath).FullName;
        // 配信対象は public/POSE_SWORD_Unity/Builds/ のみ（POSE_SWORD_Unity/Builds/ は .gitignore 済み）。
        // フォルダ名がそのままファイル名になるので、verX.Y/Build/verX.Y.wasm という形になる。
        string output = Path.GetFullPath(Path.Combine(project, "../public/POSE_SWORD_Unity/Builds/" + Version));
        var oldCompression = PlayerSettings.WebGL.compressionFormat;
        bool oldBackground = PlayerSettings.runInBackground;
        try
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.runInBackground = true;
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" }, locationPathName = output,
                target = BuildTarget.WebGL, options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Multiplayer WebGL build failed: " + report.summary.result);
            Debug.Log("Four-player WebGL ready at " + output);
            Debug.Log("BattleArena.jsx の参照を /POSE_SWORD_Unity/Builds/" + Version +
                "/Build/" + Version + ".* に合わせてください。");
        }
        finally
        {
            PlayerSettings.WebGL.compressionFormat = oldCompression;
            PlayerSettings.runInBackground = oldBackground;
        }
    }
}
