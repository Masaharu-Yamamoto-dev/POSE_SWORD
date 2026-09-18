#if UNITY_EDITOR
using System.Collections;
using UnityEngine;

// ▼【新規追加】Unityエディタで実際にPlayを押すだけで、本番のReact/サーバーを用意しなくても
// マルチプレイのホスト体験をそのまま遊んで確認できるようにするデバッグ専用ハーネス。
//
// 本番(WebGL)では、自分自身の入力(クリック)もいったんサーバーへ送られ、サーバーがplayerId/seqを
// 付与してからHostのReceiveMultiplayerInputへ送り返す設計になっている。エディタ内では
// NetworkManager.SendDataが何もしない(SendToReactが呼ばれない)ため、このハーネスが
// NetworkManager.EditorSendDataHookを購読して、送信されるはずだったメッセージをその場で折り返す。
//
// 使い方：GameManagerオブジェクトにこのコンポーネントを追加してPlayを押すだけ
// (「POSE SWORD/Add Multiplayer Local Test Harness」メニューからワンクリックで追加できる)。
// AI対戦相手は用意していないため、剣モードは自キャラだけが動き、独楽モードは全員が
// 既存の自動追尾ロジックでその場で戦い始める。
[RequireComponent(typeof(MultiplayerManager))]
public class MultiplayerLocalTestHarness : MonoBehaviour
{
    [Header("Playを押した瞬間に自動でホスト対戦を開始する")]
    public bool autoStartOnPlay = true;

    [Tooltip("未設定ならSceneController.debugBattleJsonFileの剣データをそのまま使い回す")]
    public TextAsset debugRosterJsonFile;

    [Range(2, 4)]
    [Tooltip("自分(P1)を含めた対戦人数。AIはいないためP2以降は基本的に棒立ち(独楽モードだけ自動で動く)")]
    public int playerCount = 4;

    [Tooltip("\"0\"=剣モード, \"1\"=独楽モード")]
    public string gameMode = "0";

    [Header("残機・分身の見た目テスト")]
    [Range(1, 3)]
    [Tooltip("デバッグ用の剣データから、HPが異なる手持ちの剣を何本合成するか。2以上にすると分身系必殺技(オレ達" +
        "アタック/オレ達シールド)が手持ちの剣の形を使う演出を確認できる(これはlivesModeのON/OFFに関わらず働く)。" +
        "1のままなら従来通り1本だけで、1本しかない場合にバグらないかの確認にもなる")]
    public int ownedSwordCount = 3;

    [Tooltip("ONにすると、脱落時に次の剣のHPへ持ち替えて延命する(残機モード=MultiplayerConfig.livesMode)。" +
        "OFFのままでもownedSwordCountを2以上にすれば分身の見た目バリエーションだけは確認できる")]
    public bool livesMode = false;

    [Header("1vs3（ボス vs 三人組）")]
    [Tooltip("ONにすると1人(ボス)対3人の非対称戦になる。人数は自動的に4人へ、残機モードは自動的にOFFになる" +
        "(本番のロビーでも両立しない組み合わせのため)。3人側は同士討ちしない")]
    public bool soloMode = false;

    [Range(0, 3)]
    [Tooltip("どのスロットをボスにするか。本番は開始時にReact側が抽選するが、ここでは固定で選ぶ。" +
        "AI対戦相手がいないため、制圧を実際に撃てるのは自分が操作するP1(=0)にした時だけ")]
    public int bossSlot = 0;

    [Header("1vs3：ボス強化（本番の既定値は HostRoom.js の SOLO_BUFF）")]
    public float hpMultiplier = 3.0f;
    public float attackMultiplier = 2.0f;
    public float spGainMultiplier = 1.5f;
    [Tooltip("ボスのSPゲージ上限。通常必殺技は100のままで、満タンの200で制圧を撃てる")]
    public float bossMaxSp = 200f;
    [Tooltip("制圧が届く半径。発動した瞬間にこの範囲内にいた敵だけが対象になる")]
    public float suppressRadius = 8f;
    [Tooltip("操作不能になる秒数。溜め1秒＋薙ぎ払いの後、ボスが自由に殴れる時間がここから引いた分。強さへの影響が一番大きい")]
    public float suppressDuration = 4f;

