using System;
using System.Collections;
using UnityEngine;

// Unityエディタの Play だけで4人個人戦を動かして確認するためのドライバ。
//
// エディタ専用。WebGLビルドでは Awake で自分を無効化するので、本番の React 主導の流れには一切関与しない。
// 本番では React（PeerJS）が MultiplayerManager を駆動するが、このコンポーネントは
// その React と同じ MP_* メッセージを NetworkManager 経由で送受信する。つまり通信部分だけを
// ローカルに置き換えたもので、名簿の作成・初期化の合図・入力の中継・結果の受信という
// 本番と同じ手順をそのまま通る。MultiplayerManager 側には一切の分岐を足していない。
public class LocalFourPlayerDriver : MonoBehaviour
{
    [Header("ローカル対戦設定")]
    [Tooltip("Play開始と同時に対戦を始める。OFFにすると既存の2人用デバッグ動作に戻る")]
    public bool autoStart = true;
    [Tooltip("参加人数。2 か 4 のみ有効（3以上は4として扱う）")]
    public int playerCount = 4;
    [Tooltip("ONで独楽モード、OFFで剣モード")]
    public bool komaMode = false;
    [Tooltip("各プレイヤーのHP（1〜1000）")]
    public int hitPoints = 300;

    [Header("剣データ")]
    [Tooltip("SwordData形式のJSON（Assets/Sword/dummy_sword.json など）。要素順に P1, P2, P3, P4 へ割り当てる。最優先")]
    public TextAsset[] swordJsonFiles;
    [Tooltip("刃に使う切り抜き画像。要素順に P1, P2, P3, P4 へ割り当てる。" +
             "未設定なら Assets/Sword の既存サンプルを使う")]
    public Texture2D[] swordImages;

    [Header("BOT")]
    [Tooltip("人間が操作していないプレイヤーを自動で動かす。ONにすると入力が連射されるので、" +
             "剣モードでは剣が回り続ける（1クリックごとに spinTorque が掛かるため）。既定はOFF")]
    public bool enableBots = false;
    [Tooltip("この秒数だけキー入力が無いプレイヤーはBOTに任せる")]
    public float botTakeoverDelay = 3f;
    [Tooltip("BOTの平均入力間隔（秒）")]
    public float botInterval = 0.5f;

    [Header("HUD")]
    public bool showHud = true;

    // P1 は左クリックでも操作できる（本番と同じ SubmitLocalInput 経由）。
    static readonly KeyCode[][] LeftKeys =
    {
        new[] { KeyCode.A },
        new[] { KeyCode.LeftArrow },
        new[] { KeyCode.J },
        new[] { KeyCode.Keypad4, KeyCode.N },
    };
    static readonly KeyCode[][] RightKeys =
    {
        new[] { KeyCode.D },
        new[] { KeyCode.RightArrow },
        new[] { KeyCode.L },
        new[] { KeyCode.Keypad6, KeyCode.M },
    };
    // Inspector が空のときに使う、プロジェクトに元からある切り抜き画像。
    // sampleC.png は 457x177 で被写体が右端に寄った未整形カットなので既定では使わない
    // （刃が 3.0x1.16 unit と極端に平たくなり、当たり判定も中心からずれる）。
    static readonly string[] FallbackImages =
    {
        "Assets/Sword/sampleA.png",
        "Assets/Sword/sampleB.png",
    };

    // MultiplayerManager が剣の頭上に出す P1〜P4 の色と合わせる。
    static readonly Color[] PlayerColors =
    {
        new Color(.2f, .5f, 1f), new Color(1f, .25f, .3f),
        new Color(.2f, .9f, .4f), new Color(.8f, .4f, 1f),
    };

    // ビルドでは Awake が早期に無効化するため代入されない。明示的に初期化して警告を避ける。
    MultiplayerManager multiplayer = null;
    SceneController sceneController = null;
    NetworkManager network = null;

    MultiplayerConfig config;
    MultiplayerSync latestSync;
    MultiplayerResult latestResult;
    string phase = "IDLE";
    int matchSerial;
    float maxSp = 100f;

    int[] sequences = new int[0];
    float[] lastHumanInput = new float[0];
    float[] nextBotInput = new float[0];

    void Awake()
    {
#if UNITY_EDITOR
        sceneController = GetComponent<SceneController>();
        // SceneController.Awake が MultiplayerManager を足すので、この時点では未取得の場合がある。
        multiplayer = GetComponent<MultiplayerManager>();
        if (multiplayer == null) multiplayer = gameObject.AddComponent<MultiplayerManager>();
        network = GetComponent<NetworkManager>();

        // 2人用のデバッグ自動開始と衝突させない。
        if (autoStart && sceneController != null) sceneController.autoTestOnStart = false;
#else
        // ビルドでは React が試合を始めるので、このドライバは完全に黙らせる。
        // network を掴まないので OnEnable も購読せず、enabled=false で Start/Update/OnGUI も走らない。
        autoStart = false;
        enabled = false;
#endif
    }

