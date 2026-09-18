using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// ▼【新規追加】MultiplayerLocalTestHarnessをGameManagerへワンクリックで追加するためのメニュー。
// シーンのYAMLを直接いじらず、エディタの通常のコンポーネント追加操作と同じ経路で安全に行う。
public static class MultiplayerLocalTestSetup
{
    [MenuItem("POSE SWORD/Add Multiplayer Local Test Harness")]
    public static void AddHarness()
    {
        var gameManager = GameObject.Find("GameManager");
        if (gameManager == null)
        {
            EditorUtility.DisplayDialog("Multiplayer Local Test Harness",
                "シーン内に \"GameManager\" という名前のオブジェクトが見つかりませんでした。", "OK");
            return;
        }
        if (gameManager.GetComponent<MultiplayerLocalTestHarness>() != null)
        {
            EditorUtility.DisplayDialog("Multiplayer Local Test Harness",
                "GameManagerには既にMultiplayerLocalTestHarnessが付いています。Playを押すだけで開始できます。", "OK");
            Selection.activeGameObject = gameManager;
            return;
        }
        Undo.AddComponent<MultiplayerLocalTestHarness>(gameManager);
        EditorUtility.SetDirty(gameManager);
        EditorSceneManager.MarkSceneDirty(gameManager.scene);
        Selection.activeGameObject = gameManager;
        EditorUtility.DisplayDialog("Multiplayer Local Test Harness",
            "GameManagerにMultiplayerLocalTestHarnessを追加しました。シーンを保存してPlayを押すと、ホスト側のマルチプレイ対戦が自動で始まります。", "OK");
    }
}
