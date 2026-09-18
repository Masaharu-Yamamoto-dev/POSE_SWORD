using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PoseSword.Multiplayer;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Runs after the controllers' FixedUpdates. Manual simulation gives one explicit
// post-physics boundary for all collision callbacks and simultaneous eliminations.
[DefaultExecutionOrder(10000)]
public class MultiplayerManager : MonoBehaviour
{
    public bool IsHost { get; private set; }
    public bool IsPlaying { get { return phase == "PLAYING"; } }
    // ▼【修正】ローカル(シングルプレイ)は開始直後から物理演算が常に動いており、独楽はカウントダウン中も
    // その場で回り続け、剣は重力で沈む。以前はIsPlayingになるまでPhysics2D.Simulate自体を呼んでおらず、
    // マルチプレイだけカウントダウン中に完全静止していた。COUNTDOWN中もシミュレーションを進めるための判定
    public bool IsSimulating { get { return phase == "COUNTDOWN" || IsPlaying; } }
    public bool Active { get { return config != null; } }
    public string Phase => phase;
    // ▼【新規追加】React側ではカウントダウンを表示しないため、Host自身はcountdownEndから直接計算し、
    // Client側はHostからSYNCで届く値(receivedCountdownRemaining)をそのまま使う
    public float CountdownRemaining => IsHost
        ? (phase == "COUNTDOWN" ? Mathf.Max(0, countdownEnd - Time.unscaledTime) : 0f)
        : receivedCountdownRemaining;
    private MultiplayerConfig config;
    private MatchRules rules;
    private readonly Dictionary<string, SwordBattle> swords = new Dictionary<string, SwordBattle>();
    private readonly Dictionary<string, Rigidbody2D> bodies = new Dictionary<string, Rigidbody2D>();
    private readonly Dictionary<string, string> targets = new Dictionary<string, string>();
    private readonly Dictionary<string, int> sequences = new Dictionary<string, int>();
    private readonly Dictionary<string, float> inputTimes = new Dictionary<string, float>();
    private readonly Dictionary<string, bool> invulnerable = new Dictionary<string, bool>();
    private readonly Dictionary<string, MultiplayerPlayerState> syncTargets = new Dictionary<string, MultiplayerPlayerState>();
    private readonly List<Hit> hits = new List<Hit>();
    private readonly HashSet<string> forfeits = new HashSet<string>();
    private GameObject arena;
    // ▼【新規追加】分身突進・リーフシールドなど、本体以外の"付随体"をSYNCでクライアントへ配信するための登録簿（Host側で使用）
    private readonly Dictionary<string, (GameObject obj, string ownerId, Color color, int spriteIndex)> activeClones = new Dictionary<string, (GameObject, string, Color, int)>();
    private int cloneIdSeq;
    // ▼【新規追加】クライアント側で、SYNCで届いた分身の位置を再現するための見た目専用オブジェクト
    private readonly Dictionary<string, GameObject> cloneVisuals = new Dictionary<string, GameObject>();
    // ▼【新規追加】剣本体(syncTargets)と同様に、分身・シールドの見た目も毎フレーム補間で追従させるための目標値
    private readonly Dictionary<string, MultiplayerCloneState> cloneSyncTargets = new Dictionary<string, MultiplayerCloneState>();
    // ▼【新規追加】BattleCameraはマルチプレイ中は無効化しているため、代わりにこのマネージャー自身の
    // LateUpdate()でシェイク量を消費する（TriggerShakeはローカルのBattleCamera.TriggerShakeと同じ役割）
    private float shakeDuration, shakeMagnitude;
    private Vector3 lockedCameraPosition;
    private bool hasLockedCameraPosition;
    // ▼【新規追加】ダメージを伴わない柄迫り合い・壁バウンドの演出をSYNCでゲストにも伝えるための単調増加カウンタ
    private readonly Dictionary<string, int> clashSeq = new Dictionary<string, int>();
    // ▼【新規追加】QueueHitで受け取ったクリティカル/弱点情報を、同一tick内でApplyMultiplayerHealthへ渡すための一時置き場
    private readonly Dictionary<string, bool> criticalHitThisTick = new Dictionary<string, bool>();
    // ▼【新規追加】↑と同じクリティカル情報を、clashSeqと同じ単調増加カウンタ方式でSYNC経由でもゲストへ伝える
    private readonly Dictionary<string, int> critSeq = new Dictionary<string, int>();
    // ▼【修正】シーンに元々ある PL1Bar〜PL4Bar（HP赤/緑二重ゲージ・名前・SPバー）とカットイン・必殺技ボタンが
    // 乗っているCanvas。以前は多人数対応時に全Canvasを無効化していたため、これらが丸ごと表示されなくなっていた。
    // このCanvasだけは無効化対象から除外し、各プレイヤーのUI要素をここへ配線して使う
    private Canvas hudCanvas;
    // ▼【新規追加】Reactなど外部UI側ではカウントダウンを表示しないため、SceneController側の
    // countdownText（ローカルデモと共用のTMP）をマルチプレイ中もそのまま使って表示する
    private TMPro.TextMeshProUGUI countdownText;
    private string countdownUiPhase = "IDLE"; // countdownText自体の表示状態(前フレームのphase)の追跡用
    private float countdownGoHideAt;
    private string phase = "IDLE";
    private int physicsTick, syncTick, receivedTick = -1;
    private float countdownEnd, nextSync, nextTargetUpdate;
    private float receivedCountdownRemaining;
    private SimulationMode2D previousSimulationMode;
    private bool ownsSimulation;
    private BattleCamera battleCamera;
    // ▼【新規追加】残機モード関連。livesModeActiveはconfig.livesModeのコピーで、脱落時に次の剣へ
    // 持ち替えて延命する(=MatchRulesに複数本ぶんのHPを渡す)かどうかだけを左右する。
    // ownedSwordsByPlayerは各playerの手持ちの剣(現在装備中の剣から並び順)そのもので、残機モードの
    // ON/OFFに関わらず常に構築する(分身系必殺技の見た目バリエーションはモードを問わず使うため)。
    // spawnPositionByPlayerは持ち替え時に復帰させるスポーン座標、lifeIndexByPlayerはHost/Client双方で
    // 「今使っている剣が手持ちの何番目か」を覚えておくための追跡用(残機モードでなければ常に0のまま)
    private bool livesModeActive;
    public bool LivesModeActive => livesModeActive;
    // 1vs3。boss(team 0)が1人、trio(team 1)が3人。有効な間だけ陣営と強化を使う。
    public const int BossTeam = 0;
    public const int TrioTeam = 1;
    public const int SoloModePlayers = 4;
    private bool soloModeActive;
    public bool SoloModeActive => soloModeActive;
    // 制圧が解ける時刻(Time.unscaledTime)。Hostは発動時に書き込み、ゲストはSYNCの残り秒数から
    // 同じ形に復元するので、IsSuppressed() は両方で同じように使える。
    private readonly Dictionary<string, float> suppressUntil = new Dictionary<string, float>();
    // 制圧中の頭上表示。
    private readonly Dictionary<string, TextMeshPro> suppressLabels = new Dictionary<string, TextMeshPro>();
    // 1vs3 専用HUD。ボスを上部中央に大きく、トリオ3人を下部に横並びで出す。
    // シーンには触らず実行時に組み立て、StopCurrent() で破棄して元のHUDへ戻す。
    [Header("1vs3 HUD（ボスを上に大きく、トリオ3人を下に横並び）")]
    [Tooltip("斜めの深さ(px)。0で長方形。左右の端が同じ向きに傾いた平行四辺形になる")]
    [SerializeField] float hudSlant = 18f;
    [SerializeField] Vector2 bossPanelSize = new Vector2(760, 132);
    [Tooltip("画面上端からボスパネルまでの距離")]
    [SerializeField] float bossPanelTop = 16f;
    [SerializeField] Color bossPanelColor = new Color(.16f, .06f, .24f, .82f);
    [SerializeField] Vector2 trioPanelSize = new Vector2(300, 96);
    [Tooltip("トリオパネル同士の間隔")]
    [SerializeField] float trioPanelGap = 18f;
    [Tooltip("画面下端からトリオパネルまでの距離")]
    [SerializeField] float trioPanelBottom = 16f;
    [SerializeField] Color trioPanelColor = new Color(.07f, .09f, .14f, .82f);

    [Header("1vs3 HUD の文字サイズ")]
    [Tooltip("ボスの名前（👑＋剣名）")]
    [SerializeField] float bossNameFontSize = 30f;
    [Tooltip("ボスのHPの数値")]
    [SerializeField] float bossHpFontSize = 20f;
    [Tooltip("ボスのSPの数値")]
    [SerializeField] float bossSpFontSize = 18f;
    [Tooltip("トリオの名前（「2P 剣名」）")]
    [SerializeField] float trioNameFontSize = 20f;
    [Tooltip("トリオのHPの数値と、状態表示（あなた／操作不能／撃破）")]
    [SerializeField] float trioValueFontSize = 16f;

    private GameObject soloHudRoot;
    private Sprite slantSprite;
    private readonly List<GameObject> hiddenSceneBars = new List<GameObject>();
    private readonly Dictionary<string, TextMeshProUGUI> soloHudStates = new Dictionary<string, TextMeshProUGUI>();
    private Image bossSpFill;
    // 制圧の対象領域を示す輪。狙いを付けるための下見(ボス本人のみ)と、撃った瞬間の明滅(全員)に使う。
    private SpriteRenderer suppressRange;
    private Sprite suppressRangeSprite;
    private string bossPlayerId;
    // ゲストは「誰かが制圧され始めた」瞬間を検知して明滅を出す。専用の同期項目を足さずに済ませるため。
    private readonly HashSet<string> wasSuppressed = new HashSet<string>();
    // 掌握の2段目（引き寄せ → 薙ぎ払い）の進行状態。対象は発動時に確定し、途中で増減しない。
    private readonly List<string> judgmentTargets = new List<string>();
    private Vector3 judgmentCentre;
    private float judgmentStrikeAt;
    private bool judgmentPending;
    // 吸引中だけ重力を切るので、元の値を預かっておく
    private readonly Dictionary<string, float> judgmentGravity = new Dictionary<string, float>();
    // 輪の演出の起点。Hostは発動時、ゲストは同期で拘束が始まった瞬間に入れる。
    // どちらも soloBuff の溜め時間を持っているので、同じ時間割で動かせる。
    private float judgmentVisualStart = -999f;
    const float JudgmentBurstSeconds = 0.35f;

    void RestoreJudgmentGravity()
    {
        foreach (var pair in judgmentGravity)
            if (bodies.ContainsKey(pair.Key)) bodies[pair.Key].gravityScale = pair.Value;
        judgmentGravity.Clear();
    }
    private Dictionary<string, List<SwordSlotData>> ownedSwordsByPlayer;
    private Dictionary<string, Vector3> spawnPositionByPlayer;
    private readonly Dictionary<string, int> lifeIndexByPlayer = new Dictionary<string, int>();
    // ▼【新規追加】分身系必殺技(オレ達アタック/オレ達シールド)が「装備していない手持ちの剣」の
    // 見た目を使えるよう、各playerの手持ちの剣の画像から一度だけ生成したSpriteをキャッシュしておく
    private readonly Dictionary<string, Sprite[]> lifeSpriteCache = new Dictionary<string, Sprite[]>();

    void Start() { Emit("READY", new MultiplayerCommand()); }

