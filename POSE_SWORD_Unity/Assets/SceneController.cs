using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

// Webから送られてくる合体JSONのデータ構造
// ▼【N人対応】hostSword/clientSwordの固定2枠から、可変長(2〜4)のplayers配列に変更
[System.Serializable]
public class BattleStartData
{
    public SwordData[] players;
}

public class SceneController : MonoBehaviour
{
    void Awake()
    {
        // The serialized two-player scene remains the source of the sword template.
        if (GetComponent<MultiplayerManager>() == null) gameObject.AddComponent<MultiplayerManager>();

        // ▼ p3HudTemplate/p4HudTemplateはEditorで位置確認するための実物のUIなので、
        // Playが始まった瞬間はいったん必ず隠す（実際に3・4人目が参加する試合の時だけStartBattle側で表示する）
        SetHudTemplateVisible(p3HudTemplate, false);
        SetHudTemplateVisible(p4HudTemplate, false);
    }

    [Header("剣の錬成装置（インデックス=プレイヤーID、最大4枠）")]
    public SwordGenerator[] generators = new SwordGenerator[NetworkManager.MaxPlayers];

    [Header("剣の錬成装置（マルチプレイヤーのテンプレート元）")]
    public SwordGenerator hostGenerator;
    public SwordGenerator clientGenerator;

    [Header("開始位置（剣モード・2人用）")]
    public Vector3 leftPosition = new Vector3(-5f, 0f, 0f);
    public Vector3 rightPosition = new Vector3(5f, 0f, 0f);

    // ▼【新規追加】独楽モード用の開始位置
    [Header("開始位置（独楽モード・2人用）")]
    public Vector3 komaLeftPosition = new Vector3(-4f, 3f, 0f); // 例: 剣モードより少し上で、少し近い
    public Vector3 komaRightPosition = new Vector3(4f, 3f, 0f);

    [Header("開始位置（剣モード・3〜4人用）")]
    public Vector3[] sword3PPositions = new Vector3[] {
        new Vector3(-6f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(6f, 0f, 0f)
    };
    public Vector3[] sword4PPositions = new Vector3[] {
        new Vector3(-7.5f, 0f, 0f), new Vector3(-2.5f, 0f, 0f), new Vector3(2.5f, 0f, 0f), new Vector3(7.5f, 0f, 0f)
    };

    [Header("開始位置（独楽モード・3〜4人用）")]
    public Vector3[] koma3PPositions = new Vector3[] {
        new Vector3(-5f, 3f, 0f), new Vector3(0f, 3f, 0f), new Vector3(5f, 3f, 0f)
    };
    public Vector3[] koma4PPositions = new Vector3[] {
        new Vector3(-6f, 3f, 0f), new Vector3(-2f, 3f, 0f), new Vector3(2f, 3f, 0f), new Vector3(6f, 3f, 0f)
    };

    [Header("開始位置（3〜4人用・任意）")]
    [Tooltip("対応する要素にTransformを割り当てると、そのシーン上の位置(そのTransform自身の座標)を上の配列の数値より優先して使う。" +
        "空のGameObjectをシーンに置いてドラッグするだけで、そのプレイヤー(3人目・4人目など)のスポーン位置をSceneビュー上で視覚的に決められる。" +
        "要素が未設定(null)の場合は従来通り上の配列の数値がそのまま使われる。")]
    public Transform[] sword3PSpawnPoints = new Transform[3];
    public Transform[] sword4PSpawnPoints = new Transform[4];
    public Transform[] koma3PSpawnPoints = new Transform[3];
    public Transform[] koma4PSpawnPoints = new Transform[4];

    [Header("カウントダウンTMP用UI")]
    public TMPro.TextMeshProUGUI countdownText; 

    // ▼【新規追加】インスペクタからカメラの引き具合をいつでも調整できる枠！
    [Header("演出設定")]
    [Tooltip("数値が大きいほどカメラが引きます（前回の 3f より、5f や 6f くらいにするとちょうどいい引きになります）")]
    public float countdownZoomSize = 5.5f; 

    [Header("【デバッグ】テスト用合体JSONファイル")]
    public TextAsset debugBattleJsonFile;
    [Header("【デバッグ】ゲーム起動時に自動でテスト開始するフラグ")]
    public bool autoTestOnStart = true;

    // ▼【N人対応】3・4人目は常設せず、StartBattleのたびに複製して作る(再戦時に前回分を破棄する)
    [Header("3〜4人目のHPバーを配置するY方向のずらし幅（下のテンプレートを割り当てた場合は使われない）")]
    public float extraHudOffsetY = -45f;

    [Header("3・4人目のHPバー配置（任意）")]
    [Tooltip("ここにHudTemplateを割り当てると、そのRectTransformの位置をそのまま3人目のHPバー位置として使う。" +
        "未設定の場合は従来通り1人目のHPバー + 上のYオフセットで自動配置される。")]
    public HudTemplate p3HudTemplate;
    [Tooltip("ここにHudTemplateを割り当てると、そのRectTransformの位置をそのまま4人目のHPバー位置として使う。" +
        "未設定の場合は従来通り2人目のHPバー + 上のYオフセットで自動配置される。")]
    public HudTemplate p4HudTemplate;

    private readonly List<GameObject> dynamicSwords = new List<GameObject>();
    private readonly List<GameObject> dynamicHudPieces = new List<GameObject>();

    void Start()
    {
        if (autoTestOnStart && debugBattleJsonFile != null && !string.IsNullOrEmpty(debugBattleJsonFile.text))
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.SetPlayerInfoDirect(0, 2, true);
            StartBattle(debugBattleJsonFile.text);
        }
    }

