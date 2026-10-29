using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// ▼【新規追加】必殺技ボタン(SpecialAttackButtonPL1)に、SkillIconsの層ごとの画像を重ねたアイコンを
// 実体としてシーンに配置するためのメニュー。一度実行すれば、あとはHierarchyから普通のUI要素として
// ボタンごと位置・大きさを調整できる。何度実行しても、既にあるものは作り直さず画像の割り当てだけやり直す。
// (SkillIconViewが付いていないボタンは、従来どおり「撃てる時だけ出る赤いボタン」のまま動く)
public static class SkillIconSetup
{
    const string ButtonName = "SpecialAttackButtonPL1";
    const string RootName = "SkillIcon";
    // SwordBattle.SkillIconKeyが返す名前と、render_skill_icons.pyが書き出すファイル名の頭を合わせる
    static readonly string[] SkillKeys = { "spin", "giant", "attack", "shield", "tornado", "grasp" };

    [MenuItem("POSE SWORD/Setup Skill Icon Button")]
    public static void Setup()
    {
        ReimportIcons();

        var buttonObj = FindInScene(ButtonName);
        if (buttonObj == null)
        {
            EditorUtility.DisplayDialog("Skill Icon", ButtonName + " がシーンに見つかりません。SampleSceneを開いてから実行してください。", "OK");
            return;
        }
        var button = buttonObj.GetComponent<Button>();

        // 層を下から順に作る。名前で探して、既にあれば再利用する
        var root = FindOrCreateChild(buttonObj.transform, RootName);
        var glow = FindOrCreateImage(root, "Glow");
        var track = FindOrCreateImage(root, "RingTrack");
        var ring = FindOrCreateImage(root, "Ring");
        var overRing = FindOrCreateImage(root, "OverRing");
        var body = FindOrCreateImage(root, "Body");
        var burst = FindOrCreateImage(root, "Burst");
        // 後から足した層(OverRing・Burst)は末尾に付くので、重ね順をここで揃え直す
        var order = new Image[] { glow, track, ring, overRing, body, burst };
        for (int i = 0; i < order.Length; i++)
        {
            if (order[i].transform.GetSiblingIndex() == i) continue;
            Undo.SetTransformParent(order[i].transform, root, "Setup Skill Icon");
            order[i].transform.SetSiblingIndex(i);
        }

        Undo.RecordObjects(new Object[] { ring, overRing, track }, "Setup Skill Icon");
        track.sprite = LoadSprite("ring_track");
        SetupRadial(ring);
        // 2周目(1vs3のボスが通常必殺技のラインを越えた分)。実行中はSkillIconViewが表示を切り替える
        SetupRadial(overRing);
        overRing.sprite = LoadSprite("grasp_ring");
        overRing.fillAmount = 0f;
        overRing.enabled = false;
        // 撃てるようになった瞬間に外へ広がる輪。実行中はSkillIconViewが画像と大きさを動かす
        Undo.RecordObject(burst, "Setup Skill Icon");
        burst.sprite = LoadSprite("spin_ring");
        burst.enabled = false;

        var view = buttonObj.GetComponent<SkillIconView>();
        if (view == null) view = Undo.AddComponent<SkillIconView>(buttonObj);
        Undo.RecordObject(view, "Setup Skill Icon");
        view.glowImage = glow;
        view.trackImage = track;
        view.ringImage = ring;
        view.overRingImage = overRing;
        view.bodyImage = body;
        view.burstImage = burst;
        view.scaleRoot = (RectTransform)root;
        view.skills = new SkillIconView.SkillSprites[SkillKeys.Length];
        int missing = 0;
        for (int i = 0; i < SkillKeys.Length; i++)
        {
            string key = SkillKeys[i];
            var s = new SkillIconView.SkillSprites
            {
                key = key,
                ring = LoadSprite(key + "_ring"),
                body = LoadSprite(key + "_body"),
                bodyCharge = LoadSprite(key + "_body_charge"),
                glow = LoadSprite(key + "_glow"),
            };
            if (s.ring == null || s.body == null || s.bodyCharge == null || s.glow == null) missing++;
            view.skills[i] = s;
        }

        // エディタ上で見た目を確かめられるよう、溜まりかけの大回転斬を入れておく(実行中はSwordBattleが上書きする)
        Undo.RecordObjects(new Object[] { glow, body }, "Setup Skill Icon");
        ring.sprite = view.skills[0].ring;
        ring.fillAmount = 0.6f;
        body.sprite = view.skills[0].bodyCharge;
        glow.sprite = view.skills[0].glow;
        glow.enabled = false;

        // 元の赤い四角と「必殺技」の文字は隠す。四角はタップの当たり判定に使うので、消さずに透明にする
        var plate = buttonObj.GetComponent<Image>();
        if (plate != null)
        {
            Undo.RecordObject(plate, "Setup Skill Icon");
            var c = plate.color; c.a = 0f; plate.color = c;
        }
        var label = buttonObj.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (label != null)
        {
            Undo.RecordObject(label.gameObject, "Setup Skill Icon");
            label.gameObject.SetActive(false);
        }
        // 溜めている間はボタンを押せなくする(interactable=false)。その間も色を変えないよう、
        // 押した時だけアイコン本体が暗くなる設定にしておく
        if (button != null)
        {
            Undo.RecordObject(button, "Setup Skill Icon");
            button.targetGraphic = body;
            var colors = button.colors;
            colors.disabledColor = Color.white;
            button.colors = colors;
        }

        EditorUtility.SetDirty(view);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Skill Icon",
            "必殺技ボタンにアイコンを配置しました。\n\n" +
            (missing > 0 ? $"⚠ {missing}種類の技で画像が見つかりませんでした。Assets/UI/SkillIcons を確認してください。\n\n" : "") +
            "大きさや位置は SpecialAttackButtonPL1 の RectTransform で調整してください。\n" +
            "(SkillIcon と、その中の Glow / RingTrack / Ring / OverRing / Body / Burst の名前は変えないでください)\n\n" +
            "最後にシーンを保存(Ctrl/Cmd+S)するのを忘れずに。",
            "OK");
    }

    // 取り込み設定(SkillIconImportSettings)を、すでに取り込み済みの画像にも反映させる
    static void ReimportIcons()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SkillIconImportSettings.Folder.TrimEnd('/') }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
    }

    static Sprite LoadSprite(string file)
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>(SkillIconImportSettings.Folder + file + ".png");
    }

    static Transform FindOrCreateChild(Transform parent, string name)
    {
        var found = parent.Find(name);
        if (found != null) return found;
        var obj = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(obj, "Setup Skill Icon");
        obj.layer = parent.gameObject.layer;
        obj.transform.SetParent(parent, false);
        Stretch((RectTransform)obj.transform);
        return obj.transform;
    }

    static Image FindOrCreateImage(Transform parent, string name)
    {
        var t = FindOrCreateChild(parent, name);
        var img = t.GetComponent<Image>();
        if (img == null) img = Undo.AddComponent<Image>(t.gameObject);
        // 全層同じ大きさ・同じ縦横比の画像なので、preserveAspectで重ねれば位置が揃う
        img.preserveAspect = true;
        // タップは親のボタン(透明にした四角)で受ける
        img.raycastTarget = false;
        return img;
    }

    static void SetupRadial(Image img)
    {
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = (int)Image.Origin360.Top;
        img.fillClockwise = true;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
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