    [Header("1vs3：掌握の2段目（引き寄せ → 薙ぎ払い）")]
    [Tooltip("引き寄せてから斬るまでの溜め時間")]
    public float judgmentPullSeconds = 1f;
    [Tooltip("引き寄せる力（質量に掛ける）。弱いと集まりきらない")]
    public float judgmentPullForce = 60f;
    [Tooltip("薙ぎ払いのダメージ（ボスの攻撃力に掛ける）。通常攻撃の最大の一撃が攻撃力ぶんなので、1.3なら通常1.3発ぶん")]
    public float judgmentDamageMultiplier = 1.3f;
    [Tooltip("薙ぎ払いで外へ吹き飛ばす力。そのまま速度変化(units/秒)になる。通常の剣同士の衝突は20")]
    public float judgmentKnockback = 40f;
    [Tooltip("ボスが薙ぎ払っている時間")]
    public float judgmentSweepSeconds = 0.4f;
    [Tooltip("薙ぎ払いの回転速度(度/秒)。既存の巨大回転斬が1080。720なら0.4秒で約0.8回転＝一振りとして読める")]
    public float judgmentSweepSpin = 720f;
    [Tooltip("薙ぎ払い中の剣の大きさ(元の大きさに掛ける)")]
    public float judgmentSweepScale = 2f;

    MultiplayerManager manager;
    SceneController scene;
    int localSeq;

    void Awake()
    {
        manager = GetComponent<MultiplayerManager>();
        scene = GetComponent<SceneController>();
        NetworkManager.EditorSendDataHook += HandleOutgoing;
    }

    void OnDestroy()
    {
        NetworkManager.EditorSendDataHook -= HandleOutgoing;
    }

    void Start()
    {
        if (autoStartOnPlay) StartCoroutine(RunHostMatch());
    }

    IEnumerator RunHostMatch()
    {
        // SceneController/MultiplayerManagerのAwakeが済んでから開始する
        yield return null;
        string json = BuildConfigJson();
        if (json == null) yield break;
        manager.InitializeMultiplayer(json);
    }

    string BuildConfigJson()
    {
        var source = debugRosterJsonFile != null ? debugRosterJsonFile
            : scene != null ? scene.debugBattleJsonFile : null;
        if (source == null || string.IsNullOrEmpty(source.text))
        {
            Debug.LogWarning("MultiplayerLocalTestHarness: デバッグ用の剣データJSON(debugRosterJsonFile/SceneController.debugBattleJsonFile)が見つかりません。");
            return null;
        }
        var data = JsonUtility.FromJson<BattleStartData>(source.text);
        if (data == null || data.players == null || data.players.Length == 0)
        {
            Debug.LogWarning("MultiplayerLocalTestHarness: デバッグ用JSONにplayersが含まれていません。");
            return null;
        }

        int count = Mathf.Clamp(playerCount, 2, 4);
        // 1vs3は1人対3人が揃って初めて成立する。人数が違うとValidateConfigで弾かれるので合わせる。
        if (soloMode && count != 4)
        {
            Debug.LogWarning("MultiplayerLocalTestHarness: 1vs3のため対戦人数を4人にしました。");
            count = 4;
        }
        // 残機モードとの併用は本番のロビーでも許していない(ボスの実効HPが膨らみ調整が追えなくなるため)。
        bool lives = livesMode && !soloMode;
        if (livesMode && soloMode)
            Debug.LogWarning("MultiplayerLocalTestHarness: 1vs3と残機モードは併用できないため、残機モードをOFFにしました。");
        int boss = Mathf.Clamp(bossSlot, 0, count - 1);

        var players = new MultiplayerPlayerConfig[count];
        int nextTrioSpawn = 1;
        for (int i = 0; i < count; i++)
        {
            // 手持ちの剣データが人数分無ければ使い回す
            var sword = data.players[i % data.players.Length];
            players[i] = new MultiplayerPlayerConfig
            {
                playerId = "p" + i,
                slotIndex = i,
                // 1vs3では陣営ごとにまとめて配置したいので、ボスを0番の位置に固定する
                // (本番のRoomSession.assignRolesと同じ割り当て)
                spawnIndex = !soloMode ? i : (i == boss ? 0 : nextTrioSpawn++),
                team = soloMode && i != boss ? MultiplayerManager.TrioTeam : MultiplayerManager.BossTeam,
                swordData = ownedSwordCount > 1 ? BuildOwnedSwordsTestData(sword, ownedSwordCount) : sword
            };
        }

        var config = new MultiplayerConfig
        {
            matchId = "editor-local-test-" + Time.realtimeSinceStartup,
            localPlayerId = "p0",
            isHost = true,
            gameMode = gameMode,
            livesMode = lives,
            soloMode = soloMode,
            soloBuff = soloMode ? BuildSoloBuff() : null,
            players = players
        };
        return JsonUtility.ToJson(config);
    }