    void Update()
    {
        // 起動後も「T」キーを押せば、インスペクタの数値を反映して何度でもカウントダウンからやり直せます
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (debugBattleJsonFile != null && !string.IsNullOrEmpty(debugBattleJsonFile.text))
            {
                Debug.Log("🔄 【デバッグ】Tキーが押されたため、バトルを再初期化します");
                StartBattle(debugBattleJsonFile.text);
            }
        }
    }

    public void StartBattle(string jsonString)
    {
        Debug.Log("🚩 StartBattle が呼ばれた");
        Debug.Log("⚔️ Webからバトル開始データを受信しました！");

        BattleStartData data = JsonUtility.FromJson<BattleStartData>(jsonString);
        int playerCount = data.players != null ? data.players.Length : 0;
        playerCount = Mathf.Clamp(playerCount, 2, NetworkManager.MaxPlayers);

        Vector3[] positions = GetSpawnPositions(playerCount);

        // ▼ 前回のStartBattle()で3・4人目用に動的生成したもの(剣・HPバー)を破棄してから作り直す
        foreach (var obj in dynamicSwords) if (obj != null) Destroy(obj);
        dynamicSwords.Clear();
        foreach (var obj in dynamicHudPieces) if (obj != null) Destroy(obj);
        dynamicHudPieces.Clear();

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.playerCount = playerCount;

            for (int i = 0; i < 2; i++)
            {
                // ▼ 1・2人目は常設のPlayerSword/EnemyDummyと、そのInspector配線済みHPバーをそのまま使う(前と同じ挙動)
                GameObject swordObj = NetworkManager.Instance.playerSwords[i];
                bool active = i < playerCount;
                if (swordObj == null) continue;

                swordObj.SetActive(active);
                if (!active) continue;

                swordObj.transform.position = positions[i];

                // ▼ P1赤/P2青/P3黄/P4緑の色分け(枠・必殺技オーラ・必殺技演出の背景バーなど)に使うプレイヤー番号を、
                // Inspectorでの手動設定に頼らずスロット順に確実にセットする
                SwordBattle slotBattle = swordObj.GetComponent<SwordBattle>();
                if (slotBattle != null) slotBattle.playerNumber = i + 1;

                if (generators != null && i < generators.Length && generators[i] != null && data.players != null && i < data.players.Length)
                {
                    generators[i].GenerateSwordFromJson(JsonUtility.ToJson(data.players[i]));
                }
            }

            for (int i = 2; i < NetworkManager.MaxPlayers; i++)
            {
                // ▼ 3・4人目はその都度、1・2人目の剣とHPバーを複製して作る(常設オブジェクト不要)
                bool active = i < playerCount;
                if (!active)
                {
                    NetworkManager.Instance.playerSwords[i] = null;
                    // ▼ この人数の試合では使わない3・4人目のHPバー(PL3Bar/PL4Barなど)は非表示にする
                    SetHudTemplateVisible(i == 2 ? p3HudTemplate : i == 3 ? p4HudTemplate : null, false);
                    continue;
                }

                GameObject clone = CreateDynamicPlayerSword(i, positions[i], data.players != null && i < data.players.Length ? data.players[i] : null);
                if (clone != null)
                {
                    NetworkManager.Instance.playerSwords[i] = clone;
                    dynamicSwords.Add(clone);
                }
            }

            NetworkManager.Instance.ApplyModeSettings();

            // ▼【N人対応】カメラに生存者全員のTransformを渡す(自動追尾はSwordController側が毎フレーム行う)
            BattleCamera cameraCtrl = Camera.main != null ? Camera.main.GetComponent<BattleCamera>() : null;
            if (cameraCtrl != null)
            {
                Transform[] combatants = new Transform[NetworkManager.MaxPlayers];
                for (int i = 0; i < NetworkManager.MaxPlayers; i++)
                {
                    GameObject swordObj = NetworkManager.Instance.playerSwords[i];
                    combatants[i] = swordObj != null ? swordObj.transform : null;
                }
                cameraCtrl.combatants = combatants;
            }
        }

        // ▼【N人対応】生存者数を今回の人数でリセット(再戦時の不整合防止)
        SwordBattle.alivePlayerCount = playerCount;
        SwordBattle.matchEnded = false;

        Random.InitState((int)System.DateTime.Now.Ticks);

        // カウントダウン演出を開始
        StartCoroutine(CountdownCameraRoutine(playerCount));
    }

    // ▼【N人対応】3・4人目の剣を、1人目の剣(PlayerSword)を複製して作る。見た目はJSONの画像でどのみち変わる
    GameObject CreateDynamicPlayerSword(int slotIndex, Vector3 position, SwordData swordData)
    {
        GameObject template = NetworkManager.Instance.playerSwords[0];
        if (template == null || swordData == null) return null;

        GameObject clone = Instantiate(template, position, Quaternion.identity);
        clone.name = "DynamicPlayerSword_" + slotIndex;

        var battle = clone.GetComponent<SwordBattle>();
        var controller = clone.GetComponent<SwordController>();
        var rb = clone.GetComponent<Rigidbody2D>();
        var blade = clone.transform.Find("Blade");

        // ▼ P1(複製元)の値をそのまま引き継いでしまわないよう、スロット順のプレイヤー番号を明示的に上書きする
        if (battle != null) battle.playerNumber = slotIndex + 1;

        var generator = clone.GetComponent<SwordGenerator>();
        if (generator == null) generator = clone.AddComponent<SwordGenerator>();
        generator.generateOnStart = false;
        generator.targetBladeWidth = (generators != null && generators.Length > 0 && generators[0] != null) ? generators[0].targetBladeWidth : 1.5f;
        generator.targetSpriteRenderer = blade != null ? blade.GetComponent<SpriteRenderer>() : null;
        generator.bladeCollider = blade != null ? blade.GetComponent<PolygonCollider2D>() : null;
        generator.swordRigidbody = rb;
        generator.swordBattle = battle;
        generator.handleObject = controller != null ? controller.handleObject : null;

        // ▼ HPバー等のUIの配置元を決める：
        // 専用テンプレート(p3HudTemplate/p4HudTemplate)がEditorで割り当てられていれば、
        // それ自体(PL3Bar/PL4Barなど、Editorで配置した実物のUI)をそのままこの剣のHPバーとして使う。
        // 未設定なら従来通り同じ側(偶数スロット=1人目側/奇数スロット=2人目側)のHPバーを複製 + Yオフセットで自動配置する。
        HudTemplate explicitTemplate = slotIndex == 2 ? p3HudTemplate : slotIndex == 3 ? p4HudTemplate : null;
        if (explicitTemplate != null && explicitTemplate.hpBar != null)
        {
            battle.hpBar = explicitTemplate.hpBar;
            battle.delayHpBar = explicitTemplate.delayHpBar;
            battle.nameText = explicitTemplate.nameText;
            battle.hpText = explicitTemplate.hpText;
            battle.spGaugeBar = explicitTemplate.spGaugeBar;
            battle.spText = explicitTemplate.spText;
            SetHudTemplateVisible(explicitTemplate, true);
        }
        else
        {
            GameObject hudTemplateObj = NetworkManager.Instance.playerSwords[slotIndex % 2];
            SwordBattle hudTemplate = hudTemplateObj != null ? hudTemplateObj.GetComponent<SwordBattle>() : null;
            if (hudTemplate != null)
                WireClonedHud(battle, hudTemplate.hpBar, hudTemplate.delayHpBar, hudTemplate.nameText,
                    hudTemplate.hpText, hudTemplate.spGaugeBar, hudTemplate.spText, new Vector2(0f, extraHudOffsetY));
        }

        generator.GenerateSwordFromJson(JsonUtility.ToJson(swordData));
        clone.SetActive(true);
        return clone;
    }

    // ▼ 元のHPバー/delayHpバー/SPバー(名前・数値テキストも子として含む)をそのまま複製し、
    // 指定したoffset分だけ位置をずらして新しいSwordBattleに配線する。SwordBattle側の更新ロジックは無改修のまま動く。
    // 専用テンプレート(HudTemplate)から呼ぶ場合はoffsetをVector2.zeroにし、テンプレート自身の位置をそのまま使う。
    void WireClonedHud(SwordBattle battle, Slider hpBarSrc, Slider delayHpBarSrc, TMPro.TextMeshProUGUI nameTextSrc,
        TMPro.TextMeshProUGUI hpTextSrc, Slider spGaugeBarSrc, TMPro.TextMeshProUGUI spTextSrc, Vector2 offset)
    {
        if (hpBarSrc == null) return;
        Transform canvasTransform = hpBarSrc.transform.parent;

        int nameIdx = nameTextSrc != null ? nameTextSrc.transform.GetSiblingIndex() : -1;
        int hpTextIdx = hpTextSrc != null ? hpTextSrc.transform.GetSiblingIndex() : -1;

        var hpBarClone = Instantiate(hpBarSrc.gameObject, canvasTransform);
        dynamicHudPieces.Add(hpBarClone);
        var hpBarRt = hpBarClone.GetComponent<RectTransform>();
        hpBarRt.anchoredPosition = hpBarSrc.GetComponent<RectTransform>().anchoredPosition + offset;
        battle.hpBar = hpBarClone.GetComponent<Slider>();
        if (nameIdx >= 0 && nameIdx < hpBarClone.transform.childCount)
            battle.nameText = hpBarClone.transform.GetChild(nameIdx).GetComponent<TMPro.TextMeshProUGUI>();
        if (hpTextIdx >= 0 && hpTextIdx < hpBarClone.transform.childCount)
            battle.hpText = hpBarClone.transform.GetChild(hpTextIdx).GetComponent<TMPro.TextMeshProUGUI>();

        if (delayHpBarSrc != null)
        {
            var delayClone = Instantiate(delayHpBarSrc.gameObject, canvasTransform);
            dynamicHudPieces.Add(delayClone);
            delayClone.GetComponent<RectTransform>().anchoredPosition = delayHpBarSrc.GetComponent<RectTransform>().anchoredPosition + offset;
            battle.delayHpBar = delayClone.GetComponent<Slider>();
        }

        if (spGaugeBarSrc != null)
        {
            int spTextIdx = spTextSrc != null ? spTextSrc.transform.GetSiblingIndex() : -1;
            var spClone = Instantiate(spGaugeBarSrc.gameObject, canvasTransform);
            dynamicHudPieces.Add(spClone);
            spClone.GetComponent<RectTransform>().anchoredPosition = spGaugeBarSrc.GetComponent<RectTransform>().anchoredPosition + offset;
            battle.spGaugeBar = spClone.GetComponent<Slider>();
            if (spTextIdx >= 0 && spTextIdx < spClone.transform.childCount)
                battle.spText = spClone.transform.GetChild(spTextIdx).GetComponent<TMPro.TextMeshProUGUI>();
        }
    }

    // ▼【N人対応】人数・モードに応じたスポーン座標を返す(2人時は従来のleft/rightをそのまま使用)
    // MultiplayerManagerからも同じ校正済みの座標を使うため公開している
    public Vector3[] GetSpawnPositions(int playerCount)
    {
        bool koma = SwordController.isKomaMode;
        switch (playerCount)
        {
            case 3:
                return ResolveSpawnPositions(koma ? koma3PPositions : sword3PPositions, koma ? koma3PSpawnPoints : sword3PSpawnPoints);
            case 4:
                return ResolveSpawnPositions(koma ? koma4PPositions : sword4PPositions, koma ? koma4PSpawnPoints : sword4PSpawnPoints);
            default:
                return new Vector3[] {
                    koma ? komaLeftPosition : leftPosition,
                    koma ? komaRightPosition : rightPosition
                };
        }
    }

    // ▼ p3HudTemplate/p4HudTemplate(PL3Bar/PL4Barなど、Editorで配置した実物のHPバー一式)の表示/非表示を切り替える。
    // 2人プレイなど、その人数の試合で使わない時は非表示にし、実際にその枠が参加する試合の時だけ表示する。
    // ▼【修正】MultiplayerManager側でも(autoTestOnStartの名残を消すために)呼べるようpublicにした
    public static void SetHudTemplateVisible(HudTemplate template, bool visible)
    {
        if (template == null) return;
        if (template.hpBar != null) template.hpBar.gameObject.SetActive(visible);
        if (template.delayHpBar != null) template.delayHpBar.gameObject.SetActive(visible);
        if (template.spGaugeBar != null) template.spGaugeBar.gameObject.SetActive(visible);
    }

    // ▼ 対応するindexにTransformが割り当てられていればその位置を、未設定ならfallback配列の数値をそのまま使う
    static Vector3[] ResolveSpawnPositions(Vector3[] fallback, Transform[] overrides)
    {
        Vector3[] result = new Vector3[fallback.Length];
        for (int i = 0; i < fallback.Length; i++)
        {
            Transform t = (overrides != null && i < overrides.Length) ? overrides[i] : null;
            result[i] = t != null ? t.position : fallback[i];
        }
        return result;
    }

    IEnumerator CountdownCameraRoutine(int playerCount)
    {
        SwordBattle.isRoundStarted = false;

        if (countdownText != null) countdownText.gameObject.SetActive(true);

        BattleCamera cameraCtrl = Camera.main.GetComponent<BattleCamera>();

        if (playerCount == 2 && NetworkManager.Instance != null)
        {
            GameObject hostObj = NetworkManager.Instance.playerSwords[0];
            GameObject clientObj = NetworkManager.Instance.playerSwords[1];
            Transform hostTarget = hostObj != null ? hostObj.transform : null;
            Transform clientTarget = clientObj != null ? clientObj.transform : null;

            // ーーー 【最初の1秒：カウント3】 ホスト側の剣を映す ーーー
            if (countdownText != null) countdownText.text = "3";
            if (cameraCtrl != null && hostTarget != null) {
                // ★【修正】固定値の 3f ではなく、インスペクタで指定した countdownZoomSize を使う
                cameraCtrl.FocusOnTarget(hostTarget, countdownZoomSize);
            }
            yield return new WaitForSeconds(1.0f);

            // ーーー 【次の1秒：カウント2】 クライアント側の剣を映す ーーー
            if (countdownText != null) countdownText.text = "2";
            if (cameraCtrl != null && clientTarget != null) {
                // ★【修正】こちらもインスペクタの数値を使う
                cameraCtrl.FocusOnTarget(clientTarget, countdownZoomSize);
            }
            yield return new WaitForSeconds(1.0f);

            // ーーー 【最後の1秒：カウント1】 本来の引きカメラ位置に戻す ーーー
            if (countdownText != null) countdownText.text = "1";
            if (cameraCtrl != null) {
                cameraCtrl.ResetFocus();
            }
            yield return new WaitForSeconds(1.0f);
        }
        else
        {
            // ▼【N人対応】3〜4人戦は個別クローズアップせず、最初から全員が入る引きの画角でカウントする
            if (cameraCtrl != null) cameraCtrl.ResetFocus();

            if (countdownText != null) countdownText.text = "3";
            yield return new WaitForSeconds(1.0f);

            if (countdownText != null) countdownText.text = "2";
            yield return new WaitForSeconds(1.0f);

            if (countdownText != null) countdownText.text = "1";
            yield return new WaitForSeconds(1.0f);
        }

        // ーーー 【カウント0：GO!】 操作解禁、ゲームスタート！ ーーー
        if (countdownText != null) countdownText.text = "GO!";
        SwordBattle.isRoundStarted = true;
        Debug.Log("🟢 isRoundStarted を true にした！");   // ← この行を追加

        yield return new WaitForSeconds(1.0f);
        if (countdownText != null) countdownText.gameObject.SetActive(false);
    }
}
