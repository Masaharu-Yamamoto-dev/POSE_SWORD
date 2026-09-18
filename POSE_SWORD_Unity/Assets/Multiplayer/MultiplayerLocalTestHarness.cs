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

    [Header("【調査用】ゲスト(対戦相手)視点の再現")]
    [Tooltip("OFFにすると、このEditorインスタンス自身をisHost=falseで初期化する。実際のオンライン対戦の" +
        "ゲスト側(IsHost=False・剣のRigidbody2DがKinematic)と同じコード経路をEditor上でそのまま再現できるため、" +
        "Sceneビュー/Inspectorで柄(Handle-A)の実際の状態を直接確認できる。" +
        "(注: isHost=falseだとHost側からのSYNCが無いため剣は生成後その場で静止したままになる。" +
        "動きの確認ではなく、生成直後の柄の見た目・Inspector上の値を確認する用途向け)")]
    public bool isHost = true;

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
        var players = new MultiplayerPlayerConfig[count];
        for (int i = 0; i < count; i++)
        {
            // 手持ちの剣データが人数分無ければ使い回す
            var sword = data.players[i % data.players.Length];
            players[i] = new MultiplayerPlayerConfig
            {
                playerId = "p" + i,
                slotIndex = i,
                spawnIndex = i,
                swordData = ownedSwordCount > 1 ? BuildOwnedSwordsTestData(sword, ownedSwordCount) : sword
            };
        }

        var config = new MultiplayerConfig
        {
            matchId = "editor-local-test-" + Time.realtimeSinceStartup,
            localPlayerId = "p0",
            isHost = isHost,
            gameMode = gameMode,
            livesMode = livesMode,
            players = players
        };
        return JsonUtility.ToJson(config);
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