    [UnityEngine.Scripting.Preserve]
    public void InitializeMultiplayer(string json)
    {
        var next = JsonUtility.FromJson<MultiplayerConfig>(json);
        if (next == null || string.IsNullOrEmpty(next.matchId)) return;
        if (config != null && config.matchId == next.matchId) return;
        StopCurrent();
        config = next;
        IsHost = config.isHost;
        phase = "LOADING";
        try
        {
            ValidateConfig();
            var scene = GetComponent<SceneController>();
            var network = GetComponent<NetworkManager>();
            if (scene == null || network == null || network.playerSwords.Length < 2 || network.playerSwords[0] == null || scene.hostGenerator == null)
                throw new InvalidOperationException("Missing sword template in scene.");
            // ▼【修正】autoTestOnStart(ローカルデモ)が先に走っていた場合、CountdownCameraRoutineを
            // 道連れで止めるとcountdownTextが"3"のまま誰にも更新されず残ってしまっていた。
            // マルチプレイ自身のCOUNTDOWNフェーズでこのテキストを引き継いで表示するので、ここでは
            // 古いローカルデモの進行だけを止める（テキスト自体はUpdate()側で生きたまま更新する）
            scene.StopAllCoroutines();
            scene.autoTestOnStart = false;
            // ▼【修正】autoTestOnStart(ローカルデモ)がp3HudTemplate/p4HudTemplate未設定のまま
            // 3・4人目を動的生成すると、WireClonedHudがHPバー・名前表示などを複製してしまう。
            // このクローンはStartBattle()が再度呼ばれない限り自分では破棄されないため、
            // 本番のマルチプレイ対戦がそのまま始まると「消したはずの名前表示等が残って見える」
            // 不具合の原因になっていた。マルチプレイ開始時に必ず一度破棄しておく
            scene.ClearDynamicObjects();
            countdownText = scene.countdownText;
            countdownUiPhase = "IDLE";
            if (countdownText != null) countdownText.gameObject.SetActive(false);
            network.enabled = false;
            network.isHost = IsHost;
            scene.hostGenerator.generateOnStart = false;
            scene.clientGenerator.generateOnStart = false;
            // ▼【修正】PL1〜PL4Bar・必殺技ボタン・カットインが乗っているCanvasだけは無効化しない。
            // 以前は無条件に全Canvasを無効化しており、これらが丸ごと表示されなくなっていた
            var templateBattle = network.playerSwords[0].GetComponent<SwordBattle>();
            hudCanvas = templateBattle != null && templateBattle.specialAttackButton != null
                ? templateBattle.specialAttackButton.GetComponentInParent<Canvas>() : null;
            // ▼【重要】playerSwords[0]/[1]（旧1v1用テンプレート）自身も、シーンロード時に自分のTryUltimateを
            // この共有ボタンへリスナー登録済み。Canvasを無効化しなくなった今、isLocalControlledをfalseにして
            // TryUltimate側の既存ガード（自分の操作キャラでなければ即return）で無害化しておく
            // ▼【修正】0/1だけでなく2/3も同様に処理する。SceneController.autoTestOnStartが有効なまま
            // 4人用のdebugBattleJsonFileでベースのオートテストが一瞬先に走ると、3・4人目用に動的生成された
            // 剣(playerSwords[2]/[3])が非表示にされず残り、MultiplayerManagerが新しく生成する3・4人目の剣と
            // 同じスポーン座標に重なって表示されてしまっていた
            for (int i = 0; i < network.playerSwords.Length; i++)
            {
                var templateSword = network.playerSwords[i];
                if (templateSword == null) continue;
                var templateController = templateSword.GetComponent<SwordController>();
                if (templateController != null) templateController.isLocalControlled = false;
                templateSword.SetActive(false);
            }
            SwordController.isKomaMode = config.gameMode == "1";
            // ▼【修正】以前は両方とも非表示にして、コードで生成した簡易な壁4枚だけのアリーナに差し替えていたが、
            // それだと背景美術も、SceneController側で校正済みの3〜4人用スポーン座標の前提となる床の高さ等も失われていた。
            // ゲームモードに応じた本来のステージ（床・壁のコライダー込み）をそのまま使う
            // ▼【新規追加】剣モードは2人用(SwordStage)と3・4人用(SwordStage_3)でステージを使い分ける
            bool use3PSwordStage = !SwordController.isKomaMode && config.players.Length >= 3;
            if (network.swordStage != null) network.swordStage.SetActive(!SwordController.isKomaMode && !use3PSwordStage);
            if (network.swordStage3P != null) network.swordStage3P.SetActive(use3PSwordStage);
            if (network.komaStage != null) network.komaStage.SetActive(SwordController.isKomaMode);
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas != hudCanvas) canvas.enabled = false;
            // ▼【新規追加】autoTestOnStart(ローカルデモ)や前回の対戦でPL3Bar/PL4Barが表示状態のまま
            // 残っていることがあるため、SceneController.StartBattle()と同じく今回の対戦人数に合わせて
            // 明示的に表示/非表示を揃える(これが無いと2〜3人戦でも4人分のHPバーが残って見えてしまう)
            SceneController.SetHudTemplateVisible(scene.p3HudTemplate, config.players.Length >= 3);
            SceneController.SetHudTemplateVisible(scene.p4HudTemplate, config.players.Length >= 4);
            // ▼【修正】enabled=falseで止めるだけだと、ちょうどチュートリアルの文字が流れている
            // 最中(黒い帯が表示中)だった場合にUpdate()が二度と回らなくなり、帯を隠す処理
            // (UpdateBarVisibility)も一緒に止まってしまい、帯が表示されたまま残ってしまっていた。
            // 止める前に必ず明示的に非表示にしてから無効化する
            foreach (var tutorial in FindObjectsByType<TutorialManager>(FindObjectsSortMode.None))
            {
                tutorial.ForceHideAndStop();
                tutorial.enabled = false;
            }
            if (BackgroundManager.Instance != null) BackgroundManager.Instance.enabled = false;
            if (CutinManager.Instance != null) CutinManager.Instance.StopAllCoroutines();
            Time.timeScale = 1;
            SwordBattle.isRoundStarted = false;
            SwordBattle.matchEnded = false;
            battleCamera = Camera.main == null ? null : Camera.main.GetComponent<BattleCamera>();
            if (battleCamera != null) battleCamera.enabled = false;

            previousSimulationMode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.Script;
            ownsSimulation = true;
            arena = new GameObject("MultiplayerArena");
            // ▼【新規追加】各playerの手持ちの剣(現在装備中の剣→残りの順)を先に確定させる。これは分身系
            // 必殺技の見た目バリエーションに使うため残機モードのON/OFFに関わらず常に構築する。
            // MatchRulesへ渡すHPの並びだけは残機モードの時だけ複数本ぶん、そうでなければ従来通り1本分のみ
            livesModeActive = config.livesMode;
            soloModeActive = config.soloMode;
            bossPlayerId = soloModeActive
                ? config.players.First(p => p.team == BossTeam).playerId : null;
            ownedSwordsByPlayer = config.players.ToDictionary(p => p.playerId, p => BuildOwnedSwords(p.swordData));
            lifeIndexByPlayer.Clear();
            foreach (var p in config.players) lifeIndexByPlayer[p.playerId] = 0;
            rules = new MatchRules(config.players.Select(p => p.playerId).ToArray(),
                config.players.Select(p => livesModeActive
                    ? ownedSwordsByPlayer[p.playerId].Select(l => BuffedHp(p, l.hp)).ToArray()
                    : new[] { BuffedHp(p, p.swordData.hp) }).ToArray(),
                // 陣営を渡すのは1vs3の時だけ。渡さなければ MatchRules は従来どおりの個人戦として動く。
                soloModeActive ? config.players.Select(p => p.team).ToArray() : null);
            // ▼ 壁も含めて自作していた即席アリーナをやめ、SceneController側の校正済み座標(GetSpawnPositions)を使う
            Vector3[] spawnPositions = scene.GetSpawnPositions(config.players.Length, soloModeActive);
            spawnPositionByPlayer = config.players.ToDictionary(p => p.playerId, p => spawnPositions[p.spawnIndex]);
            // ▼【修正】刀身の太さは、ローカル(3・4人目の動的生成)と同じくSceneController.generators[0]の値を基準にする。
            // 以前はhostGeneratorという別枠の値を使っており、Inspector設定次第でローカルとサイズがズレていた
            float bladeWidth = scene.generators != null && scene.generators.Length > 0 && scene.generators[0] != null
                ? scene.generators[0].targetBladeWidth : scene.hostGenerator.targetBladeWidth;
            foreach (var player in config.players) CreateSword(player, network.playerSwords[0], bladeWidth, spawnPositions[player.spawnIndex]);
            // 1vs3 は「1人 対 3人」の構図が伝わる専用HUDに差し替える。剣を作り終えてから配線する。
            if (soloModeActive) BuildSoloHud();
            physicsTick = 0; syncTick = 0; receivedTick = -1; nextSync = 0; nextTargetUpdate = 0;
            StartCoroutine(CompleteInitialization());
        }
        catch (Exception e)
        {
            Debug.LogError("Multiplayer initialization failed: " + e);
            string matchId = config.matchId;
            StopCurrent();
            Emit("LOAD_FAILED", new MultiplayerCommand { matchId = matchId });
        }
    }

    void ValidateConfig()
    {
        if (config.players == null || config.players.Length < 2 || config.players.Length > 4 || !new[] { "0", "1" }.Contains(config.gameMode) ||
            !config.players.Any(p => p != null && p.playerId == config.localPlayerId) ||
            config.players.Any(p => p == null || p.swordData == null || string.IsNullOrEmpty(p.playerId)) ||
            config.players.Select(p => p.playerId).Distinct().Count() != config.players.Length ||
            config.players.Select(p => p.slotIndex).OrderBy(i => i).Where((slot, i) => slot != i).Any() ||
            config.players.Select(p => p.spawnIndex).OrderBy(i => i).Where((slot, i) => slot != i).Any())
            throw new ArgumentException("Invalid multiplayer roster.");
        ValidateTeams();
        config.players = config.players.OrderBy(p => p.slotIndex).ToArray();
    }

    // 1vs3の編成を確かめる。ここを通さないと、陣営が壊れたまま試合が始まって
    // 味方に攻撃が通る／決着が付かない、といった状態になりうる。
    void ValidateTeams()
    {
        if (!config.soloMode)
        {
            // 通常の試合では陣営を使わない。全員0で来るはずで、そうでなければ設定が壊れている。
            if (config.players.Any(p => p.team != BossTeam))
                throw new ArgumentException("A normal match must not carry teams.");
            return;
        }
        if (config.players.Length != SoloModePlayers || config.soloBuff == null ||
            config.players.Any(p => p.team != BossTeam && p.team != TrioTeam) ||
            config.players.Count(p => p.team == BossTeam) != 1 ||
            config.players.Count(p => p.team == TrioTeam) != SoloModePlayers - 1)
            throw new ArgumentException("A solo match requires one boss and three opponents.");
        var buff = config.soloBuff;
        if (!IsUsableMultiplier(buff.hpMultiplier) || !IsUsableMultiplier(buff.attackMultiplier) ||
            !IsUsableMultiplier(buff.spGainMultiplier) || !IsUsableMultiplier(buff.maxSp) ||
            !IsUsableMultiplier(buff.suppressRadius) || !IsUsableMultiplier(buff.suppressDuration) ||
            !IsUsableMultiplier(buff.judgmentPullSeconds) || !IsUsableMultiplier(buff.judgmentPullForce) ||
            !IsUsableMultiplier(buff.judgmentDamageMultiplier) || !IsUsableMultiplier(buff.judgmentKnockback) ||
            !IsUsableMultiplier(buff.judgmentSweepSeconds) || !IsUsableMultiplier(buff.judgmentSweepSpin) ||
            !IsUsableMultiplier(buff.judgmentSweepScale))
            throw new ArgumentException("The solo buff carries an unusable value.");
    }

    static bool IsUsableMultiplier(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    bool IsBoss(MultiplayerPlayerConfig player) { return soloModeActive && player.team == BossTeam; }

    // ボスのHPだけを差し替えた複製を返す。元の設定は他の用途(控えの剣の絵など)でも使うので壊さない。
    SwordData BuffedSwordData(MultiplayerPlayerConfig player)
    {
        if (!IsBoss(player)) return player.swordData;
        var copy = JsonUtility.FromJson<SwordData>(JsonUtility.ToJson(player.swordData));
        copy.hp = BuffedHp(player, copy.hp);
        return copy;
    }

    // 制圧の対象領域。半径いっぱいの輪を描いて「どこまで届くか」を見せる。
    // ・下見（ボス本人だけ）：ゲージが満タンで撃てる間、自分の周りに薄く出し続ける
    // ・明滅（全員）：撃った瞬間に濃く出す。判定は発動時の一度きりなので、輪はその場に置いたまま動かさない
    void UpdateSuppressRange()
    {
        if (!soloModeActive || config.soloBuff == null) { HideSuppressRange(); return; }
        var buff = config.soloBuff;
        float elapsed = Time.unscaledTime - judgmentVisualStart;
        bool pulling = elapsed >= 0f && elapsed < buff.judgmentPullSeconds;
        bool bursting = elapsed >= buff.judgmentPullSeconds &&
            elapsed < buff.judgmentPullSeconds + JudgmentBurstSeconds;

        SwordBattle boss = bossPlayerId != null && swords.ContainsKey(bossPlayerId) ? swords[bossPlayerId] : null;
        bool aiming = !pulling && !bursting && boss != null && IsPlaying && boss.IsAlive &&
            boss.PlayerId == config.localPlayerId && boss.currentSp >= boss.maxSp && !IsSuppressed(boss.PlayerId);
        if (!pulling && !bursting && !aiming) { HideSuppressRange(); return; }

        if (suppressRange == null) BuildSuppressRange();
        if (suppressRange == null) return;

        float diameter;
        float alpha;
        if (pulling)
        {
            // 輪が中心へすぼまっていく。これが「吸い込まれている」ことの一番の手がかりになる
            float t = elapsed / buff.judgmentPullSeconds;
            diameter = Mathf.Lerp(buff.suppressRadius * 2f, buff.suppressRadius * 0.35f, t * t);
            alpha = Mathf.Lerp(0.35f, 0.95f, t);
        }
        else if (bursting)
        {
            // 薙ぎ払いの瞬間に一気に広がって消える
            float t = (elapsed - buff.judgmentPullSeconds) / JudgmentBurstSeconds;
            diameter = Mathf.Lerp(buff.suppressRadius * 0.35f, buff.suppressRadius * 2.6f, t);
            alpha = Mathf.Lerp(0.95f, 0f, t);
        }
        else
        {
            diameter = buff.suppressRadius * 2f;
            alpha = 0.28f;
        }

        Vector3 centre = aiming ? boss.currentCenterPosition : judgmentCentre;
        centre.z = 0.5f;   // 剣より奥に置いて、輪が剣を隠さないようにする
        suppressRange.transform.position = centre;
        // スプライトは直径1として作ってあるので、拡大率がそのまま直径になる
        suppressRange.transform.localScale = Vector3.one * diameter;
        suppressRange.color = new Color(.78f, .45f, 1f, alpha);
        if (!suppressRange.gameObject.activeSelf) suppressRange.gameObject.SetActive(true);
    }

    void HideSuppressRange()
    {
        if (suppressRange != null && suppressRange.gameObject.activeSelf) suppressRange.gameObject.SetActive(false);
    }

    // 輪の画像をその場で作る。プロジェクトの素材には手を入れず、StopCurrent() で破棄する。
    void BuildSuppressRange()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[size * size];
        float half = size / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // 中心からの距離を0〜1に直し、縁だけを濃く、内側はうっすら塗る
            float distance = Mathf.Sqrt((x + .5f - half) * (x + .5f - half) + (y + .5f - half) * (y + .5f - half)) / half;
            byte alpha = distance > 1f ? (byte)0 : distance > 0.93f ? (byte)255 : (byte)40;
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        // pixelsPerUnit を size にすると、拡大率1のときちょうど直径1ユニットになる
        suppressRangeSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);

        var obj = new GameObject("SuppressRange");
        obj.transform.SetParent(arena != null ? arena.transform : null, false);
        suppressRange = obj.AddComponent<SpriteRenderer>();
        suppressRange.sprite = suppressRangeSprite;
        obj.SetActive(false);
    }

    // 制圧されている相手の頭上に残り秒数を出す。ホストもゲストも suppressUntil を持っているので
    // 同じ表示になる。表示は剣ごとに1つ作って使い回し、StopCurrent() でまとめて消える。
    void UpdateSuppressLabels()
    {
        if (!soloModeActive) return;
        foreach (var pair in swords)
        {
            float remaining = SuppressRemaining(pair.Key);
            TextMeshPro label;
            if (!suppressLabels.TryGetValue(pair.Key, out label) || label == null)
            {
                if (remaining <= 0f) continue;
                label = new GameObject("SuppressLabel").AddComponent<TextMeshPro>();
                label.transform.SetParent(pair.Value.transform, false);
                label.transform.localPosition = new Vector3(0, 3.2f, -0.2f);
                label.fontSize = 5; label.alignment = TextAlignmentOptions.Center;
                label.color = new Color(.78f, .45f, 1f);
                if (pair.Value.nameText != null) label.font = pair.Value.nameText.font;
                suppressLabels[pair.Key] = label;
            }
            bool show = remaining > 0f && pair.Value.IsAlive;
            if (show) label.text = "操作不能 " + Mathf.CeilToInt(remaining);
            if (label.gameObject.activeSelf != show) label.gameObject.SetActive(show);
        }
    }

    // ===== 1vs3 専用HUD =====
    // 対称戦の4枠並びでは「誰がボスか」も「ボスがいつ掌握斬を撃てるか」も伝わらない。
    // ボスを上部中央に大きく1本（全員が見る共通の目標）、トリオ3人を下部に横並びにして、
    // 画面の配置そのもので1対3の構図を示す。
    void BuildSoloHud()
    {
        var font = FindHudFont();
        HideSceneHudBars();

        soloHudRoot = new GameObject("SoloHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = soloHudRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = soloHudRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var bossConfig = config.players.First(p => p.team == BossTeam);
        BuildBossPanel(swords[bossConfig.playerId], font);

        var trio = config.players.Where(p => p.team == TrioTeam).OrderBy(p => p.slotIndex).ToArray();
        for (int i = 0; i < trio.Length; i++) BuildTrioPanel(swords[trio[i].playerId], trio[i].slotIndex, i, trio.Length, font);
    }

    // 既存のPL1Bar〜PL4Barは1vs3の間だけ隠す。試合が終わったら必ず戻す（シーンは触らない）。
    void HideSceneHudBars()
    {
        hiddenSceneBars.Clear();
        if (hudCanvas == null) return;
        for (int slot = 1; slot <= 4; slot++)
        {
            var bar = hudCanvas.transform.Find("PL" + slot + "Bar");
            if (bar == null || !bar.gameObject.activeSelf) continue;
            bar.gameObject.SetActive(false);
            hiddenSceneBars.Add(bar.gameObject);
        }
    }

    // 日本語が出るフォントをシーンから借りる。TMPの既定フォントには日本語の字形が無い。
    TMP_FontAsset FindHudFont()
    {
        if (countdownText != null) return countdownText.font;
        foreach (var battle in swords.Values) if (battle.nameText != null) return battle.nameText.font;
        return null;
    }

    void BuildBossPanel(SwordBattle boss, TMP_FontAsset font)
    {
        var panel = NewPanel("BossPanel", new Vector2(.5f, 1f), new Vector2(0, -bossPanelTop),
            bossPanelSize, bossPanelColor);
        // バーの幅はパネルの幅に追従させる。斜めの縁に文字やバーが乗り上げないよう左右を空ける。
        float inner = bossPanelSize.x - 40f - hudSlant;
        // 行の位置は上から順に積む。文字を大きくしても下の行が押し下がるだけで重ならない。
        float nameHeight = bossNameFontSize * 1.35f;
        float hpHeight = Mathf.Max(bossHpFontSize * 1.5f, 24f);
        float spHeight = Mathf.Max(bossSpFontSize * 1.4f, 20f);
        float y = -8f;
        var crown = NewLabel(panel, "Name", font, bossNameFontSize, TextAlignmentOptions.Center,
            new Vector2(0, y), new Vector2(inner, nameHeight));
        crown.color = new Color(.85f, .62f, 1f);
        y -= nameHeight + 6f;

        var hp = NewBar(panel, "Hp", new Vector2(0, y), new Vector2(inner, hpHeight),
            new Color(.85f, .16f, .16f), new Color(.10f, .10f, .12f, .9f));
        var hpText = NewLabel(panel, "HpText", font, bossHpFontSize, TextAlignmentOptions.Right,
            new Vector2(-12, y), new Vector2(inner - 20, hpHeight));
        y -= hpHeight + 8f;

        var sp = NewBar(panel, "Sp", new Vector2(0, y), new Vector2(inner, spHeight),
            new Color(.35f, .75f, 1f), new Color(.10f, .10f, .12f, .9f));
        var spText = NewLabel(panel, "SpText", font, bossSpFontSize, TextAlignmentOptions.Right,
            new Vector2(-12, y), new Vector2(inner - 20, spHeight));
        // パネルの高さは中身に合わせて伸ばす。指定値より中身が大きい時だけ広げる。
        panel.sizeDelta = new Vector2(bossPanelSize.x, Mathf.Max(bossPanelSize.y, -y + spHeight + 8f));
        // 100の位置に区切り線。ゲージが2段階（100＝通常必殺 / 200＝掌握斬）であることを示す。
        float tick = boss.maxSp > 0 ? boss.ultimateSp / boss.maxSp : .5f;
        var line = NewImage(sp.transform as RectTransform, "UltimateTick", new Color(1, 1, 1, .85f));
        line.rectTransform.anchorMin = new Vector2(tick, 0f);
        line.rectTransform.anchorMax = new Vector2(tick, 1f);
        line.rectTransform.sizeDelta = new Vector2(3, 0);
        line.rectTransform.anchoredPosition = Vector2.zero;

        boss.nameText = crown;
        boss.hpBar = hp.GetComponent<Slider>();
        boss.hpText = hpText;
        boss.spGaugeBar = sp.GetComponent<Slider>();
        boss.spText = spText;
        boss.delayHpBar = null;
        bossSpFill = sp.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "Fill");
        boss.UpdateUI();
        crown.text = "\U0001F451 " + boss.swordName;
    }

    void BuildTrioPanel(SwordBattle battle, int slotIndex, int index, int count, TMP_FontAsset font)
    {
        float width = trioPanelSize.x, gap = trioPanelGap;
        float span = count * width + (count - 1) * gap;
        float x = -span / 2f + width / 2f + index * (width + gap);
        var panel = NewPanel("TrioPanel" + slotIndex, new Vector2(.5f, 0f), new Vector2(x, trioPanelBottom),
            trioPanelSize, trioPanelColor);
        float inner = width - 24f - hudSlant;

        var name = NewLabel(panel, "Name", font, trioNameFontSize, TextAlignmentOptions.Left,
            new Vector2(hudSlant / 2f, -8), new Vector2(inner, 26));
        name.color = SwordBattle.PlayerColors[Mathf.Clamp(slotIndex, 0, 3)];
        name.text = (slotIndex + 1) + "P " + battle.swordName;

        var hp = NewBar(panel, "Hp", new Vector2(0, -42), new Vector2(inner, 24),
            new Color(.3f, .8f, .35f), new Color(.10f, .10f, .12f, .9f));
        var hpText = NewLabel(panel, "HpText", font, trioValueFontSize, TextAlignmentOptions.Right,
            new Vector2(-14, -42), new Vector2(inner - 16, 22));

        var state = NewLabel(panel, "State", font, trioValueFontSize, TextAlignmentOptions.Center,
            new Vector2(0, -74), new Vector2(inner, 22));
        state.color = new Color(.85f, .62f, 1f);
        soloHudStates[battle.PlayerId] = state;

        battle.nameText = name; battle.hpBar = hp.GetComponent<Slider>(); battle.hpText = hpText;
        battle.spGaugeBar = null; battle.spText = null; battle.delayHpBar = null;
        battle.UpdateUI();
        name.text = (slotIndex + 1) + "P " + battle.swordName;
    }

    // 毎フレームの差分。自分がどれか、誰が掌握斬で固められているか、ボスが撃てる状態か。
    void UpdateSoloHud()
    {
        if (soloHudRoot == null) return;
        foreach (var pair in soloHudStates)
        {
            var battle = swords.ContainsKey(pair.Key) ? swords[pair.Key] : null;
            float remaining = SuppressRemaining(pair.Key);
            pair.Value.text = battle == null || !battle.IsAlive ? "撃破"
                : remaining > 0f ? "操作不能 " + Mathf.CeilToInt(remaining)
                : pair.Key == config.localPlayerId ? "あなた" : string.Empty;
        }
        // ボスのゲージが満タン＝掌握斬が来る。トリオが散開を判断できるよう、全員の画面で点滅させる。
        if (bossSpFill == null || bossPlayerId == null || !swords.ContainsKey(bossPlayerId)) return;
        var boss = swords[bossPlayerId];
        bool ready = boss.IsAlive && boss.currentSp >= boss.maxSp;
        bossSpFill.color = ready
            ? Color.Lerp(new Color(.78f, .45f, 1f), Color.white, Mathf.PingPong(Time.unscaledTime * 3f, 1f))
            : new Color(.35f, .75f, 1f);
    }

    // ----- 以下は組み立て用の小道具 -----

    RectTransform NewPanel(string name, Vector2 anchor, Vector2 offset, Vector2 size, Color color)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(soloHudRoot.transform, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(.5f, anchor.y);
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;
        var image = obj.GetComponent<Image>();
        image.color = color;
        ApplySlant(image);
        return rect;
    }

    // 平行四辺形の画像をその場で作る。左右の端だけが斜めで、真ん中は真っ直ぐ。
    // 9スライス（border を左右だけ持たせる）にしてあるので、バーが伸び縮みしても
    // 斜めの角度は変わらず、真ん中だけが伸びる。
    Sprite SlantSprite()
    {
        if (slantSprite != null) return slantSprite;
        int slant = Mathf.Max(0, Mathf.RoundToInt(hudSlant));
        const int height = 64;
        int width = slant * 2 + 4;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            float t = (y + .5f) / height;
            float left = slant * t;
            float right = width - slant * (1f - t);
            for (int x = 0; x < width; x++)
            {
                // 斜めの縁が階段状にならないよう、1px ぶんでなめらかに抜く
                float cx = x + .5f;
                float coverage = Mathf.Clamp01(cx - left + .5f) * Mathf.Clamp01(right - cx + .5f);
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        slantSprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 100f,
            0, SpriteMeshType.FullRect, new Vector4(slant, 0, slant, 0));
        return slantSprite;
    }

    void ApplySlant(Image image)
    {
        if (hudSlant <= 0f) return;
        image.sprite = SlantSprite();
        image.type = Image.Type.Sliced;
        image.fillCenter = true;
    }

    static Image NewImage(RectTransform parent, string name, Color color)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        var image = obj.GetComponent<Image>();
        image.color = color;
        return image;
    }

    // 背景と塗りを持つスライダー。既存のHPバーと同じく SwordBattle.UpdateUI() が値を書き込む。
    GameObject NewBar(RectTransform parent, string name, Vector2 offset, Vector2 size, Color fill, Color back)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Slider));
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;
        var backgroundImage = NewImage(rect, "Background", back);
        var fillImage = NewImage(rect, "Fill", fill);
        ApplySlant(backgroundImage); ApplySlant(fillImage);
        var background = backgroundImage;
        var fillRect = fillImage.rectTransform;
        var slider = obj.GetComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.interactable = false;
        slider.targetGraphic = background;
        slider.fillRect = fillRect;
        slider.minValue = 0;
        return obj;
    }

    static TextMeshProUGUI NewLabel(RectTransform parent, string name, TMP_FontAsset font, float size,
        TextAlignmentOptions alignment, Vector2 offset, Vector2 rectSize)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        var label = obj.AddComponent<TextMeshProUGUI>();
        var rect = label.rectTransform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.sizeDelta = rectSize;
        rect.anchoredPosition = offset;
        if (font != null) label.font = font;
        label.fontSize = size;
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }

    // ターゲット選定と制圧の範囲判定で共通に使う名簿。
    // 陣営を渡すのは1vs3の時だけ。渡さなければ TargetCandidate は「1人が1チーム」として
    // スロット番号を陣営に使うので、従来どおり自分以外の全員が候補になる。
    TargetCandidate[] TargetCandidates()
    {
        return config.players.Select(p => soloModeActive
            ? new TargetCandidate(p.playerId, p.slotIndex, swords[p.playerId].transform.position.x,
                swords[p.playerId].transform.position.y, swords[p.playerId].IsAlive, p.team)
            : new TargetCandidate(p.playerId, p.slotIndex, swords[p.playerId].transform.position.x,
                swords[p.playerId].transform.position.y, swords[p.playerId].IsAlive)).ToArray();
    }

    // 制圧を受けている間はどの入力も通らない。ゲストも同じ判定を使えるよう、
    // 解ける時刻を両側で持っている。
    public bool IsSuppressed(string playerId)
    {
        float until;
        return playerId != null && suppressUntil.TryGetValue(playerId, out until) && Time.unscaledTime < until;
    }

    public float SuppressRemaining(string playerId)
    {
        float until;
        if (playerId == null || !suppressUntil.TryGetValue(playerId, out until)) return 0f;
        return Mathf.Max(0f, until - Time.unscaledTime);
    }

    // ボスが制圧を撃った。範囲判定は発動の瞬間に一度だけ行い、以降どちらが動いても対象は変わらない。
    public void ApplySuppress(string bossId)
    {
        if (!IsHost || !IsPlaying || !soloModeActive || !swords.ContainsKey(bossId)) return;
        var boss = swords[bossId];
        bool isBoss = config.players.Any(p => p.playerId == bossId && p.team == BossTeam);
        if (!BattlePolicies.CanSuppress(boss.currentSp, boss.maxSp, IsPlaying, boss.IsAlive,
            boss.isDashing, IsSuppressed(bossId), isBoss)) return;

        var centre = boss.currentCenterPosition;
        var caught = BattlePolicies.SuppressTargets(bossId, centre.x, centre.y,
            config.soloBuff.suppressRadius, TargetCandidates());
        boss.ConsumeSuppressSp();
        // カットインは技番号(dashType)の同期に乗るので、ここでHost側が立てれば全員の画面に出る
        boss.PlaySuppressCutin();
        float until = Time.unscaledTime + config.soloBuff.suppressDuration;
        foreach (var id in caught) suppressUntil[id] = until;
        // 輪は撃った場所に置いたままにする。判定が発動時の一度きりであることを見た目でも示すため。
        StartJudgmentVisual(centre);
        RefreshSuppression();

        // ここから2段目。捕らえた相手を撃った場所へ引き寄せ、溜めが終わったら薙ぎ払う。
        judgmentTargets.Clear();
        judgmentTargets.AddRange(caught);
        judgmentCentre = centre;
        judgmentStrikeAt = Time.unscaledTime + config.soloBuff.judgmentPullSeconds;
        judgmentPending = judgmentTargets.Count > 0;
    }

    // 掌握の2段目。引き寄せ中は毎ステップ力を加え、溜めが終わった瞬間に一度だけ斬る。
    // Physics2D.Simulate の前に呼ぶこと。QueueHit で積んだダメージは、そのステップの
    // rules.ResolveStep() でまとめて裁定される。
    void UpdateJudgment()
    {
        if (!judgmentPending) return;
        var buff = config.soloBuff;
        if (Time.unscaledTime < judgmentStrikeAt)
        {
            // ボスは渦の要としてその場に踏みとどまる。剣モードでは1秒で5ユニット近く落ちてしまい、
            // 薙ぎ払う頃には集めた相手から離れてしまうため。
            if (bossPlayerId != null && bodies.ContainsKey(bossPlayerId) && swords[bossPlayerId].IsAlive)
            {
                var bossRb = bodies[bossPlayerId];
                if (!judgmentGravity.ContainsKey(bossPlayerId))
                { judgmentGravity[bossPlayerId] = bossRb.gravityScale; bossRb.gravityScale = 0f; }
                bossRb.linearVelocity *= 0.85f;
            }
            foreach (var id in judgmentTargets)
            {
                if (!bodies.ContainsKey(id) || !swords[id].IsAlive) continue;
                var rb = bodies[id];
                // 吸引中だけ重力を切る。剣モードで落下と綱引きになると「吸われている」ではなく
                // 「落ちている」ように見えてしまうため。元の値は覚えておいて薙ぎ払いの時に戻す。
                if (!judgmentGravity.ContainsKey(id)) { judgmentGravity[id] = rb.gravityScale; rb.gravityScale = 0f; }
                Vector2 toCentre = (Vector2)judgmentCentre - rb.position;
                if (toCentre.sqrMagnitude < 0.09f) continue;
                Vector2 dir = toCentre.normalized;
                // 中心へ向かう速度はそのまま伸ばし、横滑りの成分だけを削る。
                // 一律に減衰させると等速移動になって加速感が消える（＝吸われている感じが出ない）。
                Vector2 velocity = rb.linearVelocity;
                Vector2 radial = dir * Vector2.Dot(velocity, dir);
                rb.linearVelocity = radial + (velocity - radial) * 0.80f;
                rb.AddForce(dir * (buff.judgmentPullForce * rb.mass), ForceMode2D.Force);
                // 揉まれている感じを出すため、引かれながら回す
                rb.AddTorque(buff.judgmentPullForce * rb.mass * Time.fixedDeltaTime * 4f, ForceMode2D.Force);
            }
            return;
        }

        judgmentPending = false;
        RestoreJudgmentGravity();
        // 溜めの途中でボスが倒れていたら斬撃は出ない。拘束だけが残る
        if (bossPlayerId == null || !swords.ContainsKey(bossPlayerId) || !swords[bossPlayerId].IsAlive)
        {
            judgmentTargets.Clear();
            return;
        }
        var attacker = swords[bossPlayerId];
        // ボスの剣が実際に範囲を薙ぎ払う。位置・回転・大きさは同期に乗るのでゲストにも見える。
        bodies[bossPlayerId].angularVelocity = buff.judgmentSweepSpin;
        attacker.BeginJudgmentSweep(buff.judgmentSweepSeconds, buff.judgmentSweepScale);
        int damage = Mathf.Max(1, Mathf.RoundToInt(attacker.attack * buff.judgmentDamageMultiplier));
        foreach (var id in judgmentTargets)
        {
            if (!bodies.ContainsKey(id) || !swords[id].IsAlive) continue;
            QueueHit(attacker, swords[id], damage);
            var rb = bodies[id];
            Vector2 outward = rb.position - (Vector2)judgmentCentre;
            if (outward.sqrMagnitude < 0.01f) outward = Vector2.up;
            rb.AddForce(outward.normalized * (buff.judgmentKnockback * rb.mass), ForceMode2D.Impulse);
        }
        judgmentTargets.Clear();
        TriggerShake(0.25f, 0.5f);
    }

    // 輪の演出（すぼまる → 炸裂）の起点。溜め時間は全員が同じ soloBuff を持っているので、
    // Hostとゲストで同じ時間割になり、専用の同期項目を足さずに揃う。
    void StartJudgmentVisual(Vector3 centre)
    {
        judgmentCentre = centre;
        judgmentVisualStart = Time.unscaledTime;
    }

    // 独楽の自動追尾を止めるためのフラグを配る。剣モードでは入力が通らないだけで足りるが、
    // 独楽は操作なしでも敵へ向かい続けるので、追尾力も切らないと拘束にならない。
    void RefreshSuppression()
    {
        foreach (var pair in swords)
        {
            var controller = pair.Value.GetComponent<SwordController>();
            if (controller != null) controller.suppressed = IsSuppressed(pair.Key);
        }
    }

    // HP以外のボス強化。SPゲージは最大200になるが、通常必殺技のラインは全員共通の100のまま
    // (ultimateSpは触らない)。溜まる速さだけを上げて、2段階目の制圧まで届くようにする。
    void ApplyBossBuff(SwordBattle battle)
    {
        var buff = config.soloBuff;
        battle.attack = Mathf.Max(1, Mathf.RoundToInt(battle.attack * buff.attackMultiplier));
        battle.maxSp = buff.maxSp;
        battle.passiveSpFill *= buff.spGainMultiplier;
        battle.damageSpMultiplier *= buff.spGainMultiplier;
    }

    // ボスのHPは倍率ぶん増える。MatchRulesへ渡す値と、Unity側のmaxHp(=HPバーの上限)を
    // 必ず同じ計算で出すこと。片方だけ強化するとHPバーが頭打ちになって嘘の数値を表示する。
    int BuffedHp(MultiplayerPlayerConfig player, int hp)
    {
        if (!IsBoss(player)) return hp;
        return Mathf.Clamp(Mathf.RoundToInt(hp * config.soloBuff.hpMultiplier), 1, MatchRules.MaxHitPoints);
    }

    void CreateSword(MultiplayerPlayerConfig player, GameObject template, float bladeWidth, Vector3 spawnPosition)
    {
        var obj = Instantiate(template, arena.transform);
        obj.name = "Sword_" + player.playerId;
        obj.transform.position = spawnPosition;
        obj.transform.rotation = Quaternion.identity;
        var battle = obj.GetComponent<SwordBattle>();
        battle.frameImage = null; // PL{n}Bar側に対応する枠要素が無いため配線しない
        WireHudBar(battle, player.slotIndex);
        // ▼ 必殺技ボタンはPL1用の1つだけを全プレイヤーで共有する。実際に見える/押せるのは
        // isLocalControlledなインスタンスだけなので（TryUltimate側でガード済み）、これで問題ない
        battle.specialAttackButton = hudCanvas != null
            ? hudCanvas.transform.Find("SpecialAttackButtonPL1")?.GetComponent<Button>() : null;
        battle.playerNumber = player.slotIndex + 1;
        battle.ConfigureMultiplayer(this, player.playerId);
        var controller = obj.GetComponent<SwordController>();
        controller.multiplayer = this;
        controller.isLocalControlled = player.playerId == config.localPlayerId;
        var rb = obj.GetComponent<Rigidbody2D>();
        rb.simulated = false;
        rb.bodyType = IsHost ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
        var blade = obj.transform.Find("Blade");
        // ▼ 柄(Handle-A)はテンプレート側のSwordController.handleObjectが誤って別の剣の柄を参照して
        // いることがある。参照先がテンプレートの階層の外にあると、Unityは複製(Instantiate)時にこの
        // 参照を複製先へ付け替えないため、そのままだと全プレイヤーが同じ1つの(誤った)柄オブジェクトを
        // 共有してしまう。ResolveOwnHandle()が複製した自分自身の子から名前で探し直すことで、
        // Inspectorの配線ミスに関わらず必ず「自分の」柄を使うようにする
        controller.ResolveOwnHandle();
        // ▼【調査用ログ】実機のクライアント側だけ柄が透明/非表示になる不具合を追うための診断ログ
        Debug.Log($"🗡️ CreateSword: playerId={player.playerId}, IsHost={IsHost}, " +
            $"isLocalControlled={controller.isLocalControlled}, resolvedHandle={(controller.handleObject != null ? controller.handleObject.name + "(id=" + controller.handleObject.GetInstanceID() + ")" : "null")}");
        // ▼【修正】SceneController側の3・4人目動的生成(CreateDynamicPlayerSword)と同じく、
        // テンプレートに既にSwordGeneratorが付いていればそれを再利用する（無条件AddComponentは二重生成の恐れがあった）
        var generator = obj.GetComponent<SwordGenerator>();
        if (generator == null) generator = obj.AddComponent<SwordGenerator>();
        generator.generateOnStart = false;
        generator.targetBladeWidth = bladeWidth;
        generator.targetSpriteRenderer = blade.GetComponent<SpriteRenderer>();
        generator.bladeCollider = blade.GetComponent<PolygonCollider2D>();
        generator.swordRigidbody = rb;
        generator.swordBattle = battle;
        generator.handleObject = controller.handleObject;
        // ▼【新規追加】SwordGeneratorは複製元に常設されておらずAddComponentされる(=Inspectorの値を
        // 持てない)ため、常設されているSwordController側に設定した柄の画像をここでコピーする
        generator.handleSprite0 = controller.handleSprite0;
        generator.handleSprite1 = controller.handleSprite1;
        generator.handleSprite2 = controller.handleSprite2;
        generator.handleSprite3 = controller.handleSprite3;
        // ボスはHPだけ生成前に差し替える。SwordGeneratorがそのままHPとmaxHpに使うので、
        // MatchRulesへ渡した値と一致し、HPバーの上限も強化後の値になる。
        generator.GenerateSwordFromJson(JsonUtility.ToJson(BuffedSwordData(player)));
        if (!generator.LastGenerationSucceeded) throw new InvalidOperationException("Could not generate player sword.");
        // 残りの強化は生成後に掛ける。攻撃力はSwordGeneratorが1〜100を10〜90へ変換した後の
        // 実数値に掛けたいので、変換前の素の値をいじってはいけない。
        if (IsBoss(player)) ApplyBossBuff(battle);
        controller.ApplyPhysicsMode();
        swords.Add(player.playerId, battle); bodies.Add(player.playerId, rb);
        sequences[player.playerId] = 0; inputTimes[player.playerId] = -100;
        targets[player.playerId] = null;
        // ▼【新規追加】残機モードなら、開始時点で「まだ使っていない残りの剣」をHPパネルに表示する
        if (livesModeActive) battle.UpdateReserveSwordIcons(GetReserveSprites(player.playerId));
        obj.SetActive(true);
    }

    // ▼【新規追加】あるプレイヤーの手持ちの剣を「現在装備中の剣から始めて、配列の並び順で一巡する」順序に
    // 並べ、空きスロット(isEmpty)を除いたリストを返す。1本・2本しか持っていない場合はその本数分だけになる
    // (呼び出し側は必ずCount>=1として扱ってよい)。swords[]が送られてこなかった場合は、SwordDataの
    // 単一ステータスだけを1本分のリストとして返す
    static List<SwordSlotData> BuildOwnedSwords(SwordData swordData)
    {
        var owned = new List<SwordSlotData>();
        if (swordData.swords != null && swordData.swords.Length > 0)
        {
            var slots = swordData.swords;
            int n = slots.Length;
            int start = Mathf.Clamp(swordData.equippedIndex, 0, n - 1);
            for (int i = 0; i < n; i++)
            {
                var slot = slots[(start + i) % n];
                if (slot != null && !slot.isEmpty) owned.Add(slot);
            }
        }
        if (owned.Count == 0)
        {
            owned.Add(new SwordSlotData { name = swordData.name, attack = swordData.attack, weight = swordData.weight,
                hp = swordData.hp, imageStr = swordData.imageStr, hiltType = swordData.hiltType, isEmpty = false });
        }
        return owned;
    }

    // ▼【新規追加】残機モード：現在の剣が破壊されて次の剣に持ち替わった時に、見た目(刀身の再生成)・
    // 位置(スポーン地点へ復帰)・操作可否を復元する。Host/Client双方から同じ手順で呼べる
    // (ネットワーク送信は行わないため、Hostが自分のFixedUpdateから、Clientが自分のSyncMultiplayerから
    // それぞれ独立に呼んでも結果が一致する)
    void ReviveForNextLife(string playerId, int lifeIndex)
    {
        if (!swords.TryGetValue(playerId, out var battle) || battle == null ||
            !ownedSwordsByPlayer.TryGetValue(playerId, out var lives) || lifeIndex < 0 || lifeIndex >= lives.Count) return;
        var slot = lives[lifeIndex];
        battle.ReviveFromDefeat();
        if (spawnPositionByPlayer.TryGetValue(playerId, out var spawn))
        {
            battle.transform.position = spawn;
            battle.transform.rotation = Quaternion.identity;
        }
        if (bodies.TryGetValue(playerId, out var rb)) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0; }
        var controllerComp = battle.GetComponent<SwordController>();
        if (controllerComp != null)
        {
            controllerComp.isLocalControlled = config != null && playerId == config.localPlayerId;
            controllerComp.ApplyPhysicsMode();
        }
        var generator = battle.GetComponent<SwordGenerator>();
        if (generator != null)
        {
            var nextSword = new SwordData { name = slot.name, attack = slot.attack, weight = slot.weight,
                hp = slot.hp, imageStr = slot.imageStr, hiltType = slot.hiltType };
            generator.GenerateSwordFromJson(JsonUtility.ToJson(nextSword));
        }
        // ▼【新規追加】持ち替え後、HPパネルの「あと何本あるか」アイコンも更新する
        battle.UpdateReserveSwordIcons(GetReserveSprites(playerId));
        TriggerShake(0.3f, 0.4f);
    }

    // ▼【新規追加】シーンに元々あるPL{n}Bar（HP赤/緑二重ゲージ・名前・SPバー）の実要素を、
    // slotIndexに対応するプレイヤーのSwordBattleへ配線する。BuildHud()で簡易HUDを作り直す必要が無くなり、
    // SwordBattle.Update()/UpdateUI()が既に持っているダメージプレビュー(赤/緑ゲージ)等の作り込みがそのまま使える。
    // 「Player2」系の子オブジェクト名だけ"Playe2"という表記揺れがあるため、接尾辞一致で探す
    void WireHudBar(SwordBattle battle, int slotIndex)
    {
        battle.hpBar = null; battle.delayHpBar = null; battle.nameText = null; battle.hpText = null;
        battle.spGaugeBar = null; battle.spText = null;
        Transform bar = hudCanvas != null ? hudCanvas.transform.Find("PL" + (slotIndex + 1) + "Bar") : null;
        if (bar == null) return;

        Transform green = FindChildEndingWith(bar, "HPBarGreen");
        Transform red = FindChildEndingWith(bar, "HPBarRed");
        Transform sp = FindChildEndingWith(bar, "SPBar");
        if (green != null)
        {
            battle.hpBar = green.GetComponent<Slider>();
            battle.hpText = green.Find("HPText (TMP)")?.GetComponent<TextMeshProUGUI>();
            battle.nameText = green.Find("NameText (TMP)")?.GetComponent<TextMeshProUGUI>();
        }
        if (red != null) battle.delayHpBar = red.GetComponent<Slider>();
        if (sp != null)
        {
            battle.spGaugeBar = sp.GetComponent<Slider>();
            battle.spText = sp.Find("SPText (TMP)")?.GetComponent<TextMeshProUGUI>();
        }
        // ▼【新規追加】残機モード：HPパネルの右下に「あと何本あるか」の小さいアイコンを並べる枠を
        // 実行時に生成する(シーン側の手動配置は不要)
        battle.reserveSwordIcons = CreateReserveSwordIcons(bar);
    }

    static Transform FindChildEndingWith(Transform parent, string suffix)
    {
        foreach (Transform child in parent)
            if (child.name.EndsWith(suffix)) return child;
        return null;
    }

    // ▼【新規追加】残機モードで手持ちの剣の枚数分だけ、PL{n}Barの右下隅に小さいアイコンを並べる。
    // 本数が3を超えることは無い(=残りは最大2)想定だが、余分に確保しても表示側で自動的に隠れる。
    // PL{n}Bar自体は対戦をまたいで常設されたUIなので、再戦時に重複生成しないよう名前で既存のものを再利用する
    const int MaxReserveIcons = 2;
    static Image[] CreateReserveSwordIcons(Transform bar)
    {
        var icons = new Image[MaxReserveIcons];
        for (int i = 0; i < MaxReserveIcons; i++)
        {
            string name = "ReserveSwordIcon" + i;
            var existing = bar.Find(name);
            GameObject iconObj;
            if (existing != null)
            {
                iconObj = existing.gameObject;
            }
            else
            {
                iconObj = new GameObject(name, typeof(RectTransform), typeof(Image));
                iconObj.transform.SetParent(bar, false);
                var rt = iconObj.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1, 0);
                rt.anchorMax = new Vector2(1, 0);
                rt.pivot = new Vector2(1, 0);
                rt.sizeDelta = new Vector2(22, 22);
                // 右下隅を基準に、0番目(次に使う剣)を一番右、以降は左に並べる
                rt.anchoredPosition = new Vector2(-4 - i * 26, 4);
            }
            var img = iconObj.GetComponent<Image>();
            if (img == null) img = iconObj.AddComponent<Image>();
            img.preserveAspect = true;
            iconObj.SetActive(false);
            icons[i] = img;
        }
        return icons;
    }

    // ▼【新規追加】残機モード：あるプレイヤーの「今使っている剣を除いた、まだ使っていない手持ちの剣」の
    // 画像をSpriteの配列で返す(順番=次に使う順)。HPパネルのアイコン表示に使う
    Sprite[] GetReserveSprites(string playerId)
    {
        if (!ownedSwordsByPlayer.TryGetValue(playerId, out var lives)) return Array.Empty<Sprite>();
        int current = GetCurrentLifeIndex(playerId);
        var result = new List<Sprite>();
        for (int i = current + 1; i < lives.Count; i++)
        {
            var sprite = GetLifeSprite(playerId, i);
            if (sprite != null) result.Add(sprite);
        }
        return result.ToArray();
    }


    IEnumerator CompleteInitialization()
    {
        // Wait for deferred collider replacement and all cloned component Starts.
        yield return null;
        Physics2D.SyncTransforms();
        foreach (var rb in bodies.Values) rb.simulated = false;
        UpdateTargets(true);
        Emit("INITIALIZED", new MultiplayerCommand { matchId = config.matchId });
    }

    [UnityEngine.Scripting.Preserve]
    public void BeginMultiplayer(string json)
    {
        var msg = JsonUtility.FromJson<MultiplayerCommand>(json);
        if (!Matches(msg) || !IsHost || phase != "LOADING") return;
        phase = "COUNTDOWN"; countdownEnd = Time.unscaledTime + 3;
        // ▼【修正】ローカルは剣・独楽ともに配置直後から物理演算が動いており、独楽はカウントダウン中も
        // その場で回り続け、剣は重力で沈む。以前はPLAYINGになるまで物理を凍結しており、
        // マルチプレイだけカウントダウン中は完全に静止して見えていた
        foreach (var rb in bodies.Values) rb.simulated = true;
    }

    void Update()
    {
        if (config == null) return;
        UpdateCountdownUi();
        UpdateSuppressLabels();
        UpdateSuppressRange();
        UpdateSoloHud();
        if (IsHost && phase == "COUNTDOWN" && Time.unscaledTime >= countdownEnd)
        {
            phase = "PLAYING"; SwordBattle.isRoundStarted = true;
            Emit("PLAYING", new MultiplayerCommand { matchId = config.matchId });
        }
        if (IsHost && (phase == "COUNTDOWN" || IsPlaying) && Time.unscaledTime >= nextSync)
        {
            nextSync = Time.unscaledTime + 1f / 30;
            Emit("SYNC", Snapshot());
        }
        if (!IsHost)
        {
            // ▼【修正】位置と回転で追従速度を分ける。巨大化一回転(1080°/秒)のような速い回転は、
            // 位置と同じ補間レート(25)だと定常的な追従遅れ(角速度/レート ≈ 1080/25 ≈ 43°)が生じ、
            // クライアント側では「あまり回っていない」ように見えていた。回転だけ大幅に追従を速くして
            // (1080/150 ≈ 7°まで遅れを圧縮)、実際の回転速度に近い見た目にする
            float posT = 1 - Mathf.Exp(-25 * Time.unscaledDeltaTime);
            float rotT = 1 - Mathf.Exp(-150 * Time.unscaledDeltaTime);
            foreach (var pair in syncTargets)
            {
                var sword = swords[pair.Key]; var state = pair.Value;
                sword.transform.position = Vector3.Lerp(sword.transform.position, new Vector3(state.x, state.y, 0), posT);
                sword.transform.rotation = Quaternion.Slerp(sword.transform.rotation, Quaternion.Euler(0, 0, state.rotation), rotT);
            }
            // ▼ 分身・リーフシールドの見た目も、剣本体と同じ補間で滑らかに追従させる
            foreach (var pair in cloneSyncTargets)
            {
                if (!cloneVisuals.TryGetValue(pair.Key, out var obj) || obj == null) continue;
                var state = pair.Value;
                obj.transform.position = Vector3.Lerp(obj.transform.position, new Vector3(state.x, state.y, 0), posT);
                obj.transform.rotation = Quaternion.Slerp(obj.transform.rotation, Quaternion.Euler(0, 0, state.rotation), rotT);
            }
        }
    }

    // ▼【新規追加】React側ではカウントダウンを表示しないため、SceneController.countdownText
    // （ローカルデモと共用のTMP）をマルチプレイ中もそのまま使って「3・2・1・GO!」を表示する。
    // Host/Client問わず、毎フレームCountdownRemainingを反映するだけ
    void UpdateCountdownUi()
    {
        if (countdownText == null) return;
        if (phase == "COUNTDOWN")
        {
            if (countdownUiPhase != "COUNTDOWN") countdownText.gameObject.SetActive(true);
            countdownText.text = Mathf.CeilToInt(CountdownRemaining).ToString();
        }
        else if (countdownUiPhase == "COUNTDOWN")
        {
            // ちょうどCOUNTDOWNを抜けた瞬間：GO!を一瞬見せてから消す
            countdownText.text = "GO!";
            countdownGoHideAt = Time.unscaledTime + 1f;
        }
        else if (countdownText.gameObject.activeSelf && Time.unscaledTime >= countdownGoHideAt)
        {
            countdownText.gameObject.SetActive(false);
        }
        countdownUiPhase = phase;
    }

    void FixedUpdate()
    {
        // ▼【修正】カウントダウン中も(PLAYING同様に)物理演算だけは進める。ダメージ判定・勝敗判定は
        // 引き続きIsPlayingになってからのみ行う（QueueHit側もIsPlayingガード済みなので二重に安全）
        if (!IsHost || !IsSimulating) return;
        UpdateTargets(false);
        // 制圧の残り時間は実時間で切れるので、毎ステップ配り直して独楽の追尾抑止を最新にする
        RefreshSuppression();
        // 引き寄せの力と薙ぎ払いは、物理を進める前に積む（QueueHitはこのステップの裁定に乗る）
        UpdateJudgment();
        // ▼【修正】オレ達シールド展開中も本体は無敵にする(SwordBattle.TakeDamage側の無敵判定と同じ条件)。
        // これが無いと、Client側は正しいHPを受け取れても、Host自身のOnCollisionEnter2D→QueueHitでは
        // シールドの反射に加えて本体にも通常ダメージが通ってしまっていた。
        // ▼【新規追加】残機モードのリスポーン直後無敵(IsRespawnInvincible)もここに加える。
        // これが無いと見た目は点滅していても、Host権威のダメージ判定では素通しになってしまう
        foreach (var p in swords) invulnerable[p.Key] = p.Value.isDashing || p.Value.HasActiveLeafShield || p.Value.IsRespawnInvincible;
        Physics2D.Simulate(Time.fixedDeltaTime);
        if (!IsPlaying) return;
        rules.ResolveStep(++physicsTick, hits, forfeits);
        hits.Clear(); forfeits.Clear();
        foreach (var score in rules.Players)
        {
            // ▼【新規追加】残機モード：このtickで次の剣に持ち替わったプレイヤーを検知し、見た目を復活させる。
            // 直後のApplyMultiplayerHealth(score.Hp, ...)が新しい剣のmaxHpを基準にHPを反映する
            if (livesModeActive && lifeIndexByPlayer.TryGetValue(score.PlayerId, out var knownLifeIndex) && knownLifeIndex != score.CurrentLifeIndex)
            {
                lifeIndexByPlayer[score.PlayerId] = score.CurrentLifeIndex;
                ReviveForNextLife(score.PlayerId, score.CurrentLifeIndex);
            }
            bool wasCrit = criticalHitThisTick.Remove(score.PlayerId);
            if (wasCrit) critSeq[score.PlayerId] = critSeq.TryGetValue(score.PlayerId, out var seq) ? seq + 1 : 1;
            swords[score.PlayerId].ApplyMultiplayerHealth(score.Hp, wasCrit);
        }
        criticalHitThisTick.Clear();
        if (rules.Ended)
        {
            // ▼ ちょうど今決着したプレイヤーのApplyMultiplayerHealthで加えた吹っ飛ばしの力を、
            // 完全停止する前に一度だけ反映させる(ローカルのDefeatRoutineも力を加えた直後にまだ動ける)
            Physics2D.Simulate(Time.fixedDeltaTime);
            // Deliver final HP/death state before the separately acknowledged result.
            Emit("SYNC", Snapshot());
            var result = new MultiplayerResult { matchId = config.matchId, winnerId = rules.WinnerId, draw = rules.Draw,
                winnerTeam = rules.WinnerTeam,
                standings = rules.Players.Select(p => new MultiplayerScore { playerId = p.PlayerId, rank = p.Rank,
                    damageDealt = p.DamageDealt, damageTaken = p.DamageTaken, kills = p.Kills,
                    eliminationTick = p.EliminationTick, eliminationReason = p.EliminationReason }).ToArray() };
            Emit("RESULT", result);
            FinishMultiplayer(JsonUtility.ToJson(result));
        }
    }

    public void QueueHit(SwordBattle attacker, SwordBattle target, int damage, bool isCrit = false)
    {
        if (!IsHost || !IsPlaying || target.MultiplayerOwner != this || attacker.MultiplayerOwner != this ||
            !rules.Get(target.PlayerId).Alive) return;
        // 味方同士はダメージにしない。弾き合いはOnCollisionEnter2D側で既に済んでいるので、
        // ここで捨てるのは数値・撃破数・ダメージ表示だけ。最終的な裁定はMatchRules側でも同じ条件で行う。
        if (rules.Get(attacker.PlayerId).Team == rules.Get(target.PlayerId).Team) return;
        // オレ掌握斬を受けている剣は攻撃力ゼロ。入力を止めるだけでは、慣性で突っ込んだ衝突や
        // 発動済みの分身・シールドからダメージが通ってしまうので、与える側の経路をここで断つ。
        // 与えるダメージが消えるだけで、受ける側としては通常どおり成立する。
        if (IsSuppressed(attacker.PlayerId)) return;
        // Two dashes clashing in koma mode break each other's guard.
        bool blocked = invulnerable.ContainsKey(target.PlayerId) && invulnerable[target.PlayerId];
        bool clash = SwordController.isKomaMode && invulnerable.ContainsKey(attacker.PlayerId) && invulnerable[attacker.PlayerId];
        if (!blocked || clash)
        {
            hits.Add(new Hit(attacker.PlayerId, target.PlayerId, damage));
            // ▼【新規追加】クリティカル/弱点情報はMatchRulesのHitには乗らない(複数攻撃者の同時ヒットで
            // 合算されるため)ので、ここで別途覚えておいてこのtickのApplyMultiplayerHealthに渡す
            if (isCrit) criticalHitThisTick[target.PlayerId] = true;
        }
    }

    // ▼【新規追加】ローカルのBattleCamera.TriggerShakeと同じ役割。BattleCameraはマルチプレイ中は
    // 無効化されているため、代わりにこのマネージャーが自分のLateUpdate()でシェイクを消費する
    public void TriggerShake(float duration, float magnitude)
    {
        shakeDuration = duration; shakeMagnitude = magnitude;
    }

    // ▼【新規追加】柄迫り合い・壁バウンドなどダメージを伴わない衝突演出は、Host側のOnCollisionEnter2Dでしか
    // 起きないためゲスト側の画面には何も表示されない。SYNCで単調増加カウンタとして配信し、
    // ゲスト側は値が増えたことを検知して同じ演出(PlayClashEffect)を一度だけ再生する
    public void NotifyClash(string playerId)
    {
        if (playerId == null) return;
        clashSeq[playerId] = clashSeq.TryGetValue(playerId, out var seq) ? seq + 1 : 1;
    }

    // ▼【新規追加】分身突進・リーフシールドなどの付随体1体をSYNC配信対象として登録し、識別用IDを返す。
    // spriteIndexは「持ち主の手持ちの剣の何番目の形をしているか」(0=現在装備中)。残機モードのON/OFFに
    // 関わらず使う。特定の剣に紐付かない場合は-1のままでよい
    public string RegisterClone(GameObject clone, string ownerId, Color color, int spriteIndex = -1)
    {
        string id = "clone" + (++cloneIdSeq);
        activeClones[id] = (clone, ownerId, color, spriteIndex);
        return id;
    }

    // ▼【新規追加】あるプレイヤーが「今まさに使っている」剣が手持ちの何番目かを返す
    // (lifeIndexByPlayerはHost/Client双方でReviveForNextLife検知のために常に最新へ更新されており、
    // 残機モードでなければ常に0のまま＝装備中の剣)。分身系必殺技が「現在の自分＋まだ使っていない
    // 残りの剣」を並べる基準として、残機モードのON/OFFに関わらず使う
    public int GetCurrentLifeIndex(string playerId)
    {
        return lifeIndexByPlayer.TryGetValue(playerId, out var idx) ? idx : 0;
    }

    // ▼【新規追加】あるプレイヤーの手持ちの剣のうちlifeIndex番目の見た目をSpriteとして返す(base64から
    // 遅延生成してキャッシュ)。分身系必殺技が、装備中でない手持ちの剣の形を借りるために使う。
    // 残機モードのON/OFFに関わらず動作し、手持ちが1本・2本しかなくても(lives.Count>=1が保証されているため)
    // 範囲外エラーにはならない
    public Sprite GetLifeSprite(string playerId, int lifeIndex)
    {
        if (ownedSwordsByPlayer == null || !ownedSwordsByPlayer.TryGetValue(playerId, out var lives) || lives.Count == 0) return null;
        int idx = ((lifeIndex % lives.Count) + lives.Count) % lives.Count;
        if (!lifeSpriteCache.TryGetValue(playerId, out var cache) || cache.Length != lives.Count)
        {
            cache = new Sprite[lives.Count];
            lifeSpriteCache[playerId] = cache;
        }
        if (cache[idx] == null) cache[idx] = SwordGenerator.CreateSpriteFromBase64(lives[idx].imageStr);
        return cache[idx];
    }

    // ▼【新規追加】分身が消えた（命中・壁ヒット・寿命切れ）ときに登録を解除する。これでSYNCから外れ、
    // クライアント側のSyncClones()もこのIDを見なくなって見た目の分身も消える
    public void UnregisterClone(string id)
    {
        if (id != null) activeClones.Remove(id);
    }

    public void SubmitLocalInput(bool right)
    {
        if (!IsPlaying || !swords[config.localPlayerId].IsAlive) return;
        Emit("INPUT", new MultiplayerCommand { matchId = config.matchId, action = "PRIMARY", direction = right ? "RIGHT" : "LEFT" });
    }
    // ▼【新規追加】必殺技専用ボタン/スペースキー用の送信。方向は不要
    public void SubmitLocalUltimate()
    {
        if (!IsPlaying || !swords[config.localPlayerId].IsAlive) return;
        Emit("INPUT", new MultiplayerCommand { matchId = config.matchId, action = "ULTIMATE" });
    }
    // ▼【新規追加】1vs3：ボスの制圧。撃てるかどうかの判定はHost側で行うので、ここでは送るだけ
    public void SubmitLocalSuppress()
    {
        if (!IsPlaying || !soloModeActive || !swords[config.localPlayerId].IsAlive) return;
        Emit("INPUT", new MultiplayerCommand { matchId = config.matchId, action = "SUPPRESS" });
    }
    [UnityEngine.Scripting.Preserve]
    public void ReceiveMultiplayerInput(string json)
    {
        var msg = JsonUtility.FromJson<MultiplayerCommand>(json);
        if (!Matches(msg) || !IsHost || !IsPlaying || msg.playerId == null || !swords.ContainsKey(msg.playerId) ||
            msg.seq <= sequences[msg.playerId] || Time.unscaledTime - inputTimes[msg.playerId] < .04f) return;
        if (msg.action == "PRIMARY" && (msg.direction == "LEFT" || msg.direction == "RIGHT"))
        {
            sequences[msg.playerId] = msg.seq; inputTimes[msg.playerId] = Time.unscaledTime;
            swords[msg.playerId].ExecuteMultiplayerAction(msg.direction == "RIGHT");
        }
        else if (msg.action == "SUPPRESS")
        {
            sequences[msg.playerId] = msg.seq; inputTimes[msg.playerId] = Time.unscaledTime;
            swords[msg.playerId].ExecuteMultiplayerSuppress();
        }
        else if (msg.action == "ULTIMATE")
        {
            sequences[msg.playerId] = msg.seq; inputTimes[msg.playerId] = Time.unscaledTime;
            swords[msg.playerId].ExecuteMultiplayerUltimate();
        }
    }
    [UnityEngine.Scripting.Preserve]
    public void ForfeitMultiplayer(string json)
    {
        var msg = JsonUtility.FromJson<MultiplayerCommand>(json);
        if (Matches(msg) && IsHost && IsPlaying && msg.playerId != null && swords.ContainsKey(msg.playerId)) forfeits.Add(msg.playerId);
    }

    void UpdateTargets(bool force)
    {
        var candidates = TargetCandidates();
        bool interval = Time.unscaledTime >= nextTargetUpdate;
        foreach (var p in swords)
        {
            string current = targets[p.Key];
            if (!force && !interval && current != null && swords[current].IsAlive) continue;
            targets[p.Key] = BattlePolicies.Target(p.Key, p.Value.transform.position.x, p.Value.transform.position.y, candidates, current, p.Value.isDashing);
            p.Value.GetComponent<SwordController>().enemyTarget = targets[p.Key] == null ? null : swords[targets[p.Key]].transform;
        }
        if (interval) nextTargetUpdate = Time.unscaledTime + .25f;
    }

    MultiplayerSync Snapshot()
    {
        return new MultiplayerSync { matchId = config.matchId, tick = ++syncTick, phase = phase,
            countdownRemaining = phase == "COUNTDOWN" ? Mathf.Max(0, countdownEnd - Time.unscaledTime) : 0,
            players = config.players.Select(p => {
                var sword = swords[p.playerId]; var pos = sword.transform.position;
                var center = SwordController.isKomaMode ? (Vector3)bodies[p.playerId].worldCenterOfMass : pos;
                sword.currentCenterPosition = center;
                return new MultiplayerPlayerState { playerId = p.playerId, x = pos.x, y = pos.y,
                    rotation = sword.transform.eulerAngles.z, centerX = center.x, centerY = center.y,
                    hp = sword.hp, sp = sword.currentSp, isDashing = sword.isDashing, dashType = sword.currentDashType,
                    // ▼ 巨大化一回転(hiltType:"1")の拡大がクライアント側にも見えるよう、剣本体のスケールも同期する
                    scale = sword.transform.localScale.x,
                    targetPlayerId = targets[p.playerId],
                    clashSeq = clashSeq.TryGetValue(p.playerId, out var cseq) ? cseq : 0,
                    critSeq = critSeq.TryGetValue(p.playerId, out var crseq) ? crseq : 0,
                    lifeIndex = rules.Get(p.playerId).CurrentLifeIndex,
                    livesRemaining = rules.Get(p.playerId).LivesRemaining,
                    suppressedRemaining = SuppressRemaining(p.playerId) };
            }).ToArray(),
            // ▼【新規追加】分身突進・リーフシールドなどの付随体の位置をクライアントへ配信する
            clones = activeClones.Where(kv => kv.Value.obj != null).Select(kv => new MultiplayerCloneState {
                id = kv.Key, ownerId = kv.Value.ownerId,
                x = kv.Value.obj.transform.position.x, y = kv.Value.obj.transform.position.y,
                rotation = kv.Value.obj.transform.eulerAngles.z, color = kv.Value.color, spriteIndex = kv.Value.spriteIndex
            }).ToArray() };
    }

    [UnityEngine.Scripting.Preserve]
    public void SyncMultiplayer(string json)
    {
        var sync = JsonUtility.FromJson<MultiplayerSync>(json);
        if (config == null || IsHost || sync == null || sync.matchId != config.matchId || sync.tick <= receivedTick || phase == "RESULT") return;
        if (sync.players == null || sync.players.Length != config.players.Length ||
            sync.players.Any(p => p == null || p.playerId == null || !swords.ContainsKey(p.playerId)) ||
            sync.players.Select(p => p.playerId).Distinct().Count() != config.players.Length) return;
        phase = sync.phase; SwordBattle.isRoundStarted = IsPlaying;
        receivedCountdownRemaining = sync.countdownRemaining;
        foreach (var data in sync.players)
        {
            var sword = swords[data.playerId];
            if (receivedTick < 0) { sword.transform.position = new Vector3(data.x, data.y, 0); sword.transform.rotation = Quaternion.Euler(0, 0, data.rotation); }
            // ▼【新規追加】クリティカル演出・柄迫り合い等の演出は増分(カウンタが増えたか)でしか検知できないため、
            // 上書きする前の直近の値と比較する。初回同期時は前回値が無い(比較対象なし)ので発火しない
            syncTargets.TryGetValue(data.playerId, out var previous);
            bool wasCrit = previous != null && data.critSeq > previous.critSeq;
            bool clashed = previous != null && data.clashSeq > previous.clashSeq;
            syncTargets[data.playerId] = data;
            sword.currentCenterPosition = new Vector3(data.centerX, data.centerY, 0);
            // ▼【新規追加】残機モード：Hostから届いたlifeIndexがこれまで認識していた値より増えていれば、
            // Hostのfixedupdateと同じ手順(ReviveForNextLife)でこちらの見た目も持ち替えさせる
            if (livesModeActive && lifeIndexByPlayer.TryGetValue(data.playerId, out var knownLifeIndex) && data.lifeIndex != knownLifeIndex)
            {
                lifeIndexByPlayer[data.playerId] = data.lifeIndex;
                ReviveForNextLife(data.playerId, data.lifeIndex);
            }
            sword.ApplyMultiplayerHealth(data.hp, wasCrit);
            if (clashed) sword.PlayClashEffect();
            sword.ApplyMultiplayerVisuals(data.sp, data.isDashing, data.dashType, data.scale);
            // 残り秒数を「解ける時刻」に直して持つ。こうするとHostと同じ IsSuppressed() が使える。
            if (data.suppressedRemaining > 0f)
            {
                suppressUntil[data.playerId] = Time.unscaledTime + data.suppressedRemaining;
                // 0秒から立ち上がった瞬間＝ボスが今撃った、とみなして輪を明滅させる。
                // 専用の同期項目を足さずに済ませるため、ボスの現在位置を中心として使う。
                if (wasSuppressed.Add(data.playerId) && bossPlayerId != null && swords.ContainsKey(bossPlayerId))
                    StartJudgmentVisual(swords[bossPlayerId].currentCenterPosition);
            }
            else { suppressUntil.Remove(data.playerId); wasSuppressed.Remove(data.playerId); }
            targets[data.playerId] = data.targetPlayerId;
        }
        SyncClones(sync.clones);
        receivedTick = sync.tick;
    }

    // ▼【新規追加】Hostから届いた分身の一覧に合わせて、クライアント側の見た目専用オブジェクトを
    // 生成・削除する。位置・回転は即座にスナップさせず、Update()側で剣本体と同じ補間で滑らかに追従させる
    // （新規出現時だけは目標位置にスナップして、原点から一瞬で飛んでくるのを防ぐ）
    void SyncClones(MultiplayerCloneState[] cloneStates)
    {
        var incomingIds = new HashSet<string>();
        if (cloneStates != null)
        {
            foreach (var c in cloneStates)
            {
                if (c == null || c.id == null) continue;
                incomingIds.Add(c.id);
                if (!cloneVisuals.TryGetValue(c.id, out var obj) || obj == null)
                {
                    obj = CreateCloneVisual(c.ownerId, c.color, c.spriteIndex);
                    if (obj == null) continue;
                    cloneVisuals[c.id] = obj;
                    obj.transform.position = new Vector3(c.x, c.y, 0);
                    obj.transform.rotation = Quaternion.Euler(0, 0, c.rotation);
                }
                cloneSyncTargets[c.id] = c;
            }
        }
        foreach (var id in cloneVisuals.Keys.Where(id => !incomingIds.Contains(id)).ToList())
        {
            if (cloneVisuals[id] != null) Destroy(cloneVisuals[id]);
            cloneVisuals.Remove(id);
            cloneSyncTargets.Remove(id);
        }
    }

    // ▼ 付随体の見た目は、基本的に持ち主(ownerId)のBladeスプライトをそのまま複製して作る（imageStrは
    // 全員が同じmatch設定からローカルで生成済みなので、スプライト自体をネットワーク越しに送る必要はない）。
    // 色は技ごとに異なるためSYNCで受け取る。
    // ▼【新規追加】残機モードでspriteIndexが有効な値(0以上)なら、代わりにその番号の残機の剣の形を使う
    // (装備中でない剣でも、imageStrさえ手元にあればここでSpriteを生成できる)
    GameObject CreateCloneVisual(string ownerId, Color color, int spriteIndex = -1)
    {
        if (ownerId == null || !swords.TryGetValue(ownerId, out var ownerBattle) || ownerBattle == null) return null;
        var ownerBlade = ownerBattle.transform.Find("Blade");
        var ownerSr = ownerBlade != null ? ownerBlade.GetComponent<SpriteRenderer>() : null;
        if (ownerSr == null || ownerSr.sprite == null) return null;
        Sprite sprite = (spriteIndex >= 0 ? GetLifeSprite(ownerId, spriteIndex) : null) ?? ownerSr.sprite;

        var obj = new GameObject("SwordCloneVisual");
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingLayerID = ownerSr.sortingLayerID;
        sr.sortingOrder = ownerSr.sortingOrder;
        // ▼【新規追加】Host側(SwordBattle.CloneRushRoutine/LeafShieldRoutine)と同じ正規化。手持ちの剣は
        // 元画像のピクセルサイズがバラバラなため、装備中の刀身と見た目の横幅が揃うようスケールを合わせる
        float refWidth = ownerSr.sprite.bounds.size.x;
        float width = sprite.bounds.size.x;
        if (refWidth > 0f && width > 0f) obj.transform.localScale = Vector3.one * (refWidth / width);
        return obj;
    }

    void LateUpdate()
    {
        if (config == null || Camera.main == null) return;
        var camera = Camera.main;
        Vector3 basePosition;
        if (phase == "RESULT")
        {
            // ▼【修正】ローカルのBattleCamera.StopTracking()と同じく、決着した瞬間の位置で固定する
            // (以前はここで即returnしていたため、シェイクも一切乗らなかった)
            if (!hasLockedCameraPosition) { lockedCameraPosition = camera.transform.position; hasLockedCameraPosition = true; }
            basePosition = lockedCameraPosition;
            camera.transform.position = basePosition;
        }
        else
        {
            hasLockedCameraPosition = false;
            var alive = swords.Values.Where(s => s.IsAlive).ToArray();
            if (alive.Length == 0) return;
            // ▼【修正】SpriteRenderer.boundsは見た目のAABBなので、キャラが回転すると
            // (独楽モードのスピンなど)対角線の分だけ見かけ上のサイズが膨らんで、
            // カメラの位置・ズームが毎フレーム細かくぐらついてしまう。BattleCamera.GetSafePositionと
            // 同じく、回転に左右されない重心(currentCenterPosition)だけで画角を決める
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var sword in alive)
            {
                Vector3 pos = sword.currentCenterPosition;
                minX = Mathf.Min(minX, pos.x); maxX = Mathf.Max(maxX, pos.x);
                minY = Mathf.Min(minY, pos.y); maxY = Mathf.Max(maxY, pos.y);
            }
            Bounds bounds = new Bounds(new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, 0f), Vector3.zero);
            bounds.Encapsulate(new Vector3(minX - 4f, minY - 4f, 0f));
            bounds.Encapsulate(new Vector3(maxX + 4f, maxY + 4f, 0f));
            float size = Mathf.Max(8, bounds.extents.y, bounds.extents.x / camera.aspect);
            // Reserve the upper part of the viewport for React's four health panels.
            var position = new Vector3(bounds.center.x, bounds.center.y + size * .16f, -10);
            float t = 1 - Mathf.Exp(-5 * Time.unscaledDeltaTime);
            camera.transform.position = Vector3.Lerp(camera.transform.position, position, t);
            camera.orthographicSize = Mathf.Lerp(camera.orthographicSize, size * 1.2f, t);
            basePosition = camera.transform.position;
        }
        // ▼【新規追加】ローカルのBattleCamera.LateUpdate()末尾と同じシェイク処理。BattleCameraはマルチプレイ中
        // 無効化されているため、代わりにこのマネージャー自身でTriggerShakeの値を消費する
        if (shakeDuration > 0)
        {
            camera.transform.position = basePosition + (Vector3)UnityEngine.Random.insideUnitCircle * shakeMagnitude;
            shakeDuration -= Time.unscaledDeltaTime;
        }
    }

    [UnityEngine.Scripting.Preserve]
    public void FinishMultiplayer(string json)
    {
        var result = JsonUtility.FromJson<MultiplayerResult>(json);
        if (config == null || result == null || result.matchId != config.matchId || phase == "RESULT") return;
        phase = "RESULT"; SwordBattle.matchEnded = true; SwordBattle.isRoundStarted = false;
        // ▼【修正】SwordBattle.Update()は isRoundStarted/matchEnded を見た時点で即returnするため、
        // ちょうど必殺技ボタンが表示中(SP満タン)のままここに来ると、ボタンの表示を消す
        // UpdateSpecialAttackButton()が二度と呼ばれなくなり、結果画面になってもボタンが
        // 表示されたまま残ってしまっていた。Update()を止める前に明示的に隠しておく
        var firstSword = swords.Values.FirstOrDefault();
        var specialButton = firstSword != null ? firstSword.specialAttackButton : null;
        if (specialButton != null) specialButton.gameObject.SetActive(false);
        foreach (var sword in swords.Values) { sword.StopAllCoroutines(); bodies[sword.PlayerId].simulated = false; }
        // ▼【修正】ローカルのDefeatRoutine後半(カメラロック・シェイク・スローモーション・完全停止)と同じ決着演出。
        // カメラロック自体はLateUpdate()がphase=="RESULT"を見て自動的に行う
        TriggerShake(1.5f, 1.2f);
        StartCoroutine(MatchEndCinematic());
    }

    IEnumerator MatchEndCinematic()
    {
        // ▼【修正】ローカルのDefeatRoutineと同じく、値を一度セットするだけだと他の剣の
        // HitStopRoutineなどがこの直後にTime.timeScaleを上書きした場合、中途半端な速度で
        // 固まる恐れがある。2.5秒間、毎フレーム押し戻すことで確実にこの値を保つ
        float holdTimer = 0f;
        while (holdTimer < 2.5f)
        {
            Time.timeScale = 0.15f;
            holdTimer += Time.unscaledDeltaTime;
            yield return null;
        }
        Time.timeScale = 0f;
    }
    [UnityEngine.Scripting.Preserve]
    public void StopMultiplayer(string json)
    {
        var msg = JsonUtility.FromJson<MultiplayerCommand>(json);
        if (Matches(msg)) StopCurrent();
    }
    bool Matches(MultiplayerCommand msg) { return config != null && msg != null && msg.matchId == config.matchId; }
    void Emit(string type, object data) { if (NetworkManager.Instance != null) NetworkManager.Instance.SendData("MP_" + type, JsonUtility.ToJson(data)); }
    void StopCurrent()
    {
        StopAllCoroutines();
        // ▼【修正】FinishMultiplayerを経ずに対戦が中断される場合(フォーフェイト・再接続など)にも、
        // 必殺技ボタンが表示中のまま残ってしまわないよう、剣を破棄する前に明示的に隠しておく
        var firstSword = swords.Values.FirstOrDefault();
        var specialButton = firstSword != null ? firstSword.specialAttackButton : null;
        if (specialButton != null) specialButton.gameObject.SetActive(false);
        foreach (var sword in swords.Values) if (sword != null) { sword.StopAllCoroutines(); sword.gameObject.SetActive(false); }
        if (arena != null) { arena.SetActive(false); Destroy(arena); }
        if (ownsSimulation) { Physics2D.simulationMode = previousSimulationMode; ownsSimulation = false; }
        swords.Clear(); bodies.Clear(); targets.Clear(); sequences.Clear(); inputTimes.Clear();
        hits.Clear(); forfeits.Clear(); invulnerable.Clear(); syncTargets.Clear();
        cloneSyncTargets.Clear();
        activeClones.Clear();
        foreach (var visual in cloneVisuals.Values) if (visual != null) Destroy(visual);
        cloneVisuals.Clear();
        clashSeq.Clear(); criticalHitThisTick.Clear(); critSeq.Clear();
        shakeDuration = 0f; hasLockedCameraPosition = false;
        livesModeActive = false; soloModeActive = false;
        suppressUntil.Clear(); wasSuppressed.Clear();
        judgmentTargets.Clear(); judgmentPending = false; judgmentStrikeAt = 0f;
        bossPlayerId = null; judgmentVisualStart = -999f;
        RestoreJudgmentGravity();
        if (suppressRange != null) { Destroy(suppressRange.gameObject); suppressRange = null; }
        // 1vs3 専用HUDを片付け、隠していた元のHPバーを必ず戻す
        if (soloHudRoot != null) { soloHudRoot.SetActive(false); Destroy(soloHudRoot); soloHudRoot = null; }
        foreach (var bar in hiddenSceneBars) if (bar != null) bar.SetActive(true);
        hiddenSceneBars.Clear(); soloHudStates.Clear(); bossSpFill = null;
        if (slantSprite != null)
        {
            if (slantSprite.texture != null) Destroy(slantSprite.texture);
            Destroy(slantSprite); slantSprite = null;
        }
        if (suppressRangeSprite != null)
        {
            if (suppressRangeSprite.texture != null) Destroy(suppressRangeSprite.texture);
            Destroy(suppressRangeSprite); suppressRangeSprite = null;
        }
        foreach (var label in suppressLabels.Values) if (label != null) Destroy(label.gameObject);
        suppressLabels.Clear();
        ownedSwordsByPlayer = null; spawnPositionByPlayer = null; lifeIndexByPlayer.Clear();
        foreach (var cache in lifeSpriteCache.Values)
            foreach (var sprite in cache)
            {
                if (sprite == null) continue;
                if (sprite.texture != null) Destroy(sprite.texture);
                Destroy(sprite);
            }
        lifeSpriteCache.Clear();
        config = null; rules = null; phase = "IDLE";
        Time.timeScale = 1; SwordBattle.isRoundStarted = false; SwordBattle.matchEnded = false;
        if (countdownText != null) countdownText.gameObject.SetActive(false);
        countdownText = null; countdownUiPhase = "IDLE";
        if (AudioManager.Instance != null) AudioManager.Instance.ResetSoundEffects();
    }
    void OnDestroy() { if (Active) StopCurrent(); }
}
