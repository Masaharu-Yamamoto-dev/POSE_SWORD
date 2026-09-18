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

    // ▼【新規追加】MultiplayerManagerは普段 SceneController.Awake が実行時に足しているため、
    // Inspectorで設定した値がシーンに残らない。1vs3のHUDレイアウトを調整できるよう、
    // コンポーネントをシーンへ常設するためのメニュー。
    [MenuItem("POSE SWORD/Add Multiplayer Manager to Scene")]
    public static void AddManager()
    {
        var gameManager = GameObject.Find("GameManager");
        if (gameManager == null)
        {
            EditorUtility.DisplayDialog("Multiplayer Manager",
                "シーン内に \"GameManager\" という名前のオブジェクトが見つかりませんでした。", "OK");
            return;
        }
        if (gameManager.GetComponent<MultiplayerManager>() != null)
        {
            EditorUtility.DisplayDialog("Multiplayer Manager",
                "GameManagerには既にMultiplayerManagerが付いています。Inspectorの「1vs3 HUD」でレイアウトを調整できます。", "OK");
            Selection.activeGameObject = gameManager;
            return;
        }
        Undo.AddComponent<MultiplayerManager>(gameManager);
        EditorUtility.SetDirty(gameManager);
        EditorSceneManager.MarkSceneDirty(gameManager.scene);
        Selection.activeGameObject = gameManager;
        EditorUtility.DisplayDialog("Multiplayer Manager",
            "GameManagerにMultiplayerManagerを追加しました。シーンを保存すると、Inspectorの「1vs3 HUD」で調整した値が残るようになります。", "OK");
    }
}
