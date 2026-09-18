using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// ▼【新規追加】残機モードの「あと何本あるか」アイコン(ReserveSwordIcon0/1)を、
// PL1Bar〜PL4Barの子として実体としてシーンに配置するためのメニュー。
// これを一度実行すれば、あとはHierarchyから普通のUI要素として位置・サイズをエディタ上で自由に調整できる。
// (MultiplayerManager.CreateReserveSwordIconsは名前で既存のものを見つけて再利用するだけになり、
// 見つからない場合だけ同じ既定値でフォールバック生成する)
public static class ReserveSwordIconSetup
{
    const int MaxReserveIcons = 2; // MultiplayerManager.MaxReserveIconsと合わせる

    [MenuItem("POSE SWORD/Setup Reserve Sword Icons")]
    public static void SetupIcons()
    {
        int created = 0, skipped = 0, missing = 0;
        for (int slot = 1; slot <= 4; slot++)
        {
            string barName = "PL" + slot + "Bar";
            var bar = FindInScene(barName);
            if (bar == null) { missing++; continue; }

            bool touched = false;
            for (int i = 0; i < MaxReserveIcons; i++)
            {
                string iconName = "ReserveSwordIcon" + i;
                if (bar.transform.Find(iconName) != null) { skipped++; continue; }

                var iconObj = new GameObject(iconName, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(iconObj, "Add Reserve Sword Icon");
                iconObj.transform.SetParent(bar.transform, false);
                var rt = iconObj.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1, 0);
                rt.anchorMax = new Vector2(1, 0);
                rt.pivot = new Vector2(1, 0);
                rt.sizeDelta = new Vector2(22, 22);
                // 右下隅を基準に、0番目(次に使う剣)を一番右、以降は左に並べる(仮の初期値、後で自由に調整してよい)
                rt.anchoredPosition = new Vector2(-4 - i * 26, 4);
                var img = iconObj.GetComponent<Image>();
                img.preserveAspect = true;
                iconObj.SetActive(false);
                created++;
                touched = true;
            }
            if (touched) EditorUtility.SetDirty(bar);
        }

        if (created > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("Reserve Sword Icons",
            $"作成: {created}件 / 既存のためスキップ: {skipped}件 / PLxBarが見つからず対象外: {missing}件\n\n" +
            "各PLxBarの子にできた ReserveSwordIcon0 / ReserveSwordIcon1 をHierarchyから選んで、" +
            "位置・サイズ・Imageの見た目などを自由に調整してください。\n" +
            "(名前だけは変えないでください。実行時にこの名前で見つけて画像を差し替えます)",
            "OK");
    }

    static GameObject FindInScene(string name)
    {
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            var found = FindRecursive(root.transform, name);
            if (found != null) return found.gameObject;
        }
        return null;
    }

    static Transform FindRecursive(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            var found = FindRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