    void OnEnable() { if (network != null) network.LocalMessageSink += OnUnityMessage; }
    void OnDisable() { if (network != null) network.LocalMessageSink -= OnUnityMessage; }

    IEnumerator Start()
    {
        if (!autoStart) yield break;
        yield return null; // 全コンポーネントの Start を通してから開始する。
        StartMatch();
    }

    // ---------------------------------------------------------------- 開始

    public void StartMatch()
    {
        if (multiplayer == null) { Debug.LogError("LocalFourPlayerDriver: MultiplayerManager が見つかりません。"); return; }

        int count = playerCount >= 3 ? 4 : 2;
        if (count != playerCount) Debug.LogWarning($"LocalFourPlayerDriver: playerCount={playerCount} は無効なので {count} で開始します。");

        // SceneController の Tキー再初期化は、ローカル対戦の剣を壊すので止めておく。
        if (sceneController != null) { sceneController.autoTestOnStart = false; sceneController.enabled = false; }

        latestSync = null; latestResult = null; phase = "LOADING";
        sequences = new int[count];
        lastHumanInput = new float[count];
        nextBotInput = new float[count];
        for (int i = 0; i < count; i++) { lastHumanInput[i] = -999f; nextBotInput[i] = 0f; }

        if (network != null && network.hostSword != null)
        {
            var template = network.hostSword.GetComponent<SwordBattle>();
            if (template != null) maxSp = template.maxSp;
        }

        var players = new MultiplayerPlayerConfig[count];
        for (int i = 0; i < count; i++)
            players[i] = new MultiplayerPlayerConfig
            {
                playerId = "P" + (i + 1), slotIndex = i, spawnIndex = i, swordData = BuildSword(i),
            };

        config = new MultiplayerConfig
        {
            matchId = "local-" + (++matchSerial),
            localPlayerId = "P1",
            isHost = true,
            gameMode = komaMode ? "1" : "0",
            players = players,
        };

        Debug.Log($"🎮 ローカル{count}人対戦を開始します（matchId={config.matchId}, mode={(komaMode ? "独楽" : "剣")}）");
        multiplayer.InitializeMultiplayer(JsonUtility.ToJson(config));
    }

    SwordData BuildSword(int index)
    {
        // 1) SwordData のJSONがあれば最優先。実際の試合と同じ形のデータで試せる。
        if (swordJsonFiles != null && index < swordJsonFiles.Length && swordJsonFiles[index] != null &&
            !string.IsNullOrEmpty(swordJsonFiles[index].text))
        {
            var loaded = JsonUtility.FromJson<SwordData>(swordJsonFiles[index].text);
            if (loaded != null && !string.IsNullOrEmpty(loaded.imageStr))
            {
                loaded.hp = Mathf.Clamp(hitPoints, 1, 1000);
                return loaded;
            }
            Debug.LogWarning($"LocalFourPlayerDriver: swordJsonFiles[{index}] を読めないので画像から組み立てます。");
        }

        // 2) Inspector に割り当てた画像、3) 無ければ Assets/Sword の既存サンプル。
        string image = null;
        if (swordImages != null && index < swordImages.Length && swordImages[index] != null)
            image = EncodeAsset(swordImages[index]);
        if (image == null) image = EncodeFile(FallbackImages[index % FallbackImages.Length]);
        if (image == null)
            Debug.LogError("LocalFourPlayerDriver: 刃の画像を読めませんでした。Inspectorの『刃に使う切り抜き画像』を設定してください。");

        // 攻撃力・重さをずらして、4人でも力関係が偏りすぎないようにする。
        int[] attacks = { 70, 55, 85, 45 };
        int[] weights = { 30, 45, 20, 70 };
        return new SwordData
        {
            name = "Local P" + (index + 1),
            hp = Mathf.Clamp(hitPoints, 1, 1000),
            attack = attacks[index % attacks.Length],
            weight = weights[index % weights.Length],
            imageStr = image,
        };
    }

    // SwordGenerator は React が送ってくるのと同じ base64 PNG を前提にしている。
    // 画像の取り込み設定は isReadable: 0 なので EncodeToPNG は使えず、
    // 元のPNGファイルをそのまま読んで渡す（エディタ専用なので File 参照で問題ない）。
    static string EncodeAsset(Texture2D texture)
    {
#if UNITY_EDITOR
        return EncodeFile(UnityEditor.AssetDatabase.GetAssetPath(texture));
#else
        return null;
#endif
    }

