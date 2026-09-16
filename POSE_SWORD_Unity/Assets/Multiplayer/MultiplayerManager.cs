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
    private readonly Dictionary<string, (GameObject obj, string ownerId, Color color)> activeClones = new Dictionary<string, (GameObject, string, Color)>();
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
    private string phase = "IDLE";
    private int physicsTick, syncTick, receivedTick = -1;
    private float countdownEnd, nextSync, nextTargetUpdate;
    private SimulationMode2D previousSimulationMode;
    private bool ownsSimulation;
    private BattleCamera battleCamera;

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
            scene.StopAllCoroutines();
            scene.autoTestOnStart = false;
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
            if (network.swordStage != null) network.swordStage.SetActive(!SwordController.isKomaMode);
            if (network.komaStage != null) network.komaStage.SetActive(SwordController.isKomaMode);
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas != hudCanvas) canvas.enabled = false;
            foreach (var tutorial in FindObjectsByType<TutorialManager>(FindObjectsSortMode.None)) tutorial.enabled = false;
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
            rules = new MatchRules(config.players.Select(p => p.playerId).ToArray(), config.players.Select(p => p.swordData.hp).ToArray());
            // ▼ 壁も含めて自作していた即席アリーナをやめ、SceneController側の校正済み座標(GetSpawnPositions)を使う
            Vector3[] spawnPositions = scene.GetSpawnPositions(config.players.Length);
            // ▼【修正】刀身の太さは、ローカル(3・4人目の動的生成)と同じくSceneController.generators[0]の値を基準にする。
            // 以前はhostGeneratorという別枠の値を使っており、Inspector設定次第でローカルとサイズがズレていた
            float bladeWidth = scene.generators != null && scene.generators.Length > 0 && scene.generators[0] != null
                ? scene.generators[0].targetBladeWidth : scene.hostGenerator.targetBladeWidth;
            foreach (var player in config.players) CreateSword(player, network.playerSwords[0], bladeWidth, spawnPositions[player.spawnIndex]);
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
        config.players = config.players.OrderBy(p => p.slotIndex).ToArray();
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
        generator.GenerateSwordFromJson(JsonUtility.ToJson(player.swordData));
        if (!generator.LastGenerationSucceeded) throw new InvalidOperationException("Could not generate player sword.");
        controller.ApplyPhysicsMode();
        swords.Add(player.playerId, battle); bodies.Add(player.playerId, rb);
        sequences[player.playerId] = 0; inputTimes[player.playerId] = -100;
        targets[player.playerId] = null;
        obj.SetActive(true);
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
    }

    static Transform FindChildEndingWith(Transform parent, string suffix)
    {
        foreach (Transform child in parent)
            if (child.name.EndsWith(suffix)) return child;
        return null;
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

    void FixedUpdate()
    {
        // ▼【修正】カウントダウン中も(PLAYING同様に)物理演算だけは進める。ダメージ判定・勝敗判定は
        // 引き続きIsPlayingになってからのみ行う（QueueHit側もIsPlayingガード済みなので二重に安全）
        if (!IsHost || !IsSimulating) return;
        UpdateTargets(false);
        foreach (var p in swords) invulnerable[p.Key] = p.Value.isDashing;
        Physics2D.Simulate(Time.fixedDeltaTime);
        if (!IsPlaying) return;
        rules.ResolveStep(++physicsTick, hits, forfeits);
        hits.Clear(); forfeits.Clear();
        foreach (var score in rules.Players)
        {
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
                standings = rules.Players.Select(p => new MultiplayerScore { playerId = p.PlayerId, rank = p.Rank,
                    damageDealt = p.DamageDealt, damageTaken = p.DamageTaken, kills = p.Kills,
                    eliminationTick = p.EliminationTick, eliminationReason = p.EliminationReason }).ToArray() };
            Emit("RESULT", result);
            FinishMultiplayer(JsonUtility.ToJson(result));
        }
    }

    public void QueueHit(SwordBattle attacker, SwordBattle target, int damage, bool isCrit = false)
    {
        if (!IsHost || !IsPlaying || target.MultiplayerOwner != this || !rules.Get(target.PlayerId).Alive) return;
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

    // ▼【新規追加】分身突進・リーフシールドなどの付随体1体をSYNC配信対象として登録し、識別用IDを返す
    public string RegisterClone(GameObject clone, string ownerId, Color color)
    {
        string id = "clone" + (++cloneIdSeq);
        activeClones[id] = (clone, ownerId, color);
        return id;
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
        var candidates = config.players.Select(p => new TargetCandidate(p.playerId, p.slotIndex,
            swords[p.playerId].transform.position.x, swords[p.playerId].transform.position.y, swords[p.playerId].IsAlive)).ToArray();
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
                    critSeq = critSeq.TryGetValue(p.playerId, out var crseq) ? crseq : 0 };
            }).ToArray(),
            // ▼【新規追加】分身突進・リーフシールドなどの付随体の位置をクライアントへ配信する
            clones = activeClones.Where(kv => kv.Value.obj != null).Select(kv => new MultiplayerCloneState {
                id = kv.Key, ownerId = kv.Value.ownerId,
                x = kv.Value.obj.transform.position.x, y = kv.Value.obj.transform.position.y,
                rotation = kv.Value.obj.transform.eulerAngles.z, color = kv.Value.color
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
            sword.ApplyMultiplayerHealth(data.hp, wasCrit);
            if (clashed) sword.PlayClashEffect();
            sword.ApplyMultiplayerVisuals(data.sp, data.isDashing, data.dashType, data.scale);
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
                    obj = CreateCloneVisual(c.ownerId, c.color);
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

    // ▼ 付随体の見た目は、持ち主(ownerId)のBladeスプライトをそのまま複製して作る（imageStrは全員が同じmatch設定から
    // ローカルで生成済みなので、スプライト自体をネットワーク越しに送る必要はない）。色は技ごとに異なるためSYNCで受け取る
    GameObject CreateCloneVisual(string ownerId, Color color)
    {
        if (ownerId == null || !swords.TryGetValue(ownerId, out var ownerBattle) || ownerBattle == null) return null;
        var ownerBlade = ownerBattle.transform.Find("Blade");
        var ownerSr = ownerBlade != null ? ownerBlade.GetComponent<SpriteRenderer>() : null;
        if (ownerSr == null || ownerSr.sprite == null) return null;

        var obj = new GameObject("SwordCloneVisual");
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = ownerSr.sprite;
        sr.color = color;
        sr.sortingLayerID = ownerSr.sortingLayerID;
        sr.sortingOrder = ownerSr.sortingOrder;
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
            Bounds bounds = new Bounds(alive[0].transform.position, Vector3.zero);
            foreach (var sword in alive)
                foreach (var renderer in sword.GetComponentsInChildren<SpriteRenderer>()) bounds.Encapsulate(renderer.bounds);
            float size = Mathf.Max(8, bounds.extents.y + 4, (bounds.extents.x + 4) / camera.aspect);
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
        foreach (var sword in swords.Values) { sword.StopAllCoroutines(); bodies[sword.PlayerId].simulated = false; }
        // ▼【修正】ローカルのDefeatRoutine後半(カメラロック・シェイク・スローモーション・完全停止)と同じ決着演出。
        // カメラロック自体はLateUpdate()がphase=="RESULT"を見て自動的に行う
        TriggerShake(1.5f, 1.2f);
        StartCoroutine(MatchEndCinematic());
    }

    IEnumerator MatchEndCinematic()
    {
        Time.timeScale = 0.15f;
        yield return new WaitForSecondsRealtime(2.5f);
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
        config = null; rules = null; phase = "IDLE";
        Time.timeScale = 1; SwordBattle.isRoundStarted = false; SwordBattle.matchEnded = false;
        if (AudioManager.Instance != null) AudioManager.Instance.ResetSoundEffects();
    }
    void OnDestroy() { if (Active) StopCurrent(); }
}