    // 本番ではReact側(HostRoom.jsのSOLO_BUFF)が決めて配る値を、エディタではInspectorから組み立てる。
    // 倍率をここでいじって、バランスの当たりを付けるための枠でもある。
    MultiplayerSoloBuff BuildSoloBuff()
    {
        return new MultiplayerSoloBuff
        {
            hpMultiplier = hpMultiplier,
            attackMultiplier = attackMultiplier,
            spGainMultiplier = spGainMultiplier,
            maxSp = bossMaxSp,
            suppressRadius = suppressRadius,
            suppressDuration = suppressDuration,
            judgmentPullSeconds = judgmentPullSeconds,
            judgmentPullForce = judgmentPullForce,
            judgmentDamageMultiplier = judgmentDamageMultiplier,
            judgmentKnockback = judgmentKnockback,
            judgmentSweepSeconds = judgmentSweepSeconds,
            judgmentSweepSpin = judgmentSweepSpin,
            judgmentSweepScale = judgmentSweepScale
        };
    }

    // ▼【新規追加】残機モード・分身の見た目テスト用に、デバッグ用の剣1本からHPが異なる手持ちの剣を
    // その場でcount本(1〜3)合成する。本番はReact側(swordList)が既に持っている最大3本をswords[]として
    // 送ってくるが、エディタ単体テストではその元データが無いため、ここで擬似的に作る
    static SwordData BuildOwnedSwordsTestData(SwordData baseSword, int count)
    {
        count = Mathf.Clamp(count, 1, 3);
        float[] hpRatios = { 1f, 0.7f, 0.5f };
        var slots = new SwordSlotData[count];
        for (int i = 0; i < count; i++)
        {
            slots[i] = new SwordSlotData
            {
                name = baseSword.name + (i == 0 ? "" : $" {i + 1}本目"),
                attack = baseSword.attack,
                weight = baseSword.weight,
                hp = Mathf.Max(1, Mathf.RoundToInt(baseSword.hp * hpRatios[i])),
                imageStr = baseSword.imageStr,
                hiltType = baseSword.hiltType,
                isEmpty = false
            };
        }
        return new SwordData
        {
            name = slots[0].name, attack = slots[0].attack, weight = slots[0].weight, hp = slots[0].hp,
            imageStr = slots[0].imageStr, hiltType = slots[0].hiltType,
            equippedIndex = 0,
            swords = slots
        };
    }

    // ▼ NetworkManager.SendData経由で「本来はReact/サーバーへ送られるはずだった」メッセージを横取りする
    void HandleOutgoing(string type, string json)
    {
        if (manager == null || !manager.Active) return;
        switch (type)
        {
            case "MP_INITIALIZED":
                // ▼ 本番はReact側がロード完了を見てカウントダウン開始(BeginMultiplayer)を送り返してくる。
                // ペイロードがmatchIdだけのMultiplayerCommandで両者互換なのでそのまま渡せる
                manager.BeginMultiplayer(json);
                break;
            case "MP_INPUT":
                // ▼ 本番はサーバーがplayerId/seqを付与してHost自身へ送り返す。ここでは自分(p0)からの
                // 入力として、その付与処理を代わりに行ってHostのReceiveMultiplayerInputへ折り返す
                var cmd = JsonUtility.FromJson<MultiplayerCommand>(json);
                if (cmd == null) return;
                cmd.playerId = "p0";
                cmd.seq = ++localSeq;
                manager.ReceiveMultiplayerInput(JsonUtility.ToJson(cmd));
                break;
        }
    }
}
#endif