    static string EncodeFile(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath) || !System.IO.File.Exists(assetPath)) return null;
        return Convert.ToBase64String(System.IO.File.ReadAllBytes(assetPath));
    }

    // ------------------------------------------------- Unity → React（の代役）

    void OnUnityMessage(string type, string json)
    {
        if (config == null) return;
        // 自分が始めた試合以外（PlayModeテストなどが直接動かしている試合）には手を出さない。
        var envelope = JsonUtility.FromJson<MultiplayerCommand>(json);
        if (envelope == null || envelope.matchId != config.matchId) return;

        switch (type)
        {
            case "MP_INITIALIZED":
                // 本番の React と同じく、初期化完了を待ってからカウントダウンを始める。
                multiplayer.BeginMultiplayer(json);
                break;
            case "MP_LOAD_FAILED":
                phase = "LOAD_FAILED";
                Debug.LogError("LocalFourPlayerDriver: 剣の生成に失敗しました。" + json);
                break;
            case "MP_SYNC":
                var sync = JsonUtility.FromJson<MultiplayerSync>(json);
                if (sync != null) { latestSync = sync; phase = sync.phase; }
                break;
            case "MP_RESULT":
                var result = JsonUtility.FromJson<MultiplayerResult>(json);
                if (result != null)
                {
                    latestResult = result; phase = "RESULT";
                    Debug.Log($"🏁 決着: {(result.draw ? "引き分け" : result.winnerId + " の勝ち")}");
                }
                break;
            case "MP_INPUT":
                // P1 のクリック操作。React が本来ここで playerId と連番を埋める。
                int local = IndexOf(config.localPlayerId);
                if (local >= 0) { lastHumanInput[local] = Time.unscaledTime; Dispatch(local, envelope.direction == "RIGHT"); }
                break;
        }
    }

    int IndexOf(string playerId)
    {
        for (int i = 0; i < config.players.Length; i++) if (config.players[i].playerId == playerId) return i;
        return -1;
    }

    // ------------------------------------------------- React（の代役）→ Unity

    void Dispatch(int slot, bool right)
    {
        sequences[slot]++;
        multiplayer.ReceiveMultiplayerInput(JsonUtility.ToJson(new MultiplayerCommand
        {
            matchId = config.matchId,
            playerId = config.players[slot].playerId,
            seq = sequences[slot],
            action = "PRIMARY",
            direction = right ? "RIGHT" : "LEFT",
        }));
    }

    void Update()
    {
        // autoStart を切って既存の2人用デバッグ動作を試している間は、ホットキーを一切奪わない。
        if (!autoStart && config == null) return;

        if (Input.GetKeyDown(KeyCode.R)) { StartMatch(); return; }
        if (Input.GetKeyDown(KeyCode.B)) enableBots = !enableBots;
        if (Input.GetKeyDown(KeyCode.H)) showHud = !showHud;
        if (Input.GetKeyDown(KeyCode.K)) { komaMode = !komaMode; StartMatch(); return; }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { playerCount = 2; StartMatch(); return; }
        if (Input.GetKeyDown(KeyCode.Alpha4)) { playerCount = 4; StartMatch(); return; }

        if (config == null || !multiplayer.IsPlaying) return;

        for (int i = 0; i < config.players.Length; i++)
        {
            if (AnyKeyDown(LeftKeys[i])) { lastHumanInput[i] = Time.unscaledTime; Dispatch(i, false); }
            else if (AnyKeyDown(RightKeys[i])) { lastHumanInput[i] = Time.unscaledTime; Dispatch(i, true); }
            else if (IsBot(i) && Time.unscaledTime >= nextBotInput[i])
            {
                nextBotInput[i] = Time.unscaledTime + UnityEngine.Random.Range(botInterval * .6f, botInterval * 1.6f);
                Dispatch(i, UnityEngine.Random.value > .5f);
            }
        }
    }

    bool IsBot(int slot)
    {
        return enableBots && Time.unscaledTime - lastHumanInput[slot] > botTakeoverDelay;
    }

    static bool AnyKeyDown(KeyCode[] codes)
    {
        foreach (var code in codes) if (Input.GetKeyDown(code)) return true;
        return false;
    }

    // ---------------------------------------------------------------- HUD

    GUIStyle label, header, small;

    void OnGUI()
    {
        if (!showHud || config == null) return;
        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.white } };
            header = new GUIStyle(label) { fontSize = 15, fontStyle = FontStyle.Bold };
            small = new GUIStyle(label) { fontSize = 11, normal = { textColor = new Color(.75f, .78f, .85f) } };
        }

        int count = config.players.Length;
        const float cardWidth = 210f, cardHeight = 74f, gap = 8f;
        float totalWidth = count * cardWidth + (count - 1) * gap;
        float x = (Screen.width - totalWidth) * .5f;

        for (int i = 0; i < count; i++) DrawPlayerCard(new Rect(x + i * (cardWidth + gap), 10f, cardWidth, cardHeight), i);

        string status = phase == "COUNTDOWN" && latestSync != null
            ? $"COUNTDOWN {Mathf.CeilToInt(latestSync.countdownRemaining)}"
            : phase;
        GUI.Label(new Rect(12f, 10f, 320f, 22f), $"LOCAL {count}P  /  {(komaMode ? "KOMA" : "SWORD")}  /  {status}", header);
        GUI.Label(new Rect(12f, 32f, 420f, 20f), $"match {config.matchId}   bots {(enableBots ? "ON" : "OFF")}", small);

        GUI.Label(new Rect(12f, Screen.height - 92f, 520f, 90f),
            "P1 A/D or click   P2 ←/→   P3 J/L   P4 Numpad4/6 or N/M\n" +
            "R restart    2 / 4 player count    K koma mode    B bots    H hud", small);

        if (latestResult != null) DrawResult();
    }

    void DrawPlayerCard(Rect rect, int slot)
    {
        var player = config.players[slot];
        var state = FindState(player.playerId);
        Color color = PlayerColors[slot % PlayerColors.Length];
        int hp = state != null ? state.hp : player.swordData.hp;
        bool dead = hp <= 0;

        Fill(rect, new Color(0f, 0f, 0f, .55f));
        Fill(new Rect(rect.x, rect.y, 4f, rect.height), dead ? Color.gray : color);

        string tag = dead ? "K.O." : IsBot(slot) ? "BOT" : "YOU";
        if (!dead && state != null && state.isDashing) tag = "DASH";
        GUI.Label(new Rect(rect.x + 10f, rect.y + 4f, rect.width - 20f, 18f),
            $"P{slot + 1}   {tag}", header);

        var hpBar = new Rect(rect.x + 10f, rect.y + 26f, rect.width - 20f, 12f);
        Fill(hpBar, new Color(.15f, .05f, .05f, .9f));
        Fill(new Rect(hpBar.x, hpBar.y, hpBar.width * Mathf.Clamp01((float)hp / player.swordData.hp), hpBar.height),
            dead ? Color.gray : new Color(.3f, .85f, .35f));
        GUI.Label(new Rect(hpBar.x, hpBar.y - 1f, hpBar.width, 14f), $"  HP {hp} / {player.swordData.hp}", small);

        float sp = state != null ? state.sp : 0f;
        var spBar = new Rect(rect.x + 10f, rect.y + 44f, rect.width - 20f, 8f);
        Fill(spBar, new Color(.05f, .08f, .18f, .9f));
        Fill(new Rect(spBar.x, spBar.y, spBar.width * Mathf.Clamp01(sp / Mathf.Max(1f, maxSp)), spBar.height),
            new Color(.35f, .7f, 1f));

        string target = state != null && !string.IsNullOrEmpty(state.targetPlayerId) ? state.targetPlayerId : "-";
        GUI.Label(new Rect(rect.x + 10f, rect.y + 54f, rect.width - 20f, 16f), $"SP {Mathf.FloorToInt(sp)}   target {target}", small);
    }

    MultiplayerPlayerState FindState(string playerId)
    {
        if (latestSync == null || latestSync.players == null) return null;
        foreach (var state in latestSync.players) if (state != null && state.playerId == playerId) return state;
        return null;
    }

    void DrawResult()
    {
        int rows = latestResult.standings != null ? latestResult.standings.Length : 0;
        var panel = new Rect((Screen.width - 380f) * .5f, (Screen.height - (86f + rows * 20f)) * .5f, 380f, 86f + rows * 20f);
        Fill(panel, new Color(0f, 0f, 0f, .8f));

        GUI.Label(new Rect(panel.x + 16f, panel.y + 10f, panel.width - 32f, 24f),
            latestResult.draw ? "DRAW" : $"WINNER  {latestResult.winnerId}", header);
        GUI.Label(new Rect(panel.x + 16f, panel.y + 36f, panel.width - 32f, 18f),
            "rank  player   kills   dealt   taken   reason", small);

        for (int i = 0; i < rows; i++)
        {
            var score = latestResult.standings[i];
            GUI.Label(new Rect(panel.x + 16f, panel.y + 56f + i * 20f, panel.width - 32f, 18f),
                $"  {score.rank}      {score.playerId}       {score.kills}       {score.damageDealt}       {score.damageTaken}      {score.eliminationReason ?? "SURVIVED"}",
                label);
        }
        GUI.Label(new Rect(panel.x + 16f, panel.y + panel.height - 24f, panel.width - 32f, 18f), "press R to rematch", small);
    }

    static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
